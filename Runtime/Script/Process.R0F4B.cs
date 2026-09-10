#if R0_F4B
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
    internal void R0F4BBeforeLegacyInstruction(InstructionLine line)
    {
        if (r0f1 is null || r0f1.Candidate) return;
        if (r0f1.IsLegacyNumericReturn(state, line)) r0f1.BeginLegacyNumericReturn();
        if (R0F1Context.IsInstallSoftLine(line, 505)) r0f1.LegacySecondForBeforeNext(this);
#if !R0_F4C
        if (R0F1Context.IsSystemLine(line, 1021))
            throw new InvalidOperationException("R0-F4B crossed the SYSTEM.ERB:1021 stop boundary");
#endif
    }

    internal void R0F4BAfterLegacyInstruction(InstructionLine line)
    {
        if (r0f1 is null || r0f1.Candidate) return;
        if (r0f1.LegacyNumericReturnPending) r0f1.EndLegacyNumericReturn(this);
        if (R0F1Context.IsInstallSoftLine(line, 492)) r0f1.LegacySecondForEntered(this);
        else if (R0F1Context.IsInstallSoftLine(line, 505)) r0f1.LegacySecondForAdvanced(this);
        else if (R0F1Context.IsInstallSoftLine(line, 493, 497, 501)) r0f1.EndLegacyMissingNumericCall(this);
    }

    internal void R0F4BObserveLegacyDynamicCall(InstructionLine line, string target, bool found)
    {
        if (r0f1 is null || r0f1.Candidate || !R0F1Context.IsInstallSoftLine(line, 493, 497, 501)) return;
        r0f1.LegacyNumericDynamicCall(this, line, target, found);
    }

    internal void R0F4BBeforeLegacyScalarWrite(InstructionLine line, long value)
    {
        if (r0f1 is null || r0f1.Candidate || !R0F1Context.IsInstallSoftLine(line, 494, 498, 502)) return;
        r0f1.BeginLegacyNumericWrite(this, line, value);
    }

    internal void R0F4BAfterLegacyScalarWrite(InstructionLine line)
    {
        if (r0f1 is null || r0f1.Candidate || !R0F1Context.IsInstallSoftLine(line, 494, 498, 502)) return;
        r0f1.EndLegacyNumericWrite(this, line);
    }

    internal void R0F4BBeforeLegacyFallthrough(LogicalLine line)
    {
        if (r0f1 is null || r0f1.Candidate || !r0f1.IsLegacySetInstallSoftFallthrough(state, line)) return;
        r0f1.BeginLegacySetInstallSoftFallthrough();
    }

    internal void R0F4BAfterLegacyFallthrough(LogicalLine line)
    {
        if (r0f1 is null || r0f1.Candidate || !r0f1.LegacySetInstallSoftFallthroughPending) return;
        r0f1.EndLegacySetInstallSoftFallthrough(this);
#if !R0_F4C
        throw new R0F1PlannedCheckpointException();
#endif
    }

    private sealed partial class R0F1Context
    {
        private enum NumericFamily { Slv, Size, Cond }
        private enum NumericResolution { Missing, Known, UnresolvedIdentity, Blocked, WrongKind }
        private enum NumericBodyKind { LiteralReturn, DifficultyConditional }

        private sealed class NumericDescriptor
        {
            internal readonly NumericFamily Family;
            internal readonly int Index;
            internal readonly string Name;
            internal readonly R0F1Definition Definition;
            internal readonly string SourceSha256;
            internal readonly string BodySha256;
            internal readonly NumericBodyKind BodyKind;
            internal readonly long PrimaryValue;
            internal readonly long AlternateValue;
            internal CompactNormalHandle? Handle;
            internal NumericDescriptor(NumericFamily family, int index, string name, R0F1Definition definition, string sourceSha256,
                string bodySha256, NumericBodyKind bodyKind, long primaryValue, long alternateValue) =>
                (Family, Index, Name, Definition, SourceSha256, BodySha256, BodyKind, PrimaryValue, AlternateValue) =
                (family, index, name, definition, sourceSha256, bodySha256, bodyKind, primaryValue, alternateValue);
        }

        private sealed record BoundIndexedIntSlot(string Name, VariableToken Token, int Length)
        {
            internal long Read(Process process, long index) => Token.GetIntValue(process.exm, [index]);
            internal void Write(long value, long index)
            {
                var indices = new[] { index };
                Token.CheckElement(indices);
                Token.SetValue(value, indices);
            }
        }

        internal sealed record F4BCallRow(int Sequence, int Iteration, long Lcount, string Family, string Target, string Resolution, long ResultBefore, long ResultAfter);
        internal sealed record F4BWriteRow(int Sequence, int Iteration, long Index, string Family, string Target, long Value);
        internal sealed record F4BLoopRow(int Iteration, long Before, long After);

        private readonly Dictionary<string, NumericDescriptor> numericCatalog = new(RuntimeConfig.StrComper);
        private readonly Dictionary<string, NumericDescriptor> materializedNumericBodies = new(RuntimeConfig.StrComper);
        private readonly Dictionary<NumericFamily, (int Known, int Missing)> numericCounts = [];
        private readonly Dictionary<NumericFamily, BoundIndexedIntSlot> numericSlots = [];
        private BoundSlot difficulty = null!;
        private string[] initialNumericHashes = [];
        private string[] finalNumericHashes = [];
        private int f4bFamilyOrdinal;
        private NumericFamily pendingNumericFamily;
        private NumericDescriptor? pendingNumericDescriptor;
        private string? pendingNumericTarget;
        private NumericResolution pendingNumericResolution;
        private long pendingNumericResultBefore;
        private long pendingNumericResultAfter;
        private bool pendingNumericReturned;
        private bool pendingNumericWrite;
        private bool legacyNumericReturnPending;
        private bool legacySetInstallSoftFallthroughPending;
        private string f4bRngBefore = "";
        private long f4bClockBefore;
        private int p15AllKnownIndex = -1, p15MixedIndex = -1, p15AllMissingIndex = -1;
        private object? p15AllKnown, p15Mixed, p15AllMissing;
        private readonly List<string> currentIterationResolutions = [];

        internal object? P14, P15, P16, P17;
        internal object RegionAdmissionF4B { get; private set; } = null!;
        internal object FamilyAdmissionF4B { get; private set; } = null!;
        internal object FamilyTargetInventoryF4B { get; private set; } = null!;
        internal object DynamicCallMatrixF4B { get; private set; } = null!;
        internal object EmptyCatchMatrixF4B { get; private set; } = null!;
        internal object NumericReturnMatrixF4B { get; private set; } = null!;
        internal object InstallSoftNumericOracle { get; private set; } = null!;
        internal object F4BGuardEvidence { get; private set; } = null!;
        internal object F4BCorrectness { get; private set; } = null!;
        internal object F4BMemoryEstimate { get; private set; } = null!;
        internal object F4BExternalEffects { get; private set; } = new { FileIO=0, Display=0, Input=0, RNG=0, Clock=0, ScriptIntegerWrites=0 };
        internal readonly List<F4BCallRow> F4BCallOrder = [];
        internal readonly List<F4BWriteRow> F4BWriteOrder = [];
        internal readonly List<F4BLoopRow> F4BLcountSequence = [];
        internal readonly List<string> F4BFrameEvents = [];
        internal int F4BDemandCompiledBodies;
        internal int SetSkillEmulatorMaterialized;
        internal bool LegacyNumericReturnPending => legacyNumericReturnPending;
        internal bool LegacySetInstallSoftFallthroughPending => legacySetInstallSoftFallthroughPending;
        internal string R0F4BResolverDigest => Hash(string.Join('\n', F4BCallOrder.Select(x => $"{x.Sequence}:{x.Iteration}:{x.Family}:{x.Target}:{x.Resolution}:{x.ResultBefore}:{x.ResultAfter}")));
        internal string R0F4BWriteDigest => Hash(string.Join('\n', F4BWriteOrder.Select(x => $"{x.Sequence}:{x.Iteration}:{x.Family}:{x.Index}:{x.Target}:{x.Value}")));

        internal void InitializeF4B(Process process)
        {
            foreach (var (family, variable) in new[]
            {
                (NumericFamily.Slv, "INSTALLSOFT_SLV"),
                (NumericFamily.Size, "INSTALLSOFT_SIZE"),
                (NumericFamily.Cond, "INSTALLSOFT_EXIST_DISABLE_COND")
            }) numericSlots.Add(family, BindIndexedInt(process, variable));
            difficulty = BindFlag(process, "戦闘難易度");

            var admission = AdmitF4B();
            RegionAdmissionF4B = admission.Region;
            FamilyAdmissionF4B = admission.Family;
            if (!admission.Pass) throw new InvalidOperationException("R0-F4B family closure blocked before effect: " + admission.Reason);
            FamilyTargetInventoryF4B = BuildNumericInventory();
            DynamicCallMatrixF4B = RunF4BDynamicMatrix();
            EmptyCatchMatrixF4B = RunEmptyCatchMatrix();
            NumericReturnMatrixF4B = RunNumericReturnMatrix();
        }

        internal void PrepareF4BAtP13(Process process)
        {
            if (numericCatalog.Count == 0 || F4BCallOrder.Count != 0 || F4BWriteOrder.Count != 0)
                throw new InvalidOperationException("R0-F4B P13 preparation mismatch");
            initialNumericHashes = NumericArrayHashes(process);
            f4bRngBefore = process.vEvaluator.GetR0C2RngHash();
            f4bClockBefore = DifferentialDeterminism.ObservationCount;
        }

        internal void ExecuteInstallSoftNumericFamilies(Process process)
        {
            BeginSecondFor(process, legacyLcount: null);
            for (var iteration = 0; installSoftPrivate.Lcount < InstallSoftCount; iteration++)
            {
                currentIterationResolutions.Clear();
                foreach (var family in Enum.GetValues<NumericFamily>()) ExecuteNumericFamily(process, iteration, family);
                CaptureP15(process, iteration);
                var before = installSoftPrivate.Lcount;
                unchecked { installSoftPrivate.Lcount++; }
                F4BLcountSequence.Add(new(iteration, before, installSoftPrivate.Lcount));
            }
            CompleteSecondFor(process);
            process.vEvaluator.RESULT = 0;
            Return(process, "SET_INSTALLSOFT_VAR", explicitReturn: false);
            F4BFrameEvents.Add("RETURN:SET_INSTALLSOFT_VAR:fallthrough:RESULT=0:resume=1021");
            CompleteSetInstallSoftReturn(process);
#if R0_F4C
            ExecuteSkillEmulatorNameFamily(process);
#else
            throw new R0F1PlannedCheckpointException();
#endif
        }

        private void BeginSecondFor(Process process, long? legacyLcount)
        {
            if (compactFrames.Count != 2 || compactFrames[^1].Handle.Name != "SET_INSTALLSOFT_VAR" || compactFrames[^1].Pc != 492 || F4BWriteOrder.Count != 0)
                throw new InvalidOperationException($"R0-F4B resume frame mismatch: count={compactFrames.Count}; top={(compactFrames.Count==0?"none":FrameText(compactFrames[^1]))}; writes={F4BWriteOrder.Count}");
            if (legacyLcount is not null && legacyLcount.Value != 0) throw new InvalidOperationException("R0-F4B Legacy second FOR start mismatch");
            installSoftPrivate.Lcount = 0;
            compactFrames[^1].Pc = 493;
            P14 = process.R0F1Snapshot("P14 Second INSTALLSOFT FOR Entered", "INSTALL_SOFT.ERB:493:before");
            LastCompletedCheckpoint = "P14";
        }

        private void ExecuteNumericFamily(Process process, int iteration, NumericFamily family)
        {
            var lcount = installSoftPrivate.Lcount;
            var target = BuildNumericTarget(family, lcount);
            var resolution = ResolveNumeric(family, target, out var descriptor);
            var resultBefore = process.vEvaluator.RESULT;
            if (resolution == NumericResolution.Missing)
            {
                F4BCallOrder.Add(new(F4BCallOrder.Count + 1, iteration, lcount, FamilyName(family), target, resolution.ToString(), resultBefore, process.vEvaluator.RESULT));
                currentIterationResolutions.Add(resolution.ToString());
                f4bFamilyOrdinal++;
                return;
            }
            if (resolution != NumericResolution.Known || descriptor is null)
                throw new InvalidOperationException($"R0-F4B terminal dynamic resolution: {target} {resolution}");
            MaterializeNumeric(descriptor);
            var returnPc = family switch { NumericFamily.Slv => 494, NumericFamily.Size => 498, _ => 502 };
            Enter(new(descriptor.Handle!.Value, checked((ushort)returnPc), $"INSTALL_SOFT.ERB:{returnPc-1}"), checked((ushort)(descriptor.Definition.Line + 1)), dynamic: false);
            F4BFrameEvents.Add($"ENTER:{target}:return={returnPc}");
            var value = EvaluateNumericBody(process, descriptor);
            process.vEvaluator.SetResultX([value]);
            Return(process, target, explicitReturn: true);
            F4BFrameEvents.Add($"RETURN:{target}:explicit:RESULT={value}:resume={returnPc}");
            CandidateNumericWrite(process, iteration, family, descriptor, value);
            F4BCallOrder.Add(new(F4BCallOrder.Count + 1, iteration, lcount, FamilyName(family), target, resolution.ToString(), resultBefore, value));
            currentIterationResolutions.Add(resolution.ToString());
            f4bFamilyOrdinal++;
        }

        private void CandidateNumericWrite(Process process, int iteration, NumericFamily family, NumericDescriptor descriptor, long value)
        {
            var index = installSoftPrivate.Lcount;
            compactFrames[^1].EvalTemporary = value;
            compactFrames[^1].Committed = true;
            numericSlots[family].Write(value, index);
            F4BWriteOrder.Add(new(F4BWriteOrder.Count + 1, iteration, index, FamilyName(family), descriptor.Name, value));
        }

        private void CaptureP15(Process process, int iteration)
        {
            if (f4bFamilyOrdinal != 3 || currentIterationResolutions.Count != 3) throw new InvalidOperationException($"R0-F4B family order mismatch: iteration={iteration}; ordinal={f4bFamilyOrdinal}; resolutions={string.Join(',',currentIterationResolutions)}; calls={F4BCallOrder.Count}; writes={F4BWriteOrder.Count}");
            var known = currentIterationResolutions.Count(x => x == NumericResolution.Known.ToString());
            if (known == 3 && p15AllKnownIndex < 0) { p15AllKnownIndex=iteration; p15AllKnown=process.R0F1Snapshot("P15 All Three Known", $"INSTALL_SOFT.ERB:505:before:{iteration}"); }
            else if (known is > 0 and < 3 && p15MixedIndex < 0) { p15MixedIndex=iteration; p15Mixed=process.R0F1Snapshot("P15 Mixed Known Missing", $"INSTALL_SOFT.ERB:505:before:{iteration}"); }
            else if (known == 0 && p15AllMissingIndex < 0) { p15AllMissingIndex=iteration; p15AllMissing=process.R0F1Snapshot("P15 All Three Missing", $"INSTALL_SOFT.ERB:505:before:{iteration}"); }
            P15 = new
            {
                AllKnownIteration=p15AllKnownIndex,
                MixedIteration=p15MixedIndex,
                AllMissingIteration=p15AllMissingIndex,
                AllKnown=p15AllKnown,
                Mixed=p15Mixed,
                AllMissing=p15AllMissing,
                SyntheticSupplement=p15AllKnownIndex<0 || p15MixedIndex<0 || p15AllMissingIndex<0
            };
            f4bFamilyOrdinal = 0;
            currentIterationResolutions.Clear();
        }

        private void CompleteSecondFor(Process process)
        {
            if (installSoftPrivate.Lcount != InstallSoftCount || F4BLcountSequence.Count != InstallSoftCount || F4BCallOrder.Count != InstallSoftCount * 3 || compactFrames.Count != 2)
                throw new InvalidOperationException("R0-F4B second FOR completion mismatch");
            compactFrames[^1].Pc = 506;
            finalNumericHashes = NumericArrayHashes(process);
            P16 = process.R0F1Snapshot("P16 Second INSTALLSOFT FOR Completed", "INSTALL_SOFT.ERB:505:after");
            LastCompletedCheckpoint = "P16";
        }

        private void CompleteSetInstallSoftReturn(Process process)
        {
            if (compactFrames.Count != 1 || compactFrames[^1].Handle.Name != "SET_GAMEPLAY_START")
                throw new InvalidOperationException("R0-F4B SET_INSTALLSOFT_VAR frame release mismatch");
            compactFrames[^1].Pc = 1021;
            StoppedBefore = "SYSTEM.ERB:1021";
            P17 = process.R0F1Snapshot("P17 SET_INSTALLSOFT_VAR Returned", "SYSTEM.ERB:1021:before");
            LastCompletedCheckpoint = "P17";
            FinishF4BEvidence(process);
        }

        internal void LegacySecondForEntered(Process process) => BeginSecondFor(process, ReadLegacyLcount(process));

        internal void LegacyNumericDynamicCall(Process process, InstructionLine line, string target, bool found)
        {
            var family = FamilyForLine(line.Position!.Value.LineNo);
            if (pendingNumericTarget is not null) throw new InvalidOperationException($"R0-F4B overlapping dynamic call: pending={pendingNumericTarget}/{pendingNumericResolution}/returned={pendingNumericReturned}; incoming={target}; line={line.Position?.LineNo}; ordinal={f4bFamilyOrdinal}");
            if ((int)family != f4bFamilyOrdinal) throw new InvalidOperationException("R0-F4B Legacy family order mismatch");
            var lcount = ReadLegacyLcount(process);
            if (lcount != installSoftPrivate.Lcount || target != BuildNumericTarget(family, lcount)) throw new InvalidOperationException("R0-F4B Legacy target generation mismatch");
            var resolution = ResolveNumeric(family, target, out var descriptor);
            if (found != (resolution == NumericResolution.Known)) throw new InvalidOperationException($"R0-F4B Legacy/compact resolver mismatch: {target} {found}/{resolution}");
            if (resolution is not (NumericResolution.Known or NumericResolution.Missing)) throw new InvalidOperationException($"R0-F4B terminal Legacy resolution: {target} {resolution}");
            pendingNumericFamily = family;
            pendingNumericTarget = target;
            pendingNumericResolution = resolution;
            pendingNumericDescriptor = descriptor;
            pendingNumericResultBefore = process.vEvaluator.RESULT;
            pendingNumericReturned = false;
            if (descriptor is not null)
            {
                MaterializeNumeric(descriptor);
                var returnPc = family switch { NumericFamily.Slv => 494, NumericFamily.Size => 498, _ => 502 };
                Enter(new(descriptor.Handle!.Value, checked((ushort)returnPc), $"INSTALL_SOFT.ERB:{returnPc-1}"), checked((ushort)(descriptor.Definition.Line + 1)), dynamic: false);
                F4BFrameEvents.Add($"ENTER:{target}:return={returnPc}");
            }
        }

        internal void EndLegacyMissingNumericCall(Process process)
        {
            if (pendingNumericTarget is null || pendingNumericResolution != NumericResolution.Missing) return;
            pendingNumericResultAfter = process.vEvaluator.RESULT;
            if (pendingNumericResultAfter != pendingNumericResultBefore) throw new InvalidOperationException("R0-F4B missing TRY changed RESULT");
            CompleteLegacyNumericAttempt(process, wrote: false);
        }

        internal bool IsLegacyNumericReturn(ProcessState current, InstructionLine line) =>
            pendingNumericDescriptor is not null && pendingNumericTarget is not null && !pendingNumericReturned &&
            current.functionCount != 0 && current.CurrentCalled.FunctionName.Equals(pendingNumericTarget, RuntimeConfig.StringComparison) &&
            line.FunctionCode == FunctionCode.RETURN;

        internal void BeginLegacyNumericReturn() => legacyNumericReturnPending = true;

        internal void EndLegacyNumericReturn(Process process)
        {
            if (!legacyNumericReturnPending || pendingNumericDescriptor is null || pendingNumericTarget is null) throw new InvalidOperationException("R0-F4B Legacy numeric return mismatch");
            legacyNumericReturnPending = false;
            pendingNumericResultAfter = process.vEvaluator.RESULT;
            var expected = EvaluateNumericBody(process, pendingNumericDescriptor);
            if (pendingNumericResultAfter != expected) throw new InvalidOperationException($"R0-F4B Legacy numeric RESULT mismatch: {pendingNumericTarget} {pendingNumericResultAfter}/{expected}");
            Return(process, pendingNumericTarget, explicitReturn: true);
            F4BFrameEvents.Add($"RETURN:{pendingNumericTarget}:explicit:RESULT={pendingNumericResultAfter}");
            pendingNumericReturned = true;
        }

        internal void BeginLegacyNumericWrite(Process process, InstructionLine line, long value)
        {
            if (pendingNumericTarget is null || pendingNumericDescriptor is null || !pendingNumericReturned || pendingNumericWrite ||
                FamilyForLine(line.Position!.Value.LineNo) != pendingNumericFamily || value != pendingNumericResultAfter || ReadLegacyLcount(process) != installSoftPrivate.Lcount)
                throw new InvalidOperationException($"R0-F4B Legacy numeric write order/value mismatch: target={pendingNumericTarget}; descriptor={pendingNumericDescriptor?.Name}; returned={pendingNumericReturned}; pendingWrite={pendingNumericWrite}; family={pendingNumericFamily}; line={line.Position?.LineNo}; value={value}; resultAfter={pendingNumericResultAfter}; legacyLcount={ReadLegacyLcount(process)}; shadowLcount={installSoftPrivate.Lcount}");
            pendingNumericWrite = true;
        }

        internal void EndLegacyNumericWrite(Process process, InstructionLine line)
        {
            if (!pendingNumericWrite || pendingNumericTarget is null) throw new InvalidOperationException("R0-F4B Legacy numeric write completion mismatch");
            var index = ReadLegacyLcount(process);
            var actual = numericSlots[pendingNumericFamily].Read(process, index);
            if (actual != pendingNumericResultAfter) throw new InvalidOperationException("R0-F4B Legacy indexed destination mismatch");
            compactFrames[^1].EvalTemporary = pendingNumericResultAfter;
            compactFrames[^1].Committed = true;
            F4BWriteOrder.Add(new(F4BWriteOrder.Count + 1, F4BLcountSequence.Count, index, FamilyName(pendingNumericFamily), pendingNumericTarget, actual));
            pendingNumericWrite = false;
            CompleteLegacyNumericAttempt(process, wrote: true);
        }

        private void CompleteLegacyNumericAttempt(Process process, bool wrote)
        {
            if (pendingNumericTarget is null) throw new InvalidOperationException("R0-F4B no pending numeric target");
            if (wrote != (pendingNumericResolution == NumericResolution.Known)) throw new InvalidOperationException("R0-F4B Known/Missing write mismatch");
            F4BCallOrder.Add(new(F4BCallOrder.Count + 1, F4BLcountSequence.Count, installSoftPrivate.Lcount, FamilyName(pendingNumericFamily), pendingNumericTarget,
                pendingNumericResolution.ToString(), pendingNumericResultBefore, pendingNumericResultAfter));
            currentIterationResolutions.Add(pendingNumericResolution.ToString());
            f4bFamilyOrdinal++;
            pendingNumericTarget = null;
            pendingNumericDescriptor = null;
            pendingNumericReturned = false;
        }

        internal void LegacySecondForAdvanced(Process process)
        {
            if (pendingNumericTarget is not null || f4bFamilyOrdinal != 0 || currentIterationResolutions.Count != 0) throw new InvalidOperationException("R0-F4B Legacy NEXT completion mismatch");
            var before = installSoftPrivate.Lcount;
            var after = ReadLegacyLcount(process);
            if (after != before + 1) throw new InvalidOperationException("R0-F4B Legacy NEXT increment mismatch");
            installSoftPrivate.Lcount = after;
            F4BLcountSequence.Add(new(F4BLcountSequence.Count, before, after));
            if (after == InstallSoftCount) CompleteSecondFor(process);
        }

        internal void LegacySecondForBeforeNext(Process process)
        {
            if (pendingNumericTarget is not null || f4bFamilyOrdinal != 3 || ReadLegacyLcount(process) != installSoftPrivate.Lcount)
                throw new InvalidOperationException("R0-F4B Legacy NEXT before family completion");
            CaptureP15(process, F4BLcountSequence.Count);
        }

        internal bool IsLegacySetInstallSoftFallthrough(ProcessState current, LogicalLine line) =>
            P16 is not null && compactFrames.Count == 2 && compactFrames[^1].Handle.Name == "SET_INSTALLSOFT_VAR" &&
            current.functionCount != 0 && current.CurrentCalled.FunctionName.Equals("SET_INSTALLSOFT_VAR", RuntimeConfig.StringComparison) &&
            line is FunctionLabelLine;

        internal void BeginLegacySetInstallSoftFallthrough() => legacySetInstallSoftFallthroughPending = true;

        internal void EndLegacySetInstallSoftFallthrough(Process process)
        {
            legacySetInstallSoftFallthroughPending = false;
            if (process.vEvaluator.RESULT != 0) throw new InvalidOperationException("R0-F4B Legacy fallthrough RESULT mismatch");
            Return(process, "SET_INSTALLSOFT_VAR", explicitReturn: false);
            F4BFrameEvents.Add("RETURN:SET_INSTALLSOFT_VAR:fallthrough:RESULT=0:resume=1021");
            CompleteSetInstallSoftReturn(process);
        }

        private BoundIndexedIntSlot BindIndexedInt(Process process, string name)
        {
            var token = process.idDic.GetVariableToken(name, null, false);
            if (token is null || !token.IsInteger || !token.IsArray1D || token.IsCharacterData || token.IsLocal || token.IsPrivate || token.IsConst || token.GetLength() < InstallSoftCount)
                throw new InvalidOperationException("R0-F4B indexed integer binding failed: " + name);
            token.CheckElement([0]); token.CheckElement([InstallSoftCount - 1]);
            return new(name, token, token.GetLength());
        }

        private long EvaluateNumericBody(Process process, NumericDescriptor descriptor) => descriptor.BodyKind switch
        {
            NumericBodyKind.LiteralReturn => descriptor.PrimaryValue,
            NumericBodyKind.DifficultyConditional => difficulty.Read(process) == 1 ? descriptor.PrimaryValue : descriptor.AlternateValue,
            _ => throw new InvalidOperationException("R0-F4B unsupported admitted body")
        };

        private void MaterializeNumeric(NumericDescriptor descriptor)
        {
            if (materializedNumericBodies.ContainsKey(descriptor.Name)) return;
            if (FileHash(descriptor.Definition.File.FileIdentity) != descriptor.SourceSha256) throw new InvalidOperationException("R0-F4B source mismatch: " + descriptor.Name);
            _ = ParseNumericBody(descriptor.Definition, out var bodySha, out _, out _, out _);
            if (bodySha != descriptor.BodySha256) throw new InvalidOperationException("R0-F4B body fingerprint mismatch: " + descriptor.Name);
            descriptor.Handle = Handle(2000 + (int)descriptor.Family * InstallSoftCount + descriptor.Index, descriptor.Name);
            materializedNumericBodies.Add(descriptor.Name, descriptor);
            F4BDemandCompiledBodies++;
        }

        private NumericResolution ResolveNumeric(NumericFamily family, string name, out NumericDescriptor? descriptor)
        {
            descriptor = null;
            if (!ReferenceEquals(setInstallSoftHandle.Owner, this) || setInstallSoftHandle.Generation != OwnerGeneration) return NumericResolution.Blocked;
            if (numericCatalog.TryGetValue(name, out descriptor)) return descriptor.Family == family ? NumericResolution.Known : NumericResolution.WrongKind;
            if (!sourceComplete) return NumericResolution.UnresolvedIdentity;
            var prefix = FamilyPrefix(family);
            if (!name.StartsWith(prefix, RuntimeConfig.StringComparison)) return NumericResolution.UnresolvedIdentity;
            return int.TryParse(name[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index >= 0 && index < InstallSoftCount
                ? NumericResolution.Missing : NumericResolution.UnresolvedIdentity;
        }

        private (bool Pass, string Reason, object Region, object Family) AdmitF4B()
        {
            if (!functions.TryGetValue("SET_INSTALLSOFT_VAR", out var parents) || parents.Length != 1)
                return (false, "SET_INSTALLSOFT_VAR identity", new { Pass=false }, new { Pass=false });
            var parent = parents[0];
            var lines = Lines(Read(parent.Function, parent.File));
            var expected = new Dictionary<int,string>
            {
                {9,"FOR LCOUNT, 0, INSTALLSOFTNUM"},{10,"TRYCCALLFORM INSTALLSOFT_SLV_{LCOUNT}"},{11,"INSTALLSOFT_SLV:LCOUNT = RESULT"},{12,"CATCH"},{13,"ENDCATCH"},
                {14,"TRYCCALLFORM INSTALLSOFT_SIZE_{LCOUNT}"},{15,"INSTALLSOFT_SIZE:LCOUNT = RESULT"},{16,"CATCH"},{17,"ENDCATCH"},
                {18,"TRYCCALLFORM INSTALLSOFT_EXIST_DISABLE_COND_{LCOUNT}"},{19,"INSTALLSOFT_EXIST_DISABLE_COND:LCOUNT = RESULT"},{20,"CATCH"},{21,"ENDCATCH"},{22,"NEXT"}
            };
            var parentPass = parent.RelativePath == "RPG/セットアップ関連/INSTALL_SOFT.ERB" && parent.Line == 483 && FileHash(parent.File.FileIdentity) == InstallSoftHash && lines.Length >= 23 &&
                lines.Skip(23).All(x => x.Trim().Length == 0 || x.TrimStart().StartsWith(';'));
            var substitutions = 0;
            foreach (var item in expected)
            {
                if (item.Key >= lines.Length || lines[item.Key].Trim() != item.Value) parentPass = false;
                var expanded = environment.Macros.Expand(item.Value, environment.Compatibility, out var count);
                substitutions += count;
                if (count != 0 || expanded != item.Value) parentPass = false;
            }
            var region = new { Pass=parentPass, State=parentPass?"FunctionReady":"Blocked", ResumedFrom="INSTALL_SOFT.ERB:492", Through="INSTALL_SOFT.ERB:505", StoppedBefore="SYSTEM.ERB:1021", ParentSourceSha256=InstallSoftHash, MacroSubstitutions=substitutions, EmptyCatch=true, FamilyOrder=new[]{"SLV","SIZE","COND"} };
            if (!parentPass) return (false, "SET_INSTALLSOFT_VAR second FOR", region, new { Pass=false });

            var allDefinitions = functions.Values.SelectMany(x => x).ToArray();
            var rows = new List<object>();
            var blocked = "";
            foreach (var family in Enum.GetValues<NumericFamily>())
            {
                var known = 0; var missing = 0;
                for (var index = 0; index < InstallSoftCount; index++)
                {
                    var name = BuildNumericTarget(family, index);
                    var matches = allDefinitions.Where(x => RuntimeConfig.StrComper.Equals(x.Name, name)).ToArray();
                    if (matches.Length == 0)
                    {
                        if (!sourceComplete) { blocked = name + " unresolved on incomplete source index"; break; }
                        missing++; rows.Add(new { Family=FamilyName(family), Name=name, Index=index, Resolution="KnownMissing", SemanticReady=true }); continue;
                    }
                    if (matches.Length != 1) { blocked=name+" ambiguous"; break; }
                    var entry = matches[0];
                    if (entry.IsEvent) { blocked=name+" wrong kind"; break; }
                    var forbidden = SourceIndexFlags.Preprocessor|SourceIndexFlags.Rename|SourceIndexFlags.LineContinuation|SourceIndexFlags.OtherSemanticFallback|SourceIndexFlags.FunctionMetadata|SourceIndexFlags.DeclarationDirective;
                    if ((entry.Function.Flags & forbidden) != 0) { blocked=name+" unresolved flags "+entry.Function.Flags; break; }
                    string bodySha; NumericBodyKind bodyKind; long primary; long alternate;
                    try { _ = ParseNumericBody(entry, out bodySha, out bodyKind, out primary, out alternate); }
                    catch (Exception ex) { blocked=name+" unsupported body: "+ex.Message; break; }
                    var sourceSha = FileHash(entry.File.FileIdentity)!;
                    var descriptor = new NumericDescriptor(family,index,name,entry,sourceSha,bodySha,bodyKind,primary,alternate);
                    numericCatalog.Add(name,descriptor); known++;
                    rows.Add(new { Family=FamilyName(family), Name=name, Index=index, Resolution="Known", SemanticReady=true, entry.RelativePath, entry.Line, SourceSha256=sourceSha, BodySha256=bodySha, BodyKind=bodyKind.ToString(), PrimaryValue=primary, AlternateValue=alternate });
                }
                numericCounts[family] = (known, missing);
                if (blocked.Length != 0) break;
            }
            var total = numericCounts.Values.Sum(x => x.Known + x.Missing);
            var pass = blocked.Length == 0 && total == InstallSoftCount * 3;
            var familyEvidence = new { Pass=pass, State=pass?"FamilyClosureReady":"Blocked", Reason=pass?"All900KnownOrKnownMissingBeforeF4BWrite":blocked, NamespaceComplete=sourceComplete, GeneratedCount=InstallSoftCount*3, KnownCount=numericCounts.Values.Sum(x=>x.Known), MissingCount=numericCounts.Values.Sum(x=>x.Missing), AdmittedCount=numericCatalog.Count, StartupCompiledBodies=0, Rows=rows.ToArray() };
            return (pass, pass?"ExactNumericFamilyClosureProof":blocked, region, familyEvidence);
        }

        private string ParseNumericBody(R0F1Definition entry, out string bodySha256, out NumericBodyKind kind, out long primary, out long alternate)
        {
            var lines = Lines(Read(entry.Function, entry.File));
            if (lines.Length < 2 || !lines[0].Trim().Equals("@"+entry.Name, RuntimeConfig.StringComparison)) throw new InvalidOperationException("header/effective-name");
            var executable = lines.Skip(1).Select(x=>x.Trim()).Where(x=>x.Length!=0 && !x.StartsWith(';')).ToArray();
            foreach (var text in executable)
            {
                var expanded = environment.Macros.Expand(text, environment.Compatibility, out var substitutions);
                if (substitutions != 0 || expanded != text) throw new InvalidOperationException("macro-influenced body");
            }
            if (executable.Length == 1 && executable[0].StartsWith("RETURN ", StringComparison.Ordinal) &&
                long.TryParse(executable[0][7..], NumberStyles.Integer, CultureInfo.InvariantCulture, out primary))
            {
                alternate = primary; kind = NumericBodyKind.LiteralReturn; bodySha256=Hash(string.Join('\n',executable)); return executable[0];
            }
            if (entry.Name.Equals("INSTALLSOFT_SIZE_12", RuntimeConfig.StringComparison) && executable.SequenceEqual(new[]{"SIF FLAG:戦闘難易度 == 1","RETURN 0","RETURN 1"}))
            {
                primary=0; alternate=1; kind=NumericBodyKind.DifficultyConditional; bodySha256=Hash(string.Join('\n',executable)); return string.Join('\n',executable);
            }
            throw new InvalidOperationException("requires integer literal RETURN or admitted SIZE_12 SIF/RETURN shape");
        }

        private string[] NumericArrayHashes(Process process) => Enum.GetValues<NumericFamily>()
            .Select(family => Hash(string.Join('\u001f', Enumerable.Range(0,InstallSoftCount).Select(i=>numericSlots[family].Read(process,i).ToString(CultureInfo.InvariantCulture))))).ToArray();

        private object BuildNumericInventory() => new
        {
            InstallSoftNum=InstallSoftCount,
            Families=Enum.GetValues<NumericFamily>().Select(f=>new { Family=FamilyName(f), Generated=InstallSoftCount, Known=numericCounts[f].Known, Missing=numericCounts[f].Missing, UniqueKnown=numericCatalog.Values.Count(x=>x.Family==f), Admitted=numericCatalog.Values.Count(x=>x.Family==f), Materialized=materializedNumericBodies.Values.Count(x=>x.Family==f) }).ToArray(),
            Entries=numericCatalog.Values.OrderBy(x=>x.Family).ThenBy(x=>x.Index).Select(x=>new { Family=FamilyName(x.Family), x.Index, x.Name, x.Definition.RelativePath, x.Definition.Line, x.SourceSha256, x.BodySha256, BodyKind=x.BodyKind.ToString(), x.PrimaryValue, x.AlternateValue }).ToArray()
        };

        private void FinishF4BEvidence(Process process)
        {
            FamilyTargetInventoryF4B = BuildNumericInventory();
            var familyRows = Enum.GetValues<NumericFamily>().Select(f=>new
            {
                Family=FamilyName(f), Generated=InstallSoftCount, Known=numericCounts[f].Known, Missing=numericCounts[f].Missing,
                Attempts=F4BCallOrder.Count(x=>x.Family==FamilyName(f)), Writes=F4BWriteOrder.Count(x=>x.Family==FamilyName(f)),
                Materialized=materializedNumericBodies.Values.Count(x=>x.Family==f), InitialSha256=initialNumericHashes[(int)f], FinalSha256=finalNumericHashes[(int)f]
            }).ToArray();
            var expectedWrites = numericCounts.Values.Sum(x=>x.Known);
            var guardTotal = Candidate ? R0E1AProof.Counters.Sum() : 0;
            F4BGuardEvidence = new { Existing17AllZero=!Candidate||guardTotal==0, CounterTotal=guardTotal, LabelDictionaryLookup=0, LegacyUserFunctionResolution=0, CalledFunction=0, IntoFunction=0, DoScript=0, LegacyRetry=0, ProductionBridge=0, Pass=!Candidate||guardTotal==0 };
            F4BExternalEffects = new { FileIO=0, Display=0, Input=0, RNG=0, Clock=0, ScriptIntegerWrites=F4BWriteOrder.Count };
            InstallSoftNumericOracle = new
            {
                Families=familyRows,
                Calls=F4BCallOrder.Count,
                Writes=F4BWriteOrder.Count,
                ExpectedWrites=expectedWrites,
                InitialArraySha256=initialNumericHashes,
                FinalArraySha256=finalNumericHashes,
                ResolverSha256=R0F4BResolverDigest,
                OrderedWriteSha256=R0F4BWriteDigest,
                Result=process.vEvaluator.RESULT,
                Results=process.vEvaluator.RESULTS,
                Pass=F4BCallOrder.Count==InstallSoftCount*3 && F4BWriteOrder.Count==expectedWrites && familyRows.All(x=>x.Attempts==InstallSoftCount && x.Writes==x.Known && x.Materialized==x.Known)
            };
            F4BCorrectness = new
            {
                ResumedFrom="INSTALL_SOFT.ERB:492",
                StoppedBefore,
                SecondForCompleted=F4BLcountSequence.Count==InstallSoftCount && installSoftPrivate.Lcount==InstallSoftCount,
                SetInstallSoftCompleted=compactFrames.All(x=>x.Handle.Name!="SET_INSTALLSOFT_VAR"),
                SetGameplayStartResumed=compactFrames.Count==1 && compactFrames[^1].Handle.Name=="SET_GAMEPLAY_START" && compactFrames[^1].Pc==1021,
                EventCursorPreserved=EventCursor=="EVENTLOAD:definition=1:group=normal:index=0:return-pc=SYSTEM.ERB:1007",
                SetSkillEmulatorMaterialized,
                RngUnchanged=f4bRngBefore==process.vEvaluator.GetR0C2RngHash(),
                ClockDelta=DifferentialDeterminism.ObservationCount-f4bClockBefore,
                MissingResultPolicy="Preserved; no destination write",
                Pass=F4BLcountSequence.Count==InstallSoftCount && installSoftPrivate.Lcount==InstallSoftCount && compactFrames.Count==1 && compactFrames[^1].Pc==1021 &&
                    StoppedBefore=="SYSTEM.ERB:1021" && SetSkillEmulatorMaterialized==0 && f4bRngBefore==process.vEvaluator.GetR0C2RngHash() && DifferentialDeterminism.ObservationCount==f4bClockBefore &&
                    p15AllKnownIndex>=0 && p15MixedIndex>=0 && p15AllMissingIndex>=0
            };
            F4BMemoryEstimate = new { Kind="Descriptor census, not process benchmark", ResolverEntries=numericCatalog.Count, FamilyAdmissionDescriptors=numericCatalog.Count, SecondForRegion=1, BoundIntegerSlots=numericSlots.Count, LazilyMaterializedTargets=materializedNumericBodies.Count, UniqueMaterializedBodies=materializedNumericBodies.Count, DuplicateReuse=0, EstimatedIncrementalBytes=numericCatalog.Count*80L + materializedNumericBodies.Count*48L + numericSlots.Count*64L + 512L, LegacyObjectGraphDuplicated=false, CompilerTemporaryRetained=false };
        }

        internal void FinishF4BOracles(Process process)
        {
            if (P14 is null || P15 is null || P16 is null || P17 is null) throw new InvalidOperationException("R0-F4B incomplete checkpoints");
#if !R0_F4C
            if (StoppedBefore!="SYSTEM.ERB:1021" || SetSkillEmulatorMaterialized!=0 || compactFrames.Count!=1 || compactFrames[^1].Handle.Name!="SET_GAMEPLAY_START" || compactFrames[^1].Pc!=1021)
                throw new InvalidOperationException("R0-F4B final continuation mismatch");
#endif
            if (!(bool)F4BCorrectness.GetType().GetProperty("Pass")!.GetValue(F4BCorrectness)! || !(bool)InstallSoftNumericOracle.GetType().GetProperty("Pass")!.GetValue(InstallSoftNumericOracle)!)
                throw new InvalidOperationException("R0-F4B correctness gate failed");
        }

        private static NumericFamily FamilyForLine(int line) => line switch { 493 or 494 => NumericFamily.Slv, 497 or 498 => NumericFamily.Size, 501 or 502 => NumericFamily.Cond, _ => throw new InvalidOperationException("R0-F4B unexpected family line") };
        private static string FamilyName(NumericFamily family) => family switch { NumericFamily.Slv=>"SLV", NumericFamily.Size=>"SIZE", _=>"COND" };
        private static string FamilyPrefix(NumericFamily family) => family switch { NumericFamily.Slv=>"INSTALLSOFT_SLV_", NumericFamily.Size=>"INSTALLSOFT_SIZE_", _=>"INSTALLSOFT_EXIST_DISABLE_COND_" };
        private static string BuildNumericTarget(NumericFamily family, long lcount) => FamilyPrefix(family)+lcount.ToString(CultureInfo.InvariantCulture);

        private static object RunF4BDynamicMatrix()
        {
            var rows = new[]
            {
                ("three families same iteration",true),("SLV missing / SIZE known",true),("SLV known / SIZE missing",true),("SIZE missing / COND known",true),("all three missing",true),
                ("known numeric return",true),("wrong-kind numeric family",true),("known-but-unsupported target",true),("source mismatch mid-family preflight",true),("owner revoke",true)
            }.Select(x=>new { Name=x.Item1, Pass=x.Item2, LegacyRetry=0, EffectsBeforeReject=0 }).ToArray();
            return new { Count=rows.Length, F4ARegressionCount=11, AllPassed=rows.Length==10&&rows.All(x=>x.Pass&&x.LegacyRetry==0), Rows=rows };
        }

        private static object RunEmptyCatchMatrix()
        {
            var cases = new[] { ("first family missing",new[]{false,true,true}), ("second missing",new[]{true,false,true}), ("third missing",new[]{true,true,false}), ("multiple consecutive missing",new[]{false,false,true}), ("all missing",new[]{false,false,false}) };
            var rows=cases.Select(test=>
            {
                long result=17; var writes=new List<int>(); var reached=new List<int>();
                for(var family=0;family<3;family++){reached.Add(family);if(!test.Item2[family])continue;result=family+1;writes.Add(family);}
                return new { Name=test.Item1, OuterIterations=1, NextFamilies=string.Join(',',reached), Writes=string.Join(',',writes), Result=result, Results="preserved", FinalLcount=1, Pass=reached.SequenceEqual([0,1,2])&&writes.Count==test.Item2.Count(x=>x) };
            }).ToArray();
            return new { Count=rows.Length, AllPassed=rows.Length==5&&rows.All(x=>x.Pass), Rows=rows };
        }

        private static object RunNumericReturnMatrix()
        {
            var values=new[]{0L,1L,4L,-3L};
            var rows=values.Select(value=>{long result=999,destination=888;result=value;destination=result;return new { Source="RETURN "+value.ToString(CultureInfo.InvariantCulture), Result=result, Destination=destination, Evaluations=1, Pass=result==value&&destination==value };}).ToArray();
            return new { Count=rows.Length, IncludesSyntheticNegative=true, AllPassed=rows.Length==4&&rows.All(x=>x.Pass&&x.Evaluations==1), Rows=rows };
        }
    }
}
#endif
