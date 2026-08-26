using System.Buffers;
using System.Text;

namespace MinorShift.Emuera.Next.Core;

[Flags]
public enum SourceIndexFlags
{
    None = 0,
    MissingBom = 1,
    InvalidUtf8 = 2,
    Preprocessor = 4,
    Rename = 8,
    LineContinuation = 16,
    SpecialLabelOrMetadata = 32,
    ScanError = 64,
}

public readonly record struct SourceSpan(long StartOffset, long EndOffset, int StartLine, int EndLine)
{
    public long ByteLength => EndOffset - StartOffset;
    public int LineCount => EndLine < StartLine ? 0 : EndLine - StartLine + 1;
}

public sealed record FunctionIndex(
    string FileIdentity,
    string Name,
    SourceSpan Span,
    SourceIndexFlags Flags,
    string? FallbackReason);

public sealed record SourceFileIndex(
    string FileIdentity,
    long SourceBytes,
    int LineCount,
    IReadOnlyList<FunctionIndex> Functions,
    SourceIndexFlags Flags,
    string? Error)
{
    public bool HasFallback => (Flags & (SourceIndexFlags.Preprocessor | SourceIndexFlags.Rename |
        SourceIndexFlags.LineContinuation | SourceIndexFlags.SpecialLabelOrMetadata)) != 0 ||
        Functions.Any(static f => f.Flags != SourceIndexFlags.None);
}

public sealed class ErbSourceIndexer
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private const SourceIndexFlags FallbackFlags = SourceIndexFlags.Preprocessor | SourceIndexFlags.Rename |
        SourceIndexFlags.LineContinuation | SourceIndexFlags.SpecialLabelOrMetadata;

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
            while (reader.ReadLine(out var lineStart, out var lineEnd, out var lineBytes))
            {
                lineCount++;
                string line;
                try
                {
                    line = StrictUtf8.GetString(lineBytes.Array!, 0, lineBytes.Count);
                }
                catch (DecoderFallbackException)
                {
                    fileFlags |= SourceIndexFlags.InvalidUtf8;
                    return new(identity, sourceBytes, lineCount, Array.Empty<FunctionIndex>(), fileFlags,
                        $"Invalid UTF-8 at line {lineCount}");
                }

                var trimmed = line.AsSpan().TrimStart();
                if (trimmed.StartsWith("@", StringComparison.Ordinal))
                {
                    if (current is not null)
                        current.End(lineStart, lineCount - 1);
                    current = new FunctionDraft(identity, FunctionName(trimmed), lineStart, lineCount);
                    functions.Add(current);
                }

                var flags = DetectFlags(trimmed, line);
                fileFlags |= flags;
                current?.AddFlags(flags);
            }

            current?.End(sourceBytes, lineCount);
            var result = functions.Select(static f => f.ToIndex()).ToArray();
            return new(identity, sourceBytes, lineCount, result, fileFlags, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(identity, sourceBytes, 0, Array.Empty<FunctionIndex>(), SourceIndexFlags.ScanError, ex.Message);
        }
    }

    private static string FunctionName(ReadOnlySpan<char> header)
    {
        var value = header[1..].TrimStart();
        var end = value.IndexOfAny(',', ' ', '\t');
        return (end < 0 ? value : value[..end]).ToString();
    }

    private static SourceIndexFlags DetectFlags(ReadOnlySpan<char> trimmed, string line)
    {
        SourceIndexFlags flags = SourceIndexFlags.None;
        if (trimmed.StartsWith("#", StringComparison.Ordinal))
        {
            flags |= SourceIndexFlags.Preprocessor;
            if (trimmed.StartsWith("#FUNCTIONS", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("#DIM", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("#DIMS", StringComparison.OrdinalIgnoreCase))
                flags |= SourceIndexFlags.SpecialLabelOrMetadata;
        }
        if (line.Contains("[[", StringComparison.Ordinal) || line.Contains("]]", StringComparison.Ordinal))
            flags |= SourceIndexFlags.Rename;
        if (trimmed.EndsWith("\\", StringComparison.Ordinal))
            flags |= SourceIndexFlags.LineContinuation;
        if (trimmed.StartsWith("*", StringComparison.Ordinal) || trimmed.StartsWith("$", StringComparison.Ordinal))
            flags |= SourceIndexFlags.SpecialLabelOrMetadata;
        return flags;
    }

    private sealed class FunctionDraft(string fileIdentity, string name, long startOffset, int startLine)
    {
        private long endOffset;
        private int endLine;
        private SourceIndexFlags flags;

        public void AddFlags(SourceIndexFlags value) => flags |= value & FallbackFlags;

        public void End(long offset, int line)
        {
            endOffset = offset;
            endLine = line;
        }

        public FunctionIndex ToIndex() => new(fileIdentity, name,
            new SourceSpan(startOffset, endOffset, startLine, endLine), flags, Reasons(flags));
    }

    private static string? Reasons(SourceIndexFlags flags)
    {
        if (flags == SourceIndexFlags.None)
            return null;
        var reasons = new List<string>(4);
        if ((flags & SourceIndexFlags.Preprocessor) != 0) reasons.Add(nameof(SourceIndexFlags.Preprocessor));
        if ((flags & SourceIndexFlags.Rename) != 0) reasons.Add(nameof(SourceIndexFlags.Rename));
        if ((flags & SourceIndexFlags.LineContinuation) != 0) reasons.Add(nameof(SourceIndexFlags.LineContinuation));
        if ((flags & SourceIndexFlags.SpecialLabelOrMetadata) != 0) reasons.Add(nameof(SourceIndexFlags.SpecialLabelOrMetadata));
        return string.Join('|', reasons);
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
