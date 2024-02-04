using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using MinorShift.Emuera;

namespace Emuera;
static partial class Preload
{
    static Dictionary<int, string[]> files = [];

    public static string[] GetFileLines(ReadOnlySpan<char> path)
    {
        return files[string.GetHashCode(path, StringComparison.OrdinalIgnoreCase)];
    }

    public static void Load(string path)
    {
        var startTime = DateTime.Now;
        Debug.WriteLine($"Load: {path} : Start");

        Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).AsParallel().ForAll((childPath) =>
        {
            var key = string.GetHashCode(childPath, StringComparison.OrdinalIgnoreCase);
            var value = File.ReadAllLines(childPath, Config.Encode);
            lock (files)
            {
                files.TryAdd(key, value);
            }
        });

        Debug.WriteLine($"Load: {path} : End in {(DateTime.Now - startTime).TotalMilliseconds}ms");
    }

    public static void Clear()
    {
        files.Clear();
    }
}