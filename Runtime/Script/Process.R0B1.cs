#if R0_B1
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Text;
using MinorShift.Emuera.Next.Vm;
using MinorShift.Emuera.Next.Compiler;
using MinorShift.Emuera.Next.Core;
using MinorShift.Emuera.Runtime.Diagnostics;
using MinorShift.Emuera.Runtime.Script.Statements.Expression;
using MinorShift.Emuera.Runtime.Script.Statements.Function;
using MinorShift.Emuera.Runtime.Utils;

namespace MinorShift.Emuera.GameProc;
internal sealed partial class Process
{
    private sealed record B1Case(long Character,string Form,AExpression LegacyTerm);
    private sealed class B1ScalarFrame : IVmFrameVariables
    {
        internal long Arg;internal string Args="";
        public bool OwnsFrameVariable(string name)=>name.Equals("ARG",StringComparison.OrdinalIgnoreCase)||name.Equals("ARGS",StringComparison.OrdinalIgnoreCase);
        public bool TryReadFrame(string name,string? subkey,ReadOnlySpan<VmSemanticValue> indices,out VmSemanticValue value)
        {
            value=VmSemanticValue.Unavailable;
            if(subkey!=null||indices.Length!=0)return false;
            value=name.Equals("ARG",StringComparison.OrdinalIgnoreCase)?VmSemanticValue.From(Arg):VmSemanticValue.From(Args);return true;
        }
        public bool TryWriteFrame(string name,string? subkey,ReadOnlySpan<VmSemanticValue> indices,VmSemanticValue value)=>throw new InvalidOperationException("B1 read-only frame write forbidden");
    }

    internal object RunB1(string root,string phase)
    {
        var map=new B1HotMap(root);map.Save(root);
        // Name is used only to select the user-requested first specimen, never by the lowerer/executor.
        var selected=map.Top.Single(p=>p.Profile.FunctionName=="CHARA_SKILLCOUNT");var entry=selected.Function;
        if(entry.Coverage[5]!="Compiled"||!bool.Parse(entry.Coverage[6])||!entry.Closed||entry.Flat==null)throw new InvalidOperationException("Requested leaf is not structurally eligible: "+entry.FlatBlocker);
        var flat=entry.Flat;
        var label=labelDic.GetNonEventLabel(entry.Function.Name);
        var expectedPath=Path.GetRelativePath(Program.ErbDir,entry.File.FileIdentity).Replace('\\','/');
        if(label?.Position is not {} position||!string.Equals(position.Filename.Replace('\\','/'),expectedPath,StringComparison.OrdinalIgnoreCase)||position.LineNo!=entry.Function.Span.StartLine)throw new InvalidOperationException("Legacy binding is not exact source identity");
        if(!label.IsMethod||label.MethodType!=typeof(long))throw new InvalidOperationException("Legacy kind mismatch");
        var count=checked((int)vEvaluator.CHARANUM);if(count<=0)throw new InvalidOperationException("Save has no valid characters");
        var cases=new List<B1Case>();
        foreach(var c in Enumerable.Range(0,count))foreach(var form in new[]{"","通常形態","変身形態"})
        {
            var term=idDic.GetFunctionMethod(labelDic,label.LabelName,[SingleLongTerm.FromValue(c),SingleStrTerm.FromValue(form)],userDefinedOnly:true);
            if(term is not SuperUserDefinedMethodTerm)throw new InvalidOperationException("Legacy term not user-defined");
            cases.Add(new(c,form,term));
        }
        var inputs=cases.ToArray();var frame=new FlatMethodFrame(flat.RegisterCount);
        var host=new LegacyVmSemanticHost(this);
        var previousScan=scaningLine;
        try{scaningLine=label.NextLine;host.BuildSemanticContextIndex([flat.Expressions]);host.BindVariableIdentities(flat.Expressions);}finally{scaningLine=previousScan;}
        // B measures the unmodified current recursive semantic executor with the SAME linked method control.
        // Its embedding frame uses correct bare ARG/ARGS slot-0 semantics, not VmMachine's incompatible frame import.
        var recursiveFrame=new B1ScalarFrame();
        var recursive=new VmSemanticExecutor(new LinkedProgram([],[],[]),host){FrameVariables=recursiveFrame};
        long ReadRecursive(B1Case input)
        {
            recursiveFrame.Arg=input.Character;recursiveFrame.Args=input.Form;
            int pc=0,steps=0;
            while((uint)pc<(uint)flat.Control.Length)
            {
                if(++steps>flat.Control.Length+1)throw new InvalidOperationException("Recursive explicit FAIL: StepLimit");
                var op=flat.Control[pc++];
                if(op.Kind==MethodControlKind.Jump){pc=op.Target;continue;}
                if(!recursive.TryEvaluateRuntimeRecord(flat.Expressions,op.Record,out var value)||!value.TryGetInteger(out var integer))throw new InvalidOperationException("Recursive explicit FAIL: "+recursive.LastStatus+"/"+recursive.LastFault);
                if(op.Kind==MethodControlKind.Return)return integer;
                if(integer==0)pc=op.Target;
            }
            throw new InvalidOperationException("Recursive explicit FAIL: missing return");
        }
        Func<B1Case,long>[] engines=[input=>input.LegacyTerm.GetIntValue(exm),ReadRecursive,input=>flat.Execute(frame,input.Character,input.Form)];
        string[] names=["Legacy","RecursiveVmSemanticExecutor","FlatMethodProgram"];
        var initialArg=label.Arg[0].GetIntValue(exm);var initialArgs=label.Arg[1].GetStrValue(exm);
        var initialReturn=state.MethodReturnValue;var initialLine=state.CurrentLine;var initialLines=state.lineCount;
        var initialHash=GetBenchmarkStateHash();
        void RestoreLegacyScratch(){label.Arg[0].SetValue(initialArg,exm);label.Arg[1].SetValue(initialArgs,exm);state.MethodReturnValue=initialReturn;state.lineCount=initialLines;}
        bool ScratchStable()=>label.Arg[0].GetIntValue(exm)==initialArg&&label.Arg[1].GetStrValue(exm)==initialArgs&&ReferenceEquals(state.MethodReturnValue,initialReturn)&&ReferenceEquals(state.CurrentLine,initialLine)&&state.functionCount==0&&methodStack==0;
        var correctness=new List<object>();
        foreach(var input in inputs)
        {
            var a=engines[0](input);RestoreLegacyScratch();var b=engines[1](input);var c=engines[2](input);
            if(a!=b||a!=c||!ScratchStable())throw new InvalidOperationException($"Correctness FAIL at character={input.Character},form={input.Form}");
            correctness.Add(new{input.Character,input.Form,Legacy=a,Recursive=b,Flat=c,Match=true});
        }
        if(initialHash!=GetBenchmarkStateHash())throw new InvalidOperationException("Correctness changed game-save state");
        bool stepFail=false,indexFail=false;
        try{flat.Execute(frame,0,"",0);}catch(InvalidOperationException ex)when(ex.Message.Contains("StepLimit")){stepFail=true;}
        try{flat.Execute(frame,-1,"");}catch(Exception ex)when(ex is CodeEE or ArgumentOutOfRangeException or IndexOutOfRangeException){indexFail=true;}
        if(!stepFail||!indexFail||initialHash!=GetBenchmarkStateHash()||!ScratchStable())throw new InvalidOperationException("Fault/no-fallback self-test failed");
        var generalTests=B1GeneralTests(map.Environment);
        var observedReads=Enumerable.Range(0,count).Select(c=>new {Character=c,Values=flat.Code.Where(op=>op.Op==FlatOp.ReadCharacterInteger).Select(op=>new{Variable=flat.ReadSlots[op.Slot].Name,Index=op.Immediate,Value=flat.ReadSlots[op.Slot].GetIntValue(exm,[c,op.Immediate])}).Distinct().ToArray()}).ToArray();
        B1Proof.WriteJson(Path.Combine(root,"raw",phase+".generality.json"),new{Tests=generalTests,ActualSaveReads=observedReads,BranchCaveat="Actual matrix alone does not cover PT<1 early return or transformed-state true branch. Synthetic scalar branch tests use distinct return sentinels; no real character state is changed."});
        if(initialHash!=GetBenchmarkStateHash()||!ScratchStable())throw new InvalidOperationException("Generality tests changed state");
        B1Proof.WriteJson(Path.Combine(root,"raw",phase+".correctness.json"),new{Cases=correctness,StepLimitExplicitFail=stepFail,InvalidIndexExplicitFail=indexFail,StateUnchanged=true,StateScope="Normal binary-save serialization plus selected ARG/ARGS and invocation stack; RNG/SQL/GLOBAL not serialized, candidate instruction whitelist cannot access/write them",B1Proof.BridgeAttempts});
        B1Proof.WriteJson(Path.Combine(root,"flat-ir.json"),new{Eligibility="Integer Method, bare ARG/ARGS scalar signature, IF/ELSE/ENDIF/SIF-RETURNF, == and integer <, constant strings/integers, bound character integer 1-D array reads; no calls/writes/RNG/UI/save",FlatCode=flat.Code.Select((op,i)=>new{Pc=i,Opcode=op.Op.ToString(),op.Dest,op.A,op.B,op.Slot,op.Immediate}),flat.Strings,ReadSlots=flat.ReadSlots.Select((t,i)=>new{Slot=i,t.Name,Code=t.Code.ToString()}),flat.RegisterCount,Control=flat.Control.Select((op,i)=>new{Pc=i,Kind=op.Kind.ToString(),op.Record,op.Target}),SourceKey=entry.Key,Metadata=entry.Compile!.Function!.RuntimeMetadata,FramePolicy="bare ARG and ARGS are each scalar slot 0; no Legacy stack in B/C; immutable code + per-caller reusable frame"});
        if(!phase.StartsWith("benchmark-",StringComparison.Ordinal))return new{Phase=phase,Profile="VALID_FOR_TARGET_SELECTION",JoinedTop=map.Top.Length,CharacterCount=count,MatrixCases=inputs.Length,Correctness="PASS",BridgeUsed="NO",FlatInstructions=flat.Code.Length,LeafExecution="NOT_MEASURED"};

        const int calls=1_000_000,warmup=200_000,rounds=6;
        var measurements=new List<object>();var elapsedByEngine=new List<double>[] {[],[],[]};
        // Allocate all call sites/cases/frames before warmup: Legacy reuses its real compiled-call representation too.
        for(int engine=0;engine<3;engine++){Batch(engines[engine],inputs,warmup);if(engine==0)RestoreLegacyScratch();}
        int[][] order=[[0,1,2],[1,2,0],[2,0,1],[2,1,0],[1,0,2],[0,2,1]];
        long? expectedChecksum=null;
        for(int round=0;round<rounds;round++)foreach(var engine in order[round])
        {
            if(!ScratchStable()||initialHash!=GetBenchmarkStateHash())throw new InvalidOperationException("State changed before timed batch");
            GC.Collect(2,GCCollectionMode.Forced,true,true);GC.WaitForPendingFinalizers();GC.Collect(2,GCCollectionMode.Forced,true,true);
            var gcBefore=new[]{GC.CollectionCount(0),GC.CollectionCount(1),GC.CollectionCount(2)};
            long allocBefore=GC.GetAllocatedBytesForCurrentThread(),allBefore=GC.GetTotalAllocatedBytes(true);
            long started=Stopwatch.GetTimestamp();var checksum=Batch(engines[engine],inputs,calls);long elapsed=Stopwatch.GetTimestamp()-started;
            long allocated=GC.GetAllocatedBytesForCurrentThread()-allocBefore,allAllocated=GC.GetTotalAllocatedBytes(true)-allBefore;
            var gcAfter=new[]{GC.CollectionCount(0),GC.CollectionCount(1),GC.CollectionCount(2)};
            bool scratchBeforeRestore=ScratchStable();
            if(engine==0)RestoreLegacyScratch();
            bool unchanged=ScratchStable()&&initialHash==GetBenchmarkStateHash();
            if(!unchanged||(engine!=0&&!scratchBeforeRestore))throw new InvalidOperationException("Timed candidate changed state");
            expectedChecksum??=checksum;if(checksum!=expectedChecksum)throw new InvalidOperationException("Timed checksum mismatch");
            var milliseconds=elapsed*1000.0/Stopwatch.Frequency;elapsedByEngine[engine].Add(milliseconds);
            measurements.Add(new{Round=round+1,Engine=names[engine],Calls=calls,ElapsedTicks=elapsed,ElapsedMilliseconds=milliseconds,NanosecondsPerCall=elapsed*1e9/Stopwatch.Frequency/calls,AllocatedBytes=allocated,AllocationPerCall=allocated/(double)calls,ProcessAllocatedBytes=allAllocated,Checksum=checksum,GcCount=gcAfter.Zip(gcBefore,(a,b)=>a-b).ToArray(),StateUnchanged=unchanged,LegacyScratchRestored=engine==0,ScratchUnchangedBeforeRestore=scratchBeforeRestore});
        }
        double Median(List<double> values){var v=values.Order().ToArray();return (v[2]+v[3])/2;}
        var medians=elapsedByEngine.Select(Median).ToArray();var speedup=medians[0]/medians[2];
        // Predeclared R0-B1 leaf rule: >=2x median improvement and every Flat round beats every Legacy round.
        var gate=speedup>=2&&elapsedByEngine[2].Max()<elapsedByEngine[0].Min()?"STRONG_GO":speedup<=1?"STOP":"REVISE";
        var overhead=ClockOverhead();
        var result=new{R0B1Profile="VALID_FOR_TARGET_SELECTION",LeafExecution=gate,ProductionBridgeUsed="NO",WholeProductSuperiority="NOT_YET_CLAIMED",Pid=Environment.ProcessId,Runtime=System.Environment.Version.ToString(),ServerGC=GCSettings.IsServerGC,TieredCompilation=System.Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),SourceKey=entry.Key,CharacterCount=count,MatrixCases=inputs.Length,CallsPerRound=calls,Rounds=rounds,WarmupCallsPerEngine=warmup,Order=order,MedianMilliseconds=names.Zip(medians,(name,ms)=>new{Engine=name,Milliseconds=ms}).ToArray(),FlatVsLegacySpeedup=speedup,GateRule="median speedup >= 2.0 and max Flat elapsed < min Legacy elapsed; set before timing",ClockSelfTest=overhead,Measurements=measurements,ClosedCohort="PENDING_ANALYSIS_AFTER_GATE",CandidateStateUnchanged=true,B1Proof.BridgeAttempts};
        B1Proof.WriteJson(Path.Combine(root,"raw",phase+".timings.json"),result);
        if(gate=="STRONG_GO")map.SaveGraph(root);
        return result;
    }

    private object[] B1GeneralTests(StructuralSemanticEnvironment environment)
    {
        var rows=new List<object>();var compiler=new FunctionCompiler(environment);
        FlatMethodProgram Compile(string body,string signature="ARG, ARGS",string declarations="")
        {
            var text="@R0_GENERAL_METHOD("+signature+")\n#FUNCTION\n"+declarations+body+"\n";
            var bytes=Encoding.UTF8.GetBytes(text);
            var function=new FunctionIndex("R0_GENERAL_METHOD",new SourceSpan(0,bytes.Length,1,text.Count(c=>c=='\n')),SourceIndexFlags.FunctionMetadata|SourceIndexFlags.DeclarationDirective);
            var file=new SourceFileIndex("memory://r0-b1-isolated-generality",bytes.Length,function.Span.EndLine,[function],SourceIndexFlags.None,null);
            var source=new FunctionSource(file,function,bytes);var result=compiler.TryCompileRuntime(source);
            if(result.Status!=CompileStatus.Compiled)throw new NotSupportedException("Compiler:"+result.Detail);
            return FlatMethodProgram.Compile(source,result.Function!,environment);
        }
        void Check(string name,FlatMethodProgram program,long arg,string args,long expected)
        {
            var actual=program.Execute(new(program.RegisterCount),arg,args);
            if(actual!=expected)throw new InvalidOperationException("General flat self-test FAIL: "+name);
            rows.Add(new{Name=name,Expected=expected,Actual=actual,Pass=true});
        }
        var branch=Compile("SIF ARG < 1\nRETURNF 20\nIF ARGS == \"変身形態\"\nRETURNF 101\nELSE\nIF ARGS == \"通常形態\"\nRETURNF 202\nELSE\nIF ARG == 2\nRETURNF 303\nELSE\nRETURNF 404\nENDIF\nENDIF\nENDIF");
        foreach(var t in new[]{(0L,"",20L),(1L,"変身形態",101L),(1L,"通常形態",202L),(2L,"",303L),(1L,"",404L)})
            Check("Distinct branch sentinel",branch,t.Item1,t.Item2,t.Item3);
        var equal=Compile("RETURNF ARGS == \"a\"");
        foreach(var value in new[]{"a","A","a\0","ａ",""}) {
            var legacy=OperatorMethodManager.ReduceBinaryTerm(OperatorCode.Equal,SingleStrTerm.FromValue(value),SingleStrTerm.FromValue("a")).GetIntValue(exm);
            Check("Legacy ordinal string equality",equal,0,value,legacy);
        }
        Check("Parenthesized return/general name",Compile("RETURNF (20)"),0,"",20);
        Check("Implicit zero",Compile("IF ARG\nRETURNF 42\nENDIF"),0,"",0);
        Check("Prelinked true branch",Compile("IF ARG\nRETURNF 42\nENDIF"),1,"",42);
        Check("Maximum integer literal",Compile("RETURNF 9223372036854775807"),0,"",long.MaxValue);
        Check("Unselected invalid read is not evaluated",Compile("SIF ARG == 0\nRETURNF 7\nRETURNF CFLAG:9223372036854775807:0"),0,"",7);
        foreach(var t in new[]{
            ("Private CSV shadow","RETURNF CFLAG:ARG:PTフラグ","#DIM PTフラグ\n"),
            ("Global CSV shadow","RETURNF CFLAG:ARG:TARGET",""),
            ("Quoted CSV not shared with B","RETURNF CFLAG:ARG:\"PTフラグ\"",""),
            ("Arithmetic outside subset","RETURNF ARG + 1",""),
            ("External write","CFLAG:ARG:0 = 1\nRETURNF 0",""),
            ("User call","RETURNF CHARA_SKILLCOUNT(ARG)",""),
            ("Backward control","WHILE ARG\nRETURNF 1\nWEND\nRETURNF 0",""),
            ("RNG","RETURNF RAND:10","")})
        {
            string? rejection=null;
            try{Compile(t.Item2,declarations:t.Item3);}catch(NotSupportedException ex){rejection=ex.Message;}
            if(rejection==null)throw new InvalidOperationException("Eligibility did not reject: "+t.Item1);
            rows.Add(new{Name=t.Item1,Rejected=true,Reason=rejection,Pass=true});
        }
        return rows.ToArray();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long Batch(Func<B1Case,long> engine,B1Case[] inputs,int calls)
    {
        long checksum=0;int index=0;
        for(int i=0;i<calls;i++){checksum=unchecked(checksum*31+engine(inputs[index]));if(++index==inputs.Length)index=0;}
        return checksum;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static object ClockOverhead()
    {
        const int n=1_000_000;long sink=0;var rows=new List<object>();
        for(int round=0;round<5;round++)
        {
            var before=Stopwatch.GetTimestamp();for(int i=0;i<n;i++)sink=unchecked(sink+i);var baseline=Stopwatch.GetTimestamp()-before;
            before=Stopwatch.GetTimestamp();for(int i=0;i<n;i++)sink=unchecked(sink+Stopwatch.GetTimestamp());var one=Stopwatch.GetTimestamp()-before;
            rows.Add(new{Round=round,Calls=n,BaselineNanosecondsPerIteration=baseline*1e9/Stopwatch.Frequency/n,TimestampNanosecondsPerIteration=one*1e9/Stopwatch.Frequency/n,ApproxIncrementalNanoseconds=(one-baseline)*1e9/Stopwatch.Frequency/n});
        }
        return new{Rows=rows,Sink=sink,StopwatchFrequency=Stopwatch.Frequency,TimedBatchTimestampCalls=2,ProfilerStackSyncs=3613869,Caveat="Clock-only approximation; excludes profiler dictionary/stack bookkeeping. No subtraction from benchmark or profile."};
    }
}
#endif
