using System.Collections.Generic;
using System.IO;
using MinorShift.Emuera;

namespace Emuera;

//TODO:非同期化し、バックグラウンドで読み込むように変更
static class Preload
{
    public static Dictionary<string, string[]> files = new(1000);

    public static void Load(string path)
    {
        var fileAttrs = File.GetAttributes(path);
        if (fileAttrs.HasFlag(FileAttributes.Directory))
        {
            foreach (var childDirPath in Directory.EnumerateFiles(path))
            {
                Load(childDirPath);
            }
            foreach (var childDirPath in Directory.EnumerateDirectories(path))
            {
                Load(childDirPath);
            }
        }
        else
        {
            files.Add(path.ToUpperInvariant(), File.ReadAllLines(path, Config.Encode));
        }
    }

    public static void Clear()
    {
        files.Clear();
    }
}