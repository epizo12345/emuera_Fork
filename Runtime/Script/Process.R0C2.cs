#if R0_C
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using MinorShift.Emuera.Runtime.Diagnostics;
using MinorShift.Emuera.Runtime.Script.Statements.Expression;
using MinorShift.Emuera.Runtime.Script.Statements.Function;
using MinorShift.Emuera.Runtime.Utils;

namespace MinorShift.Emuera.GameProc;

internal sealed partial class Process
{
    private sealed record R0C2Probe(string Name, UserDefinedMethodTerm Term, QueryValue[] Inputs, int ProgramId);
    private sealed record R0C2Round(string Probe, string Mode, int Round, int Calls, long ElapsedTicks,
        double ElapsedMilliseconds, double NanosecondsPerCall, long ThreadAllocatedBytes, double ThreadAllocationPerCall,
        long ProcessAllocatedBytes, int[] GcCounts, long Checksum, string StateHashBefore, string StateHashAfter,
        string RngHashBefore, string RngHashAfter, bool StateUnchanged, bool RngUnchanged, bool LegacyScratchUnchanged,
        long RegistryAttemptsDelta, long RegistryCompletedDelta, long RegistryFaultsDelta, long LegacyEntriesDelta,
        int MetricAttemptsObserved, int MetricCompletedObserved, int MetricFaultsObserved, int LegacyEntryAttemptsDelta);

    internal object RunR0C2Benchmark(string root, string phase)
    {
#if R0_D1
        r0D1Prelinked = false;
#endif
        var registry = r0CRegistry;
        if (registry?.Enabled != true)
            throw new InvalidOperationException("R0-C2 requires the validated five-method Flat registry: " + r0CRegistryReason);

        var map = registry.Map;
        static AExpression E(QueryValue value) => value.Text == null
            ? SingleLongTerm.FromValue(value.Integer) : SingleStrTerm.FromValue(value.Text);
        R0C2Probe Probe(string name, params QueryValue[] inputs)
        {
            var term = idDic.GetFunctionMethod(labelDic, name, inputs.Select(E).ToList(), true) as UserDefinedMethodTerm
                ?? throw new InvalidOperationException("R0-C2 method missing: " + name);
            return new(name, term, inputs, registry.BindR0C2(term));
        }

        var probes = new[]
        {
            Probe("HAVE_SKILL", QueryValue.I(0), QueryValue.I(0)),
            Probe("COUNT_SPLIT", QueryValue.S("a/b/a"), QueryValue.S("/"), QueryValue.S("a")),
            Probe("CHARA_SKILLCOUNT", QueryValue.I(0), QueryValue.S(""))
        };
        var miss = idDic.GetFunctionMethod(labelDic, "BUST", [SingleLongTerm.FromValue(-1)], true) as UserDefinedMethodTerm
            ?? throw new InvalidOperationException("R0-C2 miss probe missing: BUST");

        var scratch = new List<(Array Live, Array Copy)>();
        foreach (var label in probes.Select(p => p.Term.Call.TopLabel).Append(miss.Call.TopLabel).Distinct())
        {
            foreach (var family in new[] { "ARG", "ARGS", "LOCAL", "LOCALS" })
            {
                var token = idDic.GetNextRuntimeLocalVariableToken(family, label);
                if (token?.GetArray() is Array live && live.Length > 0)
                    scratch.Add((live, (Array)live.Clone()));
            }
            if (label.Position is not { } position || !map.Functions.TryGetValue(B1HotMap.Key(position.Filename, position.LineNo, label.LabelName), out var entry))
                continue;
            map.Analyze(entry);
            foreach (var name in entry.Compile?.Function?.RuntimeMetadata?.PrivateVariables.Select(v => v.Name) ?? [])
            {
                if (label.GetPrivateVariable(name)?.GetArray() is Array live)
                    scratch.Add((live, (Array)live.Clone()));
            }
        }
        var initialReturn = state.MethodReturnValue;
        var initialLine = state.CurrentLine;
        var initialLineCount = state.lineCount;
        void RestoreScratch()
        {
            foreach (var pair in scratch) Array.Copy(pair.Copy, pair.Live, pair.Copy.Length);
            state.MethodReturnValue = initialReturn;
            state.lineCount = initialLineCount;
        }
        bool ScratchStable() => scratch.All(p => p.Live.Cast<object?>().SequenceEqual(p.Copy.Cast<object?>()))
            && ReferenceEquals(initialReturn, state.MethodReturnValue) && ReferenceEquals(initialLine, state.CurrentLine)
            && state.functionCount == 0 && methodStack == 0;

        var stateHash = GetBenchmarkStateHash();
        var rngHash = vEvaluator.GetR0C2RngHash();
        PerformanceMetrics.Configure(Path.Combine(root, "raw", phase + ".counter-probe.jsonl"));

        long Legacy(UserDefinedMethodTerm term)
        {
            var saved = r0CRegistry;
            r0CRegistry = null;
            try { return term.GetIntValue(exm); }
            finally { r0CRegistry = saved; }
        }
        long Prebound(R0C2Probe probe)
            => registry.ExecuteR0C2Prebound(probe.Term, probe.ProgramId);
        long Core(R0C2Probe probe) => registry.ExecuteR0C2Core(probe.ProgramId, probe.Inputs);

        var correctness = new List<object>();
        foreach (var probe in probes)
        {
            var expected = Legacy(probe.Term); RestoreScratch();
            PerformanceMetrics.BeginMacro("R0-C2 correctness");
            var current = probe.Term.GetIntValue(exm);
            var metric = PerformanceMetrics.GetR0C2FlatCounterSnapshot();
            _ = PerformanceMetrics.FinishMacro(false);
            var noAtomic = probe.Term.GetIntValue(exm);
            var prebound = Prebound(probe);
            var oldGuard = FlatQueryProof.ForbidLegacy; FlatQueryProof.ForbidLegacy = true;
            long core;
            try { core = Core(probe); }
            finally { FlatQueryProof.ForbidLegacy = oldGuard; }
            if (new[] { current, noAtomic, prebound, core }.Any(v => v != expected) || metric.Attempts != 1 || metric.Completed != 1 || metric.Faults != 0
                || !ScratchStable() || GetBenchmarkStateHash() != stateHash || vEvaluator.GetR0C2RngHash() != rngHash)
                throw new InvalidOperationException("R0-C2 correctness failed: " + probe.Name);
            correctness.Add(new { probe.Name, Legacy = expected, CurrentR0C = current, NoAtomicCounters = noAtomic, PreboundSeam = prebound, CoreOnly = core, Match = true });
        }

        const int calls = 1_000_000, warmup = 200_000, rounds = 6;
        string[] modes = ["Legacy", "CurrentR0C", "NoAtomicCounters", "PreboundSeam", "CoreOnly"];
        int[][] order = [[0,1,2,3,4],[4,3,2,1,0],[1,2,3,4,0],[3,2,1,0,4],[2,3,4,0,1],[2,1,0,4,3]];
        var allRounds = new List<R0C2Round>();
        var probeResults = new List<object>();
        foreach (var probe in probes)
        {
            Func<long> seam = () => probe.Term.GetIntValue(exm);
            Func<long>[] engines = [seam, seam, seam, () => Prebound(probe), () => Core(probe)];
            long Run(int mode, int count, out long elapsed, out (int Attempts, int Completed, int Faults) metricAfter)
            {
                var savedRegistry = r0CRegistry;
                var oldGuard = FlatQueryProof.ForbidLegacy;
                if (mode == 0) r0CRegistry = null;
                if (mode == 1) PerformanceMetrics.BeginMacro("R0-C2 CurrentR0C");
                else { PerformanceMetrics.BeginMacro("R0-C2 counter reset"); _ = PerformanceMetrics.FinishMacro(false); }
                if (mode == 4) FlatQueryProof.ForbidLegacy = true;
                try
                {
                    var started = Stopwatch.GetTimestamp();
                    var checksum = R0C2Batch(engines[mode], count);
                    elapsed = Stopwatch.GetTimestamp() - started;
                    return checksum;
                }
                finally
                {
                    metricAfter = PerformanceMetrics.GetR0C2FlatCounterSnapshot();
                    if (mode == 1) _ = PerformanceMetrics.FinishMacro(false);
                    FlatQueryProof.ForbidLegacy = oldGuard;
                    r0CRegistry = savedRegistry;
                    if (mode == 0) RestoreScratch();
                }
            }
            for (var mode = 0; mode < modes.Length; mode++) _ = Run(mode, warmup, out _, out _);
            long? expectedChecksum = null;
            var byMode = modes.ToDictionary(m => m, _ => new List<R0C2Round>(), StringComparer.Ordinal);
            for (var round = 0; round < rounds; round++) foreach (var mode in order[round])
            {
                if (!ScratchStable() || GetBenchmarkStateHash() != stateHash || vEvaluator.GetR0C2RngHash() != rngHash)
                    throw new InvalidOperationException("R0-C2 pre-round state changed: " + probe.Name);
                GC.Collect(2, GCCollectionMode.Forced, true, true); GC.WaitForPendingFinalizers(); GC.Collect(2, GCCollectionMode.Forced, true, true);
                var gcBefore = new[] { GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2) };
                var attemptsBefore = registry.Attempts; var completedBefore = registry.Completed; var faultsBefore = registry.Faults;
                var legacyBefore = r0CLegacyEntries; var legacyProofBefore = FlatQueryProof.LegacyEntryAttempts;
                var stateBefore = GetBenchmarkStateHash(); var rngBefore = vEvaluator.GetR0C2RngHash();
                var allocationBefore = GC.GetAllocatedBytesForCurrentThread(); var processAllocationBefore = GC.GetTotalAllocatedBytes(true);
                var checksum = Run(mode, calls, out var elapsed, out var metricAfter);
                var allocated = GC.GetAllocatedBytesForCurrentThread() - allocationBefore; var processAllocated = GC.GetTotalAllocatedBytes(true) - processAllocationBefore;
                var stateAfter = GetBenchmarkStateHash(); var rngAfter = vEvaluator.GetR0C2RngHash();
                var stable = ScratchStable();
                expectedChecksum ??= checksum;
                if (checksum != expectedChecksum || stateAfter != stateBefore || rngAfter != rngBefore || !stable
                    || registry.Faults != faultsBefore || FlatQueryProof.LegacyEntryAttempts != legacyProofBefore || B1Proof.BridgeAttempts != 0)
                    throw new InvalidOperationException("R0-C2 timed correctness failed: " + probe.Name + "/" + modes[mode]);
                var row = new R0C2Round(probe.Name, modes[mode], round + 1, calls, elapsed,
                    elapsed * 1000.0 / Stopwatch.Frequency, elapsed * 1e9 / Stopwatch.Frequency / calls,
                    allocated, allocated / (double)calls, processAllocated,
                    Enumerable.Range(0, 3).Select(i => GC.CollectionCount(i) - gcBefore[i]).ToArray(), checksum,
                    stateBefore, stateAfter, rngBefore, rngAfter, true, true, stable,
                    registry.Attempts - attemptsBefore, registry.Completed - completedBefore, registry.Faults - faultsBefore,
                    r0CLegacyEntries - legacyBefore, metricAfter.Attempts,
                    metricAfter.Completed, metricAfter.Faults,
                    FlatQueryProof.LegacyEntryAttempts - legacyProofBefore);
                allRounds.Add(row); byMode[modes[mode]].Add(row);
            }
            static double Median(IEnumerable<double> values) { var a = values.Order().ToArray(); return (a[2] + a[3]) / 2; }
            var medians = modes.Select(mode => new
            {
                Mode = mode,
                NanosecondsPerCall = Median(byMode[mode].Select(r => r.NanosecondsPerCall)),
                ThreadAllocationPerCall = Median(byMode[mode].Select(r => r.ThreadAllocationPerCall)),
                GcCounts = Enumerable.Range(0, 3).Select(i => byMode[mode].Sum(r => r.GcCounts[i])).ToArray()
            }).ToArray();
            foreach (var mode in modes)
                B1Proof.WriteJson(Path.Combine(root, "raw", phase + "." + probe.Name + "." + mode + ".json"), byMode[mode]);
            probeResults.Add(new { probe.Name, Inputs = probe.Inputs, Medians = medians, Rounds = byMode });
        }

        var missModes = new[] { "LegacyDirect", "EligibilityMiss" };
        var missRows = new List<R0C2Round>();
        for (var mode = 0; mode < 2; mode++)
        {
            var saved = r0CRegistry; if (mode == 0) r0CRegistry = null;
            try { _ = R0C2Batch(() => miss.GetIntValue(exm), warmup); }
            finally { r0CRegistry = saved; RestoreScratch(); }
        }
        long? missChecksum = null;
        for (var round = 0; round < rounds; round++) foreach (var mode in round % 2 == 0 ? new[] { 0, 1 } : new[] { 1, 0 })
        {
            GC.Collect(2, GCCollectionMode.Forced, true, true); GC.WaitForPendingFinalizers(); GC.Collect(2, GCCollectionMode.Forced, true, true);
            var gcBefore = new[] { GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2) };
            var attemptsBefore = registry.Attempts; var completedBefore = registry.Completed; var faultsBefore = registry.Faults;
            var legacyBefore = r0CLegacyEntries; var proofBefore = FlatQueryProof.LegacyEntryAttempts;
            var before = GetBenchmarkStateHash(); var rngBefore = vEvaluator.GetR0C2RngHash();
            var allocBefore = GC.GetAllocatedBytesForCurrentThread(); var allBefore = GC.GetTotalAllocatedBytes(true);
            var saved = r0CRegistry; if (mode == 0) r0CRegistry = null;
            long started, elapsed, checksum;
            try { started = Stopwatch.GetTimestamp(); checksum = R0C2Batch(() => miss.GetIntValue(exm), calls); elapsed = Stopwatch.GetTimestamp() - started; }
            finally { r0CRegistry = saved; RestoreScratch(); }
            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocBefore; var processAllocated = GC.GetTotalAllocatedBytes(true) - allBefore;
            var after = GetBenchmarkStateHash(); var rngAfter = vEvaluator.GetR0C2RngHash(); var metricAfter = PerformanceMetrics.GetR0C2FlatCounterSnapshot();
            missChecksum ??= checksum;
            if (checksum != missChecksum || before != after || rngBefore != rngAfter || !ScratchStable() || registry.Faults != faultsBefore
                || FlatQueryProof.LegacyEntryAttempts != proofBefore || B1Proof.BridgeAttempts != 0)
                throw new InvalidOperationException("R0-C2 miss-path correctness failed: " + missModes[mode]);
            missRows.Add(new("Noneligible_BUST", missModes[mode], round + 1, calls, elapsed,
                elapsed * 1000.0 / Stopwatch.Frequency, elapsed * 1e9 / Stopwatch.Frequency / calls,
                allocated, allocated / (double)calls, processAllocated,
                Enumerable.Range(0, 3).Select(i => GC.CollectionCount(i) - gcBefore[i]).ToArray(), checksum,
                before, after, rngBefore, rngAfter, true, true, true,
                registry.Attempts - attemptsBefore, registry.Completed - completedBefore, registry.Faults - faultsBefore,
                r0CLegacyEntries - legacyBefore, metricAfter.Attempts,
                metricAfter.Completed, metricAfter.Faults,
                FlatQueryProof.LegacyEntryAttempts - proofBefore));
        }
        foreach (var mode in missModes)
            B1Proof.WriteJson(Path.Combine(root, "raw", phase + ".Noneligible_BUST." + mode + ".json"), missRows.Where(r => r.Mode == mode));
        static double Median6(IEnumerable<double> values) { var a = values.Order().ToArray(); return (a[2] + a[3]) / 2; }
        var missResult = new
        {
            Probe = "Noneligible_BUST",
            LegacyDirectMedianNs = Median6(missRows.Where(r => r.Mode == "LegacyDirect").Select(r => r.NanosecondsPerCall)),
            EligibilityMissMedianNs = Median6(missRows.Where(r => r.Mode == "EligibilityMiss").Select(r => r.NanosecondsPerCall)),
            Rounds = missRows
        };
        if (GetBenchmarkStateHash() != stateHash || vEvaluator.GetR0C2RngHash() != rngHash || !ScratchStable()
            || registry.Faults != 0 || FlatQueryProof.LegacyEntryAttempts != 0 || B1Proof.BridgeAttempts != 0)
            throw new InvalidOperationException("R0-C2 final correctness invariant failed");
        return new
        {
            Schema = "emuera-r0c2-overhead-benchmark-v1", Phase = phase, CallsPerRound = calls, Rounds = rounds,
            WarmupCallsPerMode = warmup, Order = order, Probes = probeResults, NoneligibleMiss = missResult,
            Correctness = new { Pass = true, Cases = correctness, StateHash = stateHash, RngHash = rngHash, LegacyScratchUnchanged = true,
                FlatFaults = 0, LegacyRetryAfterFlat = 0, ProductionBridgeUsed = "NO" },
            DiagnosticOnly = new[] { "PreboundSeam", "CoreOnly" }, WholeProductSuperiority = "NOT_YET_CLAIMED"
        };
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long R0C2Batch(Func<long> call, int count)
    {
        long checksum = 0;
        for (var i = 0; i < count; i++) checksum = unchecked(checksum * 31 + call());
        return checksum;
    }
}
#endif
