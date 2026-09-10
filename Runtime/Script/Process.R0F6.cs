#if R0_F6
#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MinorShift.Emuera.GameData.Variable;
using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Runtime.Diagnostics;
using MinorShift.Emuera.Runtime.Script;
using MinorShift.Emuera.Runtime.Script.Statements;
using MinorShift.Emuera.Runtime.Script.Statements.Variable;
using MinorShift.Emuera.UI.Game.Image;
using SkiaSharp;
using RuntimeConfig = MinorShift.Emuera.Runtime.Config.Config;

namespace MinorShift.Emuera.GameProc;

internal sealed partial class Process
{
    internal void R0F6BeforeLegacyInstruction(InstructionLine line) => r0f1?.BeforeF6LegacyInstruction(this, line);
    internal void R0F6AfterLegacyInstruction(InstructionLine line) => r0f1?.AfterF6LegacyInstruction(this, line);

    private sealed partial class R0F1Context
    {
        private const string F6EventShopPath = "ＳＨＯＰ関連/SHOP.ERB";
        private const string F6ImageFunction = "RPG画像追加";
        private const string F6ImageFile = "関数/組み込み関数/画像関連関数/01_画像取り込み.ERB";
        private const string F6DungeonFile = "RPG/ダンジョンアタック/SYSTEM_DUNGEON.ERB";

        private sealed record F6HostOperation(int Sequence, int SourceLine, string Command, string[] Arguments,
            long Result, string Before, string After);
        private sealed record F6Checkpoint(string Name, string ProgramCounter, string EventCursor,
            string HostState, string ActiveFunction, string StateSha256, string ResultSha256, string ResultsSha256,
            long RngCalls, string RngHash, long ClockCalls);
        private sealed record F6InputMatrixRow(string Case, bool Accepted, bool Suspended, bool Resumed,
            bool Rejected, int ResumeCount, string Reason);

        internal bool F6Enabled;
        internal object? F6Evidence { get; private set; }
        internal string F6GraphFreeGameResumed => "NOT_YET_PROVEN";

        private bool f6Prepared, f6LegacyActive, f6BranchTaken, f6ImageEntered, f6ImageCompleted;
        private long f6Dungeon, f6QuestCount, f6QuestStart;
        private VariableToken f6QuestNames = null!;
        private readonly List<F6HostOperation> f6HostOperations = [];
        private readonly List<string> f6FunctionEntries = [];
        private string[]? f6P1FunctionEntries;
        private readonly List<F6Checkpoint> f6Checkpoints = [];
        private string? f6PendingCommand, f6PendingBefore;
        private string[] f6PendingArguments = [];
        private int f6PendingLine;
        private object? f6RepresentativeIdentity, f6EventShopOracle, f6RpgOracle, f6MakeFloorOracle;
        private object? f6InputMatrix, f6Materialization, f6GuardEvidence;
        private string f6Blocker = "NOT_EVALUATED";

        internal void ExecuteF6(Process process)
        {
#if R0_F6G7R2
            if (Candidate) R0F6G8ObserveStagedExecutionDispatch();
#endif
            PrepareF6(process);
            if (Candidate)
                ExecuteCandidateF6P0P1(process);
            else
            {
                f6LegacyActive = true;
                try { process.runScriptProc(); }
                catch (R0F1PlannedCheckpointException) when (f6ImageCompleted) { }
                finally { f6LegacyActive = false; }
            }
            FinishF6(process);
        }

        private void PrepareF6(Process process)
        {
            if (f6Prepared) return;
            if (!f5bEventShopEntered || f5bEventShopCatalog[0].RelativePath != F6EventShopPath ||
                f5bEventShopCatalog[0].HeaderLine != 69 || f5bEventShopCatalog[0].FirstExecutableLine != 79)
                throw new InvalidOperationException("R0-F6 requires the exact F5B EVENTSHOP boundary");

            var currentDungeonSlot = F5Keyword(process, VariableCode.FLAG, "現ダンジョン");
            f6Dungeon = F5Read(process, f5Flag, [currentDungeonSlot]);
            f6QuestCount = ReadScalar(process, F5Token(process, "QUEST_GIDNUM", true, false));
            f6QuestStart = ReadScalar(process, F5Token(process, "QUEST_GIDNUM_START", true, false));
            f6QuestNames = F5Token(process, "QUEST_GID_SPRITENAME", false, true);
            if (f6Dungeon < 0 || f6QuestCount is <= 0 or > 10_000)
                throw new InvalidOperationException("R0-F6 representative identity is outside the admitted bounds");

            var eventShop = Definition("EVENTSHOP", F6EventShopPath, 69);
            var eventLines = Lines(Read(eventShop.Function, eventShop.File));
            var executable = eventLines.Select((text, i) => new { Line = eventShop.Line + i, Text = text.Trim() })
                .Where(x => x.Text.Length != 0 && !x.Text.StartsWith(';') && !x.Text.StartsWith('#') && !x.Text.StartsWith('@')).ToArray();
            var exactPrefix = executable.Take(5).Select(x => x.Text).SequenceEqual([
                "IF FLAG:ショップコマンド == [[ショップ:探索]]", "CALL RPG画像追加",
                "CALL MAKE_FLOOR, FLAG:現ダンジョン", "CALL DUNGEON_ATTACK", "CALL FINALIZE_DUNGEON"]);
            if (!exactPrefix) throw new InvalidOperationException("R0-F6 EVENTSHOP actual prefix changed");

            var shopSlot = F5Keyword(process, VariableCode.FLAG, "ショップコマンド");
            var shopValue = F5Read(process, f5Flag, [shopSlot]);
            var explore = G4RenameInteger("ショップ:探索");
            f6BranchTaken = shopValue == explore;
            f6EventShopOracle = new { Source = $"{F6EventShopPath}:79", FlagShopCommand = shopValue,
                ExploreValue = explore, Taken = f6BranchTaken, Hardcoded = false, ExactPrefix = exactPrefix };
            if (!f6BranchTaken) throw new InvalidOperationException("R0-F6 representative EVENTSHOP branch was not taken");

            var image = Definition(F6ImageFunction, F6ImageFile, 169);
            var imageText = Read(image.Function, image.File);
            var imageLines = Lines(imageText);
            var dynamicPicture = $"PICTURENAME_TABLE_DUNGEON_{f6Dungeon.ToString(CultureInfo.InvariantCulture)}";
            var dynamicDungeonName = $"GET_DUNGEON_NAME_{f6Dungeon.ToString(CultureInfo.InvariantCulture)}";
            var picture = UniqueNormal(dynamicPicture);
            var dungeonName = UniqueNormal(dynamicDungeonName);
            var addPictureName = $"ADD_PICTURE_DUNGEON_{f6Dungeon.ToString(CultureInfo.InvariantCulture)}";
            var addPicturePresent = functions.TryGetValue(addPictureName, out var addMatches) && addMatches.Any(x => !x.IsEvent);

            var required = new[] { "GDISPOSE", "GCREATEFROMFILE", "SPRITECREATE", "EXISTFUNCTION", "CALLFORMF", "TRYCALLFORM" };
            var missing = required.Where(x => !imageLines.Any(line => line.Contains(x, StringComparison.OrdinalIgnoreCase))).ToArray();
            if (missing.Length != 0) throw new InvalidOperationException("R0-F6 RPG image source seam changed: " + string.Join(',', missing));

            f6RepresentativeIdentity = new {
                F0T = new { TraceRows = 174733, FirstStableInputSequence = 174732, Function = "DUNGEON_ATTACK",
                    Source = $"{F6DungeonFile}:1453", Instruction = "ONEINPUTS", SystemState = "Shop_CallEventShop",
                    AcceptedUserInputCount = 0, PrimitiveInputCount = 0, RngCalls = 0, Wait = 0,
                    OperatorConfirmedRepresentativePoint = true },
                Fresh = new { Dungeon = f6Dungeon, F0TExpectedDungeon = 105, Match = f6Dungeon == 105,
                    EventShop = Identity(eventShop), Image = Identity(image), PictureTarget = Identity(picture),
                    DungeonNameTarget = Identity(dungeonName), AddPictureTarget = addPictureName, AddPicturePresent = addPicturePresent },
                IdentityGate = f6Dungeon == 105
            };
            if (f6Dungeon != 105) throw new InvalidOperationException("R0-F6 fresh dungeon identity differs from F0T authority");

            f6InputMatrix = RunInputMatrix();
            f6Checkpoints.Add(Point(process, "R0", $"{F6EventShopPath}:79:before"));
            f6Prepared = true;
        }

        private void ExecuteCandidateF6P0P1(Process process)
        {
            var image = Definition(F6ImageFunction, F6ImageFile, 169);
            var picture = UniqueNormal($"PICTURENAME_TABLE_DUNGEON_{f6Dungeon.ToString(CultureInfo.InvariantCulture)}");
            var dungeonName = UniqueNormal($"GET_DUNGEON_NAME_{f6Dungeon.ToString(CultureInfo.InvariantCulture)}");
            var pictureNames = ParsePictureNames(Read(picture.Function, picture.File));
            var dungeonDisplayName = ParseDungeonName(Read(dungeonName.Function, dungeonName.File));
            DemandCompiledBodies += 4; // EVENTSHOP prefix + reached RPG image function + two reached source-bound methods.
            f6FunctionEntries.AddRange([F6ImageFunction]);
            f6ImageEntered = true;

            for (var i = 0; i < f6QuestCount; i++)
            {
                var gid = checked(f6QuestStart + i);
                RecordCandidate(process, 206, "GDISPOSE", [gid.ToString(CultureInfo.InvariantCulture)], () => F6GDispose(gid), gid, null);
                var sprite = pictureNames.GetValueOrDefault(i, $"PICTURE_{i:000}");
                var filename = pictureNames.TryGetValue(i, out var mapped) ? mapped + ".png" : i.ToString(CultureInfo.InvariantCulture) + ".png";
                f6FunctionEntries.Add($"PICTURENAME_TABLE_DUNGEON_{f6Dungeon.ToString(CultureInfo.InvariantCulture)}");
                f6FunctionEntries.Add("GET_DUNGEON_NAME");
                f6FunctionEntries.Add($"GET_DUNGEON_NAME_{f6Dungeon.ToString(CultureInfo.InvariantCulture)}");
                filename = $"20_ダンジョン画像//{f6Dungeon:000}_{dungeonDisplayName}//{filename}";
                var created = RecordCandidate(process, 233, "GCREATEFROMFILE",
                    [gid.ToString(CultureInfo.InvariantCulture), filename], () => F6CreateFromFile(gid, filename), gid, null);
                if (created != 0)
                {
                    RecordCandidate(process, 234, "SPRITECREATE",
                        [sprite, gid.ToString(CultureInfo.InvariantCulture)], () => F6SpriteCreate(sprite, gid), null, sprite);
                    // The source assignment has no subscript; Legacy therefore writes element zero.
                    f6QuestNames.SetValue(sprite, [0]);
                }
            }
            process.vEvaluator.RESULT = 0;
            f6ImageCompleted = true;
            EventCursor = "EVENTSHOP:definition=1:SHOP.ERB:83:before-MAKE_FLOOR";
            f6Checkpoints.Add(Point(process, "R1", $"{F6EventShopPath}:83:before"));
            f6P1FunctionEntries = f6FunctionEntries.ToArray();
            PreflightMakeFloor(process);
        }

        internal void ObserveF6LegacyFunctionEntry(Process process, CalledFunction called)
        {
            if (!F6Enabled || Candidate || !f6Prepared && !called.FunctionName.Equals("EVENTSHOP", RuntimeConfig.StringComparison)) return;
            if (called.FunctionName.Equals(F6ImageFunction, RuntimeConfig.StringComparison)) f6ImageEntered = true;
            if (f6LegacyActive || f6ImageEntered) f6FunctionEntries.Add(called.FunctionName);
#if R0_F6A
            if (F6AEnabled) ObserveF6ALegacyFunctionEntry(process, called);
#if R0_F6B
            if (F6BEnabled) ObserveF6BLegacyFunctionEntry(process, called);
#if R0_F6C
            if (F6CEnabled) ObserveF6CLegacyFunctionEntry(process, called);
#if R0_F6D
            if (F6DEnabled) ObserveF6DLegacyFunctionEntry(process, called);
#endif
#endif
#endif
#endif
        }

        internal void BeforeF6LegacyInstruction(Process process, InstructionLine line)
        {
            if (!F6Enabled || Candidate || !f6LegacyActive || line.Position is not { } p || process.state.functionCount == 0) return;
            var file = p.Filename.Replace('\\', '/');
            var current = process.state.CurrentCalled.FunctionName;
#if R0_F6A
            if (F6AEnabled) BeforeF6ALegacyInstruction(process, line, file, current);
#if R0_F6B
            if (F6BEnabled) BeforeF6BLegacyInstruction(process, line, file, current);
#if R0_F6C
            if (F6CEnabled) BeforeF6CLegacyInstruction(process, line, file, current);
#if R0_F6D
            if (F6DEnabled) BeforeF6DLegacyInstruction(process, line, file, current);
#endif
#endif
#endif
#endif
            if (file.EndsWith(F6EventShopPath, StringComparison.OrdinalIgnoreCase) && current.Equals("EVENTSHOP", RuntimeConfig.StringComparison))
            {
                if (p.LineNo == 79 && !f6BranchTaken) throw new InvalidOperationException("R0-F6 Legacy branch identity mismatch");
                if (p.LineNo == 83)
                {
                    f6ImageCompleted = true;
                    EventCursor = "EVENTSHOP:definition=1:SHOP.ERB:83:before-MAKE_FLOOR";
                    f6Checkpoints.Add(Point(process, "R1", $"{F6EventShopPath}:83:before"));
                    f6P1FunctionEntries = f6FunctionEntries.ToArray();
#if R0_F6A
                    if (F6AEnabled)
                    {
                        PrepareF6A(process);
                        return;
                    }
#endif
                    PreflightMakeFloor(process);
                    throw new R0F1PlannedCheckpointException();
                }
            }
            if (!file.EndsWith(F6ImageFile, StringComparison.OrdinalIgnoreCase) || !current.Equals(F6ImageFunction, RuntimeConfig.StringComparison)) return;
            if (p.LineNo == 206)
            {
                var gid = PrivateInt(process, "GID");
                BeginLegacyOperation(206, "GDISPOSE", [gid.ToString(CultureInfo.InvariantCulture)], Resource(gid, null));
            }
            else if (p.LineNo == 233)
            {
                var gid = PrivateInt(process, "GID");
                var filename = PrivateString(process, "L_FILENAME");
                BeginLegacyOperation(233, "GCREATEFROMFILE", [gid.ToString(CultureInfo.InvariantCulture), filename], Resource(gid, null));
            }
            else if (p.LineNo == 234)
            {
                var gid = PrivateInt(process, "GID");
                var sprite = PrivateString(process, "L_SPRITENAME");
                BeginLegacyOperation(234, "SPRITECREATE", [sprite, gid.ToString(CultureInfo.InvariantCulture)], Resource(null, sprite));
            }
        }

        internal void AfterF6LegacyInstruction(Process process, InstructionLine line)
        {
#if R0_F6D
            if (F6DEnabled) AfterF6DLegacyInstruction(process, line);
#endif
#if R0_F6C
            if (F6CEnabled) AfterF6CLegacyInstruction(process, line);
#endif
            if (f6PendingCommand is null || line.Position is not { } p || p.LineNo != f6PendingLine) return;
            long result;
            string after;
            if (f6PendingCommand == "SPRITECREATE")
            {
                var sprite = f6PendingArguments[0];
                after = Resource(null, sprite);
                result = after == f6PendingBefore ? 0 : 1;
            }
            else
            {
                var gid = long.Parse(f6PendingArguments[0], CultureInfo.InvariantCulture);
                after = Resource(gid, null);
                var beforeCreated = f6PendingBefore!.Contains("Created = True", StringComparison.Ordinal);
                var afterCreated = after.Contains("Created = True", StringComparison.Ordinal);
                result = f6PendingCommand == "GDISPOSE" ? beforeCreated ? 1 : 0 : !beforeCreated && afterCreated ? 1 : 0;
            }
            f6HostOperations.Add(new(f6HostOperations.Count + 1, f6PendingLine, f6PendingCommand,
                f6PendingArguments, result, f6PendingBefore!, after));
            f6PendingCommand = null;
        }

        private void FinishF6(Process process)
        {
            if (!f6ImageCompleted) throw new InvalidOperationException("R0-F6 did not complete F6-P1");
            var operationDigest = Hash(string.Join('\n', f6HostOperations.Select(x =>
                $"{x.Sequence}|{x.SourceLine}|{x.Command}|{string.Join('|', x.Arguments)}|{x.Result}|{x.Before}|{x.After}")));
            var resources = Enumerable.Range(0, checked((int)f6QuestCount)).Select(i => Resource(f6QuestStart + i, null)).ToArray();
            var names = Enumerable.Range(0, checked((int)f6QuestCount)).Select(i => F5String(process, f6QuestNames, [i])).ToArray();
            var dynamicCounts = (f6P1FunctionEntries ?? f6FunctionEntries.ToArray()).GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).Select(x => new { Function = x.Key, Count = x.Count() }).ToArray();
            f6RpgOracle = new {
                ClosureReady = true, Completed = true, Source = Identity(Definition(F6ImageFunction, F6ImageFile, 169)),
                Dungeon = f6Dungeon, Iterations = f6QuestCount, HostOperations = f6HostOperations.Count,
                OperationSha256 = operationDigest, DynamicCalls = dynamicCounts,
                QuestSpriteNamesSha256 = Hash(string.Join('\n', names)), ResourceStateSha256 = Hash(string.Join('\n', resources)),
                EventShopFrameActive = true, NextPc = $"{F6EventShopPath}:83:before"
            };
            f6Materialization = new { StartupCompiledBodies = 0, DemandCompiledBodies,
                ReachedPhysicalFunctions = dynamicCounts.Length + 1, Unreached = new[] { "MAKE_FLOOR", "DUNGEON_ATTACK", "FINALIZE_DUNGEON", "EVENTSHOP suffix" },
                LegacyGraphRetained = 0, DescriptorEstimateBytes = RetainedEstimateBytes, ProgramEstimateBytes = Candidate ? f6QuestCount * 8 : 0 };
            var guardTotal = Candidate ? R0E1AProof.Counters.Sum() : 0;
            f6GuardEvidence = new { ForbiddenCategories = 17, Total = guardTotal, LegacyRetryAfterCompact = 0,
                ProductionBridgeUsed = "NO", LegacyErbLoad = 0, LegacyErbExecute = 0, LegacyResolver = 0,
                CalledFunction = 0, IntoFunction = 0, DoScript = 0 };
            var matrixPass = (bool)f6InputMatrix!.GetType().GetProperty("AllPassed")!.GetValue(f6InputMatrix)!;
            var gate = f6BranchTaken && f6ImageCompleted && matrixPass && (!Candidate || guardTotal == 0) ? "PARTIAL_PASS" : "FAIL";
            F6Evidence = new {
                Schema = "emuera-r0f6-representative-input-boundary-v1", Mode = Candidate ? "GraphFreeCandidate" : "LegacyControl",
                GateResult = gate, Scope = "REPRESENTATIVE_INPUT_BOUNDARY", LastCompletedSubregion = "F6-P1",
                BlockedSubregion = "F6-P2", Blocker = f6Blocker,
                RepresentativeIdentity = f6RepresentativeIdentity, EventShopPrefixOracle = f6EventShopOracle,
                RpgImageOracle = f6RpgOracle, MakeFloorOracle = f6MakeFloorOracle,
                Checkpoints = f6Checkpoints.ToArray(), InputContinuationMatrix = f6InputMatrix,
                MaterializationCensus = f6Materialization, GuardEvidence = f6GuardEvidence,
                MakeFloorCompleted = false, DungeonAttackEntered = false, DisplayOperations = 0,
                InputRequestCount = 0, AcceptedUserInputCount = 0, PrimitiveInputCount = 0,
                InputSuspended = false, EventShopStillActive = true, DungeonAttackStillActive = false,
                FinalizeDungeonEntered = 0, F0TBoundaryMatched = false, F6RepresentativeBoundaryCompleted = false,
                GraphFreeRepresentativeInputReached = false, GraphFreeGameResumed = "NOT_YET_PROVEN",
                LegacyErbGraphAvoided = Candidate, LegacyRetryAfterCompact = 0, ProductionBridgeUsed = "NO",
                ArchitectureEscalationRequired = false, ManualRecaptureRequired = "NO",
                ProductionReady = "NO", GuiValidationCompleted = "NO", MacroBenchmarkCompleted = "NO",
                NextRecommendation = "CONTINUE_F6_FROM_BLOCKER", WholeProductSuperiority = "NOT_YET_CLAIMED"
            };
            StoppedBefore = $"{F6EventShopPath}:83 CALL MAKE_FLOOR (F6-P2 preflight blocked before effect)";
#if R0_F6A
            if (F6AEnabled) FinishF6A(process);
#endif
        }

        private void PreflightMakeFloor(Process process)
        {
#if R0_F6A
            if (F6AEnabled)
            {
                PrepareF6A(process);
                if (Candidate) ExecuteCandidateF6AFloor(process);
                return;
            }
#endif
            var wrapper = UniqueNormal("MAKE_FLOOR");
            var targetName = $"MAKE_FLOOR_{f6Dungeon.ToString(CultureInfo.InvariantCulture)}";
            var target = UniqueNormal(targetName);
            var wrapperLines = Lines(Read(wrapper.Function, wrapper.File));
            var targetLines = Lines(Read(target.Function, target.File));
            var dynamicResolved = wrapperLines.Any(x => x.Trim().Equals("CALLFORM MAKE_FLOOR_{ARG}", StringComparison.OrdinalIgnoreCase));
            var closure = new[] { targetName, "MAKE_FLOOR_LINE", $"DEFINE_TILES_{f6Dungeon}", "DEFINE_TILES", $"MAKE_FLOOR_LINE_COMMON_{f6Dungeon}" };
            var missingProgramSemantics = new[] { "source-driven FOR/REPEAT floor iteration", "bound FLOOR/DPOINT multidimensional writes",
                "shared MAKE_FLOOR_LINE call program", "tile-definition closure execution" };
            f6Blocker = "actual MAKE_FLOOR dynamic closure is identified, but its shared source-driven floor program is not admitted in this slice";
            f6MakeFloorOracle = new { ClosureReady = false, Completed = false, DynamicTarget = targetName,
                Resolver = new { Argument = f6Dungeon, Resolved = dynamicResolved, HardcodedSwitch = false },
                Wrapper = Identity(wrapper), Target = Identity(target), TargetStatementCount = targetLines.Count(IsExecutable),
                RequiredClosure = closure, MissingProgramSemantics = missingProgramSemantics,
                RejectedBeforeEffect = true, HostEffectsAfterP1 = 0, NextPc = $"{F6EventShopPath}:83" };
        }

        private void BeginLegacyOperation(int line, string command, string[] arguments, string before)
        {
            if (f6PendingCommand is not null) throw new InvalidOperationException("R0-F6 overlapping Host operation");
            f6PendingLine = line; f6PendingCommand = command; f6PendingArguments = arguments; f6PendingBefore = before;
        }

        private long RecordCandidate(Process process, int line, string command, string[] arguments, Func<long> execute, long? gid, string? sprite)
        {
            var before = Resource(gid, sprite); var result = execute(); var after = Resource(gid, sprite);
            process.vEvaluator.RESULT = result;
            f6HostOperations.Add(new(f6HostOperations.Count + 1, line, command, arguments, result, before, after));
            return result;
        }

        private static long F6GDispose(long gid)
        {
            var graphics = AppContents.GetGraphics(gid);
            if (!graphics.IsCreated) return 0;
            graphics.GDispose(); return 1;
        }

        private static long F6CreateFromFile(long gid, string filename)
        {
            if (RuntimeConfig.TextDrawingMode == TextDrawingMode.WINAPI)
                throw new InvalidOperationException("R0-F6 GCREATEFROMFILE requires the existing non-WINAPI Host seam");
            var graphics = AppContents.GetGraphics(gid);
            if (graphics.IsCreated) return 0;
            var path = Path.IsPathRooted(filename) ? filename : Program.ContentDir + filename;
            if (!File.Exists(path)) return 0;
            SKImage? image = null;
            try
            {
                image = SKImage.FromEncodedData(path);
                if (image is null || image.Width > AbstractImage.MAX_IMAGESIZE || image.Height > AbstractImage.MAX_IMAGESIZE) return 0;
                graphics.GCreateFromF(image, false); image = null;
                return graphics.IsCreated ? 1 : 0;
            }
            catch { return 0; }
            finally { image?.Dispose(); }
        }

        private static long F6SpriteCreate(string name, long gid)
        {
            if (string.IsNullOrEmpty(name) || AppContents.GetSprite(name)?.IsCreated == true) return 0;
            var graphics = AppContents.GetGraphics(gid);
            if (!graphics.IsCreated) return 0;
            AppContents.CreateSpriteG(name, graphics, new Rectangle(0, 0, graphics.Width, graphics.Height));
            return 1;
        }

        private string Resource(long? gid, string? sprite) => gid.HasValue
            ? AppContents.R0F4G2GraphicsState(gid.Value).ToString()
            : AppContents.R0F4G2SpriteState(sprite!).ToString();

        private long PrivateInt(Process process, string name) => process.state.CurrentCalled.TopLabel.GetPrivateVariable(name)?.GetIntValue(process.exm, [0])
            ?? throw new InvalidOperationException("R0-F6 missing Legacy private integer " + name);
        private string PrivateString(Process process, string name) => process.state.CurrentCalled.TopLabel.GetPrivateVariable(name)?.GetStrValue(process.exm, [0])
            ?? throw new InvalidOperationException("R0-F6 missing Legacy private string " + name);

        private R0F1Definition Definition(string name, string relativePath, int line)
        {
            if (!functions.TryGetValue(name, out var matches)) throw new InvalidOperationException("R0-F6 source definition missing: " + name);
            return matches.Single(x => x.RelativePath.Equals(relativePath, StringComparison.OrdinalIgnoreCase) && x.Line == line);
        }

        private R0F1Definition UniqueNormal(string name)
        {
            if (!functions.TryGetValue(name, out var matches)) throw new InvalidOperationException("R0-F6 dynamic target missing: " + name);
            return matches.Where(x => !x.IsEvent).Single();
        }

        private static object Identity(R0F1Definition definition) => new { definition.Name, definition.RelativePath,
            HeaderLine = definition.Line, NextHeaderLine = definition.Function.Span.EndLine + 1,
            SourceSha256 = FileHash(definition.File.FileIdentity), BodySha256 = Hash(Read(definition.Function, definition.File)),
            BodyStatementCount = Lines(Read(definition.Function, definition.File)).Count(IsExecutable),
            FirstExecutable = Lines(Read(definition.Function, definition.File)).Select((x, i) => new { x, i })
                .First(x => IsExecutable(x.x)).i + definition.Line };

        private static bool IsExecutable(string line)
        {
            line = line.Trim(); return line.Length != 0 && !line.StartsWith(';') && !line.StartsWith('#') && !line.StartsWith('@');
        }

        private static long ReadScalar(Process process, VariableToken token) => token.GetIntValue(process.exm, token.Dimension == 0 ? [] : [0]);

        private static Dictionary<int, string> ParsePictureNames(string text)
        {
            var result = new Dictionary<int, string>(); int? current = null;
            foreach (var raw in Lines(text))
            {
                var line = raw.Trim(); var match = Regex.Match(line, "^CASE\\s+([0-9]+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (match.Success) { current = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture); continue; }
                if (!current.HasValue || !line.StartsWith("RESULTS_F", StringComparison.OrdinalIgnoreCase)) continue;
                var equals = line.IndexOf('='); if (equals < 0) continue;
                result[current.Value] = line[(equals + 1)..].Trim().Trim('"'); current = null;
            }
            if (result.Count == 0) throw new InvalidOperationException("R0-F6 picture-name source program was not admitted");
            return result;
        }

        private static string ParseDungeonName(string text)
        {
            var line = Lines(text).Select(x => x.Trim()).FirstOrDefault(x => x.StartsWith("RESULTS_F", StringComparison.OrdinalIgnoreCase) && x.Contains('='));
            if (line is null) throw new InvalidOperationException("R0-F6 dungeon-name source program was not admitted");
            return line[(line.IndexOf('=') + 1)..].Trim().Trim('"');
        }

        private F6Checkpoint Point(Process process, string name, string pc)
        {
            var domains = process.vEvaluator.GetDifferentialStateHashes();
            var state = Hash(string.Join('|', domains.GetType().GetProperties().Select(p => $"{p.Name}={p.GetValue(domains)}")));
            return new(name, pc, EventCursor, process.state.SystemState.ToString(),
                name == "R1" ? "EVENTSHOP" : "EVENTSHOP", state,
                Hash(string.Join(',', process.vEvaluator.RESULT_ARRAY)), Hash(string.Join('\u001f', process.vEvaluator.RESULTS_ARRAY)),
                process.vEvaluator.GetR0F4D3RandomCallCount(), process.vEvaluator.GetR0C2RngHash(), DifferentialDeterminism.ObservationCount);
        }

        private static object RunInputMatrix()
        {
            var owner = new object(); var generation = 7; long nextId = 0; long active = 0; var consumed = false; var revoked = false;
            (long Id, int Generation, object Owner) Request() { active = ++nextId; consumed = false; revoked = false; return (active, generation, owner); }
            bool Resume((long Id, int Generation, object Owner) token)
            {
                if (revoked || consumed || token.Id != active || token.Generation != generation || !ReferenceEquals(token.Owner, owner)) return false;
                consumed = true; return true;
            }
            var token = Request();
            var rows = new List<F6InputMatrixRow> { new("I1 request -> suspend", false, true, false, false, 0, "pending") };
            var accepted = Resume(token); rows.Add(new("I2 correct token", accepted, false, accepted, !accepted, accepted ? 1 : 0, accepted ? "resumed once" : "rejected"));
            accepted = Resume(token); rows.Add(new("I3 duplicate", accepted, false, false, !accepted, 1, accepted ? "accepted" : "duplicate rejected"));
            token = Request(); var wrong = (token.Id, token.Generation + 1, token.Owner); accepted = Resume(wrong);
            rows.Add(new("I4 wrong generation", accepted, true, false, !accepted, 0, accepted ? "accepted" : "generation rejected"));
            token = Request(); revoked = true; accepted = Resume(token);
            rows.Add(new("I5 owner revoke", accepted, false, false, !accepted, 0, accepted ? "accepted" : "revoked owner rejected"));
            return new { Count = rows.Count, AllPassed = rows.Count == 5 && rows[0].Suspended && rows[1].Resumed && rows.Skip(2).All(x => x.Rejected), Rows = rows };
        }
    }
}
#endif
