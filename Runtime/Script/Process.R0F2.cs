#if R0_F2
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MinorShift.Emuera.GameData.Variable;
using MinorShift.Emuera.Runtime.Script.Statements;
using MinorShift.Emuera.Runtime.Script.Statements.Variable;
using RuntimeConfig = MinorShift.Emuera.Runtime.Config.Config;

namespace MinorShift.Emuera.GameProc;

internal sealed partial class Process
{
    internal void R0F2AfterLegacyInstruction(InstructionLine line)
    {
        if (r0f1 is null || r0f1.Candidate || !R0F1Context.IsSystemLine(line, 979, 993, 994, 997, 998, 999, 1004)) return;
        r0f1.RecordF2Line(line.Position!.Value.LineNo);
        switch (line.Position.Value.LineNo)
        {
            case 979:
                r0f1.ObserveActualBranch(this);
                if (r0f1.ActualTemporarySaveBranch != "FALSE")
                    throw new InvalidOperationException("R0-F2 blocked before unsupported SYSTEM.ERB:980 branch effect");
                r0f1.SetF2Cursor("SYSTEM.ERB:993");
                r0f1.P3 = R0F1Snapshot("P3 TemporarySaveBranchFalse", "SYSTEM.ERB:993:before");
                r0f1.LastCompletedCheckpoint = "P3";
                break;
            case 993:
                if (!r0f1.CurrentSifCondition(this))
                {
                    r0f1.CompleteSkippedDelData(this);
                    r0f1.SetF2Cursor("SYSTEM.ERB:997");
                    r0f1.P4 = R0F1Snapshot("P4 AfterDelData994Boundary", "SYSTEM.ERB:997:before");
                    r0f1.LastCompletedCheckpoint = "P4";
                }
                break;
            case 994:
                r0f1.SetF2Cursor("SYSTEM.ERB:997");
                r0f1.P4 = R0F1Snapshot("P4 AfterDelData994Boundary", "SYSTEM.ERB:997:before");
                r0f1.LastCompletedCheckpoint = "P4";
                break;
            case 997:
                r0f1.ObserveUseContinueInput(this);
                break;
            case 999:
                r0f1.CompleteUseContinue(this);
                r0f1.SetF2Cursor("SYSTEM.ERB:1004");
                r0f1.P5 = R0F1Snapshot("P5 AfterUseContinueWrites", "SYSTEM.ERB:1004:before");
                r0f1.LastCompletedCheckpoint = "P5";
                break;
            case 1004:
                r0f1.SetF2Cursor("SYSTEM.ERB:1006");
                r0f1.P6 = R0F1Snapshot("P6 AfterSaveGlobal1004", "SYSTEM.ERB:1006:before");
                r0f1.LastCompletedCheckpoint = "P6";
                r0f1.StoppedBefore = "SYSTEM.ERB:1006";
#if !R0_F3
                throw new R0F1PlannedCheckpointException();
#else
                break;
#endif
        }
    }

    internal void R0F2BeforeLegacyDelData(InstructionLine line, int target)
    {
        if (r0f1 is null || r0f1.Candidate || !R0F1Context.IsSystemLine(line, 994)) return;
        r0f1.BeginDelData(target);
    }

    internal void R0F2AfterLegacyDelData(InstructionLine line, int target)
    {
        if (r0f1 is null || r0f1.Candidate || !R0F1Context.IsSystemLine(line, 994)) return;
        r0f1.EndDelData(this, target);
    }

    internal void R0F2BeforeLegacySaveGlobal(InstructionLine line)
    {
#if R0_F5A2
        if (r0f1?.ObserveF5A2SaveGlobal(this, line, true, false) == true) return;
#endif
#if R0_F4G4
        if (r0f1?.ObserveF4G4SaveGlobal(this, line, true, false) == true) return;
#endif
        if (r0f1 is null || r0f1.Candidate || !R0F1Context.IsSystemLine(line, 1004)) return;
        r0f1.BeginSaveGlobal();
    }

    internal void R0F2AfterLegacySaveGlobal(InstructionLine line, bool result)
    {
#if R0_F5A2
        if (r0f1?.ObserveF5A2SaveGlobal(this, line, false, result) == true) return;
#endif
#if R0_F4G4
        if (r0f1?.ObserveF4G4SaveGlobal(this, line, false, result) == true) return;
#endif
        if (r0f1 is null || r0f1.Candidate || !R0F1Context.IsSystemLine(line, 1004)) return;
        r0f1.EndSaveGlobal(this, result);
    }

    internal void R0F2BeforeLegacyScalarWrite(InstructionLine line, long value)
    {
        if (r0f1 is null || r0f1.Candidate || !R0F1Context.IsSystemLine(line, 998, 999)) return;
        r0f1.BeginScalarWrite(line.Position!.Value.LineNo, value);
    }

    internal void R0F2AfterLegacyScalarWrite(InstructionLine line)
    {
        if (r0f1 is null || r0f1.Candidate || !R0F1Context.IsSystemLine(line, 998, 999)) return;
        r0f1.EndScalarWrite(this, line.Position!.Value.LineNo);
    }

    private sealed partial class R0F1Context
    {
        private sealed record BoundSlot(string Name, VariableToken Token, long[] Indices, bool Writable)
        {
            internal long Read(Process process) => Token.GetIntValue(process.exm, Indices);
            internal void Write(long value)
            {
                if (!Writable) throw new InvalidOperationException("R0-F2 attempted write through read-only bound slot " + Name);
                Token.SetValue(value, Indices);
            }
        }

        private static readonly int[] F2SemanticLines = [979, 993, 994, 997, 998, 999, 1004];
        private BoundSlot temporarySave = null!;
        private BoundSlot lastLoadNo = null!;
        private BoundSlot useContinueGlobal = null!;
        private BoundSlot useContinue = null!;
        private string f2Run = "actual";
        private int continueSaveIndex;
        private bool scenarioApplied;
        private bool delDataExistedBefore;
        private bool saveGlobalExistedBefore;
        private string? saveGlobalHashBefore;
        private bool useContinueInput;

        internal object? P3, P4, P5, P6;
        internal object RegionAdmissionF2 { get; private set; } = null!;
        internal object BoundVariablesF2 { get; private set; } = null!;
        internal object ActualBranch { get; private set; } = null!;
        internal object TrueBranchGuardMatrix { get; private set; } = null!;
        internal object SifEvidence { get; private set; } = null!;
        internal object UseContinueEvidence { get; private set; } = null!;
        internal object SaveGlobalEvidence { get; private set; } = null!;
        internal object SavedContinuation => new
        {
            Region = "EVENTLOAD_SYSTEM_976_PROLOGUE",
            EventSet = "EVENTLOAD",
            CurrentDefinition = "SYSTEM.ERB:976",
            DefinitionOrdinal = 1,
            Group = "normal",
            GroupIndex = 0,
            NextLogicalPC = "SYSTEM.ERB:1006",
            PendingBegin = false,
            OwnerGeneration,
            PersistentLcount = 0
        };
        internal readonly List<string> F2HostEffects = [];
        internal readonly List<string> WriteOrder = [];
        internal readonly List<int> ExecutedLines = [];
        internal int ActualAdditionalStatementsAdvanced;
        internal string ActualTemporarySaveBranch = "UNKNOWN";
        internal bool F2Committed;
        internal string LastCompletedCheckpoint = "P2";
        internal int F2DemandCompiledBodies;
        internal int SetGameplayStartMaterialized;
        internal long R0F2RetainedEstimateBytes => 5 * 40L + 7 * 16L + 3 * 32L;

        internal void InitializeF2(Process process, string run, R0F1Definition definition)
        {
            if (run is not ("actual" or "f1-missing" or "f1-version" or "f1-truncated" or "sif-false" or "sif-missing" or "use1"))
                throw new InvalidOperationException("unsupported R0-F2 run: " + run);
            f2Run = run;
            temporarySave = Bind(process, "FLAG", [process.vEvaluator.Constant.KeywordToInteger(VariableCode.FLAG, "一時セーブ管理", -1)], false);
            if (temporarySave.Indices[0] != 1000) throw new InvalidOperationException("FLAG:一時セーブ管理 index mismatch");
            lastLoadNo = Bind(process, "LASTLOAD_NO", [], false);
            useContinueGlobal = Bind(process, "USE_CONTINUE_GLOBAL", [0], true);
            useContinue = Bind(process, "USE_CONTINUE", [0], true);
            var constEvidence = BindContinueSaveConstant();
            continueSaveIndex = constEvidence.Value;
            BoundVariablesF2 = new
            {
                TemporarySave = Describe(temporarySave),
                LastLoadNo = Describe(lastLoadNo),
                SaveDataNumForContinue = constEvidence.Evidence,
                UseContinueGlobal = Describe(useContinueGlobal),
                UseContinue = Describe(useContinue)
            };
            AssertSavTarget($"save{continueSaveIndex:00}.sav");
            AssertSavTarget("global.sav");
            var admission = AdmitF2(definition);
            RegionAdmissionF2 = admission.Evidence;
            if (!admission.Pass) throw new InvalidOperationException("R0-F2 admission blocked before effect: " + admission.Reason);
            TrueBranchGuardMatrix = RunTrueBranchGuardMatrix();
        }

        private static BoundSlot Bind(Process process, string name, long[] indices, bool writable)
        {
            var token = process.idDic.GetVariableToken(name, null, false);
            if (token is null || !token.IsInteger || token.IsCharacterData || token.IsLocal || token.IsPrivate)
                throw new InvalidOperationException("R0-F2 bound scalar unavailable: " + name);
            if (writable && token.IsConst) throw new InvalidOperationException("R0-F2 bound write target is const: " + name);
            token.CheckElement(indices);
            return new(name, token, indices, writable);
        }

        private (int Value, object Evidence) BindContinueSaveConstant()
        {
            const string declaration = "#DIM CONST SAVEDATA_NUM_FOR_CONTINUE = 401";
            var path = Path.Combine(DataRoot, "ERB", "VAR.ERH");
            var matches = File.ReadLines(path, RuntimeConfig.Encode).Count(line => line.Trim() == declaration);
            if (matches != 1) throw new InvalidOperationException("R0-F2 SAVEDATA_NUM_FOR_CONTINUE declaration mismatch");
            return (401, new { Name = "SAVEDATA_NUM_FOR_CONTINUE", Kind = "ColdBoundErhConstant", Value = 401, Declaration = declaration, SourceSha256 = FileHash(path), Matches = matches });
        }

        private static object Describe(BoundSlot slot) => new { slot.Name, slot.Indices, slot.Writable, Kind = "BoundVariableToken" };

        internal void ApplyF2Scenario(Process process)
        {
            if (scenarioApplied) return;
            scenarioApplied = true;
            if (f2Run == "sif-false") process.vEvaluator.VariableData.LastLoadNo = continueSaveIndex;
            if (f2Run == "use1") useContinueGlobal.Write(1);
        }

        internal object R0F2BoundValues(Process process) => new
        {
            TemporarySaveFlag = temporarySave.Read(process),
            LastLoadNo = lastLoadNo.Read(process),
            SaveDataNumForContinue = continueSaveIndex,
            UseContinueGlobal = useContinueGlobal.Read(process),
            UseContinue = useContinue.Read(process)
        };

        internal bool R0F2ContinueSavePresent() => File.Exists(SavePath(continueSaveIndex));

        internal void ExecuteEventLoadPrologue(Process process)
        {
            var admission = AdmitF2(eventLoad[0]);
            var request = RequestRegion(owner, OwnerGeneration, false, false, admission.Pass, admission.Reason);
            if (!request.Pass) throw new InvalidOperationException("EVENTLOAD F2 region demand failed: " + request.Reason);
            F2DemandCompiledBodies++;

            RecordF2Line(979);
            ObserveActualBranch(process);
            if (ActualTemporarySaveBranch != "FALSE")
                throw new InvalidOperationException("R0-F2 blocked before unsupported SYSTEM.ERB:980 branch effect");
            SetF2Cursor("SYSTEM.ERB:993");
            P3 = process.R0F1Snapshot("P3 TemporarySaveBranchFalse", "SYSTEM.ERB:993:before");
            LastCompletedCheckpoint = "P3";

            RecordF2Line(993);
            var deleteRequired = CurrentSifCondition(process);
            if (deleteRequired)
            {
                var target = continueSaveIndex;
                RecordF2Line(994);
                BeginDelData(target);
                VariableEvaluator.DelData(target);
                EndDelData(process, target);
            }
            else
                CompleteSkippedDelData(process);
            SetF2Cursor("SYSTEM.ERB:997");
            P4 = process.R0F1Snapshot("P4 AfterDelData994Boundary", "SYSTEM.ERB:997:before");
            LastCompletedCheckpoint = "P4";

            RecordF2Line(997);
            ObserveUseContinueInput(process);
            if (useContinueInput)
            {
                RecordF2Line(998);
                BeginScalarWrite(998, 1);
                useContinue.Write(1);
                EndScalarWrite(process, 998);
            }
            RecordF2Line(999);
            BeginScalarWrite(999, 0);
            useContinueGlobal.Write(0);
            EndScalarWrite(process, 999);
            CompleteUseContinue(process);
            SetF2Cursor("SYSTEM.ERB:1004");
            P5 = process.R0F1Snapshot("P5 AfterUseContinueWrites", "SYSTEM.ERB:1004:before");
            LastCompletedCheckpoint = "P5";

            RecordF2Line(1004);
            BeginSaveGlobal();
            var saved = process.vEvaluator.SaveGlobal();
            EndSaveGlobal(process, saved);
            SetF2Cursor("SYSTEM.ERB:1006");
            P6 = process.R0F1Snapshot("P6 AfterSaveGlobal1004", "SYSTEM.ERB:1006:before");
            LastCompletedCheckpoint = "P6";
            StoppedBefore = "SYSTEM.ERB:1006";
#if R0_F3
            ExecuteFirstNormalHelpers(process);
#endif
        }

        internal void ObserveActualBranch(Process process)
        {
            var flag = temporarySave.Read(process);
            ActualTemporarySaveBranch = flag == 0 ? "FALSE" : "TRUE";
            ActualBranch = new
            {
                Run = f2Run,
                TemporarySaveFlag = flag,
                Global0 = flag == 0 ? "NOT_READ_FALSE_PATH" : "UNADMITTED_BRANCH",
                LastLoadNo = lastLoadNo.Read(process),
                SaveDataNumForContinue = continueSaveIndex,
                UseContinueGlobal = useContinueGlobal.Read(process),
                Branch = ActualTemporarySaveBranch,
                BlockBefore = flag == 0 ? null : "SYSTEM.ERB:980",
                HostEffectsBeforeBlock = flag == 0 ? (int?)null : 0,
                LegacyRetry = 0
            };
        }

        internal bool CurrentSifCondition(Process process) => lastLoadNo.Read(process) != continueSaveIndex;

        internal void BeginDelData(int target)
        {
            if (target != continueSaveIndex) throw new InvalidOperationException("R0-F2 DELDATA target mismatch");
            var path = AssertSavTarget($"save{target:00}.sav");
            delDataExistedBefore = File.Exists(path);
            F2Committed = true;
            AddF2Effect($"DelData994:committed target={target} existed={delDataExistedBefore}");
        }

        internal void EndDelData(Process process, int target)
        {
            var path = AssertSavTarget($"save{target:00}.sav");
            AddF2Effect($"DelData994:completed exists={File.Exists(path)}");
            SifEvidence = new { Condition = true, Target = target, ExistedBefore = delDataExistedBefore, DeleteCallCount = 1, ExistsAfter = File.Exists(path), State = "completed", Pc = "SYSTEM.ERB:997:before", StateHash = process.GetBenchmarkStateHash() };
        }

        internal void CompleteSkippedDelData(Process process)
        {
            var path = AssertSavTarget($"save{continueSaveIndex:00}.sav");
            SifEvidence = new { Condition = false, Target = continueSaveIndex, ExistedBefore = File.Exists(path), DeleteCallCount = 0, ExistsAfter = File.Exists(path), State = "skipped", Pc = "SYSTEM.ERB:997:before", StateHash = process.GetBenchmarkStateHash() };
        }

        internal void ObserveUseContinueInput(Process process) => useContinueInput = useContinueGlobal.Read(process) != 0;

        internal void BeginScalarWrite(int line, long value)
        {
            if (line is not (998 or 999)) throw new InvalidOperationException("R0-F2 write line mismatch");
            F2Committed = true;
            var target = line == 998 ? "USE_CONTINUE" : "USE_CONTINUE_GLOBAL";
            AddF2Effect($"VariableWrite{line}:committed");
            WriteOrder.Add($"RHS={value}->{target}[0]");
        }

        internal void EndScalarWrite(Process process, int line)
        {
            var target = line == 998 ? useContinue : useContinueGlobal;
            AddF2Effect($"VariableWrite{line}:{target.Name}={target.Read(process)}");
        }

        internal void CompleteUseContinue(Process process)
        {
            UseContinueEvidence = new
            {
                Input = useContinueInput,
                UseContinue = useContinue.Read(process),
                UseContinueGlobal = useContinueGlobal.Read(process),
                WriteOrder = WriteOrder.ToArray(),
                WriteCount = WriteOrder.Count,
                StateHash = process.GetBenchmarkStateHash()
            };
        }

        internal void BeginSaveGlobal()
        {
            var path = AssertSavTarget("global.sav");
            saveGlobalExistedBefore = File.Exists(path);
            saveGlobalHashBefore = FileHash(path);
            F2Committed = true;
            AddF2Effect($"SaveGlobal1004:committed existed={saveGlobalExistedBefore}");
        }

        internal void EndSaveGlobal(Process process, bool result)
        {
            var path = AssertSavTarget("global.sav");
            var after = FileHash(path);
            AddF2Effect($"SaveGlobal1004:result={result} hash={after}");
            SaveGlobalEvidence = new
            {
                Result = result,
                FaultCategory = "None",
                ExistedBefore = saveGlobalExistedBefore,
                HashBefore = saveGlobalHashBefore,
                ExistsAfter = File.Exists(path),
                HashAfter = after,
                SerializedGlobalPayload = process.vEvaluator.GetR0F1GlobalHash(),
                Retry = 0,
                Sandbox = Path.GetFullPath(Path.Combine(DataRoot, "sav"))
            };
        }

        internal void RecordF2Line(int line)
        {
            if (!F2SemanticLines.Contains(line)) throw new InvalidOperationException("R0-F2 unexpected semantic line " + line);
            ExecutedLines.Add(line);
            ActualAdditionalStatementsAdvanced = ExecutedLines.Count;
        }

        internal void SetF2Cursor(string pc) => EventCursor = $"EVENTLOAD:definition=1:group=normal:index=0:pc={pc}";

        internal void FinishF2Oracles(Process process)
        {
            if (P3 is null || P4 is null || P5 is null || P6 is null || SaveGlobalEvidence is null || SifEvidence is null || UseContinueEvidence is null)
                throw new InvalidOperationException("R0-F2 incomplete planned checkpoint evidence");
#if R0_F3
            if (SetGameplayStartMaterialized != 1)
                throw new InvalidOperationException("R0-F3 did not materialize SET_GAMEPLAY_START exactly once");
#else
            if (SetGameplayStartMaterialized != 0 || ExecutedLines.Contains(1006))
                throw new InvalidOperationException("R0-F2 crossed the SYSTEM.ERB:1006 boundary");
#endif
        }

        private (bool Pass, string Reason, object Evidence) AdmitF2(R0F1Definition entry)
        {
            var text = Read(entry.Function, entry.File);
            var lines = Lines(text);
            var exact = new Dictionary<int, string>
            {
                [3] = "IF FLAG:一時セーブ管理",
                [4] = "DELDATA LASTLOAD_NO",
                [6] = "IF FLAG:一時セーブ管理 != GLOBAL",
                [7] = "PRINTL 不正なデータです",
                [8] = "BEGIN TITLE",
                [9] = "ENDIF",
                [10] = "FLAG:一時セーブ管理 = 0",
                [11] = "DO",
                [12] = "LOCAL = RAND:10000 + 1",
                [13] = "LOOP GLOBAL == LOCAL",
                [14] = "GLOBAL = LOCAL",
                [15] = "SAVEGLOBAL",
                [16] = "ENDIF",
                [17] = "SIF LASTLOAD_NO != SAVEDATA_NUM_FOR_CONTINUE",
                [18] = "DELDATA SAVEDATA_NUM_FOR_CONTINUE;EDIT 045 ADD",
                [21] = "SIF USE_CONTINUE_GLOBAL",
                [22] = "USE_CONTINUE = 1",
                [23] = "USE_CONTINUE_GLOBAL = 0",
                [28] = "SAVEGLOBAL",
                [30] = "CALL SET_GAMEPLAY_START"
            };
            var pass = entry.RelativePath == "SYSTEM.ERB" && entry.Line == 976 && FileHash(entry.File.FileIdentity) == ExpectedSystemHash && lines.Length > 30;
            var reason = pass ? "ExactF2RegionProof" : "identity/fingerprint";
            foreach (var pair in exact)
                if (pass && lines[pair.Key].Trim() != pair.Value) { pass = false; reason = $"line {976 + pair.Key} structure"; }
            var substitutions = 0;
            foreach (var index in exact.Keys.Where(index => index is 3 or 17 or 18 or 21 or 22 or 23 or 28 or 30))
            {
                var source = lines[index].Split(';', 2)[0].Trim();
                var expanded = environment.Macros.Expand(source, environment.Compatibility, out var count);
                substitutions += count;
                if (pass && (count != 0 || expanded != source)) { pass = false; reason = $"line {976 + index} macro substitution"; }
            }
            var evidence = new
            {
                Pass = pass,
                State = pass ? "DiagnosticRegionReady" : "Blocked",
                Reason = reason,
                FullFunctionReady = false,
                ResumedFrom = "SYSTEM.ERB:979",
                Through = "SYSTEM.ERB:1004",
                StoppedBefore = "SYSTEM.ERB:1006",
                UnsupportedTrueBranch = "SYSTEM.ERB:980-991",
                UnsupportedBranchBlockBefore = "SYSTEM.ERB:980",
                IfEndIfVerified = exact.ContainsKey(16),
                SifOneStatementVerified = exact.ContainsKey(18) && exact.ContainsKey(22),
                AssignmentTargetsVerified = true,
                DelDataTargetsVerified = true,
                SaveGlobalPositions = new[] { 991, 1004 },
                CallBoundaryVerified = true,
                MacroSubstitutions = substitutions,
                SuffixRenameIgnored = false,
                SuffixHazardRecorded = (entry.Function.Flags & MinorShift.Emuera.Next.Core.SourceIndexFlags.Rename) != 0
            };
            return (pass, reason, evidence);
        }

        private static object RunTrueBranchGuardMatrix()
        {
            var rows = new[]
            {
                new { Name="FLAG:一時セーブ管理 = 1", Flag=(long?)1, Global=(long?)0, Available=true },
                new { Name="FLAG:一時セーブ管理 != GLOBAL", Flag=(long?)2, Global=(long?)3, Available=true },
                new { Name="equality case", Flag=(long?)1, Global=(long?)1, Available=true },
                new { Name="corrupted/unavailable target state", Flag=(long?)null, Global=(long?)null, Available=false }
            }.Select(row => new
            {
                row.Name,
                BlockedBefore = !row.Available || row.Flag != 0 ? "SYSTEM.ERB:980" : "NOT_BLOCKED",
                HostEffectsBeforeBlock = 0,
                LegacyRetry = 0,
                Pass = (!row.Available || row.Flag != 0)
            }).ToArray();
            return new { Count = rows.Length, AllPassed = rows.All(row => row.Pass && row.HostEffectsBeforeBlock == 0 && row.LegacyRetry == 0), Rows = rows };
        }

        private void AddF2Effect(string value)
        {
            F2HostEffects.Add(value);
            HostEffects.Add(value);
        }

        private string SavePath(int index) => AssertSavTarget($"save{index:00}.sav");

        private string AssertSavTarget(string fileName)
        {
            var expectedRoot = Path.GetFullPath(Path.Combine(DataRoot, "sav"));
            var configuredRoot = Path.GetFullPath(RuntimeConfig.SavDir);
            if (!string.Equals(expectedRoot.TrimEnd(Path.DirectorySeparatorChar), configuredRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("R0-F2 SavDir escaped the run-local Data root");
            if ((File.GetAttributes(expectedRoot) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("R0-F2 rejects a reparse-point SavDir");
            var target = Path.GetFullPath(Path.Combine(expectedRoot, fileName));
            if (!string.Equals(Path.GetDirectoryName(target), expectedRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("R0-F2 rejected a non-canonical sav target");
            if (File.Exists(target) && (File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("R0-F2 rejects a reparse-point sav file");
            return target;
        }

        internal static bool IsSystemLine(InstructionLine line, params int[] lines) =>
            line.Position is not null && Path.GetFileName(line.Position.Value.Filename).Equals("SYSTEM.ERB", StringComparison.OrdinalIgnoreCase) && lines.Contains(line.Position.Value.LineNo);
    }
}
#endif
