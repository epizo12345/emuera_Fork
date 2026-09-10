#if R0_F4G1
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MinorShift.Emuera.GameData;
using MinorShift.Emuera.GameData.Function;
using MinorShift.Emuera.GameData.Variable;
using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Runtime.Diagnostics;
using MinorShift.Emuera.Runtime.Script;
using MinorShift.Emuera.Runtime.Script.Statements;
using MinorShift.Emuera.Runtime.Script.Statements.Variable;
using MinorShift.Emuera.UI.Game.Image;
using RuntimeConfig = MinorShift.Emuera.Runtime.Config.Config;

namespace MinorShift.Emuera.GameProc;

internal sealed partial class Process
{
    private sealed partial class R0F1Context
    {
        private const string G1Parent = "モブ画像_全リセット";
        private const string G1Reset = "モブ画像_リセット";
        private const string G1SpriteReset = "モブ画像_スプライト破棄";
        private const string G1File = "画像表示.ERB";
        private sealed record G1Constants(long MobCount, long GraphicsStart, object[] Declarations);
        private sealed record G1Operation(int Sequence, string Command, string Argument, long Result,
            AppContents.R0F4G1ImageState Before, AppContents.R0F4G1ImageState After);
        private sealed record G1CallRow(int Sequence, string Caller, string Callee, long[] IntegerArguments, string[] StringArguments);
        private sealed record G1PointRow(string Name, string ProgramCounter, long Lcount, string Frames,
            string EventCursor, string SystemState, bool PendingBegin, string RngSha256, long RngCalls,
            long ClockCalls, long Result0, string ResultsSha256, string ResourceSha256);

        internal object? F4G1Evidence { get; private set; }
        private bool g1LegacyActive, g1ParentReturnPending, g1ParentReturned;
        private VariableToken? g1LegacyLcount, g1Cflag, g1ScreenList;
        private long g1Lcount, g1LegacyFinalLcount, g1CflagSlot;
        private G1Constants? g1Constants;
        private readonly List<G1Operation> g1Operations = [];
        private readonly List<G1CallRow> g1Calls = [];
        private readonly HashSet<string> g1Materialized = new(RuntimeConfig.StrComper);
        private readonly HashSet<long> g1GraphicsIds = [];
        private readonly HashSet<string> g1SpriteNames = new(RuntimeConfig.StrComper);
        private AppContents.R0F4G1ImageState? g1PendingState;
        private string g1PendingCommand = "", g1PendingArgument = "";
        private long g1PendingResult;
        private object? g1G0, g1G1, g1G2, g1G3, g1G4, g1Preflight;
        private string g1RngBefore = "";
        private long g1RngCallsBefore, g1ClockBefore;

        internal void ExecuteF4G1(Process process)
        {
            if (StoppedBefore != "SYSTEM.ERB:1025 CALL モブ画像_全リセット"
                || compactFrames.Count != 1 || compactFrames[0].Handle.Name != "SET_GAMEPLAY_START"
                || compactFrames[0].Pc != 1025)
                throw new InvalidOperationException("R0-F4G1 requires the fresh F4F SYSTEM1025 boundary");

            PrepareF4G1(process);
            g1RngBefore = process.vEvaluator.GetR0C2RngHash();
            g1RngCallsBefore = process.vEvaluator.GetR0F4D3RandomCallCount();
            g1ClockBefore = DifferentialDeterminism.ObservationCount;
            g1G0 = CaptureG1Point(process, "G0", "SYSTEM.ERB:1025:before");
            if (Candidate) RunCandidateF4G1(process); else RunLegacyF4G1(process);
            var resource = G1ResourceSnapshot();
            var guardTotal = Candidate ? R0E1AProof.Counters.Sum() : 0;
            var pass = g1G0 is not null && g1G1 is not null && g1G2 is not null && g1G3 is not null && g1G4 is not null
                && g1ParentReturned && compactFrames.Count == 1 && compactFrames[0].Handle.Name == "SET_GAMEPLAY_START"
                && compactFrames[0].Pc == 1026 && StoppedBefore == "SYSTEM.ERB:1026 CALL SET_EXTRA_TITLE_VAR"
                && g1Operations.Count > 0 && g1Operations.All(x => G1OperationValid(x))
                && process.vEvaluator.GetR0C2RngHash() == g1RngBefore
                && process.vEvaluator.GetR0F4D3RandomCallCount() == g1RngCallsBefore
                && DifferentialDeterminism.ObservationCount == g1ClockBefore
                && guardTotal == 0 && B1Proof.BridgeAttempts == 0;
            F4G1Evidence = new
            {
                Schema = "emuera-r0f4g1-mob-image-reset-v1", Mode = Candidate ? "GraphFreeCandidate" : "LegacyControl",
                Scope = "MOB_IMAGE_RESET_REAL_FLOW", GateResult = pass ? "PASS" : "FAIL",
                ResumedFrom = "SYSTEM.ERB:1025 CALL モブ画像_全リセット", StoppedBefore,
                Preflight = g1Preflight, Checkpoints = new { G0 = g1G0, G1 = g1G1, G2 = g1G2, G3 = g1G3, G4 = g1G4 },
                Calls = g1Calls.ToArray(), HostOperations = g1Operations.ToArray(), GraphicsResourceState = resource,
                PersistentState = new { Lcount = ReadG1Lcount(process), Result = (long[])process.vEvaluator.RESULT_ARRAY.Clone(),
                    Results = (string[])process.vEvaluator.RESULTS_ARRAY.Clone(), ResultSha256 = HashLongs(process.vEvaluator.RESULT_ARRAY),
                    ResultsSha256 = HashStrings(process.vEvaluator.RESULTS_ARRAY) },
                Materialization = new { StartupCompiled = 0, Names = g1Materialized.OrderBy(x => x).ToArray(), Count = g1Materialized.Count,
                    ClosureOnly = g1Materialized.All(x => x is G1Parent or G1Reset or G1SpriteReset) },
                Guards = new { Total = guardTotal, Categories = Candidate ? R0E1AProof.GuardSnapshot() : null },
                ExternalEffects = new { FileIO = 0, Display = 0, Input = 0, RNG = 0, Clock = 0, Graphics = g1Operations.Count },
                MobImageResetCompleted = true, SetGameplayStartResumed = true,
                SetExtraTitleVar = new { Lookup = 0, Materialized = 0, Executed = 0, BoundaryEnforced = true },
                EventLoadCompleted = false, GraphFreeGameResumed = "NOT_YET_PROVEN", LegacyErbGraphAvoided = Candidate,
                LegacyRetryAfterCompact = 0, ProductionBridgeUsed = "NO", ManualRecaptureRequired = "NO",
                WholeProductSuperiority = "NOT_YET_CLAIMED"
            };
            if (!pass) throw new InvalidOperationException("R0-F4G1 gate failed");
        }

        private void PrepareF4G1(Process process)
        {
            var stateBefore = process.GetBenchmarkStateHash();
            var parent = G1Definition(G1Parent, 112);
            var reset = G1Definition(G1Reset, 86);
            var sprite = G1Definition(G1SpriteReset, 121);
            RequireBody(parent, ["#DIM DYNAMIC LCOUNT", "FOR LCOUNT, 0 , MOB_CHARA_GIDNUM", "CALL モブ画像_リセット( , LCOUNT)", "NEXT"]);
            RequireBody(reset, ["#DIM キャラ番号", "#DIM モブグラフィックID", "#DIM _CNT", "#DIM 合計画面数", "#DIMS 設定画面名",
                "SIF CFLAG:キャラ番号:キャラ固有の番号 < 0", "RETURN", "SIF モブグラフィックID == -1", "モブグラフィックID = CFLAG:キャラ番号:キャラ固有の番号",
                "SIF !GCREATED(モブグラフィックID + MOB_CHARA_GIDNUM_START)", "RETURN", "SPLIT 画像エディット_設定画面リスト, \",\", LOCALS",
                "合計画面数 = (RESULT - 1)", "FOR _CNT, 0, 合計画面数", "設定画面名 '= LOCALS:(_CNT + 1)",
                "CALL モブ画像_スプライト破棄(モブグラフィックID, 設定画面名, 4)", "CALL モブ画像_スプライト破棄(モブグラフィックID, 設定画面名, 6)",
                "SPRITEDISPOSE @\"モブ画像_{モブグラフィックID}_%設定画面名%\"", "NEXT", "GDISPOSE モブグラフィックID + MOB_CHARA_GIDNUM_START"]);
            RequireBody(sprite, ["#DIM モブグラフィックID", "#DIMS 設定画面名", "#DIM 設定画面番号", "#DIM 画像分割数", "#DIM _CNT",
                "FOR _CNT, 0, 画像分割数", "SPRITEDISPOSE @\"モブ画像_{モブグラフィックID}_%設定画面名%_{画像分割数}_{_CNT + 1}\"", "NEXT"]);
            g1Constants = BindG1Constants();
            g1Cflag = process.idDic.GetVariableToken("CFLAG", null, false);
            g1ScreenList = process.idDic.GetVariableToken("画像エディット_設定画面リスト", null, false);
            if (g1Cflag is null || !g1Cflag.IsInteger || !g1Cflag.IsCharacterData || !g1Cflag.IsArray1D
                || g1ScreenList is null || !g1ScreenList.IsString || !g1ScreenList.IsArray1D || g1ScreenList.GetLength() < 1)
                throw new InvalidOperationException($"R0-F4G1 variable schema cflag={g1Cflag is not null}:{g1Cflag?.IsInteger}:{g1Cflag?.IsCharacterData}:{g1Cflag?.Dimension} screen={g1ScreenList is not null}:{g1ScreenList?.IsString}:{g1ScreenList?.Dimension}");
            g1CflagSlot = process.vEvaluator.Constant.KeywordToInteger(VariableCode.CFLAG, "キャラ固有の番号", 1);
            if (g1CflagSlot < 0 || RuntimeConfig.TextDrawingMode == TextDrawingMode.WINAPI)
                throw new InvalidOperationException("R0-F4G1 host admission blocked before effect");
            g1Preflight = new
            {
                Schema = "emuera-r0f4g1-preflight-v1", Pass = stateBefore == process.GetBenchmarkStateHash(), EffectCount = 0,
                Constants = g1Constants, CflagSlot = g1CflagSlot,
                Definitions = new[] { DefinitionRow(parent), DefinitionRow(reset), DefinitionRow(sprite) },
                Edges = new[] { new { Caller = G1Parent, Callee = G1Reset, Kind = "StaticCall" },
                    new { Caller = G1Reset, Callee = G1SpriteReset, Kind = "StaticCall" },
                    new { Caller = G1Reset, Callee = "GCREATED", Kind = "ExistingHostGraphicsCommand" },
                    new { Caller = G1Reset, Callee = "SPRITEDISPOSE", Kind = "ExistingHostGraphicsCommand" },
                    new { Caller = G1Reset, Callee = "GDISPOSE", Kind = "ExistingHostGraphicsCommand" },
                    new { Caller = G1SpriteReset, Callee = "SPRITEDISPOSE", Kind = "ExistingHostGraphicsCommand" } },
                Unsupported = Array.Empty<string>(), DynamicCallees = 0, HostSeam = "AppContents existing graphics subsystem"
            };
        }

        private R0F1Definition G1Definition(string name, int line)
        {
            if (!functions.TryGetValue(name, out var values) || values.Length != 1 || values[0].IsEvent
                || values[0].Line != line || !values[0].RelativePath.EndsWith("画像表示.ERB", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("R0-F4G1 definition identity: " + name);
            return values[0];
        }

        private void RequireBody(R0F1Definition definition, string[] expected)
        {
            var actual = Executable(definition).Select(NormalizeG1).ToArray();
            if (!actual.SequenceEqual(expected.Select(NormalizeG1)))
                throw new InvalidOperationException("R0-F4G1 body mismatch: " + definition.Name);
        }
        private static string NormalizeG1(string value) => Regex.Replace(value.Split(';')[0].Trim(), "\\s+", " ");
        private object DefinitionRow(R0F1Definition x)
        {
            var source = Lines(Read(x.Function, x.File)); var body = Executable(x);
            return new { EffectiveName = x.Name, x.RelativePath, HeaderLine = x.Line, NextHeaderBoundary = x.Line + source.Length,
                Header = source[0].Trim(), Kind = "NormalFunction", Signature = source[0].TrimStart('@'), BodyStatements = body,
                SourceSha256 = FileHash(x.File.FileIdentity), BodySha256 = BodyHash(body), Classification = "ExistingCompactSemantic" };
        }

        private G1Constants BindG1Constants()
        {
            var declarations = new List<object>();
            var values = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in RuntimeConfig.GetFiles(Program.ErbDir, "*.ERH").Select(x => x.Value))
            foreach (var pair in File.ReadLines(path, RuntimeConfig.Encode).Select((text, i) => (text, line: i + 1)))
            {
                var match = Regex.Match(pair.text.Trim(), "^#DIM\\s+CONST\\s+([A-Z0-9_]+)\\s*=\\s*([^;]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (!match.Success) continue;
                var name = match.Groups[1].Value; var expression = match.Groups[2].Value.Trim();
                long value;
                if (!long.TryParse(expression, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                {
                    var add = Regex.Match(expression, "^([A-Z0-9_]+)(?:\\s*\\+\\s*([0-9]+))?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                    if (!add.Success || !values.TryGetValue(add.Groups[1].Value, out value)) continue;
                    if (add.Groups[2].Success) value = checked(value + long.Parse(add.Groups[2].Value, CultureInfo.InvariantCulture));
                }
                values[name] = value;
                if (name is "MAX_PLAYER_CHARA" or "CHARA_GIDNUM" or "MOB_CHARA_GIDNUM" or "MOB_CHARA_GIDNUM_START")
                    declarations.Add(new { Name = name, Value = value, Path = Path.GetRelativePath(DataRoot, path).Replace('\\', '/'), pair.line, Sha256 = FileHash(path) });
            }
            if (!values.TryGetValue("MOB_CHARA_GIDNUM", out var count) || !values.TryGetValue("MOB_CHARA_GIDNUM_START", out var start) || count <= 0)
                throw new InvalidOperationException("R0-F4G1 constant binding");
            return new(count, start, declarations.ToArray());
        }

        private void RunCandidateF4G1(Process process)
        {
            G1Materialize(G1Parent, 54_000); G1Call("SET_GAMEPLAY_START", G1Parent, [], []);
            Enter(new(F4D5Handle(54_000, G1Parent), 1026, "SYSTEM.ERB:1025"), 114, false);
            g1G1 = CaptureG1Point(process, "G1", "画像表示.ERB:114:before");
            for (g1Lcount = 0; g1Lcount < g1Constants!.MobCount; g1Lcount++)
            {
                G1Materialize(G1Reset, 54_001); G1Call(G1Parent, G1Reset, [0, g1Lcount], []);
                Enter(new(F4D5Handle(54_001, G1Reset), 116, "画像表示.ERB:115"), 94, false);
                RunCandidateG1Reset(process, 0, g1Lcount);
            }
            g1G3 = CaptureG1Point(process, "G3", "画像表示.ERB:116:before");
            process.vEvaluator.RESULT = 0; Return(process, G1Parent, false); g1ParentReturned = true;
            StoppedBefore = "SYSTEM.ERB:1026 CALL SET_EXTRA_TITLE_VAR";
            g1G4 = CaptureG1Point(process, "G4", "SYSTEM.ERB:1026:before");
        }

        private void RunCandidateG1Reset(Process process, long character, long mobId)
        {
            if (g1Cflag!.GetIntValue(process.exm, [character, g1CflagSlot]) < 0) { process.vEvaluator.RESULT = 0; Return(process, G1Reset, true); return; }
            if (mobId == -1) mobId = g1Cflag.GetIntValue(process.exm, [character, g1CflagSlot]);
            var graphicsId = checked(mobId + g1Constants!.GraphicsStart);
            if (G1Graphics(process, "GCREATED", graphicsId) == 0) { process.vEvaluator.RESULT = 0; Return(process, G1Reset, true); return; }
            var screens = (g1ScreenList!.GetStrValue(process.exm, [0]) ?? string.Empty).Split([","], StringSplitOptions.None);
            process.vEvaluator.RESULT = screens.Length;
            for (var screen = 1; screen < screens.Length; screen++)
            {
                RunCandidateG1Sprite(process, mobId, screens[screen], 4);
                RunCandidateG1Sprite(process, mobId, screens[screen], 6);
                G1Sprite(process, $"モブ画像_{mobId}_{screens[screen]}");
            }
            G1Graphics(process, "GDISPOSE", graphicsId);
            process.vEvaluator.RESULT = 0; Return(process, G1Reset, false);
        }

        private void RunCandidateG1Sprite(Process process, long mobId, string screen, long split)
        {
            G1Materialize(G1SpriteReset, 54_002); G1Call(G1Reset, G1SpriteReset, [mobId, split], [screen]);
            Enter(new(F4D5Handle(54_002, G1SpriteReset), 106, "画像表示.ERB:105"), 127, false);
            for (long i = 0; i < split; i++) G1Sprite(process, $"モブ画像_{mobId}_{screen}_{split}_{i + 1}");
            process.vEvaluator.RESULT = 0; Return(process, G1SpriteReset, false);
        }

        private long G1Graphics(Process process, string command, long id)
        {
            var before = AppContents.R0F4G1GraphicsState(id); var graphics = AppContents.GetGraphics(id);
            var result = graphics.IsCreated ? 1L : 0L;
            if (command == "GDISPOSE" && result != 0) graphics.GDispose();
            process.vEvaluator.RESULT = result; g1GraphicsIds.Add(id);
            G1Record(command, id.ToString(CultureInfo.InvariantCulture), result, before, AppContents.R0F4G1GraphicsState(id));
            return result;
        }

        private long G1Sprite(Process process, string name)
        {
            var before = AppContents.R0F4G1SpriteState(name); var result = before.Created ? 1L : 0L;
            if (result != 0) AppContents.SpriteDispose(name);
            process.vEvaluator.RESULT = result; g1SpriteNames.Add(name);
            G1Record("SPRITEDISPOSE", name, result, before, AppContents.R0F4G1SpriteState(name));
            return result;
        }

        private void RunLegacyF4G1(Process process)
        {
            var stopped = process.state.CurrentLine; var previous = (LogicalLine?)stopped?.ParentLabelLine;
            while (previous is not null && !ReferenceEquals(previous.NextLine, stopped)) previous = previous.NextLine;
            if (previous is null) throw new InvalidOperationException("R0-F4G1 Legacy resume predecessor");
            process.state.CurrentLine = previous; g1LegacyActive = true;
            try { process.runScriptProc(); throw new InvalidOperationException("R0-F4G1 Legacy crossed SYSTEM1026"); }
            catch (R0F1PlannedCheckpointException) { }
            finally { g1LegacyActive = false; }
        }

        internal void ObserveF4G1Entry(Process process, CalledFunction call)
        {
            if (!g1LegacyActive) return;
            var name = call.TopLabel.LabelName;
            if (name.Equals(G1Parent, RuntimeConfig.StringComparison))
            {
                g1LegacyLcount = call.TopLabel.GetPrivateVariable("LCOUNT"); G1Materialize(G1Parent, 54_000);
                G1Call("SET_GAMEPLAY_START", G1Parent, [], []); Enter(new(F4D5Handle(54_000, G1Parent), 1026, "SYSTEM.ERB:1025"), 114, false);
                g1G1 = CaptureG1Point(process, "G1", "画像表示.ERB:114:before"); return;
            }
            if (name.Equals(G1Reset, RuntimeConfig.StringComparison))
            {
                var character = G1Private(call, "キャラ番号", process); var mob = G1Private(call, "モブグラフィックID", process);
                G1Materialize(G1Reset, 54_001); G1Call(G1Parent, G1Reset, [character, mob], []);
                Enter(new(F4D5Handle(54_001, G1Reset), 116, "画像表示.ERB:115"), 94, false); return;
            }
            if (name.Equals(G1SpriteReset, RuntimeConfig.StringComparison))
            {
                var mob = G1Private(call, "モブグラフィックID", process); var split = G1Private(call, "画像分割数", process);
                var screen = call.TopLabel.GetPrivateVariable("設定画面名")?.GetStrValue(process.exm, [0]) ?? "";
                G1Materialize(G1SpriteReset, 54_002); G1Call(G1Reset, G1SpriteReset, [mob, split], [screen]);
                Enter(new(F4D5Handle(54_002, G1SpriteReset), 106, "画像表示.ERB:105"), 127, false);
            }
        }

        internal void ObserveF4G1Instruction(Process process, InstructionLine line, bool before)
        {
            if (!g1LegacyActive) return;
            if (before && IsF4D5Line(line, "SYSTEM.ERB", 1026))
            {
                if (!g1ParentReturned) throw new InvalidOperationException("R0-F4G1 parent did not return");
                StoppedBefore = "SYSTEM.ERB:1026 CALL SET_EXTRA_TITLE_VAR"; g1G4 = CaptureG1Point(process, "G4", "SYSTEM.ERB:1026:before");
                throw new R0F1PlannedCheckpointException();
            }
            if (line.Position is not { } p || !Path.GetFileName(p.Filename).Equals(G1File, StringComparison.OrdinalIgnoreCase)) return;
            if (before && p.LineNo == 97)
            {
                var mob = G1Private(process.state.CurrentCalled, "モブグラフィックID", process); var id = checked(mob + g1Constants!.GraphicsStart);
                G1Begin("GCREATED", id.ToString(CultureInfo.InvariantCulture), AppContents.R0F4G1GraphicsState(id)); g1GraphicsIds.Add(id);
            }
            else if (!before && p.LineNo == 97) G1End(AppContents.R0F4G1GraphicsState(long.Parse(g1PendingArgument, CultureInfo.InvariantCulture)));
            if (before && p.LineNo is 94 or 98) { process.vEvaluator.RESULT = 0; Return(process, G1Reset, true); }
            if (before && p.LineNo == 107) G1BeginLegacySprite(process);
            else if (!before && p.LineNo == 107) G1End(AppContents.R0F4G1SpriteState(g1PendingArgument));
            if (before && p.LineNo == 128) G1BeginLegacySprite(process);
            else if (!before && p.LineNo == 128) G1End(AppContents.R0F4G1SpriteState(g1PendingArgument));
            if (before && p.LineNo == 110)
            {
                var mob = G1Private(process.state.CurrentCalled, "モブグラフィックID", process); var id = checked(mob + g1Constants!.GraphicsStart);
                G1Begin("GDISPOSE", id.ToString(CultureInfo.InvariantCulture), AppContents.R0F4G1GraphicsState(id)); g1GraphicsIds.Add(id);
            }
            else if (!before && p.LineNo == 110) G1End(AppContents.R0F4G1GraphicsState(long.Parse(g1PendingArgument, CultureInfo.InvariantCulture)));
        }

        internal void ObserveF4G1Fallthrough(Process process, LogicalLine line, bool before)
        {
            if (!g1LegacyActive) return;
            if (!before && g1ParentReturnPending)
            {
                g1ParentReturnPending = false; process.vEvaluator.RESULT = 0;
                Return(process, G1Parent, false); g1ParentReturned = true; return;
            }
            if (process.state.functionCount == 0) return;
            var current = process.state.CurrentCalled.FunctionName;
            if (current.Equals(G1SpriteReset, RuntimeConfig.StringComparison) && before) { process.vEvaluator.RESULT = 0; Return(process, G1SpriteReset, false); return; }
            if (current.Equals(G1Reset, RuntimeConfig.StringComparison) && before) { process.vEvaluator.RESULT = 0; Return(process, G1Reset, false); return; }
            if (current.Equals(G1Parent, RuntimeConfig.StringComparison))
            {
                if (before) { g1LegacyFinalLcount = g1LegacyLcount!.GetIntValue(process.exm, [0]); g1G3 = CaptureG1Point(process, "G3", "画像表示.ERB:116:before"); g1ParentReturnPending = true; }
            }
        }

        private void G1BeginLegacySprite(Process process)
        {
            var call = process.state.CurrentCalled; var mob = G1Private(call, "モブグラフィックID", process);
            var screen = call.TopLabel.GetPrivateVariable("設定画面名")?.GetStrValue(process.exm, [0]) ?? "";
            var line = process.state.CurrentLine!.Position!.Value.LineNo;
            string name;
            if (line == 128)
            {
                var split = G1Private(call, "画像分割数", process); var count = G1Private(call, "_CNT", process);
                name = $"モブ画像_{mob}_{screen}_{split}_{count + 1}";
            }
            else name = $"モブ画像_{mob}_{screen}";
            g1SpriteNames.Add(name); G1Begin("SPRITEDISPOSE", name, AppContents.R0F4G1SpriteState(name));
        }

        private void G1Begin(string command, string argument, AppContents.R0F4G1ImageState before)
        { g1PendingCommand = command; g1PendingArgument = argument; g1PendingState = before; g1PendingResult = before.Created ? 1 : 0; }
        private void G1End(AppContents.R0F4G1ImageState after)
        { G1Record(g1PendingCommand, g1PendingArgument, g1PendingResult, g1PendingState!.Value, after); g1PendingState = null; }
        private void G1Record(string command, string argument, long result, AppContents.R0F4G1ImageState before, AppContents.R0F4G1ImageState after)
        {
            g1Operations.Add(new(g1Operations.Count + 1, command, argument, result, before, after));
            if (g1Operations.Count == 1) g1G2 = new { Name = "G2", ProgramCounter = command, Argument = argument, Result = result,
                Before = before, After = after, ResourceSha256 = G1ResourceHash() };
        }
        private static bool G1OperationValid(G1Operation x) => x.Result == (x.Before.Created ? 1 : 0)
            && (x.Command == "GCREATED" ? x.After.Present && x.After.Created == x.Before.Created
                : x.Result == 0 ? x.After.Equals(x.Before) : !x.After.Created);
        private void G1Call(string caller, string callee, long[] ints, string[] strings) => g1Calls.Add(new(g1Calls.Count + 1, caller, callee, ints, strings));
        private void G1Materialize(string name, int id) { if (g1Materialized.Add(name)) _ = F4D5Handle(id, name); DemandCompiledBodies = Math.Max(DemandCompiledBodies, g1Materialized.Count); }
        private static long G1Private(CalledFunction call, string name, Process process) => call.TopLabel.GetPrivateVariable(name)?.GetIntValue(process.exm, [0])
            ?? throw new InvalidOperationException("R0-F4G1 private bind: " + name);
        private long ReadG1Lcount(Process process) => Candidate ? g1Lcount : g1ParentReturned ? g1LegacyFinalLcount : g1LegacyLcount?.GetIntValue(process.exm, [0]) ?? 0;
        private object G1ResourceSnapshot() => new { Graphics = g1GraphicsIds.Order().Select(AppContents.R0F4G1GraphicsState).ToArray(),
            Sprites = g1SpriteNames.OrderBy(x => x, RuntimeConfig.StrComper).Select(AppContents.R0F4G1SpriteState).ToArray(), Sha256 = G1ResourceHash() };
        private string G1ResourceHash() => Hash(string.Join('\n', g1GraphicsIds.Order().Select(x => AppContents.R0F4G1GraphicsState(x).ToString())
            .Concat(g1SpriteNames.OrderBy(x => x, RuntimeConfig.StrComper).Select(x => AppContents.R0F4G1SpriteState(x).ToString()))));
        private G1PointRow CaptureG1Point(Process process, string name, string pc) => new(name, pc, ReadG1Lcount(process),
            string.Join(" > ", compactFrames.Select(FrameText)), EventCursor, process.state.SystemState.ToString(), process.state.isBegun,
            process.vEvaluator.GetR0C2RngHash(), process.vEvaluator.GetR0F4D3RandomCallCount(), DifferentialDeterminism.ObservationCount,
            process.vEvaluator.RESULT, HashStrings(process.vEvaluator.RESULTS_ARRAY), G1ResourceHash());
    }
}
#endif
