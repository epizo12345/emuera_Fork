using MinorShift.Emuera.Runtime.Config.JSON;
using RuntimeConfig = MinorShift.Emuera.Runtime.Config.Config;
using MinorShift.Emuera.Runtime.Config;
using System;
using System.Collections.Generic;
using System.IO;

namespace MinorShift.Emuera.Runtime.Utils;

internal static class LazyErbPolicy
{
    // [Emuera改修:MEM-13R40.3 2026-08-23]
    // Enabled=falseはLazy全体を止める完全なsafety switchとし、Debug/Analysisは従来どおり強制eagerにする。
    // 設定はDataDir相対だが、実際の対象はERB配下だけに限定し、ERB root自身も誤記による全ERB対象化を避けて拒否する。
    internal static bool IsEnabledForCurrentMode =>
        JSONConfig.Game?.LazyErb?.Enabled == true
        && !Program.DebugMode
        && !Program.AnalysisMode
            && RuntimeConfig.IgnoreUncalledFunction
            && !RuntimeConfig.NeedReduceArgumentOnLoad
            && RuntimeConfig.FunctionNotCalledWarning == DisplayWarningFlag.IGNORE;

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

    internal static bool RequiresFullReloadForDirectory(string reloadDirectory, bool searchSubdirectory)
    {
        if (!IsEnabledForCurrentMode || string.IsNullOrWhiteSpace(reloadDirectory))
            return false;
        try
        {
            string erbRoot = NormalizeRoot(Program.ErbDir);
            string reloadRoot = NormalizeAbsolute(reloadDirectory);
            if (!IsWithin(reloadRoot, erbRoot))
                return false;
            foreach (string configuredDirectory in GetConfiguredDirectories(erbRoot))
            {
                if (IsWithin(reloadRoot, configuredDirectory)
                    || searchSubdirectory && IsWithin(configuredDirectory, reloadRoot))
                    return true;
            }
        }
        catch
        {
            return false;
        }
        return false;
    }

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
                fullDirectory = NormalizePath(normalized, NormalizeRoot(Program.ExeDir));
            }
            catch
            {
                continue;
            }
            // [Emuera改修:MEM-13R40.3 2026-08-23]
            // 設定値はDataDir相対で解決した後、ERB root配下かつroot自身ではない場合だけ採用する。
            // 同一表記の重複を除き、directory boundaryをIsWithinで確認して兄弟prefixを対象にしない。
            if (fullDirectory == null
                || string.Equals(fullDirectory, erbRoot, StringComparison.OrdinalIgnoreCase)
                || !IsWithin(fullDirectory, erbRoot)
                || !seen.Add(fullDirectory))
                continue;
            yield return fullDirectory;
        }
    }

    static string NormalizeRoot(string root)
    {
        return NormalizeAbsolute(root);
    }

    static string NormalizePath(string path, string erbRoot)
    {
        string fullPath = NormalizeAbsolute(Path.IsPathRooted(path) ? path : Path.Combine(erbRoot, path));
        return IsWithin(fullPath, erbRoot) ? fullPath : null;
    }

    static string NormalizeAbsolute(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string root = Path.GetPathRoot(fullPath);
        return fullPath.Length == root.Length
            ? fullPath
            : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    static bool IsWithin(string path, string directory)
    {
        if (path.Equals(directory, StringComparison.OrdinalIgnoreCase))
            return true;
        string prefix = directory.EndsWith(Path.DirectorySeparatorChar)
            || directory.EndsWith(Path.AltDirectorySeparatorChar)
            ? directory
            : directory + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
