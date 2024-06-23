using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Runtime.Config.JSON;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace MinorShift.Emuera.Runtime.Utils;
static partial class Preload
{
    static ConcurrentDictionary<string, string[]> files = new(StringComparer.OrdinalIgnoreCase);

    public static string[] GetFileLines(string path)
    {
        return files[path];
    }

    public static async Task Load(string path)
    {
        var startTime = DateTime.Now;
        Debug.WriteLine($"Load: {path} : Start");

        var dir = new DirectoryInfo(path);
        if (dir.Exists)
        {
            await Task.Run(() =>
            {
                dir.EnumerateFiles("*", SearchOption.AllDirectories)
                .AsParallel()
                .Where(x =>
                {
                    var ext = x.Extension;
                    return ext.Equals(".csv", StringComparison.OrdinalIgnoreCase) ||
                            ext.Equals(".erb", StringComparison.OrdinalIgnoreCase) ||
                            ext.Equals(".erh", StringComparison.OrdinalIgnoreCase);
                }).ForAll((childPath) =>
                {
                    if (JSONConfig.Game.CheckUTF8withBOM)
                    {
                        using var file = File.OpenRead(childPath.FullName);
                        Span<byte> bom = stackalloc byte[3];
                        _ = file.Read(bom);
                        file.Close();
                        if (!bom.SequenceEqual<byte>([0xEF, 0xBB, 0xBF]))
                        {
                            ParserMediator.ConfigWarn("ファイルが UTF-8 with BOM ではありません", new ScriptPosition(childPath.FullName, 0), 0, "");
                        }
                    }


                    var value = File.ReadAllLines(childPath.FullName, Config.Config.Encode);
                    files[childPath.FullName] = value;
                });
            });
        }
        else
        {
            var key = path;
            var value = File.ReadAllLines(path, Config.Config.Encode);
            files[key] = value;
        };




        Debug.WriteLine($"Load: {path} : End in {(DateTime.Now - startTime).TotalMilliseconds}ms");
    }

    public static async Task Load(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            await Load(path);
        }
    }

    public static void Clear()
    {
        files.Clear();
    }
}