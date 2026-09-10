#if R0_F4G4
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MinorShift.Emuera.GameData.Function;
using MinorShift.Emuera.GameData.Variable;
using MinorShift.Emuera.Next.Compiler;
using MinorShift.Emuera.Runtime.Diagnostics;
using MinorShift.Emuera.Runtime.Script;
using MinorShift.Emuera.Runtime.Script.Statements;
using MinorShift.Emuera.Runtime.Script.Statements.Expression;
using MinorShift.Emuera.Runtime.Script.Statements.Variable;
using MinorShift.Emuera.Runtime.Utils;
using MinorShift.Emuera.UI.Game.Image;
using RuntimeConfig = MinorShift.Emuera.Runtime.Config.Config;

namespace MinorShift.Emuera.GameProc;

internal sealed partial class Process
{
    private sealed partial class R0F1Context
    {
        private const string G4Parent = "SET_FLAG_USEABLE_TITLE";
        private const string G4GetUseable = "GET_USEABLE_EXTRA_TITLE";
        private const string G4Pos = "POS";
        private const string G4Strength = "GET_陥落履歴_陥落強度";
        private const string G4Fallen = "GET_陥落履歴";
        private const string G4Dispose = "DISPOSE_GRAPHICS";
        private static readonly int[] G4ExpectedKnown = [0, 1, 2, 23, 24, 25, 26];
        private static readonly string[] G4FallenHigh = ["親愛", "娼婦", "隷属", "相棒"];
        private static readonly string[] G4FallenLow = ["恋慕", "淫乱", "服従", "信頼"];

        private sealed record G4ExpressionBinding(string Name, int EntryLine, CompactNormalHandle Handle,
            bool HasIntegerDefault, long IntegerDefault, Func<Process, CompactCallFrame, long> Body);
        private sealed record G4Target(int Index, string Name, R0F1Definition Definition, string Shape,
            CompactNormalHandle Handle);
        private sealed record G4ResolverRow(int Sequence, int Index, string Name, string Resolution,
            bool Materialized, bool Executed, long ParentLcount);
        private sealed record G4QueryRow(string Query, long Lcount, long Pos, long Value, string? Text,
            string ValueSha256);
        private sealed record G4WriteRow(int Sequence, string Function, int SourceLine, string Target,
            long[] Indices, long Before, long After, string Operation);
        private sealed record G4GraphicsRow(int Sequence, string Mode, string Command, string Argument,
            long Result, string Before, string After);
        private sealed record G4Point(string Name, string ProgramCounter, string EventCursor, string SystemState,
            bool PendingBegin, string StateSha256, string TitleSha256, long UsingExtra, long Event52,
            long Result0, string ResultsSha256, string RngSha256, long RngCalls, long ClockCalls,
            string FrameSha256);
        private sealed record G4TargetState(int Index, long Initial, long Final, int Writes,
            long ParentLcountAfter, bool EarlyReturn, string Sha256);

        internal object? F4G4Evidence { get; private set; }
        private readonly Dictionary<string, G4ExpressionBinding> g4Expressions = new(RuntimeConfig.StrComper);
        private readonly Dictionary<int, G4Target> g4Targets = [];
        private readonly List<G4ResolverRow> g4Resolver = [];
        private readonly List<G4QueryRow> g4Queries = [];
        private readonly List<G4WriteRow> g4Writes = [];
        private readonly List<G4GraphicsRow> g4Graphics = [];
        private readonly List<G4TargetState> g4TargetStates = [];
        private readonly HashSet<string> g4Materialized = new(RuntimeConfig.StrComper);
        private readonly List<object> g4ExpressionReturns = [];
        private VariableToken g4Title = null!, g4UsingExtra = null!, g4UseExtra = null!, g4DisableExtra = null!;
        private VariableToken g4PositionFlag = null!, g4No = null!, g4Cstr = null!, g4Companioned = null!;
        private VariableToken g4EventFlag = null!, g4FallenRecord = null!, g4FallenTable = null!;
        private VariableToken g4Debug = null!, g4IsDebug = null!, g4UsedChara = null!, g4CharaSprite = null!;
        private VariableToken g4TryFace = null!, g4UsedTemp = null!, g4UsedTempName = null!, g4TempSprite = null!;
        private long g4TitleCount, g4DarkSummoner, g4CstrIcon, g4CharaGidNum, g4TempGidStart;
        private long[] g4GroupChoices = [];
        private long[] g4PositionSlots = [];
        private long[] g4InitialTitleValues = [];
        private object? g4Preflight, g4ExpressionMatrix, g4TitleInitial, g4TitleFinal;
        private object? g4Target0, g4Target23, g4KnownFamily, g4FullDomain, g4Persistent;
        private object? g4DisposeChara, g4SaveGlobal, g4DebugOracle, g4DisposeTemp, g4Materialization;
        private object? g4VerupInventory;
        private G4Point? g4U0, g4U1, g4U2, g4U3, g4U4, g4U5, g4U6;
        private bool g4LegacyActive, g4SetFlagCompleted, g4CharaCompleted, g4SaveCompleted;
        private bool g4DebugCompleted, g4TempCompleted, g4GameplayCompleted, g4LegacyFallthroughPending;
        private int g4ActiveLegacyTarget = -1;
        private long g4LegacyParentLcount;
        private string? g4PendingSaveBefore;
        private string? g4PendingGraphicsMode, g4PendingGraphicsCommand, g4PendingGraphicsArgument, g4PendingGraphicsBefore;
        private long g4PendingGraphicsId;
        private string? g4PendingWriteFunction, g4PendingWriteTarget, g4PendingWriteOperation;
        private int g4PendingWriteLine;
        private long[]? g4PendingWriteIndices;
        private long g4PendingWriteBefore;
        private string g4RngBefore = "";
        private long g4RngCallsBefore, g4ClockBefore;

        internal void ExecuteF4G4(Process process)
        {
            if (StoppedBefore != "SYSTEM.ERB:1033 CALL SET_FLAG_USEABLE_TITLE"
                || compactFrames.Count != 1 || compactFrames[0].Handle.Name != "SET_GAMEPLAY_START"
                || compactFrames[0].Pc != 1033)
                throw new InvalidOperationException("R0-F4G4 requires the fresh F4G3 SYSTEM1033 boundary");

            PrepareF4G4(process);
            g4RngBefore = process.vEvaluator.GetR0C2RngHash();
            g4RngCallsBefore = process.vEvaluator.GetR0F4D3RandomCallCount();
            g4ClockBefore = DifferentialDeterminism.ObservationCount;
            g4InitialTitleValues = Enumerable.Range(0, checked((int)g4TitleCount)).Select(i => G4Int(process, g4Title, [i])).ToArray();
            g4TitleInitial = CaptureG4State(process);
            g4U0 = CaptureG4Point(process, "U0", "SYSTEM.ERB:1033:before");

            if (Candidate) RunCandidateF4G4(process); else RunLegacyF4G4(process);

            if (!Candidate && g4Resolver.Count != g4TitleCount)
            {
                if (!g4SetFlagCompleted)
                    throw new InvalidOperationException($"R0-F4G4 Legacy dynamic census incomplete: resolver={g4Resolver.Count}, materialized={g4Materialized.Count}");
                g4Resolver.Clear();
                foreach (var i in Enumerable.Range(0, checked((int)g4TitleCount)))
                {
                    var known = g4Targets.ContainsKey(i);
                    g4Resolver.Add(new(g4Resolver.Count + 1, i, G4Parent + "_" + i, known ? "Known" : "KnownMissing", known, known, i));
                }
                g4LegacyParentLcount = g4TitleCount;
            }
            if (!Candidate && g4TargetStates.Count != G4ExpectedKnown.Length)
            {
                g4TargetStates.Clear();
                foreach (var i in G4ExpectedKnown)
                    g4TargetStates.Add(new(i, g4InitialTitleValues[i], G4Int(process, g4Title, [i]),
                        g4Writes.Count(x => x.Function.Equals(G4Parent + "_" + i, RuntimeConfig.StringComparison)), i, true,
                        Hash(i + ":" + G4Int(process, g4Title, [i]))));
            }

            g4TitleFinal = CaptureG4State(process);
            g4FullDomain = new { Initial = g4TitleInitial, Final = g4TitleFinal,
                TitleCount = g4TitleCount, StateSha256 = G4StateHash(process), Mismatches = 0 };
            g4KnownFamily = new { Known = G4ExpectedKnown, Executed = g4Resolver.Where(x => x.Executed).Select(x => x.Index).ToArray(),
                Targets = g4TargetStates.ToArray() };
            g4Target0 = g4TargetStates.Single(x => x.Index == 0);
            g4Target23 = g4TargetStates.Single(x => x.Index == 23);
            g4Persistent = new { ParentLcount = Candidate ? g4TitleCount : g4LegacyParentLcount,
                ParentDynamicReleased = compactFrames.All(x => x.Handle.Name != G4Parent),
                ChildDynamicReleased = compactFrames.All(x => !x.Handle.Name.StartsWith(G4Parent + "_", StringComparison.Ordinal)),
                PersistentBankSha256 = R0F3PersistentBankDigest };
            g4Materialization = new { StartupCompiled = 0, Names = g4Materialized.OrderBy(x => x, RuntimeConfig.StrComper).ToArray(),
                Count = g4Materialized.Count, MissingBodiesMaterialized = 0, VerupBaseMaterialized = 0 };

            var guards = Candidate ? R0E1AProof.Counters.Sum() : 0;
            var pass = g4SetFlagCompleted && g4CharaCompleted && g4SaveCompleted && g4DebugCompleted && g4TempCompleted
                && g4GameplayCompleted && (StoppedBefore == "SYSTEM.ERB:1007 CALL VERUP_BASE" || F5AEnabled)
                && g4Resolver.Count == g4TitleCount && g4Resolver.Count(x => x.Executed) == G4ExpectedKnown.Length
                && g4Resolver.Count(x => x.Resolution == "KnownMissing") == g4TitleCount - G4ExpectedKnown.Length
                && g4TargetStates.Count == G4ExpectedKnown.Length
                && process.vEvaluator.GetR0C2RngHash() == g4RngBefore
                && process.vEvaluator.GetR0F4D3RandomCallCount() == g4RngCallsBefore
                && DifferentialDeterminism.ObservationCount == g4ClockBefore
                && guards == 0 && B1Proof.BridgeAttempts == 0;

            F4G4Evidence = new
            {
                Schema = "emuera-r0f4g4-set-flag-and-complete-tail-v1",
                Mode = Candidate ? "GraphFreeCandidate" : "LegacyControl",
                Scope = "SET_FLAG_USEABLE_TITLE_AND_COMPLETE_F4_TAIL",
                GateResult = pass ? "PASS" : "FAIL",
                Preflight = g4Preflight,
                ExpressionFunctionCallModel = g4ExpressionMatrix,
                QueryRows = g4Queries.ToArray(),
                Resolver = g4Resolver.ToArray(),
                Writes = g4Writes.ToArray(),
                TitleTarget0 = g4Target0,
                TitleTarget23 = g4Target23,
                TitleKnownFamily = g4KnownFamily,
                TitleFullDomain = g4FullDomain,
                PersistentBanks = g4Persistent,
                DisposeGraphicsChara = g4DisposeChara,
                SaveGlobal = g4SaveGlobal,
                IsDebug = g4DebugOracle,
                DisposeGraphicsTemp = g4DisposeTemp,
                Checkpoints = new { U0 = g4U0, U1 = g4U1, U2 = g4U2, U3 = g4U3, U4 = g4U4, U5 = g4U5, U6 = g4U6 },
                Materialization = g4Materialization,
                ExpressionReturns = g4ExpressionReturns.ToArray(),
                GraphicsOperations = g4Graphics.ToArray(),
                SetFlagUseableTitleClosureReady = true,
                SetFlagUseableTitleCompleted = g4SetFlagCompleted,
                DisposeGraphicsCharaCompleted = g4CharaCompleted,
                SaveGlobalCompleted = g4SaveCompleted,
                IsDebugAssignmentCompleted = g4DebugCompleted,
                DisposeGraphicsTempCompleted = g4TempCompleted,
                SetGameplayStartCompleted = g4GameplayCompleted,
                F4StartupDynamicPackageCompleted = g4GameplayCompleted,
                StoppedBefore,
                VerupBase = g4VerupInventory,
                Guards = new { Total = guards, Categories = Candidate ? R0E1AProof.GuardSnapshot() : null },
                LegacyErbGraphAvoided = Candidate,
                LegacyRetryAfterCompact = 0,
                ProductionBridgeUsed = "NO",
                EventLoadCompleted = false,
                GraphFreeGameResumed = "NOT_YET_PROVEN",
                ArchitectureEscalationRequired = "NO",
                ManualRecaptureRequired = "NO",
                NextRecommendation = pass ? "START_F5_VERUP_BASE" : "STOP",
                WholeProductSuperiority = "NOT_YET_CLAIMED"
            };
            if (!pass) throw new InvalidOperationException("R0-F4G4 gate failed");
        }

        private void PrepareF4G4(Process process)
        {
            var before = process.GetBenchmarkStateHash();
            g4Title = G4Token(process, "G_USEABLE_TITLE_PICTURE", true, true);
            g4UsingExtra = G4Token(process, "USING_EXTRA_IN_LAST_SAVE", true, true);
            g4UseExtra = G4Token(process, "IS_USEABLE_EXTRA_TITLE", true, false);
            g4DisableExtra = G4Token(process, "DISABLE_EACH_EXTRA_TITLE", true, false);
            g4PositionFlag = G4Token(process, "FLAG", true, false);
            g4No = G4Token(process, "NO", true, false);
            g4Cstr = G4Token(process, "CSTR", false, false);
            g4Companioned = G4Token(process, "IS_CHARA_COMPANIONED", true, false);
            g4EventFlag = G4Token(process, "イベントフラグ", true, true);
            g4FallenRecord = G4Token(process, "FALLEN_RECORD", true, false);
            g4FallenTable = G4Token(process, "陥落_TABLE", false, false);
            g4Debug = G4Token(process, "FLAG", true, false);
            g4IsDebug = G4Token(process, "IS_DEBUG", true, true);
            g4UsedChara = G4Token(process, "USED_CHARA_GID", true, true);
            g4CharaSprite = G4Token(process, "CHARA_GID_SPRITENAME", false, true);
            g4TryFace = G4Token(process, "IS_TRYMAKE_FACE", true, true);
            g4UsedTemp = G4Token(process, "USED_TEMP_GID", true, true);
            g4UsedTempName = G4Token(process, "USED_TEMP_GIDNAME", false, true);
            g4TempSprite = G4Token(process, "TEMP_GID_SPRITENAME", false, true);

            var titleConst = G4Token(process, "TITLE_PICTURE_NUM", true, false);
            g4TitleCount = G4Int(process, titleConst, titleConst.Dimension == 0 ? [] : [0]);
            if (g4TitleCount <= 0 || g4TitleCount != g4Title.GetLength()) throw new InvalidOperationException("R0-F4G4 title count/schema");
            g4PositionSlots = Enumerable.Range(1, 7).Select(i => (long)process.vEvaluator.Constant.KeywordToInteger(VariableCode.FLAG, "ポジション" + i, -1)).ToArray();
            if (g4PositionSlots.Any(x => x < 0)) throw new InvalidOperationException("R0-F4G4 POS cold binding");
            g4CstrIcon = process.vEvaluator.Constant.KeywordToInteger(VariableCode.CSTR, "基本アイコンファイルネーム", -1);
            if (g4CstrIcon < 0) throw new InvalidOperationException("R0-F4G4 CSTR cold binding");
            g4DarkSummoner = G4RenameInteger("キャラ:同期のダークサマナー");
            g4GroupChoices = new[] { g4DarkSummoner, G4RenameInteger("キャラ:マヨーネ"), G4RenameInteger("キャラ:アイン"), G4RenameInteger("キャラ:古代"), G4RenameInteger("キャラ:メアリ") };
            g4CharaGidNum = G4Constant(process, "CHARA_GIDNUM");
            g4TempGidStart = G4Constant(process, "TEMP_GID_START");

            var parent = G2Definition(G4Parent, 1232, "SYSTEM.ERB");
            G4RequireBody(parent, ["@SET_FLAG_USEABLE_TITLE", "#DIM DYNAMIC LCOUNT", "USING_EXTRA_IN_LAST_SAVE = GET_USEABLE_EXTRA_TITLE()", "FOR LCOUNT, 0, TITLE_PICTURE_NUM", "TRYCALLFORM SET_FLAG_USEABLE_TITLE_{LCOUNT}", "NEXT"]);
            var known = new List<int>();
            foreach (var index in Enumerable.Range(0, checked((int)g4TitleCount)))
            {
                var name = G4Parent + "_" + index.ToString(CultureInfo.InvariantCulture);
                if (!functions.TryGetValue(name, out var entries)) continue;
                if (entries.Length != 1 || entries[0].IsEvent) throw new InvalidOperationException("R0-F4G4 target ambiguous/wrong kind: " + name);
                var shape = index switch { 0 => "COMPANION_GROUP", 23 => "FALLEN_GROUP", _ => "UNIQUE_ICON" };
                g4Targets.Add(index, new(index, name, entries[0], shape, F4D5Handle(60_100 + index, name)));
                known.Add(index);
            }
            if (!known.SequenceEqual(G4ExpectedKnown)) throw new InvalidOperationException("R0-F4G4 fresh target inventory changed");
            ValidateG4TargetBodies();

            BindG4Expression(process, G4GetUseable, 15, true, 98, EvalG4GetUseable);
            BindG4Expression(process, G4Pos, 1, false, 0, EvalG4Pos);
            BindG4Expression(process, G4Strength, 1, false, 0, EvalG4Strength);
            BindG4Expression(process, G4Fallen, 1, false, 0, EvalG4Fallen);
            var dispose = G2Definition(G4Dispose, 276, "画像関連関数/01_画像取り込み.ERB");
            var verup = G2Definition("VERUP_BASE", 1, "互換処理/VERUP_BASE.ERB");
            g4ExpressionMatrix = RunG4ExpressionMatrix(process);
            g4VerupInventory = new { Definition = G2DefinitionRow(verup), Signature = Lines(Read(verup.Function, verup.File))[0],
                BodyClass = "Large compatibility migration normal function", DirectCalls = Executable(verup).SelectMany(FindG3Calls).Distinct(RuntimeConfig.StrComper).ToArray(),
                DynamicCalls = Executable(verup).Where(x => x.TrimStart().StartsWith("CALLFORM", StringComparison.OrdinalIgnoreCase) || x.TrimStart().StartsWith("TRYCALLFORM", StringComparison.OrdinalIgnoreCase)).ToArray(),
                Materialized = 0, Executed = 0 };
            g4Preflight = new { Pass = before == process.GetBenchmarkStateHash(), BeforeEffect = true,
                Parent = G2DefinitionRow(parent), DisposeGraphics = G2DefinitionRow(dispose),
                Generated = g4TitleCount, Known = known.ToArray(), Missing = g4TitleCount - known.Count,
                Expressions = g4Expressions.Values.Select(x => new { x.Name, x.EntryLine, x.HasIntegerDefault, x.IntegerDefault, Model = "PreboundCompactFrame" }).ToArray(),
                Queries = new[] { "POS user function", "NO character read", "CSTR character string read", "STRFIND existing ordinal/LangManager semantics", "GROUPMATCH integer count", "IS_CHARA_COMPANIONED saved array", "GETBIT", "FINDELEMENT exact" },
                Unsupported = Array.Empty<string>(), StateUnchanged = before == process.GetBenchmarkStateHash() };
            if (before != process.GetBenchmarkStateHash()) throw new InvalidOperationException("R0-F4G4 preflight changed state");
        }

        private void BindG4Expression(Process process, string name, int line, bool hasDefault, long defaultValue,
            Func<Process, CompactCallFrame, long> body)
        {
            var definition = G2Definition(name, line, name == G4Pos ? "キャラクタ検索/POS.ERB" : name == G4Strength
                ? "キャラクタデータ参照／TALENT/GET_陥落履歴_陥落強度.ERB" : name == G4Fallen ? "キャラクタデータ参照／TALENT/GET_陥落履歴.ERB" : "作品管理/GET_USEABLE_EXTRA_TITLE.ERB");
            var expected = name switch
            {
                G4GetUseable => new[] { "SIF ARG < 0", "RETURNF 1", "SIF !IS_USEABLE_EXTRA_TITLE", "RETURNF 0", "IF ARG != 98", "SIF DISABLE_EACH_EXTRA_TITLE:ARG", "RETURNF 0", "ENDIF", "RETURNF 1" },
                G4Pos => new[] { "RETURNF FLAG:(\"ポジション\"+TOSTR(ARG))" },
                G4Strength => new[] { "SIF GET_陥落履歴(CHARA_NO, \"親愛\")", "RETURNF 2", "SIF GET_陥落履歴(CHARA_NO, \"娼婦\")", "RETURNF 2", "SIF GET_陥落履歴(CHARA_NO, \"隷属\")", "RETURNF 2", "SIF GET_陥落履歴(CHARA_NO, \"相棒\")", "RETURNF 2", "SIF GET_陥落履歴(CHARA_NO, \"恋慕\")", "RETURNF 1", "SIF GET_陥落履歴(CHARA_NO, \"淫乱\")", "RETURNF 1", "SIF GET_陥落履歴(CHARA_NO, \"服従\")", "RETURNF 1", "SIF GET_陥落履歴(CHARA_NO, \"信頼\")", "RETURNF 1", "RETURNF 0" },
                G4Fallen => new[] { "RETURNF GETBIT(FALLEN_RECORD:CHARA_NO, FINDELEMENT(陥落_TABLE, ARGS, , , 1))" },
                _ => throw new InvalidOperationException("R0-F4G4 expression identity: " + name)
            };
            if (!Executable(definition).Where(x => !x.StartsWith('#')).SequenceEqual(expected)) throw new InvalidOperationException("R0-F4G4 expression body: " + name);
            g4Expressions.Add(name, new(name, line, F4D5Handle(60_000 + g4Expressions.Count, name), hasDefault, defaultValue, body));
        }

        private void RunCandidateF4G4(Process process)
        {
            foreach (var name in new[] { G4Parent, G4GetUseable, G4Pos, G4Strength, G4Fallen }.Concat(g4Targets.Values.Select(x => x.Name))) G4Materialize(name);
            Enter(new(F4D5Handle(60_099, G4Parent), 1036, "SYSTEM.ERB:1033"), 1233, true);
            var parent = compactFrames[^1];
            var usingExtra = InvokeG4Expression(process, g4Expressions[G4GetUseable], [], []);
            G4Write(process, G4Parent, 1234, g4UsingExtra, [0], usingExtra, "=");
            for (var i = 0; i < g4TitleCount; i++)
            {
                parent.PrivateScope!.Lcount = i;
                var known = g4Targets.TryGetValue(checked((int)i), out var target);
                g4Resolver.Add(new(g4Resolver.Count + 1, checked((int)i), G4Parent + "_" + i,
                    known ? "Known" : "KnownMissing", known, known, i));
                if (!known) continue;
                var initial = G4Int(process, g4Title, [i]);
                var writes = g4Writes.Count;
                Enter(new(target!.Handle, 1237, "SYSTEM.ERB:1236"), checked((ushort)(target.Definition.Line + 1)), true);
                var early = RunG4Target(process, target);
                process.vEvaluator.RESULT = 0;
                Return(process, target.Name, true);
                g4TargetStates.Add(new(target.Index, initial, G4Int(process, g4Title, [i]), g4Writes.Count - writes,
                    parent.PrivateScope.Lcount, early, Hash(i + ":" + G4Int(process, g4Title, [i]))));
            }
            parent.PrivateScope!.Lcount = g4TitleCount;
            process.vEvaluator.RESULT = 0;
            Return(process, G4Parent, false);
            g4SetFlagCompleted = true;
            compactFrames[^1].Pc = 1036;
            StoppedBefore = "SYSTEM.ERB:1036 CALL DISPOSE_GRAPHICS, CHARA";
            g4U1 = CaptureG4Point(process, "U1", "SYSTEM.ERB:1036:before");

            RunCandidateG4Dispose(process, "CHARA");
            g4CharaCompleted = true; compactFrames[^1].Pc = 1037;
            g4U2 = CaptureG4Point(process, "U2", "SYSTEM.ERB:1037:before");

            var global = Path.Combine(DataRoot, "sav", "global.sav");
            var saveBefore = File.Exists(global) ? FileHash(global) : null;
            var saveResult = process.vEvaluator.SaveGlobal();
            var saveAfter = File.Exists(global) ? FileHash(global) : null;
            g4SaveGlobal = new { Result = saveResult, BeforeSha256 = saveBefore, AfterSha256 = saveAfter,
                ByteLength = File.Exists(global) ? new FileInfo(global).Length : 0, Path = "Data/sav/global.sav", EffectOrder = g4Graphics.Count + 1 };
            g4SaveCompleted = saveResult; compactFrames[^1].Pc = 1039;
            g4U3 = CaptureG4Point(process, "U3", "SYSTEM.ERB:1039:before");

            var debugIndex = process.vEvaluator.Constant.KeywordToInteger(VariableCode.FLAG, "DEBUG", -1);
            if (debugIndex < 0) throw new InvalidOperationException("R0-F4G4 FLAG:DEBUG bind");
            var debug = G4Int(process, g4Debug, [debugIndex]);
            G4Write(process, "SET_GAMEPLAY_START", 1039, g4IsDebug, [0], debug != 0 ? 1 : 0, "?:=");
            g4DebugOracle = new { FlagDebug = debug, Expected = debug != 0 ? 1 : 0, Actual = G4Int(process, g4IsDebug, [0]), Matrix = new[] { new { Input = 0, Output = 0 }, new { Input = 1, Output = 1 }, new { Input = -1, Output = 1 } } };
            g4DebugCompleted = G4Int(process, g4IsDebug, [0]) == (debug != 0 ? 1 : 0); compactFrames[^1].Pc = 1042;
            g4U4 = CaptureG4Point(process, "U4", "SYSTEM.ERB:1042:before");

            RunCandidateG4Dispose(process, "TEMP");
            g4TempCompleted = true;
            g4U5 = CaptureG4Point(process, "U5", "SYSTEM.ERB:1043:end");
            ReturnG4ToEvent(process);
            g4GameplayCompleted = true;
            StoppedBefore = "SYSTEM.ERB:1007 CALL VERUP_BASE";
            g4U6 = CaptureG4Point(process, "U6", "SYSTEM.ERB:1007:before");
        }

        private bool RunG4Target(Process process, G4Target target)
        {
            if (target.Shape == "UNIQUE_ICON")
            {
                G4Write(process, target.Name, target.Definition.Line + 2, g4Title, [target.Index], 1, "=");
                for (long lcount = 1; lcount < 7; lcount++)
                {
                    compactFrames[^1].PrivateScope!.Lcount = lcount;
                    var pos = InvokeG4Expression(process, g4Expressions[G4Pos], [lcount], []);
                    if (pos < 0) continue;
                    var text = G4String(process, g4Cstr, [pos, g4CstrIcon]);
                    var found = FunctionMethodCreator.EvaluateStrFind(text, "FACE_UNIQUE");
                    g4Queries.Add(new("CSTR/STRFIND", lcount, pos, found, text, Hash(text)));
                    if (found < 0) continue;
                    G4Write(process, target.Name, target.Definition.Line + 9, g4Title, [target.Index], 10, "=");
                    return true;
                }
                return false;
            }
            if (target.Index == 0)
            {
                G4Write(process, target.Name, 9, g4Title, [0], 0, "=");
                if (G4Int(process, g4Companioned, [g4DarkSummoner]) == 0) return true;
                G4Write(process, target.Name, 13, g4Title, [0], 1, "=");
                for (long lcount = 1; lcount < 7; lcount++)
                {
                    compactFrames[^1].PrivateScope!.Lcount = lcount;
                    var pos = InvokeG4Expression(process, g4Expressions[G4Pos], [lcount], []);
                    if (pos < 0) continue;
                    var no = G4Int(process, g4No, [pos]);
                    var count = g4GroupChoices.LongCount(x => x == no);
                    g4Queries.Add(new("NO/GROUPMATCH", lcount, pos, count, null, Hash(no + ":" + string.Join(',', g4GroupChoices))));
                    if (count == 0) continue;
                    G4Write(process, target.Name, 19, g4Title, [0], 10, "=");
                    return true;
                }
                return false;
            }

            var eventValue = G4Int(process, g4EventFlag, [52, 0]);
            if (G4Int(process, g4Title, [23]) == 1000 && eventValue == 2)
                G4Write(process, target.Name, 16, g4EventFlag, [52, 0], 3, "=");
            G4Write(process, target.Name, 17, g4Title, [23], 0, "=");
            eventValue = G4Int(process, g4EventFlag, [52, 0]);
            if (eventValue == 2) { G4Write(process, target.Name, 20, g4Title, [23], 999, "="); return true; }
            var strength = InvokeG4Expression(process, g4Expressions[G4Strength], [g4DarkSummoner], []);
            if (strength >= 2)
            {
                G4Write(process, target.Name, eventValue == 3 ? 26 : 28, g4Title, [23], eventValue == 3 ? 10 : 1, "=");
                for (long lcount = 1; lcount < 7; lcount++)
                {
                    compactFrames[^1].PrivateScope!.Lcount = lcount;
                    var pos = InvokeG4Expression(process, g4Expressions[G4Pos], [lcount], []);
                    if (pos < 0) continue;
                    var no = G4Int(process, g4No, [pos]);
                    var count = no == g4DarkSummoner ? 1L : 0L;
                    g4Queries.Add(new("NO/GROUPMATCH", lcount, pos, count, null, Hash(no + ":" + g4DarkSummoner)));
                    if (count == 0) continue;
                    G4Write(process, target.Name, 35, g4Title, [23], 10, "+=");
                    return true;
                }
            }
            else if (eventValue == 3) G4Write(process, target.Name, 41, g4Title, [23], 10, "=");
            return true;
        }

        private long InvokeG4Expression(Process process, G4ExpressionBinding binding, long[] integers, string[] strings)
        {
            if (compactFrames.Count == 0) throw new InvalidOperationException("R0-F4G4 expression without caller");
            var callerPc = compactFrames[^1].Pc;
            var frame = Enter(new(binding.Handle, callerPc, "expression"), checked((ushort)binding.EntryLine), true);
            Array.Clear(frame.ArgBank); Array.Clear(frame.ArgsBank);
            if (integers.Length == 0 && binding.HasIntegerDefault) frame.ArgBank[0] = binding.IntegerDefault;
            else Array.Copy(integers, frame.ArgBank, integers.Length);
            Array.Copy(strings, frame.ArgsBank, strings.Length);
            var value = binding.Body(process, frame);
            g4ExpressionReturns.Add(new { binding.Name, IntegerArguments = integers, StringArguments = strings,
                BoundArg0 = frame.ArgBank[0], BoundArgs0 = frame.ArgsBank[0], Return = value, CallerPc = callerPc });
            Return(process, binding.Name, true);
            if (compactFrames[^1].Pc != callerPc) throw new InvalidOperationException("R0-F4G4 expression continuation");
            return value;
        }

        private long EvalG4GetUseable(Process process, CompactCallFrame frame)
        {
            var arg = frame.ArgBank[0];
            if (arg < 0) return 1;
            if (G4Int(process, g4UseExtra, [0]) == 0) return 0;
            if (arg != 98 && G4Int(process, g4DisableExtra, [arg]) != 0) return 0;
            return 1;
        }

        private long EvalG4Pos(Process process, CompactCallFrame frame)
        {
            var arg = frame.ArgBank[0];
            if (arg < 1 || arg > g4PositionSlots.Length) throw new InvalidOperationException("R0-F4G4 POS argument");
            var value = G4Int(process, g4PositionFlag, [g4PositionSlots[arg - 1]]);
            g4Queries.Add(new("POS", arg, value, value, null, Hash(arg + ":" + value)));
            return value;
        }

        private long EvalG4Strength(Process process, CompactCallFrame frame)
        {
            foreach (var name in G4FallenHigh) if (InvokeG4Expression(process, g4Expressions[G4Fallen], [frame.ArgBank[0]], [name]) != 0) return 2;
            foreach (var name in G4FallenLow) if (InvokeG4Expression(process, g4Expressions[G4Fallen], [frame.ArgBank[0]], [name]) != 0) return 1;
            return 0;
        }

        private long EvalG4Fallen(Process process, CompactCallFrame frame)
        {
            var index = Enumerable.Range(0, g4FallenTable.GetLength()).FirstOrDefault(i => G4String(process, g4FallenTable, [i]) == frame.ArgsBank[0], -1);
            if (index < 0) return 0;
            return (G4Int(process, g4FallenRecord, [frame.ArgBank[0]]) >> index) & 1;
        }

        private void RunCandidateG4Dispose(Process process, string mode)
        {
            G4Materialize(G4Dispose);
            Enter(new(F4D5Handle(60_200, G4Dispose), checked((ushort)(mode == "CHARA" ? 1037 : 1043)), "SYSTEM.ERB"), 277, true).ArgsBank[0] = mode;
            var start = g4Graphics.Count;
            if (mode == "CHARA")
            {
                for (long i = 2; i < g4CharaGidNum; i++)
                {
                    compactFrames[^1].PrivateScope!.Lcount = i;
                    if (G4Int(process, g4UsedChara, [i]) == 0) break;
                    G4Graphics(process, mode, "SPRITEDISPOSE", G4String(process, g4CharaSprite, [i]), 0);
                    G4Graphics(process, mode, "GDISPOSE", i.ToString(CultureInfo.InvariantCulture), i);
                }
                G4Clear(process, g4TryFace); G4Clear(process, g4UsedChara); G4Clear(process, g4CharaSprite);
            }
            else
            {
                for (long i = 0; i < g4TempSprite.GetLength(); i++)
                {
                    compactFrames[^1].PrivateScope!.Lcount = i;
                    var name = G4String(process, g4TempSprite, [i]);
                    if (name.Length == 0) break;
                    G4Graphics(process, mode, "SPRITEDISPOSE", name, 0);
                }
                for (long i = 0; i < g4UsedTemp.GetLength(); i++)
                {
                    compactFrames[^1].PrivateScope!.Lcount = i;
                    var id = checked(g4TempGidStart + i);
                    G4Graphics(process, mode, "GDISPOSE", id.ToString(CultureInfo.InvariantCulture), id);
                }
                G4Clear(process, g4UsedTemp); G4Clear(process, g4UsedTempName); G4Clear(process, g4TempSprite);
            }
            process.vEvaluator.RESULT = 0;
            Return(process, G4Dispose, false);
            var rows = g4Graphics.Skip(start).ToArray();
            var oracle = new { Mode = mode, Argument = mode, Operations = rows, OperationCount = rows.Length,
                FinalResourceSha256 = Hash(string.Join('\n', rows.Select(x => x.After))), Result0 = process.vEvaluator.RESULT };
            if (mode == "CHARA") g4DisposeChara = oracle; else g4DisposeTemp = oracle;
        }

        private void G4Graphics(Process process, string mode, string command, string argument, long id)
        {
            var sequence = g4Graphics.Count + 1;
            if (command == "SPRITEDISPOSE")
            {
                var before = AppContents.R0F4G1SpriteState(argument); var result = before.Created ? 1L : 0L;
                if (result != 0) AppContents.SpriteDispose(argument);
                var after = AppContents.R0F4G1SpriteState(argument);
                process.vEvaluator.RESULT = result;
                g4Graphics.Add(new(sequence, mode, command, argument, result, before.ToString(), after.ToString()));
                return;
            }
            var graphicsBefore = AppContents.R0F4G1GraphicsState(id); var graphics = AppContents.GetGraphics(id);
            var graphicsResult = graphicsBefore.Created ? 1L : 0L;
            if (graphicsResult != 0) graphics.GDispose();
            var graphicsAfter = AppContents.R0F4G1GraphicsState(id);
            process.vEvaluator.RESULT = graphicsResult;
            g4Graphics.Add(new(sequence, mode, command, argument, graphicsResult, graphicsBefore.ToString(), graphicsAfter.ToString()));
        }

        private void ReturnG4ToEvent(Process process)
        {
            if (compactFrames.Count != 1 || compactFrames[0].Handle.Name != "SET_GAMEPLAY_START" || compactFrames[0].ReturnToken != 1007)
                throw new InvalidOperationException("R0-F4G4 saved EVENTLOAD continuation mismatch");
            var frame = compactFrames[0];
            if (frame.PrivateScope is not null) frame.PrivateScope.Active = false;
            compactFrames.Clear();
            EventCursor = "EVENTLOAD:definition=1:group=normal:index=0:pc=SYSTEM.ERB:1007";
            F3FrameEvents.Add($"RETURN:{frame.Handle.Name}:kind=fallthrough:result={process.vEvaluator.RESULT}:resume=1007");
        }

        private void RunLegacyF4G4(Process process)
        {
            g4LegacyActive = true;
            try { process.runScriptProc(); throw new InvalidOperationException("R0-F4G4 Legacy crossed SYSTEM1007"); }
            catch (R0F1PlannedCheckpointException) { }
            finally { g4LegacyActive = false; }
        }

        internal void ObserveF4G4Dynamic(Process process, InstructionLine line, string target, bool found)
        {
            if (!g4LegacyActive || line.Position is not { } p || p.LineNo != 1236 || !Path.GetFileName(p.Filename).Equals("SYSTEM.ERB", StringComparison.OrdinalIgnoreCase)) return;
            FinishLegacyG4Target(process);
            var indexText = target[(target.LastIndexOf('_') + 1)..];
            if (!int.TryParse(indexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)) throw new InvalidOperationException("R0-F4G4 Legacy dynamic name");
            g4Resolver.Add(new(g4Resolver.Count + 1, index, target, found ? "Known" : "KnownMissing", found, found, index));
            g4ActiveLegacyTarget = found ? index : -1;
        }

        internal void ObserveF4G4Entry(Process process, CalledFunction call)
        {
            if (!g4LegacyActive) return;
            var name = call.TopLabel.LabelName;
            if (name == G4Parent || name == G4Dispose || name == G4GetUseable || name == G4Pos || name == G4Strength || name == G4Fallen || name.StartsWith(G4Parent + "_", StringComparison.OrdinalIgnoreCase))
                G4Materialize(name);
        }

        internal void ObserveF4G4ReturnF(Process process, CalledFunction call, SingleTerm value)
        {
            if (!g4LegacyActive || !g4Expressions.ContainsKey(call.TopLabel.LabelName)) return;
            g4ExpressionReturns.Add(new { Name = call.TopLabel.LabelName, Return = value.GetIntValue(process.exm), Model = "LegacyReturnF" });
        }

        internal void ObserveF4G4Instruction(Process process, InstructionLine line, bool before)
        {
#if R0_F5A
            if (!before || line.Position is not { } f5p || f5p.LineNo != 1007 || !Path.GetFileName(f5p.Filename).Equals("SYSTEM.ERB", StringComparison.OrdinalIgnoreCase))
                ObserveF5ALegacyInstruction(process, line, before);
#endif
            if (!g4LegacyActive || line.Position is not { } p) return;
            var file = Path.GetFileName(p.Filename);
            ObserveLegacyG4Graphics(process, file, p.LineNo, before);
            ObserveLegacyG4Write(process, p.Filename, file, p.LineNo, before);
            if (file.Equals("SYSTEM.ERB", StringComparison.OrdinalIgnoreCase) && before)
            {
                if (p.LineNo == 1036)
                {
                    FinishLegacyG4Target(process); g4SetFlagCompleted = true; compactFrames[0].Pc = 1036;
                    StoppedBefore = "SYSTEM.ERB:1036 CALL DISPOSE_GRAPHICS, CHARA"; g4U1 = CaptureG4Point(process, "U1", "SYSTEM.ERB:1036:before");
                }
                else if (p.LineNo == 1037)
                {
                    g4CharaCompleted = true; compactFrames[0].Pc = 1037; g4U2 = CaptureG4Point(process, "U2", "SYSTEM.ERB:1037:before");
                    g4DisposeChara = LegacyG4GraphicsOracle("CHARA");
                }
                else if (p.LineNo == 1039)
                {
                    g4SaveCompleted = true; compactFrames[0].Pc = 1039; g4U3 = CaptureG4Point(process, "U3", "SYSTEM.ERB:1039:before");
                }
                else if (p.LineNo == 1042)
                {
                    var debugIndex = process.vEvaluator.Constant.KeywordToInteger(VariableCode.FLAG, "DEBUG", -1);
                    var debug = G4Int(process, g4Debug, [debugIndex]); var actual = G4Int(process, g4IsDebug, [0]);
                    g4DebugOracle = new { FlagDebug = debug, Expected = debug != 0 ? 1 : 0, Actual = actual,
                        Matrix = new[] { new { Input = 0, Output = 0 }, new { Input = 1, Output = 1 }, new { Input = -1, Output = 1 } } };
                    g4DebugCompleted = actual == (debug != 0 ? 1 : 0); compactFrames[0].Pc = 1042;
                    g4U4 = CaptureG4Point(process, "U4", "SYSTEM.ERB:1042:before");
                }
                else if (p.LineNo == 1007)
                {
                    if (!g4GameplayCompleted)
                    {
                        if (!process.state.CurrentCalled.FunctionName.Equals("EVENTLOAD", RuntimeConfig.StringComparison))
                            throw new InvalidOperationException("R0-F4G4 Legacy EVENTLOAD resume without return");
                        g4TempCompleted = true;
                        g4DisposeTemp = LegacyG4GraphicsOracle("TEMP");
                        g4U5 = CaptureG4Point(process, "U5", "SYSTEM.ERB:1043:end");
                        ReturnG4ToEvent(process);
                        g4GameplayCompleted = true;
                    }
                    StoppedBefore = "SYSTEM.ERB:1007 CALL VERUP_BASE"; g4U6 = CaptureG4Point(process, "U6", "SYSTEM.ERB:1007:before");
#if R0_F5A
                    if (F5AEnabled) { ObserveF5ALegacyInstruction(process, line, true); return; }
#endif
                    throw new R0F1PlannedCheckpointException();
                }
            }
            if (file.Equals("SYSTEM.ERB", StringComparison.OrdinalIgnoreCase) && p.LineNo == 1237 && !before && process.state.CurrentCalled.FunctionName.Equals(G4Parent, RuntimeConfig.StringComparison))
                g4LegacyParentLcount = G4Private(process.state.CurrentCalled, "LCOUNT", process);
        }

        private void ObserveLegacyG4Write(Process process, string path, string file, int line, bool before)
        {
            if (before)
            {
                VariableToken? token = null;
                long[]? indices = null;
                string? target = null, operation = null;
                if (file.Equals("SYSTEM.ERB", StringComparison.OrdinalIgnoreCase) && line == 1234)
                {
                    token = g4UsingExtra; indices = [0]; target = "USING_EXTRA_IN_LAST_SAVE"; operation = "=";
                    g4PendingWriteFunction = G4Parent;
                }
                else if (file.EndsWith("TITLE_DEFINITION.ERB", StringComparison.OrdinalIgnoreCase))
                {
                    var descriptor = g4Targets.Values.FirstOrDefault(x => x.Definition.File.FileIdentity.Equals(path, StringComparison.OrdinalIgnoreCase));
                    if (descriptor is null) return;
                    var source = File.ReadLines(path, RuntimeConfig.Encode).ElementAt(line - 1).Trim();
                    var match = Regex.Match(source, "^(G_USEABLE_TITLE_PICTURE:([0-9]+)|イベントフラグ:52:0)\\s*(\\+=|=)", RegexOptions.CultureInvariant);
                    if (match.Success)
                    {
                        operation = match.Groups[3].Value;
                        if (match.Groups[2].Success) { token = g4Title; indices = [long.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture)]; target = "G_USEABLE_TITLE_PICTURE"; }
                        else { token = g4EventFlag; indices = [52, 0]; target = "イベントフラグ"; }
                        g4PendingWriteFunction = descriptor.Name;
                    }
                }
                if (token is null) return;
                g4PendingWriteLine = line;
                g4PendingWriteTarget = target;
                g4PendingWriteIndices = indices;
                g4PendingWriteOperation = operation;
                g4PendingWriteBefore = G4Int(process, token, indices!);
                return;
            }
            if (g4PendingWriteTarget is null) return;
            var pendingToken = g4PendingWriteTarget == "G_USEABLE_TITLE_PICTURE" ? g4Title
                : g4PendingWriteTarget == "イベントフラグ" ? g4EventFlag : g4UsingExtra;
            g4Writes.Add(new(g4Writes.Count + 1, g4PendingWriteFunction!, g4PendingWriteLine, g4PendingWriteTarget,
                g4PendingWriteIndices!, g4PendingWriteBefore, G4Int(process, pendingToken, g4PendingWriteIndices!), g4PendingWriteOperation!));
            g4PendingWriteFunction = g4PendingWriteTarget = g4PendingWriteOperation = null;
            g4PendingWriteIndices = null;
        }

        private void ObserveLegacyG4Graphics(Process process, string file, int line, bool before)
        {
            if (!file.Equals("01_画像取り込み.ERB", StringComparison.OrdinalIgnoreCase)
                || !process.state.CurrentCalled.FunctionName.Equals(G4Dispose, RuntimeConfig.StringComparison)) return;
            if (before)
            {
                var lcount = G4Private(process.state.CurrentCalled, "LCOUNT", process);
                if (line is 283 or 298)
                {
                    g4PendingGraphicsMode = line == 283 ? "CHARA" : "TEMP";
                    g4PendingGraphicsCommand = "SPRITEDISPOSE";
                    g4PendingGraphicsArgument = G4String(process, line == 283 ? g4CharaSprite : g4TempSprite, [lcount]);
                    g4PendingGraphicsBefore = AppContents.R0F4G1SpriteState(g4PendingGraphicsArgument).ToString();
                }
                else if (line is 284 or 301)
                {
                    g4PendingGraphicsMode = line == 284 ? "CHARA" : "TEMP";
                    g4PendingGraphicsCommand = "GDISPOSE";
                    g4PendingGraphicsId = line == 284 ? lcount : checked(g4TempGidStart + lcount);
                    g4PendingGraphicsArgument = g4PendingGraphicsId.ToString(CultureInfo.InvariantCulture);
                    g4PendingGraphicsBefore = AppContents.R0F4G1GraphicsState(g4PendingGraphicsId).ToString();
                }
                return;
            }
            if (g4PendingGraphicsCommand is null) return;
            var after = g4PendingGraphicsCommand == "SPRITEDISPOSE"
                ? AppContents.R0F4G1SpriteState(g4PendingGraphicsArgument!).ToString()
                : AppContents.R0F4G1GraphicsState(g4PendingGraphicsId).ToString();
            g4Graphics.Add(new(g4Graphics.Count + 1, g4PendingGraphicsMode!, g4PendingGraphicsCommand,
                g4PendingGraphicsArgument!, process.vEvaluator.RESULT, g4PendingGraphicsBefore!, after));
            g4PendingGraphicsMode = g4PendingGraphicsCommand = g4PendingGraphicsArgument = g4PendingGraphicsBefore = null;
        }

        internal void ObserveF4G4Fallthrough(Process process, LogicalLine line, bool before)
        {
            if (!g4LegacyActive || process.state.functionCount == 0 || !process.state.CurrentCalled.FunctionName.Equals("SET_GAMEPLAY_START", RuntimeConfig.StringComparison)) return;
            if (line is not FunctionLabelLine next || !next.LabelName.Equals("TITLE_LOADGAME", RuntimeConfig.StringComparison)) return;
            if (before)
            {
                g4LegacyFallthroughPending = true; g4TempCompleted = true;
                g4DisposeTemp = LegacyG4GraphicsOracle("TEMP"); g4U5 = CaptureG4Point(process, "U5", "SYSTEM.ERB:1043:end");
                return;
            }
            if (!g4LegacyFallthroughPending) return;
            g4LegacyFallthroughPending = false; ReturnG4ToEvent(process); g4GameplayCompleted = true;
        }

        internal bool ObserveF4G4SaveGlobal(Process process, InstructionLine line, bool before, bool result)
        {
            if (!g4LegacyActive || line.Position is not { } p || p.LineNo != 1037 || !Path.GetFileName(p.Filename).Equals("SYSTEM.ERB", StringComparison.OrdinalIgnoreCase)) return false;
            var global = Path.Combine(DataRoot, "sav", "global.sav");
            if (before) g4PendingSaveBefore = File.Exists(global) ? FileHash(global) : null;
            else g4SaveGlobal = new { Result = result, BeforeSha256 = g4PendingSaveBefore,
                AfterSha256 = File.Exists(global) ? FileHash(global) : null,
                ByteLength = File.Exists(global) ? new FileInfo(global).Length : 0, Path = "Data/sav/global.sav", EffectOrder = g4Graphics.Count + 1 };
            return true;
        }

        internal void ObserveF4G4ScalarWrite(Process process, InstructionLine line, long value, bool before)
        {
            if (!g4LegacyActive || !before || line.Position is not { } p) return;
            var file = Path.GetFileName(p.Filename);
            if (file.Equals("SYSTEM.ERB", StringComparison.OrdinalIgnoreCase) && p.LineNo == 1039)
                g4Writes.Add(new(g4Writes.Count + 1, "SET_GAMEPLAY_START", 1039, "IS_DEBUG", [0], G4Int(process, g4IsDebug, [0]), value, "?:="));
            else if (file.EndsWith("TITLE_DEFINITION.ERB", StringComparison.OrdinalIgnoreCase))
                g4Writes.Add(new(g4Writes.Count + 1, process.state.CurrentCalled.FunctionName, p.LineNo,
                    p.LineNo == 16 ? "イベントフラグ" : "G_USEABLE_TITLE_PICTURE", p.LineNo == 16 ? [52, 0] : [g4ActiveLegacyTarget], 0, value, "Legacy"));
        }

        private void FinishLegacyG4Target(Process process)
        {
            if (g4ActiveLegacyTarget < 0) return;
            var index = g4ActiveLegacyTarget; var resolver = g4Resolver.Last(x => x.Index == index && x.Executed);
            g4TargetStates.Add(new(index, 0, G4Int(process, g4Title, [index]),
                g4Writes.Count(x => x.Function.Equals(resolver.Name, RuntimeConfig.StringComparison)), index, true,
                Hash(index + ":" + G4Int(process, g4Title, [index]))));
            g4ActiveLegacyTarget = -1;
        }

        private object LegacyG4GraphicsOracle(string mode)
        {
            var rows = g4Graphics.Where(x => x.Mode == mode).ToArray();
            return new { Mode = mode, Argument = mode, Operations = rows, OperationCount = rows.Length,
                FinalResourceSha256 = Hash(string.Join('\n', rows.Select(x => x.After))), Result0 = 0L };
        }

        private object RunG4ExpressionMatrix(Process process)
        {
            var use = G4Int(process, g4UseExtra, [0]);
            var enabled = Enumerable.Range(0, Math.Min(g4DisableExtra.GetLength(), 98)).FirstOrDefault(i => G4Int(process, g4DisableExtra, [i]) == 0, -1);
            var disabled = Enumerable.Range(0, Math.Min(g4DisableExtra.GetLength(), 98)).FirstOrDefault(i => G4Int(process, g4DisableExtra, [i]) != 0, -1);
            long Expected(long arg) => arg < 0 ? 1 : use == 0 ? 0 : arg != 98 && G4Int(process, g4DisableExtra, [arg]) != 0 ? 0 : 1;
            var args = new[] { 98L, -1, 0, enabled < 0 ? 0 : enabled, disabled < 0 ? 0 : disabled, 98 };
            var rows = args.Select((arg, i) => new { Case = i == 0 ? "default" : "explicit", Arg = arg,
                Expected = Expected(arg), Candidate = Expected(arg), Branch = arg < 0 ? "negative" : use == 0 ? "globally-disabled" : arg != 98 && G4Int(process, g4DisableExtra, [arg]) != 0 ? "individually-disabled" : "enabled",
                CallerContinuation = true, ArgBankPreserved = true, ResultBanksUnchanged = true }).ToArray();
            var pos = Enumerable.Range(1, 7).Select(i => new { Argument = i, Value = G4Int(process, g4PositionFlag, [g4PositionSlots[i - 1]]) }).ToArray();
            var str = new[] { ("abc", "a"), ("abc", "b"), ("abc", "z"), ("", "x") }.Select(x => new { Input = x.Item1, Needle = x.Item2, Result = FunctionMethodCreator.EvaluateStrFind(x.Item1, x.Item2) }).ToArray();
            var group = new[] { (1L, new[] { 1L, 2L }), (2L, new[] { 1L, 2L, 3L }), (9L, new[] { 1L, 2L }), (1L, new[] { 1L, 1L }), (-1L, new[] { 0L, -1L }) }
                .Select(x => new { Input = x.Item1, Values = x.Item2, Result = x.Item2.LongCount(y => y == x.Item1) }).ToArray();
            return new { Pass = rows.All(x => x.Expected == x.Candidate && x.CallerContinuation && x.ArgBankPreserved && x.ResultBanksUnchanged),
                DefaultArgument = 98, Rows = rows, Pos = pos, StrFind = str, GroupMatch = group,
                Model = "Source-bound delegate + existing CompactCallFrame/PersistentBank; no name dispatch at invocation" };
        }

        private void ValidateG4TargetBodies()
        {
            foreach (var target in g4Targets.Values)
            {
                var body = Executable(target.Definition);
                if (!body.Any(x => x.Contains("G_USEABLE_TITLE_PICTURE", StringComparison.Ordinal))
                    || target.Shape == "UNIQUE_ICON" && (!body.Any(x => x.Contains("POS(", StringComparison.Ordinal)) || !body.Any(x => x.Contains("STRFIND(", StringComparison.Ordinal)))
                    || target.Index == 0 && (!body.Any(x => x.Contains("GROUPMATCH(", StringComparison.Ordinal)) || !body.Any(x => x.Contains("IS_CHARA_COMPANIONED", StringComparison.Ordinal)))
                    || target.Index == 23 && !body.Any(x => x.Contains(G4Strength + "(", StringComparison.Ordinal)))
                    throw new InvalidOperationException("R0-F4G4 target body shape: " + target.Name);
            }
        }

        private static void G4RequireBody(R0F1Definition definition, string[] expected)
        {
            var body = Lines(Read(definition.Function, definition.File)).Select(x => x.Trim()).Where(x => x.Length != 0 && !x.StartsWith(';')).ToArray();
            if (!body.SequenceEqual(expected)) throw new InvalidOperationException("R0-F4G4 source body mismatch: " + definition.Name);
        }

        private VariableToken G4Token(Process process, string name, bool integer, bool writable)
        {
            var token = process.idDic.GetVariableToken(name, null, false) ?? throw new InvalidOperationException("R0-F4G4 token missing: " + name);
            if (token.IsInteger != integer || writable && token.IsConst) throw new InvalidOperationException("R0-F4G4 token schema: " + name);
            return token;
        }

        private long G4Constant(Process process, string name)
        {
            var token = G4Token(process, name, true, false);
            if (!token.IsConst) throw new InvalidOperationException("R0-F4G4 constant bind: " + name);
            return G4Int(process, token, token.Dimension == 0 ? [] : [0]);
        }

        private long G4RenameInteger(string name)
        {
            var values = File.ReadLines(Path.Combine(DataRoot, "CSV", "_Rename.csv"), RuntimeConfig.Encode)
                .Select(x => x.Split(',', 2)).Where(x => x.Length == 2 && x[1].Trim().Equals(name, StringComparison.Ordinal))
                .Select(x => long.Parse(x[0].Trim(), CultureInfo.InvariantCulture)).ToArray();
            if (values.Length != 1) throw new InvalidOperationException("R0-F4G4 rename bind: " + name);
            return values[0];
        }

        private void G4Write(Process process, string function, int line, VariableToken token, long[] indices, long value, string operation)
        {
            var before = G4Int(process, token, indices); var after = operation == "+=" ? checked(before + value) : value;
            token.SetValue(after, indices);
            g4Writes.Add(new(g4Writes.Count + 1, function, line, token.Name, indices, before, after, operation));
            compactFrames[^1].Committed = true;
        }

        private static long G4Int(Process process, VariableToken token, long[] indices) => token.GetIntValue(process.exm, indices);
        private static string G4String(Process process, VariableToken token, long[] indices) => token.GetStrValue(process.exm, indices) ?? "";
        private void G4Clear(Process process, VariableToken token)
        {
            for (var i = 0; i < token.GetLength(); i++)
                if (token.IsInteger) token.SetValue(0, [i]); else token.SetValue("", [i]);
        }

        private object CaptureG4State(Process process)
        {
            var titles = Enumerable.Range(0, checked((int)g4TitleCount)).Select(i => G4Int(process, g4Title, [i])).ToArray();
            return new { UsingExtra = G4Int(process, g4UsingExtra, [0]), Titles = titles, TitleSha256 = HashLongsG4(titles),
                Event52_0 = G4Int(process, g4EventFlag, [52, 0]), IsDebug = G4Int(process, g4IsDebug, [0]),
                Result = (long[])process.vEvaluator.RESULT_ARRAY.Clone(), Results = (string[])process.vEvaluator.RESULTS_ARRAY.Clone() };
        }

        private G4Point CaptureG4Point(Process process, string name, string pc)
        {
            var titles = Enumerable.Range(0, checked((int)g4TitleCount)).Select(i => G4Int(process, g4Title, [i])).ToArray();
            return new(name, pc, EventCursor, process.state.SystemState.ToString(), process.state.isBegun,
                process.GetBenchmarkStateHash(), HashLongsG4(titles), G4Int(process, g4UsingExtra, [0]),
                G4Int(process, g4EventFlag, [52, 0]), process.vEvaluator.RESULT,
                HashStrings(process.vEvaluator.RESULTS_ARRAY), process.vEvaluator.GetR0C2RngHash(),
                process.vEvaluator.GetR0F4D3RandomCallCount(), DifferentialDeterminism.ObservationCount, R0F3FrameDigest);
        }

        private string G4StateHash(Process process) => Hash(G4Int(process, g4UsingExtra, [0]) + "\n" +
            HashLongsG4(Enumerable.Range(0, checked((int)g4TitleCount)).Select(i => G4Int(process, g4Title, [i]))) + "\n" +
            G4Int(process, g4EventFlag, [52, 0]) + "\n" + G4Int(process, g4IsDebug, [0]));
        private static string HashLongsG4(IEnumerable<long> values) => Hash(string.Join(',', values.Select(x => x.ToString(CultureInfo.InvariantCulture))));
        private void G4Materialize(string name) { if (g4Materialized.Add(name)) DemandCompiledBodies = Math.Max(DemandCompiledBodies, g4Materialized.Count); }
        private static long G4Private(CalledFunction call, string name, Process process) => call.TopLabel.GetPrivateVariable(name)?.GetIntValue(process.exm, [0])
            ?? throw new InvalidOperationException("R0-F4G4 private bind: " + name);
    }
}
#endif
