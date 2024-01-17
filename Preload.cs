using System;
using System.Collections.Generic;
using System.IO;
using MinorShift.Emuera;

namespace Emuera;

static class Preload
{
    static readonly Dictionary<string, string[]> files = [];

    public static string[] GetFileLines(string path)
    {
        return files[path];
    }

    public static void Load(string path, Action tickCallback)
    {
        var startTime = DateTime.Now;
        Console.WriteLine($"Load: {path} : Start");
        if (Directory.Exists(path))
        {
            foreach (var childDirPath in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                var text = File.ReadAllLines(childDirPath, Config.Encode);
                files.Add(childDirPath.ToUpperInvariant(), text);

                var elapsedMs = (DateTime.Now - startTime).TotalMilliseconds;
                if (elapsedMs > 100)
                {
                    tickCallback();
                }
            }
        }
        else
        {
            var text = File.ReadAllLines(path, Config.Encode);
            files.Add(path.ToUpperInvariant(), text);
        }
        Console.WriteLine($"Load: {path} : End in {(DateTime.Now - startTime).TotalMilliseconds}ms");
    }

    public static void Clear()
    {
        files.Clear();
    }
}