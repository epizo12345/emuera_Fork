#if R0_F4D5
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MinorShift.Emuera.GameData.Function;
using MinorShift.Emuera.GameData.Variable;
using MinorShift.Emuera.Runtime.Diagnostics;
using MinorShift.Emuera.Runtime.Script;
using MinorShift.Emuera.Runtime.Script.Statements;
using MinorShift.Emuera.Runtime.Script.Statements.Variable;
using RuntimeConfig = MinorShift.Emuera.Runtime.Config.Config;

namespace MinorShift.Emuera.GameProc;

internal sealed partial class Process
{
    internal void R0F4D5BeforeLegacyInstruction(InstructionLine line) => r0f1?.ObserveF4D5LegacyInstruction(this, line, true);
    internal void R0F4D5AfterLegacyInstruction(InstructionLine line) => r0f1?.ObserveF4D5LegacyInstruction(this, line, false);
    internal void R0F4D5BeforeLegacyFallthrough(LogicalLine line) => r0f1?.ObserveF4D5LegacyFallthrough(this, line, true);
    internal void R0F4D5AfterLegacyFallthrough(LogicalLine line) => r0f1?.ObserveF4D5LegacyFallthrough(this, line, false);
    internal void R0F4D5ObserveLegacyDynamicCall(InstructionLine line, string target, bool found) => r0f1?.ObserveF4D5LegacyDynamic(this, line, target, found);
    internal void R0F4D5BeforeLegacyScalarWrite(InstructionLine line, long value) => r0f1?.ObserveF4D5LegacyWrite(this, line, value, true);
    internal void R0F4D5AfterLegacyScalarWrite(InstructionLine line) => r0f1?.ObserveF4D5LegacyWrite(this, line, 0, false);
    internal void R0F4D5AfterLegacyArgumentsBound(CalledFunction call) => r0f1?.ObserveF4D5LegacyEntry(call);

    private sealed partial class R0F1Context
    {
        private const string F4D5SetEquip = "SET_EQUIP_VAR";
        private const string F4D5EquipmentPath = "RPG/アイテム関連/装備品/EQUIPMENT_FUNC.ERB";
        private const int F4D5DepthLimit = 60;

        private sealed record F4D5Constants(long Min, long Max, int Count, string VarSha256, int MinLine, int MaxLine, int CountLine);
        private sealed record F4D5FamilyRow(int Iteration, string Name, string Classification, string Readiness, string? SourcePath, int? Line);
        private sealed record F4D5ClosureRow(int Root, long Cell, string[] Path, string Terminal, bool Ready);
        private sealed record F4D5ResolverRow(int Sequence, int Iteration, string Name, string Resolution, string Class,
            bool Invoked, long? ReturnedValue, bool Write, string? NestedName, string? NestedResolution,
            string ResultBeforeSha256, string ResultAfterSha256);
        private sealed record F4D5WriteRow(int Sequence, int Iteration, string Name, string Class, long Value);
        private sealed record F4D5Point(string Name, string ProgramCounter, long Lcount, long[] EquipPart,
            string EquipPartSha256, long[] Result, string ResultSha256, string[] Results, string ResultsSha256,
            string GetEquipNumArgs, long HelperArg, string Frame, string EventCursor, string SystemState,
            bool PendingBegin, string RngSha256, long RngCalls, long ClockCalls);

        private sealed class F4D5Pending
        {
            internal int Iteration;
            internal string Name = "";
            internal string Classification = "";
            internal string ResultBefore = "";
            internal string? NestedName;
            internal string? NestedResolution;
            internal long Value;
        }

        internal bool F4D5LegacyResumeEnabled;
        internal object? F4D5Evidence { get; private set; }
        private bool f4d5LegacyActive;
        private VariableToken? f4d5LegacyLcount;
        private F4D3BoundArray? f4d5EquipPart;
        private F4D5Constants? f4d5Constants;
        private F4D5Pending? f4d5Pending;
        private long f4d5Lcount;
        private int f4d5Entries;
        private int f4d5TrailingReturns;
        private int f4d5SetTurnEndEntries;
        private int f4d5SetTurnEndMaterialized;
        private int f4d5ScriptWrites;
        private bool f4d5LegacyFallthroughPending;
        private object? f4d5P23;
        private object? f4d5P26;
        private object? f4d5P27;
        private readonly List<F4D5ResolverRow> f4d5ResolverRows = [];
        private readonly List<F4D5WriteRow> f4d5WriteRows = [];
        private readonly HashSet<string> f4d5MaterializedA = new(StringComparer.Ordinal);
        private readonly HashSet<string> f4d5MaterializedB = new(StringComparer.Ordinal);
        private readonly HashSet<string> f4d5MaterializedC = new(StringComparer.Ordinal);

        internal void ExecuteF4D5(Process process)
        {
            if (StoppedBefore != "SYSTEM.ERB:1022" || SetEquipVarMaterialized != 0
                || compactFrames.Count != 1 || compactFrames[0].Handle.Name != "SET_GAMEPLAY_START"
                || compactFrames[0].Pc != 1022)
                throw new InvalidOperationException("R0-F4D5 requires fresh actual PC1022");

            var preflightStart = process.GetBenchmarkStateHash();
            f4d5Constants = BindF4D5Constants();
            ValidateF4D5Parent();
            f4d4ClassC = AdmitF4D4ClassC(Path.Combine(DataRoot, "..", "evidence", "family-target-inventory.json"));
            f4d5EquipPart = BindF4D3Array(process, "EQUIP_PART", f4d5Constants.Count);
            var baseCells = Enumerable.Range(0, 60).Select(i => f4d2Saved.Read(process, i)).ToArray();
            var closure = f4d2ClassB.Values.OrderBy(x => x.Argument).Select(x => CheckF4D5Closure(x, baseCells)).ToArray();
            var family = BuildF4D5Family();
            var ready = family.All(x => x.Readiness is "Ready" or "KnownMissing") && closure.All(x => x.Ready);
            var source = F4D3SourceGeneration();
            var config = F4D3ConfigIdentity();
            var schema = F4D3SchemaIdentity(f4d2Saved.Token, f4d5EquipPart.Token);
            var save = FileHash(Path.Combine(RuntimeConfig.SavDir, "save219.sav"));
            var preflightEnd = process.GetBenchmarkStateHash();
            if (!ready || preflightStart != preflightEnd)
                throw new InvalidOperationException(ready ? "R0-F4D5 preflight changed script state" : "R0-F4D5 whole family blocked");

            var initialEquip = ReadF4D5Equip(process);
            var rngBefore = process.vEvaluator.GetR0C2RngHash();
            var rngCallsBefore = process.vEvaluator.GetR0F4D3RandomCallCount();
            var clockBefore = DifferentialDeterminism.ObservationCount;
            f4d5P23 = CaptureF4D5Point(process, "P23", "SYSTEM.ERB:1022:before");

            if (Candidate) RunCandidateF4D5(process);
            else RunLegacyF4D5(process);

            var finalEquip = ReadF4D5Equip(process);
            var known = family.Count(x => x.Classification != "KnownMissing");
            var missing = family.Length - known;
            var a = f4d5ResolverRows.Count(x => x.Class == "A" && x.Invoked);
            var b = f4d5ResolverRows.Count(x => x.Class == "B" && x.Invoked);
            var c = f4d5ResolverRows.Count(x => x.Class == "C" && x.Invoked);
            var outerMissing = f4d5ResolverRows.Where(x => x.Class == "Missing").ToArray();
            var classB = f4d5ResolverRows.Where(x => x.Class == "B").ToArray();
            var guardTotal = Candidate ? R0E1AProof.Counters.Sum() : 0;
            var finalResult = (long[])process.vEvaluator.RESULT_ARRAY.Clone();
            var finalResults = (string[])process.vEvaluator.RESULTS_ARRAY.Clone();
            var pass = f4d5Entries == 1 && f4d5ResolverRows.Count == f4d5Constants.Count
                && f4d5WriteRows.Count == known && f4d5ScriptWrites == known
                && outerMissing.All(x => !x.Write && !x.Invoked)
                && classB.All(x => x.Write) && classB.Where(x=>x.NestedResolution=="KnownMissing").All(x=>x.ReturnedValue==0)
                && f4d5TrailingReturns == 0 && f4d5SetTurnEndEntries == 0 && f4d5SetTurnEndMaterialized == 0
                && compactFrames.Count == 1 && compactFrames[0].Handle.Name == "SET_GAMEPLAY_START"
                && compactFrames[0].Pc == 1023 && StoppedBefore == "SYSTEM.ERB:1023"
                && process.vEvaluator.GetR0C2RngHash() == rngBefore
                && process.vEvaluator.GetR0F4D3RandomCallCount() == rngCallsBefore
                && DifferentialDeterminism.ObservationCount == clockBefore
                && (!Candidate || guardTotal == 0);

            F4D5Evidence = new
            {
                Schema="emuera-r0f4d5-actual-set-equip-var-v1", Mode=Candidate?"GraphFreeCandidate":"LegacyControl",
                Scope="ACTUAL_SET_EQUIP_VAR_RETRY", GateResult=pass?"PASS":"FAIL",
                FreshPreflight=new { Whole4500FamilyReady=ready, SourceGenerationSha256=source, ConfigSha256=config,
                    ErhSchemaSha256=schema, Save219Sha256=save, OwnerGeneration, ResetGeneration=1,
                    Constants=f4d5Constants, BaseEquipment=baseCells, BaseEquipmentSha256=HashLongs(baseCells),
                    Family=family, Closure=closure, StateStable=preflightStart==preflightEnd },
                P23=f4d5P23,
                P24=new { ClassA=f4d5ResolverRows.FirstOrDefault(x=>x.Class=="A"), ClassB=f4d5ResolverRows.FirstOrDefault(x=>x.Class=="B"), ClassC=f4d5ResolverRows.FirstOrDefault(x=>x.Class=="C") },
                P25=f4d5ResolverRows.FirstOrDefault(x=>x.Class=="Missing"), P26=f4d5P26, P27=f4d5P27,
                GeneratedTargets=f4d5ResolverRows.Count, KnownTargets=known, MissingTargets=missing,
                ClassAExecutions=a, ClassBExecutions=b, ClassCExecutions=c,
                EquipPartWrites=f4d5WriteRows.Count, InitialEquipPart=initialEquip, InitialEquipPartSha256=HashLongs(initialEquip),
                FinalEquipPart=finalEquip, FinalEquipPartSha256=HashLongs(finalEquip),
                ResolverRows=f4d5ResolverRows, ResolverSha256=Hash(string.Join('\n', f4d5ResolverRows.Select(ResolverText))),
                WriteRows=f4d5WriteRows, WriteSha256=Hash(string.Join('\n', f4d5WriteRows.Select(x=>$"{x.Sequence}:{x.Iteration}:{x.Name}:{x.Class}:{x.Value}"))),
                Result=finalResult, ResultSha256=HashLongs(finalResult), Results=finalResults, ResultsSha256=HashStrings(finalResults),
                PersistentBanks=new { SetEquipVarLcount=ReadF4D5Lcount(process), GetEquipNumArgs=Candidate?EnsureF4D1Bank(GetEquipNumName).Args[0]:ReadLegacyPersistentArgs(process),
                    HelperArg=ReadPersistentArg(process), WrapperArgumentAndLocalBanks="NONE_BY_ADMITTED_SCHEMA" },
                ClassBInnerMissingWritesZero=classB.Where(x=>x.NestedResolution=="KnownMissing").All(x=>x.Write&&x.ReturnedValue==0),
                ClassCTrailingReturnExecutions=f4d5TrailingReturns,
                ExternalEffects=new { RNG=process.vEvaluator.GetR0F4D3RandomCallCount()-rngCallsBefore,
                    Clock=DifferentialDeterminism.ObservationCount-clockBefore, FileIO=0, Display=0, Input=0 },
                Materialization=Candidate ? new { ClassAPhysical=f4d5MaterializedA.Count, ClassBWrappers=f4d5MaterializedB.Count,
                    Helper=f4d5MaterializedB.Count>0?1:0, ClassC=f4d5MaterializedC.Count, GetEquipNumPrograms=1,
                    SharedClassATemplates=f4d5MaterializedA.Count>0?1:0, SharedClassBTemplates=f4d5MaterializedB.Count>0?1:0,
                    SharedClassCTemplates=f4d5MaterializedC.Count>0?1:0, MissingBodies=0, LargeDuplicatePrograms=0 }
                    : new { ClassAPhysical=0, ClassBWrappers=0, Helper=0, ClassC=0, GetEquipNumPrograms=0,
                        SharedClassATemplates=0, SharedClassBTemplates=0, SharedClassCTemplates=0, MissingBodies=0, LargeDuplicatePrograms=0 },
                MemoryEstimate=new { Kind="descriptor/program census, not process benchmark",
                    EstimatedIncrementalBytes=Candidate?family.Length*48L+known*40L+32768L:0L, TensOfMiBAdded=false },
                ActualSetEquipVarExecuted=f4d5Entries==1, SetEquipVarCompleted=StoppedBefore=="SYSTEM.ERB:1023",
                SetGameplayStartResumed=compactFrames.Count==1&&compactFrames[0].Handle.Name=="SET_GAMEPLAY_START",
                SetTurnEndEvVarMaterialized=f4d5SetTurnEndMaterialized, EventLoadCompleted=false,
                GraphFreeGameResumed="NOT_YET_PROVEN", LegacyErbGraphAvoided=Candidate,
                LegacyRetryAfterCompact=0, ProductionBridgeUsed="NO", GuardTotal=guardTotal,
                ManualRecaptureRequired="NO", WholeProductSuperiority="NOT_YET_CLAIMED"
            };
        }

        private F4D5Constants BindF4D5Constants()
        {
            var path=Path.Combine(DataRoot,"ERB","VAR.ERH");
            var lines=File.ReadAllLines(path,RuntimeConfig.Encode);
            (long value,int line) Read(string name)
            {
                var matches=lines.Select((text,index)=>(text:text.Trim(),line:index+1))
                    .Where(x=>Regex.IsMatch(x.text,$"^#DIM\\s+CONST\\s+{name}\\s*=\\s*[0-9]+$",RegexOptions.CultureInvariant)).ToArray();
                if(matches.Length!=1||!long.TryParse(matches[0].text[(matches[0].text.IndexOf('=')+1)..].Trim(),NumberStyles.None,CultureInfo.InvariantCulture,out var value))
                    throw new InvalidOperationException("R0-F4D5 constant bind: "+name);
                return(value,matches[0].line);
            }
            var min=Read("MIN_EQUIPNUM"); var max=Read("MAX_EQUIPNUM");
            var count=lines.Select((text,index)=>(text:text.Trim(),line:index+1))
                .Where(x=>x.text=="#DIM CONST EQUIPNUM = MAX_EQUIPNUM - MIN_EQUIPNUM").ToArray();
            if(count.Length!=1||max.value-min.value is <=0 or >int.MaxValue) throw new InvalidOperationException("R0-F4D5 EQUIPNUM bind");
            return new(min.value,max.value,checked((int)(max.value-min.value)),FileHash(path)!,min.line,max.line,count[0].line);
        }

        private void ValidateF4D5Parent()
        {
            if(!functions.TryGetValue(F4D5SetEquip,out var matches)||matches.Length!=1) throw new InvalidOperationException("R0-F4D5 parent identity");
            var d=matches[0];
            var expected=new[]{"#DIM LCOUNT","FOR LCOUNT, 0, EQUIPNUM","TRYCCALLFORM 装備箇所_{LCOUNT + MIN_EQUIPNUM}","EQUIP_PART:LCOUNT = RESULT","CATCH","ENDCATCH","NEXT"};
            if(d.IsEvent||d.RelativePath!=F4D5EquipmentPath||d.Line!=1002||!Executable(d).SequenceEqual(expected))
                throw new InvalidOperationException("R0-F4D5 parent source mismatch");
        }

        private F4D5FamilyRow[] BuildF4D5Family()
        {
            var rows=new F4D5FamilyRow[f4d5Constants!.Count];
            for(var i=0;i<rows.Length;i++)
            {
                var name=F4D2TargetName(i+f4d5Constants.Min);
                var classification=ClassifyF4D5(name);
                R0F1Definition? d=null;
                if(functions.TryGetValue(name,out var matches)&&matches.Length==1)d=matches[0];
                rows[i]=new(i,name,classification,classification=="KnownMissing"?"KnownMissing":classification is "A" or "B" or "C"?"Ready":"Blocked",d?.RelativePath,d?.Line);
            }
            return rows;
        }

        private string ClassifyF4D5(string name)
        {
            if(f4d1ClassA.ContainsKey(name))return "A";
            if(f4d2ClassB.ContainsKey(name))return "B";
            if(f4d4ClassC.ContainsKey(name))return "C";
            return Resolve(name,false) switch
            {
                R0F1Resolution.KnownMissing=>"KnownMissing", R0F1Resolution.WrongKind=>"WrongKind",
                R0F1Resolution.Unknown=>"Unresolved", _=>"Blocked"
            };
        }

        private F4D5ClosureRow CheckF4D5Closure(ClassBDescriptor root,long[] cells)
        {
            var current=root; var active=new HashSet<ClosureState>(); var path=new List<string>();
            for(var depth=0;depth<F4D5DepthLimit;depth++)
            {
                var state=new ClosureState(current.Name,current.Argument);
                if(!active.Add(state))return new(root.Argument,cells[root.Argument],path.ToArray(),"Cycle",false);
                path.Add($"{state.PhysicalDefinition}(ARG={state.BoundFormal})");
                var target=F4D2TargetName(cells[current.Argument]); var c=ClassifyF4D5(target);
                if(c=="B"){current=f4d2ClassB[target];continue;}
                return new(root.Argument,cells[root.Argument],path.Append(target+":"+c).ToArray(),c,c is "A" or "C" or "KnownMissing");
            }
            return new(root.Argument,cells[root.Argument],path.ToArray(),"DepthExceeded",false);
        }

        private void InitializeF4D5Candidate()
        {
            var id=10_000;
            foreach(var d in f4d1ClassA.Values.OrderBy(x=>x.Name,StringComparer.Ordinal)) f4d1WrapperHandles.TryAdd(d.Name,F4D5Handle(id++,d.Name));
            f4d2HelperHandle=F4D5Handle(20_000,F4D2HelperName);
            foreach(var d in f4d2ClassB.Values.OrderBy(x=>x.Argument))f4d2WrapperHandles.TryAdd(d.Name,F4D5Handle(20_001+d.Argument,d.Name));
            foreach(var d in f4d4ClassC.Values.OrderBy(x=>x.Spec.Name,StringComparer.Ordinal))f4d1WrapperHandles.TryAdd(d.Spec.Name,F4D5Handle(30_000+f4d1WrapperHandles.Count,d.Spec.Name));
            _=EnsureF4D1Bank(GetEquipNumName); _=EnsureF4D1Bank(F4D2HelperName);
        }

        private void RunCandidateF4D5(Process process)
        {
            InitializeF4D5Candidate();
            var methods=new F4D1Runtime(this,process);
            var setHandle=F4D5Handle(40_000,F4D5SetEquip);
            Enter(new(setHandle,1023,"SYSTEM.ERB:1022"),1003,false);
            SetEquipVarMaterialized=1; f4d5Entries=1; f4d5Lcount=0;
            for(var iteration=0;f4d5Lcount<f4d5Constants!.Count;iteration++,f4d5Lcount++)
            {
                var name=F4D2TargetName(f4d5Lcount+f4d5Constants.Min);
                var before=HashLongs(process.vEvaluator.RESULT_ARRAY); var classification=ClassifyF4D5(name);
                if(classification=="KnownMissing")
                {
                    f4d5ResolverRows.Add(new(f4d5ResolverRows.Count+1,iteration,name,"KnownMissing","Missing",false,null,false,null,null,before,HashLongs(process.vEvaluator.RESULT_ARRAY)));
                    continue;
                }
                var invocation=ExecuteF4D5Known(process,methods,name,0,new HashSet<string>(StringComparer.Ordinal));
                var value=invocation.Value;
                f4d5EquipPart!.Token.SetValue(value,[f4d5Lcount]); f4d5ScriptWrites++;
                f4d5WriteRows.Add(new(f4d5WriteRows.Count+1,iteration,name,classification,value));
                f4d5ResolverRows.Add(new(f4d5ResolverRows.Count+1,iteration,name,"Known",classification,true,value,true,invocation.NestedName,invocation.NestedResolution,before,HashLongs(process.vEvaluator.RESULT_ARRAY)));
            }
            compactFrames[^1].Pc=1009;
            f4d5P26=CaptureF4D5Point(process,"P26","EQUIPMENT_FUNC.ERB:1009:after");
            process.vEvaluator.RESULT=0;
            Return(process,F4D5SetEquip,false);
            StoppedBefore="SYSTEM.ERB:1023";
            f4d5P27=CaptureF4D5Point(process,"P27","SYSTEM.ERB:1023:before");
        }

        private (long Value,string? NestedName,string? NestedResolution) ExecuteF4D5Known(Process process,F4D1Runtime methods,string name,int depth,HashSet<string> active)
        {
            if(depth>=F4D5DepthLimit||!active.Add(name))throw new InvalidOperationException("R0-F4D5 late closure violation: "+name);
            try
            {
                if(f4d1ClassA.TryGetValue(name,out var a))
                {
                    f4d5MaterializedA.Add(name); Enter(new(f4d1WrapperHandles[name],1006,"SET_EQUIP_VAR"),(ushort)(a.Line+1),false);
                    var value=methods.InvokeBound(methods.Bind(methods.PositiveRequest(a)),()=>a.Argument,1_000_000).Value;
                    process.vEvaluator.SetResultX([value]); Return(process,name,true); return(value,null,null);
                }
                if(f4d4ClassC.TryGetValue(name,out var c))
                {
                    f4d5MaterializedC.Add(name); Enter(new(f4d1WrapperHandles[name],1006,"SET_EQUIP_VAR"),(ushort)c.Spec.FirstReturnLine,false);
                    var value=methods.InvokeMethodOnly(GetEquipNumName,c.Argument,1_000_000).Value;
                    process.vEvaluator.SetResultX([value]); Return(process,name,true); return(value,null,null);
                }
                if(!f4d2ClassB.TryGetValue(name,out var b))throw new InvalidOperationException("R0-F4D5 dynamic target not executable: "+name);
                f4d5MaterializedB.Add(name); Enter(new(f4d2WrapperHandles[name],1006,"SET_EQUIP_VAR"),(ushort)(b.Line+1),false);
                var bank=EnsureF4D1Bank(F4D2HelperName); bank.Arg[0]=b.Argument;
                Enter(new(f4d2HelperHandle,1,b.RelativePath+":"+b.Line),2872,false);
                process.vEvaluator.SetResultX([0]);
                var nested=F4D2TargetName(f4d2Saved.Read(process,bank.Arg[0])); var resolution=ClassifyF4D5(nested);
                if(resolution!="KnownMissing")
                {
                    if(resolution is not ("A" or "B" or "C"))throw new InvalidOperationException("R0-F4D5 late inner resolution: "+resolution);
                    _=ExecuteF4D5Known(process,methods,nested,depth+1,active);
                }
                var result=process.vEvaluator.RESULT; process.vEvaluator.SetResultX([result]); Return(process,F4D2HelperName,true);
                process.vEvaluator.SetResultX([result]); Return(process,name,true); return(result,nested,resolution);
            }
            finally{active.Remove(name);}
        }

        private void RunLegacyF4D5(Process process)
        {
            F4D5LegacyResumeEnabled=true; f4d5LegacyActive=true;
            try{process.runScriptProc();throw new InvalidOperationException("R0-F4D5 Legacy crossed stop boundary");}
            catch(R0F1PlannedCheckpointException){}
            finally{f4d5LegacyActive=false;F4D5LegacyResumeEnabled=false;}
        }

        internal void ObserveF4D5LegacyInstruction(Process process,InstructionLine line,bool before)
        {
            if(!f4d5LegacyActive)return;
            if(IsF4D5Line(line,"SYSTEM.ERB",1022))
            {
                if(before){f4d5Entries++;SetEquipVarMaterialized=1;}
                else
                {
                    var token=process.state.CurrentCalled.TopLabel.GetPrivateVariable("LCOUNT");
                    if(token is null||!token.IsInteger||!token.IsPrivate)throw new InvalidOperationException("R0-F4D5 Legacy LCOUNT bind");
                    f4d5LegacyLcount=token;
                    Enter(new(F4D5Handle(40_000,F4D5SetEquip),1023,"SYSTEM.ERB:1022"),1003,false);
                }
                return;
            }
            if(!before&&IsF4D5Line(line,"EQUIPMENT_FUNC.ERB",1009)&&ReadF4D5Lcount(process)==f4d5Constants!.Count&&f4d5P26 is null)
            {
                compactFrames[^1].Pc=1009;
                f4d5P26=CaptureF4D5Point(process,"P26","EQUIPMENT_FUNC.ERB:1009:after");
            }
            if(before&&IsF4D5Line(line,"SYSTEM.ERB",1023))
            {
                if(compactFrames.Count==2)Return(process,F4D5SetEquip,false);
                StoppedBefore="SYSTEM.ERB:1023";
                f4d5P27=CaptureF4D5Point(process,"P27","SYSTEM.ERB:1023:before");
                throw new R0F1PlannedCheckpointException();
            }
            if(before&&f4d4ClassC.Values.Any(x=>line.ParentLabelLine?.LabelName==x.Spec.Name&&line.Position?.LineNo==x.Spec.TrailingReturnLine))f4d5TrailingReturns++;
        }

        internal void ObserveF4D5LegacyDynamic(Process process,InstructionLine line,string target,bool found)
        {
            if(!f4d5LegacyActive)return;
            if(IsF4D5Line(line,"EQUIPMENT_FUNC.ERB",1005))
            {
                var iteration=checked((int)ReadF4D5Lcount(process)); var c=ClassifyF4D5(target);
                var expectedFound=c is "A" or "B" or "C";
                if(found!=expectedFound||target!=F4D2TargetName(iteration+f4d5Constants!.Min))throw new InvalidOperationException("R0-F4D5 Legacy outer resolver mismatch");
                var before=HashLongs(process.vEvaluator.RESULT_ARRAY);
                if(!found)f4d5ResolverRows.Add(new(f4d5ResolverRows.Count+1,iteration,target,"KnownMissing","Missing",false,null,false,null,null,before,HashLongs(process.vEvaluator.RESULT_ARRAY)));
                else f4d5Pending=new(){Iteration=iteration,Name=target,Classification=c,ResultBefore=before};
            }
            else if(IsF4D5Line(line,"117_ドワーフの鍛冶屋.ERB",2873)&&f4d5Pending is not null)
            {
                f4d5Pending.NestedName=target; f4d5Pending.NestedResolution=ClassifyF4D5(target)=="KnownMissing"?"KnownMissing":"Known";
            }
        }

        internal void ObserveF4D5LegacyWrite(Process process,InstructionLine line,long value,bool before)
        {
            if(!f4d5LegacyActive||!IsF4D5Line(line,"EQUIPMENT_FUNC.ERB",1006))return;
            if(before)
            {
                if(f4d5Pending is null)throw new InvalidOperationException("R0-F4D5 Legacy write without Known resolve");
                f4d5Pending.Value=value;
            }
            else
            {
                var pending=f4d5Pending??throw new InvalidOperationException("R0-F4D5 Legacy write completion");
                if(f4d5EquipPart!.Token.GetIntValue(process.exm,[pending.Iteration])!=pending.Value)throw new InvalidOperationException("R0-F4D5 Legacy cell write mismatch");
                f4d5ScriptWrites++; f4d5WriteRows.Add(new(f4d5WriteRows.Count+1,pending.Iteration,pending.Name,pending.Classification,pending.Value));
                f4d5ResolverRows.Add(new(f4d5ResolverRows.Count+1,pending.Iteration,pending.Name,"Known",pending.Classification,true,pending.Value,true,pending.NestedName,pending.NestedResolution,pending.ResultBefore,HashLongs(process.vEvaluator.RESULT_ARRAY)));
                f4d5Pending=null;
            }
        }

        internal void ObserveF4D5LegacyFallthrough(Process process,LogicalLine line,bool before)
        {
            if(!f4d5LegacyActive)return;
            if(before)
            {
                if(process.state.functionCount==0||!process.state.CurrentCalled.FunctionName.Equals(F4D5SetEquip,RuntimeConfig.StringComparison)||line is not FunctionLabelLine)return;
                f4d5LegacyFallthroughPending=true;
                compactFrames[^1].Pc=1009;
                f4d5P26=CaptureF4D5Point(process,"P26","EQUIPMENT_FUNC.ERB:1009:after");
                return;
            }
            if(!f4d5LegacyFallthroughPending)return;
            f4d5LegacyFallthroughPending=false;
            Return(process,F4D5SetEquip,false); StoppedBefore="SYSTEM.ERB:1023";
            f4d5P27=CaptureF4D5Point(process,"P27","SYSTEM.ERB:1023:before");
            throw new R0F1PlannedCheckpointException();
        }

        internal void ObserveF4D5LegacyEntry(CalledFunction call)
        {
            if(!f4d5LegacyActive)return;
            if(call.TopLabel.LabelName=="SET_TURNEND_EV_VAR")f4d5SetTurnEndMaterialized++;
        }

        private F4D5Point CaptureF4D5Point(Process process,string name,string pc)
        {
            var equip=ReadF4D5Equip(process); var result=(long[])process.vEvaluator.RESULT_ARRAY.Clone(); var results=(string[])process.vEvaluator.RESULTS_ARRAY.Clone();
            return new(name,pc,ReadF4D5Lcount(process),equip,HashLongs(equip),result,HashLongs(result),results,HashStrings(results),
                Candidate?EnsureF4D1Bank(GetEquipNumName).Args[0]:ReadLegacyPersistentArgs(process),ReadPersistentArg(process),
                string.Join(" > ",compactFrames.Select(FrameText)),EventCursor,process.state.SystemState.ToString(),process.state.isBegun,
                process.vEvaluator.GetR0C2RngHash(),process.vEvaluator.GetR0F4D3RandomCallCount(),DifferentialDeterminism.ObservationCount);
        }

        private long ReadF4D5Lcount(Process process)=>Candidate?f4d5Lcount:f4d5LegacyLcount?.GetIntValue(process.exm,[0])??0;
        private CompactNormalHandle F4D5Handle(int id,string name)
        {
            if(!persistentBanks.ContainsKey(name))persistentBanks.Add(name,new PersistentBank());
            return new(this,OwnerGeneration,id,name);
        }
        private long[] ReadF4D5Equip(Process process)=>Enumerable.Range(0,f4d5EquipPart!.Length).Select(i=>f4d5EquipPart.Token.GetIntValue(process.exm,[i])).ToArray();
        private static bool IsF4D5Line(LogicalLine line,string file,int number)=>line.Position is { } p&&p.LineNo==number&&Path.GetFileName(p.Filename).Equals(file,StringComparison.OrdinalIgnoreCase);
        private static string ResolverText(F4D5ResolverRow x)=>$"{x.Sequence}:{x.Iteration}:{x.Name}:{x.Resolution}:{x.Class}:{x.Invoked}:{x.ReturnedValue}:{x.Write}:{x.NestedName}:{x.NestedResolution}";
    }
}
#endif
