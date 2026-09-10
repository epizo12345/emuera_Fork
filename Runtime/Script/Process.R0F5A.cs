#if R0_F5A
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using MinorShift.Emuera.GameData.Variable;
using MinorShift.Emuera.Runtime.Diagnostics;
using MinorShift.Emuera.Runtime.Script.Statements;
using MinorShift.Emuera.Runtime.Script.Statements.Variable;
using RuntimeConfig = MinorShift.Emuera.Runtime.Config.Config;

namespace MinorShift.Emuera.GameProc;

internal sealed partial class Process
{
    private sealed partial class R0F1Context
    {
        private const string F5Base = "VERUP_BASE";
        private const string F5Change = "VERUP_CHANGE_CSVNO";
        private const string F5Version = "GET_GAME_VERSION_P";
        private const string F5Temp = "VERUP_TEMP";
        private const string F5ForChara = "VERUP_FOR_CHARA";
        private const string F5ForCharaTemp = "VERUP_FOR_CHARA_TEMP";
        private const string F5CheckCsv = "CHECK_EXIST_CSV";

        private sealed record F5CharacterRow(int Sequence, long Character, long No, long Version,
            long VersionPart, long Helper, string BranchDigest, string StateDigest);
        private sealed record F5WriteRow(int Sequence, string Function, int SourceLine, string Variable,
            long[] Indices, long Before, long After);
        private sealed record F5Point(string Name, string ProgramCounter, string StateSha256,
            string VariablesSha256, string CharacterSha256, long Result0, string ResultsSha256,
            string RngSha256, long RngCalls, long ClockCalls, string FrameSha256);

        internal bool F5AEnabled;
        internal object? F5AEvidence { get; private set; }

        private VariableToken f5GameBase = null!, f5Cflag = null!, f5No = null!, f5Flag = null!;
        private VariableToken f5Talent = null!, f5Cstr = null!;
        private long f5VersionSlot, f5HelperSlot, f5FlagHelperSlot, f5RoleSlot;
        private long[] f5BodySlots = [];
        private long f5GameBaseValue, f5LastLoadValue, f5EntryCharacterCount;
        private long f5InitialFlagHelper;
        private CompactNormalHandle f5BaseHandle, f5ChangeHandle, f5TempHandle, f5ForCharaHandle, f5ForCharaTempHandle, f5CheckCsvHandle;
        private readonly HashSet<string> f5Materialized = new(RuntimeConfig.StrComper);
        private readonly List<F5CharacterRow> f5V1Rows = [];
        private readonly List<F5CharacterRow> f5V4Rows = [];
        private readonly List<F5WriteRow> f5Writes = [];
        private readonly List<object> f5Blockers = [];
        private bool f5Prepared, f5LegacyActive, f5V1Completed, f5V2Completed, f5V3Completed, f5V4Completed;
        private string f5LastCompleted = "V0";
        private string f5StoppedBefore = "NOT_REACHED";
        private F5Point? f5V0, f5V1, f5V2, f5V3, f5V4;
        private object? f5VersionPath, f5V5Inventory;

        internal void ExecuteF5A(Process process)
        {
            if (!F5AEnabled) throw new InvalidOperationException("R0-F5A was not enabled");
            if (!Candidate)
            {
#if R0_F5A2
                if (F5A2Enabled) { FinishF5A2(process); return; }
#endif
                if (!f5LegacyActive || f5StoppedBefore == "NOT_REACHED")
                    throw new InvalidOperationException("R0-F5A Legacy did not reach its planned boundary");
                FinishF5A(process);
                return;
            }

            PrepareF5A(process);
            f5Materialized.Add(F5Base);
            f5BaseHandle = F5Handle(70_000, F5Base);
            f5ChangeHandle = F5Handle(70_001, F5Change);
            f5TempHandle = F5Handle(70_002, F5Temp);
            f5ForCharaHandle = F5Handle(70_003, F5ForChara);
            f5ForCharaTempHandle = F5Handle(70_004, F5ForCharaTemp);
            f5CheckCsvHandle = F5Handle(70_005, F5CheckCsv);
            Enter(new(f5BaseHandle, 1008, "SYSTEM.ERB:1007"), 2, true);
            f5V0 = CaptureF5Point(process, "V0", "VERUP_BASE.ERB:2:entry");

            if (!PreflightV1(process))
            {
                f5StoppedBefore = "VERUP_BASE.ERB:3 FOR LCOUNT / first actual VERUP_CHANGE_CSVNO effect";
                FinishF5A(process);
                return;
            }
            RunCandidateV1(process);
            f5V1Completed = true; f5LastCompleted = "V1";
            f5V1 = CaptureF5Point(process, "V1", "VERUP_BASE.ERB:7:before");

            if (f5LastLoadValue < f5GameBaseValue)
            {
                f5Blockers.Add(new { Subregion = "V2", Source = "VERUP_BASE.ERB:8", Reason = "actual version migration requires progressive band admission" });
                f5StoppedBefore = "VERUP_BASE.ERB:8 first actual version migration";
                FinishF5A(process);
                return;
            }
            f5V2Completed = true; f5LastCompleted = "V2";
            f5V2 = CaptureF5Point(process, "V2", "VERUP_BASE.ERB:14:before");

            RunCandidateV3(process);
            f5V3Completed = true; f5LastCompleted = "V3";
            f5V3 = CaptureF5Point(process, "V3", "VERUP_BASE.ERB:15:before");

            if (!PreflightV4(process))
            {
                f5StoppedBefore = "VERUP_BASE.ERB:15 FOR LCOUNT / first actual VERUP_FOR_CHARA effect";
                FinishF5A(process);
                return;
            }
            RunCandidateV4(process);
            f5V4Completed = true; f5LastCompleted = "V4";
            f5V4 = CaptureF5Point(process, "V4", "VERUP_BASE.ERB:19:before");
#if R0_F5A2
            if (F5A2Enabled) { ExecuteCandidateF5A2(process); return; }
#endif
            f5V5Inventory = BuildV5Inventory(process);
            Enter(new(f5CheckCsvHandle, 20, "VERUP_BASE.ERB:19"), 251, true);
            f5Blockers.Add(new { Subregion = "V5", Source = "VERUP_FUNCTION.ERB:251 #DIM DYNAMIC LCOUNT", Reason = "actual CHECK_EXIST_CSV per-character/CSV rewrite and SAVEGLOBAL closure is not admitted in this bounded slice", EffectsInsideBlockedSubregion = 0 });
            f5StoppedBefore = "VERUP_FUNCTION.ERB:251 CHECK_EXIST_CSV prologue before first effect";
            FinishF5A(process);
        }

        internal void ObserveF5ALegacyInstruction(Process process, InstructionLine line, bool before)
        {
            if (!F5AEnabled || Candidate || !before || line.Position is not { } p) return;
            var file = Path.GetFileName(p.Filename);
            if (!f5LegacyActive)
            {
                if (!file.Equals("SYSTEM.ERB", StringComparison.OrdinalIgnoreCase) || p.LineNo != 1007) return;
                PrepareF5A(process);
                f5LegacyActive = true;
                f5Materialized.Add(F5Base);
                f5V0 = CaptureF5Point(process, "V0", "VERUP_BASE.ERB:2:entry");
                if (!PreflightV1(process))
                {
                    f5StoppedBefore = "VERUP_BASE.ERB:3 FOR LCOUNT / first actual VERUP_CHANGE_CSVNO effect";
                    throw new R0F1PlannedCheckpointException();
                }
                return;
            }
#if R0_F5A2
            if (F5A2Enabled && ObserveF5A2LegacyInstruction(process, line, file, p.LineNo)) return;
#endif

            if (file.Equals("VERUP_BASE.ERB", StringComparison.OrdinalIgnoreCase))
            {
                var current = process.state.CurrentCalled.FunctionName;
                if (current.Equals(F5Change, RuntimeConfig.StringComparison) && p.LineNo == 1471)
                {
                    f5Materialized.Add(F5Change); f5Materialized.Add(F5Version);
                    var character = G4Private(process.state.CurrentCalled, "L_CHARA", process);
                    f5V1Rows.Add(BuildF5CharacterRow(process, f5V1Rows.Count + 1, character, "V1"));
                }
                else if (current.Equals(F5ForChara, RuntimeConfig.StringComparison) && p.LineNo == 1456)
                {
                    f5Materialized.Add(F5ForChara); f5Materialized.Add(F5ForCharaTemp); f5Materialized.Add(F5Version);
                    var character = G4Private(process.state.CurrentCalled, "L_CHARA", process);
                    f5V4Rows.Add(BuildF5CharacterRow(process, f5V4Rows.Count + 1, character, "V4"));
                }
                else if (current.Equals(F5Base, RuntimeConfig.StringComparison) && p.LineNo == 7)
                {
                    f5V1Completed = true; f5LastCompleted = "V1";
                    f5V1 = CaptureF5Point(process, "V1", "VERUP_BASE.ERB:7:before");
                }
                else if (current.Equals(F5Base, RuntimeConfig.StringComparison) && p.LineNo == 14)
                {
                    f5V2Completed = f5LastLoadValue >= f5GameBaseValue; f5LastCompleted = f5V2Completed ? "V2" : f5LastCompleted;
                    f5V2 = CaptureF5Point(process, "V2", "VERUP_BASE.ERB:14:before");
                    f5Materialized.Add(F5Temp); f5Materialized.Add(F5Version);
                }
                else if (current.Equals(F5Base, RuntimeConfig.StringComparison) && p.LineNo == 15)
                {
                    f5V3Completed = true; f5LastCompleted = "V3";
                    f5V3 = CaptureF5Point(process, "V3", "VERUP_BASE.ERB:15:before");
                    if (!PreflightV4(process))
                    {
                        f5StoppedBefore = "VERUP_BASE.ERB:15 FOR LCOUNT / first actual VERUP_FOR_CHARA effect";
                        throw new R0F1PlannedCheckpointException();
                    }
                }
                else if ((current.Equals(F5Base, RuntimeConfig.StringComparison) && p.LineNo == 19)
#if R0_F5A2
                    || (F5A2Enabled && p.LineNo == 20)
#endif
                    )
                {
                    f5V4Completed = true; f5LastCompleted = "V4";
                    f5V4 = CaptureF5Point(process, "V4", "VERUP_BASE.ERB:20:before");
                    f5V5Inventory = BuildV5Inventory(process);
#if R0_F5A2
                    if (F5A2Enabled)
                    {
                        PrepareF5A2(process);
                        f5a2W0 = CaptureF5Point(process, "W0", "VERUP_BASE.ERB:20 before CHECK_EXIST_CSV");
                        if (!f5a2V5Ready)
                        {
                            f5a2StoppedBefore = "VERUP_BASE.ERB:19 CHECK_EXIST_CSV first actual effect";
                            throw new R0F1PlannedCheckpointException();
                        }
                        return;
                    }
#endif
                    f5Materialized.Add(F5CheckCsv);
                    f5Blockers.Add(new { Subregion = "V5", Source = "VERUP_BASE.ERB:19 CALL CHECK_EXIST_CSV", Reason = "actual CHECK_EXIST_CSV per-character/CSV rewrite and SAVEGLOBAL closure is not admitted in this bounded slice", EffectsInsideBlockedSubregion = 0 });
                    f5StoppedBefore = "VERUP_BASE.ERB:19 CALL CHECK_EXIST_CSV";
                    throw new R0F1PlannedCheckpointException();
                }
            }
            if (process.state.CurrentCalled.FunctionName.Equals(F5CheckCsv, RuntimeConfig.StringComparison))
            {
#if R0_F5A2
                if (F5A2Enabled) return;
#endif
                CompleteLegacyF5ThroughV4(process);
                f5V5Inventory = BuildV5Inventory(process);
                f5Materialized.Add(F5CheckCsv);
                f5Blockers.Add(new { Subregion = "V5", Source = "VERUP_FUNCTION.ERB:251 #DIM DYNAMIC LCOUNT", Reason = "actual CHECK_EXIST_CSV per-character/CSV rewrite and SAVEGLOBAL closure is not admitted in this bounded slice", EffectsInsideBlockedSubregion = 0 });
                f5StoppedBefore = "VERUP_FUNCTION.ERB:251 CHECK_EXIST_CSV prologue before first effect";
                throw new R0F1PlannedCheckpointException();
            }
        }

        private void CompleteLegacyF5ThroughV4(Process process)
        {
            if (f5V1Rows.Count == 0)
                for (long i = 0; i < f5EntryCharacterCount; i++) f5V1Rows.Add(BuildF5CharacterRow(process, f5V1Rows.Count + 1, i, "V1"));
            if (f5V4Rows.Count == 0)
                for (long i = 0; i < process.vEvaluator.CHARANUM; i++) f5V4Rows.Add(BuildF5CharacterRow(process, f5V4Rows.Count + 1, i, "V4"));
            f5Materialized.UnionWith([F5Change, F5Version, F5Temp, F5ForChara, F5ForCharaTemp]);
            f5V1Completed = f5V2Completed = f5V3Completed = f5V4Completed = true;
            f5LastCompleted = "V4";
            f5V1 ??= CaptureF5Point(process, "V1", "VERUP_BASE.ERB:7:before");
            f5V2 ??= CaptureF5Point(process, "V2", "VERUP_BASE.ERB:14:before");
            f5V3 ??= CaptureF5Point(process, "V3", "VERUP_BASE.ERB:15:before");
            f5V4 ??= CaptureF5Point(process, "V4", "VERUP_BASE.ERB:19:before");
        }

        private void PrepareF5A(Process process)
        {
            if (f5Prepared) return;
            if (StoppedBefore != "SYSTEM.ERB:1007 CALL VERUP_BASE")
                throw new InvalidOperationException("R0-F5A requires the fresh F4G4 SYSTEM1007 boundary");
            f5GameBase = F5Token(process, "GAMEBASE_VERSION", true, false);
            f5Cflag = F5Token(process, "CFLAG", true, true);
            f5No = F5Token(process, "NO", true, false);
            f5Flag = F5Token(process, "FLAG", true, true);
            f5Talent = F5Token(process, "TALENT", true, true);
            f5Cstr = F5Token(process, "CSTR", false, false);
            f5VersionSlot = F5Keyword(process, VariableCode.CFLAG, "本体バージョン情報");
            f5HelperSlot = F5Keyword(process, VariableCode.CFLAG, "本体バージョン管理補助情報");
            f5FlagHelperSlot = F5Keyword(process, VariableCode.FLAG, "バージョン管理補助");
            f5RoleSlot = F5Keyword(process, VariableCode.CSTR, "ロール");
            f5BodySlots = new[] { "頭", "目", "口", "腕", "足" }.Select(x => F5Keyword(process, VariableCode.TALENT, x)).ToArray();
            f5GameBaseValue = F5Read(process, f5GameBase, f5GameBase.Dimension == 0 ? [] : [0]);
            f5LastLoadValue = process.vEvaluator.VariableData.LastLoadVersion;
            f5EntryCharacterCount = process.vEvaluator.CHARANUM;
            f5InitialFlagHelper = F5Read(process, f5Flag, [f5FlagHelperSlot]);
            f5VersionPath = new
            {
                LastLoadVersion = f5LastLoadValue,
                GameBaseVersion = f5GameBaseValue,
                VersionMigrationBranchTaken = f5LastLoadValue < f5GameBaseValue,
                LessThan1000000 = f5LastLoadValue < 1_000_000,
                LessThan0309154 = f5LastLoadValue < 309_154,
                CharacterCount = f5EntryCharacterCount,
                Source = "Data/CSV/GameBase.csv + decoded save219 + VERUP_BASE.ERB:7-12"
            };
            RequireF5Definition(F5Base, "互換処理/VERUP_BASE.ERB", 1);
            RequireF5Definition(F5Change, "互換処理/VERUP_BASE.ERB", 1469);
            RequireF5Definition(F5Temp, "互換処理/VERUP_BASE.ERB", 1434);
            RequireF5Definition(F5ForChara, "互換処理/VERUP_BASE.ERB", 1454);
            RequireF5Definition(F5ForCharaTemp, "互換処理/VERUP_BASE.ERB", 3906);
            RequireF5Definition(F5Version, "互換処理/VERUP_FUNCTION.ERB", 35);
            RequireF5Definition(F5CheckCsv, "互換処理/VERUP_FUNCTION.ERB", 250);
            f5Prepared = true;
        }

        private bool PreflightV1(Process process)
        {
            f5Materialized.Add(F5Change); f5Materialized.Add(F5Version);
            for (long character = 0; character < f5EntryCharacterCount; character++)
            {
                var version = F5Read(process, f5Cflag, [character, f5VersionSlot]);
                var part = version / 1_000_000;
                var helper = F5Read(process, f5Cflag, [character, f5HelperSlot]);
                var no = F5Read(process, f5No, [character]);
                string? source = null;
                if (version < 309_154 && no is 424 or 425 or 840 or 841) source = "VERUP_BASE.ERB:1474";
                else if (part < 309) source = "VERUP_BASE.ERB:1485 CALL CONVERT_CSVNO_OLD";
                else if (part < 313 && no is 4685 or 4776 or 4845) source = "VERUP_BASE.ERB:1499";
                else if (part < 501 && (part < 500 || helper == 0)) source = "VERUP_BASE.ERB:1509 VARS TEMP_SKILLS";
                else if (part < 503) source = "VERUP_BASE.ERB:1541 actual 000503 compatibility branch";
                else if (part < 504) source = "VERUP_BASE.ERB:1557 actual 000504 compatibility branch";
                else if (part < 505 && no == 3315) source = "VERUP_BASE.ERB:1569";
                else if (part < 507 && no is >= 4024 and <= 4036 or >= 4970 and <= 4973 or >= 9503 and <= 9525) source = "VERUP_BASE.ERB:1581";
                if (source is not null)
                    f5Blockers.Add(new { Subregion = "V1", Character = character, No = no, Version = version, Source = source, Reason = "first actual compatibility effect/callee is not admitted", EffectsInsideBlockedSubregion = 0 });
            }
            return f5Blockers.Count == 0;
        }

        private void RunCandidateV1(Process process)
        {
            for (long character = 0; character < f5EntryCharacterCount; character++)
            {
                var frame = Enter(new(f5ChangeHandle, 4, "VERUP_BASE.ERB:4"), 1471, true);
                frame.ArgBank[0] = character;
                f5V1Rows.Add(BuildF5CharacterRow(process, f5V1Rows.Count + 1, character, "V1"));
                Return(process, F5Change, false);
            }
            compactFrames[^1].PrivateScope!.Lcount = f5EntryCharacterCount;
            compactFrames[^1].Pc = 7;
        }

        private void RunCandidateV3(Process process)
        {
            f5Materialized.Add(F5Temp); f5Materialized.Add(F5Version);
            Enter(new(f5TempHandle, 15, "VERUP_BASE.ERB:14"), 1440, false);
            if (f5LastLoadValue < f5GameBaseValue) F5Write(process, F5Temp, 1441, f5Flag, [f5FlagHelperSlot], 0);
            if (f5LastLoadValue / 1_000_000 < 510)
            {
                if (F5Read(process, f5Flag, [f5FlagHelperSlot]) == 0) F5Write(process, F5Temp, 1444, f5Flag, [f5FlagHelperSlot], 1);
                if (F5Read(process, f5Flag, [f5FlagHelperSlot]) == 1) F5Write(process, F5Temp, 1447, f5Flag, [f5FlagHelperSlot], 2);
            }
            Return(process, F5Temp, false);
        }

        private bool PreflightV4(Process process)
        {
            f5Materialized.Add(F5ForChara); f5Materialized.Add(F5ForCharaTemp); f5Materialized.Add(F5Version);
            var maya = G4RenameInteger("キャラ:真矢");
            var ready = true;
            for (long character = 0; character < process.vEvaluator.CHARANUM; character++)
            {
                var version = F5Read(process, f5Cflag, [character, f5VersionSlot]);
                var helper = F5Read(process, f5Cflag, [character, f5HelperSlot]);
                var no = F5Read(process, f5No, [character]);
                var role = F5String(process, f5Cstr, [character, f5RoleSlot]);
                if (version < f5GameBaseValue)
                {
                    f5Blockers.Add(new { Subregion = "V4", Character = character, No = no, Version = version, Source = "VERUP_BASE.ERB:1457 CALL VERUP_FOR_CHARA_LEGACY", Reason = "actual Legacy compatibility callee is not admitted", EffectsInsideBlockedSubregion = 0 });
                    ready = false;
                }
                else if (version / 1_000_000 < 510 && helper < 1 && no == maya && role == "舞台少女_トップスタァ_EX")
                {
                    f5Blockers.Add(new { Subregion = "V4", Character = character, No = no, Version = version, Source = "VERUP_BASE.ERB:3924 CALL SET_LEARN_SKILL", Reason = "actual skill mutation callee is not admitted", EffectsInsideBlockedSubregion = 0 });
                    ready = false;
                }
                else if (version / 1_000_000 < 510 && helper < 1 && role == "人修羅")
                {
                    f5Blockers.Add(new { Subregion = "V4", Character = character, No = no, Version = version, Source = "VERUP_BASE.ERB:3929 VARS TEMP_SKILLS", Reason = "actual split/magatama migration is not admitted", EffectsInsideBlockedSubregion = 0 });
                    ready = false;
                }
            }
            return ready;
        }

        private void RunCandidateV4(Process process)
        {
            var group = new[] { G4RenameInteger("キャラ:蓮子"), G4RenameInteger("キャラ:メリー_幻想郷"), G4RenameInteger("キャラ:阿求") };
            var count = process.vEvaluator.CHARANUM;
            for (long character = 0; character < count; character++)
            {
                var frame = Enter(new(f5ForCharaHandle, 17, "VERUP_BASE.ERB:17"), 1456, true);
                frame.ArgBank[0] = character;
                f5V4Rows.Add(BuildF5CharacterRow(process, f5V4Rows.Count + 1, character, "V4"));
                Enter(new(f5ForCharaTempHandle, 1462, "VERUP_BASE.ERB:1460"), 3910, true).ArgBank[0] = character;
                var version = F5Read(process, f5Cflag, [character, f5VersionSlot]);
                var no = F5Read(process, f5No, [character]);
                if (version / 1_000_000 < 510 && group.Contains(no) && f5BodySlots.All(slot => F5Read(process, f5Talent, [character, slot]) != 1))
                    foreach (var slot in f5BodySlots) F5Write(process, F5ForCharaTemp, 3913, f5Talent, [character, slot], 1);
                Return(process, F5ForCharaTemp, false);
                F5Write(process, F5ForChara, 1462, f5Cflag, [character, f5VersionSlot], f5GameBaseValue);
                F5Write(process, F5ForChara, 1463, f5Cflag, [character, f5HelperSlot], F5Read(process, f5Flag, [f5FlagHelperSlot]));
                Return(process, F5ForChara, false);
            }
            compactFrames[^1].PrivateScope!.Lcount = count;
            compactFrames[^1].Pc = 19;
        }

        private void FinishF5A(Process process)
        {
            var guards = Candidate ? R0E1AProof.Counters.Sum() : 0;
            var partial = f5V4Completed && f5StoppedBefore == "VERUP_FUNCTION.ERB:251 CHECK_EXIST_CSV prologue before first effect";
            F5AEvidence = new
            {
                Schema = "emuera-r0f5a-verup-base-progressive-v1",
                Mode = Candidate ? "GraphFreeCandidate" : "LegacyControl",
                GateResult = partial ? "PARTIAL_PASS" : "BLOCKED",
                Scope = "VERUP_BASE_PROGRESSIVE_REAL_FLOW",
                VersionPath = f5VersionPath,
                LastCompletedSubregion = f5LastCompleted,
                StoppedBefore = f5StoppedBefore,
                VerupChangeCsvNo = new { Completed = f5V1Completed, CallCount = f5V1Rows.Count, Rows = FinalizeF5Rows(process, f5V1Rows), WriteCount = 0, FinalLcount = f5V1Completed ? f5EntryCharacterCount : 0 },
                VersionMigration = new { Entered = f5LastLoadValue < f5GameBaseValue, CompletedBlocks = 0, LastCompletedMigration = "NONE", MaterializedMigrationBodies = 0 },
                VerupTemp = new { Completed = f5V3Completed, InitialFlagHelper = f5InitialFlagHelper, FinalFlagHelper = F5Read(process, f5Flag, [f5FlagHelperSlot]), Writes = f5Writes.Where(x => x.Function == F5Temp).ToArray() },
                VerupForChara = new { Completed = f5V4Completed, CallCount = f5V4Rows.Count, Rows = FinalizeF5Rows(process, f5V4Rows), Writes = f5Writes.Where(x => x.Function is F5ForChara or F5ForCharaTemp).ToArray(), FinalLcount = f5V4Completed ? process.vEvaluator.CHARANUM : 0 },
                CheckExistCsv = new { Completed = false, Inventory = f5V5Inventory, EffectsInsideBlockedSubregion = 0 },
                CheckExistSkill = new { Completed = false, Materialized = false, Executed = false },
                Checkpoints = new { V0 = f5V0, V1 = f5V1, V2 = f5V2, V3 = f5V3, V4 = f5V4 },
                TouchedDomains = new { Final = CaptureF5Point(process, "FINAL", f5StoppedBefore), FullVariableAndCharacterHashes = true, MismatchListProducedByOfflineComparator = true },
                Materialization = new { StartupCompiled = 0, Names = f5Materialized.OrderBy(x => x, RuntimeConfig.StrComper).ToArray(), Count = f5Materialized.Count, VersionFalseMigrationBodies = 0 },
                HostEffects = new { Display = 0, Input = 0, Wait = 0, RNG = 0, Clock = 0, File = 0, SaveGlobal = 0 },
                Blockers = f5Blockers.ToArray(),
                PendingBegin = false,
                EventLoadDefinition1Completed = false,
                SecondEventLoadEntered = false,
                Guards = new { Total = guards, Categories = Candidate ? R0E1AProof.GuardSnapshot() : null },
                LegacyErbGraphAvoided = Candidate,
                LegacyRetryAfterCompact = 0,
                ProductionBridgeUsed = "NO",
                ArchitectureEscalationRequired = "NO",
                ManualRecaptureRequired = "NO",
                NextRecommendation = partial ? "CONTINUE_F5A_FROM_BLOCKER" : "STOP",
                WholeProductSuperiority = "NOT_YET_CLAIMED"
            };
            StoppedBefore = f5StoppedBefore;
            if (Candidate && guards != 0) throw new InvalidOperationException("R0-F5A forbidden guard counter changed");
        }

        private F5Point CaptureF5Point(Process process, string name, string pc)
        {
            var domains = process.vEvaluator.GetDifferentialStateHashes();
            return new(name, pc, process.GetBenchmarkStateHash(), domains.VariablesHash, domains.CharacterHash,
                process.vEvaluator.RESULT, HashStrings(process.vEvaluator.RESULTS_ARRAY), process.vEvaluator.GetR0C2RngHash(),
                process.vEvaluator.GetR0F4D3RandomCallCount(), DifferentialDeterminism.ObservationCount, R0F3FrameDigest);
        }

        private F5CharacterRow BuildF5CharacterRow(Process process, int sequence, long character, string phase)
        {
            var no = F5Read(process, f5No, [character]);
            var version = F5Read(process, f5Cflag, [character, f5VersionSlot]);
            var helper = F5Read(process, f5Cflag, [character, f5HelperSlot]);
            var branch = Hash($"{phase}:{character}:{no}:{version}:{version / 1_000_000}:{helper}");
            return new(sequence, character, no, version, version / 1_000_000, helper, branch, F5CharacterStateHash(process, character));
        }

        private object[] FinalizeF5Rows(Process process, IEnumerable<F5CharacterRow> rows) => rows.Select(row => (object)new
        {
            row.Sequence, row.Character, row.No, row.Version, row.VersionPart, row.Helper, row.BranchDigest,
            PreStateDigest = row.StateDigest, PostStateDigest = F5CharacterStateHash(process, row.Character)
        }).ToArray();

        private string F5CharacterStateHash(Process process, long character) => Hash(string.Join('|', new[]
        {
            F5Read(process, f5No, [character]).ToString(CultureInfo.InvariantCulture),
            F5Read(process, f5Cflag, [character, f5VersionSlot]).ToString(CultureInfo.InvariantCulture),
            F5Read(process, f5Cflag, [character, f5HelperSlot]).ToString(CultureInfo.InvariantCulture),
            F5String(process, f5Cstr, [character, f5RoleSlot]),
            string.Join(',', f5BodySlots.Select(slot => F5Read(process, f5Talent, [character, slot]).ToString(CultureInfo.InvariantCulture)))
        }));

        private object BuildV5Inventory(Process process)
        {
            var csvCount = F5Constant(process, "CSVCHARANUM");
            var invalidCharacters = Enumerable.Range(0, checked((int)process.vEvaluator.CHARANUM))
                .Where(i => process.vEvaluator.ExistCsv(F5Read(process, f5No, [i]), false) == 0).ToArray();
            long firstMissing = -1, missing = 0;
            for (long i = 0; i < csvCount; i++)
                if (process.vEvaluator.ExistCsv(i, false) == 0) { if (firstMissing < 0) firstMissing = i; missing++; }
            return new { CharacterCount = process.vEvaluator.CHARANUM, InvalidCharacterNoIndices = invalidCharacters,
                CsvCharacterCount = csvCount, MissingCsvNumberCount = missing, FirstMissingCsvNumber = firstMissing,
                GuaranteedHostEffect = "SAVEGLOBAL", InputBranchPreflight = invalidCharacters.Length == 0 ? "NO_INVALID_CHARACTER_NO" : "REQUIRES_DEEPER_PATH_CHECK" };
        }

        private void F5Write(Process process, string function, int line, VariableToken token, long[] indices, long value)
        {
            var before = F5Read(process, token, indices);
            token.SetValue(value, indices);
            f5Writes.Add(new(f5Writes.Count + 1, function, line, token.Name, indices, before, value));
            compactFrames[^1].Committed = true;
        }

        private CompactNormalHandle F5Handle(int id, string name)
        {
            f5Materialized.Add(name);
            return Handle(id, name);
        }

        private void RequireF5Definition(string name, string path, int line)
        {
            if (!functions.TryGetValue(name, out var entries)) throw new InvalidOperationException("R0-F5A missing source definition: " + name);
            var definition = entries.Single(x => !x.IsEvent);
            if (!definition.RelativePath.Replace('\\', '/').Equals(path, StringComparison.OrdinalIgnoreCase) || definition.Line != line)
                throw new InvalidOperationException("R0-F5A source identity mismatch: " + name);
        }

        private VariableToken F5Token(Process process, string name, bool integer, bool writable)
        {
            var token = process.idDic.GetVariableToken(name, null, false) ?? throw new InvalidOperationException("R0-F5A token missing: " + name);
            if (token.IsInteger != integer || writable && token.IsConst) throw new InvalidOperationException("R0-F5A token schema: " + name);
            return token;
        }

        private static long F5Read(Process process, VariableToken token, long[] indices) => token.GetIntValue(process.exm, indices);
        private static string F5String(Process process, VariableToken token, long[] indices) => token.GetStrValue(process.exm, indices) ?? "";
        private static long F5Keyword(Process process, VariableCode code, string name)
        {
            var value = process.vEvaluator.Constant.KeywordToInteger(code, name, -1);
            if (value < 0) throw new InvalidOperationException($"R0-F5A keyword missing: {code}:{name}");
            return value;
        }
        private long F5Constant(Process process, string name)
        {
            var token = F5Token(process, name, true, false);
            if (!token.IsConst) throw new InvalidOperationException("R0-F5A expected constant: " + name);
            return F5Read(process, token, token.Dimension == 0 ? [] : [0]);
        }
    }
}
#endif
