using System.Diagnostics;

namespace MinorShift.Emuera.Next.Core;

public readonly record struct SemanticCompileObservation(bool PreparedEmpty, int MacroSubstitutionCount, int MacroDefinitionCount, bool RenameResolverPresent);
public readonly record struct CompileCohortSnapshot(long Count, long Ticks);
public readonly record struct OptionalIntExpressionCohortSnapshot(
    CompileCohortSnapshot PreparedEmpty,
    CompileCohortSnapshot PreparedNonEmpty,
    CompileCohortSnapshot MacroSubstitutionZero,
    CompileCohortSnapshot MacroSubstitutionPositive,
    long TotalMacroSubstitutionCount,
    int MaxMacroSubstitutionCountPerCompletedCall,
    long TotalPayloadNodes,
    long TotalPayloadEdges,
    long TotalPayloadSymbols,
    long TotalPayloadCaseArms,
    long TotalPayloadRecords,
    long TotalPayloadHostIdentities,
    long TotalPayloadUtf8Bytes,
    CompileCohortSnapshot NodeCount0,
    CompileCohortSnapshot NodeCount1,
    CompileCohortSnapshot NodeCount2To4,
    CompileCohortSnapshot NodeCount5To8,
    CompileCohortSnapshot NodeCount9To16,
    CompileCohortSnapshot NodeCount17Plus,
    int MacroDefinitionCount,
    bool RenameResolverPresent);

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
    long SemanticCountedLoopCompletedCount,
    long SemanticCountedLoopCompletedTicks,
    long SemanticCountedLoopExceptionCount,
    long SemanticCountedLoopExceptionTicks,
    long SemanticOptionalIntExpressionCompletedCount,
    long SemanticOptionalIntExpressionCompletedTicks,
    long SemanticOptionalIntExpressionExceptionCount,
    long SemanticOptionalIntExpressionExceptionTicks,
    long SemanticExpressionCompletedCount,
    long SemanticExpressionCompletedTicks,
    long SemanticExpressionExceptionCount,
    long SemanticExpressionExceptionTicks,
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
    long InstructionEmittedCount,
    OptionalIntExpressionCohortSnapshot OptionalIntExpressionCohorts);

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
    private static long semanticCountedLoopCompletedCount;
    private static long semanticCountedLoopCompletedTicks;
    private static long semanticCountedLoopExceptionCount;
    private static long semanticCountedLoopExceptionTicks;
    private static long semanticOptionalIntExpressionCompletedCount;
    private static long semanticOptionalIntExpressionCompletedTicks;
    private static long semanticOptionalIntExpressionExceptionCount;
    private static long semanticOptionalIntExpressionExceptionTicks;
    private static long semanticExpressionCompletedCount;
    private static long semanticExpressionCompletedTicks;
    private static long semanticExpressionExceptionCount;
    private static long semanticExpressionExceptionTicks;
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
    private static long optionalPreparedEmptyCount;
    private static long optionalPreparedEmptyTicks;
    private static long optionalPreparedNonEmptyCount;
    private static long optionalPreparedNonEmptyTicks;
    private static long optionalMacroZeroCount;
    private static long optionalMacroZeroTicks;
    private static long optionalMacroPositiveCount;
    private static long optionalMacroPositiveTicks;
    private static long optionalTotalMacroSubstitutions;
    private static int optionalMaxMacroSubstitutions;
    private static long optionalPayloadNodes;
    private static long optionalPayloadEdges;
    private static long optionalPayloadSymbols;
    private static long optionalPayloadCaseArms;
    private static long optionalPayloadRecords;
    private static long optionalPayloadHostIdentities;
    private static long optionalPayloadUtf8Bytes;
    private static long optionalNode0Count;
    private static long optionalNode0Ticks;
    private static long optionalNode1Count;
    private static long optionalNode1Ticks;
    private static long optionalNode2To4Count;
    private static long optionalNode2To4Ticks;
    private static long optionalNode5To8Count;
    private static long optionalNode5To8Ticks;
    private static long optionalNode9To16Count;
    private static long optionalNode9To16Ticks;
    private static long optionalNode17PlusCount;
    private static long optionalNode17PlusTicks;
    private static int optionalMacroDefinitionCount;
    private static bool optionalRenameResolverPresent;
    private static bool optionalContextObserved;

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
        semanticCountedLoopCompletedCount = 0;
        semanticCountedLoopCompletedTicks = 0;
        semanticCountedLoopExceptionCount = 0;
        semanticCountedLoopExceptionTicks = 0;
        semanticOptionalIntExpressionCompletedCount = 0;
        semanticOptionalIntExpressionCompletedTicks = 0;
        semanticOptionalIntExpressionExceptionCount = 0;
        semanticOptionalIntExpressionExceptionTicks = 0;
        semanticExpressionCompletedCount = 0;
        semanticExpressionCompletedTicks = 0;
        semanticExpressionExceptionCount = 0;
        semanticExpressionExceptionTicks = 0;
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
        optionalPreparedEmptyCount = optionalPreparedEmptyTicks = 0;
        optionalPreparedNonEmptyCount = optionalPreparedNonEmptyTicks = 0;
        optionalMacroZeroCount = optionalMacroZeroTicks = 0;
        optionalMacroPositiveCount = optionalMacroPositiveTicks = 0;
        optionalTotalMacroSubstitutions = 0;
        optionalMaxMacroSubstitutions = 0;
        optionalPayloadNodes = optionalPayloadEdges = optionalPayloadSymbols = optionalPayloadCaseArms = 0;
        optionalPayloadRecords = optionalPayloadHostIdentities = optionalPayloadUtf8Bytes = 0;
        optionalNode0Count = optionalNode0Ticks = optionalNode1Count = optionalNode1Ticks = 0;
        optionalNode2To4Count = optionalNode2To4Ticks = optionalNode5To8Count = optionalNode5To8Ticks = 0;
        optionalNode9To16Count = optionalNode9To16Ticks = optionalNode17PlusCount = optionalNode17PlusTicks = 0;
        optionalMacroDefinitionCount = 0;
        optionalRenameResolverPresent = false;
        optionalContextObserved = false;
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
    public static void RecordSemanticCompileInclusive(long ticks, int category, bool completed)
        => RecordSemanticCompileInclusiveCore(ticks, category, completed);

    private static void RecordSemanticCompileInclusiveCore(long ticks, int category, bool completed)
    {
        semanticCompileInclusiveCount++;
        semanticCompileInclusiveTicks += ticks;
        if (category == 1) semanticCountedLoopCount++;
        else if (category == 2) semanticOptionalIntExpressionCount++;
        else semanticExpressionCount++;

        if (category == 1)
        {
            if (completed) { semanticCountedLoopCompletedCount++; semanticCountedLoopCompletedTicks += ticks; }
            else { semanticCountedLoopExceptionCount++; semanticCountedLoopExceptionTicks += ticks; }
        }
        else if (category == 2)
        {
            if (completed) { semanticOptionalIntExpressionCompletedCount++; semanticOptionalIntExpressionCompletedTicks += ticks; }
            else { semanticOptionalIntExpressionExceptionCount++; semanticOptionalIntExpressionExceptionTicks += ticks; }
        }
        else if (completed) { semanticExpressionCompletedCount++; semanticExpressionCompletedTicks += ticks; }
        else { semanticExpressionExceptionCount++; semanticExpressionExceptionTicks += ticks; }
    }

    [Conditional("PERFORMANCE_METRICS")]
    public static void RecordSemanticCompileInclusive(long ticks, int category, bool completed, SemanticCompileObservation observation, int nodes, int edges, int symbols, int caseArms, int records, int hostIdentities, int utf8Bytes)
    {
        RecordSemanticCompileInclusiveCore(ticks, category, completed);
        if (category != 2 || !completed) return;
        if (!optionalContextObserved)
        {
            optionalMacroDefinitionCount = observation.MacroDefinitionCount;
            optionalRenameResolverPresent = observation.RenameResolverPresent;
            optionalContextObserved = true;
        }
        if (observation.PreparedEmpty) { optionalPreparedEmptyCount++; optionalPreparedEmptyTicks += ticks; }
        else { optionalPreparedNonEmptyCount++; optionalPreparedNonEmptyTicks += ticks; }
        if (observation.MacroSubstitutionCount == 0) { optionalMacroZeroCount++; optionalMacroZeroTicks += ticks; }
        else { optionalMacroPositiveCount++; optionalMacroPositiveTicks += ticks; }
        optionalTotalMacroSubstitutions += observation.MacroSubstitutionCount;
        if (observation.MacroSubstitutionCount > optionalMaxMacroSubstitutions) optionalMaxMacroSubstitutions = observation.MacroSubstitutionCount;
        optionalPayloadNodes += nodes; optionalPayloadEdges += edges; optionalPayloadSymbols += symbols;
        optionalPayloadCaseArms += caseArms; optionalPayloadRecords += records;
        optionalPayloadHostIdentities += hostIdentities; optionalPayloadUtf8Bytes += utf8Bytes;
        if (nodes == 0) { optionalNode0Count++; optionalNode0Ticks += ticks; }
        else if (nodes == 1) { optionalNode1Count++; optionalNode1Ticks += ticks; }
        else if (nodes <= 4) { optionalNode2To4Count++; optionalNode2To4Ticks += ticks; }
        else if (nodes <= 8) { optionalNode5To8Count++; optionalNode5To8Ticks += ticks; }
        else if (nodes <= 16) { optionalNode9To16Count++; optionalNode9To16Ticks += ticks; }
        else { optionalNode17PlusCount++; optionalNode17PlusTicks += ticks; }
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
        semanticCountedLoopCompletedCount, semanticCountedLoopCompletedTicks,
        semanticCountedLoopExceptionCount, semanticCountedLoopExceptionTicks,
        semanticOptionalIntExpressionCompletedCount, semanticOptionalIntExpressionCompletedTicks,
        semanticOptionalIntExpressionExceptionCount, semanticOptionalIntExpressionExceptionTicks,
        semanticExpressionCompletedCount, semanticExpressionCompletedTicks,
        semanticExpressionExceptionCount, semanticExpressionExceptionTicks,
        scanFinalizeCount, scanFinalizeTicks,
        semanticPayloadMergeFunctionCount, semanticPartCount,
        assignmentSearchCount, assignmentAcceptedCount,
        physicalLineCountScanned, physicalLineBytesScanned,
        emptyOrCommentSkippedLineCount, metadataSkippedLineCount, instructionEmittedCount,
        new OptionalIntExpressionCohortSnapshot(
            new(optionalPreparedEmptyCount, optionalPreparedEmptyTicks), new(optionalPreparedNonEmptyCount, optionalPreparedNonEmptyTicks),
            new(optionalMacroZeroCount, optionalMacroZeroTicks), new(optionalMacroPositiveCount, optionalMacroPositiveTicks),
            optionalTotalMacroSubstitutions, optionalMaxMacroSubstitutions,
            optionalPayloadNodes, optionalPayloadEdges, optionalPayloadSymbols, optionalPayloadCaseArms, optionalPayloadRecords, optionalPayloadHostIdentities, optionalPayloadUtf8Bytes,
            new(optionalNode0Count, optionalNode0Ticks), new(optionalNode1Count, optionalNode1Ticks), new(optionalNode2To4Count, optionalNode2To4Ticks),
            new(optionalNode5To8Count, optionalNode5To8Ticks), new(optionalNode9To16Count, optionalNode9To16Ticks), new(optionalNode17PlusCount, optionalNode17PlusTicks),
            optionalMacroDefinitionCount, optionalRenameResolverPresent));
}
