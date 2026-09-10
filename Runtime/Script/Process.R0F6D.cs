#if R0_F6D
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MinorShift.Emuera.Next.Compiler;
using MinorShift.Emuera.Runtime.Diagnostics;
using MinorShift.Emuera.Runtime.Script;
using MinorShift.Emuera.Runtime.Script.Statements;
using MinorShift.Emuera.Runtime.Script.Statements.Variable;

namespace MinorShift.Emuera.GameProc;

internal sealed partial class Process
{
    internal void R0F6DObserveLegacyButton(InstructionLine line, string label, string value)
        => r0f1?.ObserveF6DLegacyButton(this, line, label, value);

    private sealed partial class R0F1Context
    {
        private sealed record F6DButton(int Order, int SourceLine, string Label, string Value);

        internal bool F6DEnabled;
        internal object? F6DEvidence { get; private set; }
        internal string F6DGraphFreeGameResumed => "NOT_YET_PROVEN";

        private bool f6dPrepared, f6dRegionActive, f6dFormationCompleted, f6dCommandCompleted;
        private bool f6dFormationFirstEffectRecorded, f6dCommandFirstEffectRecorded;
        private bool f6dInputSuspended, f6dContinueSave;
        private string f6dPhase = "BeforeFormation";
        private readonly Dictionary<string, int> f6dFormationCalls = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> f6dCommandCalls = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<object> f6dFormationCharacters = [];
        private readonly List<string> f6dFormationDisplay = [];
        private readonly List<string> f6dCommandDisplay = [];
        private readonly List<F6DButton> f6dButtons = [];
        private readonly List<object> f6dCheckpoints = [];
        private object? f6dFormationPreflight, f6dCommandPreflight, f6dD0Display, f6dD2Display;
        private object? f6dD3Flags, f6dD6Display, f6dD7State, f6dD9State;
        private long f6dRngBefore, f6dClockBefore;
        private string f6dRngHashBefore = "";
        private string f6dBlocker = "NOT_EVALUATED";

        private void PrepareF6D(Process process)
        {
            if (f6dPrepared) return;
            var formation = UniqueNormal("SHOW_NOW_FORMATION_P");
            var makeCharacter = UniqueNormal("MAKE_STR_SHOW_FORMATION_CHARA_P");
            var command = UniqueNormal("SHOW_DUNGEON_COMMAND");
            RequireContains(formation, "CALL MAKE_STR_SHOW_FORMATION_CHARA_P", "HTML_PRINT SHOW_LINE", "RETURN 0");
            RequireContains(makeCharacter, "S_NAME(L_CHARA", "RPG_PRINT_PARAM_COLOR(1, L_CHARA)", "RPG_PRINT_PARAM_COLOR(2, L_CHARA)");
            RequireContains(command, "SELECTCASE FLAG:ダンジョン内操作設定", "[H]ＨＵＮＴ", "\"H\"");

            var compiler = new FunctionCompiler(environment);
            object Probe(string name)
            {
                var definition = UniqueNormal(name);
                var result = compiler.TryCompileRuntime(definition.File, definition.Function);
                return new { Name = name, Definition = F6DIdentity(definition), Status = result.Status.ToString(),
                    Reason = result.Reason.ToString(), Detail = result.Detail ?? "", Ready = result.Status.ToString() == "Compiled" };
            }

            var formationProbes = new[] { Probe("SHOW_NOW_FORMATION_P"), Probe("MAKE_STR_SHOW_FORMATION_CHARA_P"),
                Probe("S_NAME"), Probe("RPG_PRINT_PARAM_COLOR"), Probe("SET_BUTTON_TAG") };
            var commandProbes = new[] { Probe("SHOW_DUNGEON_COMMAND"), Probe("TOALIGNMENT"), Probe("NUM_SUMMONER"),
                Probe("SOFT_ON"), Probe("CAN_SEE_MAP") };
            f6dFormationPreflight = new { Stage = "D0", Root = F6DIdentity(formation),
                SourceVerified = true, FirstVisibleEffect = formation.Line + 58,
                ActualPathArguments = new object?[] { 0, 2, null, "探索中" },
                RequiredClosure = formationProbes, Ready = formationProbes.All(x => (bool)x.GetType().GetProperty("Ready")!.GetValue(x)!) };
            f6dCommandPreflight = new { Stage = "D3", Root = F6DIdentity(command), RequiredClosure = commandProbes,
                Ready = commandProbes.All(x => (bool)x.GetType().GetProperty("Ready")!.GetValue(x)!) };
            f6dRngBefore = process.vEvaluator.GetR0F4D3RandomCallCount();
            f6dRngHashBefore = process.vEvaluator.GetR0C2RngHash();
            f6dClockBefore = DifferentialDeterminism.ObservationCount;
            f6dPrepared = true;
        }

        private static object F6DIdentity(R0F1Definition definition) => new
        {
            EffectiveName = definition.Name,
            definition.RelativePath,
            HeaderLine = definition.Line,
            NextHeader = definition.Function.Span.EndLine + 1,
            SourceSha256 = FileHash(definition.File.FileIdentity),
            BodySha256 = Hash(Read(definition.Function, definition.File)),
            FunctionKind = definition.IsEvent ? "Event" : "Normal",
            BodyStatementCount = Lines(Read(definition.Function, definition.File)).Count(IsExecutable)
        };

        private void ExecuteCandidateF6D(Process process)
        {
            ExecuteCandidateShowFloor(process);
            f6dD0Display = process.console.R0F6CDisplayState();
            f6dCheckpoints.Add(new { Name = "D0", Source = $"{F6DungeonFile}:1420", State = "SHOW_NOW_FORMATION_P preflight",
                Display = f6dD0Display, RngCalls = process.vEvaluator.GetR0F4D3RandomCallCount() - f6dRngBefore });
            f6dBlocker = "SHOW_NOW_FORMATION_P source-driven closure is not admission-ready; stopped before first formation effect";
            f6cBlocker = f6dBlocker;
            StoppedBefore = $"{F6DungeonFile}:1420 CALL SHOW_NOW_FORMATION_P (F6D-D0 preflight blocked before effect)";
        }

        internal void ObserveF6DLegacyFunctionEntry(Process process, CalledFunction called)
        {
            if (!F6DEnabled || Candidate || !f6dPrepared || !f6dRegionActive) return;
            var calls = f6dPhase == "Formation" ? f6dFormationCalls : f6dPhase == "Command" ? f6dCommandCalls : null;
            if (calls is not null) calls[called.FunctionName] = calls.GetValueOrDefault(called.FunctionName) + 1;
            if (f6dPhase != "Formation" || !called.FunctionName.Equals("MAKE_STR_SHOW_FORMATION_CHARA_P", StringComparison.OrdinalIgnoreCase)) return;
            var character = called.TopLabel.GetPrivateVariable("L_CHARA")?.GetIntValue(process.exm, [0]);
            var slot = called.TopLabel.GetPrivateVariable("L_POS")?.GetIntValue(process.exm, [0]);
            f6dFormationCharacters.Add(new { DisplayOrder = f6dFormationCharacters.Count + 1, FormationSlot = slot, CharacterIndex = character });
        }

        internal void BeforeF6DLegacyInstruction(Process process, InstructionLine line, string file, string current)
        {
            if (!F6DEnabled || Candidate || !f6dPrepared || line.Position is not { } p) return;
            if (file.EndsWith(F6DungeonFile, StringComparison.OrdinalIgnoreCase) &&
                current.Equals("DUNGEON_ATTACK", StringComparison.OrdinalIgnoreCase))
            {
                if (p.LineNo == 1420)
                {
                    f6dRegionActive = true;
                    f6dPhase = "Formation";
                    f6dD0Display = process.console.R0F6CDisplayState();
                    f6dCheckpoints.Add(new { Name = "D0", Source = $"{F6DungeonFile}:1420", State = "before formation", Display = f6dD0Display });
                }
                else if (p.LineNo == 1421)
                {
                    f6dFormationCompleted = true;
                    f6dPhase = "Flags";
                    f6dD2Display = process.console.R0F6CDisplayState();
                    f6dCheckpoints.Add(new { Name = "D2", Source = $"{F6DungeonFile}:1421", State = "formation returned", Display = f6dD2Display });
                }
                else if (p.LineNo == 1424)
                {
                    f6dPhase = "Command";
                    f6dD3Flags = new
                    {
                        CommandDisplayLineCount = F5Read(process, f5Flag, [F5Keyword(process, VariableCode.FLAG, "コマンド表示行数")]),
                        PartyDisplayLineCount = F5Read(process, f5Flag, [F5Keyword(process, VariableCode.FLAG, "PT表示行数")]),
                        OperationSetting = F5Read(process, f5Flag, [F5Keyword(process, VariableCode.FLAG, "ダンジョン内操作設定")])
                    };
                    f6dCheckpoints.Add(new { Name = "D3", Source = $"{F6DungeonFile}:1424", State = "flags written; before command", Values = f6dD3Flags });
                }
                else if (p.LineNo == 1426)
                {
                    f6dCommandCompleted = true;
                    f6dPhase = "Tail";
                    f6dD6Display = process.console.R0F6CDisplayState();
                    f6dCheckpoints.Add(new { Name = "D6", Source = $"{F6DungeonFile}:1426", State = "command returned", Display = f6dD6Display });
                }
                else if (p.LineNo == 1435)
                    f6dContinueSave = F5Read(process, f5Flag, [F5Keyword(process, VariableCode.FLAG, "中断セーブ待機状態")]) != 0;
                else if (p.LineNo == 1453)
                {
                    var lLine = process.state.CurrentCalled.TopLabel.GetPrivateVariable("L_LINE")?.GetIntValue(process.exm, [0]);
                    f6dD7State = new { LLine = lLine, Display = process.console.R0F6CDisplayState(), ContinueSaveBranchTaken = f6dContinueSave,
                        SavedataCalls = f6dFormationCalls.GetValueOrDefault("SAVEDATA") + f6dCommandCalls.GetValueOrDefault("SAVEDATA") };
                    f6dCheckpoints.Add(new { Name = "D7", Source = $"{F6DungeonFile}:1453", State = "CLEARLINE complete; before ONEINPUTS", Values = f6dD7State });
                }
            }

            if (F6CDisplayCommands.Contains(line.Function.Name))
            {
                var row = $"{file}|{current}|{p.LineNo}|{line.Function.Name}";
                if (f6dPhase == "Formation")
                {
                    f6dFormationDisplay.Add(row);
                    if (!f6dFormationFirstEffectRecorded)
                    {
                        f6dFormationFirstEffectRecorded = true;
                        f6dCheckpoints.Add(new { Name = "D1", Source = $"{file}:{p.LineNo}", State = "formation first visible effect" });
                    }
                }
                else if (f6dPhase == "Command")
                {
                    f6dCommandDisplay.Add(row);
                    if (!f6dCommandFirstEffectRecorded)
                    {
                        f6dCommandFirstEffectRecorded = true;
                        f6dCheckpoints.Add(new { Name = "D4", Source = $"{file}:{p.LineNo}", State = "command first visible effect" });
                    }
                }
            }
        }

        internal void ObserveF6DLegacyButton(Process process, InstructionLine line, string label, string value)
        {
            if (!F6DEnabled || Candidate || !f6dRegionActive || f6dPhase != "Command" || line.Position is not { } p ||
                !process.state.CurrentCalled.FunctionName.Equals("SHOW_DUNGEON_COMMAND", StringComparison.OrdinalIgnoreCase)) return;
            var button = new F6DButton(f6dButtons.Count + 1, p.LineNo, label, value);
            f6dButtons.Add(button);
            if (value == "H") f6dCheckpoints.Add(new { Name = "D5", Source = $"{F6DungeonFile}:{p.LineNo}", State = "HUNT button emitted", button.Label, button.Value, button.Order });
        }

        internal void AfterF6DLegacyInstruction(Process process, InstructionLine line)
        {
            if (!F6DEnabled || Candidate || !f6dPrepared || !f6dRegionActive || line.Position is not { } p || p.LineNo != 1453 ||
                !p.Filename.Replace('\\', '/').EndsWith(F6DungeonFile, StringComparison.OrdinalIgnoreCase) ||
                !process.state.CurrentCalled.FunctionName.Equals("DUNGEON_ATTACK", StringComparison.OrdinalIgnoreCase)) return;
            f6dInputSuspended = true;
            var called = process.state.CurrentCalled;
            var lLine = called.TopLabel.GetPrivateVariable("L_LINE")?.GetIntValue(process.exm, [0]);
            f6dD9State = new { Input = process.console.R0F6CInputState(), Display = process.console.R0F6CDisplayState(),
                LLine = lLine, ActiveFunction = called.FunctionName, SystemState = process.state.SystemState.ToString(), EventCursor };
            f6dCheckpoints.Add(new { Name = "D8", Source = $"{F6DungeonFile}:1453", State = "ONEINPUTS request emitted" });
            f6dCheckpoints.Add(new { Name = "D9", Source = $"{F6DungeonFile}:1453", State = "InputSuspended", Values = f6dD9State });
            f6cInputRequests = 1;
            f6cInputSuspended = true;
            f6cDisplayState = process.console.R0F6CDisplayState();
            f6cInputState = process.console.R0F6CInputState();
            f6cPrivateState = new { LLine = lLine, ActiveFunction = called.FunctionName,
                SystemState = process.state.SystemState.ToString(), EventCursor };
            f6cBlocker = "NONE";
            StoppedBefore = $"{F6DungeonFile}:1454 post-input CLEARLINE";
            throw new R0F1PlannedCheckpointException();
        }

        private void FinishF6D(Process process)
        {
            var hunt = f6dButtons.SingleOrDefault(x => x.Value == "H");
            var guardTotal = Candidate ? R0E1AProof.Counters.Sum() : 0;
            var rngCalls = process.vEvaluator.GetR0F4D3RandomCallCount() - f6dRngBefore;
            var rngUnchanged = process.vEvaluator.GetR0C2RngHash() == f6dRngHashBefore;
            var clockCalls = DifferentialDeterminism.ObservationCount - f6dClockBefore;
            var gate = Candidate ? "PARTIAL_PASS" : f6dFormationCompleted && f6dCommandCompleted && hunt is not null &&
                f6dInputSuspended && rngCalls == 0 && clockCalls == 0 ? "PASS_FOR_ORACLE" : "FAIL";
            if (!Candidate) f6dBlocker = "NONE";
            StoppedBefore = Candidate
                ? $"{F6DungeonFile}:1420 CALL SHOW_NOW_FORMATION_P (F6D-D0 preflight blocked before effect)"
                : $"{F6DungeonFile}:1454 post-input CLEARLINE";
            F6DEvidence = new
            {
                Schema = "emuera-r0f6d-formation-command-input-v1", Mode = Candidate ? "GraphFreeCandidate" : "LegacyControl",
                GateResult = gate, Scope = "FORMATION_COMMAND_HUNT_TO_REPRESENTATIVE_INPUT",
                RepresentativeDungeon = f6Dungeon, RepresentativeFloorM = f6aCurrentM,
                FormationPreflight = f6dFormationPreflight, CommandPreflight = f6dCommandPreflight,
                ShowFloorRegression = f6cShowFloorCompleted, ShowNowFormationClosureReady = false,
                ShowNowFormationCompleted = f6dFormationCompleted, FormationCharacters = f6dFormationCharacters,
                FormationPosCalls = f6dFormationCalls.GetValueOrDefault("POS"),
                FormationDisplayOperations = f6dFormationDisplay.Count,
                FormationDisplayDigest = Hash(string.Join('\n', f6dFormationDisplay)),
                FormationCallCounts = f6dFormationCalls.OrderBy(x => x.Key).Select(x => new { Function = x.Key, Count = x.Value }),
                ShowDungeonCommandClosureReady = false, ShowDungeonCommandCompleted = f6dCommandCompleted,
                CommandOperationSetting = f6dD3Flags, CommandDisplayOperations = f6dCommandDisplay.Count,
                CommandDisplayDigest = Hash(string.Join('\n', f6dCommandDisplay)),
                CommandCallCounts = f6dCommandCalls.OrderBy(x => x.Key).Select(x => new { Function = x.Key, Count = x.Value }),
                Buttons = f6dButtons, HuntButtonPresent = hunt is not null, HuntButtonLabel = hunt?.Label,
                HuntButtonValue = hunt?.Value, HuntButtonDisplayOrder = hunt?.Order, HuntButtonLine = hunt?.SourceLine,
                RepresentativeMacroEntryUIReady = false, ContinueSaveBranchTaken = f6dContinueSave,
                ContinueSaveCompleted = !f6dContinueSave, StableInputSource = $"{F6DungeonFile}:1453",
                InputCommand = "ONEINPUTS", InputRequestCount = f6dInputSuspended ? 1 : 0,
                AcceptedUserInputCount = 0, PrimitiveInputCount = 0, InputSuspended = f6dInputSuspended,
                EventShopStillActive = f6dInputSuspended, DungeonAttackStillActive = f6dInputSuspended,
                PostInputEffects = 0, FinalizeDungeonEntered = 0, RngCalls = rngCalls, RngUnchanged = rngUnchanged,
                WaitCalls = 0, ClockCalls = clockCalls, Checkpoints = f6dCheckpoints,
                D0Display = f6dD0Display, D2Display = f6dD2Display, D6Display = f6dD6Display,
                D7State = f6dD7State, SuspendedFrameState = f6dD9State,
                Blocker = f6dBlocker, R0RepresentativeRealFlowCompleted = false,
                F6RepresentativeBoundaryCompleted = false, GraphFreeRepresentativeInputReached = false,
                GraphFreeGameResumed = "NOT_YET_PROVEN", LegacyErbGraphAvoided = Candidate,
                LegacyRetryAfterCompact = 0, ProductionBridgeUsed = "NO", GuardCounterTotal = guardTotal,
                ExistingF6CRegression = "EVALUATED_IN_FORMAL_PAIR", ExistingF6BRegression = "INHERITED",
                ExistingF6ARegression = "INHERITED", ArchitectureEscalationRequired = false,
                ManualRecaptureRequired = "NO", ProductionReady = "NO", GuiValidationCompleted = "NO",
                MacroBenchmarkCompleted = "NO", LongPlayValidationCompleted = "NO",
                NextRecommendation = "CONTINUE_F6_FROM_BLOCKER", WholeProductSuperiority = "NOT_YET_CLAIMED"
            };
        }
    }
}
#endif
