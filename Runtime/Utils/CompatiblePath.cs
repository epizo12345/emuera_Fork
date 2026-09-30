using System.IO.Enumeration;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace MinorShift.Emuera.Runtime.Utils;

public static class CompatiblePath
{
    sealed record DirectorySnapshot(long LastWriteTicks, string[] Entries);
    static readonly object snapshotGate = new();
    static readonly Dictionary<string, DirectorySnapshot> directorySnapshots = new(StringComparer.Ordinal);
    const int MaxDirectorySnapshots = 256;

    public static string ResolveExistingFile(string path)
    {
        string fullPath = Path.GetFullPath(path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar));
        // The browser VFS is case-sensitive. Exact package paths are common, and a direct probe
        // avoids walking and enumerating every parent directory for both hits and misses.
        if (OperatingSystem.IsBrowser() && File.Exists(fullPath))
            return fullPath;
        string root = Path.GetPathRoot(fullPath) ?? string.Empty;
        string current = root;
        foreach (string segment in fullPath[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Directory.Exists(current))
                return null;
            string[] matches = GetDirectoryEntries(current)
                .Where(candidate => Path.GetFileName(candidate).Equals(segment, StringComparison.OrdinalIgnoreCase))
                .Take(2)
                .ToArray();
            if (matches.Length == 0)
                return null;
            if (matches.Length > 1)
                throw new IOException($"大小文字を無視すると複数のパスへ一致します: {path}");
            current = matches[0];
        }
        return File.Exists(current) ? current : null;
    }

    static string[] GetDirectoryEntries(string directory)
    {
        long lastWriteTicks = Directory.GetLastWriteTimeUtc(directory).Ticks;
        lock (snapshotGate)
        {
            if (directorySnapshots.TryGetValue(directory, out DirectorySnapshot snapshot) && snapshot.LastWriteTicks == lastWriteTicks)
                return snapshot.Entries;
        }

        string[] entries = Directory.EnumerateFileSystemEntries(directory).ToArray();
        long verifiedLastWriteTicks = Directory.GetLastWriteTimeUtc(directory).Ticks;
        lock (snapshotGate)
        {
            if (directorySnapshots.Count >= MaxDirectorySnapshots)
                directorySnapshots.Clear();
            directorySnapshots[directory] = new(verifiedLastWriteTicks, entries);
            return entries;
        }
    }

    public static string[] GetFiles(string directory, string pattern, SearchOption option = SearchOption.TopDirectoryOnly) =>
        Directory.GetFiles(directory, "*", option)
            .Where(path => FileSystemName.MatchesSimpleExpression(pattern, Path.GetFileName(path), ignoreCase: true))
            .ToArray();
}
