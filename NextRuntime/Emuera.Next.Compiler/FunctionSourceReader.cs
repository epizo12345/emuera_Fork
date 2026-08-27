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
    private bool disposed;

    internal FunctionSourceSession(SourceFileIndex file)
    {
        this.file = file;
        stream = new(file.FileIdentity, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1, FileOptions.RandomAccess);
        if (IsSnapshotChanged()) throw new IOException("file length or last-write time changed");
    }

    public FunctionSourceReadResult Read(FunctionIndex function)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var span = function.Span;
        if (span.StartOffset < 0 || span.EndOffset < span.StartOffset || span.EndOffset > file.SourceBytes || span.ByteLength > int.MaxValue)
            return new(SourceReadStatus.InvalidSource, null, "invalid source span");
        try
        {
            if (IsSnapshotChanged()) return FunctionSourceReadResult.Changed("file length or last-write time changed");
            // [Emuera改修:NEXT-1A-R3 2026-08-27]
            // sessionの所有streamからindexed spanだけを再利用読み取りし、source bytesは各compile終了後に解放可能にする。
            var bytes = GC.AllocateUninitializedArray<byte>((int)span.ByteLength);
            stream.Seek(span.StartOffset, SeekOrigin.Begin);
            var read = 0;
            while (read < bytes.Length)
            {
                var count = stream.Read(bytes, read, bytes.Length - read);
                if (count == 0) return new(SourceReadStatus.InvalidSource, null, "source ended before indexed span");
                read += count;
            }
            StrictUtf8.GetCharCount(bytes);
            return new(SourceReadStatus.Read, new(file, function, bytes), null);
        }
        catch (DecoderFallbackException ex) { return new(SourceReadStatus.InvalidSource, null, ex.Message); }
        catch (IOException ex) { return new(SourceReadStatus.ReadError, null, ex.Message); }
        catch (UnauthorizedAccessException ex) { return new(SourceReadStatus.ReadError, null, ex.Message); }
    }

    private bool IsSnapshotChanged()
    {
        var info = new FileInfo(file.FileIdentity);
        return info.Length != file.SourceBytes || (file.LastWriteTimeUtcTicks != 0 && info.LastWriteTimeUtc.Ticks != file.LastWriteTimeUtcTicks);
    }

    public void Dispose()
    {
        if (!disposed) { disposed = true; stream.Dispose(); }
    }
}
