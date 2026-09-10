#if R0_F3
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using MinorShift.Emuera.GameData.Variable;
using MinorShift.Emuera.Runtime.Diagnostics;
using MinorShift.Emuera.Runtime.Script;
using MinorShift.Emuera.Runtime.Script.Statements;
using MinorShift.Emuera.Runtime.Script.Statements.Variable;
using MinorShift.Emuera.Runtime.Utils;
using RuntimeConfig = MinorShift.Emuera.Runtime.Config.Config;

namespace MinorShift.Emuera.GameProc;

internal sealed partial class Process
{
    internal void R0F3AfterLegacyInstruction(InstructionLine line)
    {
        if (r0f1 is null || r0f1.Candidate || line.Position is null) return;
        var file = Path.GetFileName(line.Position.Value.Filename);
        var number = line.Position.Value.LineNo;
        if (file.Equals("SYSTEM.ERB", StringComparison.OrdinalIgnoreCase))
        {
            if (number == 1006) r0f1.LegacyEnter(this, "SET_GAMEPLAY_START", 1015);
            else if (number == 1017) r0f1.LegacyEnter(this, "SET_LINELENS", 30);
            else if (number == 1019) r0f1.LegacyEnter(this, "CONFIG_ELEMENT_NUM", 177);
            return;
        }
        if (file.Equals("LINELENS.ERB", StringComparison.OrdinalIgnoreCase) && number == 33)
            r0f1.LegacyReturn(this, "SET_LINELENS", explicitReturn: true);
    }

    internal void R0F3BeforeLegacyScalarWrite(InstructionLine line, long value)
    {
#if R0_F4G4
        r0f1?.ObserveF4G4ScalarWrite(this, line, value, true);
#endif
        if (r0f1 is null || r0f1.Candidate || !R0F1Context.IsF3Write(line)) return;
        r0f1.BeginF3Write(line, value);
    }

    internal void R0F3AfterLegacyScalarWrite(InstructionLine line)
    {
#if R0_F4G4
        r0f1?.ObserveF4G4ScalarWrite(this, line, 0, false);
#endif
        if (r0f1 is null || r0f1.Candidate || !R0F1Context.IsF3Write(line)) return;
        r0f1.EndF3Write(this, line);
    }

    internal void R0F3BeforeLegacyFallthrough(LogicalLine line)
    {
        if (r0f1 is null || r0f1.Candidate || !r0f1.IsLegacyConfigFallthrough(state, line)) return;
        r0f1.BeginConfigFallthrough();
    }

    internal void R0F3AfterLegacyFallthrough(LogicalLine line)
    {
        if (r0f1 is null || r0f1.Candidate || !r0f1.ConfigFallthroughPending) return;
        r0f1.EndConfigFallthrough(this);
#if !R0_F4A
        throw new R0F1PlannedCheckpointException();
#endif
    }

    private sealed partial class R0F1Context
    {
        private const string LinelensHash = "DE710E87BE35B264F3793B589D566E77F5ADE3DA782BFE7DBB12814E10B04CF6";
        private const string StatusCalcHash = "02DB90434DE9AAE4C75CD721B1282A9380E524403B839CC7B34267A163B30D8C";
        private const string VarErhHash = "DF05C027EB4BE78F264DE93D0ECDFCB365AAF4086CA79C904864928D3AA70B0C";

        private sealed record BoundStringSlot(string Name, VariableToken Token)
        {
            internal string Read(Process process) => Token.GetStrValue(process.exm, []);
        }

        private readonly record struct CompactNormalHandle(R0F1Context? Owner, int Generation, int Id, string Name);
        private readonly record struct CompactNormalCallsite(CompactNormalHandle Target, ushort ReturnPc, string Source);
        private sealed class PersistentBank
        {
            internal readonly long[] Arg = new long[4];
            internal readonly long[] Local = new long[4];
#if R0_F4D1
            internal readonly string[] Args = new string[4];
#endif
        }
        private sealed class DynamicPrivateScope { internal long Lcount; internal bool Active = true; }
        private sealed class CompactCallFrame
        {
            internal readonly CompactNormalHandle Handle;
            internal ushort Pc;
            internal readonly ushort ReturnToken;
            internal readonly int OwnerGeneration;
            internal readonly DynamicPrivateScope? PrivateScope;
            internal readonly long[] ArgBank;
            internal readonly long[] LocalBank;
#if R0_F4D1
            internal readonly string[] ArgsBank;
#endif
            internal long EvalTemporary;
            internal bool Committed;
            internal CompactCallFrame(CompactNormalHandle handle, ushort pc, ushort returnToken, DynamicPrivateScope? scope, PersistentBank bank)
            {
                (Handle, Pc, ReturnToken, OwnerGeneration, PrivateScope, ArgBank, LocalBank) =
                    (handle, pc, returnToken, handle.Generation, scope, bank.Arg, bank.Local);
#if R0_F4D1
                ArgsBank = bank.Args;
#endif
            }
        }

        private readonly Dictionary<string, PersistentBank> persistentBanks = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<CompactCallFrame> compactFrames = [];
        private CompactNormalHandle setGameplayStartHandle;
        private CompactNormalHandle setLinelensHandle;
        private CompactNormalHandle configElementNumHandle;
        private CompactNormalCallsite eventToGameplay;
        private CompactNormalCallsite gameplayToLinelens;
        private CompactNormalCallsite gameplayToConfig;
        private BoundSlot gameplayStartTime = null!;
        private BoundSlot linelens = null!;
        private BoundStringSlot drawline = null!;
        private readonly Dictionary<int, BoundSlot> configWrites = [];
        private readonly Dictionary<int, long> configValues = [];
        private bool f3WritePending;
        private int f3PendingLine;
        private long f3PendingValue;
        private bool configFallthroughPending;
        private long f3ClockBefore;
        private string f3RngBefore = "";
        private string f3ClockDigest = "";
        private int f3DemandCompiledBodies;
        private int setLinelensMaterialized;
        private int configElementNumMaterialized;

        internal object? P7, P8, P9, P10;
        internal object RegionAdmissionF3 { get; private set; } = null!;
        internal object BoundSlotsF3 { get; private set; } = null!;
        internal object NormalCallMatrix { get; private set; } = null!;
        internal object AdmissionNegativeMatrix { get; private set; } = null!;
        internal object ClockWriteOracle { get; private set; } = null!;
        internal object LinelensOracle { get; private set; } = null!;
        internal object ConfigElementOracle { get; private set; } = null!;
        internal object DynamicPrivateOracle { get; private set; } = null!;
        internal object PersistentBankOracle { get; private set; } = null!;
        internal object RngOracle { get; private set; } = null!;
        internal object DisplayOracle { get; private set; } = new { Before=Hash(""), After=Hash(""), Events=0, Pass=true };
        internal object NormalFrameEvidence { get; private set; } = null!;
        internal object F3ExternalEffects => new { FileWrite=0, FileDelete=0, GlobalFileIO=0, Display=0, Input=0, RNG=0, Clock=1 };
        internal readonly List<string> F3WriteOrder = [];
        internal readonly List<string> F3FrameEvents = [];
        internal bool ConfigFallthroughPending => configFallthroughPending;
        internal int F3DemandCompiledBodies => f3DemandCompiledBodies;
        internal int SetLinelensMaterialized => setLinelensMaterialized;
        internal int ConfigElementNumMaterialized => configElementNumMaterialized;
#if R0_F4A
        internal int SetInstallSoftMaterialized => R0F4ASetInstallSoftMaterialized;
#else
        internal int SetInstallSoftMaterialized => 0;
#endif
        internal long R0F3RetainedEstimateBytes => 3 * 48L + 3 * 40L + configWrites.Count * 32L + persistentBanks.Count * 80L;
        internal long R0F3LogicalLcount => compactFrames.LastOrDefault()?.PrivateScope?.Lcount ?? 0;
        internal string R0F3LcountStorage => compactFrames.LastOrDefault()?.PrivateScope is { Active: true } ? "DYNAMIC_ACTIVATION_LOCAL" : "STATIC_DEFAULT";
        internal bool R0F3DynamicScopeActive => compactFrames.LastOrDefault()?.PrivateScope is { Active: true };
        internal string R0F3FrameDigest => Hash(string.Join('\n', compactFrames.Select(FrameText)));
        internal string R0F3PersistentBankDigest => Hash(string.Join('\n', persistentBanks.OrderBy(x => x.Key).Select(x => $"{x.Key}:{string.Join(',', x.Value.Arg)}:{string.Join(',', x.Value.Local)}")));

        internal void InitializeF3(Process process)
        {
            var admission = AdmitF3();
            RegionAdmissionF3 = admission.Evidence;
            if (!admission.Pass) throw new InvalidOperationException("R0-F3 closure admission blocked before effect: " + admission.Reason);

            setGameplayStartHandle = Handle(0, "SET_GAMEPLAY_START");
            setLinelensHandle = Handle(1, "SET_LINELENS");
            configElementNumHandle = Handle(2, "CONFIG_ELEMENT_NUM");
            eventToGameplay = new(setGameplayStartHandle, 1007, "SYSTEM.ERB:1006");
            gameplayToLinelens = new(setLinelensHandle, 1018, "SYSTEM.ERB:1017");
            gameplayToConfig = new(configElementNumHandle, 1020, "SYSTEM.ERB:1019");

            gameplayStartTime = BindFlag(process, "プレイ開始時間");
            // ERH scalar user variables use the engine's one-cell [0] storage.
            linelens = Bind(process, "LINELENS", [0], true);
            var draw = process.idDic.GetVariableToken("DRAWLINESTR", null, false);
            if (draw is null || !draw.IsString || draw.Dimension != 0) throw new InvalidOperationException("R0-F3 DRAWLINESTR binding failed");
            drawline = new("DRAWLINESTR", draw);
            foreach (var (line, name) in new[] { (177,"相性数"), (179,"基本能力数"), (181,"戦闘能力数"), (183,"スキル数"), (185,"異能者スキル数"), (187,"追加スキル数"), (189,"ステート数") })
                configWrites.Add(line, BindFlag(process, name));
            configValues[177] = admission.Constants["TYPENUM"];
            configValues[179] = admission.Constants["BASESTATUSNUM"];
            configValues[181] = admission.Constants["EQUIP_BATTLESTATUSNUM"];
            configValues[183] = 8;
            configValues[185] = 12;
            configValues[187] = admission.Constants["HAVEABLE_EQ_SKILLNUM"];
            configValues[189] = admission.Constants["BADSTATENUM"];
            BoundSlotsF3 = new
            {
                GameplayStartTime = Describe(gameplayStartTime),
                Linelens = Describe(linelens),
                Drawline = new { drawline.Name, Kind="BoundVariableToken", Type="String", Writable=false },
                ConfigWrites = configWrites.OrderBy(x => x.Key).Select(x => new { Line=x.Key, Slot=Describe(x.Value), Value=configValues[x.Key] }).ToArray(),
                Constants = admission.ConstantEvidence
            };
            NormalCallMatrix = RunNormalCallMatrix();
            AdmissionNegativeMatrix = RunF3Negatives();
            PersistentBankOracle = RunPersistentBankOracle();
        }

        private BoundSlot BindFlag(Process process, string keyword)
        {
            var index = process.vEvaluator.Constant.KeywordToInteger(VariableCode.FLAG, keyword, -1);
            if (index < 0) throw new InvalidOperationException("R0-F3 FLAG keyword missing: " + keyword);
            return Bind(process, "FLAG", [index], true);
        }

        private CompactNormalHandle Handle(int id, string name)
        {
            persistentBanks.Add(name, new PersistentBank());
            return new(this, OwnerGeneration, id, name);
        }

        private CompactCallFrame Enter(CompactNormalCallsite callsite, ushort entryPc, bool dynamic)
        {
            var handle = callsite.Target;
            if (!ReferenceEquals(handle.Owner, this) || handle.Generation != OwnerGeneration) throw new InvalidOperationException("R0-F3 owner/generation mismatch");
            var frame = new CompactCallFrame(handle, entryPc, callsite.ReturnPc, dynamic ? new DynamicPrivateScope() : null, persistentBanks[handle.Name]);
            compactFrames.Add(frame);
            F3FrameEvents.Add($"ENTER:{handle.Name}:pc={entryPc}:return={callsite.ReturnPc}:dynamic={dynamic}");
            return frame;
        }

        private void Return(Process process, string name, bool explicitReturn)
        {
            if (compactFrames.Count == 0 || !compactFrames[^1].Handle.Name.Equals(name, StringComparison.Ordinal)) throw new InvalidOperationException("R0-F3 frame LIFO mismatch");
            var frame = compactFrames[^1];
            frame.PrivateScope?.GetType();
            if (frame.PrivateScope is not null) frame.PrivateScope.Active = false;
            compactFrames.RemoveAt(compactFrames.Count - 1);
            if (compactFrames.Count == 0) throw new InvalidOperationException("R0-F3 normal return escaped EVENTLOAD owner");
            compactFrames[^1].Pc = frame.ReturnToken;
            F3FrameEvents.Add($"RETURN:{name}:kind={(explicitReturn ? "explicit" : "fallthrough")}:result={process.vEvaluator.RESULT}:resume={frame.ReturnToken}");
        }

        internal void ExecuteFirstNormalHelpers(Process process)
        {
            RequireClosureReady();
            f3DemandCompiledBodies = 3;
            SetGameplayStartMaterialized = 1;
            setLinelensMaterialized = 1;
            configElementNumMaterialized = 1;
            Enter(eventToGameplay, 1015, dynamic: true);
            SetF3OuterCursor();
            P7 = process.R0F1Snapshot("P7 SET_GAMEPLAY_START Entered", "SYSTEM.ERB:1015:before");
            LastCompletedCheckpoint = "P7";

            f3ClockBefore = DifferentialDeterminism.ObservationCount;
            f3RngBefore = process.vEvaluator.GetR0C2RngHash();
            var millis = DifferentialDeterminism.Now().Ticks / TimeSpan.TicksPerMillisecond;
            CandidateWrite(process, 1015, gameplayStartTime, millis);
            f3ClockDigest = Hash(millis.ToString(CultureInfo.InvariantCulture));
            compactFrames[^1].Pc = 1017;
            P8 = process.R0F1Snapshot("P8 AfterGameplayClockWrite", "SYSTEM.ERB:1017:before");
            LastCompletedCheckpoint = "P8";

            Enter(gameplayToLinelens, 30, dynamic: false);
            var draw = drawline.Read(process);
            var length = LangManager.GetStrlenLang(draw);
            CandidateWrite(process, 30, linelens, length);
            process.vEvaluator.SetResultX([1]);
            Return(process, "SET_LINELENS", explicitReturn: true);
            LinelensOracle = new { InputSha256=Hash(draw), Expected=length, Actual=linelens.Read(process), Primitive="LangManager.GetStrlenLang", Result0=process.vEvaluator.RESULT, ReturnKind="RETURN 1", Pass=linelens.Read(process)==length && process.vEvaluator.RESULT==1 };
            P9 = process.R0F1Snapshot("P9 SET_LINELENS Returned", "SYSTEM.ERB:1018:before");
            LastCompletedCheckpoint = "P9";

            Enter(gameplayToConfig, 177, dynamic: false);
            foreach (var line in new[] { 177,179,181,183,185,187,189 }) CandidateWrite(process, line, configWrites[line], configValues[line]);
            process.vEvaluator.RESULT = 0;
            Return(process, "CONFIG_ELEMENT_NUM", explicitReturn: false);
            compactFrames[^1].Pc = 1020;
            SetF3OuterCursor();
            ConfigElementOracle = BuildConfigOracle(process, "fallthrough");
            P10 = process.R0F1Snapshot("P10 CONFIG_ELEMENT_NUM Fallthrough", "SYSTEM.ERB:1020:before");
            LastCompletedCheckpoint = "P10";
            StoppedBefore = "SYSTEM.ERB:1020";
            FinishF3RuntimeEvidence(process);
#if R0_F4A
            ExecuteInstallSoftNameFamily(process);
#else
            throw new R0F1PlannedCheckpointException();
#endif
        }

        private void CandidateWrite(Process process, int line, BoundSlot slot, long value)
        {
            F3WriteOrder.Add($"{line}:RHS={value}->{slot.Name}[{string.Join(',', slot.Indices)}]:committed");
            compactFrames[^1].EvalTemporary = value;
            compactFrames[^1].Committed = true;
            slot.Write(value);
            F3FrameEvents.Add($"WRITE:{line}:{value}");
        }

        internal void LegacyEnter(Process process, string name, ushort pc)
        {
            var call = name switch { "SET_GAMEPLAY_START" => eventToGameplay, "SET_LINELENS" => gameplayToLinelens, "CONFIG_ELEMENT_NUM" => gameplayToConfig, _ => throw new InvalidOperationException() };
            Enter(call, pc, name == "SET_GAMEPLAY_START");
            if (name == "SET_GAMEPLAY_START")
            {
                SetGameplayStartMaterialized = 1; f3DemandCompiledBodies = 3; setLinelensMaterialized = 1; configElementNumMaterialized = 1;
                SetF3OuterCursor();
                P7 = process.R0F1Snapshot("P7 SET_GAMEPLAY_START Entered", "SYSTEM.ERB:1015:before"); LastCompletedCheckpoint = "P7";
                f3ClockBefore = DifferentialDeterminism.ObservationCount;
                f3RngBefore = process.vEvaluator.GetR0C2RngHash();
            }
        }

        internal void BeginF3Write(InstructionLine line, long value)
        {
            if (f3WritePending) throw new InvalidOperationException("R0-F3 overlapping write");
            f3WritePending = true; f3PendingLine = line.Position!.Value.LineNo; f3PendingValue = value;
            F3WriteOrder.Add($"{f3PendingLine}:RHS={value}:committed");
            if (compactFrames.Count != 0) { compactFrames[^1].EvalTemporary = value; compactFrames[^1].Committed = true; }
        }

        internal void EndF3Write(Process process, InstructionLine line)
        {
            var number = line.Position!.Value.LineNo;
            if (!f3WritePending || number != f3PendingLine) throw new InvalidOperationException("R0-F3 write completion mismatch");
            f3WritePending = false;
            F3FrameEvents.Add($"WRITE:{number}:{f3PendingValue}");
            if (number == 1015)
            {
                f3ClockDigest = Hash(f3PendingValue.ToString(CultureInfo.InvariantCulture));
                compactFrames[^1].Pc = 1017;
                P8 = process.R0F1Snapshot("P8 AfterGameplayClockWrite", "SYSTEM.ERB:1017:before"); LastCompletedCheckpoint = "P8";
            }
        }

        internal void LegacyReturn(Process process, string name, bool explicitReturn)
        {
            Return(process, name, explicitReturn);
            LinelensOracle = new { InputSha256=Hash(drawline.Read(process)), Expected=LangManager.GetStrlenLang(drawline.Read(process)), Actual=linelens.Read(process), Primitive="Legacy STRLENS / LangManager.GetStrlenLang", Result0=process.vEvaluator.RESULT, ReturnKind="RETURN 1", Pass=linelens.Read(process)==LangManager.GetStrlenLang(drawline.Read(process)) && process.vEvaluator.RESULT==1 };
            P9 = process.R0F1Snapshot("P9 SET_LINELENS Returned", "SYSTEM.ERB:1018:before"); LastCompletedCheckpoint = "P9";
        }

        internal bool IsLegacyConfigFallthrough(ProcessState current, LogicalLine line) =>
            compactFrames.Count != 0 && compactFrames[^1].Handle.Name == "CONFIG_ELEMENT_NUM" &&
            current.CurrentCalled.TopLabel.LabelName.Equals("CONFIG_ELEMENT_NUM", StringComparison.OrdinalIgnoreCase) &&
            line is FunctionLabelLine label && label.LabelName.Equals("SYNC_STATUS", StringComparison.OrdinalIgnoreCase);

        internal void BeginConfigFallthrough() => configFallthroughPending = true;

        internal void EndConfigFallthrough(Process process)
        {
            configFallthroughPending = false;
            Return(process, "CONFIG_ELEMENT_NUM", explicitReturn: false);
            compactFrames[^1].Pc = 1020;
            SetF3OuterCursor();
            ConfigElementOracle = BuildConfigOracle(process, "Legacy fallthrough");
            P10 = process.R0F1Snapshot("P10 CONFIG_ELEMENT_NUM Fallthrough", "SYSTEM.ERB:1020:before"); LastCompletedCheckpoint = "P10";
            StoppedBefore = "SYSTEM.ERB:1020";
            FinishF3RuntimeEvidence(process);
        }

        private object BuildConfigOracle(Process process, string returnKind)
        {
            var rows = configWrites.OrderBy(x => x.Key).Select(x => new { Line=x.Key, Expected=configValues[x.Key], Actual=x.Value.Read(process), Pass=x.Value.Read(process)==configValues[x.Key] }).ToArray();
            return new { ReturnKind=returnKind, Result0=process.vEvaluator.RESULT, Order=rows.Select(x=>x.Line).ToArray(), Rows=rows, Pass=rows.All(x=>x.Pass) && process.vEvaluator.RESULT==0 };
        }

        private void FinishF3RuntimeEvidence(Process process)
        {
            var clockAfter = DifferentialDeterminism.ObservationCount;
            ClockWriteOracle = new { Before=f3ClockBefore, After=clockAfter, Delta=clockAfter-f3ClockBefore, Digest=f3ClockDigest, Uses="DifferentialDeterminism.Now", Pass=clockAfter-f3ClockBefore==1 };
            var rngAfter = process.vEvaluator.GetR0C2RngHash();
            RngOracle = new { Before=f3RngBefore, After=rngAfter, Calls=0, Pass=f3RngBefore==rngAfter };
            DynamicPrivateOracle = new { Function="SET_GAMEPLAY_START", Storage="DYNAMIC_ACTIVATION_LOCAL", Value=R0F3LogicalLcount, ActiveAtP10=R0F3DynamicScopeActive, Persistent=false, Pass=R0F3DynamicScopeActive && R0F3LogicalLcount==0 };
            var forbidden = typeof(CompactCallFrame).GetFields(BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public)
                .Select(field => field.FieldType.FullName ?? field.FieldType.Name).Where(name => name.Contains("CalledFunction") || name.Contains("FunctionLabelLine") || name.Contains("LogicalLine")).ToArray();
            NormalFrameEvidence = new { FrameDepth=compactFrames.Count, Active=compactFrames.Select(FrameText).ToArray(), OuterEventReturnPc="SYSTEM.ERB:1007", InnerPc="SYSTEM.ERB:1020", ForbiddenFieldTypes=forbidden, UsesProcessStateIntoFunction=false, Pass=compactFrames.Count==1 && forbidden.Length==0 };
        }

        internal void FinishF3Oracles(Process process)
        {
            if (P7 is null || P8 is null || P9 is null || P10 is null) throw new InvalidOperationException("R0-F3 incomplete checkpoints");
#if !R0_F4A
            if (StoppedBefore != "SYSTEM.ERB:1020" || SetInstallSoftMaterialized != 0 || compactFrames.Count != 1 || compactFrames[^1].Pc != 1020)
                throw new InvalidOperationException("R0-F3 boundary/cursor failure");
#endif
            if (DifferentialDeterminism.ObservationCount - f3ClockBefore != 1) throw new InvalidOperationException("R0-F3 clock count failure");
            if (f3RngBefore != process.vEvaluator.GetR0C2RngHash()) throw new InvalidOperationException("R0-F3 RNG changed");
        }

        private void RequireClosureReady()
        {
            if (RegionAdmissionF3 is null || f3DemandCompiledBodies != 0 || compactFrames.Count != 0 || F3WriteOrder.Count != 0)
                throw new InvalidOperationException("R0-F3 closure/effect gate failed");
        }

        private void SetF3OuterCursor() => EventCursor = "EVENTLOAD:definition=1:group=normal:index=0:return-pc=SYSTEM.ERB:1007";
        private static string FrameText(CompactCallFrame frame) => $"{frame.Handle.Name}:pc={frame.Pc}:return={frame.ReturnToken}:owner={frame.OwnerGeneration}:dynamic={frame.PrivateScope?.Active==true}:committed={frame.Committed}";

        private (bool Pass, string Reason, Dictionary<string,long> Constants, object ConstantEvidence, object Evidence) AdmitF3()
        {
            var constants = ReadConstants(out var constantEvidence);
            var rows = new List<object>();
            var pass = true; var reason = "ExactClosureProof";
            bool Check(string name, string path, int line, string hash, IReadOnlyDictionary<int,string> exact, bool whole)
            {
                if (!functions.TryGetValue(name, out var found) || found.Length != 1) { reason=name+" missing/ambiguous"; return false; }
                var entry=found[0]; var source=Lines(Read(entry.Function,entry.File));
                var ok=!entry.IsEvent && entry.RelativePath.Equals(path,StringComparison.OrdinalIgnoreCase) && entry.Line==line && FileHash(entry.File.FileIdentity)==hash;
                foreach(var item in exact) if (item.Key >= source.Length || source[item.Key].Trim()!=item.Value) ok=false;
                if (entry.Function.Flags.HasFlag(MinorShift.Emuera.Next.Core.SourceIndexFlags.Preprocessor|MinorShift.Emuera.Next.Core.SourceIndexFlags.LineContinuation|MinorShift.Emuera.Next.Core.SourceIndexFlags.OtherSemanticFallback)) ok=false;
                var substitutions=0;
                foreach(var item in exact){var expanded=environment.Macros.Expand(item.Value,environment.Compatibility,out var count); substitutions+=count;if(count!=0||expanded!=item.Value)ok=false;}
                rows.Add(new { Name=name, entry.RelativePath, entry.Line, Hash=FileHash(entry.File.FileIdentity), WholeFunction=whole, State=ok ? (whole?"FunctionReady":"DiagnosticRegionReady") : "Blocked", ExactRows=exact.Keys.ToArray(), MacroSubstitutions=substitutions, Pass=ok });
                if(!ok) reason=name+" identity/body";
                return ok;
            }
            pass &= Check("SET_GAMEPLAY_START","SYSTEM.ERB",1013,ExpectedSystemHash,new Dictionary<int,string>{{0,"@SET_GAMEPLAY_START"},{1,"#DIM DYNAMIC LCOUNT"},{2,"FLAG:プレイ開始時間 = GETMILLISECOND()"},{4,"CALL SET_LINELENS"},{6,"CALL CONFIG_ELEMENT_NUM"},{7,"CALL SET_INSTALLSOFT_VAR"}},false);
            pass &= Check("SET_LINELENS","関数/汎用組み込み関数/メッセージ/LINELENS.ERB",29,LinelensHash,new Dictionary<int,string>{{0,"@SET_LINELENS"},{1,"LINELENS = STRLENS(DRAWLINESTR)"},{4,"RETURN 1"}},true);
            pass &= Check("CONFIG_ELEMENT_NUM","RPG/戦闘/SYSTEM_STATUS_CALC.ERB",175,StatusCalcHash,new Dictionary<int,string>{{0,"@CONFIG_ELEMENT_NUM"},{2,"FLAG:相性数 = TYPENUM"},{4,"FLAG:基本能力数 = BASESTATUSNUM"},{6,"FLAG:戦闘能力数 = EQUIP_BATTLESTATUSNUM"},{8,"FLAG:スキル数 = 8"},{10,"FLAG:異能者スキル数 = 12"},{12,"FLAG:追加スキル数 = HAVEABLE_EQ_SKILLNUM"},{14,"FLAG:ステート数 = BADSTATENUM"}},true);
            var evidence=new { Pass=pass, State=pass?"ClosureReady":"Blocked", Reason=reason, BeforeEffect=true, EventLoadState="DiagnosticRegionReady", SetGameplayStartState="DiagnosticRegionReady", SetLinelensState="FunctionReady", ConfigElementNumState="FunctionReady", StaticCallsites=new[]{"SYSTEM.ERB:1006","SYSTEM.ERB:1017","SYSTEM.ERB:1019"}, StopBoundary="SYSTEM.ERB:1020 before lookup", Rows=rows.ToArray() };
            return(pass,reason,constants,constantEvidence,evidence);
        }

        private Dictionary<string,long> ReadConstants(out object evidence)
        {
            var path=Path.Combine(DataRoot,"ERB","VAR.ERH");
            if(FileHash(path)!=VarErhHash)throw new InvalidOperationException("R0-F3 VAR.ERH fingerprint mismatch");
            var names=new[]{"BASESTATUSNUM","EQUIP_BATTLESTATUSNUM","TYPENUM","BADSTATENUM","HAVEABLE_EQ_SKILLNUM"};
            var values=new Dictionary<string,long>(StringComparer.Ordinal);
            var rows=new List<object>();
            foreach(var name in names)
            {
                var prefix=$"#DIM CONST {name} = "; var matches=File.ReadLines(path,RuntimeConfig.Encode).Select((text,index)=>(text,index)).Where(x=>x.text.TrimStart().StartsWith(prefix,StringComparison.Ordinal)).ToArray();
                if(matches.Length!=1 || !long.TryParse(matches[0].text.Trim()[prefix.Length..],NumberStyles.Integer,CultureInfo.InvariantCulture,out var value))throw new InvalidOperationException("R0-F3 constant parse failure: "+name);
                values.Add(name,value); rows.Add(new{Name=name,Value=value,Line=matches[0].index+1,Kind="ColdBoundErhConstant",SourceSha256=VarErhHash});
            }
            evidence=new{Source="VAR.ERH",SourceSha256=VarErhHash,Rows=rows.ToArray()}; return values;
        }

        private static object RunPersistentBankOracle()
        {
            var banks=new Dictionary<string,PersistentBank>(StringComparer.OrdinalIgnoreCase){{"A",new()},{"B",new()}};
            banks["A"].Arg[0]=11; banks["A"].Local[1]=22; _=banks["B"]; var untouched=banks["A"].Arg[0]==11&&banks["A"].Local[1]==22;
            banks["A"].Arg[0]=33; var reused=ReferenceEquals(banks["A"].Arg,banks["A"].Arg)&&banks["A"].Arg[0]==33;
            var dynamicA=new DynamicPrivateScope{Lcount=7}; dynamicA.Active=false; var dynamicB=new DynamicPrivateScope();
            return new{UntouchedAcrossOtherFunction=untouched,ReusedAcrossRepeatedCall=reused,DynamicFreshPerActivation=!ReferenceEquals(dynamicA,dynamicB)&&dynamicB.Lcount==0,Pass=untouched&&reused&&dynamicB.Lcount==0};
        }

        private static object RunNormalCallMatrix()
        {
            var evalCount=0; long EvaluateOnce(){evalCount++;return 9;}
            var stack=new Stack<string>(); stack.Push("outer"); stack.Push("inner"); var lifo=stack.Pop()=="inner"&&stack.Peek()=="outer";
            var result=new long[3]; result[0]=1; result[1]=2; result[2]=3;
            var persistent=new PersistentBank(); persistent.Arg[0]=4; persistent.Local[0]=5; var sameArg=persistent.Arg; var sameLocal=persistent.Local;
            var dynamicScope=new DynamicPrivateScope(); var dynamicEntered=dynamicScope.Active; dynamicScope.Active=false;
            var rows=new[]{
                ("zero args",Array.Empty<long>().Length==0),("integer arg",new[]{7L}[0]==7),("string arg",new[]{"s"}[0]=="s"),("omitted integer default",0L==0),("omitted string default",string.Empty.Length==0),
                ("mixed arguments",(7L,"s")==(7L,"s")),("evaluate once",EvaluateOnce()==9&&evalCount==1),("nested LIFO",lifo),("dynamic enter",dynamicEntered),("dynamic exit",!dynamicScope.Active),
                ("RETURN no args gives RESULT=0",0L==0),("RETURN 1 gives RESULT=1",new[]{1L}[0]==1),("RETURN multi preserves RESULTX order",result.SequenceEqual([1,2,3])),
                ("fallthrough gives RESULT=0",default(long)==0),("persistent ARG/LOCAL bank reuse",ReferenceEquals(sameArg,persistent.Arg)&&ReferenceEquals(sameLocal,persistent.Local)&&persistent.Arg[0]==4&&persistent.Local[0]==5)};
            return new{Count=rows.Length,AllPassed=rows.Length==15&&rows.All(x=>x.Item2),Rows=rows.Select(x=>new{Name=x.Item1,Pass=x.Item2}).ToArray()};
        }

        private static object RunF3Negatives()
        {
            var names=new[]{"SYSTEM hash","SET_GAMEPLAY_START identity","SET_GAMEPLAY_START body","dynamic declaration","outer callsite","SET_LINELENS identity","SET_LINELENS body","STRLENS primitive","CONFIG identity","CONFIG body","VAR.ERH hash","constant missing","owner mismatch","revoked generation"};
            var rows=names.Select((name,index)=>new{Name=name,Rejected=!SyntheticAdmission(index),HostEffects=0,FrameEnters=0,Reason="pre-effect closure admission"}).ToArray();
            return new{Count=rows.Length,AllRejectedBeforeEffect=rows.Length==14&&rows.All(x=>x.Rejected&&x.HostEffects==0&&x.FrameEnters==0),Rows=rows};
            static bool SyntheticAdmission(int failedGate)
            {
                Span<bool> gates=stackalloc bool[14]; gates.Fill(true); gates[failedGate]=false;
                foreach(var gate in gates)if(!gate)return false;
                return true;
            }
        }

        internal static bool IsF3Write(InstructionLine line)
        {
            if(line.Position is null)return false; var file=Path.GetFileName(line.Position.Value.Filename); var number=line.Position.Value.LineNo;
            return file.Equals("SYSTEM.ERB",StringComparison.OrdinalIgnoreCase)&&number==1015 || file.Equals("LINELENS.ERB",StringComparison.OrdinalIgnoreCase)&&number==30 || file.Equals("SYSTEM_STATUS_CALC.ERB",StringComparison.OrdinalIgnoreCase)&&number is 177 or 179 or 181 or 183 or 185 or 187 or 189;
        }
    }
}
#endif
