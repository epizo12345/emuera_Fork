namespace MinorShift.Emuera.Next.Core;

public readonly record struct SourceReaderMetricsSnapshot(
    long FileStreamOpenCount,
    long FileStreamOpenTicks,
    long InitialSnapshotCheckCount,
    long InitialSnapshotChangedCount,
    long InitialSnapshotCheckTicks,
    long InitialFileInfoConstructionCount,
    long InitialFileInfoConstructionTicks,
    long InitialLengthAccessCount,
    long InitialLengthAccessTicks,
    long InitialLastWriteTimeUtcAccessCount,
    long InitialLastWriteTimeUtcAccessTicks,
    long InitialSnapshotComparisonCount,
    long InitialSnapshotComparisonTicks,
    long SpanValidationCount,
    long SpanValidationInvalidCount,
    long SpanValidationTicks,
    long ReadSnapshotCheckCount,
    long ReadSnapshotChangedCount,
    long ReadSnapshotCheckTicks,
    long ReadFileInfoConstructionCount,
    long ReadFileInfoConstructionTicks,
    long ReadLengthAccessCount,
    long ReadLengthAccessTicks,
    long ReadLastWriteTimeUtcAccessCount,
    long ReadLastWriteTimeUtcAccessTicks,
    long ReadSnapshotComparisonCount,
    long ReadSnapshotComparisonTicks,
    long BufferAllocationCount,
    long BufferAllocationTicks,
    long BufferAllocatedBytes,
    long SeekCount,
    long SeekAlreadyAtTargetCount,
    long SeekForwardCount,
    long SeekBackwardCount,
    long SeekTicks,
    long SeekAbsoluteDistance,
    long SeekForwardBytes,
    long SeekBackwardBytes,
    long ReadFunctionCount,
    long StreamReadCallCount,
    long MultiReadFunctionCount,
    long ZeroReadCount,
    long TotalBytesRequested,
    long TotalBytesRead,
    long StreamReadTicks,
    long Utf8ValidationCount,
    long Utf8ValidationTicks,
    long Utf8ValidatedBytes,
    long ResultConstructionCount,
    long ResultConstructionTicks);

public static class SourceReaderMetrics
{
    private static long fileStreamOpenCount;
    private static long fileStreamOpenTicks;
    private static long initialSnapshotCheckCount;
    private static long initialSnapshotChangedCount;
    private static long initialSnapshotCheckTicks;
    private static long initialFileInfoConstructionCount;
    private static long initialFileInfoConstructionTicks;
    private static long initialLengthAccessCount;
    private static long initialLengthAccessTicks;
    private static long initialLastWriteTimeUtcAccessCount;
    private static long initialLastWriteTimeUtcAccessTicks;
    private static long initialSnapshotComparisonCount;
    private static long initialSnapshotComparisonTicks;
    private static long spanValidationCount;
    private static long spanValidationInvalidCount;
    private static long spanValidationTicks;
    private static long readSnapshotCheckCount;
    private static long readSnapshotChangedCount;
    private static long readSnapshotCheckTicks;
    private static long readFileInfoConstructionCount;
    private static long readFileInfoConstructionTicks;
    private static long readLengthAccessCount;
    private static long readLengthAccessTicks;
    private static long readLastWriteTimeUtcAccessCount;
    private static long readLastWriteTimeUtcAccessTicks;
    private static long readSnapshotComparisonCount;
    private static long readSnapshotComparisonTicks;
    private static long bufferAllocationCount;
    private static long bufferAllocationTicks;
    private static long bufferAllocatedBytes;
    private static long seekCount;
    private static long seekAlreadyAtTargetCount;
    private static long seekForwardCount;
    private static long seekBackwardCount;
    private static long seekTicks;
    private static long seekAbsoluteDistance;
    private static long seekForwardBytes;
    private static long seekBackwardBytes;
    private static long readFunctionCount;
    private static long streamReadCallCount;
    private static long multiReadFunctionCount;
    private static long zeroReadCount;
    private static long totalBytesRequested;
    private static long totalBytesRead;
    private static long streamReadTicks;
    private static long utf8ValidationCount;
    private static long utf8ValidationTicks;
    private static long utf8ValidatedBytes;
    private static long resultConstructionCount;
    private static long resultConstructionTicks;

    public static void Reset()
    {
        fileStreamOpenCount = 0;
        fileStreamOpenTicks = 0;
        initialSnapshotCheckCount = 0;
        initialSnapshotChangedCount = 0;
        initialSnapshotCheckTicks = 0;
        initialFileInfoConstructionCount = 0;
        initialFileInfoConstructionTicks = 0;
        initialLengthAccessCount = 0;
        initialLengthAccessTicks = 0;
        initialLastWriteTimeUtcAccessCount = 0;
        initialLastWriteTimeUtcAccessTicks = 0;
        initialSnapshotComparisonCount = 0;
        initialSnapshotComparisonTicks = 0;
        spanValidationCount = 0;
        spanValidationInvalidCount = 0;
        spanValidationTicks = 0;
        readSnapshotCheckCount = 0;
        readSnapshotChangedCount = 0;
        readSnapshotCheckTicks = 0;
        readFileInfoConstructionCount = 0;
        readFileInfoConstructionTicks = 0;
        readLengthAccessCount = 0;
        readLengthAccessTicks = 0;
        readLastWriteTimeUtcAccessCount = 0;
        readLastWriteTimeUtcAccessTicks = 0;
        readSnapshotComparisonCount = 0;
        readSnapshotComparisonTicks = 0;
        bufferAllocationCount = 0;
        bufferAllocationTicks = 0;
        bufferAllocatedBytes = 0;
        seekCount = 0;
        seekAlreadyAtTargetCount = 0;
        seekForwardCount = 0;
        seekBackwardCount = 0;
        seekTicks = 0;
        seekAbsoluteDistance = 0;
        seekForwardBytes = 0;
        seekBackwardBytes = 0;
        readFunctionCount = 0;
        streamReadCallCount = 0;
        multiReadFunctionCount = 0;
        zeroReadCount = 0;
        totalBytesRequested = 0;
        totalBytesRead = 0;
        streamReadTicks = 0;
        utf8ValidationCount = 0;
        utf8ValidationTicks = 0;
        utf8ValidatedBytes = 0;
        resultConstructionCount = 0;
        resultConstructionTicks = 0;
    }

    public static void RecordSessionOpen(long openTicks, long snapshotTicks, bool snapshotChanged)
    {
        fileStreamOpenCount++;
        fileStreamOpenTicks += openTicks;
        initialSnapshotCheckCount++;
        initialSnapshotCheckTicks += snapshotTicks;
        if (snapshotChanged) initialSnapshotChangedCount++;
    }

    public static void RecordSpanValidation(long ticks, bool invalid)
    {
        spanValidationCount++;
        spanValidationTicks += ticks;
        if (invalid) spanValidationInvalidCount++;
    }

    public static void RecordReadSnapshotCheck(long ticks, bool changed)
    {
        readSnapshotCheckCount++;
        readSnapshotCheckTicks += ticks;
        if (changed) readSnapshotChangedCount++;
    }

    public static void RecordSnapshotCheckParts(bool initial, long fileInfoConstructionTicks, long lengthAccessTicks,
        long lastWriteTimeUtcAccessTicks, long comparisonTicks, bool lastWriteTimeUtcAccessed)
    {
        if (initial)
        {
            initialFileInfoConstructionCount++;
            initialFileInfoConstructionTicks += fileInfoConstructionTicks;
            initialLengthAccessCount++;
            initialLengthAccessTicks += lengthAccessTicks;
            if (lastWriteTimeUtcAccessed)
            {
                initialLastWriteTimeUtcAccessCount++;
                initialLastWriteTimeUtcAccessTicks += lastWriteTimeUtcAccessTicks;
            }
            initialSnapshotComparisonCount++;
            initialSnapshotComparisonTicks += comparisonTicks;
        }
        else
        {
            readFileInfoConstructionCount++;
            readFileInfoConstructionTicks += fileInfoConstructionTicks;
            readLengthAccessCount++;
            readLengthAccessTicks += lengthAccessTicks;
            if (lastWriteTimeUtcAccessed)
            {
                readLastWriteTimeUtcAccessCount++;
                readLastWriteTimeUtcAccessTicks += lastWriteTimeUtcAccessTicks;
            }
            readSnapshotComparisonCount++;
            readSnapshotComparisonTicks += comparisonTicks;
        }
    }

    public static void RecordBufferAllocation(long ticks, long bytes)
    {
        bufferAllocationCount++;
        bufferAllocationTicks += ticks;
        bufferAllocatedBytes += bytes;
    }

    public static void RecordSeek(long ticks, long currentPosition, long targetPosition)
    {
        seekCount++;
        seekTicks += ticks;
        var distance = targetPosition - currentPosition;
        if (distance == 0) seekAlreadyAtTargetCount++;
        else if (distance > 0) { seekForwardCount++; seekForwardBytes += distance; }
        else { seekBackwardCount++; seekBackwardBytes += -distance; }
        seekAbsoluteDistance += Math.Abs(distance);
    }

    public static void RecordStreamRead(long ticks, long callCount, long requestedBytes, long readBytes, bool zeroRead)
    {
        readFunctionCount++;
        streamReadTicks += ticks;
        streamReadCallCount += callCount;
        if (callCount > 1) multiReadFunctionCount++;
        if (zeroRead) zeroReadCount++;
        totalBytesRequested += requestedBytes;
        totalBytesRead += readBytes;
    }

    public static void RecordUtf8Validation(long ticks, long bytes)
    {
        utf8ValidationCount++;
        utf8ValidationTicks += ticks;
        utf8ValidatedBytes += bytes;
    }

    public static void RecordResultConstruction(long ticks)
    {
        resultConstructionCount++;
        resultConstructionTicks += ticks;
    }

    public static SourceReaderMetricsSnapshot Snapshot() => new(
        fileStreamOpenCount, fileStreamOpenTicks, initialSnapshotCheckCount, initialSnapshotChangedCount, initialSnapshotCheckTicks,
        initialFileInfoConstructionCount, initialFileInfoConstructionTicks, initialLengthAccessCount, initialLengthAccessTicks,
        initialLastWriteTimeUtcAccessCount, initialLastWriteTimeUtcAccessTicks, initialSnapshotComparisonCount, initialSnapshotComparisonTicks,
        spanValidationCount, spanValidationInvalidCount, spanValidationTicks, readSnapshotCheckCount, readSnapshotChangedCount, readSnapshotCheckTicks,
        readFileInfoConstructionCount, readFileInfoConstructionTicks, readLengthAccessCount, readLengthAccessTicks,
        readLastWriteTimeUtcAccessCount, readLastWriteTimeUtcAccessTicks, readSnapshotComparisonCount, readSnapshotComparisonTicks,
        bufferAllocationCount, bufferAllocationTicks, bufferAllocatedBytes, seekCount, seekAlreadyAtTargetCount, seekForwardCount, seekBackwardCount,
        seekTicks, seekAbsoluteDistance, seekForwardBytes, seekBackwardBytes, readFunctionCount, streamReadCallCount, multiReadFunctionCount,
        zeroReadCount, totalBytesRequested, totalBytesRead, streamReadTicks, utf8ValidationCount, utf8ValidationTicks, utf8ValidatedBytes,
        resultConstructionCount, resultConstructionTicks);
}
