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

            var bytes = GC.AllocateUninitializedArray<byte>((int)span.ByteLength);
            using var stream = new FileStream(file.FileIdentity, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                64 * 1024, FileOptions.SequentialScan);
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
}
