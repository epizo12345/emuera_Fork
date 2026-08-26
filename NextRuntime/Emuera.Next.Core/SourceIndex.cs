using System.Buffers;
using System.Text;

namespace MinorShift.Emuera.Next.Core;

[Flags]
public enum SourceIndexFlags
{
    None = 0,
    Preprocessor = 1,
    DeclarationDirective = 2,
    FunctionMetadata = 4,
    Rename = 8,
    LineContinuation = 16,
    OtherSemanticFallback = 32,
    MissingBom = 64,
    InvalidUtf8 = 128,
    ScanError = 256,
}

public readonly record struct SourceSpan(long StartOffset, long EndOffset, int StartLine, int EndLine)
{
    public long ByteLength => EndOffset - StartOffset;
    public int LineCount => EndLine < StartLine ? 0 : EndLine - StartLine + 1;
}

public readonly record struct FunctionIndex(string Name, SourceSpan Span, SourceIndexFlags Flags);

public sealed record SourceFileIndex(
    string FileIdentity,
    long SourceBytes,
    int LineCount,
    IReadOnlyList<FunctionIndex> Functions,
    SourceIndexFlags Flags,
    string? Error)
{
    public bool HasFallback => (Flags & FallbackFlags) != 0 || Functions.Any(static f => (f.Flags & FallbackFlags) != 0);

    public const SourceIndexFlags FallbackFlags = SourceIndexFlags.Preprocessor |
        SourceIndexFlags.DeclarationDirective | SourceIndexFlags.FunctionMetadata |
        SourceIndexFlags.Rename | SourceIndexFlags.LineContinuation | SourceIndexFlags.OtherSemanticFallback;
}

public static class ErbSourceIndexer
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static IReadOnlyList<SourceFileIndex> IndexDirectory(string directory)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException(directory);

        return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(static path => string.Equals(Path.GetExtension(path), ".erb", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(IndexFile)
            .ToArray();
    }

    public static SourceFileIndex IndexFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var identity = Path.GetFullPath(path);
        var sourceBytes = new FileInfo(identity).Length;
        try
        {
            using var stream = new FileStream(identity, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                64 * 1024, FileOptions.SequentialScan);
            using var reader = new ByteLineReader(stream);
            if (reader.ReadByte() != 0xEF || reader.ReadByte() != 0xBB || reader.ReadByte() != 0xBF)
                return new(identity, sourceBytes, 0, Array.Empty<FunctionIndex>(), SourceIndexFlags.MissingBom,
                    "UTF-8 BOM (EF BB BF) is required");

            var functions = new List<FunctionDraft>();
            SourceIndexFlags fileFlags = SourceIndexFlags.None;
            int lineCount = 0;
            FunctionDraft? current = null;
            while (reader.ReadLine(out var lineStart, out _, out var lineBytes))
            {
                lineCount++;
                var bytes = lineBytes.AsSpan();
                try
                {
                    StrictUtf8.GetCharCount(bytes);
                }
                catch (DecoderFallbackException)
                {
                    fileFlags |= SourceIndexFlags.InvalidUtf8;
                    return new(identity, sourceBytes, lineCount, Array.Empty<FunctionIndex>(), fileFlags,
                        $"Invalid UTF-8 at line {lineCount}");
                }

                var trimmed = TrimAsciiStart(bytes);
                if (trimmed.Length > 0 && trimmed[0] == (byte)'@')
                {
                    if (current is not null)
                        current.End(lineStart, lineCount - 1);
                    var header = StrictUtf8.GetString(trimmed);
                    current = new FunctionDraft(FunctionName(header), lineStart, lineCount);
                    functions.Add(current);
                }

                var flags = DetectFlags(trimmed);
                fileFlags |= flags;
                current?.AddFlags(flags);
            }

            current?.End(sourceBytes, lineCount);
            var result = functions.Select(static f => f.ToIndex()).ToArray();
            if ((fileFlags & SourceIndexFlags.Preprocessor) != 0)
            {
                for (var i = 0; i < result.Length; i++)
                    result[i] = result[i] with { Flags = result[i].Flags | SourceIndexFlags.Preprocessor };
            }
            return new(identity, sourceBytes, lineCount, result, fileFlags, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(identity, sourceBytes, 0, Array.Empty<FunctionIndex>(), SourceIndexFlags.ScanError, ex.Message);
        }
    }

    private static ReadOnlySpan<byte> TrimAsciiStart(ReadOnlySpan<byte> bytes)
    {
        var index = 0;
        while (index < bytes.Length && (bytes[index] == (byte)' ' || bytes[index] == (byte)'\t' ||
               bytes[index] == (byte)'\v' || bytes[index] == (byte)'\f')) index++;
        return bytes[index..];
    }

    private static string FunctionName(string header)
    {
        var value = header.AsSpan(1).TrimStart();
        var end = value.IndexOfAny(',', ' ', '\t');
        return (end < 0 ? value : value[..end]).ToString();
    }

    private static SourceIndexFlags DetectFlags(ReadOnlySpan<byte> trimmed)
    {
        SourceIndexFlags flags = SourceIndexFlags.None;
        if (trimmed.Length > 0 && trimmed[0] == (byte)'[' && (trimmed.Length < 2 || trimmed[1] != (byte)'['))
            flags |= SourceIndexFlags.Preprocessor;
        if (Contains(trimmed, "[["u8) || Contains(trimmed, "]]"u8))
            flags |= SourceIndexFlags.Rename;
        if (trimmed.Length > 0 && trimmed[0] == (byte)'#')
            flags |= DirectiveFlag(trimmed[1..]);
        if (trimmed.Length > 0 && (trimmed[0] == (byte)'*' || trimmed[0] == (byte)'$'))
            flags |= SourceIndexFlags.OtherSemanticFallback;
        var end = trimmed.Length;
        while (end > 0 && (trimmed[end - 1] == (byte)' ' || trimmed[end - 1] == (byte)'\t')) end--;
        if (end > 0 && trimmed[end - 1] == (byte)'\\')
            flags |= SourceIndexFlags.LineContinuation;
        return flags;
    }

    private static SourceIndexFlags DirectiveFlag(ReadOnlySpan<byte> token)
    {
        var end = 0;
        while (end < token.Length && token[end] is not ((byte)' ' or (byte)'\t' or (byte)']' or (byte)',')) end++;
        token = token[..end];
        if (AsciiEquals(token, "DIM"u8) || AsciiEquals(token, "DIMS"u8))
            return SourceIndexFlags.DeclarationDirective;
        if (AsciiEquals(token, "FUNCTION"u8) || AsciiEquals(token, "FUNCTIONS"u8) ||
            AsciiEquals(token, "LOCALSIZE"u8) || AsciiEquals(token, "LOCALSSIZE"u8) ||
            AsciiEquals(token, "PRI"u8) || AsciiEquals(token, "LATER"u8) ||
            AsciiEquals(token, "ONLY"u8) || AsciiEquals(token, "SINGLE"u8))
            return SourceIndexFlags.FunctionMetadata;
        return SourceIndexFlags.OtherSemanticFallback;
    }

    private static bool Contains(ReadOnlySpan<byte> value, ReadOnlySpan<byte> target) => value.IndexOf(target) >= 0;

    private static bool AsciiEquals(ReadOnlySpan<byte> value, ReadOnlySpan<byte> target)
    {
        if (value.Length != target.Length) return false;
        for (var i = 0; i < value.Length; i++)
        {
            var left = value[i];
            var right = target[i];
            if (left is >= (byte)'a' and <= (byte)'z') left = (byte)(left - ('a' - 'A'));
            if (right is >= (byte)'a' and <= (byte)'z') right = (byte)(right - ('a' - 'A'));
            if (left != right) return false;
        }
        return true;
    }

    private sealed class FunctionDraft(string name, long startOffset, int startLine)
    {
        private long endOffset;
        private int endLine;
        private SourceIndexFlags flags;
        public void AddFlags(SourceIndexFlags value) => flags |= value & SourceFileIndex.FallbackFlags;
        public void End(long offset, int line) { endOffset = offset; endLine = line; }
        public FunctionIndex ToIndex() => new(name, new SourceSpan(startOffset, endOffset, startLine, endLine), flags);
    }

    private sealed class ByteLineReader(Stream stream) : IDisposable
    {
        private readonly byte[] buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        private byte[] line = new byte[256];
        private int position;
        private int length;
        private long offset;

        public int ReadByte()
        {
            if (position == length)
            {
                length = stream.Read(buffer, 0, buffer.Length);
                position = 0;
                if (length == 0) return -1;
            }
            offset++;
            return buffer[position++];
        }

        public bool ReadLine(out long start, out long end, out ArraySegment<byte> bytes)
        {
            start = offset;
            var count = 0;
            while (true)
            {
                var value = ReadByte();
                if (value < 0)
                {
                    end = offset;
                    bytes = new(line, 0, count);
                    return count != 0;
                }
                if (value == '\n')
                {
                    end = offset;
                    if (count > 0 && line[count - 1] == '\r') count--;
                    bytes = new(line, 0, count);
                    return true;
                }
                if (count == line.Length) Array.Resize(ref line, checked(line.Length * 2));
                line[count++] = (byte)value;
            }
        }

        public void Dispose() => ArrayPool<byte>.Shared.Return(buffer);
    }
}
