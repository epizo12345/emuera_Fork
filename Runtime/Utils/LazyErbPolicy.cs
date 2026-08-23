using MinorShift.Emuera.Runtime.Config.JSON;
using System;
using System.Collections.Generic;
using System.IO;

namespace MinorShift.Emuera.Runtime.Utils;

internal static class LazyErbPolicy
{
    internal static bool IsEnabledForCurrentMode =>
        JSONConfig.Game?.LazyErb?.Enabled == true
        && !Program.DebugMode
        && !Program.AnalysisMode;

    internal static bool IsConfiguredTargetPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;
        try
        {
            string erbRoot = NormalizeRoot(Program.ErbDir);
            string fullPath = NormalizePath(path, erbRoot);
            if (fullPath == null)
                return false;

            foreach (string configuredDirectory in GetConfiguredDirectories(erbRoot))
            {
                if (IsWithin(fullPath, configuredDirectory))
                    return true;
            }
        }
        catch
        {
            return false;
        }
        return false;
    }

    internal static bool IsActiveTarget(string path) =>
        IsEnabledForCurrentMode && IsConfiguredTargetPath(path);

    static IEnumerable<string> GetConfiguredDirectories(string erbRoot)
    {
        string[] directories = JSONConfig.Game?.LazyErb?.Directories;
        if (directories == null)
            yield break;

        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (string rawDirectory in directories)
        {
            if (string.IsNullOrWhiteSpace(rawDirectory) || Path.IsPathRooted(rawDirectory))
                continue;
            string normalized = rawDirectory.Trim().Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            string fullDirectory;
            try
            {
                fullDirectory = NormalizePath(normalized, erbRoot);
            }
            catch
            {
                continue;
            }
            // ErbDir root itself is deliberately rejected; a broad root target would remove the R39 safety boundary.
            if (fullDirectory == null || fullDirectory == erbRoot || !IsWithin(fullDirectory, erbRoot) || !seen.Add(fullDirectory))
                continue;
            yield return fullDirectory;
        }
    }

    static string NormalizeRoot(string root)
    {
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fullRoot + Path.DirectorySeparatorChar;
    }

    static string NormalizePath(string path, string erbRoot)
    {
        string fullPath = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(erbRoot, path))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return IsWithin(fullPath, erbRoot) ? fullPath : null;
    }

    static bool IsWithin(string path, string directory)
    {
        string normalizedDirectory = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return path.Equals(normalizedDirectory, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(normalizedDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
