using System.Diagnostics;
using System.Text;
using MinorShift.Emuera.Next.Core;

namespace MinorShift.Emuera.Next.Compiler;

public enum SourceReadStatus
{
    Read,
    SourceChanged,
    InvalidSource,
    ReadError,
}

public readonly record struct FunctionSource(SourceFileIndex File, FunctionIndex Function, byte[] Bytes)
{
    public int Length => Bytes.Length;
}

public sealed record FunctionSourceReadResult(SourceReadStatus Status, FunctionSource? Source, string? Reason)
{
    public static FunctionSourceReadResult Changed(string reason) => new(SourceReadStatus.SourceChanged, null, reason);
}

public sealed class FunctionSourceReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static FunctionSourceReadResult Read(SourceFileIndex file, FunctionIndex function)
    {
        var span = function.Span;
        if (span.StartOffset < 0 || span.EndOffset < span.StartOffset || span.EndOffset > file.SourceBytes || span.ByteLength > int.MaxValue)
            return new(SourceReadStatus.InvalidSource, null, "invalid source span");

        try
        {
            var info = new FileInfo(file.FileIdentity);
            if (info.Length != file.SourceBytes || (file.LastWriteTimeUtcTicks != 0 && info.LastWriteTimeUtc.Ticks != file.LastWriteTimeUtcTicks))
                return FunctionSourceReadResult.Changed("file length or last-write time changed");

            // [Emuera改修:NEXT-1A-R3 2026-08-27]
            // 関数sliceだけをRandomAccessで読む。全体ERBを保持せず、indexed spanとsnapshotの整合性を所有境界にする。
            var bytes = GC.AllocateUninitializedArray<byte>((int)span.ByteLength);
            using var stream = new FileStream(file.FileIdentity, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                1, FileOptions.RandomAccess);
            stream.Seek(span.StartOffset, SeekOrigin.Begin);
            var read = 0;
            while (read < bytes.Length)
            {
                var count = stream.Read(bytes, read, bytes.Length - read);
                if (count == 0)
                    return new(SourceReadStatus.InvalidSource, null, "source ended before indexed span");
                read += count;
            }
            StrictUtf8.GetCharCount(bytes);
            return new(SourceReadStatus.Read, new(file, function, bytes), null);
        }
        catch (DecoderFallbackException ex)
        {
            return new(SourceReadStatus.InvalidSource, null, ex.Message);
        }
        catch (IOException ex)
        {
            return new(SourceReadStatus.ReadError, null, ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return new(SourceReadStatus.ReadError, null, ex.Message);
        }
    }

    // [Emuera改修:NEXT-1A-R3 2026-08-27]
    // CompilerAuditのbatch呼び出しでは同一ERBのFileStreamを共有し、関数ごとのopen/closeと評価順を固定する。
    public static FunctionSourceSession OpenFile(SourceFileIndex file) => new(file);
}

public sealed class FunctionSourceSession : IDisposable
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly SourceFileIndex file;
    private readonly FileStream stream;
    private readonly FileInfo fileInfo;
    private bool disposed;

    internal FunctionSourceSession(SourceFileIndex file)
    {
        this.file = file;
#if PERFORMANCE_METRICS
        var openStart = Stopwatch.GetTimestamp();
#endif
        stream = new(file.FileIdentity, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1, FileOptions.RandomAccess);
#if PERFORMANCE_METRICS
        var fileInfoConstructionStart = Stopwatch.GetTimestamp();
#endif
        fileInfo = new(file.FileIdentity);
#if PERFORMANCE_METRICS
        SourceReaderMetrics.RecordFileInfoObjectConstruction(true, Stopwatch.GetTimestamp() - fileInfoConstructionStart);
#endif
#if PERFORMANCE_METRICS
        var openTicks = Stopwatch.GetTimestamp() - openStart;
        var initialSnapshotStart = Stopwatch.GetTimestamp();
#endif
        var snapshotChanged = IsSnapshotChanged(initial: true);
#if PERFORMANCE_METRICS
        SourceReaderMetrics.RecordSessionOpen(openTicks, Stopwatch.GetTimestamp() - initialSnapshotStart, snapshotChanged);
#endif
        if (snapshotChanged) throw new IOException("file length or last-write time changed");
    }

    public FunctionSourceReadResult Read(FunctionIndex function)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var span = function.Span;
#if PERFORMANCE_METRICS
        var spanValidationStart = Stopwatch.GetTimestamp();
#endif
        var spanValid = span.StartOffset >= 0 && span.EndOffset >= span.StartOffset && span.EndOffset <= file.SourceBytes && span.ByteLength <= int.MaxValue;
#if PERFORMANCE_METRICS
        SourceReaderMetrics.RecordSpanValidation(Stopwatch.GetTimestamp() - spanValidationStart, !spanValid);
#endif
        if (!spanValid)
            return new(SourceReadStatus.InvalidSource, null, "invalid source span");
        try
        {
#if PERFORMANCE_METRICS
            var snapshotCheckStart = Stopwatch.GetTimestamp();
#endif
            var snapshotChanged = IsSnapshotChanged(initial: false);
#if PERFORMANCE_METRICS
            SourceReaderMetrics.RecordReadSnapshotCheck(Stopwatch.GetTimestamp() - snapshotCheckStart, snapshotChanged);
#endif
            if (snapshotChanged) return FunctionSourceReadResult.Changed("file length or last-write time changed");
            // [Emuera改修:NEXT-1A-R3 2026-08-27]
            // sessionの所有streamからindexed spanだけを再利用読み取りし、source bytesは各compile終了後に解放可能にする。
#if PERFORMANCE_METRICS
            var bufferAllocationStart = Stopwatch.GetTimestamp();
#endif
            var bytes = GC.AllocateUninitializedArray<byte>((int)span.ByteLength);
#if PERFORMANCE_METRICS
            SourceReaderMetrics.RecordBufferAllocation(Stopwatch.GetTimestamp() - bufferAllocationStart, bytes.Length);
            var currentPosition = stream.Position;
            var seekStart = Stopwatch.GetTimestamp();
#endif
            stream.Seek(span.StartOffset, SeekOrigin.Begin);
#if PERFORMANCE_METRICS
            SourceReaderMetrics.RecordSeek(Stopwatch.GetTimestamp() - seekStart, currentPosition, span.StartOffset);
            var streamReadStart = Stopwatch.GetTimestamp();
#endif
            var read = 0;
            var streamReadCalls = 0L;
            var totalBytesRequested = 0L;
            var zeroRead = false;
            while (read < bytes.Length)
            {
                var request = bytes.Length - read;
                totalBytesRequested += request;
                var count = stream.Read(bytes, read, request);
                streamReadCalls++;
                if (count == 0) { zeroRead = true; break; }
                read += count;
            }
#if PERFORMANCE_METRICS
            SourceReaderMetrics.RecordStreamRead(Stopwatch.GetTimestamp() - streamReadStart, streamReadCalls, totalBytesRequested, read, zeroRead);
#endif
            if (zeroRead) return new(SourceReadStatus.InvalidSource, null, "source ended before indexed span");
#if PERFORMANCE_METRICS
            var utf8ValidationStart = Stopwatch.GetTimestamp();
#endif
            try { StrictUtf8.GetCharCount(bytes); }
            finally
            {
#if PERFORMANCE_METRICS
                SourceReaderMetrics.RecordUtf8Validation(Stopwatch.GetTimestamp() - utf8ValidationStart, bytes.Length);
#endif
            }
#if PERFORMANCE_METRICS
            var resultConstructionStart = Stopwatch.GetTimestamp();
#endif
            var source = new FunctionSource(file, function, bytes);
            var result = new FunctionSourceReadResult(SourceReadStatus.Read, source, null);
#if PERFORMANCE_METRICS
            SourceReaderMetrics.RecordResultConstruction(Stopwatch.GetTimestamp() - resultConstructionStart);
#endif
            return result;
        }
        catch (DecoderFallbackException ex) { return new(SourceReadStatus.InvalidSource, null, ex.Message); }
        catch (IOException ex) { return new(SourceReadStatus.ReadError, null, ex.Message); }
        catch (UnauthorizedAccessException ex) { return new(SourceReadStatus.ReadError, null, ex.Message); }
    }

    private bool IsSnapshotChanged(bool initial)
    {
#if PERFORMANCE_METRICS
        var fileInfoRefreshStart = Stopwatch.GetTimestamp();
#endif
        fileInfo.Refresh();
#if PERFORMANCE_METRICS
        var fileInfoRefreshTicks = Stopwatch.GetTimestamp() - fileInfoRefreshStart;
        var lengthAccessStart = Stopwatch.GetTimestamp();
#endif
        var length = fileInfo.Length;
#if PERFORMANCE_METRICS
        var lengthAccessTicks = Stopwatch.GetTimestamp() - lengthAccessStart;
        var comparisonStart = Stopwatch.GetTimestamp();
#endif
        var changed = length != file.SourceBytes;
#if PERFORMANCE_METRICS
        var lastWriteTimeUtcAccessed = false;
        var lastWriteTimeUtcAccessTicks = 0L;
#endif
        if (!changed && file.LastWriteTimeUtcTicks != 0)
        {
#if PERFORMANCE_METRICS
            var lastWriteTimeUtcAccessStart = Stopwatch.GetTimestamp();
            lastWriteTimeUtcAccessed = true;
#endif
            changed = fileInfo.LastWriteTimeUtc.Ticks != file.LastWriteTimeUtcTicks;
#if PERFORMANCE_METRICS
            lastWriteTimeUtcAccessTicks = Stopwatch.GetTimestamp() - lastWriteTimeUtcAccessStart;
#endif
        }
#if PERFORMANCE_METRICS
        SourceReaderMetrics.RecordSnapshotCheckParts(initial, fileInfoRefreshTicks, lengthAccessTicks,
            lastWriteTimeUtcAccessTicks, Stopwatch.GetTimestamp() - comparisonStart, lastWriteTimeUtcAccessed);
#endif
        return changed;
    }

    public void Dispose()
    {
        if (!disposed) { disposed = true; stream.Dispose(); }
    }
}
