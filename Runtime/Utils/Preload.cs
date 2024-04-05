using MinorShift.Emuera.Runtime.Config;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace MinorShift.Emuera.Runtime.Utils;
static partial class Preload
{
    static Dictionary<string, string[]> files = new(StringComparer.OrdinalIgnoreCase);

    public static string[] GetFileLines(string path)
    {
        return files[path];
    }

    public static async Task Load(string path)
    {
        var startTime = DateTime.Now;
        Debug.WriteLine($"Load: {path} : Start");

        if (Directory.Exists(path))
        {
            await Task.Run(() =>
            {
                Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).AsParallel().ForAll((childPath) =>
                {
                    var key = childPath;
                    if (false)
                    {
                        using var file = File.Open(childPath, FileMode.Open);
                        Span<byte> bom = stackalloc byte[3];
                        _ = file.Read(bom);
                        file.Close();
                        if (!bom.SequenceEqual<byte>([0xEF, 0xBB, 0xBF]))
                        {

                        }
                    }


                    var value = File.ReadAllLines(childPath, Config.Config.Encode);
                    lock (files)
                    {
                        files[key] = value;
                    }
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