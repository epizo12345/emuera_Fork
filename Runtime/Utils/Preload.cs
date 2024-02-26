using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MinorShift.Emuera.Runtime.Config;

namespace Emuera;
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
                    var value = File.ReadAllLines(childPath, Config.Encode);
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
            var value = File.ReadAllLines(path, Config.Encode);
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