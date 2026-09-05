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
    long RejectFinalizeTicks,
    long SemanticCompileInclusiveCount,
    long SemanticCompileInclusiveTicks,
    long SemanticCountedLoopCount,
    long SemanticOptionalIntExpressionCount,
    long SemanticExpressionCount,
    long ScanFinalizeCount,
    long ScanFinalizeTicks,
    long SemanticPayloadMergeFunctionCount,
    long SemanticPartCount,
    long AssignmentSearchCount,
    long AssignmentAcceptedCount,
    long PhysicalLineCountScanned,
    long PhysicalLineBytesScanned,
    long EmptyOrCommentSkippedLineCount,
    long MetadataSkippedLineCount,
    long InstructionEmittedCount);

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
    private static long semanticCompileInclusiveCount;
    private static long semanticCompileInclusiveTicks;
    private static long semanticCountedLoopCount;
    private static long semanticOptionalIntExpressionCount;
    private static long semanticExpressionCount;
    private static long scanFinalizeCount;
    private static long scanFinalizeTicks;
    private static long semanticPayloadMergeFunctionCount;
    private static long semanticPartCount;
    private static long assignmentSearchCount;
    private static long assignmentAcceptedCount;
    private static long physicalLineCountScanned;
    private static long physicalLineBytesScanned;
    private static long emptyOrCommentSkippedLineCount;
    private static long metadataSkippedLineCount;
    private static long instructionEmittedCount;

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
        semanticCompileInclusiveCount = 0;
        semanticCompileInclusiveTicks = 0;
        semanticCountedLoopCount = 0;
        semanticOptionalIntExpressionCount = 0;
        semanticExpressionCount = 0;
        scanFinalizeCount = 0;
        scanFinalizeTicks = 0;
        semanticPayloadMergeFunctionCount = 0;
        semanticPartCount = 0;
        assignmentSearchCount = 0;
        assignmentAcceptedCount = 0;
        physicalLineCountScanned = 0;
        physicalLineBytesScanned = 0;
        emptyOrCommentSkippedLineCount = 0;
        metadataSkippedLineCount = 0;
        instructionEmittedCount = 0;
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

    [Conditional("PERFORMANCE_METRICS")]
    public static void RecordSemanticCompileInclusive(long ticks, int category)
    {
        semanticCompileInclusiveCount++;
        semanticCompileInclusiveTicks += ticks;
        if (category == 1) semanticCountedLoopCount++;
        else if (category == 2) semanticOptionalIntExpressionCount++;
        else semanticExpressionCount++;
    }

    [Conditional("PERFORMANCE_METRICS")]
    public static void RecordScanFinalize(long ticks, int semanticPartCountForScan)
    {
        scanFinalizeCount++;
        scanFinalizeTicks += ticks;
        if (semanticPartCountForScan > 0) semanticPayloadMergeFunctionCount++;
        semanticPartCount += semanticPartCountForScan;
    }

    [Conditional("PERFORMANCE_METRICS")]
    public static void RecordAssignmentSearch(bool accepted)
    {
        assignmentSearchCount++;
        if (accepted) assignmentAcceptedCount++;
    }

    [Conditional("PERFORMANCE_METRICS")]
    public static void RecordPhysicalLine(int byteCount)
    {
        physicalLineCountScanned++;
        physicalLineBytesScanned += byteCount;
    }

    [Conditional("PERFORMANCE_METRICS")]
    public static void RecordEmptyOrCommentSkippedLine() => emptyOrCommentSkippedLineCount++;

    [Conditional("PERFORMANCE_METRICS")]
    public static void RecordMetadataSkippedLine() => metadataSkippedLineCount++;

    [Conditional("PERFORMANCE_METRICS")]
    public static void RecordInstructionEmitted() => instructionEmittedCount++;

    public static CompileRuntimeMetricsSnapshot Snapshot() => new(
        runtimeGateMetadataCount, runtimeGateMetadataTicks,
        scanInclusiveCount, scanInclusiveTicks,
        compiledFinalizeCount, compiledFinalizeTicks,
        rejectFinalizeCount, rejectFinalizeTicks,
        semanticCompileInclusiveCount, semanticCompileInclusiveTicks,
        semanticCountedLoopCount, semanticOptionalIntExpressionCount, semanticExpressionCount,
        scanFinalizeCount, scanFinalizeTicks,
        semanticPayloadMergeFunctionCount, semanticPartCount,
        assignmentSearchCount, assignmentAcceptedCount,
        physicalLineCountScanned, physicalLineBytesScanned,
        emptyOrCommentSkippedLineCount, metadataSkippedLineCount, instructionEmittedCount);
}
