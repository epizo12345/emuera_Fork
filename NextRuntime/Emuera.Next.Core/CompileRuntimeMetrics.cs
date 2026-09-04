using System.Diagnostics;

namespace MinorShift.Emuera.Next.Core;

public readonly record struct CompileRuntimeMetricsSnapshot(
    long RuntimeGateMetadataCount,
    long RuntimeGateMetadataTicks,
    long ScanInclusiveCount,
    long ScanInclusiveTicks,
    long CompiledFinalizeCount,
    long CompiledFinalizeTicks,
    long RejectFinalizeCount,
    long RejectFinalizeTicks);

public static class CompileRuntimeMetrics
{
    private static long runtimeGateMetadataCount;
    private static long runtimeGateMetadataTicks;
    private static long scanInclusiveCount;
    private static long scanInclusiveTicks;
    private static long compiledFinalizeCount;
    private static long compiledFinalizeTicks;
    private static long rejectFinalizeCount;
    private static long rejectFinalizeTicks;

    public static void Reset()
    {
        runtimeGateMetadataCount = 0;
        runtimeGateMetadataTicks = 0;
        scanInclusiveCount = 0;
        scanInclusiveTicks = 0;
        compiledFinalizeCount = 0;
        compiledFinalizeTicks = 0;
        rejectFinalizeCount = 0;
        rejectFinalizeTicks = 0;
    }

    [Conditional("PERFORMANCE_METRICS")]
    public static void RecordRuntimeGateMetadata(long ticks)
    {
        runtimeGateMetadataCount++;
        runtimeGateMetadataTicks += ticks;
    }

    [Conditional("PERFORMANCE_METRICS")]
    public static void RecordScanInclusive(long ticks)
    {
        scanInclusiveCount++;
        scanInclusiveTicks += ticks;
    }

    [Conditional("PERFORMANCE_METRICS")]
    public static void RecordCompiledFinalize(long ticks)
    {
        compiledFinalizeCount++;
        compiledFinalizeTicks += ticks;
    }

    [Conditional("PERFORMANCE_METRICS")]
    public static void RecordRejectFinalize(long ticks)
    {
        rejectFinalizeCount++;
        rejectFinalizeTicks += ticks;
    }

    public static CompileRuntimeMetricsSnapshot Snapshot() => new(
        runtimeGateMetadataCount, runtimeGateMetadataTicks,
        scanInclusiveCount, scanInclusiveTicks,
        compiledFinalizeCount, compiledFinalizeTicks,
        rejectFinalizeCount, rejectFinalizeTicks);
}
