using System;
using System.Collections.Generic;
using System.IO;
using MinorShift.Emuera;

namespace Emuera;

static class Preload
{
    static readonly Dictionary<string, string> files = [];

    public static string[] GetFileLines(string path)
    {
        return files[path].Split('\r', '\n');
    }

    public static void Load(string path)
    {
        var startTime = DateTime.Now;
        Console.WriteLine($"Load: {path} : Start");
        if (Directory.Exists(path))
        {
            foreach (var childDirPath in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                var stream = new StreamReader(childDirPath, Config.Encode);
                var text = stream.ReadToEnd();
                files.Add(childDirPath.ToUpperInvariant(), text);
            }
        }
        else
        {
            var stream = new StreamReader(path, Config.Encode);
            var text = stream.ReadToEnd();
            files.Add(path.ToUpperInvariant(), text);
        }
        Console.WriteLine($"Load: {path} : End in {(DateTime.Now - startTime).TotalMilliseconds}ms");
    }

    public static void Clear()
    {
        files.Clear();
    }
}