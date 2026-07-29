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
    // [Emuera改修:START-01]
    // 複数ファイルを同時に読み込むため、読み込み結果の置き場も並列対応にする。
    // キーはファイルパス、値はそのファイルを行ごとに分けた文字列配列。
    // 参照: プロジェクト資料/06_コード案内.md
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
                // CSV/ERH/ERBは互いに別ファイルなので、ディスクとCPUが許す範囲で同時に先読みする。
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

                    var n = (byte)'\n';
                    // 先に改行数を数えてListの必要容量を確保し、拡張用配列の作り直しを減らす。
                    int lineCount = 1;
                    foreach (byte value in bytes)
                    {
                        if (value == n)
                            lineCount++;
                    }
                    var lines = new List<string>(lineCount);

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
