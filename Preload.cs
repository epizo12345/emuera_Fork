using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
        Console.WriteLine($"Load: {path} : Start");
        if (Directory.Exists(path))
        {
            var filelines = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).AsParallel().Select((childDirPath, _) =>
            {
                var key = string.GetHashCode(childDirPath, StringComparison.OrdinalIgnoreCase);
                return (key: key, value: File.ReadAllLines(childDirPath, Config.Encode));
            });

            foreach (var (key, value) in filelines)
            {
                files.Add(key, value);
            }
        }
        else
        {
            var text = File.ReadAllLines(path, Config.Encode);
            files.Add(string.GetHashCode(path, StringComparison.OrdinalIgnoreCase), text);
        }
        Console.WriteLine($"Load: {path} : End in {(DateTime.Now - startTime).TotalMilliseconds}ms");
    }

    public static void Clear()
    {
        files.Clear();
    }
}