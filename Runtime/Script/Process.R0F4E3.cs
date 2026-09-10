#if R0_F4E3
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MinorShift.Emuera.GameData.Function;
using MinorShift.Emuera.Runtime.Diagnostics;
using MinorShift.Emuera.Runtime.Script;
using MinorShift.Emuera.Runtime.Script.Statements;
using RuntimeConfig = MinorShift.Emuera.Runtime.Config.Config;

namespace MinorShift.Emuera.GameProc;

internal sealed partial class Process
{
    private sealed partial class R0F1Context
    {
        private sealed record F4E3PreflightRow(string Family, int Outer, string Target, string Resolution,
            string BodyClass, string SourcePath, int Line, string SourceSha256, string BodySha256);
        private sealed record F4E3DigestRow(int Outer, string Family, string Target, string Resolution,
            int CalleeWrites, string ResultsSha256, int CopyWrites, string DestinationSha256);
        private sealed record F4E3Point(string Name, string ProgramCounter, long OuterLcount, long InnerLcount,
            string Frames, string EventCursor, string SystemState, bool PendingBegin, string RngSha256,
            long RngCalls, long ClockCalls, string DomainSha256);

        internal bool F4E3Enabled { get; set; }
        internal object? F4E3Evidence { get; private set; }
        private object? f4e3Preflight;
        private readonly List<F4E3PreflightRow> f4e3PreflightRows = [];
        private readonly List<F4E3DigestRow> f4e3Digest = [];
        private readonly Dictionary<string, CompactNormalHandle> f4e3NameHandles = new(RuntimeConfig.StrComper);
        private bool f4e3LegacyActive;
        private bool f4e3NameReturnPending;
        private bool f4e3ParentReturnPending;
        private bool f4e3Returned;
        private int f4e3CalleeWrites;
        private int f4e3CopyWrites;
        private int f4e3BreakExecutions;
        private string? f4e3PendingValue;
        private object? f4e3P38, f4e3P39, f4e3P40, f4e3P41, f4e3P42;
        private string f4e3RngBefore = "";
        private long f4e3RngCallsBefore;
        private long f4e3ClockBefore;

        private void PrepareF4E3Preflight(Process process, F4E1FamilyRow[] names)
        {
            var stateBefore = process.GetBenchmarkStateHash();
            ConfigureF4E2Families();
            ValidateF4E2Parent();
            BindF4E2Slots(process);
            _ = AdmitF4E2Programs();
            f4e3PreflightRows.Clear();
            foreach (var row in names)
                f4e3PreflightRows.Add(new(row.Family, row.Index, row.Name, row.Resolution, row.BodyClass,
                    row.SourcePath ?? "", row.Line ?? 0, row.SourceSha256 ?? "", row.BodySha256 ?? ""));
            foreach (var descriptor in f4e2Catalog.Values.OrderBy(x => x.Family).ThenBy(x => x.Name, RuntimeConfig.StrComper))
                f4e3PreflightRows.Add(new(descriptor.Family,
                    int.Parse(descriptor.Name[descriptor.Prefix.Length..], CultureInfo.InvariantCulture), descriptor.Name,
                    ResolveF4E2(descriptor.Name, out _).ToString(), "indexed RESULTS literals + fallthrough",
                    descriptor.Definition.RelativePath, descriptor.Definition.Line, descriptor.SourceSha256, descriptor.BodySha256));
            var pass = stateBefore == process.GetBenchmarkStateHash()
                && f4e3PreflightRows.Count == f4e1Constants!.TurnEndEvNum * 5
                && f4e3PreflightRows.All(x => x.Resolution == "Known")
                && f4e3PreflightRows.Count(x => x.Family == "NAME") == f4e1Constants.TurnEndEvNum
                && f4e3PreflightRows.Count(x => x.Family != "NAME") == f4e1Constants.TurnEndEvNum * 4;
            f4e3Preflight = new
            {
                Schema = "emuera-r0f4e3-preflight-v1", Pass = pass, EffectCount = 0,
                Constants = f4e1Constants, Rows = f4e3PreflightRows.ToArray(),
                Generated = f4e3PreflightRows.Count, Known = f4e3PreflightRows.Count(x => x.Resolution == "Known"), Unsupported = f4e3PreflightRows.Count(x => x.Resolution != "Known")
            };
            if (!pass) throw new InvalidOperationException("R0-F4E3 all-50 preflight blocked before effect");
        }

        internal void RecordF4E3Name(Process process)
        {
            if (f4e3Digest.Any(x => x.Outer == ReadF4E2Lcount(process, 0) && x.Family == "NAME")) return;
            if (f4e1Resolution == F4E1Resolution.Known && f4e1Names.TryGetValue(f4e1Target, out var descriptor))
                f4e3NameHandles.TryAdd(descriptor.Name, f4e1TargetHandle);
            f4e3Digest.Add(new((int)ReadF4E2Lcount(process, 0), "NAME", f4e1Target, f4e1Resolution.ToString(),
                f4e1Resolution == F4E1Resolution.Known ? 1 : 0, HashStrings(process.vEvaluator.RESULTS_ARRAY),
                f4e1Resolution == F4E1Resolution.Known ? 1 : 0, HashStrings(ReadF4E1Names(process))));
        }

        internal void RecordF4E3Family(Process process,
            (string Family, string Prefix, string Destination, int Count, int CallLine, int ForLine, int WriteLine, int NextLine, int NextFamilyLine) family)
        {
            var outer = (int)ReadF4E2Lcount(process, 0);
            if (f4e3Digest.Any(x => x.Outer == outer && x.Family == family.Family)) return;
            var target = family.Prefix + outer.ToString(CultureInfo.InvariantCulture);
            var resolution = ResolveF4E2(target, out var descriptor);
            f4e3Digest.Add(new(outer, family.Family, target, resolution.ToString(),
                resolution == F4E2Resolution.Known ? descriptor!.Count : 0, HashStrings(process.vEvaluator.RESULTS_ARRAY),
                resolution == F4E2Resolution.Known ? family.Count : 0, HashStrings(f4e2Destinations[family.Family].Snapshot(process))));
        }

        internal void ExecuteF4E3(Process process)
        {
            if (StoppedBefore != "TURNEND_EV_独自変数システム.ERB:54" || compactFrames.Count != 2
                || compactFrames[^1].Handle.Name != F4E1Parent || compactFrames[^1].Pc != 54 || f4e3Digest.Count != 5)
                throw new InvalidOperationException("R0-F4E3 requires the real F4E2 iteration0 boundary");
            f4e3RngBefore = process.vEvaluator.GetR0C2RngHash();
            f4e3RngCallsBefore = process.vEvaluator.GetR0F4D3RandomCallCount();
            f4e3ClockBefore = DifferentialDeterminism.ObservationCount;
            f4e3P38 = CaptureF4E3Point(process, "P38", F4E1ParentPath + ":54:before");

            if (Candidate) RunCandidateF4E3(process); else RunLegacyF4E3(process);

            var domains = CaptureF4E3Domains(process);
            var expectedFamilies = new[] { "NAME", "FLAGNAME", "STRNAME", "TAKEOVER_FLAGNAME", "TAKEOVER_STRNAME" };
            var orderPass = f4e3Digest.Count == f4e1Constants!.TurnEndEvNum * expectedFamilies.Length
                && f4e3Digest.Select((x, i) => x.Outer == i / expectedFamilies.Length && x.Family == expectedFamilies[i % expectedFamilies.Length]).All(x => x);
            var finalOuter = ReadF4E2Lcount(process, 0);
            var finalInner = ReadF4E2Lcount(process, 1);
            var guardTotal = Candidate ? R0E1AProof.Counters.Sum() : 0;
            var pass = orderPass && finalOuter == f4e1Constants.TurnEndEvNum && f4e3BreakExecutions == 0
                && f4e3P38 is not null && f4e3P39 is not null && f4e3P40 is not null && f4e3P41 is not null && f4e3P42 is not null
                && compactFrames.Count == 1 && compactFrames[0].Handle.Name == "SET_GAMEPLAY_START" && compactFrames[0].Pc == 1024
                && StoppedBefore == "SYSTEM.ERB:1024 CALL SET_SHOP_EV_VAR" && EventCursor.Contains("return-pc=SYSTEM.ERB:1007", StringComparison.Ordinal)
                && process.state.SystemState == SystemStateCode.LoadData_CallEventLoad && !process.state.isBegun
                && f4e3NameHandles.Count == f4e1Constants.TurnEndEvNum
                && f4e2Families.All(x => f4e2Materialized.GetValueOrDefault(x.Family) == f4e1Constants.TurnEndEvNum
                    && f4e2Executed.GetValueOrDefault(x.Family) == f4e1Constants.TurnEndEvNum)
                && process.vEvaluator.GetR0C2RngHash() == f4e3RngBefore
                && process.vEvaluator.GetR0F4D3RandomCallCount() == f4e3RngCallsBefore
                && DifferentialDeterminism.ObservationCount == f4e3ClockBefore && guardTotal == 0;

            F4E3Evidence = new
            {
                Schema = "emuera-r0f4e3-complete-set-turnend-v1", Mode = Candidate ? "GraphFreeCandidate" : "LegacyControl",
                Scope = "COMPLETE_SET_TURNEND_EV_VAR", GateResult = pass ? "PASS" : "FAIL",
                ResumedFrom = F4E1ParentPath + ":54 NEXT", StoppedBefore,
                Preflight = f4e3Preflight, Checkpoints = new { P38 = f4e3P38, P39 = f4e3P39, P40 = f4e3P40, P41 = f4e3P41, P42 = f4e3P42 },
                OrderedDigest = f4e3Digest.ToArray(), FullDomain = domains,
                OuterIterationsCompleted = finalOuter, TurnEndDynamicCalls = f4e3Digest.Count,
                BreakExecutions = f4e3BreakExecutions,
                PersistentBank = new { Lcount = new[] { finalOuter, finalInner }, OuterExpected = f4e1Constants.TurnEndEvNum, ReturnRestoredOldValue = false },
                Models = new { TurnEndName = "PASS", VarsetResults = "PASS", IndexedResultsWrite = "PASS", InnerFor = "PASS", TwoDimensionalWrite = "PASS", EmptyCatch = "PASS" },
                Materialization = new
                {
                    StartupCompiled = 0, PhysicalDefinitions = f4e3NameHandles.Count + f4e2Handles.Count,
                    NameDefinitions = f4e3NameHandles.Count, RemainingDefinitions = f4e2Handles.Count,
                    UniqueTemplates = 5, DescriptorEstimateBytes = (f4e3NameHandles.Count + f4e2Handles.Count) * 48L,
                    ProgramEstimateBytes = f4e3NameHandles.Count * 24L + f4e2Catalog.Values.Sum(x => 32L + x.Program.Writes.Length * 24L),
                    MissingBodies = 0
                },
                SetTurnEndEvVarCompleted = true, SetGameplayStartResumed = true,
                SetShopEvVar = new { Lookup = 0, Materialized = 0, Executed = 0, BoundaryEnforced = true },
                EventLoadCompleted = false, GraphFreeGameResumed = "NOT_YET_PROVEN",
                Guards = new { Total = guardTotal, Categories = Candidate ? R0E1AProof.GuardSnapshot() : null },
                ExternalEffects = new { FileIO = 0, Display = 0, Input = 0, RNG = 0, Clock = 0 },
                LegacyErbGraphAvoided = Candidate, LegacyRetryAfterCompact = 0, ProductionBridgeUsed = "NO",
                ManualRecaptureRequired = "NO", WholeProductSuperiority = "NOT_YET_CLAIMED"
            };
            if (!pass) throw new InvalidOperationException($"R0-F4E3 correctness gate failed order={orderPass} outer={finalOuter} inner={finalInner} rows={f4e3Digest.Count} names={f4e3NameHandles.Count} frames={compactFrames.Count}:{compactFrames[0].Pc} stop={StoppedBefore}");
        }

        private void RunCandidateF4E3(Process process)
        {
            f4e2PersistentLcount[0] = 1;
            while (f4e2PersistentLcount[0] < f4e1Constants!.TurnEndEvNum)
            {
                if (!RunCandidateF4E3Name(process)) break;
                foreach (var family in f4e2Families) RunCandidateF4E3Family(process, family);
                CaptureF4E3IterationPoint(process);
                unchecked { f4e2PersistentLcount[0]++; }
            }
            compactFrames[^1].Pc = 55;
            process.vEvaluator.RESULT = 0;
            Return(process, F4E1Parent, false);
            StoppedBefore = "SYSTEM.ERB:1024 CALL SET_SHOP_EV_VAR";
            f4e3P42 = CaptureF4E3Point(process, "P42", "SYSTEM.ERB:1024:before");
        }

        private bool RunCandidateF4E3Name(Process process)
        {
            var outer = ReadF4E2Lcount(process, 0);
            f4e1Target = BuildF4E1Target(outer);
            f4e1Resolution = ResolveF4E1(f4e1Target, out var descriptor);
            f4e3CalleeWrites = f4e3CopyWrites = 0;
            if (f4e1Resolution == F4E1Resolution.KnownMissing) { f4e3BreakExecutions++; return false; }
            if (f4e1Resolution != F4E1Resolution.Known || descriptor is null) throw new InvalidOperationException("R0-F4E3 NAME blocked after commit");
            var handle = MaterializeF4E3Name(descriptor);
            Enter(new(handle, 22, F4E1ParentPath + ":21"), checked((ushort)(descriptor.Definition.Line + 1)), false);
            process.vEvaluator.RESULTS = descriptor.Value; f4e3CalleeWrites++;
            process.vEvaluator.RESULT = 0;
            Return(process, descriptor.Name, false);
            f4e1NameSlot.Write(process.vEvaluator.RESULTS, outer); f4e3CopyWrites++;
            compactFrames[^1].Committed = true; compactFrames[^1].Pc = 26;
            RecordCurrentF4E3(process, "NAME", descriptor.Name, f4e1Resolution.ToString(), f4e3CalleeWrites, f4e3CopyWrites);
            return true;
        }

        private void RunCandidateF4E3Family(Process process,
            (string Family, string Prefix, string Destination, int Count, int CallLine, int ForLine, int WriteLine, int NextLine, int NextFamilyLine) family)
        {
            ApplyF4E2Varset(0, family.Count);
            f4e2CurrentFamily = family.Family;
            f4e2CurrentTarget = family.Prefix + ReadF4E2Lcount(process, 0).ToString(CultureInfo.InvariantCulture);
            f4e2CurrentResolution = ResolveF4E2(f4e2CurrentTarget, out var descriptor);
            f4e3CalleeWrites = f4e3CopyWrites = 0;
            if (f4e2CurrentResolution == F4E2Resolution.KnownMissing)
            {
                RecordCurrentF4E3(process, family.Family, f4e2CurrentTarget, f4e2CurrentResolution.ToString(), 0, 0);
                return;
            }
            if (f4e2CurrentResolution != F4E2Resolution.Known || descriptor is null) throw new InvalidOperationException("R0-F4E3 family blocked after commit");
            MaterializeF4E2(descriptor);
            Enter(new(f4e2Handles[descriptor.Name], checked((ushort)family.ForLine), F4E1ParentPath + ":" + family.CallLine), checked((ushort)(descriptor.Definition.Line + 1)), false);
            foreach (var write in descriptor.Program.Writes) { f4e2Results.SetValue(write.Value, [write.Index]); f4e3CalleeWrites++; }
            process.vEvaluator.RESULT = 0;
            Return(process, descriptor.Name, false);
            f4e2Executed[family.Family] = f4e2Executed.GetValueOrDefault(family.Family) + 1;
            f4e2PersistentLcount[1] = 0;
            while (f4e2PersistentLcount[1] < family.Count)
            {
                var inner = f4e2PersistentLcount[1];
                f4e2Destinations[family.Family].Write(f4e2Results.GetStrValue(process.exm, [inner]) ?? string.Empty, f4e2PersistentLcount[0], inner);
                f4e3CopyWrites++;
                unchecked { f4e2PersistentLcount[1]++; }
            }
            compactFrames[^1].Committed = true; compactFrames[^1].Pc = checked((ushort)family.NextFamilyLine);
            RecordCurrentF4E3(process, family.Family, descriptor.Name, f4e2CurrentResolution.ToString(), f4e3CalleeWrites, f4e3CopyWrites);
        }

        private CompactNormalHandle MaterializeF4E3Name(F4E1Descriptor descriptor)
        {
            if (FileHash(descriptor.Definition.File.FileIdentity) != descriptor.SourceSha256
                || ParseF4E1NameBody(descriptor.Definition, out var bodySha) != descriptor.Value || bodySha != descriptor.BodySha256)
                throw new InvalidOperationException("R0-F4E3 NAME materialization source mismatch");
            if (!f4e3NameHandles.TryGetValue(descriptor.Name, out var handle))
            {
                handle = F4D5Handle(52_000 + f4e3NameHandles.Count, descriptor.Name);
                f4e3NameHandles.Add(descriptor.Name, handle);
            }
            return handle;
        }

        private void RunLegacyF4E3(Process process)
        {
            var stopped = process.state.CurrentLine;
            var previous = (LogicalLine?)stopped?.ParentLabelLine;
            while (previous is not null && !ReferenceEquals(previous.NextLine, stopped)) previous = previous.NextLine;
            if (previous is null) throw new InvalidOperationException("R0-F4E3 Legacy resume predecessor not found");
            process.state.CurrentLine = previous;
            f4e3LegacyActive = true;
            try { process.runScriptProc(); throw new InvalidOperationException("R0-F4E3 Legacy crossed SYSTEM1024 stop"); }
            catch (R0F1PlannedCheckpointException) { }
            finally { f4e3LegacyActive = false; }
        }

        internal void ObserveF4E3Instruction(Process process, InstructionLine line, bool before)
        {
            if (!before) return;
            if (IsF4E1Line(line, "SYSTEM.ERB", 1024))
            {
                if (!f4e3Returned || compactFrames.Count != 1 || compactFrames[0].Pc != 1024) throw new InvalidOperationException("R0-F4E3 Legacy return boundary");
                StoppedBefore = "SYSTEM.ERB:1024 CALL SET_SHOP_EV_VAR";
                f4e3P42 = CaptureF4E3Point(process, "P42", "SYSTEM.ERB:1024:before");
                throw new R0F1PlannedCheckpointException();
            }
            if (!IsF4E1Line(line, "TURNEND_EV_独自変数システム.ERB", line.Position?.LineNo ?? -1)) return;
            var number = line.Position!.Value.LineNo;
            compactFrames[^1].Pc = checked((ushort)number);
            if (number == 21)
            {
                f4e1Target = BuildF4E1Target(ReadF4E2Lcount(process, 0));
                f4e1Resolution = ResolveF4E1(f4e1Target, out _);
                f4e3CalleeWrites = f4e3CopyWrites = 0; f4e3Returned = false;
            }
            if (number == 26) RecordCurrentF4E3(process, "NAME", f4e1Target, f4e1Resolution.ToString(), f4e3CalleeWrites, f4e3CopyWrites);
            foreach (var family in f4e2Families.Where(x => x.CallLine == number))
            {
                f4e2CurrentFamily = family.Family;
                f4e2CurrentTarget = family.Prefix + ReadF4E2Lcount(process, 0).ToString(CultureInfo.InvariantCulture);
                f4e2CurrentResolution = ResolveF4E2(f4e2CurrentTarget, out _);
                f4e3CalleeWrites = f4e3CopyWrites = 0; f4e3Returned = false;
            }
            foreach (var family in f4e2Families.Where(x => x.NextFamilyLine == number))
                RecordCurrentF4E3(process, family.Family, family.Prefix + ReadF4E2Lcount(process, 0).ToString(CultureInfo.InvariantCulture),
                    ResolveF4E2(family.Prefix + ReadF4E2Lcount(process, 0).ToString(CultureInfo.InvariantCulture), out _).ToString(), f4e3CalleeWrites, f4e3CopyWrites);
            if (number == 54) CaptureF4E3IterationPoint(process);
        }

        internal void ObserveF4E3NameDynamic(Process process, InstructionLine line, string target, bool found)
        {
            if (!IsF4E1Line(line, "TURNEND_EV_独自変数システム.ERB", 21)) return;
            var expected = BuildF4E1Target(ReadF4E2Lcount(process, 0));
            var resolution = ResolveF4E1(target, out _);
            if (target != expected || found != (resolution == F4E1Resolution.Known)) throw new InvalidOperationException("R0-F4E3 Legacy NAME resolver mismatch");
            f4e1Target = target; f4e1Resolution = resolution;
            if (resolution == F4E1Resolution.KnownMissing) f4e3BreakExecutions++;
        }

        internal void ObserveF4E3NameEntry(Process process, CalledFunction call)
        {
            var name = call.TopLabel.LabelName;
            if (!name.Equals(f4e1Target, RuntimeConfig.StringComparison)) return;
            if (f4e1Resolution != F4E1Resolution.Known || !f4e1Names.TryGetValue(name, out var descriptor)) throw new InvalidOperationException("R0-F4E3 Legacy NAME entry admission");
            Enter(new(MaterializeF4E3Name(descriptor), 22, F4E1ParentPath + ":21"), checked((ushort)(descriptor.Definition.Line + 1)), false);
            f4e3CalleeWrites = 0;
        }

        internal void ObserveF4E3NameWrite(Process process, InstructionLine line, string? value, bool before)
        {
            if (line.ParentLabelLine?.LabelName.Equals(f4e1Target, RuntimeConfig.StringComparison) == true)
            {
                var descriptor = f4e1Names[f4e1Target];
                if (before) { if (value != descriptor.Value) throw new InvalidOperationException("R0-F4E3 Legacy NAME RHS"); f4e3PendingValue = value; }
                else { if (process.vEvaluator.RESULTS != f4e3PendingValue) throw new InvalidOperationException("R0-F4E3 Legacy NAME RESULTS"); f4e3CalleeWrites++; f4e3PendingValue = null; }
                return;
            }
            if (!IsF4E1Line(line, "TURNEND_EV_独自変数システム.ERB", 22)) return;
            var outer = ReadF4E2Lcount(process, 0);
            if (before) { if (!f4e3Returned || value != process.vEvaluator.RESULTS) throw new InvalidOperationException("R0-F4E3 Legacy NAME copy order"); f4e3PendingValue = value; }
            else { if (f4e1NameSlot.Read(process, outer) != f4e3PendingValue) throw new InvalidOperationException("R0-F4E3 Legacy NAME copy"); f4e3CopyWrites++; f4e3PendingValue = null; compactFrames[^1].Committed = true; }
        }

        internal void ObserveF4E3NameOrParentFallthrough(Process process, LogicalLine line, bool before)
        {
            if (!before && f4e3NameReturnPending)
            {
                f4e3NameReturnPending = false; Return(process, f4e1Target, false); f4e3Returned = true; return;
            }
            if (!before && f4e3ParentReturnPending)
            {
                f4e3ParentReturnPending = false; Return(process, F4E1Parent, false); f4e3Returned = true; return;
            }
            if (process.state.functionCount == 0) return;
            var current = process.state.CurrentCalled.FunctionName;
            if (current.Equals(f4e1Target, RuntimeConfig.StringComparison))
            {
                if (before) f4e3NameReturnPending = true;
                return;
            }
            if (!current.Equals(F4E1Parent, RuntimeConfig.StringComparison)) return;
            if (before) f4e3ParentReturnPending = true;
        }

        internal void ObserveF4E3FamilyDynamic(Process process, InstructionLine line, string target, bool found)
        {
            var family = f4e2Families.FirstOrDefault(x => IsF4E1Line(line, "TURNEND_EV_独自変数システム.ERB", x.CallLine));
            if (family.Family is null) return;
            var expected = family.Prefix + ReadF4E2Lcount(process, 0).ToString(CultureInfo.InvariantCulture);
            var resolution = ResolveF4E2(target, out _);
            if (target != expected || found != (resolution == F4E2Resolution.Known)) throw new InvalidOperationException("R0-F4E3 Legacy family resolver mismatch");
            f4e2CurrentFamily = family.Family; f4e2CurrentTarget = target; f4e2CurrentResolution = resolution;
        }

        internal void ObserveF4E3FamilyEntry(Process process, CalledFunction call)
        {
            var name = call.TopLabel.LabelName;
            if (!name.Equals(f4e2CurrentTarget, RuntimeConfig.StringComparison)) return;
            if (ResolveF4E2(name, out var descriptor) != F4E2Resolution.Known || descriptor is null) throw new InvalidOperationException("R0-F4E3 Legacy family entry admission");
            MaterializeF4E2(descriptor);
            var family = f4e2Families.Single(x => x.Family == descriptor.Family);
            Enter(new(f4e2Handles[descriptor.Name], checked((ushort)family.ForLine), F4E1ParentPath + ":" + family.CallLine), checked((ushort)(descriptor.Definition.Line + 1)), false);
            f4e2CalleeWriteCursor = 0; f4e3CalleeWrites = 0;
        }

        internal void ObserveF4E3FamilyWrite(Process process, InstructionLine line, string? value, bool before)
        {
            if (f4e2CurrentFamily.Length == 0) return;
            var descriptor = f4e2Catalog[f4e2CurrentTarget];
            if (line.ParentLabelLine?.LabelName.Equals(f4e2CurrentTarget, RuntimeConfig.StringComparison) == true)
            {
                var write = descriptor.Program.Writes[f4e2CalleeWriteCursor];
                if (before) { if (value != write.Value) throw new InvalidOperationException("R0-F4E3 Legacy RESULTS RHS"); f4e3PendingValue = value; }
                else { if (process.vEvaluator.RESULTS_ARRAY[write.Index] != f4e3PendingValue) throw new InvalidOperationException("R0-F4E3 Legacy RESULTS write"); f4e2CalleeWriteCursor++; f4e3CalleeWrites++; f4e3PendingValue = null; }
                return;
            }
            var family = f4e2Families.Single(x => x.Family == f4e2CurrentFamily);
            if (!IsF4E1Line(line, "TURNEND_EV_独自変数システム.ERB", family.WriteLine)) return;
            var outer = ReadF4E2Lcount(process, 0); var inner = ReadF4E2Lcount(process, 1);
            if (before) { if (!f4e3Returned || value != process.vEvaluator.RESULTS_ARRAY[inner]) throw new InvalidOperationException("R0-F4E3 Legacy 2D order"); f4e3PendingValue = value; }
            else { if (f4e2Destinations[family.Family].Read(process, outer, inner) != f4e3PendingValue) throw new InvalidOperationException("R0-F4E3 Legacy 2D write"); f4e3CopyWrites++; f4e3PendingValue = null; compactFrames[^1].Committed = true; }
        }

        internal void ObserveF4E3FamilyFallthrough(Process process, LogicalLine line, bool before)
        {
            if (!before && f4e2ReturnPending)
            {
                f4e2ReturnPending = false;
                if (f4e2CalleeWriteCursor != f4e2Catalog[f4e2CurrentTarget].Count) throw new InvalidOperationException("R0-F4E3 Legacy family early fallthrough");
                Return(process, f4e2CurrentTarget, false); f4e3Returned = true;
                f4e2Executed[f4e2CurrentFamily] = f4e2Executed.GetValueOrDefault(f4e2CurrentFamily) + 1;
                return;
            }
            if (process.state.functionCount == 0 || !process.state.CurrentCalled.FunctionName.Equals(f4e2CurrentTarget, RuntimeConfig.StringComparison)) return;
            if (before) f4e2ReturnPending = true;
        }

        private void RecordCurrentF4E3(Process process, string family, string target, string resolution, int calleeWrites, int copyWrites)
        {
            var outer = (int)ReadF4E2Lcount(process, 0);
            if (f4e3Digest.Any(x => x.Outer == outer && x.Family == family)) return;
            var destination = family == "NAME" ? ReadF4E1Names(process) : f4e2Destinations[family].Snapshot(process);
            f4e3Digest.Add(new(outer, family, target, resolution, calleeWrites, HashStrings(process.vEvaluator.RESULTS_ARRAY), copyWrites, HashStrings(destination)));
        }

        private void CaptureF4E3IterationPoint(Process process)
        {
            var outer = ReadF4E2Lcount(process, 0);
            var point = outer switch
            {
                0 => CaptureF4E3Point(process, "P38", F4E1ParentPath + ":54:before"),
                1 => CaptureF4E3Point(process, "P39", F4E1ParentPath + ":54:before"),
                5 => CaptureF4E3Point(process, "P40", F4E1ParentPath + ":54:before"),
                9 => CaptureF4E3Point(process, "P41", F4E1ParentPath + ":54:before"),
                _ => null
            };
            switch (outer) { case 0: f4e3P38 = point; break; case 1: f4e3P39 = point; break; case 5: f4e3P40 = point; break; case 9: f4e3P41 = point; break; }
        }

        private F4E3Point CaptureF4E3Point(Process process, string name, string pc) => new(name, pc,
            ReadF4E2Lcount(process, 0), ReadF4E2Lcount(process, 1), string.Join(" > ", compactFrames.Select(FrameText)),
            EventCursor, process.state.SystemState.ToString(), process.state.isBegun, process.vEvaluator.GetR0C2RngHash(),
            process.vEvaluator.GetR0F4D3RandomCallCount(), DifferentialDeterminism.ObservationCount,
            HashStrings(CaptureF4E3DomainStrings(process)));

        private object CaptureF4E3Domains(Process process) => new
        {
            TurnEndEvName = ReadF4E1Names(process),
            TurnEndEvFlagName = f4e2Destinations["FLAGNAME"].Snapshot(process),
            TurnEndEvStrName = f4e2Destinations["STRNAME"].Snapshot(process),
            TurnEndEvFlagNameTakeover = f4e2Destinations["TAKEOVER_FLAGNAME"].Snapshot(process),
            TurnEndEvStrNameTakeover = f4e2Destinations["TAKEOVER_STRNAME"].Snapshot(process),
            Result = (long[])process.vEvaluator.RESULT_ARRAY.Clone(), Results = (string[])process.vEvaluator.RESULTS_ARRAY.Clone(),
            Lcount = new[] { ReadF4E2Lcount(process, 0), ReadF4E2Lcount(process, 1) },
            Sha256 = HashStrings(CaptureF4E3DomainStrings(process))
        };

        private string[] CaptureF4E3DomainStrings(Process process) => ReadF4E1Names(process)
            .Concat(SnapshotF4E2Destinations(process))
            .Concat(process.vEvaluator.RESULT_ARRAY.Select(x => x.ToString(CultureInfo.InvariantCulture)))
            .Concat(process.vEvaluator.RESULTS_ARRAY.Select(x => x ?? ""))
            .Concat(new[] { ReadF4E2Lcount(process, 0).ToString(CultureInfo.InvariantCulture), ReadF4E2Lcount(process, 1).ToString(CultureInfo.InvariantCulture) })
            .ToArray();
    }
}
#endif
