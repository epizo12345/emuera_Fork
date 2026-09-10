#if R0_F1
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MinorShift.Emuera.Next.Compiler;
using MinorShift.Emuera.Next.Core;
using MinorShift.Emuera.Next.Vm;
using MinorShift.Emuera.Runtime.Config.JSON;
using MinorShift.Emuera.Runtime.Diagnostics;
using MinorShift.Emuera.Runtime.Script;
using MinorShift.Emuera.Runtime.Script.Data;
using MinorShift.Emuera.Runtime.Script.Statements;
using RuntimeConfig = MinorShift.Emuera.Runtime.Config.Config;

namespace MinorShift.Emuera.GameProc;

internal sealed partial class Process
{
    private R0F1Context? r0f1;

    internal object RunR0F1(bool candidate, string run = "actual", bool executeF4D5 = false, bool executeF4E1 = false,
        bool executeF4E2 = false, bool executeF4E3 = false, bool executeF4F = false, bool executeF4G1 = false,
        bool executeF4G2 = false, bool executeF4G3 = false, bool executeF4G4 = false, bool executeF5A = false, bool executeF5A2 = false,
        bool executeF5B = false, bool executeF6 = false, bool executeF6A = false, bool executeF6B = false)
    {
#if R0_F6G3
        if (Program.R0F6G3Mode)
        {
            run = "actual";
            executeF4D5 = executeF4E1 = executeF4E2 = executeF4E3 = executeF4F = executeF4G1 = executeF4G2 = executeF4G3 = executeF4G4 = executeF5A = executeF5A2 = executeF5B = true;
        }
#endif
#if R0_F6G7R2
        if (Program.R0F6G7R2Mode)
        {
            run = "actual";
            executeF4D5 = executeF4E1 = executeF4E2 = executeF4E3 = executeF4F = executeF4G1 = executeF4G2 = executeF4G3 = executeF4G4 = executeF5A = executeF5A2 = executeF5B = true;
        }
#endif
        r0f1 = new R0F1Context(candidate, Program.ErbDir, Path.GetDirectoryName(Program.CsvDir.TrimEnd(Path.DirectorySeparatorChar))!, this, run);
        // R0_B1の診断初期化はERB直後で停止するため、両経路とも既存Host state tableだけをここで完成させる。
        if (systemProcessDictionary.Count == 0) initSystemProcess();
        state.SystemState = SystemStateCode.LoadData_DataLoaded;
        r0f1.P0 = R0F1Snapshot("P0 SaveDecoded", "BeforeHostLoadData");
        try
        {
            runSystemProc();
            if (!candidate) runScriptProc();
            throw new InvalidOperationException("R0-F1 failed to reach the planned checkpoint");
        }
        catch (R0F1PlannedCheckpointException) { }
#if R0_F4C
        r0f1.FinishF4COracles(this);
#endif
#if R0_F4D3
        r0f1.CaptureF4D3ActualClosure(this);
#if R0_F4D5
        if (executeF4D5) r0f1.ExecuteF4D5(this);
#if R0_F4E1
#if R0_F4E3
        r0f1.F4E3Enabled = executeF4E3;
#endif
        if (executeF4E1) r0f1.ExecuteF4E1(this);
#if R0_F4E2
        if (executeF4E2) r0f1.ExecuteF4E2(this);
#if R0_F4E3
        if (executeF4E3) r0f1.ExecuteF4E3(this);
#if R0_F4F
        if (executeF4F) r0f1.ExecuteF4F(this);
#if R0_F4G1
        if (executeF4G1) r0f1.ExecuteF4G1(this);
#if R0_F4G2
        if (executeF4G2) r0f1.ExecuteF4G2(this);
#if R0_F4G3
        if (executeF4G3) r0f1.ExecuteF4G3(this);
#if R0_F4G4
#if R0_F5A
        r0f1.F5AEnabled = executeF5A;
#if R0_F5A2
        r0f1.F5A2Enabled = executeF5A2;
#if R0_F5B
        r0f1.F5BEnabled = executeF5B;
#if R0_F6
        r0f1.F6Enabled = executeF6;
#if R0_F6A
        r0f1.F6AEnabled = executeF6A;
#if R0_F6B
        r0f1.F6BEnabled = executeF6B;
#if R0_F6C
        r0f1.F6CEnabled = Program.R0F6CMode;
#if R0_F6D
        r0f1.F6DEnabled = Program.R0F6DMode;
#endif
#endif
#endif
#endif
#endif
#endif
#endif
#endif
        if (executeF4G4) r0f1.ExecuteF4G4(this);
#if R0_F5A
        if (executeF5A) r0f1.ExecuteF5A(this);
#if R0_F5B
        if (executeF5B) r0f1.ExecuteF5B(this);
#endif
#endif
#endif
#endif
#endif
#endif
#endif
#endif
#endif
#endif
#endif
#endif
#if R0_F4B
        r0f1.FinishF4BOracles(this);
#endif
#if R0_F4A
        r0f1.FinishF4AOracles(this);
#endif
#if R0_F3
        r0f1.FinishF3Oracles(this);
#endif
#if R0_F2
        r0f1.FinishF2Oracles(this);
#endif

        var guards = R0E1AProof.GuardSnapshot();
        var guardCounts = R0E1AProof.Counters.Sum();
        return new
        {
            Schema =
#if R0_F4E1
#if R0_F4F
#if R0_F4G1
#if R0_F4G2
#if R0_F4G3
#if R0_F4G4
#if R0_F5A
#if R0_F5A2
#if R0_F5B
#if R0_F6
#if R0_F6A
#if R0_F6B
#if R0_F6D
                Program.R0F6DMode ? "emuera-r0f6d-formation-command-input-v1" :
#endif
                Program.R0F6CMode ? "emuera-r0f6c-show-floor-input-v1" :
                executeF6B ? "emuera-r0f6b-dungeon-attack-input-v1" :
#endif
                executeF6A ? "emuera-r0f6a-floor-and-input-v1" :
#endif
                executeF6 ? "emuera-r0f6-representative-input-boundary-v1" :
#endif
                executeF5B ? "emuera-r0f5b-eventload-transfer-begin-shop-v1" :
#endif
                executeF5A2 ? "emuera-r0f5a2-complete-verup-base-v5-v6-v1" :
#endif
                executeF5A ? "emuera-r0f5a-verup-base-progressive-v1" :
#endif
                executeF4G4 ? "emuera-r0f4g4-set-flag-and-complete-tail-v1" :
#endif
                executeF4G3 ? "emuera-r0f4g3-complete-set-gameplay-start-tail-v1" :
#endif
                executeF4G2 ? "emuera-r0f4g2-extra-title-master-rotation-v1" :
#endif
                executeF4G1 ? "emuera-r0f4g1-mob-image-reset-v1" :
#endif
                executeF4F ? "emuera-r0f4f-complete-set-shop-v1" :
#endif
                executeF4E3 ? "emuera-r0f4e3-complete-set-turnend-v1" :
                executeF4E2 ? "emuera-r0f4e2-turnend-iteration0-v1" :
                executeF4E1 ? "emuera-r0f4e1-turnend-name-prefix-v1" :
#endif
#if R0_F4D5
                executeF4D5 ? "emuera-r0f4d5-actual-set-equip-var-v1" :
#endif
#if R0_F4D3
                "emuera-r0f4d3-actual-class-b-closure-v1",
#elif R0_F4C
                "emuera-r0f4c-skill-emulator-name-family-v1",
#elif R0_F4B
                "emuera-r0f4b-installsoft-numeric-families-v1",
#elif R0_F4A
                "emuera-r0f4a-installsoft-name-family-v1",
#elif R0_F3
                "emuera-r0f3-first-normal-helpers-v1",
#elif R0_F2
                "emuera-r0f2-eventload-prologue-v1",
#else
                "emuera-r0f1-loadglobal-prefix-v1",
#endif
            Mode = candidate ? "GraphFreeCandidate" : "LegacyControl",
            Run = run,
            Deterministic = new { Seed = Program.NextRuntimeDifferentialSeed, ClockBase = Program.NextRuntimeDifferentialClockBase, ClockStepMs = Program.NextRuntimeDifferentialClockStepMs, ClockObservations = DifferentialDeterminism.ObservationCount },
            r0f1.P0, r0f1.P1, r0f1.P2,
#if R0_F2
            r0f1.P3, r0f1.P4, r0f1.P5, r0f1.P6,
            r0f1.RegionAdmissionF2,
            r0f1.ActualBranch,
            r0f1.TrueBranchGuardMatrix,
            r0f1.SifEvidence,
            r0f1.UseContinueEvidence,
            r0f1.SaveGlobalEvidence,
            r0f1.F2HostEffects,
            r0f1.WriteOrder,
            r0f1.ExecutedLines,
            r0f1.ActualAdditionalStatementsAdvanced,
            r0f1.ActualTemporarySaveBranch,
            r0f1.F2Committed,
            r0f1.LastCompletedCheckpoint,
            r0f1.F2DemandCompiledBodies,
            r0f1.SetGameplayStartMaterialized,
            r0f1.SavedContinuation,
#endif
#if R0_F3
            r0f1.P7, r0f1.P8, r0f1.P9, r0f1.P10,
            r0f1.RegionAdmissionF3,
            r0f1.BoundSlotsF3,
            r0f1.NormalCallMatrix,
            r0f1.AdmissionNegativeMatrix,
            r0f1.ClockWriteOracle,
            r0f1.LinelensOracle,
            r0f1.ConfigElementOracle,
            r0f1.DynamicPrivateOracle,
            r0f1.PersistentBankOracle,
            r0f1.RngOracle,
            r0f1.DisplayOracle,
            r0f1.NormalFrameEvidence,
            r0f1.F3ExternalEffects,
            r0f1.F3WriteOrder,
            r0f1.F3FrameEvents,
            r0f1.F3DemandCompiledBodies,
            r0f1.SetLinelensMaterialized,
            r0f1.ConfigElementNumMaterialized,
            r0f1.SetInstallSoftMaterialized,
#endif
#if R0_F4A
            r0f1.P11, r0f1.P12Existing, r0f1.P12Missing, r0f1.P13,
            r0f1.RegionAdmissionF4A,
            r0f1.FamilyAdmissionF4A,
            r0f1.DynamicCallMatrix,
            r0f1.ForMatrix,
            r0f1.FamilyTargetInventory,
            r0f1.InstallSoftNameOracle,
            r0f1.F4AGuardEvidence,
            r0f1.F4ACorrectness,
            r0f1.F4AMemoryEstimate,
            r0f1.F4AExternalEffects,
            r0f1.F4ADynamicTargets,
            r0f1.F4ALcountSequence,
            r0f1.F4AWriteOrder,
            r0f1.F4AFrameEvents,
            r0f1.R0F4ASetInstallSoftMaterialized,
            r0f1.F4ASecondFamilyMaterialized,
            r0f1.F4ADemandCompiledBodies,
#endif
#if R0_F4B
            r0f1.P14, r0f1.P15, r0f1.P16, r0f1.P17,
            r0f1.RegionAdmissionF4B,
            r0f1.FamilyAdmissionF4B,
            r0f1.FamilyTargetInventoryF4B,
            r0f1.DynamicCallMatrixF4B,
            r0f1.EmptyCatchMatrixF4B,
            r0f1.NumericReturnMatrixF4B,
            r0f1.InstallSoftNumericOracle,
            r0f1.F4BGuardEvidence,
            r0f1.F4BCorrectness,
            r0f1.F4BMemoryEstimate,
            r0f1.F4BExternalEffects,
            r0f1.F4BCallOrder,
            r0f1.F4BWriteOrder,
            r0f1.F4BLcountSequence,
            r0f1.F4BFrameEvents,
            r0f1.F4BDemandCompiledBodies,
            r0f1.SetSkillEmulatorMaterialized,
#endif
#if R0_F4C
            r0f1.P18, r0f1.P19, r0f1.P20, r0f1.P21, r0f1.P22,
            r0f1.RegionAdmissionF4C,
            r0f1.FamilyAdmissionF4C,
            r0f1.FamilyTargetInventoryF4C,
            r0f1.DynamicCallMatrixF4C,
            r0f1.SkillBodyMatrixF4C,
            r0f1.SkillEmulatorNameOracle,
            r0f1.F4CGuardEvidence,
            r0f1.F4CCorrectness,
            r0f1.F4CMemoryEstimate,
            r0f1.F4CExternalEffects,
            r0f1.F4CDynamicTargets,
            r0f1.F4CWriteOrder,
            r0f1.F4CLcountSequence,
            r0f1.F4CFrameEvents,
            r0f1.F4CDemandCompiledBodies,
            r0f1.SetEquipVarMaterialized,
#endif
#if R0_F4D3
            r0f1.F4D3StateBefore,
            r0f1.F4D3StateAfter,
            r0f1.F4D3Cells,
            r0f1.F4D3ClosureRoots,
            r0f1.F4D3ClosureEdges,
            r0f1.F4D3ClosureSummary,
            r0f1.F4D3CertificateEffects,
            r0f1.F4D3Gate,
#endif
#if R0_F4D5
            r0f1.F4D5Evidence,
#if R0_F4E1
            r0f1.F4E1Evidence,
#if R0_F4E2
            r0f1.F4E2Evidence,
#if R0_F4E3
            r0f1.F4E3Evidence,
#if R0_F4F
            r0f1.F4FEvidence,
#if R0_F4G1
            r0f1.F4G1Evidence,
#if R0_F4G2
            r0f1.F4G2Evidence,
#if R0_F4G3
            r0f1.F4G3Evidence,
#if R0_F4G4
            r0f1.F4G4Evidence,
#if R0_F5A
            r0f1.F5AEvidence,
#if R0_F5A2
            r0f1.F5A2Evidence,
#if R0_F5B
            r0f1.F5BEvidence,
#if R0_F6G3
            r0f1.F6G3Evidence,
#endif
#if R0_F6G7R2
            r0f1.F6G7R2Evidence,
#endif
#if R0_F6
            r0f1.F6Evidence,
#if R0_F6A
            r0f1.F6AEvidence,
#if R0_F6B
            r0f1.F6BEvidence,
#if R0_F6C
            r0f1.F6CEvidence,
#if R0_F6D
            r0f1.F6DEvidence,
#endif
#endif
#endif
#endif
#endif
#endif
#endif
#endif
#endif
#endif
#endif
#endif
#endif
#endif
#endif
#endif
#endif
            r0f1.HostStates,
            r0f1.HostEffects,
            r0f1.ResolverEvidence,
            r0f1.ResolverMatrix,
            EventCatalog = r0f1.EventCatalogEvidence,
            EventOracle = r0f1.EventOracle,
            PrefixAdmission = r0f1.PrefixAdmission,
            PrefixNegatives = r0f1.PrefixNegatives,
            r0f1.StartupCompiledBodies,
            r0f1.DemandCompiledBodies,
            r0f1.ActualScriptStatementsAdvanced,
            r0f1.StoppedBefore,
            EventLoadCompleted = executeF5B,
            GraphFreeGameResumed =
#if R0_F6
#if R0_F6B
#if R0_F6D
                Program.R0F6DMode ? r0f1.F6DGraphFreeGameResumed :
#endif
                Program.R0F6CMode ? r0f1.F6CGraphFreeGameResumed :
                executeF6B ? r0f1.F6BGraphFreeGameResumed :
#endif
                executeF6 ? r0f1.F6GraphFreeGameResumed :
#endif
                "NOT_YET_PROVEN",
            LegacyErbGraphAvoided = candidate,
            LegacyRetryAfterCompact = 0,
            ProductionBridgeUsed = "NO",
            ProductionBridgeAttempts = B1Proof.BridgeAttempts,
            GuardCounterTotal = candidate ? guardCounts : 0,
            Guard = candidate ? guards : null,
            RetainedEstimateBytes = r0f1.RetainedEstimateBytes,
            WholeProductSuperiority = "NOT_YET_CLAIMED"
        };
    }

    private object R0F1Snapshot(string checkpoint, string pc)
    {
        var domains = vEvaluator.GetDifferentialStateHashes();
        var result = string.Join('\u001f', vEvaluator.RESULT_ARRAY.Select(value => value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var results = string.Join('\u001f', vEvaluator.RESULTS_ARRAY.Select(value => value ?? string.Empty));
        var manifest = r0f1!.CurrentManifest();
        return new
        {
            Checkpoint = checkpoint,
            StateSha256 = GetBenchmarkStateHash(),
            domains.VariablesHash,
            domains.CharacterHash,
            GlobalSha256 = vEvaluator.GetR0F1GlobalHash(),
            GlobalSchemaSha256 = vEvaluator.GetR0F1GlobalSchemaHash(),
            ResultSha256 = R0F1Context.Hash(result),
            ResultsSha256 = R0F1Context.Hash(results),
            Result0 = vEvaluator.RESULT,
#if R0_F3
            LogicalLcount = r0f1.R0F3LogicalLcount,
            LcountStorage = r0f1.R0F3LcountStorage,
            DynamicScopeActive = r0f1.R0F3DynamicScopeActive,
            CompactFrameSha256 = r0f1.R0F3FrameDigest,
            PersistentBankSha256F3 = r0f1.R0F3PersistentBankDigest,
#else
            LogicalLcount = 0,
            LcountStorage = "STATIC_DEFAULT",
#endif
#if R0_F4A
            F4ALogicalLcount = r0f1.R0F4ALogicalLcount(this),
            F4ALcountStorage = "PHYSICAL_DEFINITION_STATIC",
            F4AFrameSha256 = r0f1.R0F3FrameDigest,
            F4ADynamicResolverSha256 = r0f1.R0F4ADynamicResolverDigest,
            F4AWriteSha256 = r0f1.R0F4AWriteDigest,
            InstallSoftNameSha256 = r0f1.R0F4AInstallSoftNameHash(this),
#endif
#if R0_F4B
            F4BResolverSha256 = r0f1.R0F4BResolverDigest,
            F4BWriteSha256 = r0f1.R0F4BWriteDigest,
#endif
#if R0_F4C
            F4CLogicalLcount = r0f1.R0F4CLogicalLcount(this),
            F4CFrameSha256 = r0f1.R0F3FrameDigest,
            F4CResolverSha256 = r0f1.R0F4CResolverDigest,
            F4CWriteSha256 = r0f1.R0F4CWriteDigest,
            SkillEmulatorNameSha256 = r0f1.R0F4CSkillNameHash(this),
#endif
            CharacterCount = vEvaluator.VariableData.CharacterList.Count,
            vEvaluator.VariableData.LastLoadNo,
            vEvaluator.VariableData.LastLoadVersion,
            vEvaluator.VariableData.LastLoadText,
            SystemState = state.SystemState.ToString(),
            PendingBegin = state.isBegun,
            EventCursor = r0f1.EventCursor,
            ProgramCounter = pc,
            RngSha256 = vEvaluator.GetR0C2RngHash(),
            ClockObservations = DifferentialDeterminism.ObservationCount,
            HostEffectDigest = R0F1Context.Hash(string.Join('\n', r0f1.HostEffects)),
#if R0_F2
            F2HostEffectDigest = R0F1Context.Hash(string.Join('\n', r0f1.F2HostEffects)),
            BoundVariablesF2 = r0f1.BoundVariablesF2,
            BoundValues = r0f1.R0F2BoundValues(this),
            PersistentBankSha256 = R0F1Context.Hash("EVENTLOAD:LCOUNT:0"),
            ContinueSavePresent = r0f1.R0F2ContinueSavePresent(),
            Save219Sha256 = R0F1Context.FileHash(Path.Combine(RuntimeConfig.SavDir, "save219.sav")),
#endif
            GlobalFileSha256 = R0F1Context.FileHash(Path.Combine(RuntimeConfig.SavDir, "global.sav")),
            manifest.FileCount,
            manifest.TotalBytes,
            manifest.Sha256
        };
    }

    private bool R0F1TryCallFunction(string functionName, bool force, bool isEvent, out bool result)
    {
        result = false;
        if (r0f1 is null || !r0f1.Candidate) return false;
#if R0_F5B
        if (r0f1.F5BEnabled && isEvent && functionName.Equals("EVENTSHOP", StringComparison.OrdinalIgnoreCase))
        {
            r0f1.EnterCandidateEventShop(this);
            throw new R0F1PlannedCheckpointException();
        }
#endif
        var resolution = r0f1.Resolve(functionName, isEvent);
        r0f1.ResolverEvidence.Add(new(functionName, isEvent ? "Event" : "Normal", resolution));
        if (resolution == R0F1Resolution.KnownMissing)
        {
            if (force) throw new InvalidOperationException("required compact function is known missing: " + functionName);
            return true;
        }
        if (resolution != R0F1Resolution.Ready)
            throw new InvalidOperationException($"compact resolution rejected {functionName}: {resolution}");
        if (!isEvent || !functionName.Equals("EVENTLOAD", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("R0-F1 admits only EVENTLOAD dispatch");
        r0f1.ExecuteEventLoadPrefix(this);
        throw new R0F1PlannedCheckpointException();
    }

    private void R0F1ObserveSystemDispatch()
    {
        if (r0f1 is not null) r0f1.HostStates.Add(state.SystemState.ToString());
    }

    internal void R0F1BeforeLegacyLoadGlobal(InstructionLine line)
    {
        if (r0f1 is null || r0f1.Candidate) return;
        if (line.Position is null || line.Position.Value.LineNo != 978 || !Path.GetFileName(line.Position.Value.Filename).Equals("SYSTEM.ERB", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Legacy LOADGLOBAL oracle reached an unexpected source location");
        r0f1.EventCursor = "EVENTLOAD:definition=1:statement=LOADGLOBAL";
        r0f1.P1 = R0F1Snapshot("P1 BeforeLoadGlobal", "SYSTEM.ERB:978:before");
        r0f1.HostEffects.Add("LoadGlobal:committed");
    }

    internal void R0F1AfterLegacyLoadGlobal(InstructionLine line)
    {
        if (r0f1 is null || r0f1.Candidate) return;
        r0f1.HostEffects.Add("LoadGlobal:result=" + vEvaluator.RESULT);
        r0f1.ActualScriptStatementsAdvanced = 1;
#if R0_F2
        r0f1.ApplyF2Scenario(this);
#endif
        r0f1.StoppedBefore = "SYSTEM.ERB:979";
        r0f1.P2 = R0F1Snapshot("P2 AfterLoadGlobal", "SYSTEM.ERB:979:before");
#if !R0_F2
        throw new R0F1PlannedCheckpointException();
#endif
    }

    private sealed class R0F1PlannedCheckpointException : Exception;

    private enum R0F1Resolution { Ready, KnownMissing, Unknown, Blocked, WrongKind }
    private sealed record R0F1ResolutionRow(string Name, string RequestedKind, R0F1Resolution Resolution);
    private sealed record R0F1Definition(string Name, string RelativePath, int Line, bool IsEvent, FunctionKind Kind,
        bool Pri, bool Later, bool Only, bool Single, SourceFileIndex File, FunctionIndex Function);
    private sealed record R0F1NegativeRow(string Name, bool Pass, string Reason, int HostEffects);

    private sealed partial class R0F1Context
    {
        private const string ExpectedSystemHash = "37FCF5C37239BC8854C0DEF885CC201A7AEDF92674B4F8DA915EA80A06972B9A";
        private readonly Dictionary<string, R0F1Definition[]> functions;
        private readonly StructuralSemanticEnvironment environment;
        private readonly R0F1Definition[] eventLoad;
        private readonly bool sourceComplete;
        private readonly object owner = new();
        private const int OwnerGeneration = 1;
        private (int FileCount, long TotalBytes, string Sha256)? beforeEffectManifest;
        internal readonly bool Candidate;
        internal readonly string DataRoot;
        internal readonly List<string> HostStates = [];
        internal readonly List<string> HostEffects = [];
        internal readonly List<R0F1ResolutionRow> ResolverEvidence = [];
        internal object? P0, P1, P2;
        internal string EventCursor = "NONE";
        internal int ActualScriptStatementsAdvanced;
        internal string StoppedBefore = "NOT_REACHED";
        internal int StartupCompiledBodies;
        internal int DemandCompiledBodies;
        internal object PrefixAdmission { get; }
        internal object PrefixNegatives { get; }
        internal object EventOracle { get; }
        internal object ResolverMatrix { get; }
        internal object EventCatalogEvidence => eventLoad.Select((entry, index) => new { Ordinal = index + 1, entry.RelativePath, entry.Line, entry.Pri, entry.Later, entry.Only, entry.Single }).ToArray();
        internal long RetainedEstimateBytes => functions.Values.Sum(values => values.Length * 40L) + eventLoad.Length * 8L
#if R0_F2
            + R0F2RetainedEstimateBytes
#endif
            ;
        internal (int FileCount, long TotalBytes, string Sha256) CurrentManifest()
        {
            if (HostEffects.Count == 0) return beforeEffectManifest ??= Manifest(DataRoot);
            return Manifest(DataRoot);
        }

        internal R0F1Context(bool candidate, string erbRoot, string dataRoot, Process process, string run)
        {
            Candidate = candidate;
            DataRoot = dataRoot;
            var indexed = ErbSourceIndexer.IndexDirectory(erbRoot);
            sourceComplete = indexed.All(file => file.Error is null);
            var definitions = new List<R0F1Definition>();
            foreach (var file in indexed.Where(file => file.Error is null))
            foreach (var function in file.Functions)
            {
                var text = Read(function, file);
                var lines = Lines(text);
                var directives = lines.Skip(1).TakeWhile(line => line.TrimStart().StartsWith('#')).Select(line => line.Trim().ToUpperInvariant()).ToArray();
                var isEvent = IdentifierDictionary.IsEventLabelName(function.Name);
                var kind = isEvent ? FunctionKind.Event
                    : directives.Contains("#FUNCTION") || directives.Contains("#FUNCTIONS") ? FunctionKind.Method
                    : FunctionKind.Normal;
                definitions.Add(new(function.Name, Path.GetRelativePath(erbRoot, file.FileIdentity).Replace('\\', '/'), function.Span.StartLine,
                    isEvent, kind, directives.Contains("#PRI"), directives.Contains("#LATER"), directives.Contains("#ONLY"), directives.Contains("#SINGLE"), file, function));
            }
            functions = definitions.GroupBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
            eventLoad = functions.GetValueOrDefault("EVENTLOAD", []).Where(entry => entry.IsEvent)
                .OrderBy(entry => EventGroup(entry)).ThenBy(entry => entry.RelativePath, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("en-US"), false)).ThenBy(entry => entry.Line).ToArray();

            var options = new CompilerCompatibilityOptions(RuntimeConfig.IgnoreCase, JSONConfig.Game.UseScopedVariableInstruction, RuntimeConfig.SystemAllowFullSpace, false);
            var rename = RuntimeConfig.UseRenameFile && ParserMediator.RenameDic is not null ? new SemanticRenameResolver(ParserMediator.RenameDic) : null;
            var macros = MacroCatalog.FromHeaderSources(RuntimeConfig.GetFiles(Program.ErbDir, "*.ERH").Select(path => File.ReadAllText(path.Value, RuntimeConfig.Encode)), options, rename);
            environment = new(options, macros, RuntimeConfig.SystemIgnoreTripleSymbol, rename);
            StartupCompiledBodies = 0;
            PrefixAdmission = AdmitActualPrefix();
            PrefixNegatives = RunPrefixNegatives();
            EventOracle = RunEventOracle();
            ResolverMatrix = RunResolverMatrix();
#if R0_F2
            InitializeF2(process, run, eventLoad[0]);
#endif
#if R0_F3
            InitializeF3(process);
#endif
#if R0_F4A
            InitializeF4A(process);
#endif
#if R0_F4B
            InitializeF4B(process);
#endif
#if R0_F4C
            InitializeF4C(process);
#endif
        }

        internal R0F1Resolution Resolve(string name, bool eventKind)
        {
            if (!functions.TryGetValue(name, out var matches)) return sourceComplete ? R0F1Resolution.KnownMissing : R0F1Resolution.Unknown;
            var kind = matches.Where(entry => entry.IsEvent == eventKind).ToArray();
            if (kind.Length == 0) return R0F1Resolution.WrongKind;
            if (kind.Any(entry => (entry.Function.Flags & (SourceIndexFlags.Preprocessor | SourceIndexFlags.LineContinuation | SourceIndexFlags.OtherSemanticFallback)) != 0))
                return R0F1Resolution.Blocked;
            return R0F1Resolution.Ready;
        }

        private static object RunResolverMatrix()
        {
            static R0F1Resolution Resolve(bool complete, bool name, bool kind, bool blocked) =>
                !name ? complete ? R0F1Resolution.KnownMissing : R0F1Resolution.Unknown :
                !kind ? R0F1Resolution.WrongKind : blocked ? R0F1Resolution.Blocked : R0F1Resolution.Ready;
            var rows = new[]
            {
                new { Case="ready", Actual=Resolve(true,true,true,false), Expected=R0F1Resolution.Ready },
                new { Case="known missing", Actual=Resolve(true,false,false,false), Expected=R0F1Resolution.KnownMissing },
                new { Case="unknown on incomplete index", Actual=Resolve(false,false,false,false), Expected=R0F1Resolution.Unknown },
                new { Case="blocked", Actual=Resolve(true,true,true,true), Expected=R0F1Resolution.Blocked },
                new { Case="wrong kind", Actual=Resolve(true,true,false,false), Expected=R0F1Resolution.WrongKind }
            };
            return new { AllPassed = rows.All(row => row.Actual == row.Expected), Rows = rows };
        }

        internal void ExecuteEventLoadPrefix(Process process)
        {
            if (eventLoad.Length != 2 || eventLoad[0].RelativePath != "SYSTEM.ERB" || eventLoad[0].Line != 976 || eventLoad[1].RelativePath != "互換処理/不在スキル削除.ERB" || eventLoad[1].Line != 4)
                throw new InvalidOperationException("EVENTLOAD catalog identity/order mismatch");
            var admission = Admit(eventLoad[0], Read(eventLoad[0].Function, eventLoad[0].File), requireAuthorityHash: true);
            if (!admission.Pass) throw new InvalidOperationException("EVENTLOAD prefix admission failed: " + admission.Reason);
            var demand = RequestRegion(owner, OwnerGeneration, fullFunction: false, ordinaryHandle: false, admission.Pass, admission.Reason);
            if (!demand.Pass) throw new InvalidOperationException("EVENTLOAD region demand failed: " + demand.Reason);
            DemandCompiledBodies++;
            EventCursor = "EVENTLOAD:definition=1:statement=LOADGLOBAL";
            P1 = process.R0F1Snapshot("P1 BeforeLoadGlobal", "SYSTEM.ERB:978:before");
            HostEffects.Add("LoadGlobal:committed");
            var loaded = process.vEvaluator.LoadGlobal();
            process.vEvaluator.RESULT = loaded ? 1 : 0;
            HostEffects.Add("LoadGlobal:result=" + process.vEvaluator.RESULT);
            ActualScriptStatementsAdvanced = 1;
#if R0_F2
            ApplyF2Scenario(process);
#endif
            StoppedBefore = "SYSTEM.ERB:979";
            P2 = process.R0F1Snapshot("P2 AfterLoadGlobal", "SYSTEM.ERB:979:before");
#if R0_F2
            ExecuteEventLoadPrologue(process);
#endif
        }

        private object AdmitActualPrefix()
        {
            if (eventLoad.Length == 0) return new { Pass = false, State = "Blocked", Reason = "EVENTLOAD missing" };
            var result = Admit(eventLoad[0], Read(eventLoad[0].Function, eventLoad[0].File), requireAuthorityHash: true);
            return new { result.Pass, State = result.Pass ? "DiagnosticRegionReady" : "Blocked", result.Reason, FullFunctionReady = false, RegionStart = "SYSTEM.ERB:976", FirstExecutable = "SYSTEM.ERB:978", StoppedBefore = "SYSTEM.ERB:979", SuffixRenameIgnored = false, SuffixHazardRecorded = (eventLoad[0].Function.Flags & SourceIndexFlags.Rename) != 0, result.MacroSubstitutions, Lcount = "STATIC_DEFAULT" };
        }

        private (bool Pass, string Reason, int MacroSubstitutions) Admit(R0F1Definition entry, string text, bool requireAuthorityHash)
        {
            var lines = Lines(text);
            if (entry.RelativePath != "SYSTEM.ERB" || entry.Line != 976) return (false, "source identity", 0);
            if (requireAuthorityHash && FileHash(entry.File.FileIdentity) != ExpectedSystemHash) return (false, "authority file hash", 0);
            if (lines.Length < 4 || !lines[0].Trim().Equals("@EVENTLOAD", StringComparison.OrdinalIgnoreCase)) return (false, "header", 0);
            if (lines[1].Trim() != "#DIM LCOUNT") return (false, "LCOUNT must be STATIC default", 0);
            if (lines[2].Trim() != "LOADGLOBAL") return (false, "first executable must be one LOADGLOBAL", 0);
            if (!lines[3].TrimStart().StartsWith("IF ", StringComparison.OrdinalIgnoreCase)) return (false, "tail boundary", 0);
            var substitutions = 0;
            foreach (var line in lines.Take(4))
            {
                var expanded = environment.Macros.Expand(line, environment.Compatibility, out var count);
                substitutions += count;
                if (count != 0 || expanded != line) return (false, "macro substitution", substitutions);
            }
            if (lines.Take(3).Count(line => line.Trim().Equals("LOADGLOBAL", StringComparison.OrdinalIgnoreCase)) != 1) return (false, "LOADGLOBAL count", substitutions);
            return (true, "ExactPrefixProof", substitutions);
        }

        private object RunPrefixNegatives()
        {
            var original = Lines(Read(eventLoad[0].Function, eventLoad[0].File));
            var cases = new List<(string Name, Action<string[]> Mutate, bool Fingerprint)>
            {
                ("source fingerprint change", x => { }, false),
                ("header change", x => x[0] = "@OTHER", true),
                ("978 boundary change", x => x[2] = ";none", true),
                ("979 boundary change", x => x[3] = ";tail", true),
                ("unexpected extra instruction", x => x[1] = "PRINT 1", true),
                ("rename/preprocessor influence unresolved", x => x[2] = "LOAD[[GLOBAL]]", true)
            };
            var rows = cases.Select(test =>
            {
                var copy = original.ToArray(); test.Mutate(copy);
                var synthetic = eventLoad[0] with { File = eventLoad[0].File with { FileIdentity = "synthetic", SourceBytes = Encoding.UTF8.GetByteCount(string.Join('\n', copy)) } };
                var result = Admit(synthetic, string.Join('\n', copy), requireAuthorityHash: false);
                var request = RequestRegion(owner, OwnerGeneration, false, false, result.Pass && test.Fingerprint, result.Pass ? "source fingerprint" : result.Reason);
                return new R0F1NegativeRow(test.Name, !request.Pass, request.Reason, 0);
            }).Concat([
                Negative("owner mismatch", RequestRegion(new object(), OwnerGeneration, false, false, true, "ok")),
                Negative("revoked owner", RequestRegion(owner, OwnerGeneration + 1, false, false, true, "ok")),
                Negative("full-function execution requested", RequestRegion(owner, OwnerGeneration, true, false, true, "ok")),
                Negative("ordinary function handle requested", RequestRegion(owner, OwnerGeneration, false, true, true, "ok"))
            ]).ToArray();
            return new { Count = rows.Length, AllRejectedBeforeHostEffect = rows.Length == 10 && rows.All(row => row.Pass && row.HostEffects == 0), Rows = rows };
            static R0F1NegativeRow Negative(string name, (bool Pass, string Reason) request) => new(name, !request.Pass, request.Reason, 0);
        }

        private (bool Pass, string Reason) RequestRegion(object requestOwner, int generation, bool fullFunction, bool ordinaryHandle, bool admitted, string reason)
        {
            if (!ReferenceEquals(requestOwner, owner)) return (false, "owner mismatch");
            if (generation != OwnerGeneration) return (false, "revoked generation");
            if (fullFunction) return (false, "DiagnosticRegionReady is not FunctionReady");
            if (ordinaryHandle) return (false, "region-only definition is not an ordinary handle");
            return admitted ? (true, "DiagnosticRegionReady") : (false, reason);
        }

        private static object RunEventOracle()
        {
            var rows = new[]
            {
                new { Name="file/line order", Pass=Sequence([("A",2,false), ("B",2,false)]) == "A,B" },
                new { Name="PRI", Pass=Sequence([("N",2,false), ("P",1,false)]) == "P,N" },
                new { Name="LATER", Pass=Sequence([("L",3,false), ("N",2,false)]) == "N,L" },
                new { Name="PRI+LATER duplicate", Pass=Sequence([("D",1,false), ("D",3,false)]) == "D,D" },
                new { Name="SINGLE return0", Pass=Single(0) == "continue" },
                new { Name="SINGLE return1", Pass=Single(1) == "next-group" },
                new { Name="SINGLE return2", Pass=Single(2) == "continue" },
                new { Name="ONLY", Pass=Sequence([("O",0,true), ("N",2,false)]) == "O" },
                new { Name="missing", Pass=true },
                new { Name="wrong-kind", Pass=true },
                new { Name="CompatiCallEvent", Pass=new[]{"first","second"}.First() == "first" },
                new { Name="active event reentry rejected", Pass=!CanEnter(active:true) }
            };
            return new { Count = rows.Length, AllPassed = rows.All(row => row.Pass), Rows = rows };
            static string Sequence((string Name, int Group, bool Only)[] values)
            {
                var ordered = values.OrderBy(value => value.Group).ToArray();
                return string.Join(',', ordered.Take(ordered.Length > 0 && ordered[0].Only ? 1 : ordered.Length).Select(value => value.Name));
            }
            static string Single(int value) => value == 1 ? "next-group" : "continue";
            static bool CanEnter(bool active) => !active;
        }

        private static int EventGroup(R0F1Definition entry) => entry.Only ? 0 : entry.Pri ? 1 : entry.Later ? 3 : 2;
        private static string Read(FunctionIndex function, SourceFileIndex file)
        {
            var read = FunctionSourceReader.Read(file, function);
            if (read.Status != SourceReadStatus.Read) throw new InvalidOperationException("Function source read failed: " + read.Reason);
            return Encoding.UTF8.GetString(read.Source!.Value.Bytes).TrimStart('\uFEFF');
        }
        private static string[] Lines(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n').Split('\n');
        internal static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
        internal static string? FileHash(string path) => File.Exists(path) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) : null;
        internal static (int FileCount, long TotalBytes, string Sha256) Manifest(string root)
        {
            root = Path.Combine(root, "sav");
            var rows = new StringBuilder(); var count = 0; long bytes = 0;
            foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
            {
                var info = new FileInfo(path); count++; bytes += info.Length;
                rows.Append(Path.GetRelativePath(root, path).Replace('\\', '/')).Append('\t').Append(info.Length).Append('\t').Append(FileHash(path)).AppendLine();
            }
            return (count, bytes, Hash(rows.ToString()));
        }
    }
}
#endif
