#if R0_B2
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MinorShift.Emuera.GameData.Variable;
using MinorShift.Emuera.GameData.Function;
using MinorShift.Emuera.Runtime.Script.Statements.Variable;
using MinorShift.Emuera.Next.Compiler;
using MinorShift.Emuera.Next.Core;
using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Runtime.Script.Statements.Function;
using MinorShift.Emuera.Runtime.Utils;

namespace MinorShift.Emuera.Runtime.Diagnostics;

internal sealed class FlatQueryCatalog
{
    private readonly Dictionary<string,B1Function> functions;
    private readonly StructuralSemanticEnvironment environment;
    private readonly Action<B1Function> analyze;
    private readonly Dictionary<string,B1Function[]> names;
    private readonly Dictionary<string,VariableToken> variables;
    private readonly Dictionary<string,FunctionMethod> native=FunctionMethodCreator.GetMethodList();
    private readonly Dictionary<string,int> ids=new();
    private readonly HashSet<string> open=new();
    internal readonly HashSet<string> ExternalFrameAliases;
    internal readonly Dictionary<string,string> Blockers=new();
    internal readonly List<QueryProgram?> Programs=[];
    internal FlatQueryCatalog(B1HotMap map) : this(map.Functions, map.Environment, map.Analyze,
        Runtime.Config.Config.GetFiles(Program.ErbDir,"*.ERB").Concat(Runtime.Config.Config.GetFiles(Program.ErbDir,"*.ERH"))
            .SelectMany(p=>FindExternalFrameAliases(System.IO.File.ReadAllText(p.Value,Runtime.Config.Config.Encode)))
            .ToHashSet(map.Environment.Compatibility.NameComparer)) { }
    internal FlatQueryCatalog(Dictionary<string,B1Function> functions, StructuralSemanticEnvironment environment,
        Action<B1Function> analyze, HashSet<string> externalFrameAliases)
    {
        this.functions=functions;this.environment=environment;this.analyze=analyze;
        names=functions.Values.GroupBy(f=>f.Function.Name,environment.Compatibility.NameComparer).ToDictionary(g=>g.Key,g=>g.ToArray(),environment.Compatibility.NameComparer);
        variables=GlobalStatic.IdentifierDictionary.GetNextRuntimeVariableTokens().ToDictionary(p=>p.Key,p=>p.Value,environment.Compatibility.NameComparer);
        ExternalFrameAliases=externalFrameAliases;
    }
    internal static string[] FindExternalFrameAliases(string source)=>Regex.Matches(source,@"\b(?:ARG|ARGS|LOCAL|LOCALS)\s*@\s*([^\s:;,()\[\]+*/=<>!&|]+)",RegexOptions.IgnoreCase).Select(m=>m.Groups[1].Value).ToArray();
    internal int Link(string key)
    {
        if(ids.TryGetValue(key,out var known))return known;
        if(Blockers.TryGetValue(key,out var blocker))throw new NotSupportedException(blocker);
        if(!open.Add(key))throw new NotSupportedException("Static recursion requires separate proof");
        try {
            var entry=functions[key];
            if(ExternalFrameAliases.Contains(entry.Function.Name))throw new NotSupportedException("Externally addressed persistent frame: "+entry.Function.Name);
            analyze(entry);
            if(entry.Compile?.Status!=CompileStatus.Compiled)throw new NotSupportedException("Compiler/hard flag: "+(entry.Compile?.Detail??entry.FlatBlocker));
            var program=new Builder(this,entry).Compile();
            program.RequiredFrames=1+program.Calls.Select(c=>Programs[c.Program]!.RequiredFrames).DefaultIfEmpty(0).Max();
            if(program.RequiredFrames>32)throw new NotSupportedException("Static call depth exceeds admitted 32-frame limit");
            int id=Programs.Count;Programs.Add(program);ids.Add(key,id);return id;
        }
        catch(Exception ex)when(ex is NotSupportedException or CodeEE or FormatException){Blockers[key]=ex.Message;throw;}
        finally{open.Remove(key);}
    }
    internal QueryProgram[] Snapshot()=>Programs.Select(p=>p!).ToArray();
    internal int Synthetic(string name,string body)
    {
        var bytes=Encoding.UTF8.GetBytes(body);var function=new FunctionIndex(name,new SourceSpan(0,bytes.Length,1,body.Count(c=>c=='\n')+1),SourceIndexFlags.FunctionMetadata|SourceIndexFlags.DeclarationDirective);
        var file=new SourceFileIndex("memory://b2-tests",bytes.Length,function.Span.EndLine,[function],SourceIndexFlags.None,null);
        string key="SYNTHETIC\t1\t"+name;
        var entry=new B1Function(file,function,key,[]){Analyzed=true,Source=new FunctionSource(file,function,bytes)};
        entry.Compile=new FunctionCompiler(environment).TryCompileRuntime(entry.Source.Value);
        if(entry.Compile.Status==CompileStatus.Compiled){
            try{entry.Flat=FlatMethodProgram.Compile(entry.Source.Value,entry.Compile.Function!,environment);}
            catch(Exception ex)when(ex is NotSupportedException or CodeEE or FormatException){}
        }
        functions.Add(key,entry);names.Add(name,[entry]);return Link(key);
    }
    private int ResolveCall(string name)
    {
        if(!names.TryGetValue(name,out var matches)||matches.Length!=1)throw new NotSupportedException("Static target missing/ambiguous: "+name);
        return Link(matches[0].Key);
    }
    private sealed class Builder
    {
        private readonly FlatQueryCatalog owner;
        private readonly B1Function entry;
        private readonly StructuralSemanticEnvironment environment;
        private readonly FunctionRuntimeMetadata metadata;
        private readonly Dictionary<string,(int Cell,int Length,bool String)> cells;
        private readonly List<bool> types=[];
        private readonly List<QueryInstruction> code=[];
        private readonly List<string> text=[];
        private readonly List<QueryHostRead> reads=[];
        private readonly List<QueryStaticCall> calls=[];
        private readonly List<QueryBuiltin> builtins=[];
        private readonly List<QueryParameter> parameters=[];
        private int cellCount;
        internal Builder(FlatQueryCatalog owner,B1Function entry)
        {
            this.owner=owner;this.entry=entry;environment=owner.environment;metadata=entry.Compile!.Function!.RuntimeMetadata!;
            if(metadata?.ReturnType==null)throw new NotSupportedException("Not a typed method");
            cells=new(environment.Compatibility.NameComparer);
        }
        private int Register(bool str=false){types.Add(str);return types.Count-1;}
        private int Integer(long value){var r=Register();code.Add(new(QueryOp.Integer,r,Immediate:value));return r;}
        private int String(string value){var r=Register(true);text.Add(value);code.Add(new(QueryOp.String,r,text.Count-1));return r;}
        private void Family(string name,int length,bool str)
        {
            if(length<1||length>64||cells.ContainsKey(name))throw new NotSupportedException("Frame shape/name conflict: "+name);
            int start=types.Count;for(int i=0;i<length;i++)Register(str);cells.Add(name,(start,length,str));
        }
        internal QueryProgram Compile()
        {
            var source=Encoding.UTF8.GetString(entry.Source!.Value.Bytes);
            var header=source.Split('\n')[0].Trim();
            var scan=LegacyIdentifierScanner.ReadFirstIdentifier(header[1..],environment.Compatibility);
            var suffix=header[(scan.StopPosition+1)..].Trim();
            var paramText=suffix.StartsWith('(')&&suffix.EndsWith(')')?suffix[1..^1]:suffix.StartsWith(',')?suffix[1..]:suffix;
            var parts=paramText.Length==0?new List<string>():SemanticLexicalTokenStream.SplitTopLevel(paramText,',',environment.Compatibility);
            if(parts.Count!=metadata.Parameters.Length||parts.Count>32)throw new NotSupportedException("Header parameter form");
            var shape=parts.Select(p=>Regex.Match(p.Trim(),@"^(ARG|ARGS)(?::([0-9]+))?(?:\s*=\s*.*)?$",RegexOptions.IgnoreCase)).ToArray();
            if(shape.Any(p=>!p.Success))throw new NotSupportedException("Only ARG/ARGS parameter families");
            var slots=shape.Select(p=>p.Groups[2].Success?int.Parse(p.Groups[2].Value):0).ToArray();
            Family("ARG",Math.Max(1,shape.Select((p,i)=>p.Groups[1].Value.Equals("ARG",StringComparison.OrdinalIgnoreCase)?slots[i]+1:0).DefaultIfEmpty().Max()),false);
            Family("ARGS",Math.Max(1,shape.Select((p,i)=>p.Groups[1].Value.Equals("ARGS",StringComparison.OrdinalIgnoreCase)?slots[i]+1:0).DefaultIfEmpty().Max()),true);
            if(metadata.LocalSize>0)Family("LOCAL",metadata.LocalSize,false);
            if(metadata.LocalsSize>0)Family("LOCALS",metadata.LocalsSize,true);
            foreach(var v in metadata.PrivateVariables){
                if(v.Dimensions.Length!=1)throw new NotSupportedException("Private frame dimension");
                Family(v.Name,v.Dimensions[0],v.Type==RuntimeMetadataValueType.String);
            }
            if(Regex.IsMatch(source,@"(?m)^\s*#DIMS?\b[^\r\n]*="))throw new NotSupportedException("Private initializer needs semantic proof");
            for(int i=0;i<shape.Length;i++){
                var family=cells[shape[i].Groups[1].Value.ToUpperInvariant()];var p=metadata.Parameters[i];
                if(p.DefaultString?.Contains('\\')==true)throw new NotSupportedException("Escaped default needs proof");
                parameters.Add(new(family.Cell+slots[i],family.String,family.String?QueryValue.S(p.DefaultString):QueryValue.I(p.DefaultInteger),p.HasDefault));
            }
            if(parameters.Select(p=>p.Cell).Distinct().Count()!=parameters.Count)throw new NotSupportedException("Aliased parameter cells");
            cellCount=types.Count;
            var ifs=new Stack<(int Pending,List<int> Ends)>();
            var loops=new Stack<(int Counter,int Step,int Test)>();
            int pendingSif=-1;
            foreach(var instruction in entry.Compile!.Function!.Instructions){
                var op=instruction.Opcode;
                var operand=Encoding.UTF8.GetString(entry.Source.Value.Bytes,instruction.OperandOffset,instruction.OperandLength);
                int previousSif=pendingSif;pendingSif=-1;
                if(previousSif>=0&&op is not (PrototypeOpcode.SET or PrototypeOpcode.RETURNF))throw new NotSupportedException("SIF next statement");
                switch(op){
                    case PrototypeOpcode.IF: {
                        var value=Expression(operand);RequireInteger(value);int branch=code.Count;code.Add(new(QueryOp.False,-1,value));ifs.Push((branch,[]));break;
                    }
                    case PrototypeOpcode.ELSEIF:case PrototypeOpcode.ELSE: {
                        if(!ifs.TryPop(out var g)||g.Pending<0)throw new NotSupportedException("IF nesting");
                        g.Ends.Add(code.Count);code.Add(new(QueryOp.Jump,-1));Patch(g.Pending,code.Count);
                        int branch=-1;
                        if(op==PrototypeOpcode.ELSEIF){var value=Expression(operand);RequireInteger(value);branch=code.Count;code.Add(new(QueryOp.False,-1,value));}
                        ifs.Push((branch,g.Ends));break;
                    }
                    case PrototypeOpcode.ENDIF: {
                        if(!ifs.TryPop(out var g))throw new NotSupportedException("ENDIF nesting");
                        if(g.Pending>=0)Patch(g.Pending,code.Count);foreach(var end in g.Ends)Patch(end,code.Count);break;
                    }
                    case PrototypeOpcode.SIF: {
                        var value=Expression(operand);RequireInteger(value);pendingSif=code.Count;code.Add(new(QueryOp.False,-1,value));break;
                    }
                    case PrototypeOpcode.RETURNF: {
                        var value=Expression(operand);
                        if(types[value]!=(metadata.ReturnType==RuntimeMetadataValueType.String))throw new NotSupportedException("Return type");
                        code.Add(new(QueryOp.Return,A:value));break;
                    }
                    case PrototypeOpcode.SET:Assignment(operand);break;
                    case PrototypeOpcode.FOR: {
                        var payload=SemanticIrCompiler.CompileCountedLoop(operand,environment,0,false);var node=payload.Nodes[payload.Records[0].RootNodeIndex];
                        int cell=Cell(payload,node.A);if(types[cell])throw new NotSupportedException("String FOR counter");
                        var start=Emit(payload,node.B);RequireInteger(start);code.Add(new(QueryOp.StoreCell,cell,start));
                        var end=Emit(payload,node.C);var step=Emit(payload,node.D);RequireInteger(end);RequireInteger(step);
                        int test=code.Count;code.Add(new(QueryOp.ForTest,-1,cell,end,step));loops.Push((cell,step,test));break;
                    }
                    case PrototypeOpcode.NEXT: {
                        if(!loops.TryPop(out var loop))throw new NotSupportedException("NEXT nesting");
                        code.Add(new(QueryOp.ForNext,A:loop.Counter,B:loop.Step));code.Add(new(QueryOp.Jump,loop.Test));Patch(loop.Test,code.Count);break;
                    }
                    default:throw new NotSupportedException("Unsupported statement: "+op);
                }
                if(previousSif>=0)Patch(previousSif,code.Count);
            }
            if(ifs.Count!=0||loops.Count!=0||pendingSif>=0)throw new NotSupportedException("Unbalanced control");
            int fallback=metadata.ReturnType==RuntimeMetadataValueType.String?String(""):Integer(0);code.Add(new(QueryOp.Return,A:fallback));
            CheckDefiniteScratch();
            // Reuse B1's instruction loop only AFTER the complete B2 eligibility proof.
            return Finish(entry.Flat);
        }
        private QueryProgram Finish(FlatMethodProgram? fast)=>new(){Key=entry.Key,Code=code.ToArray(),Text=text.ToArray(),RegisterStrings=types.ToArray(),Parameters=parameters.ToArray(),Reads=reads.ToArray(),Calls=calls.ToArray(),Builtins=builtins.ToArray(),CellCount=cellCount,ReturnsString=metadata.ReturnType==RuntimeMetadataValueType.String,FastLeaf=fast};
        private void Patch(int at,int target)=>code[at]=code[at] with{Dest=target};
        private void RequireInteger(int register){if(types[register])throw new NotSupportedException("Integer operand required");}
        private int Expression(string value){var p=SemanticIrCompiler.CompileExpression(value,environment);return Emit(p,p.Records[0].RootNodeIndex);}
        private int Cell(SemanticPayload p,int index)
        {
            var node=p.Nodes[index];
            if(node.Kind is not (SemanticNodeKind.Symbol or SemanticNodeKind.Variable))throw new NotSupportedException("Frame lvalue shape");
            var name=p.ReadSymbol(p.Symbols[node.A]);
            if(!cells.TryGetValue(name,out var family))throw new NotSupportedException("External write/frame unavailable: "+name);
            int slot=0;
            if(node.Kind==SemanticNodeKind.Variable){
                if(node.C!=1)throw new NotSupportedException("Frame index arity");
                var key=p.Nodes[p.Edges[node.B].To];
                if(key.Kind!=SemanticNodeKind.IntegerLiteral||!int.TryParse(p.ReadSymbol(p.Symbols[key.A]),out slot))throw new NotSupportedException("Dynamic frame index not needed by closure");
            }
            if((uint)slot>=(uint)family.Length)throw new NotSupportedException("Frame bounds");
            return family.Cell+slot;
        }
        private void Assignment(string operand)
        {
            var match=Regex.Match(operand,@"^\s*([^\s:=+\-']+(?::[0-9]+)?)\s*('=|\+=|-=|=)\s*(.*)$");
            if(!match.Success)throw new NotSupportedException("Assignment form");
            var lhs=SemanticIrCompiler.CompileExpression(match.Groups[1].Value,environment);int cell=Cell(lhs,lhs.Records[0].RootNodeIndex);
            var operation=match.Groups[2].Value;var body=match.Groups[3].Value;int value;
            if(types[cell]&&operation=="="){
                if(body.Contains(';'))throw new NotSupportedException("Formatted assignment comment requires proof");
                var format=SemanticIrCompiler.CompileFormat(body,environment);value=Emit(format,format.Records[0].RootNodeIndex);
            }else value=Expression(body.Length==0?(types[cell]?"\"\"":"0"):body);
            if(types[cell]!=types[value])throw new NotSupportedException("Assignment type");
            if(operation is "+=" or "-="){
                if(types[cell])throw new NotSupportedException("String compound assignment");
                int old=Register();code.Add(new(QueryOp.LoadCell,old,cell));int result=Register();
                code.Add(new(QueryOp.Binary,result,old,value,(int)(operation=="+="?SemanticOperator.Plus:SemanticOperator.Minus)));value=result;
            }
            code.Add(new(QueryOp.StoreCell,cell,value));
        }
        private int Emit(SemanticPayload p,int index)
        {
            var n=p.Nodes[index];string Symbol(int s)=>p.ReadSymbol(p.Symbols[s]);
            switch(n.Kind){
                case SemanticNodeKind.IntegerLiteral:
                    if(!long.TryParse(Symbol(n.A),out var literal))throw new NotSupportedException("Non-decimal literal");return Integer(literal);
                case SemanticNodeKind.StringLiteral:return String(Symbol(n.A));
                case SemanticNodeKind.Symbol:case SemanticNodeKind.Variable:
                    if(cells.ContainsKey(Symbol(n.A))){int cell=Cell(p,index),result=Register(types[cell]);code.Add(new(QueryOp.LoadCell,result,cell));return result;}
                    var reference=HostReference(p,index);var r=Register(!reference.Token.IsInteger);reads.Add(reference);code.Add(new(QueryOp.ReadHost,r,reads.Count-1));return r;
                case SemanticNodeKind.Call:return Call(p,n);
                case SemanticNodeKind.Unary:
                    if(n.Operator is not (SemanticOperator.Not or SemanticOperator.Plus or SemanticOperator.Minus))throw new NotSupportedException("Unary "+n.Operator);
                    int value=Emit(p,n.A);RequireInteger(value);int unary=Register();code.Add(new(QueryOp.Unary,unary,value,(int)n.Operator));return unary;
                case SemanticNodeKind.Binary: {
                    if(n.Operator is not (SemanticOperator.Plus or SemanticOperator.Minus or SemanticOperator.Equal or SemanticOperator.NotEqual or SemanticOperator.Less or SemanticOperator.Greater or SemanticOperator.LessEqual or SemanticOperator.GreaterEqual or SemanticOperator.LogicalAnd or SemanticOperator.LogicalOr))throw new NotSupportedException("Binary "+n.Operator);
                    var left=Emit(p,n.A);
                    if(n.Operator is SemanticOperator.LogicalAnd or SemanticOperator.LogicalOr){
                        RequireInteger(left);int boolean=Integer(n.Operator==SemanticOperator.LogicalAnd?0:1);int branch=code.Count;
                        code.Add(new(n.Operator==SemanticOperator.LogicalAnd?QueryOp.False:QueryOp.True,-1,left));
                        var right=Emit(p,n.B);RequireInteger(right);int zero=Integer(0);code.Add(new(QueryOp.Binary,boolean,right,zero,(int)SemanticOperator.NotEqual));Patch(branch,code.Count);return boolean;
                    }
                    var rhs=Emit(p,n.B);
                    if(types[left]!=types[rhs]||types[left]&&n.Operator is not (SemanticOperator.Plus or SemanticOperator.Equal or SemanticOperator.NotEqual))throw new NotSupportedException("Binary types");
                    int result=Register(types[left]&&n.Operator==SemanticOperator.Plus);code.Add(new(QueryOp.Binary,result,left,rhs,(int)n.Operator));return result;
                }
                case SemanticNodeKind.Format: {
                    if(n.B>=0)throw new NotSupportedException("Format width");
                    int inner=Emit(p,n.A);
                    if((SemanticFormatKind)n.D==SemanticFormatKind.Percent){if(!types[inner])throw new NotSupportedException("Percent format type");return inner;}
                    RequireInteger(inner);int formatted=Register(true);code.Add(new(QueryOp.FormatInteger,formatted,inner));return formatted;
                }
                case SemanticNodeKind.FormattedSequence: {
                    int sequence=String("");
                    for(int i=0;i<n.B;i++){var part=Emit(p,p.Edges[n.A+i].To);if(!types[part])throw new NotSupportedException("Format segment type");var joined=Register(true);code.Add(new(QueryOp.Binary,joined,sequence,part,(int)SemanticOperator.Plus));sequence=joined;}
                    return sequence;
                }
                default:throw new NotSupportedException("Expression node: "+n.Kind);
            }
        }
        private QueryHostRead HostReference(SemanticPayload p,int index,bool ignoreCharacter=false)
        {
            var n=p.Nodes[index];if(n.Kind is not (SemanticNodeKind.Symbol or SemanticNodeKind.Variable))throw new NotSupportedException("Host reference shape");
            var name=p.ReadSymbol(p.Symbols[n.A]);
            if(cells.ContainsKey(name)||!owner.variables.TryGetValue(name,out var token)||token.IsReference||token.IsPrivate||token.IsForbid)throw new NotSupportedException("Unbound host reference: "+name);
            bool calculated=(token.Code&VariableCode.__CALC__)!=0;
            if(!(!calculated&&(token.IsCharacterData&&token.Dimension<=1||token.IsConst)||token.Code==VariableCode.TARGET||token.Code==VariableCode.CHARANUM))throw new NotSupportedException("Host read purity not proved: "+name);
            int arity=n.Kind==SemanticNodeKind.Variable?n.C:0;
            int wanted=token.Dimension+(token.IsCharacterData?1:0);if(wanted>2||arity>wanted)throw new NotSupportedException("Host dimensions");
            var indices=new List<int>();
            if(ignoreCharacter&&token.IsCharacterData)indices.Add(Integer(0));
            if(!ignoreCharacter&&token.IsCharacterData&&arity<wanted){
                if(Runtime.Config.Config.SystemNoTarget)throw new NotSupportedException("No TARGET omission");
                var target=owner.variables["TARGET"];reads.Add(new(target,[]));int targetValue=Register();code.Add(new(QueryOp.ReadHost,targetValue,reads.Count-1));indices.Add(targetValue);
            }
            for(int i=ignoreCharacter&&arity==wanted?1:0;i<arity;i++){
                int arg=p.Edges[n.B+i].To;var key=p.Nodes[arg];int value;
                string? word=key.Kind==SemanticNodeKind.Symbol?p.ReadSymbol(p.Symbols[key.A]):null;
                if(word!=null&&!cells.ContainsKey(word)&&!owner.variables.ContainsKey(word))value=Integer(GlobalStatic.ConstantData.KeywordToInteger(token.Code,word,indices.Count));
                else value=Emit(p,arg);
                if(types[value]){
                    reads.Add(new(token,[]));int converted=Register();code.Add(new(QueryOp.Keyword,converted,reads.Count-1,value,indices.Count));value=converted;
                }
                indices.Add(value);
            }
            while(indices.Count<wanted)indices.Add(Integer(0));
            return new(token,indices.ToArray());
        }
        private int Call(SemanticPayload p,SemanticNode node)
        {
            string name=p.ReadSymbol(p.Symbols[node.A]);
            if(owner.names.ContainsKey(name)){
                if(owner.native.ContainsKey(name))throw new NotSupportedException("User/builtin identity collision");
                int id=owner.ResolveCall(name);var target=owner.Programs[id]!;
                if(node.C>target.Parameters.Length||node.C>32)throw new NotSupportedException("Static call arity");
                var args=new int[node.C];
                for(int i=0;i<node.C;i++){
                    int child=p.Edges[node.B+i].To;
                    if(p.Nodes[child].Kind==SemanticNodeKind.MissingArgument){var d=target.Parameters[i].Default;args[i]=target.Parameters[i].String?String(d.Text??""):Integer(d.Integer);}
                    else args[i]=Emit(p,child);
                    if(types[args[i]]!=target.Parameters[i].String)throw new NotSupportedException("Static call argument type");
                }
                calls.Add(new(id,args));int result=Register(target.ReturnsString);code.Add(new(QueryOp.Call,result,calls.Count-1));return result;
            }
            var idName=name.ToUpperInvariant();
            var builtin=idName switch{"GETNUM"=>PureQueryId.GetNum,"MATCH"=>PureQueryId.Match,"FINDCHARA"=>PureQueryId.FindChara,"STRCOUNT"=>PureQueryId.StrCount,_=>throw new NotSupportedException("Unapproved builtin: "+name)};
            if(!owner.native.TryGetValue(name,out var method))throw new NotSupportedException("Builtin case/binding");
            if(node.C<2||node.C>(builtin is PureQueryId.Match or PureQueryId.FindChara?4:2))throw new NotSupportedException("Builtin arity");
            // Native wrappers define evaluation order; never eagerly evaluate in textual argument order.
            int Child(int i)=>p.Edges[node.B+i].To;
            QueryHostRead? reference=null;
            var registers=new int[builtin is PureQueryId.Match or PureQueryId.FindChara?4:2];Array.Fill(registers,-1);
            var strings=new bool[registers.Length];
            if(builtin==PureQueryId.GetNum){
                var refNode=p.Nodes[Child(0)];
                if(refNode.Kind is not (SemanticNodeKind.Symbol or SemanticNodeKind.Variable)||cells.ContainsKey(p.ReadSymbol(p.Symbols[refNode.A]))||!owner.variables.TryGetValue(p.ReadSymbol(p.Symbols[refNode.A]),out var refToken))throw new NotSupportedException("GETNUM reference identity");
                reference=new(refToken,[]);registers[1]=Emit(p,Child(1));strings[1]=types[registers[1]];
                if(!strings[1])throw new NotSupportedException("GETNUM key type");
            }
            else if(builtin==PureQueryId.StrCount){
                registers[1]=Emit(p,Child(1));strings[1]=types[registers[1]];
                if(!strings[1])throw new NotSupportedException("STRCOUNT pattern type");
            }
            else{
                if(builtin==PureQueryId.FindChara)reference=HostReference(p,Child(0),ignoreCharacter:true);
                registers[2]=node.C>2&&p.Nodes[Child(2)].Kind!=SemanticNodeKind.MissingArgument?Emit(p,Child(2)):Integer(0);RequireInteger(registers[2]);
                if(node.C>3&&p.Nodes[Child(3)].Kind!=SemanticNodeKind.MissingArgument)registers[3]=Emit(p,Child(3));
                else if(builtin==PureQueryId.FindChara){reads.Add(new(owner.variables["CHARANUM"],[]));registers[3]=Register();code.Add(new(QueryOp.ReadHost,registers[3],reads.Count-1));}
                else{
                    var refNode=p.Nodes[Child(0)];
                    if(refNode.Kind is not (SemanticNodeKind.Symbol or SemanticNodeKind.Variable)||!owner.variables.TryGetValue(p.ReadSymbol(p.Symbols[refNode.A]),out var refToken))throw new NotSupportedException("MATCH array identity");
                    registers[3]=Integer(refToken.GetLength());
                }
                RequireInteger(registers[3]);
                if(builtin==PureQueryId.Match)reference=HostReference(p,Child(0));
                if(!reference!.Token.IsInteger||builtin==PureQueryId.Match&&reference.Token.Dimension!=1||builtin==PureQueryId.FindChara&&!reference.Token.IsCharacterData)throw new NotSupportedException("Builtin supported reference subset");
            }
            int binding=builtins.Count;builtins.Add(new(builtin,registers,strings,reference?.Token,reference?.Indices??[]));
            if(builtin!=PureQueryId.GetNum)code.Add(new(QueryOp.PrepareBuiltin,A:binding));
            if(builtin==PureQueryId.StrCount){registers[0]=Emit(p,Child(0));strings[0]=types[registers[0]];if(!strings[0])throw new NotSupportedException("STRCOUNT input type");}
            else if(builtin!=PureQueryId.GetNum){registers[1]=Emit(p,Child(1));RequireInteger(registers[1]);}
            int output=Register();code.Add(new(QueryOp.Builtin,output,binding));return output;
        }
        private void CheckDefiniteScratch()
        {
            // ponytail: literal frame cells only; dynamic local arrays need a separate definite-assignment proof.
            var incoming=new HashSet<int>?[code.Count];incoming[0]=parameters.Select(p=>p.Cell).ToHashSet();var queue=new Queue<int>();queue.Enqueue(0);
            while(queue.TryDequeue(out var pc)){
                var assigned=new HashSet<int>(incoming[pc]!);var instruction=code[pc];
                if(instruction.Op==QueryOp.StoreCell)assigned.Add(instruction.Dest);
                IEnumerable<int> next=instruction.Op switch{QueryOp.Return=>[],QueryOp.Jump=>[instruction.Dest],QueryOp.False or QueryOp.True or QueryOp.ForTest=>[pc+1,instruction.Dest],_=>[pc+1]};
                foreach(int target in next){
                    if((uint)target>=(uint)code.Count)throw new NotSupportedException("Control target");
                    if(incoming[target]==null){incoming[target]=new(assigned);queue.Enqueue(target);}
                    else{var reduced=new HashSet<int>(incoming[target]!);reduced.IntersectWith(assigned);if(!reduced.SetEquals(incoming[target]!)){incoming[target]=reduced;queue.Enqueue(target);}}
                }
            }
            for(int pc=0;pc<code.Count;pc++){
                if(incoming[pc]==null)continue;var op=code[pc];
                if(op.Op is QueryOp.LoadCell or QueryOp.ForTest or QueryOp.ForNext&&!incoming[pc]!.Contains(op.A))throw new NotSupportedException("Persistent scratch read before definite write: cell "+op.A);
            }
        }
    }
}
#endif
