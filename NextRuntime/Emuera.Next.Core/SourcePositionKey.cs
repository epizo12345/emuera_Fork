using System.IO;

namespace MinorShift.Emuera.Next.Core;

public static class SourcePositionKey
{
    public static string NormalizePath(string path, string baseDirectory)
    {
        var full = Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(Path.Combine(baseDirectory, path));
        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public static string FromNormalizedPath(string normalizedPath, int line) => $"{normalizedPath}:{line}";

    public static string Create(string path, int line, string baseDirectory) => FromNormalizedPath(NormalizePath(path, baseDirectory), line);

    public static string GetOrNormalize(ref string? cachedPath, string path, string baseDirectory, out bool normalized)
    {
        normalized = cachedPath is null;
        cachedPath ??= NormalizePath(path, baseDirectory);
        return cachedPath;
    }
}
