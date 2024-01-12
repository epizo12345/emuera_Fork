using System.Collections.Generic;
using System.IO;
using MinorShift.Emuera;

namespace Emuera;

static class Preload
{
    public static Dictionary<string, string[]> files = [];

    public static void Load(string path)
    {
        var fileAttrs = File.GetAttributes(path);
        if (fileAttrs.HasFlag(FileAttributes.Directory))
        {
            foreach (var filePath in Directory.EnumerateFiles(path))
            {
                files.Add(filePath, File.ReadAllLines(filePath, Config.Encode));
            }
            foreach (var childDirPath in Directory.EnumerateDirectories(path))
            {
                Load(childDirPath);
            }
        }
    }
}