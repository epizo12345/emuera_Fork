#if R0_E2
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using MinorShift.Emuera.GameData.Variable;
using MinorShift.Emuera.Runtime.Diagnostics;
using MinorShift.Emuera.Runtime.Script.Statements.Expression;
using MinorShift.Emuera.Runtime.Script.Statements.Function;
using MinorShift.Emuera.Runtime.Script.Statements.Variable;
using MinorShift.Emuera.Runtime.Utils;

namespace MinorShift.Emuera.GameProc;

internal sealed partial class Process
{
    private const string R0E2ExpectedState = "173E7952965EFCFABDAC9AB5A69935E6CDAC58A186221D6CE3D23C8B7494CB3F";
    private const string R0E2ExpectedCharacter = "7129A077CF0B7307C3780A0628267305707AF476BE28FE0CE81F9BFCAB4655E7";
    private const string R0E2ExpectedRng = "762C5BDC3CD4C515DC01F61E8BA0A7775895B7948D35600C7166C06B9BF8DBD7";

    private sealed record E2Identity(string StateHash, string VariableHash, string CharacterOrderHash,
        int CharacterCount, int CodecRows, int UnknownOrTypeMismatchCount, string RngHash, bool AuthorityMatch);
    private sealed record E2GraphSetup(CompactRuntimeOwner Owner, CompactBoundCallsite Have,
        CompactBoundCallsite CountSplit, CompactBoundCallsite CharaSkillCount,
        double SourceIndexMilliseconds, double FirstHaveMaterializeMilliseconds,
        double FirstHaveExecutionMilliseconds, double RepeatHaveMilliseconds,
        double CountSplitMaterializeMilliseconds, int StartupCompiled, int AfterHaveCompiled,
        int RepeatAdditionalCompiles, int AfterCountSplitCompiled, int FirstHaveSourceReads,
        int RepeatAdditionalSourceReads, int CountSplitAdditionalSourceReads);

    private E2Identity R0E2Identity()
    {
        var domains = vEvaluator.GetDifferentialStateHashes();
        var codec = R0E1AProof.CodecCounts();
        var stateHash = GetBenchmarkStateHash();
        var rng = vEvaluator.GetR0C2RngHash();
        var identity = new E2Identity(stateHash, domains.VariablesHash, domains.CharacterHash,
            vEvaluator.VariableData.CharacterList.Count, codec.Rows, codec.UnknownOrMismatch, rng,
            stateHash == R0E2ExpectedState && domains.CharacterHash == R0E2ExpectedCharacter
                && vEvaluator.VariableData.CharacterList.Count == 5 && codec == (860, 0) && rng == R0E2ExpectedRng);
        if (!identity.AuthorityMatch) throw new InvalidOperationException("R0-E2 save identity differs from E1A authority");
        return identity;
    }

    private E2GraphSetup R0E2PrepareGraph(bool executeFirstUse)
    {
        var start = Stopwatch.GetTimestamp();
        var owner = new CompactRuntimeOwner(this, exm);
        var sourceIndexMs = R0E2Measurement.Milliseconds(Stopwatch.GetTimestamp() - start);
        var startupCompiled = owner.CompiledProgramCount;

        start = Stopwatch.GetTimestamp();
        var have = owner.Bind(owner.Handle("HAVE_SKILL"), owner, this,
            [CompactSourceType.Integer, CompactSourceType.Integer, CompactSourceType.Integer, CompactSourceType.String]);
        var firstHaveMs = R0E2Measurement.Milliseconds(Stopwatch.GetTimestamp() - start);
        var afterHave = owner.CompiledProgramCount;
        var firstReads = owner.FunctionSliceReadCount;

        double firstExecutionMs = 0;
        if (executeFirstUse)
        {
            start = Stopwatch.GetTimestamp();
            _ = have.Execute(owner, this, [QueryValue.I(0), QueryValue.I(0), QueryValue.I(0), QueryValue.S("")]);
            firstExecutionMs = R0E2Measurement.Milliseconds(Stopwatch.GetTimestamp() - start);
        }
        var compiles = owner.CompileCount;
        var reads = owner.FunctionSliceReadCount;
        start = Stopwatch.GetTimestamp();
        have = owner.Bind(owner.Handle("HAVE_SKILL"), owner, this,
            [CompactSourceType.Integer, CompactSourceType.Integer, CompactSourceType.Integer, CompactSourceType.String]);
        if (executeFirstUse) _ = have.Execute(owner, this,
            [QueryValue.I(0), QueryValue.I(0), QueryValue.I(0), QueryValue.S("")]);
        var repeatMs = R0E2Measurement.Milliseconds(Stopwatch.GetTimestamp() - start);
        var repeatCompiles = owner.CompileCount - compiles;
        var repeatReads = owner.FunctionSliceReadCount - reads;

        reads = owner.FunctionSliceReadCount;
        start = Stopwatch.GetTimestamp();
        var count = owner.Bind(owner.Handle("COUNT_SPLIT"), owner, this,
            [CompactSourceType.String, CompactSourceType.String, CompactSourceType.String]);
        var countMs = R0E2Measurement.Milliseconds(Stopwatch.GetTimestamp() - start);
        var countReads = owner.FunctionSliceReadCount - reads;
        var chara = owner.Bind(owner.Handle("CHARA_SKILLCOUNT"), owner, this,
            [CompactSourceType.Integer, CompactSourceType.String]);
        var result = new E2GraphSetup(owner, have, count, chara, sourceIndexMs, firstHaveMs,
            firstExecutionMs, repeatMs, countMs, startupCompiled, afterHave, repeatCompiles,
            owner.CompiledProgramCount, firstReads, repeatReads, countReads);
        if (result.StartupCompiled != 0 || result.AfterHaveCompiled != 4 || result.RepeatAdditionalCompiles != 0
            || result.AfterCountSplitCompiled != 5 || result.FirstHaveSourceReads != 4
            || result.RepeatAdditionalSourceReads != 0 || result.CountSplitAdditionalSourceReads != 1)
            throw new InvalidOperationException("R0-E2 lazy materialization gate failed");
        return result;
    }

    internal object RunR0E2CommonMemory(string run)
    {
        var memory = R0E2Measurement.CaptureFinal();
        return new { Schema="emuera-r0e2-memory-v1", Run=run, Mode="Common", Memory=memory,
            ProcessToB0Milliseconds=R0E2Measurement.ProcessToB0Milliseconds, Boundary=R0E1AProof.Boundary,
            Guard=R0E1AProof.GuardSnapshot(), LegacyErbGraphBuilt=false, SaveDecoded=false,
            CodeRetainedEstimate=(object?)null, WholeProductSuperiority="NOT_YET_CLAIMED" };
    }

    internal object RunR0E2LegacyMemory(string run, double graphReadyMilliseconds, double saveMilliseconds)
    {
        var identity = R0E2Identity();
        var labels = labelDic.Count;
        var memory = R0E2Measurement.CaptureFinal();
        return new { Schema="emuera-r0e2-memory-v1", Run=run, Mode="Legacy", Memory=memory,
            ProcessToB0Milliseconds=R0E2Measurement.ProcessToB0Milliseconds, B0ToGraphReadyMilliseconds=graphReadyMilliseconds,
            SaveDecodeMilliseconds=saveMilliseconds, Identity=identity, LabelCount=labels,
            LegacyErbGraphBuilt=labels>0, WholeProductSuperiority="NOT_YET_CLAIMED" };
    }

    internal object RunR0E2GraphFreeMemory(string run, double saveMilliseconds)
    {
        var identity = R0E2Identity();
        var setup = R0E2PrepareGraph(false);
        var estimate = setup.Owner.R0E2EstimateRetained();
        var memory = R0E2Measurement.CaptureFinal();
        var temporaryReleased = setup.Owner.R0E2TemporaryRootsReleased;
        return new { Schema="emuera-r0e2-memory-v1", Run=run, Mode="GraphFree", Memory=memory,
            ProcessToB0Milliseconds=R0E2Measurement.ProcessToB0Milliseconds, SaveDecodeMilliseconds=saveMilliseconds,
            Identity=identity, Lazy=setup, CodeRetainedEstimate=estimate, TemporaryRootsReleased=temporaryReleased,
            Boundary=R0E1AProof.Boundary, Guard=R0E1AProof.GuardSnapshot(), LegacyErbGraphBuilt=false,
            ProductionBridgeAttempts=B1Proof.BridgeAttempts, WholeProductSuperiority="NOT_YET_CLAIMED" };
    }

    internal object RunR0E2LegacyStartup(string run, double graphReadyMilliseconds, double saveMilliseconds)
    {
        var identity = R0E2Identity();
        return new { Schema="emuera-r0e2-startup-v1", Run=run, Mode="Legacy",
            ProcessToB0Milliseconds=R0E2Measurement.ProcessToB0Milliseconds,
            B0ToGraphReadyMilliseconds=graphReadyMilliseconds, SaveDecodeMilliseconds=saveMilliseconds,
            B0ToSaveDecodedMilliseconds=R0E2Measurement.SinceB0Milliseconds, Identity=identity,
            LegacyErbGraphBuilt=labelDic.Count>0, WholeProductSuperiority="NOT_YET_CLAIMED" };
    }

    internal object RunR0E2GraphFreeStartup(string run, double saveMilliseconds)
    {
        var identity = R0E2Identity();
        var setup = R0E2PrepareGraph(true);
        return new { Schema="emuera-r0e2-startup-v1", Run=run, Mode="GraphFree",
            ProcessToB0Milliseconds=R0E2Measurement.ProcessToB0Milliseconds,
            SourceIndexMilliseconds=setup.SourceIndexMilliseconds, SaveDecodeMilliseconds=saveMilliseconds,
            FirstHaveMaterializeMilliseconds=setup.FirstHaveMaterializeMilliseconds,
            FirstHaveExecutionMilliseconds=setup.FirstHaveExecutionMilliseconds,
            RepeatHaveMilliseconds=setup.RepeatHaveMilliseconds,
            CountSplitMaterializeMilliseconds=setup.CountSplitMaterializeMilliseconds,
            B0ToFiveMethodReadyMilliseconds=R0E2Measurement.SinceB0Milliseconds, Identity=identity, Lazy=setup,
#if R0_E2S
            StartupBreakdown=setup.Owner.R0E2SBreakdown,
            FunctionSliceBytesRead=setup.Owner.R0E2SFunctionSliceBytesRead,
            FingerprintBytes=setup.Owner.R0E2SFingerprintBytes,
#endif
            Guard=R0E1AProof.GuardSnapshot(), TemporaryRootsReleasedAfterForcedGc="NOT_MEASURED_IN_STARTUP_PROCESS",
            ProductionBridgeAttempts=B1Proof.BridgeAttempts, WholeProductSuperiority="NOT_YET_CLAIMED" };
    }

    private sealed record E2ExecutionRow(string Probe, string Mode, int Round, int WarmupCalls, int Calls,
        long ElapsedTicks, double Milliseconds, double NanosecondsPerCall, long ThreadAllocatedBytes,
        double AllocatedBytesPerCall, long ProcessAllocatedBytes, int Gen0, int Gen1, int Gen2,
        long Checksum, string StateBefore, string StateAfter, string RngBefore, string RngAfter,
        long Attempts, long Completed, long Faults, long FlatToFlatCalls, long LegacyRetryAfterFlat,
        long ProductionBridgeAttempts, bool StateUnchanged, bool RngUnchanged);

    internal object RunR0E2Execution(string run, bool graphFree)
    {
        var identity = R0E2Identity();
        const int warmup = 200_000, calls = 1_000_000;
        if (!int.TryParse(run.AsSpan(run.LastIndexOf('-') + 1), out var round) || round is < 1 or > 6)
            throw new InvalidOperationException("Execution run id must end in round 1..6");

        E2GraphSetup? graph = graphFree ? R0E2PrepareGraph(false) : null;
        static AExpression E(QueryValue value) => value.Text is null
            ? SingleLongTerm.FromValue(value.Integer) : SingleStrTerm.FromValue(value.Text);
        var inputs = new Dictionary<string, QueryValue[]>(StringComparer.Ordinal)
        {
            ["HAVE_SKILL"]=[QueryValue.I(0),QueryValue.I(0),QueryValue.I(0),QueryValue.S("")],
            ["COUNT_SPLIT"]=[QueryValue.S("a/b/a"),QueryValue.S("/"),QueryValue.S("a")],
            ["CHARA_SKILLCOUNT"]=[QueryValue.I(0),QueryValue.S("")]
        };
        var terms = graphFree ? null : inputs.ToDictionary(pair => pair.Key, pair =>
            (UserDefinedMethodTerm)idDic.GetFunctionMethod(labelDic, pair.Key, pair.Value.Select(E).ToList(), true), StringComparer.Ordinal);

        var scratch = new List<(VariableToken Token, Array Live, Array Copy)>();
        if (terms is not null)
            foreach (var label in terms.Values.Select(term => term.Call.TopLabel).Distinct())
                foreach (var token in new[] { "ARG", "ARGS", "LOCAL", "LOCALS" }
                    .Select(name => idDic.GetNextRuntimeLocalVariableToken(name, label))
                    .Concat(label.GetNextRuntimePrivateVariables().Select(pair => (VariableToken)pair.Value))
                    .Where(token => token is not null).Distinct())
                    if (token.GetArray() is Array live) scratch.Add((token, live, (Array)live.Clone()));
        var initialReturn = state.MethodReturnValue;
        var initialLine = state.CurrentLine;
        var initialLineCount = state.lineCount;
        var initialFunctionCount = state.functionCount;
        var initialMin = state.currentMin;
        var initialStack = methodStack;
        void Restore()
        {
            foreach (var item in scratch) Array.Copy(item.Copy, item.Live, item.Copy.Length);
            state.MethodReturnValue = initialReturn;
            state.lineCount = initialLineCount;
        }
        bool ScratchStable() => scratch.All(item => ReferenceEquals(item.Token.GetArray(), item.Live)
                && item.Live.Cast<object?>().SequenceEqual(item.Copy.Cast<object?>()))
            && ReferenceEquals(initialReturn, state.MethodReturnValue) && ReferenceEquals(initialLine, state.CurrentLine)
            && initialFunctionCount == state.functionCount && initialMin == state.currentMin && initialStack == methodStack;
        var stateHash = identity.StateHash;
        var rngHash = identity.RngHash;
        var rows = new List<E2ExecutionRow>(3);

        foreach (var name in new[] { "HAVE_SKILL", "COUNT_SPLIT", "CHARA_SKILLCOUNT" })
        {
            var values = inputs[name];
            CompactBoundCallsite bound = name switch
            {
                "HAVE_SKILL" => graph?.Have ?? default,
                "COUNT_SPLIT" => graph?.CountSplit ?? default,
                _ => graph?.CharaSkillCount ?? default
            };
            Func<long> invoke = graphFree
                ? () => bound.Execute(graph!.Owner, this, values).Integer
                : () => terms![name].GetIntValue(exm);
            static long Batch(Func<long> call, int count)
            {
                long checksum = 0;
                for (var i = 0; i < count; i++) checksum = unchecked(checksum + call());
                return checksum;
            }
            _ = Batch(invoke, warmup);
            if (!graphFree) Restore();
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            var a = graph?.Owner.Attempts ?? 0; var c = graph?.Owner.Completed ?? 0;
            var f = graph?.Owner.Faults ?? 0; var nested = graph?.Owner.FlatToFlatCalls ?? 0;
            var retry = FlatQueryProof.LegacyEntryAttempts; var bridge = B1Proof.BridgeAttempts;
            var gc0 = GC.CollectionCount(0); var gc1 = GC.CollectionCount(1); var gc2 = GC.CollectionCount(2);
            var processAllocated = GC.GetTotalAllocatedBytes(true); var threadAllocated = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            var checksum = Batch(invoke, calls);
            var elapsed = Stopwatch.GetTimestamp() - start;
            var threadDelta = GC.GetAllocatedBytesForCurrentThread() - threadAllocated;
            var processDelta = GC.GetTotalAllocatedBytes(true) - processAllocated;
            if (!graphFree) Restore();
            var stateAfter = GetBenchmarkStateHash(); var rngAfter = vEvaluator.GetR0C2RngHash();
            var row = new E2ExecutionRow(name, graphFree ? "GraphFree" : "Legacy", round, warmup, calls,
                elapsed, R0E2Measurement.Milliseconds(elapsed), elapsed * 1e9 / Stopwatch.Frequency / calls,
                threadDelta, threadDelta / (double)calls, processDelta, GC.CollectionCount(0)-gc0,
                GC.CollectionCount(1)-gc1, GC.CollectionCount(2)-gc2, checksum, stateHash, stateAfter, rngHash, rngAfter,
                (graph?.Owner.Attempts ?? 0)-a, (graph?.Owner.Completed ?? 0)-c, (graph?.Owner.Faults ?? 0)-f,
                (graph?.Owner.FlatToFlatCalls ?? 0)-nested, FlatQueryProof.LegacyEntryAttempts-retry,
                B1Proof.BridgeAttempts-bridge, stateHash==stateAfter && ScratchStable(), rngHash==rngAfter);
            if (!row.StateUnchanged || !row.RngUnchanged || row.Faults != 0 || row.LegacyRetryAfterFlat != 0
                || row.ProductionBridgeAttempts != 0 || graphFree && (row.Attempts != calls || row.Completed != calls))
                throw new InvalidOperationException("R0-E2 execution correctness failed: " + name);
            rows.Add(row);
        }
        return new { Schema="emuera-r0e2-execution-v1", Run=run, Mode=graphFree?"GraphFree":"Legacy",
            Round=round, WarmupCalls=warmup, CallsPerProbe=calls, Rows=rows, Identity=identity,
            Lazy=graph, Guard=graphFree?R0E1AProof.GuardSnapshot():null,
            FlatFaults=graph?.Owner.Faults??0, LegacyRetryAfterFlat=FlatQueryProof.LegacyEntryAttempts,
            ProductionBridgeAttempts=B1Proof.BridgeAttempts, WholeProductSuperiority="NOT_YET_CLAIMED" };
    }
}
#endif
