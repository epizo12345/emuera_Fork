#if R0_B1
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MinorShift.Emuera.Next.Compiler;
using MinorShift.Emuera.Next.Core;
using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Runtime.Config.JSON;
using MinorShift.Emuera.Runtime.Utils;
using RuntimeConfig = MinorShift.Emuera.Runtime.Config.Config;

namespace MinorShift.Emuera.Runtime.Diagnostics;

internal sealed record B1ProfileRow(string FunctionName,string SourcePath,int SourceLine,string Kind,long CallCount,int PartialAtStart,double InclusiveMilliseconds,double ExclusiveMilliseconds,int OpenAtStop);
internal sealed record B1Profile(double ActiveScriptMilliseconds,int Slices,long StackSyncs,string WaitBetweenDoScript,B1ProfileRow[] Rows);
internal sealed class B1Function(SourceFileIndex file,FunctionIndex function,string key,string[] coverage)
{
    internal readonly SourceFileIndex File=file;
    internal readonly FunctionIndex Function=function;
    internal readonly string Key=key;
    internal readonly string[] Coverage=coverage;
    internal bool Analyzed;
    internal FunctionSource? Source;
    internal CompileResult? Compile;
    internal FlatMethodProgram? Flat;
    internal string FlatBlocker="NotAnalyzed";
    internal string[] Callees=[],Builtins=[],HostReads=[],SideEffects=[],FrameWrites=[],DynamicCalls=[],Instructions=[],AmbiguousCalls=[];
    internal bool Closed;
}

internal sealed class B1HotMap
{
    internal readonly B1Profile Profile;
    internal readonly StructuralSemanticEnvironment Environment;
    internal readonly Dictionary<string,B1Function> Functions=new(StringComparer.Ordinal);
    internal readonly (B1ProfileRow Profile,B1Function Function)[] Top;
    private readonly Dictionary<string,B1Function[]> names;
    private readonly FunctionCompiler compiler;
    private static string Norm(string path)=>path.Replace('\\','/').TrimStart('/').ToUpperInvariant();
    internal static string Key(string path,int line,string name)=>$"{Norm(path)}\t{line}\t{name.ToUpperInvariant()}";

    internal B1HotMap(string root)
    {
        Profile=JsonSerializer.Deserialize<B1Profile>(File.ReadAllText(Path.Combine(root,"evidence/profile.json")))!;
        if(Profile.ActiveScriptMilliseconds!=3308.6726 || Profile.Slices!=118 || Profile.StackSyncs!=3613869 || Profile.WaitBetweenDoScript!="EXCLUDED")throw new InvalidOperationException("Profile identity mismatch");
        var first=File.ReadAllBytes(Path.Combine(root,"evidence/compact-1.coverage.tsv"));
        foreach(var n in new[]{2,3})if(!first.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(root,$"evidence/compact-{n}.coverage.tsv"))))throw new InvalidOperationException("R0 coverage runs disagree");
        var coverage=new Dictionary<string,string[]>(StringComparer.Ordinal);
        foreach(var line in Encoding.UTF8.GetString(first).Split('\n').Skip(1))
        {
            if(string.IsNullOrWhiteSpace(line))continue;
            var f=line.TrimEnd('\r').Split('\t');coverage.Add(Key(f[1],int.Parse(f[2]),f[3]),f);
        }
        foreach(var file in RuntimeConfig.GetFiles(Program.ErbDir,"*.ERB").Select(p=>ErbSourceIndexer.IndexFile(p.Value)))
        foreach(var function in file.Functions)
        {
            var key=Key(Path.GetRelativePath(Program.ErbDir,file.FileIdentity),function.Span.StartLine,function.Name);
            if(!coverage.TryGetValue(key,out var c)||long.Parse(c[7])!=function.Span.ByteLength)throw new InvalidOperationException("Source/coverage mismatch: "+key);
            Functions.Add(key,new(file,function,key,c));
        }
        if(Functions.Count!=coverage.Count)throw new InvalidOperationException("Source count mismatch");
        names=Functions.Values.GroupBy(f=>f.Function.Name,StringComparer.OrdinalIgnoreCase).ToDictionary(g=>g.Key,g=>g.ToArray(),StringComparer.OrdinalIgnoreCase);
        var options=new CompilerCompatibilityOptions(RuntimeConfig.IgnoreCase,JSONConfig.Game.UseScopedVariableInstruction,RuntimeConfig.SystemAllowFullSpace,false);
        var rename=RuntimeConfig.UseRenameFile&&ParserMediator.RenameDic!=null?new SemanticRenameResolver(ParserMediator.RenameDic):null;
        var macros=MacroCatalog.FromHeaderSources(RuntimeConfig.GetFiles(Program.ErbDir,"*.ERH").Select(p=>File.ReadAllText(p.Value,RuntimeConfig.Encode)),options,rename);
        Environment=new(options,macros,RuntimeConfig.SystemIgnoreTripleSymbol,rename);
        compiler=new(Environment);
        Top=Profile.Rows.OrderByDescending(r=>r.ExclusiveMilliseconds).Take(100).Select(r=>(r,Functions.TryGetValue(Key(r.SourcePath,r.SourceLine,r.FunctionName),out var f)?f:throw new InvalidOperationException("Exact profile join missing"))).ToArray();
        foreach(var pair in Top)Analyze(pair.Function);
        foreach(var pair in Top)pair.Function.Closed=Closure(pair.Function,new HashSet<string>(),new HashSet<string>());
    }

    internal void Analyze(B1Function entry)
    {
        if(entry.Analyzed)return;entry.Analyzed=true;
        var read=FunctionSourceReader.Read(entry.File,entry.Function);
        if(read.Status!=SourceReadStatus.Read)throw new InvalidOperationException("Source read changed");
        entry.Source=read.Source!.Value;
        var lines=Encoding.UTF8.GetString(entry.Source.Value.Bytes).Replace("\r\n","\n").Split('\n').Skip(1).Select(MaskText).Where(l=>!string.IsNullOrWhiteSpace(l)&&!l.TrimStart().StartsWith('#')).ToArray();
        var calls=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var builtin=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var dynamic=new HashSet<string>();var effects=new HashSet<string>();var frames=new HashSet<string>();var hosts=new HashSet<string>();var commands=new HashSet<string>();var ambiguous=new HashSet<string>();
        var text=string.Join('\n',lines);
        foreach(Match match in Regex.Matches(text,@"([\p{L}_][\p{L}\p{N}_]*)\s*\("))Resolve(match.Groups[1].Value);
        foreach(var line in lines)
        {
            var command=Regex.Match(line.TrimStart(),@"^[\p{L}_][\p{L}\p{N}_]*").Value.ToUpperInvariant();commands.Add(command);
            if(Regex.IsMatch(command,@"^(?:TRY|TRYC)?(?:CALL|JUMP|GOTO).*FORM") || command is "EXISTFUNCTION" or "FUNCREF")dynamic.Add(command);
            if(Regex.IsMatch(command,@"^(?:TRY)?(?:CALL|CALLF|JUMP)$"))
            {
                var target=Regex.Match(line.TrimStart()[command.Length..].TrimStart(),@"^[\p{L}_][\p{L}\p{N}_]*").Value;
                if(target.Length==0)dynamic.Add(command+":UnknownTarget");else Resolve(target);
            }
            if(Regex.IsMatch(command,@"^(PRINT|DRAW|REDRAW|WAIT|TWAIT|AWAIT|INPUT|TINPUT|ONEINPUT|TONEINPUT|FORCEWAIT)"))effects.Add("UI/Input/Wait");
            if(Regex.IsMatch(command,@"^(SAVE|LOAD|DEL|OUTPUTLOG|SQL|QUIT|RESTART|BEGIN|ADDCHARA|ADDSPCHARA|SWAPCHARA)"))effects.Add("ExternalState/Save/IO");
            if(Regex.IsMatch(line,@"\b(RAND|RANDOMIZE|RANDOM)\b"))effects.Add("RNG");
            if(Regex.IsMatch(line,@"(?<![=!<>])=(?!=)|\+\+|--") || command is "VARSET" or "SPLIT" or "ARRAYSHIFT")
            {
                if(command is "ARG" or "ARGS" or "LOCAL" or "LOCALS")frames.Add(command);
                else effects.Add("WriteOrUnknownDestination:"+command);
            }
            foreach(Match variable in Regex.Matches(line,@"([\p{L}_][\p{L}\p{N}_]*)\s*:"))hosts.Add(variable.Groups[1].Value);
        }
        if(Regex.IsMatch(text,@"\bEXISTFUNCTION\s*\("))dynamic.Add("EXISTFUNCTION");
        entry.Callees=calls.Order().ToArray();entry.Builtins=builtin.Order().ToArray();entry.HostReads=hosts.Order().ToArray();entry.SideEffects=effects.Order().ToArray();entry.FrameWrites=frames.Order().ToArray();entry.DynamicCalls=dynamic.Order().ToArray();entry.Instructions=commands.Order().ToArray();entry.AmbiguousCalls=ambiguous.Order().ToArray();
        if((entry.Function.Flags&(SourceIndexFlags.Preprocessor|SourceIndexFlags.Rename|SourceIndexFlags.LineContinuation|SourceIndexFlags.OtherSemanticFallback))!=0){entry.FlatBlocker="HardFlags:"+entry.Function.Flags;return;}
        entry.Compile=compiler.TryCompileRuntime(entry.Source.Value);
        if(entry.Compile.Status!=CompileStatus.Compiled){entry.FlatBlocker="Compiler:"+entry.Compile.Detail;return;}
        try{entry.Flat=FlatMethodProgram.Compile(entry.Source.Value,entry.Compile.Function!,Environment);entry.FlatBlocker="None";}
        catch(Exception ex)when(ex is NotSupportedException or FormatException or CodeEE){entry.FlatBlocker=ex.Message;}
        void Resolve(string name)
        {
            if(name.ToUpperInvariant() is "IF" or "SIF" or "ELSEIF" or "RETURNF" or "RETURN" or "SELECTCASE" or "WHILE" or "REPEAT")return;
            if(names.TryGetValue(name,out var targets))
            {
                if(targets.Length==1)calls.Add(targets[0].Key);
                else ambiguous.Add(name+":"+targets.Length);
            }
            else builtin.Add(name.ToUpperInvariant());
        }
    }

    private bool Closure(B1Function f,HashSet<string> visited,HashSet<string> open)
    {
        Analyze(f);if(f.Flat is null||f.SideEffects.Length!=0||f.DynamicCalls.Length!=0||f.AmbiguousCalls.Length!=0||f.Builtins.Length!=0)return false;
        if(!open.Add(f.Key))return false;
        if(!visited.Add(f.Key)){open.Remove(f.Key);return true;}
        foreach(var key in f.Callees)if(!Closure(Functions[key],visited,open))return false;
        open.Remove(f.Key);return true;
    }

    internal void Save(string root)
    {
        var rows=Top.Select((p,i)=>new {Rank=i+1,Key=p.Function.Key,p.Profile.FunctionName,p.Profile.SourcePath,StartLine=p.Profile.SourceLine,p.Profile.CallCount,p.Profile.ExclusiveMilliseconds,p.Profile.InclusiveMilliseconds,p.Profile.PartialAtStart,p.Profile.OpenAtStop,CompileStatus=p.Function.Coverage[5],MetadataAvailable=bool.Parse(p.Function.Coverage[6]),HardFlags=p.Function.Function.Flags.ToString(),SpanBytes=p.Function.Function.Span.ByteLength,StaticUserDefinedCallees=p.Function.Callees,AmbiguousCallees=p.Function.AmbiguousCalls,DynamicCalls=p.Function.DynamicCalls,HostReads=p.Function.HostReads,BuiltinRequirements=p.Function.Builtins,ExternalSideEffectsOrUnknown=p.Function.SideEffects,FrameWrites=p.Function.FrameWrites,FlatEligible=p.Function.Flat!=null,p.Function.FlatBlocker,ClosedCohort=p.Function.Closed,Analysis="Conservative lexical dependency screen + compiler/flat whitelist; dynamic/ambiguous references fail closed"}).ToArray();
        B1Proof.WriteJson(Path.Combine(root,"joined-hotmap.json"),rows);
        using var output=new StreamWriter(Path.Combine(root,"joined-hotmap.tsv"),false,new UTF8Encoding(false));
        output.WriteLine("Rank\tSourcePath\tStartLine\tFunctionName\tCallCount\tExclusiveMs\tInclusiveMs\tCompileStatus\tMetadataAvailable\tHardFlags\tSpanBytes\tStaticCallees\tDynamicCalls\tHostReads\tBuiltinRequirements\tExternalEffects\tFrameWrites\tFlatBlocker\tClosedCohort");
        foreach(var row in rows)output.WriteLine(string.Join('\t',row.Rank,row.SourcePath,row.StartLine,row.FunctionName,row.CallCount,row.ExclusiveMilliseconds,row.InclusiveMilliseconds,row.CompileStatus,row.MetadataAvailable,row.HardFlags,row.SpanBytes,JsonSerializer.Serialize(row.StaticUserDefinedCallees),JsonSerializer.Serialize(row.DynamicCalls),JsonSerializer.Serialize(row.HostReads),JsonSerializer.Serialize(row.BuiltinRequirements),JsonSerializer.Serialize(row.ExternalSideEffectsOrUnknown),JsonSerializer.Serialize(row.FrameWrites),row.FlatBlocker.Replace('\t',' ').Replace('\n',' '),row.ClosedCohort));
    }

    internal void SaveGraph(string root)
    {
        var matched=Profile.Rows.Where(r=>Functions.ContainsKey(Key(r.SourcePath,r.SourceLine,r.FunctionName))).ToArray();
        var unmatched=Profile.Rows.Except(matched).ToArray();
        var pending=new Queue<B1Function>(matched.Select(r=>Functions[Key(r.SourcePath,r.SourceLine,r.FunctionName)]));
        var visited=new HashSet<string>();const int limit=10000;
        while(pending.TryDequeue(out var f)) {
            if(!visited.Add(f.Key))continue;
            if(visited.Count>limit)throw new InvalidOperationException("Cohort analysis safety limit reached");
            Analyze(f);
            foreach(var key in f.Callees)if(!visited.Contains(key))pending.Enqueue(Functions[key]);
        }
        var nodes=visited.Select(key=>Functions[key]).OrderBy(f=>f.Key).Select(f=>new{
            f.Key,FunctionName=f.Function.Name,SourcePath=Path.GetRelativePath(Program.ErbDir,f.File.FileIdentity),StartLine=f.Function.Span.StartLine,
            CompileStatus=f.Coverage[5],MetadataAvailable=bool.Parse(f.Coverage[6]),HardFlags=f.Function.Flags.ToString(),f.Callees,f.AmbiguousCalls,f.DynamicCalls,f.Builtins,f.HostReads,
            f.Instructions,f.SideEffects,f.FrameWrites,PrivateNames=f.Compile?.Function?.RuntimeMetadata?.PrivateVariables.Select(v=>v.Name).ToArray()??[],
            IsMethod=f.Compile?.Function?.RuntimeMetadata?.ReturnType!=null,f.FlatBlocker,FlatEligible=f.Flat!=null,
            ClosedCohort=Closure(f,new HashSet<string>(),new HashSet<string>())
        }).ToArray();
        B1Proof.WriteJson(Path.Combine(root,"callgraph.json"),new{RootProfileCount=Profile.Rows.Length,MatchedProfileCount=matched.Length,UnmatchedProfileRows=unmatched,StaticReachableCount=visited.Count,DynamicTargets="UNRESOLVED and BLOCKED",Nodes=nodes});
    }

    // Strip strings/comments, retaining character positions. This is a conservative screen, not a new ERB parser.
    private static string MaskText(string line)
    {
        var chars=line.ToCharArray();bool quote=false;
        for(int i=0;i<chars.Length;i++)
        {
            if(quote && chars[i]=='\\'){chars[i]=' ';if(i+1<chars.Length)chars[++i]=' ';continue;}
            if(chars[i]=='"'){quote=!quote;chars[i]=' ';continue;}
            if(!quote && chars[i]==';'){Array.Fill(chars,' ',i,chars.Length-i);break;}
            if(quote)chars[i]=' ';
        }
        return new(chars);
    }
}
#endif
