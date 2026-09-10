#if R0_D1
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using MinorShift.Emuera.GameData.Variable;
using MinorShift.Emuera.Runtime.Diagnostics;
using MinorShift.Emuera.Runtime.Script.Statements.Expression;
using MinorShift.Emuera.Runtime.Script.Statements.Function;
using MinorShift.Emuera.Runtime.Utils;

namespace MinorShift.Emuera.GameProc;

internal sealed partial class Process
{
    internal object RunR0D1Benchmark(string root, string phase)
    {
        var registry = r0CRegistry;
        if (registry?.Enabled != true || Program.NextRuntimeMode)
            throw new InvalidOperationException("R0D1 requires an admitted five-method registry and NextRuntime OFF");
        static AExpression E(QueryValue value) => value.Text == null
            ? SingleLongTerm.FromValue(value.Integer) : SingleStrTerm.FromValue(value.Text);
        R0C2Probe Probe(string name, params QueryValue[] inputs)
        {
            var term = (UserDefinedMethodTerm)idDic.GetFunctionMethod(labelDic, name, inputs.Select(E).ToList(), true);
            return new(name, term, inputs, term.Call.TopLabel.R0D1Binding?.ProgramId ?? -1);
        }
        var probes = new[]
        {
            Probe("HAVE_SKILL", QueryValue.I(0), QueryValue.I(0)),
            Probe("COUNT_SPLIT", QueryValue.S("a/b/a"), QueryValue.S("/"), QueryValue.S("a")),
            Probe("CHARA_SKILLCOUNT", QueryValue.I(0), QueryValue.S("")),
            Probe("BUST", QueryValue.I(-1))
        };
        if (probes.Take(3).Any(p => p.ProgramId < 0) || probes[3].ProgramId != -1)
            throw new InvalidOperationException("Incorrect prelinked cohort/miss binding");

        var scratch = new List<(VariableToken Token, Array Live, Array Copy)>();
        foreach (var label in R0CKeys.Select(key => labelDic.GetNonEventLabel(registry.Map.Functions[key].Function.Name)!)
            .Append(probes[3].Term.Call.TopLabel).Distinct())
        {
            var tokens = new[] { "ARG", "ARGS", "LOCAL", "LOCALS" }
                .Select(name => idDic.GetNextRuntimeLocalVariableToken(name, label))
                .Concat(label.GetNextRuntimePrivateVariables().Select(pair => (VariableToken)pair.Value));
            foreach (var token in tokens.Where(t => t is not null).Distinct())
                if (token.GetArray() is Array live) scratch.Add((token, live, (Array)live.Clone()));
        }
        var initialReturn = state.MethodReturnValue;
        var initialLine = state.CurrentLine;
        var initialLineCount = state.lineCount;
        var initialFunctionCount = state.functionCount;
        var initialMin = state.currentMin;
        var initialStack = methodStack;
        bool ScratchStable() => scratch.All(p => ReferenceEquals(p.Token.GetArray(), p.Live)
                && p.Live.Cast<object?>().SequenceEqual(p.Copy.Cast<object?>()))
            && ReferenceEquals(initialReturn, state.MethodReturnValue) && ReferenceEquals(initialLine, state.CurrentLine)
            && initialFunctionCount == state.functionCount && initialMin == state.currentMin && initialStack == methodStack;
        void RestoreLegacyScratch()
        {
            foreach (var p in scratch) Array.Copy(p.Copy, p.Live, p.Copy.Length);
            state.MethodReturnValue = initialReturn;
            state.lineCount = initialLineCount;
        }
        var stateHash = GetBenchmarkStateHash();
        var rngHash = vEvaluator.GetR0C2RngHash();
        PerformanceMetrics.Configure(Path.Combine(root, "raw", phase + ".metrics.jsonl"));
        const int calls = 1_000_000, warmup = 200_000, rounds = 6;
        string[] allModes = ["Legacy", "CurrentR0C", "PrelinkedR0D1", "CoreOnly"];
        var rows = new List<R0C2Round>();
        var returns = new List<object>();
        foreach (var probe in probes)
        {
            bool eligible = probe.ProgramId >= 0;
            var modes = eligible ? allModes : allModes[..3];
            Func<long> seam = () => probe.Term.GetIntValue(exm);
            Func<long> core = () => registry.ExecuteR0C2Core(probe.ProgramId, probe.Inputs);
            R0C2Round Batch(int mode, int count, int round)
            {
                if (!ScratchStable() || GetBenchmarkStateHash() != stateHash || vEvaluator.GetR0C2RngHash() != rngHash)
                    throw new InvalidOperationException("Pre-batch state/scratch changed");
                r0D1Prelinked = mode == 2;
                r0CRegistry = mode == 0 ? null : registry;
                var oldGuard = FlatQueryProof.ForbidLegacy;
                if (mode == 3) FlatQueryProof.ForbidLegacy = true;
                // Matched counter/metrics configuration in every mode; no new per-call instrumentation.
                PerformanceMetrics.BeginMacro("R0D1 batch");
                GC.Collect(2, GCCollectionMode.Forced, true, true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced, true, true);
                var a = registry.Attempts; var c = registry.Completed; var f = registry.Faults;
                var l = r0CLegacyEntries; var retry = FlatQueryProof.LegacyEntryAttempts;
                var gc0 = GC.CollectionCount(0); var gc1 = GC.CollectionCount(1); var gc2 = GC.CollectionCount(2);
                var processBefore = GC.GetTotalAllocatedBytes(true);
                var threadBefore = GC.GetAllocatedBytesForCurrentThread();
                long elapsed, checksum, allocated, processAllocated;
                int[] gc;
                try
                {
                    var start = Stopwatch.GetTimestamp();
                    checksum = R0C2Batch(mode == 3 ? core : seam, count);
                    elapsed = Stopwatch.GetTimestamp() - start;
                    allocated = GC.GetAllocatedBytesForCurrentThread() - threadBefore;
                    processAllocated = GC.GetTotalAllocatedBytes(true) - processBefore;
                    gc = [GC.CollectionCount(0) - gc0, GC.CollectionCount(1) - gc1, GC.CollectionCount(2) - gc2];
                }
                finally
                {
                    FlatQueryProof.ForbidLegacy = oldGuard;
                    r0CRegistry = registry;
                }
                var metric = PerformanceMetrics.GetR0C2FlatCounterSnapshot();
                _ = PerformanceMetrics.FinishMacro(false);
                // Legacy intentionally writes its native frame. Only Legacy runs are restored.
                if (mode == 0 || !eligible) RestoreLegacyScratch();
                var stateAfter = GetBenchmarkStateHash(); var rngAfter = vEvaluator.GetR0C2RngHash();
                bool scratchStable = ScratchStable();
                long expectedAttempts = eligible && mode is 1 or 2 ? count : 0;
                // HAVE_SKILL's Legacy body calls CHARA_SKILLCOUNT once for these fixed inputs.
                // Existing C2 raw evidence also records two native entries per top-level HAVE_SKILL.
                long expectedLegacy = mode == 0 ? count * (probe.Name == "HAVE_SKILL" ? 2L : 1L) : !eligible ? count : 0;
                if (!scratchStable || stateAfter != stateHash || rngAfter != rngHash
                    || registry.Attempts - a != expectedAttempts || registry.Completed - c != expectedAttempts
                    || metric.Attempts != expectedAttempts || metric.Completed != expectedAttempts || metric.Faults != 0
                    || registry.Faults != f || r0CLegacyEntries - l != expectedLegacy
                    || FlatQueryProof.LegacyEntryAttempts != retry || B1Proof.BridgeAttempts != 0)
                    throw new InvalidOperationException($"R0D1 batch correctness failed: {probe.Name}/{modes[mode]} scratch={scratchStable} state={stateAfter == stateHash} rng={rngAfter == rngHash} attempts={registry.Attempts-a}/{expectedAttempts} completed={registry.Completed-c} faults={registry.Faults-f} legacy={r0CLegacyEntries-l}/{expectedLegacy} metric={metric} retry={FlatQueryProof.LegacyEntryAttempts-retry}");
                return new(probe.Name, modes[mode], round, count, elapsed, elapsed * 1000.0 / Stopwatch.Frequency,
                    elapsed * 1e9 / Stopwatch.Frequency / count, allocated, allocated / (double)count, processAllocated,
                    gc, checksum, stateHash, stateAfter, rngHash, rngAfter, true, true, scratchStable,
                    registry.Attempts - a, registry.Completed - c, registry.Faults - f, r0CLegacyEntries - l,
                    metric.Attempts, metric.Completed, metric.Faults, FlatQueryProof.LegacyEntryAttempts - retry);
            }
            var values = Enumerable.Range(0, modes.Length).Select(m => Batch(m, 1, -1).Checksum).ToArray();
            if (values.Any(v => v != values[0])) throw new InvalidOperationException("Return mismatch: " + probe.Name);
            returns.Add(new { Probe = probe.Name, Modes = modes, Values = values, Equal = true });
            for (var m = 0; m < modes.Length; m++) _ = Batch(m, warmup, 0);
            long? expectedChecksum = null;
            for (var round = 1; round <= rounds; round++)
            {
                var order = Enumerable.Range(0, modes.Length);
                if (round % 2 == 0) order = order.Reverse();
                foreach (var m in order)
                {
                    var row = Batch(m, calls, round);
                    expectedChecksum ??= row.Checksum;
                    if (row.Checksum != expectedChecksum) throw new InvalidOperationException("Checksum mismatch");
                    rows.Add(row);
                    B1Proof.WriteJson(Path.Combine(root, "raw", $"{phase}.{probe.Name}.{modes[m]}.{round}.json"), row);
                }
            }
        }
        var labels = labelDic.GetAllLabels(true).Distinct().ToArray();
        return new
        {
            Schema = "emuera-r0d1-prelink-benchmark-v1", CallsPerRound = calls, WarmupCallsPerMode = warmup,
            RoundsPerMode = rounds, Order = "odd: Legacy/Current/Prelinked/Core; even: reverse; Core omitted for miss",
            MetricConfiguration = "MacroActive=true in ALL modes; timing/allocation/GC bracket batch only",
            Rows = rows, Returns = returns, AllFunctionLabelCount = labels.Length,
            BoundLabelCount = labels.Count(l => l.R0D1Binding is not null),
            ScratchArraysChecked = scratch.Count,
            Correctness = new { Pass = true, StateHash = stateHash, RngHash = rngHash, FlatFaults = registry.Faults,
                LegacyRetryAfterFlat = FlatQueryProof.LegacyEntryAttempts, ProductionBridgeUsed = "NO",
                LegacyScratch = "restored outside batch for Legacy executions only; candidates checked before any restoration" },
            WholeProductSuperiority = "NOT_YET_CLAIMED"
        };
    }
}
#endif
