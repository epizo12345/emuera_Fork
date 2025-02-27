using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Runtime.Config.JSON;
using MinorShift.Emuera.UI.Framework;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Unicode;
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
                    var bytes = File.ReadAllBytes(childPath.FullName).AsSpan();

                    if (bytes.IsEmpty)
                    {
                        files[childPath.FullName] = [""];
                        return;
                    }

                    var encoding = Config.Config.Encode;
                    if (bytes.StartsWith<byte>([0xEF, 0xBB, 0xBF]))
                    {
                        encoding = Encoding.UTF8;
                        bytes = bytes[3..];
                    }
                    else
                    {
                        if (JSONConfig.Game.CheckUTF8withBOM)
                        {
                            ParserMediator.ConfigWarn(LocalizationManager.Error.FileNotUTF8BOM, new ScriptPosition(childPath.FullName, 0), 0, "");
                        }
                    }

                    var lines = new List<string>();
                    var n = (byte)'\n';

                    foreach (var range in ((ReadOnlySpan<byte>)bytes[..]).Split(n))
                    {
                        if (bytes[range].IsEmpty)
                        {
                            lines.Add("");
                        }
                        else
                        {
                            if (bytes[range].EndsWith([(byte)'\r']))
                            {
                                lines.Add(encoding.GetString(bytes[range.Start..(range.End.Value - 1)]));
                            }
                            else
                            {
                                lines.Add(encoding.GetString(bytes[range]));
                            }

                        }
                    }
                    files[childPath.FullName] = [.. lines];
                });
            });
        }
        else
        {
            var key = path;
            var value = File.ReadAllLines(path, Config.Config.Encode);
            files[key] = value;
        }

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