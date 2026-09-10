#if R0_F4G2
#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MinorShift.Emuera.GameData.Function;
using MinorShift.Emuera.GameData.Variable;
using MinorShift.Emuera.Next.Compiler;
using MinorShift.Emuera.Next.Core;
using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Runtime.Diagnostics;
using MinorShift.Emuera.Runtime.Script;
using MinorShift.Emuera.Runtime.Script.Statements;
using MinorShift.Emuera.UI.Game.Image;
using RuntimeConfig = MinorShift.Emuera.Runtime.Config.Config;

namespace MinorShift.Emuera.GameProc;

internal sealed partial class Process
{
    private sealed partial class R0F1Context
    {
        private const string G2Title = "SET_EXTRA_TITLE_VAR";
        private const string G2Master = "マスター回転画像作成";
        private const string G2MasterFile = "主人マップアイコン.ERB";
        private static readonly string[] G2AnimationNames = ["＠時計回り", "＠反時計回り", "＠中庸"];

        private sealed record G2TitleWrite(int Sequence, int SourceLine, long Index, string Value, string ValueSha256);
        private sealed record G2HostOperation(int Sequence, int SourceLine, string Command, string[] Arguments, long Result,
            AppContents.R0F4G2ResourceState Before, AppContents.R0F4G2ResourceState After);
        private sealed record G2Point(string Name, string ProgramCounter, string Frames, string EventCursor,
            string SystemState, bool PendingBegin, string RngSha256, long RngCalls, long ClockCalls,
            long Result0, string ResultsSha256, string ExtraTitleSha256, string ResourceSha256);

        internal object? F4G2Evidence { get; private set; }
        private readonly List<G2TitleWrite> g2TitleWrites = [];
        private readonly List<G2HostOperation> g2HostOperations = [];
        private readonly HashSet<string> g2Materialized = new(RuntimeConfig.StrComper);
        private BoundIndexedStringSlot g2ExtraTitle = null!;
        private G2TitleWrite[] g2TitleProgram = [];
        private R0F1Definition g2TitleDefinition = null!;
        private R0F1Definition g2MasterDefinition = null!;
        private long g2GraphicsBase;
        private bool g2LegacyActive, g2TitleReturnPending, g2MasterReturnPending, g2TitleReturned, g2MasterReturned;
        private G2HostOperation? g2PendingOperation;
        private object? g2H0, g2H1, g2H2, g2H3, g2H4, g2TitlePreflight, g2MasterPreflight;
        private string[] g2InitialTitle = [];
        private string g2RngBefore = "";
        private long g2RngCallsBefore, g2ClockBefore;

        internal void ExecuteF4G2(Process process)
        {
            if (StoppedBefore != "SYSTEM.ERB:1026 CALL SET_EXTRA_TITLE_VAR"
                || compactFrames.Count != 1 || compactFrames[0].Handle.Name != "SET_GAMEPLAY_START"
                || compactFrames[0].Pc != 1026)
                throw new InvalidOperationException("R0-F4G2 requires the fresh F4G1 SYSTEM1026 boundary");

            PrepareF4G2Title(process);
            g2InitialTitle = ReadG2Title(process);
            g2RngBefore = process.vEvaluator.GetR0C2RngHash();
            g2RngCallsBefore = process.vEvaluator.GetR0F4D3RandomCallCount();
            g2ClockBefore = DifferentialDeterminism.ObservationCount;
            g2H0 = CaptureG2Point(process, "H0", "SYSTEM.ERB:1026:before");

            if (Candidate) RunCandidateF4G2(process); else RunLegacyF4G2(process);

            var finalTitle = ReadG2Title(process);
            var titlePass = g2TitleWrites.Count == 99
                && g2TitleWrites.Select((x, i) => x.Index == g2TitleProgram[i].Index && x.Value == g2TitleProgram[i].Value).All(x => x)
                && g2TitleProgram.All(x => finalTitle[x.Index] == x.Value)
                && Enumerable.Range(0, finalTitle.Length).Where(i => i >= 99).All(i => finalTitle[i] == g2InitialTitle[i]);
            var hostPass = g2HostOperations.Count > 0 && g2HostOperations.All(G2OperationValid)
                && Enumerable.Range(0, 6).All(i => { var s = AppContents.R0F4G2GraphicsState(g2GraphicsBase + i); return s.Created && s.Width == 16 && s.Height == 16; })
                && AppContents.R0F4G2SpriteState(G2AnimationNames[0]) is { Created: true, Width: 16, Height: 16, Frames: 6, TotalDelayMs: 750 }
                && AppContents.R0F4G2SpriteState(G2AnimationNames[1]) is { Created: true, Width: 16, Height: 16, Frames: 6, TotalDelayMs: 750 }
                && AppContents.R0F4G2SpriteState(G2AnimationNames[2]) is { Created: true, Width: 16, Height: 16, Frames: 8, TotalDelayMs: 1000 };
            var guardTotal = Candidate ? R0E1AProof.Counters.Sum() : 0;
            var pass = titlePass && hostPass && g2TitleReturned && g2MasterReturned
                && g2H0 is not null && g2H1 is not null && g2H2 is not null && g2H3 is not null && g2H4 is not null
                && compactFrames.Count == 1 && compactFrames[0].Handle.Name == "SET_GAMEPLAY_START" && compactFrames[0].Pc == 1031
                && StoppedBefore == "SYSTEM.ERB:1031 CALL INITIALIZE_MESSAGE"
                && process.vEvaluator.GetR0C2RngHash() == g2RngBefore
                && process.vEvaluator.GetR0F4D3RandomCallCount() == g2RngCallsBefore
                && DifferentialDeterminism.ObservationCount == g2ClockBefore
                && guardTotal == 0 && B1Proof.BridgeAttempts == 0;

            F4G2Evidence = new
            {
                Schema = "emuera-r0f4g2-extra-title-master-rotation-v1",
                Mode = Candidate ? "GraphFreeCandidate" : "LegacyControl",
                Scope = "SET_EXTRA_TITLE_AND_MASTER_ROTATION",
                GateResult = pass ? "PASS" : "FAIL",
                R0F4G2Gate = pass ? "PASS" : "FAIL",
                ExtraTitleGate = titlePass ? "PASS" : "FAIL",
                MasterRotationGate = hostPass ? "PASS" : "FAIL",
                ResumedFrom = "SYSTEM.ERB:1026 CALL SET_EXTRA_TITLE_VAR",
                StoppedBefore,
                Checkpoints = new { H0 = g2H0, H1 = g2H1, H2 = g2H2, H3 = g2H3, H4 = g2H4 },
                ExtraTitlePreflight = g2TitlePreflight,
                MasterRotationPreflight = g2MasterPreflight,
                ExtraTitle = new
                {
                    Initial = g2InitialTitle,
                    Final = finalTitle,
                    InitialSha256 = HashStrings(g2InitialTitle),
                    FinalSha256 = HashStrings(finalTitle),
                    Program = g2TitleProgram,
                    Writes = g2TitleWrites.ToArray(),
                    WriteCount = g2TitleWrites.Count,
                    UnchangedTail = Enumerable.Range(99, Math.Max(0, finalTitle.Length - 99)).All(i => finalTitle[i] == g2InitialTitle[i])
                },
                HostOperations = g2HostOperations.ToArray(),
                GraphicsResourceState = G2ResourceSnapshot(),
                Materialization = new
                {
                    StartupCompiled = 0,
                    Names = g2Materialized.OrderBy(x => x, RuntimeConfig.StrComper).ToArray(),
                    Count = g2Materialized.Count,
                    ClosureOnly = g2Materialized.All(x => x is G2Title or G2Master)
                },
                NextInitializeMessageInventory = InventoryG2Next(),
                SetExtraTitleVarCompleted = g2TitleReturned,
                MasterRotationCompleted = g2MasterReturned,
                InitializeMessageExecuted = false,
                Guards = new { Total = guardTotal, Categories = Candidate ? R0E1AProof.GuardSnapshot() : null },
                ExternalEffects = new { FileIO = 0, Display = 0, Input = 0, RNG = 0, Clock = 0, Graphics = g2HostOperations.Count },
                LegacyErbGraphAvoided = Candidate,
                LegacyRetryAfterCompact = 0,
                ProductionBridgeUsed = "NO",
                ManualRecaptureRequired = "NO",
                WholeProductSuperiority = "NOT_YET_CLAIMED"
            };
            if (!pass) throw new InvalidOperationException($"R0-F4G2 gate failed title={titlePass} host={hostPass} ops={g2HostOperations.Count} stop={StoppedBefore}");
        }

        private void PrepareF4G2Title(Process process)
        {
            var stateBefore = process.GetBenchmarkStateHash();
            g2TitleDefinition = G2Definition(G2Title, 14, "作品管理/SET_EXTRA_TITLE_VAR.ERB");
            var body = Executable(g2TitleDefinition);
            var rows = new List<G2TitleWrite>();
            var regex = new Regex("^EXTRA_TITLE:([0-9]+)\\s*'=\\s*\"(.*)\"$", RegexOptions.CultureInvariant);
            for (var i = 0; i < body.Length; i++)
            {
                var match = regex.Match(body[i]);
                if (!match.Success || !long.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var index))
                    throw new InvalidOperationException("R0-F4G2 title statement: " + body[i]);
                var value = match.Groups[2].Value;
                rows.Add(new(i + 1, g2TitleDefinition.Line + i + 1, index, value, Hash(value)));
            }
            var compile = new FunctionCompiler(environment).TryCompileRuntime(g2TitleDefinition.File, g2TitleDefinition.Function);
            if (rows.Count != 99 || !rows.Select(x => x.Index).SequenceEqual(Enumerable.Range(0, 99).Select(x => (long)x))
                || compile.Status != CompileStatus.Compiled || compile.Function is null || compile.Function.Instructions.Length != 99)
                throw new InvalidOperationException("R0-F4G2 title compiler/schema preflight");
            g2TitleProgram = rows.ToArray();
            var token = process.idDic.GetVariableToken("EXTRA_TITLE", null, false);
            if (token is null || !token.IsString || !token.IsArray1D || token.IsCharacterData || token.IsLocal || token.IsPrivate || token.IsConst || token.GetLength() < 99)
                throw new InvalidOperationException("R0-F4G2 EXTRA_TITLE binding");
            token.CheckElement([0]); token.CheckElement([98]);
            g2ExtraTitle = new("EXTRA_TITLE", token, token.GetLength());
            g2TitlePreflight = new
            {
                Schema = "emuera-r0f4g2-extra-title-preflight-v1",
                Pass = stateBefore == process.GetBenchmarkStateHash(),
                EffectCount = 0,
                Definition = G2DefinitionRow(g2TitleDefinition),
                RuntimeCompile = new { Status = compile.Status.ToString(), Instructions = compile.Function.Instructions.Length },
                Program = g2TitleProgram,
                Destination = new { g2ExtraTitle.Name, g2ExtraTitle.Length, First = 0, Last = 98 }
            };
        }

        private void PrepareF4G2Master(Process process)
        {
            var stateBefore = process.GetBenchmarkStateHash();
            g2MasterDefinition = G2Definition(G2Master, 37, "画像処理/主人マップアイコン.ERB");
            var expected = new[]
            {
                "#DIM DYNAMIC L_GID = FILTER_GID +100", "FOR LOCAL , 0 , 6", "GCREATE L_GID+LOCAL , 16 , 16", "GDRAWSPRITE L_GID+LOCAL , @\"＠{LOCAL}\"", "NEXT",
                "IF SPRITECREATED(\"＠時計回り\")", "SPRITEDISPOSE \"＠時計回り\"", "SPRITEDISPOSE \"＠反時計回り\"", "SPRITEDISPOSE \"＠中庸\"", "ENDIF",
                "SPRITEANIMECREATE \"＠時計回り\" , 16 , 16", "FOR LOCAL , 5 , -1 , -1", "SPRITEANIMEADDFRAME \"＠時計回り\" , L_GID+LOCAL , 0 , 0 , 16 , 16 , 0 , 0 , 125", "NEXT",
                "SPRITEANIMECREATE \"＠反時計回り\" , 16 , 16", "FOR LOCAL , 0 , 6", "SPRITEANIMEADDFRAME \"＠反時計回り\" , L_GID+LOCAL , 0 , 0 , 16 , 16 , 0 , 0 , 125", "NEXT",
                "SPRITEANIMECREATE \"＠中庸\" , 16 , 16",
                "SPRITEANIMEADDFRAME \"＠中庸\" , L_GID+0 , 0 , 0 , 16 , 16 , 0 , 0 , 125",
                "SPRITEANIMEADDFRAME \"＠中庸\" , L_GID+1 , 0 , 0 , 16 , 16 , 0 , 0 , 125",
                "SPRITEANIMEADDFRAME \"＠中庸\" , L_GID+2 , 0 , 0 , 16 , 16 , 0 , 0 , 125",
                "SPRITEANIMEADDFRAME \"＠中庸\" , L_GID+1 , 0 , 0 , 16 , 16 , 0 , 0 , 125",
                "SPRITEANIMEADDFRAME \"＠中庸\" , L_GID+0 , 0 , 0 , 16 , 16 , 0 , 0 , 125",
                "SPRITEANIMEADDFRAME \"＠中庸\" , L_GID+5 , 0 , 0 , 16 , 16 , 0 , 0 , 125",
                "SPRITEANIMEADDFRAME \"＠中庸\" , L_GID+4 , 0 , 0 , 16 , 16 , 0 , 0 , 125",
                "SPRITEANIMEADDFRAME \"＠中庸\" , L_GID+5 , 0 , 0 , 16 , 16 , 0 , 0 , 125"
            };
            var actual = Executable(g2MasterDefinition).Select(NormalizeG1).ToArray();
            var constantPath = Path.Combine(DataRoot, "ERB", "関数", "汎用組み込み関数", "画像処理", "画像処理.ERH");
            var constant = File.ReadLines(constantPath, RuntimeConfig.Encode).Select((text, i) => (match: Regex.Match(text.Trim(), "^#DIM\\s+CONST\\s+FILTER_GID\\s*=\\s*([0-9]+)", RegexOptions.IgnoreCase), line: i + 1)).Single(x => x.match.Success);
            g2GraphicsBase = long.Parse(constant.match.Groups[1].Value, CultureInfo.InvariantCulture) + 100;
            var sourceSprites = Enumerable.Range(0, 6).Select(i => AppContents.R0F4G2SpriteState($"＠{i}")).ToArray();
            if (!actual.SequenceEqual(expected.Select(NormalizeG1)) || RuntimeConfig.TextDrawingMode == TextDrawingMode.WINAPI
                || sourceSprites.Any(x => !x.Created))
                throw new InvalidOperationException("R0-F4G2 master closure blocked before effect");
            g2MasterPreflight = new
            {
                Schema = "emuera-r0f4g2-master-rotation-preflight-v1",
                Pass = stateBefore == process.GetBenchmarkStateHash(),
                EffectCount = 0,
                Definition = G2DefinitionRow(g2MasterDefinition),
                Constant = new { Name = "FILTER_GID", Value = g2GraphicsBase - 100, GraphicsBase = g2GraphicsBase, Path = Path.GetRelativePath(DataRoot, constantPath).Replace('\\', '/'), constant.line, Sha256 = FileHash(constantPath) },
                Closure = new { DynamicCallees = 0, UserCallees = Array.Empty<string>(), HostCommands = new[] { "GCREATE", "GDRAWSPRITE", "SPRITECREATED", "SPRITEDISPOSE", "SPRITEANIMECREATE", "SPRITEANIMEADDFRAME" }, Unsupported = Array.Empty<string>() },
                SourceSprites = sourceSprites
            };
        }

        private void RunCandidateF4G2(Process process)
        {
            G2Materialize(G2Title, 55_000);
            Enter(new(F4D5Handle(55_000, G2Title), 1028, "SYSTEM.ERB:1026"), 15, false);
            foreach (var row in g2TitleProgram)
            {
                g2ExtraTitle.Write(row.Value, row.Index);
                g2TitleWrites.Add(row with { Sequence = g2TitleWrites.Count + 1 });
            }
            process.vEvaluator.RESULT = 0; Return(process, G2Title, false); g2TitleReturned = true;
            StoppedBefore = "SYSTEM.ERB:1028 CALL マスター回転画像作成";
            g2H1 = CaptureG2Point(process, "H1", "SYSTEM.ERB:1028:before");
            PrepareF4G2Master(process);
            G2Materialize(G2Master, 55_001);
            Enter(new(F4D5Handle(55_001, G2Master), 1031, "SYSTEM.ERB:1028"), 38, false);
            g2H2 = CaptureG2Point(process, "H2", "主人マップアイコン.ERB:38:before");
            RunCandidateG2Master(process);
            process.vEvaluator.RESULT = 0; Return(process, G2Master, false); g2MasterReturned = true;
            StoppedBefore = "SYSTEM.ERB:1031 CALL INITIALIZE_MESSAGE";
            g2H4 = CaptureG2Point(process, "H4", "SYSTEM.ERB:1031:before");
        }

        private void RunCandidateG2Master(Process process)
        {
            for (var i = 0; i < 6; i++)
            {
                G2CreateGraphics(process, 41, g2GraphicsBase + i, 16, 16);
                G2DrawSprite(process, 43, g2GraphicsBase + i, $"＠{i}");
            }
            var clockwise = G2SpriteCreated(process, 46, G2AnimationNames[0]);
            if (clockwise != 0) foreach (var name in G2AnimationNames) G2DisposeSprite(process, name == G2AnimationNames[0] ? 47 : name == G2AnimationNames[1] ? 48 : 49, name);
            G2CreateAnime(process, 51, G2AnimationNames[0], 16, 16);
            for (var i = 5; i >= 0; i--) G2AddFrame(process, 53, G2AnimationNames[0], g2GraphicsBase + i);
            G2CreateAnime(process, 56, G2AnimationNames[1], 16, 16);
            for (var i = 0; i < 6; i++) G2AddFrame(process, 58, G2AnimationNames[1], g2GraphicsBase + i);
            G2CreateAnime(process, 61, G2AnimationNames[2], 16, 16);
            var neutral = new[] { 0, 1, 2, 1, 0, 5, 4, 5 };
            for (var i = 0; i < neutral.Length; i++) G2AddFrame(process, 62 + i, G2AnimationNames[2], g2GraphicsBase + neutral[i]);
        }

        private void RunLegacyF4G2(Process process)
        {
            var stopped = process.state.CurrentLine; var previous = (LogicalLine?)stopped?.ParentLabelLine;
            while (previous is not null && !ReferenceEquals(previous.NextLine, stopped)) previous = previous.NextLine;
            if (previous is null) throw new InvalidOperationException("R0-F4G2 Legacy resume predecessor");
            process.state.CurrentLine = previous; g2LegacyActive = true;
            try { process.runScriptProc(); throw new InvalidOperationException("R0-F4G2 Legacy crossed SYSTEM1031"); }
            catch (R0F1PlannedCheckpointException) { }
            finally { g2LegacyActive = false; }
        }

        internal void ObserveF4G2Entry(Process process, CalledFunction call)
        {
            if (!g2LegacyActive) return;
            var name = call.TopLabel.LabelName;
            if (name.Equals(G2Title, RuntimeConfig.StringComparison))
            {
                G2Materialize(G2Title, 55_000); Enter(new(F4D5Handle(55_000, G2Title), 1028, "SYSTEM.ERB:1026"), 15, false);
            }
            else if (name.Equals(G2Master, RuntimeConfig.StringComparison))
            {
                G2Materialize(G2Master, 55_001); Enter(new(F4D5Handle(55_001, G2Master), 1031, "SYSTEM.ERB:1028"), 38, false);
                g2H2 = CaptureG2Point(process, "H2", "主人マップアイコン.ERB:38:before");
            }
        }

        internal void ObserveF4G2StringWrite(Process process, InstructionLine line, string? value, bool before)
        {
            if (!g2LegacyActive || !before || value is null || line.Position is not { } p
                || !Path.GetFileName(p.Filename).Equals("SET_EXTRA_TITLE_VAR.ERB", StringComparison.OrdinalIgnoreCase)) return;
            var expected = g2TitleProgram[p.LineNo - 15];
            if (expected.SourceLine != p.LineNo || expected.Value != value) throw new InvalidOperationException("R0-F4G2 Legacy title write mismatch");
            g2TitleWrites.Add(expected with { Sequence = g2TitleWrites.Count + 1 });
        }

        internal void ObserveF4G2Instruction(Process process, InstructionLine line, bool before)
        {
            if (!g2LegacyActive || line.Position is not { } p) return;
            if (before && IsF4D5Line(line, "SYSTEM.ERB", 1028))
            {
                if (!g2TitleReturned && compactFrames.Count == 2 && compactFrames[^1].Handle.Name == G2Title)
                {
                    process.vEvaluator.RESULT = 0; Return(process, G2Title, false); g2TitleReturned = true;
                }
                if (!g2TitleReturned) throw new InvalidOperationException("R0-F4G2 title did not return");
                StoppedBefore = "SYSTEM.ERB:1028 CALL マスター回転画像作成";
                g2H1 = CaptureG2Point(process, "H1", "SYSTEM.ERB:1028:before");
                PrepareF4G2Master(process);
                return;
            }
            if (before && IsF4D5Line(line, "SYSTEM.ERB", 1031))
            {
                if (!g2MasterReturned && compactFrames.Count == 2 && compactFrames[^1].Handle.Name == G2Master)
                {
                    process.vEvaluator.RESULT = 0; Return(process, G2Master, false); g2MasterReturned = true;
                }
                if (!g2MasterReturned) throw new InvalidOperationException("R0-F4G2 master did not return");
                StoppedBefore = "SYSTEM.ERB:1031 CALL INITIALIZE_MESSAGE";
                g2H4 = CaptureG2Point(process, "H4", "SYSTEM.ERB:1031:before");
                throw new R0F1PlannedCheckpointException();
            }
            if (!Path.GetFileName(p.Filename).Equals(G2MasterFile, StringComparison.OrdinalIgnoreCase) || !G2HostLine(p.LineNo)) return;
            if (before) G2BeginLegacyOperation(process, p.LineNo); else G2EndLegacyOperation(process, p.LineNo);
        }

        internal void ObserveF4G2Fallthrough(Process process, LogicalLine line, bool before)
        {
            if (!g2LegacyActive || process.state.functionCount == 0) return;
            var current = process.state.CurrentCalled.FunctionName;
            if (current.Equals(G2Title, RuntimeConfig.StringComparison))
            {
                if (before) g2TitleReturnPending = true;
                else if (g2TitleReturnPending) { g2TitleReturnPending = false; process.vEvaluator.RESULT = 0; Return(process, G2Title, false); g2TitleReturned = true; }
            }
            else if (current.Equals(G2Master, RuntimeConfig.StringComparison))
            {
                if (before) g2MasterReturnPending = true;
                else if (g2MasterReturnPending) { g2MasterReturnPending = false; process.vEvaluator.RESULT = 0; Return(process, G2Master, false); g2MasterReturned = true; }
            }
        }

        private void G2BeginLegacyOperation(Process process, int line)
        {
            var local = process.idDic.GetNextRuntimeLocalVariableToken("LOCAL", process.state.CurrentCalled.TopLabel).GetIntValue(process.exm, [0]);
            var baseId = G2Private(process.state.CurrentCalled, "L_GID", process);
            var (command, args, before, result) = G2DescribeOperation(line, local, baseId);
            g2PendingOperation = new(g2HostOperations.Count + 1, line, command, args, result, before, before);
        }

        private void G2EndLegacyOperation(Process process, int line)
        {
            if (g2PendingOperation is not { } pending || pending.SourceLine != line) throw new InvalidOperationException("R0-F4G2 Legacy host operation pairing");
            var after = G2TargetState(pending.Command, pending.Arguments);
            G2RecordOperation(pending with { After = after });
            g2PendingOperation = null;
        }

        private (string Command, string[] Arguments, AppContents.R0F4G2ResourceState Before, long Result) G2DescribeOperation(int line, long local, long baseId)
        {
            if (line == 41) return G2DescribeGraphics("GCREATE", baseId + local, ["16", "16"]);
            if (line == 43) return G2DescribeDraw(baseId + local, $"＠{local}");
            if (line == 46) return G2DescribeSprite("SPRITECREATED", G2AnimationNames[0]);
            if (line is >= 47 and <= 49) return G2DescribeSprite("SPRITEDISPOSE", G2AnimationNames[line - 47]);
            if (line is 51 or 56 or 61) return G2DescribeSprite("SPRITEANIMECREATE", line == 51 ? G2AnimationNames[0] : line == 56 ? G2AnimationNames[1] : G2AnimationNames[2], ["16", "16"]);
            if (line is 53 or 58) return G2DescribeAdd(line == 53 ? G2AnimationNames[0] : G2AnimationNames[1], baseId + local);
            if (line is >= 62 and <= 69)
            {
                var ids = new long[] { 0, 1, 2, 1, 0, 5, 4, 5 };
                return G2DescribeAdd(G2AnimationNames[2], baseId + ids[line - 62]);
            }
            throw new InvalidOperationException("R0-F4G2 host source line: " + line);
        }

        private (string, string[], AppContents.R0F4G2ResourceState, long) G2DescribeGraphics(string command, long id, string[]? tail = null)
        {
            var before = AppContents.R0F4G2GraphicsState(id);
            return (command, [id.ToString(CultureInfo.InvariantCulture), .. tail ?? []], before, command == "GCREATE" ? before.Created ? 0 : 1 : before.Created ? 1 : 0);
        }
        private (string, string[], AppContents.R0F4G2ResourceState, long) G2DescribeDraw(long id, string sprite)
        {
            var before = AppContents.R0F4G2GraphicsState(id);
            return ("GDRAWSPRITE", [id.ToString(CultureInfo.InvariantCulture), sprite], before, before.Created && AppContents.R0F4G2SpriteState(sprite).Created ? 1 : 0);
        }
        private (string, string[], AppContents.R0F4G2ResourceState, long) G2DescribeSprite(string command, string name, string[]? tail = null)
        {
            var before = AppContents.R0F4G2SpriteState(name);
            var result = command switch { "SPRITECREATED" => before.Created ? 1 : 0, "SPRITEDISPOSE" => before.Created ? 1 : 0, "SPRITEANIMECREATE" => before.Created ? 0 : 1, _ => 0 };
            return (command, [name, .. tail ?? []], before, result);
        }
        private (string, string[], AppContents.R0F4G2ResourceState, long) G2DescribeAdd(string name, long id)
        {
            var before = AppContents.R0F4G2SpriteState(name);
            var result = before.Created && AppContents.R0F4G2GraphicsState(id).Created ? 1 : 0;
            return ("SPRITEANIMEADDFRAME", [name, id.ToString(CultureInfo.InvariantCulture), "0", "0", "16", "16", "0", "0", "125"], before, result);
        }

        private void G2CreateGraphics(Process process, int line, long id, int width, int height)
        {
            var described = G2DescribeGraphics("GCREATE", id, [width.ToString(CultureInfo.InvariantCulture), height.ToString(CultureInfo.InvariantCulture)]);
            if (described.Item4 != 0) AppContents.GetGraphics(id).GCreate(width, height, false);
            process.vEvaluator.RESULT = described.Item4; G2RecordOperation(new(g2HostOperations.Count + 1, line, described.Item1, described.Item2, described.Item4, described.Item3, AppContents.R0F4G2GraphicsState(id)));
        }
        private void G2DrawSprite(Process process, int line, long id, string sprite)
        {
            var described = G2DescribeDraw(id, sprite);
            if (described.Item4 != 0)
            {
                var image = AppContents.GetSprite(sprite)!;
                AppContents.GetGraphics(id).GDrawCImg(image, new Rectangle(0, 0, image.DestBaseSize.Width, image.DestBaseSize.Height));
            }
            process.vEvaluator.RESULT = described.Item4; G2RecordOperation(new(g2HostOperations.Count + 1, line, described.Item1, described.Item2, described.Item4, described.Item3, AppContents.R0F4G2GraphicsState(id)));
        }
        private long G2SpriteCreated(Process process, int line, string name)
        {
            var described = G2DescribeSprite("SPRITECREATED", name);
            process.vEvaluator.RESULT = described.Item4; G2RecordOperation(new(g2HostOperations.Count + 1, line, described.Item1, described.Item2, described.Item4, described.Item3, AppContents.R0F4G2SpriteState(name))); return described.Item4;
        }
        private void G2DisposeSprite(Process process, int line, string name)
        {
            var described = G2DescribeSprite("SPRITEDISPOSE", name);
            if (described.Item4 != 0) AppContents.SpriteDispose(name);
            process.vEvaluator.RESULT = described.Item4; G2RecordOperation(new(g2HostOperations.Count + 1, line, described.Item1, described.Item2, described.Item4, described.Item3, AppContents.R0F4G2SpriteState(name)));
        }
        private void G2CreateAnime(Process process, int line, string name, int width, int height)
        {
            var described = G2DescribeSprite("SPRITEANIMECREATE", name, [width.ToString(CultureInfo.InvariantCulture), height.ToString(CultureInfo.InvariantCulture)]);
            if (described.Item4 != 0) AppContents.CreateSpriteAnime(name, width, height);
            process.vEvaluator.RESULT = described.Item4; G2RecordOperation(new(g2HostOperations.Count + 1, line, described.Item1, described.Item2, described.Item4, described.Item3, AppContents.R0F4G2SpriteState(name)));
        }
        private void G2AddFrame(Process process, int line, string name, long id)
        {
            var described = G2DescribeAdd(name, id);
            if (described.Item4 != 0) ((SpriteAnime)AppContents.GetSprite(name)!).AddFrame(AppContents.GetGraphics(id), new Rectangle(0, 0, 16, 16), new Point(0, 0), 125);
            process.vEvaluator.RESULT = described.Item4; G2RecordOperation(new(g2HostOperations.Count + 1, line, described.Item1, described.Item2, described.Item4, described.Item3, AppContents.R0F4G2SpriteState(name)));
        }

        private void G2RecordOperation(G2HostOperation operation)
        {
            g2HostOperations.Add(operation);
            if (g2HostOperations.Count == 1) g2H3 = new { Name = "H3", ProgramCounter = $"{G2MasterFile}:{operation.SourceLine}:after", Operation = operation, ResourceSha256 = G2ResourceHash() };
        }

        private static bool G2OperationValid(G2HostOperation x) => x.Command switch
        {
            "GCREATE" => x.Result == (x.Before.Created ? 0 : 1) && (x.Result == 0 ? x.After.Equals(x.Before) : x.After is { Created: true, Width: 16, Height: 16 }),
            "GDRAWSPRITE" => x.Result == 1 && x.After.Created && x.After.ContentSha256.Length == 64,
            "SPRITECREATED" => x.Result == (x.Before.Created ? 1 : 0) && x.After.Equals(x.Before),
            "SPRITEDISPOSE" => x.Result == (x.Before.Created ? 1 : 0) && (x.Result == 0 ? x.After.Equals(x.Before) : !x.After.Created),
            "SPRITEANIMECREATE" => x.Result == (x.Before.Created ? 0 : 1) && (x.Result == 0 ? x.After.Equals(x.Before) : x.After is { Created: true, Width: 16, Height: 16, Frames: 0 }),
            "SPRITEANIMEADDFRAME" => x.Result == 1 && x.After.Frames == x.Before.Frames + 1 && x.After.TotalDelayMs == x.Before.TotalDelayMs + 125,
            _ => false
        };

        private static bool G2HostLine(int line) => line is 41 or 43 or 46 or 47 or 48 or 49 or 51 or 53 or 56 or 58 or 61 or 62 or 63 or 64 or 65 or 66 or 67 or 68 or 69;
        private void G2Materialize(string name, int id) { if (g2Materialized.Add(name)) _ = F4D5Handle(id, name); DemandCompiledBodies = Math.Max(DemandCompiledBodies, g2Materialized.Count); }
        private static long G2Private(CalledFunction call, string name, Process process) => call.TopLabel.GetPrivateVariable(name)?.GetIntValue(process.exm, [0])
            ?? throw new InvalidOperationException("R0-F4G2 private bind: " + name);
        private string[] ReadG2Title(Process process) => Enumerable.Range(0, g2ExtraTitle.Length).Select(i => g2ExtraTitle.Read(process, i)).ToArray();
        private string G2ResourceHash() => Hash(string.Join('\n', Enumerable.Range(0, 6).Select(i => AppContents.R0F4G2GraphicsState(g2GraphicsBase + i).ToString())
            .Concat(Enumerable.Range(0, 6).Select(i => AppContents.R0F4G2SpriteState($"＠{i}").ToString()))
            .Concat(G2AnimationNames.Select(x => AppContents.R0F4G2SpriteState(x).ToString()))));
        private object G2ResourceSnapshot() => new
        {
            Graphics = Enumerable.Range(0, 6).Select(i => AppContents.R0F4G2GraphicsState(g2GraphicsBase + i)).ToArray(),
            SourceSprites = Enumerable.Range(0, 6).Select(i => AppContents.R0F4G2SpriteState($"＠{i}")).ToArray(),
            Animations = G2AnimationNames.Select(AppContents.R0F4G2SpriteState).ToArray(),
            Sha256 = G2ResourceHash()
        };
        private G2Point CaptureG2Point(Process process, string name, string pc) => new(name, pc,
            string.Join(" > ", compactFrames.Select(FrameText)), EventCursor, process.state.SystemState.ToString(), process.state.isBegun,
            process.vEvaluator.GetR0C2RngHash(), process.vEvaluator.GetR0F4D3RandomCallCount(), DifferentialDeterminism.ObservationCount,
            process.vEvaluator.RESULT, HashStrings(process.vEvaluator.RESULTS_ARRAY), HashStrings(ReadG2Title(process)), G2ResourceHash());
        private R0F1Definition G2Definition(string name, int line, string suffix)
        {
            if (!functions.TryGetValue(name, out var values) || values.Length != 1 || values[0].IsEvent || values[0].Line != line
                || !values[0].RelativePath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("R0-F4G2 definition identity: " + name);
            return values[0];
        }
        private object G2DefinitionRow(R0F1Definition x)
        {
            var source = Lines(Read(x.Function, x.File)); var body = Executable(x);
            return new { EffectiveName = x.Name, x.RelativePath, HeaderLine = x.Line, NextHeaderBoundary = x.Line + source.Length,
                Header = source[0].Trim(), Kind = "NormalFunction", BodyStatements = body, SourceSha256 = FileHash(x.File.FileIdentity), BodySha256 = BodyHash(body) };
        }
        private object InventoryG2Next()
        {
            if (!functions.TryGetValue("INITIALIZE_MESSAGE", out var values)) return new { Resolution = "KnownMissing" };
            return new { Resolution = values.Length == 1 && !values[0].IsEvent ? "Known" : "Blocked", Definitions = values.Select(G2DefinitionRow).ToArray(), Materialized = 0, Executed = 0 };
        }
        private AppContents.R0F4G2ResourceState G2TargetState(string command, string[] args) => command.StartsWith('G')
            ? AppContents.R0F4G2GraphicsState(long.Parse(args[0], CultureInfo.InvariantCulture))
            : AppContents.R0F4G2SpriteState(args[0]);
    }
}
#endif
