using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
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
        if (Directory.Exists(path))
        {
            Parallel.ForEach(Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories), (childDirPath) =>
            {
                if (!Directory.Exists(childDirPath))
                {
                    files.Add(childDirPath.ToUpperInvariant(), File.ReadAllText(childDirPath, Config.Encode));
                }
            });
        }
        else
        {
            files.Add(path.ToUpperInvariant(), File.ReadAllText(path, Config.Encode));
        }
    }

    public static void Clear()
    {
        files.Clear();
    }
}