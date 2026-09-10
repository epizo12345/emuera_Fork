#if R0_F5A2
#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using MinorShift.Emuera.GameData.Variable;
using MinorShift.Emuera.Next.Compiler;
using MinorShift.Emuera.Next.Core;
using MinorShift.Emuera.Next.Vm;
using MinorShift.Emuera.Runtime.Diagnostics;
using MinorShift.Emuera.Runtime.Script.Data;
using MinorShift.Emuera.Runtime.Script.Statements;
using MinorShift.Emuera.Runtime.Script.Statements.Variable;
using RuntimeConfig = MinorShift.Emuera.Runtime.Config.Config;

namespace MinorShift.Emuera.GameProc;

internal sealed partial class Process
{
    private sealed partial class R0F1Context
    {
        private const string F5A2CheckChara = "CHECK_EXIST_CSV_FOR_CHARA";
        private const string F5A2CheckSkill = "CHECK_EXIST_SKILL";
        private const string F5A2GetSkill = "GET_SKILL_PROP";
        private const string F5A2GetSkills = "GETS_SKILL_PROP";
        private const string F5A2IsPu = "IS_PU_SKILLNUM";
        private const string F5A2PuNum = "PU_NUM";

        private sealed record F5A2CharacterRow(long Character, long No, long PregnancyFather, long InitialLink,
            long Persona1, long Persona2, long Persona3, long Mutation, long Awakening, long Fetus0, long Fetus1,
            string Nickname, string CsvNickname, bool NicknameMismatch, string BranchDigest, string StateDigest);
        private sealed record F5A2DomainBefore(string Name, VariableToken Token, long[] Values, string Hash);
        private sealed record F5A2MethodProgram(string Name, LinkedProgram Program, RuntimeFunctionId Entry,
            CompactNormalHandle Handle, string SourcePath, int SourceLine);

        internal bool F5A2Enabled;
        internal object? F5A2Evidence { get; private set; }

        private bool f5a2Prepared, f5a2V5Ready, f5a2V5Completed, f5a2V6Ready, f5a2V6Completed, f5a2BaseCompleted;
        private string f5a2StoppedBefore = "NOT_REACHED";
        private object? f5a2V5Preflight, f5a2V5Characters, f5a2PersonaCsv, f5a2MissingCsv, f5a2SaveGlobal;
        private object? f5a2V6Preflight, f5a2CharacterSkills, f5a2PersonaSkills, f5a2SkillCalls, f5a2NextInventory;
        private F5Point? f5a2W0, f5a2W1, f5a2W2, f5a2W3;
        private VariableToken f5a2Abl = null!, f5a2Fetus = null!, f5a2Nickname = null!, f5a2Ditem = null!;
        private VariableToken[] f5a2Cleanup = [];
        private long f5a2CsvCount, f5a2PersonaNoSlot;
        private long[] f5a2CharacterSkillSlots = [], f5a2CharacterLearnedSlots = [];
        private long[] f5a2PersonaSkillSlots = [], f5a2PersonaLearnedSlots = [], f5a2PersonaLearnedLvSlots = [];
        private readonly List<F5A2CharacterRow> f5a2CharacterRows = [];
        private readonly List<F5A2DomainBefore> f5a2DomainBefore = [];
        private readonly long[] f5a2AssignmentCounts = new long[5], f5a2ChangedCounts = new long[5];
        private readonly StringBuilder f5a2Operations = new();
        private readonly Dictionary<long, F5A2MethodProgram> f5a2SkillPrograms = [];
        private long f5a2PuStart, f5a2PuCount;
        private CompactNormalHandle f5a2CheckCharaHandle, f5a2CheckSkillHandle, f5a2GetSkillHandle, f5a2GetSkillsHandle;
        private int f5a2CharactersObserved, f5a2PersonaCsvObserved, f5a2SkillCallsObserved;
        private int f5a2CharacterSkillObserved, f5a2PersonaSkillObserved, f5a2InvalidCharacterSkills, f5a2InvalidPersonaSkills;
        private int f5a2PersonaDeleteCalls;
        private long f5a2FinalSkillCount;
        private string? f5a2SaveBeforeHash, f5a2SaveBeforePayloadHash;
        private string? f5a2PendingSkillKey;

        internal void ExecuteCandidateF5A2(Process process)
        {
            PrepareF5A2(process);
            f5a2W0 = CaptureF5Point(process, "W0", "VERUP_FUNCTION.ERB:251 CHECK_EXIST_CSV entry");
            if (!f5a2V5Ready)
            {
                f5a2StoppedBefore = "VERUP_FUNCTION.ERB:253 CHECK_EXIST_CSV first actual effect";
                FinishF5A2(process);
                return;
            }

            Enter(new(f5CheckCsvHandle, 20, "VERUP_BASE.ERB:19"), 251, true);
            for (long character = 0; character < process.vEvaluator.CHARANUM; character++)
            {
                var frame = Enter(new(f5a2CheckCharaHandle, 255, "VERUP_FUNCTION.ERB:254"), 276, true);
                frame.ArgBank[0] = character;
                f5a2CharactersObserved++;
                Return(process, F5A2CheckChara, false);
            }
            compactFrames[^1].PrivateScope!.Lcount = process.vEvaluator.CHARANUM;

            for (long slot = 0; slot < 100; slot++)
            {
                _ = F5Read(process, f5a2Ditem, [slot, f5a2PersonaNoSlot]);
                f5a2PersonaCsvObserved++;
            }
            compactFrames[^1].PrivateScope!.Lcount = 100;

            for (long id = 0; id < f5a2CsvCount; id++)
            {
                if (process.vEvaluator.ExistCsv(id, false) != 0) continue;
                for (var family = 0; family < f5a2Cleanup.Length; family++)
                    F5A2WriteCleanup(process, family, id);
            }
            compactFrames[^1].PrivateScope!.Lcount = f5a2CsvCount;
            SaveF5A2Global(process);
            Return(process, F5CheckCsv, false);
            f5a2V5Completed = true;
            f5a2W1 = CaptureF5Point(process, "W1", "VERUP_BASE.ERB:20 before CHECK_EXIST_SKILL");
            CaptureF5A2V5Completion(process);

            PrepareF5A2V6(process);
            if (!f5a2V6Ready)
            {
                f5a2StoppedBefore = "VERUP_FUNCTION.ERB:4352 CHECK_EXIST_SKILL first GET_SKILL_PROP effect";
                FinishF5A2(process);
                return;
            }

            RunCandidateF5A2V6(process);
            f5a2V6Completed = true;
            f5a2W2 = CaptureF5Point(process, "W2", "VERUP_BASE.ERB:20 body end");
            ReturnF5A2ToEvent(process);
            f5a2BaseCompleted = true;
            EventCursor = "EVENTLOAD:definition=1:statement=SYSTEM1008";
            f5a2StoppedBefore = "SYSTEM.ERB:1008 before IF FLAG:ショップコマンド";
            StoppedBefore = f5a2StoppedBefore;
            f5a2W3 = CaptureF5Point(process, "W3", f5a2StoppedBefore);
            BuildF5A2NextInventory(process);
            FinishF5A2(process);
        }

        private void ReturnF5A2ToEvent(Process process)
        {
            if (compactFrames.Count != 1 || !compactFrames[0].Handle.Name.Equals(F5Base, StringComparison.Ordinal) || compactFrames[0].ReturnToken != 1008)
                throw new InvalidOperationException("R0-F5A2 saved EVENTLOAD continuation mismatch");
            var frame = compactFrames[0];
            if (frame.PrivateScope is not null) frame.PrivateScope.Active = false;
            compactFrames.Clear();
            F3FrameEvents.Add($"RETURN:{F5Base}:kind=fallthrough:result={process.vEvaluator.RESULT}:resume=1008");
        }

        internal bool ObserveF5A2LegacyInstruction(Process process, InstructionLine line, string file, int lineNo)
        {
            var current = process.state.functionCount == 0 ? "" : process.state.CurrentCalled.FunctionName;
            if (file.Equals("VERUP_FUNCTION.ERB", StringComparison.OrdinalIgnoreCase) && current.Equals(F5CheckCsv, RuntimeConfig.StringComparison))
            {
                if (!f5a2Prepared)
                {
                    PrepareF5A2(process);
                    f5a2W0 = CaptureF5Point(process, "W0", $"VERUP_FUNCTION.ERB:{lineNo} first CHECK_EXIST_CSV instruction");
                    if (!f5a2V5Ready)
                    {
                        f5a2StoppedBefore = "VERUP_FUNCTION.ERB:253 CHECK_EXIST_CSV first actual effect";
                        throw new R0F1PlannedCheckpointException();
                    }
                }
                if (lineNo == 278) f5a2CharactersObserved++;
                if (lineNo == 258) f5a2PersonaCsvObserved++;
                var family = lineNo switch { 265 => 0, 267 => 1, 269 => 2, 271 => 3, 272 => 4, _ => -1 };
                if (family >= 0)
                {
                    var id = G4Private(process.state.CurrentCalled, "LCOUNT", process);
                    F5A2ObserveCleanupWrite(process, family, id);
                }
                return false;
            }

            if (file.Equals("VERUP_BASE.ERB", StringComparison.OrdinalIgnoreCase) && current.Equals(F5Base, RuntimeConfig.StringComparison) && lineNo == 21)
            {
                f5a2V5Completed = true;
                f5a2W1 = CaptureF5Point(process, "W1", "VERUP_BASE.ERB:20 before CHECK_EXIST_SKILL");
                CaptureF5A2V5Completion(process);
                PrepareF5A2V6(process);
                if (!f5a2V6Ready)
                {
                    f5a2StoppedBefore = "VERUP_FUNCTION.ERB:4352 CHECK_EXIST_SKILL first GET_SKILL_PROP effect";
                    throw new R0F1PlannedCheckpointException();
                }
                return false;
            }

            if (file.Equals("VERUP_FUNCTION.ERB", StringComparison.OrdinalIgnoreCase) && current.Equals(F5A2CheckSkill, RuntimeConfig.StringComparison))
            {
                if (lineNo is 4352 or 4360)
                {
                    var character = G4Private(process.state.CurrentCalled, "CHARA", process);
                    var skill = G4Private(process.state.CurrentCalled, "SKILL", process);
                    var kind = G4Private(process.state.CurrentCalled, "LCOUNT", process);
                    var slot = kind == 0 ? f5a2CharacterSkillSlots[skill - 1] : f5a2CharacterLearnedSlots[skill - 1];
                    f5a2PendingSkillKey = $"C:{character}:{kind}:{skill}:{F5Read(process, f5a2Abl, [character, slot])}";
                    f5a2CharacterSkillObserved++;
                }
                else if (lineNo is 4377 or 4387)
                {
                    var persona = G4Private(process.state.CurrentCalled, "L_PERSONA", process);
                    var skill = G4Private(process.state.CurrentCalled, "LCOUNT", process);
                    var slot = lineNo == 4377 ? f5a2PersonaSkillSlots[skill - 1] : f5a2PersonaLearnedSlots[skill - 1];
                    f5a2PendingSkillKey = $"P:{persona}:{lineNo}:{skill}:{F5Read(process, f5a2Ditem, [persona, slot])}";
                    f5a2PersonaSkillObserved++;
                }
                else if (lineNo is 4353 or 4361 or 4378 or 4388)
                    ObserveF5A2SkillResult(process, lineNo < 4370);
                else if (lineNo == 4398)
                    f5a2FinalSkillCount = G4Private(process.state.CurrentCalled, "LSKILL_COUNT", process);
                return false;
            }

            if (file.Equals("SYSTEM.ERB", StringComparison.OrdinalIgnoreCase) && lineNo == 1009)
            {
                f5a2V6Completed = true;
                f5a2BaseCompleted = true;
                f5a2W2 = CaptureF5Point(process, "W2", "VERUP_BASE.ERB:20 body end");
                f5a2StoppedBefore = "SYSTEM.ERB:1008 before IF FLAG:ショップコマンド";
                StoppedBefore = f5a2StoppedBefore;
                EventCursor = "EVENTLOAD:definition=1:statement=SYSTEM1008";
                f5a2W3 = CaptureF5Point(process, "W3", f5a2StoppedBefore);
                BuildF5A2NextInventory(process);
                throw new R0F1PlannedCheckpointException();
            }
            return false;
        }

        internal bool ObserveF5A2SaveGlobal(Process process, InstructionLine line, bool before, bool result)
        {
            if (!F5A2Enabled || Candidate || line.Position is not { } p || p.LineNo != 274 ||
                !Path.GetFileName(p.Filename).Equals("VERUP_FUNCTION.ERB", StringComparison.OrdinalIgnoreCase)) return false;
            var path = Path.Combine(DataRoot, "sav", "global.sav");
            if (before)
            {
                f5a2SaveBeforeHash = FileHash(path);
                f5a2SaveBeforePayloadHash = process.vEvaluator.GetR0F1GlobalHash();
            }
            else
            {
                f5a2SaveGlobal = new { Result = result, BeforeSha256 = f5a2SaveBeforeHash, BeforePayloadSha256 = f5a2SaveBeforePayloadHash,
                    AfterSha256 = FileHash(path), AfterPayloadSha256 = process.vEvaluator.GetR0F1GlobalHash(),
                    ByteLength = File.Exists(path) ? new FileInfo(path).Length : 0, CallCount = 1, EffectOrder = f5a2AssignmentCounts.Sum() + 1 };
            }
            return true;
        }

        private void PrepareF5A2(Process process)
        {
            if (f5a2Prepared) return;
            f5V5Inventory = BuildV5Inventory(process);
            f5a2Abl = F5Token(process, "ABL", true, true);
            f5a2Fetus = F5Token(process, "FETUS_PALENT_NO", true, true);
            f5a2Nickname = F5Token(process, "NICKNAME", false, true);
            f5a2Ditem = F5Token(process, "DITEMTYPE", true, true);
            f5a2Cleanup = [
                F5Token(process, "REMOVED_FUSION_LIMIT", true, true),
                F5Token(process, "COUNT_SOLD_CHARA", true, true),
                F5Token(process, "FALLEN_RECORD", true, true),
                F5Token(process, "ANALYSIS_RATE", true, true),
                F5Token(process, "IS_CHARA_COMPANIONED", true, true)];
            f5a2CsvCount = F5Constant(process, "CSVCHARANUM");
            f5a2PersonaNoSlot = 1; // @ペルソナ("NO") source authority.
            RequireF5Definition(F5A2CheckChara, "互換処理/VERUP_FUNCTION.ERB", 275);
            RequireF5Definition(F5A2CheckSkill, "互換処理/VERUP_FUNCTION.ERB", 4336);
            RequireF5Definition(F5A2GetSkill, "RPG/スキル関係/SKILL_ACTION_FUNCTION.ERB", 86);
            RequireF5Definition(F5A2GetSkills, "RPG/スキル関係/SKILL_ACTION_FUNCTION.ERB", 98);
            RequireF5Definition(F5A2IsPu, "RPG/スキル関係/90_CSTR専用スキル/専用スキル関数.ERB", 33);
            RequireF5Definition(F5A2PuNum, "RPG/スキル関係/90_CSTR専用スキル/専用スキル関数.ERB", 25);
            f5a2CheckCharaHandle = F5Handle(71_000, F5A2CheckChara);
            f5a2CheckSkillHandle = F5Handle(71_001, F5A2CheckSkill);
            f5a2GetSkillHandle = F5Handle(71_002, F5A2GetSkill);
            f5a2GetSkillsHandle = F5Handle(71_003, F5A2GetSkills);
            f5Materialized.Add(F5CheckCsv);
            BuildF5A2CharacterPreflight(process);
            BuildF5A2PersonaCsvPreflight(process);
            CaptureF5A2DomainBefore(process);
            f5a2V5Ready = f5a2CharacterRows.All(row => row.BranchDigest.StartsWith("READY:", StringComparison.Ordinal)) &&
                f5a2PersonaDeleteCalls == 0;
            f5a2V5Preflight = new { Ready = f5a2V5Ready, FreshIdentity = f5V5Inventory, Closure = new[] { F5CheckCsv, F5A2CheckChara, "EXISTCSV", "missing-CSV five-array writes", "SAVEGLOBAL" },
                InteractiveInputReachable = false, Input = 0, InputInt = 0, EffectsBeforeReady = 0 };
            f5a2Prepared = true;
        }

        private void BuildF5A2CharacterPreflight(Process process)
        {
            var fatherSlot = F5Keyword(process, VariableCode.CFLAG, "妊娠確定後の父親の判定");
            var linkSlot = F5Keyword(process, VariableCode.CFLAG, "初期リンク悪魔");
            var personaSlots = new[] { "初期ペルソナ", "初期ペルソナ2", "初期ペルソナ3" }.Select(x => F5Keyword(process, VariableCode.ABL, x)).ToArray();
            var mutationSlot = F5Keyword(process, VariableCode.ABL, "変異");
            var awakeningSlot = F5Keyword(process, VariableCode.ABL, "覚醒スキル");
            for (long character = 0; character < process.vEvaluator.CHARANUM; character++)
            {
                var no = F5Read(process, f5No, [character]);
                var father = F5Read(process, f5Cflag, [character, fatherSlot]);
                var link = F5Read(process, f5Cflag, [character, linkSlot]);
                var p1 = F5Read(process, f5a2Abl, [character, personaSlots[0]]);
                var p2 = F5Read(process, f5a2Abl, [character, personaSlots[1]]);
                var p3 = F5Read(process, f5a2Abl, [character, personaSlots[2]]);
                var mutation = F5Read(process, f5a2Abl, [character, mutationSlot]);
                var awakening = F5Read(process, f5a2Abl, [character, awakeningSlot]);
                var fetus0 = F5Read(process, f5a2Fetus, [character, 0]);
                var fetus1 = F5Read(process, f5a2Fetus, [character, 1]);
                var nickname = F5String(process, f5a2Nickname, [character]);
                var csvNickname = process.vEvaluator.GetCharacterStrfromCSVData(no, CharacterStrData.NICKNAME, false, 0);
                var mismatch = f5LastLoadValue / 1_000_000 >= 309 && nickname.Length != 0 && nickname != csvNickname;
                var unsafeBranch = father >= 0 && process.vEvaluator.ExistCsv(father, false) == 0 || process.vEvaluator.ExistCsv(link, false) == 0 ||
                    process.vEvaluator.ExistCsv(p1, false) == 0 || process.vEvaluator.ExistCsv(p2, false) == 0 || process.vEvaluator.ExistCsv(p3, false) == 0 ||
                    process.vEvaluator.ExistCsv(mutation, false) == 0 || process.vEvaluator.ExistCsv(awakening, false) == 0 ||
                    process.vEvaluator.ExistCsv(fetus0, false) == 0 || process.vEvaluator.ExistCsv(fetus1, false) == 0 ||
                    process.vEvaluator.ExistCsv(no, false) == 0 || mismatch;
                var values = $"{character}|{no}|{father}|{link}|{p1}|{p2}|{p3}|{mutation}|{awakening}|{fetus0}|{fetus1}|{nickname}|{csvNickname}|{mismatch}";
                var state = Hash(values);
                f5a2CharacterRows.Add(new(character, no, father, link, p1, p2, p3, mutation, awakening, fetus0, fetus1,
                    nickname, csvNickname, mismatch, (unsafeBranch ? "BLOCKED:" : "READY:") + Hash(values), state));
            }
            f5a2V5Characters = new { Count = f5a2CharacterRows.Count, Ready = f5a2CharacterRows.All(x => x.BranchDigest.StartsWith("READY:", StringComparison.Ordinal)),
                InputCalls = 0, InputIntCalls = 0, OrderedDigest = Hash(string.Join('|', f5a2CharacterRows.Select(x => x.BranchDigest))), Rows = f5a2CharacterRows };
        }

        private void BuildF5A2PersonaCsvPreflight(Process process)
        {
            var rows = new List<string>();
            for (long slot = 0; slot < 100; slot++)
            {
                var no = F5Read(process, f5a2Ditem, [slot, f5a2PersonaNoSlot]);
                var exists = process.vEvaluator.ExistCsv(no, false) != 0;
                if (!exists) f5a2PersonaDeleteCalls++;
                rows.Add($"{slot}:{no}:{exists}");
            }
            f5a2PersonaCsv = new { SlotsChecked = 100, DeleteCalls = f5a2PersonaDeleteCalls, OrderedDigest = Hash(string.Join('|', rows)),
                Representatives = new[] { rows[0], rows[49], rows[99] }, DelPersona2Materialized = f5a2PersonaDeleteCalls != 0 };
        }

        private void CaptureF5A2DomainBefore(Process process)
        {
            foreach (var token in f5a2Cleanup)
            {
                var values = Enumerable.Range(0, checked((int)f5a2CsvCount)).Select(i => F5Read(process, token, [i])).ToArray();
                f5a2DomainBefore.Add(new(token.Name, token, values, F5A2HashLongs(values)));
            }
        }

        private void F5A2WriteCleanup(Process process, int family, long id)
        {
            var token = f5a2Cleanup[family]; var before = F5Read(process, token, [id]);
            token.SetValue(0, [id]);
            f5a2AssignmentCounts[family]++;
            if (before != 0) f5a2ChangedCounts[family]++;
            f5a2Operations.Append(id).Append(':').Append(family).Append(';');
            compactFrames[^1].Committed = true;
        }

        private void F5A2ObserveCleanupWrite(Process process, int family, long id)
        {
            var before = F5Read(process, f5a2Cleanup[family], [id]);
            f5a2AssignmentCounts[family]++;
            if (before != 0) f5a2ChangedCounts[family]++;
            f5a2Operations.Append(id).Append(':').Append(family).Append(';');
        }

        private void SaveF5A2Global(Process process)
        {
            var path = Path.Combine(DataRoot, "sav", "global.sav");
            var before = FileHash(path); var payloadBefore = process.vEvaluator.GetR0F1GlobalHash();
            var result = process.vEvaluator.SaveGlobal();
            f5a2SaveGlobal = new { Result = result, BeforeSha256 = before, BeforePayloadSha256 = payloadBefore,
                AfterSha256 = FileHash(path), AfterPayloadSha256 = process.vEvaluator.GetR0F1GlobalHash(),
                ByteLength = File.Exists(path) ? new FileInfo(path).Length : 0, CallCount = 1, EffectOrder = f5a2AssignmentCounts.Sum() + 1 };
        }

        private void CaptureF5A2V5Completion(Process process)
        {
            var missing = Enumerable.Range(0, checked((int)f5a2CsvCount)).Where(i => process.vEvaluator.ExistCsv(i, false) == 0).ToArray();
            var rows = f5a2DomainBefore.Select((before, i) =>
            {
                var after = Enumerable.Range(0, checked((int)f5a2CsvCount)).Select(x => F5Read(process, before.Token, [x])).ToArray();
                return new { before.Name, AssignmentCount = f5a2AssignmentCounts[i], ChangedCellCount = f5a2ChangedCounts[i],
                    BeforeSha256 = before.Hash, AfterSha256 = F5A2HashLongs(after) };
            }).ToArray();
            f5a2MissingCsv = new { CsvSpace = f5a2CsvCount, MissingIds = missing.Length,
                OrderedMissingIdSha256 = F5A2HashLongs(missing.Select(x => (long)x)), AssignmentAttempts = f5a2AssignmentCounts.Sum(),
                CombinedOrderedOperationSha256 = Hash(f5a2Operations.ToString()), Domains = rows,
                Representatives = missing.Length == 0 ? [] : new[] { missing[0], missing[missing.Length / 2], missing[^1] } };
        }

        private void PrepareF5A2V6(Process process)
        {
            if (f5a2V6Preflight is not null) return;
            if (!f5a2Prepared || f5a2Abl is null || f5a2Ditem is null)
                throw new InvalidOperationException($"R0-F5A2 V6 phase state missing: prepared={f5a2Prepared}; w0={f5a2W0 is not null}; characters={f5a2CharacterRows.Count}; abl={f5a2Abl is not null}; ditem={f5a2Ditem is not null}");
            f5Materialized.UnionWith([F5A2CheckSkill, F5A2GetSkill, F5A2GetSkills, F5A2IsPu, F5A2PuNum]);
            f5a2CharacterSkillSlots = Enumerable.Range(1, 20).Select(i => F5Keyword(process, VariableCode.ABL, $"スキル{i}")).ToArray();
            f5a2CharacterLearnedSlots = Enumerable.Range(1, 20).Select(i => F5Keyword(process, VariableCode.ABL, $"習得スキル{i}")).ToArray();
            // @ペルソナ(ARGS) defines these contiguous source-authority ranges.
            f5a2PersonaSkillSlots = Enumerable.Range(11, 8).Select(i => (long)i).ToArray();
            f5a2PersonaLearnedSlots = Enumerable.Range(40, 20).Select(i => (long)i).ToArray();
            f5a2PersonaLearnedLvSlots = Enumerable.Range(60, 20).Select(i => (long)i).ToArray();
            f5a2PuStart = G4RenameInteger("スキル:専用技1");
            f5a2PuCount = 12; // @PU_NUM source authority; its file-level fallback flags prevent isolated compilation.

            var characterRows = new List<object>(); var personaRows = new List<string>(); var blockers = new List<string>();
            var invalidCharacter = 0; var invalidPersona = 0; var expectedCalls = 0;
            for (long character = 0; character < process.vEvaluator.CHARANUM; character++)
            {
                var values = new List<long>(); var invalid = 0;
                foreach (var slot in f5a2CharacterSkillSlots.Concat(f5a2CharacterLearnedSlots))
                {
                    var skill = F5A2Read(process, f5a2Abl, [character, slot], $"character={character},slot={slot}"); values.Add(skill); expectedCalls++;
                    if (RunF5A2IsPu(skill)) { blockers.Add($"PU conversion character={character} skill={skill}"); continue; }
                    if (!TryAdmitF5A2SkillName(process, skill, out var reason)) { blockers.Add(reason); invalid++; invalidCharacter++; }
                }
                characterRows.Add(new { Character = character, SlotsScanned = values.Count, BeforeSha256 = F5A2HashLongs(values), Invalid = invalid });
            }
            for (long persona = 0; persona < 100; persona++)
            {
                var values = f5a2PersonaSkillSlots.Concat(f5a2PersonaLearnedSlots).Select(slot => F5A2Read(process, f5a2Ditem, [persona, slot], $"persona={persona},slot={slot}")).ToArray();
                var invalid = 0;
                foreach (var skill in values)
                {
                    expectedCalls++;
                    if (!TryAdmitF5A2SkillName(process, skill, out var reason)) { blockers.Add(reason); invalid++; invalidPersona++; }
                }
                personaRows.Add($"{persona}:{F5A2HashLongs(values)}:{invalid}");
            }
            f5a2V6Ready = blockers.Count == 0 && invalidCharacter == 0 && invalidPersona == 0;
            f5a2CharacterSkills = new { CharacterCount = process.vEvaluator.CHARANUM, SlotsChecked = process.vEvaluator.CHARANUM * 40,
                Invalid = invalidCharacter, Rows = characterRows };
            f5a2PersonaSkills = new { PersonaSlots = 100, SkillSlotsChecked = 2_800, Invalid = invalidPersona,
                OrderedDigest = Hash(string.Join('|', personaRows)), Representatives = new[] { personaRows[0], personaRows[49], personaRows[99] } };
            f5a2V6Preflight = new { Ready = f5a2V6Ready, CharacterSkillSlotsChecked = process.vEvaluator.CHARANUM * 40,
                PersonaSkillSlotsChecked = 2_800, GetSkillPropCallsExpected = expectedCalls, ValidResults = expectedCalls - invalidCharacter - invalidPersona,
                InvalidNormalSkills = invalidCharacter, InvalidPuSkills = blockers.Count(x => x.StartsWith("PU ", StringComparison.Ordinal)),
                InvalidPersonaSkills = invalidPersona, InvalidPersonaLearnedSkills = 0, DisplayBranchExpected = invalidCharacter + invalidPersona != 0,
                ClearLearnedSkillActualCalls = 0, Blockers = blockers.Distinct().ToArray(), EffectsBeforeReady = 0 };
        }

        private void RunCandidateF5A2V6(Process process)
        {
            Enter(new(f5a2CheckSkillHandle, 21, "VERUP_BASE.ERB:20"), 4337, true);
            for (long character = 0; character < process.vEvaluator.CHARANUM; character++)
            for (var kind = 0; kind < 2; kind++)
            for (var skillIndex = 0; skillIndex < 20; skillIndex++)
            {
                var slot = kind == 0 ? f5a2CharacterSkillSlots[skillIndex] : f5a2CharacterLearnedSlots[skillIndex];
                RunF5A2SkillCall(process, $"C:{character}:{kind}:{skillIndex + 1}", F5A2Read(process, f5a2Abl, [character, slot], $"character={character},slot={slot}"), character, true);
            }
            for (long persona = 0; persona < 100; persona++)
            {
                for (var i = 0; i < f5a2PersonaSkillSlots.Length; i++)
                    RunF5A2SkillCall(process, $"P:{persona}:4377:{i + 1}", F5A2Read(process, f5a2Ditem, [persona, f5a2PersonaSkillSlots[i]], $"persona={persona},slot={f5a2PersonaSkillSlots[i]}"), -1, false);
                for (var i = 0; i < f5a2PersonaLearnedSlots.Length; i++)
                    RunF5A2SkillCall(process, $"P:{persona}:4387:{i + 1}", F5A2Read(process, f5a2Ditem, [persona, f5a2PersonaLearnedSlots[i]], $"persona={persona},slot={f5a2PersonaLearnedSlots[i]}"), -1, false);
            }
            f5a2FinalSkillCount = 0;
            Return(process, F5A2CheckSkill, false);
            f5a2SkillCalls = new { CallCount = f5a2SkillCallsObserved, ArgumentAndResultSha256 = Hash(f5a2SkillDigest.ToString()),
                Valid = f5a2SkillCallsObserved - f5a2InvalidCharacterSkills - f5a2InvalidPersonaSkills,
                InvalidCharacter = f5a2InvalidCharacterSkills, InvalidPersona = f5a2InvalidPersonaSkills, FinalLskillCount = f5a2FinalSkillCount };
        }

        private readonly StringBuilder f5a2SkillDigest = new();
        private void RunF5A2SkillCall(Process process, string key, long skill, long actor, bool character)
        {
            _ = RunF5A2IsPu(skill);
            Enter(new(f5a2GetSkillHandle, 0, key), 87, true);
            Enter(new(f5a2GetSkillsHandle, 96, key), 99, true);
            Array.Fill(process.vEvaluator.RESULT_ARRAY, 0L, 0, Math.Min(10, process.vEvaluator.RESULT_ARRAY.Length));
            Array.Fill(process.vEvaluator.RESULTS_ARRAY, "", 0, Math.Min(10, process.vEvaluator.RESULTS_ARRAY.Length));
            if (!TryExecuteF5A2SkillName(process, skill, actor, out var name, out var reason)) throw new InvalidOperationException(reason);
            f5a2SkillCallsObserved++;
            if (name.Length == 0) { if (character) f5a2InvalidCharacterSkills++; else f5a2InvalidPersonaSkills++; }
            f5a2SkillDigest.Append(key).Append(':').Append(skill).Append(':').Append(name).Append(';');
            Return(process, F5A2GetSkills, false);
            Return(process, F5A2GetSkill, false);
        }

        private void ObserveF5A2SkillResult(Process process, bool character)
        {
            if (f5a2PendingSkillKey is null) throw new InvalidOperationException("R0-F5A2 missing Legacy skill call key");
            var name = process.vEvaluator.RESULTS ?? "";
            f5a2SkillCallsObserved++;
            if (name.Length == 0) { if (character) f5a2InvalidCharacterSkills++; else f5a2InvalidPersonaSkills++; }
            f5a2SkillDigest.Append(f5a2PendingSkillKey).Append(':').Append(name).Append(';');
            f5a2PendingSkillKey = null;
        }

        private bool RunF5A2IsPu(long skill)
            => skill >= f5a2PuStart && skill <= f5a2PuStart + f5a2PuCount;

        private bool TryAdmitF5A2SkillName(Process process, long skill, out string reason)
        {
            reason = "";
            if (!f5a2SkillPrograms.TryGetValue(skill, out var program))
            {
                try
                {
                    program = skill == 815
                        ? CompileF5A2Method(process, "SKILL_NAME_815", "SKILL_CHANGE", "FINDCHARA_TIMEID")
                        : CompileF5A2Method(process, "SKILL_NAME_" + skill.ToString(CultureInfo.InvariantCulture));
                }
                catch (Exception ex) { reason = $"SKILL_NAME_{skill} admission: {ex.Message}"; return false; }
                f5a2SkillPrograms.Add(skill, program);
            }
            return true;
        }

        private bool TryExecuteF5A2SkillName(Process process, long skill, long actor, out string name, out string reason)
        {
            name = "";
            if (!TryAdmitF5A2SkillName(process, skill, out reason)) return false;
            var program = f5a2SkillPrograms[skill];
            var host = new LegacyVmSemanticHost(process);
            host.BuildSemanticContextIndex([program.Program.SemanticArena, program.Program.RuntimeStatements.OperandArena, program.Program.CallArgumentArena]);
            host.BindVariableIdentities(program.Program.SemanticArena);
            host.BindVariableIdentities(program.Program.RuntimeStatements.OperandArena);
            host.BindVariableIdentities(program.Program.CallArgumentArena);
            var executor = new VmSemanticExecutor(program.Program, host);
            var machine = new VmMachine(program.Program, executor, new LegacyVmRuntimeEffects(process));
            var actuals = program.Program.RuntimeMetadata[program.Entry.Value].Parameters.Length == 0 ? [] : new[] { VmSemanticValue.From(actor) };
            var stop = machine.Run(program.Entry, actuals);
            if (stop != VmStopReason.Returned)
            {
                var frame = machine.CurrentFrame;
                reason = $"{program.Name} source VM failed: {stop}/{executor.LastStatus}/{executor.LastFault}; " +
                    $"frame={frame.FunctionId}:{frame.Pc}; arena={executor.LastEvaluationArena}; record={executor.LastRecordIndex}; " +
                    $"node={executor.LastNodeIndex}/{executor.LastNodeKind}; host={executor.LastHostOperation}/{executor.LastHostSymbol}/" +
                    $"{executor.LastHostBindingPresent}/{executor.LastHostResult}";
                return false;
            }
            name = process.vEvaluator.RESULTS ?? "";
            return true;
        }

        private F5A2MethodProgram CompileF5A2Method(Process process, params string[] names)
        {
            var definitions = new List<R0F1Definition>();
            foreach (var name in names)
            {
                if (!functions.TryGetValue(name, out var matches) || matches.Length != 1 || matches[0].IsEvent)
                    throw new InvalidOperationException(name + " missing/ambiguous");
                definitions.Add(matches[0]);
            }
            var compiler = new FunctionCompiler(environment);
            var prototypes = new List<RuntimeFunctionPrototype>();
            var catalogDefinitions = new List<FunctionDefinition>();
            for (var i = 0; i < definitions.Count; i++)
            {
                var definition = definitions[i];
                var read = FunctionSourceReader.Read(definition.File, definition.Function);
                if (read.Status != SourceReadStatus.Read) throw new InvalidOperationException(definition.Name + " source read: " + read.Reason);
                var source = read.Source!.Value;
                if (definition.Name.Equals("SKILL_NAME_815", RuntimeConfig.StringComparison))
                {
                    const string symbolic = "[[キャラ:晶_舞台少女]]";
                    var text = new UTF8Encoding(false, true).GetString(source.Bytes);
                    if (!text.Contains(symbolic, StringComparison.Ordinal)) throw new InvalidOperationException("SKILL_NAME_815 symbolic identity changed");
                    text = text.Replace(symbolic, G4RenameInteger("キャラ:晶_舞台少女").ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
                    source = source with { Bytes = Encoding.UTF8.GetBytes(text), Function = source.Function with { Flags = source.Function.Flags & ~SourceIndexFlags.Rename } };
                }
                var compiled = compiler.TryCompileRuntime(source);
                if (compiled.Status != CompileStatus.Compiled || compiled.Function is not { RuntimeMetadata: not null } body)
                    throw new InvalidOperationException(definition.Name + " compile: " + compiled.Status + "/" + compiled.Reason);
                var bytes = source.Bytes;
                var operands = body.Instructions.Select(instruction => instruction.OperandLength == 0 ? "" : Encoding.UTF8.GetString(bytes, instruction.OperandOffset, instruction.OperandLength)).ToArray();
                prototypes.Add(new(new RuntimeFunctionId(i), body.Instructions, operands.ToImmutableArray(), body.SemanticPayload, body.RuntimeMetadata));
                catalogDefinitions.Add(new(definition.Name, definition.File.FileIdentity, definition.Function.Span,
                    Kind: FunctionKind.Method, EffectiveName: definition.Name, EffectiveNameKnown: true));
            }
            var catalog = FunctionCatalog.FromDefinitions(catalogDefinitions);
            catalog.MarkCodeAvailable(Enumerable.Range(0, prototypes.Count).Select(i => new RuntimeFunctionId(i)));
            var program = ControlLinker.Link(catalog, prototypes, runtimeEnvironment: environment, runtimeStatements: true).Program;
            if (!Process.TryActivateNextRuntimeProgram(program, VmRuntimeRequirement.None, out var activation) || activation.Promoted != prototypes.Count)
                throw new InvalidOperationException(names[0] + " activation failed");
            foreach (var definition in definitions) f5Materialized.Add(definition.Name);
            var first = definitions[0];
            var handleId = first.Name.Equals(F5A2IsPu, RuntimeConfig.StringComparison) ? 71_900 : 72_000 + f5a2SkillPrograms.Count;
            return new(first.Name, program, new RuntimeFunctionId(0), Handle(handleId, first.Name), first.RelativePath, first.Line);
        }

        private void BuildF5A2NextInventory(Process process)
        {
            var shop = F5Keyword(process, VariableCode.FLAG, "ショップコマンド");
            var explore = G4RenameInteger("ショップ:探索");
            var value = F5Read(process, f5Flag, [shop]);
            f5a2NextInventory = new { FlagShopCommand = value, ExploreValue = explore, System1008BranchTaken = value == explore,
                BeginShopWillBeRequested = value == explore, EventLoadDefinition1ReturnState = "NOT_EXECUTED",
                SecondEventLoad = new { Ordinal = 2, RelativePath = eventLoad[1].RelativePath, eventLoad[1].Line },
                SecondEventLoadMaterialized = false, ExpectedBeginRequestApplication = "F5B only", HostEventCursor = EventCursor };
        }

        internal void FinishF5A2(Process process)
        {
            if (!F5A2Enabled) return;
            if (f5a2SkillCalls is null && f5a2SkillCallsObserved > 0)
                f5a2SkillCalls = new { CallCount = f5a2SkillCallsObserved, ArgumentAndResultSha256 = Hash(f5a2SkillDigest.ToString()),
                    Valid = f5a2SkillCallsObserved - f5a2InvalidCharacterSkills - f5a2InvalidPersonaSkills,
                    InvalidCharacter = f5a2InvalidCharacterSkills, InvalidPersona = f5a2InvalidPersonaSkills, FinalLskillCount = f5a2FinalSkillCount };
            var guards = Candidate ? R0E1AProof.Counters.Sum() : 0;
            var gate = f5a2BaseCompleted ? "PASS" : f5a2V5Completed || f5V4Completed ? "PARTIAL_PASS" : "BLOCKED";
            F5A2Evidence = new
            {
                Schema = "emuera-r0f5a2-complete-verup-base-v5-v6-v1",
                Mode = Candidate ? "GraphFreeCandidate" : "LegacyControl",
                GateResult = gate,
                Scope = "COMPLETE_VERUP_BASE_V5_V6",
                CheckExistCsvClosureReady = f5a2V5Ready,
                CheckExistCsvCompleted = f5a2V5Completed,
                CheckExistCsvPreflight = f5a2V5Preflight,
                CharacterCsv = f5a2V5Characters,
                PersonaCsv = f5a2PersonaCsv,
                MissingCsv = f5a2MissingCsv,
                SaveGlobal = f5a2SaveGlobal,
                CheckExistSkillClosureReady = f5a2V6Ready,
                CheckExistSkillCompleted = f5a2V6Completed,
                CheckExistSkillPreflight = f5a2V6Preflight,
                CharacterSkills = f5a2CharacterSkills,
                PersonaSkills = f5a2PersonaSkills,
                SkillCalls = f5a2SkillCalls,
                VerupBaseCompleted = f5a2BaseCompleted,
                StoppedBefore = f5a2StoppedBefore,
                Checkpoints = new { W0 = f5a2W0, W1 = f5a2W1, W2 = f5a2W2, W3 = f5a2W3 },
                TouchedDomains = new { Final = CaptureF5Point(process, "FINAL", f5a2StoppedBefore), FullVariableAndCharacterHashes = true },
                Materialization = new { StartupCompiled = 0, Names = f5Materialized.OrderBy(x => x, RuntimeConfig.StrComper).ToArray(), Count = f5Materialized.Count,
                    VersionMigrationBodies = 0, InteractiveCleanupBodies = 0, SkillNameTargets = f5a2SkillPrograms.Values.Select(x => new { x.Name, x.SourcePath, x.SourceLine }).OrderBy(x => x.Name).ToArray() },
                PendingBegin = false,
                EventLoadDefinition1Completed = false,
                SecondEventLoadEntered = false,
                NextF5BInventory = f5a2NextInventory,
                Guards = new { Total = guards, Categories = Candidate ? R0E1AProof.GuardSnapshot() : null },
                LegacyErbGraphAvoided = Candidate,
                LegacyRetryAfterCompact = 0,
                ProductionBridgeUsed = "NO",
                ExistingF5ARegression = "PENDING_OFFLINE",
                ExistingF4G4Regression = "PENDING_OFFLINE",
                NormalBuildIsolation = "PENDING_OFFLINE",
                ArchitectureEscalationRequired = "NO",
                ManualRecaptureRequired = "NO",
                NextRecommendation = f5a2BaseCompleted ? "START_F5B_EVENT_TRANSFER" : "CONTINUE_F5A2_FROM_BLOCKER",
                WholeProductSuperiority = "NOT_YET_CLAIMED"
            };
            StoppedBefore = f5a2StoppedBefore;
            if (Candidate && guards != 0) throw new InvalidOperationException("R0-F5A2 forbidden guard counter changed");
        }

        private static string F5A2HashLongs(IEnumerable<long> values) => Hash(string.Join(',', values.Select(x => x.ToString(CultureInfo.InvariantCulture))));
        private static long F5A2Read(Process process, VariableToken token, long[] indices, string context)
        {
            try { return F5Read(process, token, indices); }
            catch (Exception ex) { throw new InvalidOperationException($"R0-F5A2 read failed: {context}; token={token?.Name ?? "<null>"}; indices={string.Join(',', indices)}", ex); }
        }
    }
}
#endif
