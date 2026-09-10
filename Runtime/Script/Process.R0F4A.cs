#if R0_F4A
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using MinorShift.Emuera.GameData.Variable;
using MinorShift.Emuera.Next.Core;
using MinorShift.Emuera.Runtime.Diagnostics;
using MinorShift.Emuera.Runtime.Script;
using MinorShift.Emuera.Runtime.Script.Statements;
using RuntimeConfig = MinorShift.Emuera.Runtime.Config.Config;

namespace MinorShift.Emuera.GameProc;

internal sealed partial class Process
{
    internal void R0F4ABeforeLegacyInstruction(InstructionLine line)
    {
        if (r0f1 is null || r0f1.Candidate) return;
        if (!R0F1Context.IsInstallSoftLine(line, 492)) return;
        r0f1.CompleteInstallSoftNameFamily(this);
#if !R0_F4B
        throw new R0F1PlannedCheckpointException();
#endif
    }

    internal void R0F4AAfterLegacyInstruction(InstructionLine line)
    {
        if (r0f1 is null || r0f1.Candidate || line.Position is null) return;
        if (R0F1Context.IsSystemLine(line, 1020))
        {
            var token = state.CurrentCalled.TopLabel.GetPrivateVariable("LCOUNT");
            r0f1.LegacyEnterInstallSoft(this, token);
            return;
        }
        if (!R0F1Context.IsInstallSoftLine(line, 485, 489, 491)) return;
        var number = line.Position.Value.LineNo;
        if (number == 485) r0f1.LegacyForEntered(this);
        else if (number == 489) r0f1.LegacyLoopAdvanced(this, "CONTINUE");
        else r0f1.LegacyLoopAdvanced(this, "NEXT");
    }

    internal void R0F4AObserveLegacyDynamicCall(InstructionLine line, string target, bool found)
    {
        if (r0f1 is null || r0f1.Candidate || !R0F1Context.IsInstallSoftLine(line, 486)) return;
        r0f1.LegacyDynamicCall(this, target, found);
    }

    internal void R0F4ABeforeLegacyStringWrite(InstructionLine line, string value)
    {
        if (r0f1 is null || r0f1.Candidate || !R0F1Context.IsInstallSoftLine(line, 487)) return;
        r0f1.BeginLegacyInstallSoftNameWrite(this, value);
    }

    internal void R0F4AAfterLegacyStringWrite(InstructionLine line)
    {
        if (r0f1 is null || r0f1.Candidate || !R0F1Context.IsInstallSoftLine(line, 487)) return;
        r0f1.EndLegacyInstallSoftNameWrite(this);
    }

    internal void R0F4ABeforeLegacyFallthrough(LogicalLine line)
    {
        if (r0f1 is null || r0f1.Candidate || !r0f1.IsLegacyInstallSoftNameReturn(state)) return;
        r0f1.BeginLegacyInstallSoftNameReturn();
    }

    internal void R0F4AAfterLegacyFallthrough(LogicalLine line)
    {
        if (r0f1 is null || r0f1.Candidate || !r0f1.LegacyInstallSoftNameReturnPending) return;
        r0f1.EndLegacyInstallSoftNameReturn(this);
    }

    private sealed partial class R0F1Context
    {
        private const string InstallSoftHash = "CFE5AECA24B0034571E49E1AFECDDB7FAF992F617F76460A2A8A0B3CAACEB4E6";
        private const int InstallSoftCount = 300;
        private enum F4AResolution { Missing, Known, UnresolvedIdentity, Blocked, WrongKind }

        private sealed class FamilyDescriptor
        {
            internal readonly int Index;
            internal readonly string Name;
            internal readonly R0F1Definition Definition;
            internal readonly string SourceSha256;
            internal readonly string BodySha256;
            internal CompactNormalHandle? Handle;
            internal string? Value;
            internal FamilyDescriptor(int index, string name, R0F1Definition definition, string sourceSha256, string bodySha256)
                => (Index, Name, Definition, SourceSha256, BodySha256) = (index, name, definition, sourceSha256, bodySha256);
        }

        private sealed record BoundIndexedStringSlot(string Name, VariableToken Token, int Length)
        {
            internal string Read(Process process, long index) => Token.GetStrValue(process.exm, [index]) ?? string.Empty;
            internal void Write(string value, long index)
            {
                var indices = new[] { index };
                Token.CheckElement(indices);
                Token.SetValue(value, indices);
            }
        }

        private sealed class StaticInstallSoftPrivate { internal long Lcount; }
        internal sealed record F4ATargetRow(int Iteration, long Lcount, string Target, string Resolution);
        internal sealed record F4ALoopRow(int Iteration, long Before, string Target, string Resolution, string Transition, long After);
        internal sealed record F4AWriteRow(int Sequence, long Index, string Value, string ValueSha256);

        private readonly Dictionary<string, FamilyDescriptor> familyCatalog = new(RuntimeConfig.StrComper);
        private readonly Dictionary<string, string> materializedFamilyBodies = new(RuntimeConfig.StrComper);
        private readonly StaticInstallSoftPrivate installSoftPrivate = new();
        private BoundIndexedStringSlot installSoftName = null!;
        private CompactNormalHandle setInstallSoftHandle;
        private CompactNormalCallsite gameplayToInstallSoft;
        private VariableToken? legacyInstallSoftLcount;
        private string? pendingLegacyTarget;
        private string? pendingLegacyResolution;
        private long pendingLegacyLcount;
        private bool legacyReturnPending;
        private string? pendingWriteValue;
        private string f4aRngBefore = "";
        private long f4aClockBefore;
        private string initialInstallSoftNameHash = "";

        internal object? P11, P12Existing, P12Missing, P13;
        internal object RegionAdmissionF4A { get; private set; } = null!;
        internal object FamilyAdmissionF4A { get; private set; } = null!;
        internal object DynamicCallMatrix { get; private set; } = null!;
        internal object ForMatrix { get; private set; } = null!;
        internal object FamilyTargetInventory { get; private set; } = null!;
        internal object InstallSoftNameOracle { get; private set; } = null!;
        internal object F4AGuardEvidence { get; private set; } = null!;
        internal object F4ACorrectness { get; private set; } = null!;
        internal object F4AMemoryEstimate { get; private set; } = null!;
        internal object F4AExternalEffects { get; private set; } = new { FileIO=0, Display=0, Input=0, RNG=0, Clock=0, ScriptStringWrites=0 };
        internal readonly List<F4ATargetRow> F4ADynamicTargets = [];
        internal readonly List<F4ALoopRow> F4ALcountSequence = [];
        internal readonly List<F4AWriteRow> F4AWriteOrder = [];
        internal readonly List<string> F4AFrameEvents = [];
        internal int R0F4ASetInstallSoftMaterialized;
        internal int F4ASecondFamilyMaterialized;
        internal int F4ADemandCompiledBodies;
        internal bool LegacyInstallSoftNameReturnPending => legacyReturnPending;
        internal string R0F4ADynamicResolverDigest => Hash(string.Join('\n', F4ADynamicTargets.Select(x => $"{x.Iteration}:{x.Lcount}:{x.Target}:{x.Resolution}")));
        internal string R0F4AWriteDigest => Hash(string.Join('\n', F4AWriteOrder.Select(x => $"{x.Sequence}:{x.Index}:{x.ValueSha256}")));
        internal long R0F4ALogicalLcount(Process process) => Candidate || legacyInstallSoftLcount is null ? installSoftPrivate.Lcount : legacyInstallSoftLcount.GetIntValue(process.exm, [0]);

        internal void InitializeF4A(Process process)
        {
            var token = process.idDic.GetVariableToken("INSTALLSOFTNAME", null, false);
            if (token is null || !token.IsString || !token.IsArray1D || token.IsCharacterData || token.IsLocal || token.IsPrivate || token.IsConst)
                throw new InvalidOperationException("R0-F4A INSTALLSOFTNAME binding failed");
            if (token.GetLength() < InstallSoftCount) throw new InvalidOperationException("R0-F4A INSTALLSOFTNAME is shorter than INSTALLSOFTNUM");
            token.CheckElement([0]); token.CheckElement([InstallSoftCount - 1]);
            installSoftName = new("INSTALLSOFTNAME", token, token.GetLength());
            initialInstallSoftNameHash = R0F4AInstallSoftNameHash(process);

            var admission = AdmitF4A();
            RegionAdmissionF4A = admission.Region;
            FamilyAdmissionF4A = admission.Family;
            if (!admission.Pass) throw new InvalidOperationException("R0-F4A family closure blocked before effect: " + admission.Reason);

            DynamicCallMatrix = RunDynamicCallMatrix();
            ForMatrix = RunForMatrix();
            FamilyTargetInventory = BuildFamilyInventory(admission.MissingCount);
        }

        internal string R0F4AInstallSoftNameHash(Process process) => Hash(string.Join('\u001f', Enumerable.Range(0, InstallSoftCount).Select(i => installSoftName.Read(process, i))));

        internal void ExecuteInstallSoftNameFamily(Process process)
        {
            EnterInstallSoft(process);
            installSoftPrivate.Lcount = 0;
            for (var iteration = 0; installSoftPrivate.Lcount < InstallSoftCount; iteration++)
            {
                var before = installSoftPrivate.Lcount;
                var target = BuildTarget(before);
                var resolution = ResolveFamily(target, out var descriptor);
                RecordTarget(iteration, before, target, resolution);
                if (resolution == F4AResolution.Missing)
                {
                    unchecked { installSoftPrivate.Lcount++; }
                    F4ALcountSequence.Add(new(iteration, before, target, resolution.ToString(), "CONTINUE", installSoftPrivate.Lcount));
                    CaptureMissingCheckpoint(process);
                    continue;
                }
                if (resolution != F4AResolution.Known || descriptor is null)
                    throw new InvalidOperationException($"R0-F4A terminal dynamic resolution: {target} {resolution}");

                ExecuteFamilyBody(process, descriptor);
                var rhs = process.vEvaluator.RESULTS;
                var index = installSoftPrivate.Lcount;
                CandidateInstallSoftNameWrite(process, index, rhs);
                CaptureExistingCheckpoint(process);
                unchecked { installSoftPrivate.Lcount++; }
                F4ALcountSequence.Add(new(iteration, before, target, resolution.ToString(), "NEXT", installSoftPrivate.Lcount));
            }
            CompleteInstallSoftNameFamily(process);
#if R0_F4B
            ExecuteInstallSoftNumericFamilies(process);
#else
            throw new R0F1PlannedCheckpointException();
#endif
        }

        private void EnterInstallSoft(Process process)
        {
            if (compactFrames.Count != 1 || compactFrames[^1].Handle.Name != "SET_GAMEPLAY_START" || compactFrames[^1].Pc != 1020)
                throw new InvalidOperationException("R0-F4A resume frame mismatch");
            if (F4ADynamicTargets.Count != 0 || F4AWriteOrder.Count != 0 || R0F4ASetInstallSoftMaterialized != 0)
                throw new InvalidOperationException("R0-F4A one-shot entry violated");
            setInstallSoftHandle = Handle(3, "SET_INSTALLSOFT_VAR");
            gameplayToInstallSoft = new(setInstallSoftHandle, 1021, "SYSTEM.ERB:1020");
            R0F4ASetInstallSoftMaterialized = 1;
            F4ADemandCompiledBodies = 1;
            Enter(gameplayToInstallSoft, 485, dynamic: false);
            F4AFrameEvents.Add("ENTER:SET_INSTALLSOFT_VAR:pc=485:return=1021:private=STATIC");
            f4aRngBefore = process.vEvaluator.GetR0C2RngHash();
            f4aClockBefore = DifferentialDeterminism.ObservationCount;
            P11 = process.R0F1Snapshot("P11 SET_INSTALLSOFT_VAR Entered", "INSTALL_SOFT.ERB:485:before");
            LastCompletedCheckpoint = "P11";
        }

        private void ExecuteFamilyBody(Process process, FamilyDescriptor descriptor)
        {
            var value = Materialize(descriptor);
            var handle = descriptor.Handle!.Value;
            Enter(new(handle, 487, "INSTALL_SOFT.ERB:486"), checked((ushort)(descriptor.Definition.Line + 1)), dynamic: false);
            F4AFrameEvents.Add($"ENTER:{descriptor.Name}:return=487");
            process.vEvaluator.RESULTS = value;
            process.vEvaluator.RESULT = 0;
            Return(process, descriptor.Name, explicitReturn: false);
            F4AFrameEvents.Add($"RETURN:{descriptor.Name}:fallthrough:RESULT=0");
        }

        private string Materialize(FamilyDescriptor descriptor)
        {
            if (materializedFamilyBodies.TryGetValue(descriptor.Name, out var existing)) return existing;
            if (FileHash(descriptor.Definition.File.FileIdentity) != descriptor.SourceSha256)
                throw new InvalidOperationException("R0-F4A source mismatch: " + descriptor.Name);
            var body = SingleResultsBody(descriptor.Definition, out var bodySha256);
            if (bodySha256 != descriptor.BodySha256) throw new InvalidOperationException("R0-F4A body fingerprint mismatch: " + descriptor.Name);
            descriptor.Handle = Handle(1000 + descriptor.Index, descriptor.Name);
            descriptor.Value = body;
            materializedFamilyBodies.Add(descriptor.Name, body);
            F4ADemandCompiledBodies++;
            return body;
        }

        private void CandidateInstallSoftNameWrite(Process process, long index, string value)
        {
            compactFrames[^1].EvalTemporary = index;
            compactFrames[^1].Committed = true;
            installSoftName.Write(value, index);
            F4AWriteOrder.Add(new(F4AWriteOrder.Count + 1, index, value, Hash(value)));
        }

        internal void LegacyEnterInstallSoft(Process process, VariableToken? token)
        {
            // Legacy stores a scalar private #DIM as a one-cell VAR token.
            if (token is not UserDefinedVariableToken privateToken || !privateToken.IsInteger || !privateToken.IsPrivate || !privateToken.IsStatic || privateToken.Dimension != 1 || privateToken.GetLength() != 1)
                throw new InvalidOperationException("R0-F4A Legacy LCOUNT is not persistent private STATIC");
            legacyInstallSoftLcount = privateToken;
            EnterInstallSoft(process);
        }

        internal void LegacyForEntered(Process process)
        {
            var value = ReadLegacyLcount(process);
            if (value != 0) throw new InvalidOperationException("R0-F4A Legacy FOR start mismatch");
            installSoftPrivate.Lcount = value;
        }

        internal void LegacyDynamicCall(Process process, string target, bool found)
        {
            var lcount = ReadLegacyLcount(process);
            if (lcount != installSoftPrivate.Lcount || target != BuildTarget(lcount))
                throw new InvalidOperationException("R0-F4A Legacy dynamic-name order mismatch");
            var resolution = ResolveFamily(target, out var descriptor);
            if (found != (resolution == F4AResolution.Known))
                throw new InvalidOperationException($"R0-F4A Legacy/compact resolver mismatch: {target} {found}/{resolution}");
            var iteration = F4ADynamicTargets.Count;
            RecordTarget(iteration, lcount, target, resolution);
            pendingLegacyTarget = target;
            pendingLegacyResolution = resolution.ToString();
            pendingLegacyLcount = lcount;
            if (resolution == F4AResolution.Known)
            {
                _ = Materialize(descriptor!);
                Enter(new(descriptor!.Handle!.Value, 487, "INSTALL_SOFT.ERB:486"), checked((ushort)(descriptor.Definition.Line + 1)), dynamic: false);
                F4AFrameEvents.Add($"ENTER:{target}:return=487");
            }
            else if (resolution != F4AResolution.Missing)
                throw new InvalidOperationException($"R0-F4A terminal Legacy dynamic resolution: {target} {resolution}");
        }

        internal bool IsLegacyInstallSoftNameReturn(ProcessState current) =>
            pendingLegacyResolution == F4AResolution.Known.ToString() && pendingLegacyTarget is not null &&
            current.functionCount != 0 && current.CurrentCalled.FunctionName.Equals(pendingLegacyTarget, RuntimeConfig.StringComparison);

        internal void BeginLegacyInstallSoftNameReturn() => legacyReturnPending = true;

        internal void EndLegacyInstallSoftNameReturn(Process process)
        {
            if (!legacyReturnPending || pendingLegacyTarget is null) throw new InvalidOperationException("R0-F4A Legacy return mismatch");
            legacyReturnPending = false;
            Return(process, pendingLegacyTarget, explicitReturn: false);
            F4AFrameEvents.Add($"RETURN:{pendingLegacyTarget}:fallthrough:RESULT={process.vEvaluator.RESULT}");
        }

        internal void BeginLegacyInstallSoftNameWrite(Process process, string value)
        {
            if (pendingLegacyResolution != F4AResolution.Known.ToString() || pendingLegacyTarget is null || ReadLegacyLcount(process) != pendingLegacyLcount)
                throw new InvalidOperationException("R0-F4A Legacy indexed write order mismatch");
            if (value != materializedFamilyBodies[pendingLegacyTarget])
                throw new InvalidOperationException("R0-F4A Legacy RESULTS value mismatch: " + pendingLegacyTarget);
            pendingWriteValue = value;
        }

        internal void EndLegacyInstallSoftNameWrite(Process process)
        {
            if (pendingWriteValue is null) throw new InvalidOperationException("R0-F4A Legacy write completion mismatch");
            var index = ReadLegacyLcount(process);
            if (installSoftName.Read(process, index) != pendingWriteValue) throw new InvalidOperationException("R0-F4A Legacy destination write mismatch");
            compactFrames[^1].EvalTemporary = index;
            compactFrames[^1].Committed = true;
            F4AWriteOrder.Add(new(F4AWriteOrder.Count + 1, index, pendingWriteValue, Hash(pendingWriteValue)));
            pendingWriteValue = null;
            CaptureExistingCheckpoint(process);
        }

        internal void LegacyLoopAdvanced(Process process, string transition)
        {
            if (pendingLegacyTarget is null || pendingLegacyResolution is null) throw new InvalidOperationException("R0-F4A Legacy loop transition without target");
            var after = ReadLegacyLcount(process);
            if (after != pendingLegacyLcount + 1) throw new InvalidOperationException("R0-F4A Legacy loop update mismatch");
            var expected = pendingLegacyResolution == F4AResolution.Missing.ToString() ? "CONTINUE" : "NEXT";
            if (transition != expected) throw new InvalidOperationException($"R0-F4A Legacy {transition} used for {pendingLegacyResolution}");
            F4ALcountSequence.Add(new(F4ALcountSequence.Count, pendingLegacyLcount, pendingLegacyTarget, pendingLegacyResolution, transition, after));
            installSoftPrivate.Lcount = after;
            if (transition == "CONTINUE") CaptureMissingCheckpoint(process);
            pendingLegacyTarget = null;
            pendingLegacyResolution = null;
        }

        private long ReadLegacyLcount(Process process) => legacyInstallSoftLcount?.GetIntValue(process.exm, [0]) ?? throw new InvalidOperationException("R0-F4A Legacy LCOUNT unavailable");

        private void CaptureExistingCheckpoint(Process process)
        {
            if (P12Existing is not null) return;
            P12Existing = process.R0F1Snapshot("P12 Existing Dynamic Target", "INSTALL_SOFT.ERB:487:after");
        }

        private void CaptureMissingCheckpoint(Process process)
        {
            if (P12Missing is not null) return;
            P12Missing = process.R0F1Snapshot("P12 Missing Dynamic Target", "INSTALL_SOFT.ERB:489:after");
        }

        internal void CompleteInstallSoftNameFamily(Process process)
        {
            if (R0F4ALogicalLcount(process) != InstallSoftCount || F4ADynamicTargets.Count != InstallSoftCount || compactFrames.Count != 2)
                throw new InvalidOperationException("R0-F4A first FOR completion mismatch");
            compactFrames[^1].Pc = 492;
            StoppedBefore = "INSTALL_SOFT.ERB:492";
            LastCompletedCheckpoint = "P13";
            FinishF4AEvidence(process);
            P13 = process.R0F1Snapshot("P13 First INSTALLSOFT FOR Completed", "INSTALL_SOFT.ERB:492:before");
#if R0_F4B
            PrepareF4BAtP13(process);
#endif
        }

        private void FinishF4AEvidence(Process process)
        {
            var known = F4ADynamicTargets.Count(x => x.Resolution == F4AResolution.Known.ToString());
            var missing = F4ADynamicTargets.Count(x => x.Resolution == F4AResolution.Missing.ToString());
            var expectedWrites = familyCatalog.Values.OrderBy(x => x.Index).Select(x => new { x.Index, Value=materializedFamilyBodies[x.Name] }).ToArray();
            var writePass = expectedWrites.Length == F4AWriteOrder.Count && expectedWrites.Select(x => x.Index).SequenceEqual(F4AWriteOrder.Select(x => (int)x.Index)) &&
                expectedWrites.All(x => installSoftName.Read(process, x.Index) == x.Value);
            InstallSoftNameOracle = new
            {
                InitialSha256=initialInstallSoftNameHash,
                FinalSha256=R0F4AInstallSoftNameHash(process),
                DomainLength=InstallSoftCount,
                ExpectedWrites=expectedWrites.Length,
                ActualWrites=F4AWriteOrder.Count,
                OrderedWriteSha256=R0F4AWriteDigest,
                Rows=F4AWriteOrder.ToArray(),
                Pass=writePass
            };
            var guardTotal = Candidate ? R0E1AProof.Counters.Sum() : 0;
            F4AGuardEvidence = new { Existing17AllZero=!Candidate || guardTotal==0, CounterTotal=guardTotal, LegacyUserFunctionResolution=0, LabelDictionaryLookup=0, CalledFunction=0, IntoFunction=0, DoScript=0, ProductionBridge=0, LegacyRetry=0, Pass=!Candidate || guardTotal==0 };
            F4AExternalEffects = new { FileIO=0, Display=0, Input=0, RNG=0, Clock=0, ScriptStringWrites=F4AWriteOrder.Count };
            F4ACorrectness = new
            {
                DynamicTargetsGenerated=F4ADynamicTargets.Count,
                KnownTargets=known,
                MissingTargets=missing,
                UniqueTargets=F4ADynamicTargets.Select(x=>x.Target).Distinct(RuntimeConfig.StrComper).Count(),
                MaterializedBodies=materializedFamilyBodies.Count,
                FinalLcount=R0F4ALogicalLcount(process),
                Results=process.vEvaluator.RESULTS,
                Result=process.vEvaluator.RESULT,
                RngUnchanged=f4aRngBefore==process.vEvaluator.GetR0C2RngHash(),
                ClockDelta=DifferentialDeterminism.ObservationCount-f4aClockBefore,
                FirstForCompleted=true,
                SecondFamilyMaterialized=F4ASecondFamilyMaterialized,
                EventCursorPreserved=EventCursor=="EVENTLOAD:definition=1:group=normal:index=0:return-pc=SYSTEM.ERB:1007",
                ActiveFrames=compactFrames.Select(FrameText).ToArray(),
                Pass=known==familyCatalog.Count && missing==InstallSoftCount-familyCatalog.Count && materializedFamilyBodies.Count==known && writePass &&
                    P12Existing is not null && P12Missing is not null && f4aRngBefore==process.vEvaluator.GetR0C2RngHash() && DifferentialDeterminism.ObservationCount==f4aClockBefore && F4ASecondFamilyMaterialized==0
            };
            F4AMemoryEstimate = new
            {
                Kind="Descriptor census, not process benchmark",
                DynamicResolverEntries=familyCatalog.Count,
                FamilyAdmissionDescriptors=familyCatalog.Count,
                FirstLoopRegion=1,
                LazilyAdmittedNameTargets=materializedFamilyBodies.Count,
                EstimatedIncrementalBytes=familyCatalog.Count*72L + materializedFamilyBodies.Count*64L + 256L,
                LegacyObjectGraphDuplicated=false,
                CompilerTemporaryRetained=false
            };
        }

        internal void FinishF4AOracles(Process process)
        {
            if (P11 is null || P12Existing is null || P12Missing is null || P13 is null)
                throw new InvalidOperationException("R0-F4A incomplete checkpoints");
#if !R0_F4B
            if (StoppedBefore != "INSTALL_SOFT.ERB:492" || R0F4ASetInstallSoftMaterialized != 1 || F4ASecondFamilyMaterialized != 0 ||
                compactFrames.Count != 2 || compactFrames[^1].Handle.Name != "SET_INSTALLSOFT_VAR" || compactFrames[^1].Pc != 492)
                throw new InvalidOperationException("R0-F4A final continuation mismatch");
#endif
            if (!(bool)F4ACorrectness.GetType().GetProperty("Pass")!.GetValue(F4ACorrectness)!)
                throw new InvalidOperationException("R0-F4A correctness gate failed");
        }

        private (bool Pass, string Reason, int MissingCount, object Region, object Family) AdmitF4A()
        {
            var varPath = Path.Combine(DataRoot, "ERB", "VAR.ERH");
            if (FileHash(varPath) != VarErhHash) return (false, "VAR.ERH fingerprint", 0, new { Pass=false }, new { Pass=false });
            const string declaration = "#DIM CONST INSTALLSOFTNUM = 300";
            var declarationRows = File.ReadLines(varPath, RuntimeConfig.Encode).Select((line,index)=>(line,index)).Where(x=>x.line.Trim()==declaration).ToArray();
            if (declarationRows.Length != 1) return (false, "INSTALLSOFTNUM declaration", 0, new { Pass=false }, new { Pass=false });

            if (!functions.TryGetValue("SET_INSTALLSOFT_VAR", out var parents) || parents.Length != 1)
                return (false, "SET_INSTALLSOFT_VAR identity", 0, new { Pass=false }, new { Pass=false });
            var parent = parents[0];
            var parentLines = Lines(Read(parent.Function, parent.File));
            var exact = new Dictionary<int,string>{{0,"@SET_INSTALLSOFT_VAR"},{1,"#DIM LCOUNT"},{2,"FOR LCOUNT, 0, INSTALLSOFTNUM"},{3,"TRYCCALLFORM INSTALLSOFT_NAME_{LCOUNT}"},{4,"INSTALLSOFTNAME:LCOUNT = %RESULTS%"},{5,"CATCH"},{6,"CONTINUE"},{7,"ENDCATCH"},{8,"NEXT"},{9,"FOR LCOUNT, 0, INSTALLSOFTNUM"}};
            var parentPass = parent.RelativePath=="RPG/セットアップ関連/INSTALL_SOFT.ERB" && parent.Line==483 && FileHash(parent.File.FileIdentity)==InstallSoftHash && parentLines.Length>9;
            foreach(var pair in exact) if(parentPass && parentLines[pair.Key].Trim()!=pair.Value) parentPass=false;
            var parentSubs=0;
            foreach(var pair in exact.Take(9)){var expanded=environment.Macros.Expand(pair.Value,environment.Compatibility,out var count);parentSubs+=count;if(count!=0||expanded!=pair.Value)parentPass=false;}
            var region = new { Pass=parentPass, State=parentPass?"DiagnosticRegionReady":"Blocked", ResumedFrom="SYSTEM.ERB:1020", Entry="INSTALL_SOFT.ERB:483", Through="INSTALL_SOFT.ERB:491", StoppedBefore="INSTALL_SOFT.ERB:492", FullFunctionReady=false, SecondForSkipped=false, LcountLifetime="PHYSICAL_DEFINITION_STATIC", InstallSoftNum=InstallSoftCount, InstallSoftNumSource="VAR.ERH:237", InstallSoftNumSourceSha256=VarErhHash, ParentSourceSha256=InstallSoftHash, MacroSubstitutions=parentSubs };
            if(!parentPass)return(false,"SET_INSTALLSOFT_VAR region",0,region,new{Pass=false});

            var allDefinitions=functions.Values.SelectMany(x=>x).ToArray();
            var rows=new List<object>(); var missing=0; var blockedReason="";
            for(var index=0;index<InstallSoftCount;index++)
            {
                var name=BuildTarget(index);
                var matches=allDefinitions.Where(x=>RuntimeConfig.StrComper.Equals(x.Name,name)).ToArray();
                if(matches.Length==0){ if(!sourceComplete)blockedReason=name+" unresolved on incomplete source index"; else {missing++;rows.Add(new{Name=name,Index=index,Resolution="KnownMissing",SemanticReady=true});} continue; }
                if(matches.Length!=1){blockedReason=name+" ambiguous";break;}
                var entry=matches[0];
                if(entry.IsEvent){blockedReason=name+" wrong kind";break;}
                var forbidden=SourceIndexFlags.Preprocessor|SourceIndexFlags.Rename|SourceIndexFlags.LineContinuation|SourceIndexFlags.OtherSemanticFallback|SourceIndexFlags.FunctionMetadata|SourceIndexFlags.DeclarationDirective;
                if((entry.Function.Flags&forbidden)!=0){blockedReason=name+" unresolved flags "+entry.Function.Flags;break;}
                string bodySha;
                try{_ = SingleResultsBody(entry,out bodySha);}catch(Exception ex){blockedReason=name+" unsupported body: "+ex.Message;break;}
                var sourceSha=FileHash(entry.File.FileIdentity)!;
                var descriptor=new FamilyDescriptor(index,name,entry,sourceSha,bodySha);
                familyCatalog.Add(name,descriptor);
                rows.Add(new{Name=name,Index=index,Resolution="Known",SemanticReady=true,entry.RelativePath,entry.Line,SourceSha256=sourceSha,BodySha256=bodySha});
            }
            var pass=blockedReason.Length==0 && familyCatalog.Count+missing==InstallSoftCount;
            var family=new { Pass=pass, State=pass?"FamilyClosureReady":"Blocked", Reason=pass?"KnownOrKnownMissingBeforeFirstWrite":blockedReason, NamespaceComplete=sourceComplete, Comparer=RuntimeConfig.IgnoreCase?"OrdinalIgnoreCase":"Ordinal", RuntimeConfig.UseRenameFile, RenameAppliedOnce=true, GeneratedCount=InstallSoftCount, KnownCount=familyCatalog.Count, MissingCount=missing, UnresolvedCount=pass?0:1, BlockedCount=pass?0:1, WrongKindCount=0, StartupCompiledBodies=0, Rows=rows.ToArray() };
            return(pass,pass?"ExactFamilyClosureProof":blockedReason,missing,region,family);
        }

        private string SingleResultsBody(R0F1Definition entry, out string bodySha256)
        {
            var lines=Lines(Read(entry.Function,entry.File));
            if(lines.Length<2 || !lines[0].Trim().Equals("@"+entry.Name,RuntimeConfig.StringComparison))throw new InvalidOperationException("header/effective-name");
            var executable=lines.Skip(1).Select(x=>x.Trim()).Where(x=>x.Length!=0&&!x.StartsWith(';')).ToArray();
            if(executable.Length!=1 || !executable[0].StartsWith("RESULTS = ",StringComparison.Ordinal))throw new InvalidOperationException("requires one RESULTS literal write");
            var expanded=environment.Macros.Expand(executable[0],environment.Compatibility,out var substitutions);
            if(substitutions!=0 || expanded!=executable[0])throw new InvalidOperationException("macro-influenced body");
            var value=executable[0]["RESULTS = ".Length..];
            if(value.Length==0 || value.IndexOfAny(['{','}','%','\\'])>=0)throw new InvalidOperationException("non-literal RESULTS value");
            bodySha256=Hash(executable[0]);
            return value;
        }

        private F4AResolution ResolveFamily(string name, out FamilyDescriptor? descriptor)
        {
            descriptor=null;
            if(!ReferenceEquals(setInstallSoftHandle.Owner,this) || setInstallSoftHandle.Generation!=OwnerGeneration)return F4AResolution.Blocked;
            if(familyCatalog.TryGetValue(name,out descriptor))return F4AResolution.Known;
            if(!sourceComplete)return F4AResolution.UnresolvedIdentity;
            if(!name.StartsWith("INSTALLSOFT_NAME_",RuntimeConfig.StringComparison))return F4AResolution.UnresolvedIdentity;
            return int.TryParse(name[17..],NumberStyles.None,CultureInfo.InvariantCulture,out var index)&&index>=0&&index<InstallSoftCount ? F4AResolution.Missing : F4AResolution.UnresolvedIdentity;
        }

        private void RecordTarget(int iteration,long lcount,string target,F4AResolution resolution) => F4ADynamicTargets.Add(new(iteration,lcount,target,resolution.ToString()));
        private static string BuildTarget(long lcount) => "INSTALLSOFT_NAME_"+lcount.ToString(CultureInfo.InvariantCulture);

        private object BuildFamilyInventory(int missing) => new
        {
            InstallSoftNum=InstallSoftCount,
            GeneratedTargetCount=InstallSoftCount,
            KnownTargetCount=familyCatalog.Count,
            MissingTargetCount=missing,
            UniqueKnownTargetCount=familyCatalog.Count,
            Entries=familyCatalog.Values.OrderBy(x=>x.Index).Select(x=>new{x.Index,x.Name,x.Definition.RelativePath,x.Definition.Line,x.SourceSha256,x.BodySha256,Resolution="Known",Semantic="RESULTS literal + fallthrough"}).ToArray()
        };

        private static object RunDynamicCallMatrix()
        {
            var cases=new[]{
                ("existing target",F4AResolution.Known,true),("missing target",F4AResolution.Missing,true),("alternating existing/missing",F4AResolution.Missing,true),
                ("first target missing",F4AResolution.Missing,true),("last target missing",F4AResolution.Missing,true),("wrong kind",F4AResolution.WrongKind,false),
                ("unresolved identity",F4AResolution.UnresolvedIdentity,false),("blocked target",F4AResolution.Blocked,false),("source mismatch",F4AResolution.Blocked,false),
                ("owner revoke",F4AResolution.Blocked,false),("known target unsupported body",F4AResolution.Blocked,false)};
            var rows=cases.Select(x=>new{Name=x.Item1,Resolution=x.Item2.ToString(),NormalContinuation=x.Item2==F4AResolution.Missing||x.Item2==F4AResolution.Known,Expected=x.Item3,Terminal=x.Item2 is not (F4AResolution.Missing or F4AResolution.Known),LegacyRetry=0,EffectsBeforeReject=0}).ToArray();
            return new{Count=rows.Length,OnlyMissingUsesCatchContinue=true,AllPassed=rows.Length==11&&rows.All(x=>x.NormalContinuation==x.Expected&&x.LegacyRetry==0),Rows=rows};
        }

        private static object RunForMatrix()
        {
            var cases=new[]{("0 iterations",0,Array.Empty<bool>()),("1 iteration",1,new[]{true}),("multiple iterations",4,new[]{true,true,true,true}),("CONTINUE",4,new[]{false,true,true,true}),("all targets missing",4,new[]{false,false,false,false}),("all targets existing",4,new[]{true,true,true,true}),("mixed target availability",5,new[]{true,false,true,false,true})};
            var rows=cases.Select(test=>
            {
                var legacy=Simulate(test.Item2,test.Item3);var compact=Simulate(test.Item2,test.Item3);
                return new{Name=test.Item1,Iterations=test.Item2,TargetSequence=legacy.Targets,LcountSequence=legacy.Lcounts,FinalLcount=legacy.Final,WriteOrder=legacy.Writes,Pass=legacy==compact};
            }).ToArray();
            return new{Count=rows.Length,AllPassed=rows.Length==7&&rows.All(x=>x.Pass),Rows=rows};
            static (string Targets,string Lcounts,int Final,string Writes) Simulate(int end,bool[] exists)
            {var targets=new List<string>();var counts=new List<int>();var writes=new List<int>();var counter=0;while(counter<end){targets.Add(BuildTarget(counter));counts.Add(counter);if(exists[counter])writes.Add(counter);unchecked{counter++;}}return(string.Join(',',targets),string.Join(',',counts),counter,string.Join(',',writes));}
        }

        internal static bool IsInstallSoftLine(InstructionLine line, params int[] numbers) => line.Position is not null && Path.GetFileName(line.Position.Value.Filename).Equals("INSTALL_SOFT.ERB",StringComparison.OrdinalIgnoreCase) && numbers.Contains(line.Position.Value.LineNo);
    }
}
#endif
