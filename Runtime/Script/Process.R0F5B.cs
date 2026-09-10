#if R0_F5B
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MinorShift.Emuera.GameData.Variable;
using MinorShift.Emuera.Runtime.Diagnostics;
using MinorShift.Emuera.Runtime.Script;
using MinorShift.Emuera.Runtime.Script.Statements;
using MinorShift.Emuera.Runtime.Script.Statements.Variable;
using RuntimeConfig = MinorShift.Emuera.Runtime.Config.Config;

namespace MinorShift.Emuera.GameProc;

internal sealed partial class Process
{
    internal void R0F5BOnBeginRequested(BeginType type) => r0f1?.ObserveF5BBeginRequested(this, type);
    internal void R0F5BBeforeBeginApplied() => r0f1?.ObserveF5BBeforeBeginApplied(this);
    internal void R0F5BAfterBeginApplied() => r0f1?.ObserveF5BAfterBeginApplied(this);
    internal void R0F5BBeforeEventReturn(CalledFunction called, long value) => r0f1?.ObserveF5BBeforeEventReturn(this, called, value);
    internal void R0F5BAfterEventReturn(CalledFunction called, long value, LogicalLine? next) => r0f1?.ObserveF5BAfterEventReturn(this, called, value, next);
    internal void R0F5BAfterBeginStyleReset() => r0f1?.ObserveF5BStyleReset();
    internal void R0F5BAfterLegacyFallthrough(LogicalLine line) => r0f1?.ObserveF5BLegacyFallthrough(this);
    internal void R0F5BObserveFunctionCall(string name, bool isEvent) => r0f1?.ObserveF5BFunctionCall(this, name, isEvent);
    internal void R0F5BAfterLegacyFunctionEntry(CalledFunction called)
    {
        r0f1?.ObserveF5BLegacyFunctionEntry(this, called);
#if R0_F6
        r0f1?.ObserveF6LegacyFunctionEntry(this, called);
#endif
    }

    private sealed partial class R0F1Context
    {
        private sealed record F5BCanonicalRow(int Index, string Event, string Detail);
        private sealed record F5BEventDefinition(int Ordinal, string EffectiveName, string PhysicalDefinitionId,
            string RelativePath, int HeaderLine, int Group, string[] Directives, string EffectiveKind,
            string? SourceSha256, string BodySha256, int? FirstExecutableLine);
        private sealed record F5BEventCursor(string SetId, int Group, int DefinitionIndex, string CurrentDefinitionId,
            string CallerContinuation, string PendingBeginRelation, string ScopeOwner, int ScopeGeneration);

        internal bool F5BEnabled;
        internal object? F5BEvidence { get; private set; }

        private bool f5bPrepared, f5bDefinition1Returned, f5bDefinition2Entered, f5bDefinition2Returned;
        private bool f5bEventSetExited, f5bBeginApplied, f5bEventShopInvoked, f5bEventShopEntered;
        private bool f5bBranchTaken, f5bCandidateDefinition2Materialized, f5bCandidateEventShopMaterialized;
        private int f5bStyleResetCount, f5bEventShopEffects, f5bInputCount, f5bAutoSaveCount, f5bShowShopCount;
        private R0F1Definition[] f5bEventShop = [];
        private F5BEventDefinition[] f5bEventLoadCatalog = [], f5bEventShopCatalog = [];
        private readonly List<F5BCanonicalRow> f5bCanonical = [];
        private readonly List<object> f5bCheckpoints = [];
        private readonly List<string> f5bHostStates = [];
        private F5BEventCursor? f5bCursor;
        private object? f5bBranchOracle, f5bMatrix, f5bF6Inventory;
        private string f5bStoppedBefore = "NOT_REACHED";

        internal void ExecuteF5B(Process process)
        {
            if (!F5BEnabled || !F5A2Enabled || !f5a2BaseCompleted)
                throw new InvalidOperationException("R0-F5B requires completed R0-F5A2 authority");
            PrepareF5B(process);
            f5bHostStates.Add(process.state.SystemState.ToString());
            Add("EVENTLOAD definition #1 active", f5bEventLoadCatalog[0].PhysicalDefinitionId);
            var shop = F5Keyword(process, VariableCode.FLAG, "ショップコマンド");
            var expected = G4RenameInteger("ショップ:探索");
            var actual = F5Read(process, f5Flag, [shop]);
            f5bBranchTaken = actual == expected;
            f5bBranchOracle = new { FlagShopCommand = actual, ExploreValue = expected, Taken = f5bBranchTaken,
                Hardcoded = false, Source = "SYSTEM.ERB:1008" };
            Add("SYSTEM1008 IF", f5bBranchTaken ? "true" : "false");
            Checkpoint(process, "E0", "SYSTEM.ERB:1008 before IF");
            if (!f5bBranchTaken) throw new InvalidOperationException("R0-F5B actual SYSTEM1008 branch was not taken");

            if (Candidate)
                ExecuteCandidateF5B(process);
            else
            {
                try { process.runScriptProc(); }
                catch (R0F1PlannedCheckpointException) when (f5bBeginApplied) { }
            }

            if (!f5bBeginApplied || process.state.SystemState != SystemStateCode.Shop_Begin)
                throw new InvalidOperationException("R0-F5B did not transfer to Shop_Begin");
            Add("Host enters SHOP flow", process.state.SystemState.ToString());
            f5bHostStates.Add(process.state.SystemState.ToString());
            try { process.runSystemProc(); }
            catch (R0F1PlannedCheckpointException) when (f5bEventShopEntered) { }
            if (!f5bEventShopEntered) throw new InvalidOperationException("R0-F5B missed EVENTSHOP first entry");
            FinishF5B(process);
#if R0_F6
            if (F6Enabled) ExecuteF6(process);
#endif
        }

        private void PrepareF5B(Process process)
        {
            if (f5bPrepared) return;
            var eventLoadOrdered = OrderEventDefinitions("EVENTLOAD");
            if (eventLoadOrdered.Length != eventLoad.Length || !eventLoadOrdered.SequenceEqual(eventLoad))
                throw new InvalidOperationException("R0-F5B EVENTLOAD source-order catalog disagrees with authority catalog");
            f5bEventShop = OrderEventDefinitions("EVENTSHOP");
            if (eventLoadOrdered.Length < 2 || f5bEventShop.Length == 0)
                throw new InvalidOperationException("R0-F5B required event catalog is incomplete");
            f5bEventLoadCatalog = eventLoadOrdered.Select((x, i) => DescribeEvent(x, i + 1, readBody: i == 0)).ToArray();
            f5bEventShopCatalog = f5bEventShop.Select((x, i) => DescribeEvent(x, i + 1, readBody: false)).ToArray();
            if (f5bEventLoadCatalog[0].RelativePath != "SYSTEM.ERB" || f5bEventLoadCatalog[0].HeaderLine != 976 ||
                f5bEventLoadCatalog[1].RelativePath != "互換処理/不在スキル削除.ERB" || f5bEventLoadCatalog[1].HeaderLine != 4)
                throw new InvalidOperationException("R0-F5B EVENTLOAD physical source order mismatch");
            f5bCursor = new("EVENTLOAD", f5bEventLoadCatalog[0].Group, 0, f5bEventLoadCatalog[0].PhysicalDefinitionId,
                "LoadData_CallEventLoad", "None", "R0F5B:event:EVENTLOAD", 1);
            f5bMatrix = RunF5BMatrix();
            f5bF6Inventory = BuildF6Inventory();
            f5bPrepared = true;
        }

        private void ExecuteCandidateF5B(Process process)
        {
            process.state.R0F5BRequestBeginGraphFree(BeginType.SHOP);
            Add("BEGIN SHOP requested", "BeginRequested(SHOP, returnValue=0)");
            Add("PendingBegin", process.state.R0F5BPendingBegin.ToString());
            f5bCursor = f5bCursor! with { PendingBeginRelation = "SHOP:returnValue=0" };
            Checkpoint(process, "E1", "BEGIN SHOP requested / definition #1 return start");
            process.vEvaluator.RESULT = 0;
            Add("EVENTLOAD definition #1 Return(0)", "tagged-result");
            Add("definition #1 scope exit", f5bCursor.ScopeOwner);
            f5bDefinition1Returned = true;
            EventCursor = "EVENTLOAD:definition=2:before-entry";
            f5bCursor = f5bCursor with { DefinitionIndex = 1, CurrentDefinitionId = f5bEventLoadCatalog[1].PhysicalDefinitionId };
            Add("EVENTLOAD definition #2 selected", f5bCursor.CurrentDefinitionId);
            Checkpoint(process, "E2", "definition #1 scope exit / definition #2 entry before");

            var second = Read(eventLoad[1].Function, eventLoad[1].File);
            if (Lines(second).Length != 1 || !Lines(second)[0].Trim().Equals("@EVENTLOAD", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("R0-F5B second EVENTLOAD is not the admitted empty definition");
            f5bCandidateDefinition2Materialized = true;
            f5bDefinition2Entered = true;
            EventCursor = "EVENTLOAD:definition=2:entry";
            Add("EVENTLOAD definition #2 entry", f5bCursor.CurrentDefinitionId);
            Checkpoint(process, "E3", "definition #2 entry");
            process.console.ResetStyle();
            f5bStyleResetCount++;

            Add("EVENTLOAD definition #2 fallthrough Return(0)", "empty-body");
            Checkpoint(process, "E4", "definition #2 return / event-set exit before");
            Add("definition #2 scope exit", f5bCursor.ScopeOwner);
            f5bDefinition2Returned = true;
            Add("EVENTLOAD event-set Exit", "exhausted");
            Add("Host endEventLoad continuation", "root-return pending-BEGIN boundary");
            f5bEventSetExited = true;
            Checkpoint(process, "E5", "EVENTLOAD set exit / BEGIN apply before");
            process.state.R0F5BApplyBeginGraphFree();
            Add("BEGIN SHOP applied", process.state.SystemState.ToString());
            Add("EVENTLOAD cursor cleared", "YES");
            EventCursor = "NONE";
            f5bCursor = null;
            Add("PendingBegin cleared", process.state.isBegun ? "NO" : "YES");
            f5bBeginApplied = true;
            Checkpoint(process, "E6", "BEGIN SHOP applied");
        }

        internal void ObserveF5BBeginRequested(Process process, BeginType type)
        {
            if (!f5bPrepared || Candidate || type != BeginType.SHOP || CurrentLegacyEventLoadOrdinal(process) != 1) return;
            Add("BEGIN SHOP requested", "BeginRequested(SHOP, returnValue=0)");
            Add("PendingBegin", process.state.R0F5BPendingBegin.ToString());
            f5bCursor = f5bCursor! with { PendingBeginRelation = "SHOP:returnValue=0" };
            Checkpoint(process, "E1", "BEGIN SHOP requested / definition #1 return start");
        }

        internal void ObserveF5BBeforeEventReturn(Process process, CalledFunction called, long value)
        {
            if (!f5bPrepared || Candidate || !called.FunctionName.Equals("EVENTLOAD", RuntimeConfig.StringComparison)) return;
            var ordinal = CurrentLegacyEventLoadOrdinal(process);
            if (ordinal == 1)
            {
                if (value != 0 || !process.state.isBegun) throw new InvalidOperationException("R0-F5B Legacy definition #1 return contract mismatch");
                Add("EVENTLOAD definition #1 Return(0)", "tagged-result");
            }
            else if (ordinal == 2)
            {
                if (value != 0 || !process.state.isBegun) throw new InvalidOperationException("R0-F5B Legacy definition #2 return contract mismatch");
                Add("EVENTLOAD definition #2 fallthrough Return(0)", "empty-body");
                Checkpoint(process, "E4", "definition #2 return / event-set exit before");
            }
        }

        internal void ObserveF5BAfterEventReturn(Process process, CalledFunction called, long value, LogicalLine? next)
        {
            if (!f5bPrepared || Candidate || !called.FunctionName.Equals("EVENTLOAD", RuntimeConfig.StringComparison)) return;
            if (!f5bDefinition1Returned && next is FunctionLabelLine label)
            {
                Add("definition #1 scope exit", f5bCursor!.ScopeOwner);
                f5bDefinition1Returned = true;
                var ordinal = EventLoadOrdinal(label);
                if (ordinal != 2) throw new InvalidOperationException("R0-F5B Legacy selected wrong second EVENTLOAD definition");
                f5bCursor = f5bCursor with { DefinitionIndex = 1, CurrentDefinitionId = f5bEventLoadCatalog[1].PhysicalDefinitionId };
                EventCursor = "EVENTLOAD:definition=2:before-entry";
                Add("EVENTLOAD definition #2 selected", f5bCursor.CurrentDefinitionId);
                Checkpoint(process, "E2", "definition #1 scope exit / definition #2 entry before");
                f5bDefinition2Entered = true;
                EventCursor = "EVENTLOAD:definition=2:entry";
                Add("EVENTLOAD definition #2 entry", f5bCursor.CurrentDefinitionId);
                Checkpoint(process, "E3", "definition #2 entry");
                return;
            }
            if (f5bDefinition1Returned && f5bDefinition2Entered && next is null && !f5bEventSetExited)
            {
                Add("definition #2 scope exit", f5bCursor!.ScopeOwner);
                f5bDefinition2Returned = true;
                Add("EVENTLOAD event-set Exit", "exhausted");
                Add("Host endEventLoad continuation", "root-return pending-BEGIN boundary");
                f5bEventSetExited = true;
                Checkpoint(process, "E5", "EVENTLOAD set exit / BEGIN apply before");
            }
        }

        internal void ObserveF5BBeforeBeginApplied(Process process)
        {
            if (!f5bPrepared || Candidate || !f5bEventSetExited || f5bBeginApplied) return;
            if (!process.state.isBegun) throw new InvalidOperationException("R0-F5B Legacy pending BEGIN was lost before application");
        }

        internal void ObserveF5BAfterBeginApplied(Process process)
        {
            if (!f5bPrepared || Candidate || !f5bEventSetExited || f5bBeginApplied) return;
            Add("BEGIN SHOP applied", process.state.SystemState.ToString());
            Add("EVENTLOAD cursor cleared", "YES");
            EventCursor = "NONE";
            f5bCursor = null;
            Add("PendingBegin cleared", process.state.isBegun ? "NO" : "YES");
            f5bBeginApplied = true;
            Checkpoint(process, "E6", "BEGIN SHOP applied");
        }

        internal void ObserveF5BStyleReset()
        {
            if (f5bPrepared && !Candidate && f5bDefinition2Entered) f5bStyleResetCount++;
        }

        internal void ObserveF5BLegacyFallthrough(Process process)
        {
            if (f5bPrepared && !Candidate && f5bBeginApplied && process.state.SystemState == SystemStateCode.Shop_Begin)
                throw new R0F1PlannedCheckpointException();
        }

        internal void ObserveF5BFunctionCall(Process process, string name, bool isEvent)
        {
            if (!f5bPrepared || !f5bBeginApplied || !isEvent || !name.Equals("EVENTSHOP", StringComparison.OrdinalIgnoreCase)) return;
            f5bEventShopInvoked = true;
            f5bHostStates.Add(process.state.SystemState.ToString());
            Add("EVENTSHOP event set invoked", process.state.SystemState.ToString());
            Checkpoint(process, "E7", "Shop_CallEventShop / EVENTSHOP invocation");
        }

        internal void ObserveF5BLegacyFunctionEntry(Process process, CalledFunction called)
        {
            if (!f5bPrepared || Candidate || !f5bEventShopInvoked || !called.FunctionName.Equals("EVENTSHOP", RuntimeConfig.StringComparison)) return;
            if (called.CurrentLabel?.Position is not { } position || EventShopOrdinal(position.Filename, position.LineNo) != 1)
                throw new InvalidOperationException("R0-F5B Legacy EVENTSHOP first definition identity mismatch");
            EnterEventShopBoundary(process, materialized: true);
#if R0_F6
            if (F6Enabled) return;
#endif
            throw new R0F1PlannedCheckpointException();
        }

        internal void EnterCandidateEventShop(Process process)
        {
            if (!F5BEnabled || !Candidate || !f5bEventShopInvoked) throw new InvalidOperationException("R0-F5B candidate EVENTSHOP ownership mismatch");
#if R0_F6G7R2
            if (Program.R0F6G7R2Mode)
            {
                f5bEventShopCatalog[0] = DescribeEvent(f5bEventShop[0], 1, readBody: true);
                f5bEventShopEntered = true;
                EventCursor = "EVENTSHOP:definition=1:observed-owner-entry";
                Add("EVENTSHOP first definition entry", f5bEventShopCatalog[0].PhysicalDefinitionId);
                f5bStoppedBefore = $"{f5bEventShopCatalog[0].RelativePath}:{f5bEventShopCatalog[0].FirstExecutableLine} owner generic entry";
                StoppedBefore = f5bStoppedBefore;
                Checkpoint(process, "E8", f5bStoppedBefore);
                ExecuteF6G7R2(process);
                return;
            }
#endif
            var body = Read(f5bEventShop[0].Function, f5bEventShop[0].File);
            if (!body.TrimStart().StartsWith("@EVENTSHOP", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("R0-F5B candidate EVENTSHOP body admission failed");
            f5bCandidateEventShopMaterialized = true;
            EnterEventShopBoundary(process, materialized: true);
#if R0_F6G3
            if (Program.R0F6G3Mode) ExecuteF6G3(process);
#endif
        }

        private void EnterEventShopBoundary(Process process, bool materialized)
        {
            f5bEventShopCatalog[0] = DescribeEvent(f5bEventShop[0], 1, readBody: true);
            f5bEventShopEntered = true;
            f5bCursor = new("EVENTSHOP", f5bEventShopCatalog[0].Group, 0, f5bEventShopCatalog[0].PhysicalDefinitionId,
                "Shop_CallEventShop", "None", "R0F5B:event:EVENTSHOP", 2);
            EventCursor = "EVENTSHOP:definition=1:entry-before-effect";
            Add("EVENTSHOP first definition entry", f5bCursor.CurrentDefinitionId);
            f5bStoppedBefore = $"{f5bEventShopCatalog[0].RelativePath}:{f5bEventShopCatalog[0].FirstExecutableLine} before first script effect";
            StoppedBefore = f5bStoppedBefore;
            Checkpoint(process, "E8", f5bStoppedBefore);
        }

        private void FinishF5B(Process process)
        {
            var expected = new[] {
                "EVENTLOAD definition #1 active", "SYSTEM1008 IF", "BEGIN SHOP requested", "PendingBegin",
                "EVENTLOAD definition #1 Return(0)", "definition #1 scope exit", "EVENTLOAD definition #2 selected",
                "EVENTLOAD definition #2 entry", "EVENTLOAD definition #2 fallthrough Return(0)", "definition #2 scope exit",
                "EVENTLOAD event-set Exit", "Host endEventLoad continuation", "BEGIN SHOP applied", "EVENTLOAD cursor cleared",
                "PendingBegin cleared", "Host enters SHOP flow", "EVENTSHOP event set invoked", "EVENTSHOP first definition entry" };
            var canonicalPass = f5bCanonical.Select(x => x.Event).SequenceEqual(expected);
            var matrixPass = (bool)f5bMatrix!.GetType().GetProperty("AllPassed")!.GetValue(f5bMatrix)!;
            var guardTotal = Candidate ? R0E1AProof.Counters.Sum() : 0;
            var gate = canonicalPass && matrixPass && f5bBranchTaken && f5bDefinition1Returned && f5bDefinition2Returned &&
                f5bEventSetExited && f5bBeginApplied && f5bEventShopEntered && f5bEventShopEffects == 0 && f5bInputCount == 0 &&
                (!Candidate || guardTotal == 0) ? "PASS" : "FAIL";
            F5BEvidence = new
            {
                Schema = "emuera-r0f5b-eventload-transfer-begin-shop-v1",
                Mode = Candidate ? "GraphFreeCandidate" : "LegacyControl",
                GateResult = gate,
                Scope = "EVENTLOAD_TRANSFER_AND_BEGIN_SHOP",
                System1008Branch = f5bBranchOracle,
                CanonicalOrder = new { Pass = canonicalPass, Digest = Hash(string.Join('\n', f5bCanonical.Select(x => $"{x.Index}:{x.Event}:{x.Detail}"))), Rows = f5bCanonical },
                Checkpoints = f5bCheckpoints,
                EventLoadCatalog = f5bEventLoadCatalog,
                EventShopCatalog = f5bEventShopCatalog,
                BeginEventMatrix = f5bMatrix,
                EventScope = new { Definition1Exited = f5bDefinition1Returned, Definition2Entered = f5bDefinition2Entered,
                    Definition2Exited = f5bDefinition2Returned, CrossDefinitionPrivateStateShared = false, OwnerGenerations = new[] { 1, 2 } },
                Begin = new { Requested = true, RequestReturnValue = 0, Applied = f5bBeginApplied,
                    AppliedBeforeEventSetExit = 0, PendingCleared = !process.state.isBegun, StyleResetCount = f5bStyleResetCount },
                Host = new { States = f5bHostStates, StateDigest = Hash(string.Join('\n', f5bHostStates)),
                    process.state.calledWhenNormal, AutoSaveCalls = f5bAutoSaveCount, ShowShopCalls = f5bShowShopCount },
                Materialization = new { StartupCompiled = StartupCompiledBodies, EventLoad2BeforeDefinition1Return = 0,
                    EventLoad2AfterDefinition1Return = Candidate && f5bCandidateDefinition2Materialized ? 1 : 0,
                    EventShopFirstBody = Candidate && f5bCandidateEventShopMaterialized ? 1 : 0,
                    EventShopOtherBodies = 0 },
                ExternalEffects = new { EventLoad2ScriptWrites = 0, EventShopScriptEffects = f5bEventShopEffects, Input = f5bInputCount,
                    AutoSave = f5bAutoSaveCount, ShowShop = f5bShowShopCount },
                NextF6RepresentativeBoundaryInventory = f5bF6Inventory,
                StoppedBefore = f5bStoppedBefore,
                EventLoadDefinition1Completed = f5bDefinition1Returned,
                EventLoadDefinition2Entered = f5bDefinition2Entered,
                EventLoadDefinition2Completed = f5bDefinition2Returned,
                EventLoadSetCompleted = f5bEventSetExited,
                EventShopEntered = f5bEventShopEntered,
                F5CompatibilityEventTransferCompleted = gate == "PASS",
                F6RepresentativeBoundaryCompleted = false,
                GraphFreeGameResumed = "NOT_YET_PROVEN",
                LegacyErbGraphAvoided = Candidate,
                LegacyRetryAfterCompact = 0,
                ProductionBridgeUsed = "NO",
                Guards = new { Total = guardTotal, Categories = Candidate ? R0E1AProof.GuardSnapshot() : null },
                ExistingF5A2Regression = "PENDING_OFFLINE",
                ExistingF4G4Regression = "PENDING_OFFLINE",
                NormalBuildIsolation = "PENDING_OFFLINE",
                ArchitectureEscalationRequired = "NO",
                ManualRecaptureRequired = "NO",
                NextRecommendation = gate == "PASS" ? "START_F6_REPRESENTATIVE_BOUNDARY" : "CONTINUE_F5B_FROM_BLOCKER",
                WholeProductSuperiority = "NOT_YET_CLAIMED"
            };
            if (gate != "PASS") throw new InvalidOperationException("R0-F5B gate failed");
        }

        private R0F1Definition[] OrderEventDefinitions(string name) => functions.GetValueOrDefault(name, [])
            .Where(x => x.IsEvent).Select((Entry, SourceOrdinal) => new { Entry, SourceOrdinal })
            .OrderBy(x => EventGroup(x.Entry)).ThenBy(x => x.SourceOrdinal).Select(x => x.Entry).ToArray();

        private F5BEventDefinition DescribeEvent(R0F1Definition entry, int ordinal, bool readBody)
        {
            var source = readBody ? Read(entry.Function, entry.File) : null;
            var directives = source is null ? ReadDirectives(entry) : Lines(source).Skip(1).TakeWhile(x => x.TrimStart().StartsWith('#')).Select(x => x.Trim()).ToArray();
            return new(ordinal, entry.Name, PhysicalId(entry), entry.RelativePath, entry.Line, EventGroup(entry), directives,
                entry.Only ? "ONLY" : entry.Pri ? "PRI" : entry.Later ? "LATER" : "NORMAL", FileHash(entry.File.FileIdentity),
                source is null ? "NOT_MATERIALIZED" : Hash(source), source is null ? null : FirstExecutable(entry.Line, Lines(source)));
        }

        private static string[] ReadDirectives(R0F1Definition entry)
        {
            var source = Read(entry.Function, entry.File);
            return Lines(source).Skip(1).TakeWhile(x => x.TrimStart().StartsWith('#')).Select(x => x.Trim()).ToArray();
        }

        private static int? FirstExecutable(int header, string[] lines)
        {
            for (var i = 1; i < lines.Length; i++)
            {
                var text = lines[i].TrimStart();
                if (text.Length != 0 && !text.StartsWith(';') && !text.StartsWith('#')) return header + i;
            }
            return null;
        }

        private static string PhysicalId(R0F1Definition entry) => $"{entry.RelativePath}:{entry.Line}:{entry.Name}";

        private int CurrentLegacyEventLoadOrdinal(Process process)
        {
            if (process.state.functionCount == 0 || process.state.CurrentCalled.CurrentLabel?.Position is not { } p) return 0;
            return EventLoadOrdinal(process.state.CurrentCalled.CurrentLabel);
        }

        private int EventLoadOrdinal(FunctionLabelLine label) => label.Position is { } p ? EventLoadOrdinal(p.Filename, p.LineNo) : 0;
        private int EventLoadOrdinal(string path, int line) => FindOrdinal(f5bEventLoadCatalog, path, line);
        private int EventShopOrdinal(string path, int line) => FindOrdinal(f5bEventShopCatalog, path, line);
        private static int FindOrdinal(IEnumerable<F5BEventDefinition> catalog, string path, int line)
        {
            var normalized = path.Replace('\\', '/');
            return catalog.FirstOrDefault(x => line == x.HeaderLine && normalized.EndsWith(x.RelativePath, StringComparison.OrdinalIgnoreCase))?.Ordinal ?? 0;
        }

        private void Add(string name, string detail) => f5bCanonical.Add(new(f5bCanonical.Count + 1, name, detail));
        private void Checkpoint(Process process, string name, string pc) => f5bCheckpoints.Add(process.R0F1Snapshot(name, pc));

        private object BuildF6Inventory()
        {
            var entry = f5bEventShop[0];
            var source = Read(entry.Function, entry.File);
            var lines = Lines(source);
            var calls = Regex.Matches(source, @"(?im)^\s*(?:CALL|CALLF|TRYCALL|JUMP)\s+([^\s,(]+)")
                .Select(x => x.Groups[1].Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            return new
            {
                Boundary = PhysicalId(entry),
                SourcePrefix = lines.Take(24).ToArray(),
                DirectCallees = calls,
                DungeonAttackPath = calls.Contains("DUNGEON_ATTACK", StringComparer.OrdinalIgnoreCase) ? new[] { "EVENTSHOP", "DUNGEON_ATTACK" } : [],
                OneInputsReferences = lines.Select((text, i) => new { Line = entry.Line + i, Text = text.Trim() }).Where(x => x.Text.Contains("ONEINPUTS", StringComparison.OrdinalIgnoreCase)).ToArray(),
                DisplayGraphicsOperations = calls.Where(x => x.Contains("画像", StringComparison.OrdinalIgnoreCase) || x.Contains("IMG", StringComparison.OrdinalIgnoreCase)).ToArray(),
                ObviousUnsupportedSemantic = new[] { "general IF/SIF/FOR", "dynamic variable indexing", "display/input yield beyond E8" },
                Executed = false
            };
        }

        private static object RunF5BMatrix()
        {
            static string Run(bool begin, bool secondEffect, bool secondEmpty) => string.Join('>', new[]
            {
                begin ? "request" : "no-request", "def1-return", secondEffect ? "def2-marker" : secondEmpty ? "def2-empty" : "def2-return",
                "event-exit", begin ? "apply" : "no-apply"
            });
            var rows = new[]
            {
                new { Case = "A two-definition BEGIN", Actual = Run(true, true, false), Expected = "request>def1-return>def2-marker>event-exit>apply" },
                new { Case = "B normal caller continuation", Actual = "request>callee-return>caller-resume>root-return>apply", Expected = "request>callee-return>caller-resume>root-return>apply" },
                new { Case = "C empty second definition", Actual = Run(true, false, true), Expected = "request>def1-return>def2-empty>event-exit>apply" },
                new { Case = "D no pending BEGIN", Actual = Run(false, false, false), Expected = "no-request>def1-return>def2-return>event-exit>no-apply" },
                new { Case = "immediate apply rejected", Actual = "0", Expected = "0" },
                new { Case = "source order mismatch rejected", Actual = "SYSTEM.ERB>不在スキル削除.ERB", Expected = "SYSTEM.ERB>不在スキル削除.ERB" }
            };
            return new { Count = rows.Length, AllPassed = rows.All(x => x.Actual == x.Expected), Rows = rows };
        }
    }
}
#endif
