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
using System.Threading;
using System.Threading.Tasks;

namespace MinorShift.Emuera.Runtime.Utils;
static partial class Preload
{
    // [Emuera改修:START-01]
    // 複数ファイルを同時に読み込むため、読み込み結果の置き場も並列対応にする。
    // キーはファイルパス、値はそのファイルを行ごとに分けた文字列配列。
    // 参照: プロジェクト資料/06_コード案内.md
    static ConcurrentDictionary<string, string[]> files = new(StringComparer.OrdinalIgnoreCase);
#if PERFORMANCE_METRICS
    static int cachedFileCount;
    static int lazySkippedFileCount;

    internal static int CachedFileCount => Volatile.Read(ref cachedFileCount);
    internal static int LazySkippedFileCount => Volatile.Read(ref lazySkippedFileCount);
#endif

    public static string[] GetFileLines(string path)
    {
        return files[path];
    }

    internal static bool TryGetFileLines(string path, out string[] lines)
    {
        return files.TryGetValue(path, out lines);
    }

    internal static string[] ReadFileLines(string path, bool checkUtf8Bom)
    {
        var bytes = File.ReadAllBytes(path).AsSpan();
        if (bytes.IsEmpty)
            return [""];

        var encoding = Config.Config.Encode;
        if (bytes.StartsWith<byte>([0xEF, 0xBB, 0xBF]))
        {
            encoding = Encoding.UTF8;
            bytes = bytes[3..];
        }
        else if (checkUtf8Bom && JSONConfig.Game.CheckUTF8withBOM)
        {
            ParserMediator.ConfigWarn(LocalizationManager.Error.FileNotUTF8BOM, new ScriptPosition(path, 0), 0, "");
        }

        var n = (byte)'\n';
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
                lines.Add("");
            else if (bytes[range].EndsWith([(byte)'\r']))
                lines.Add(encoding.GetString(bytes[range.Start..(range.End.Value - 1)]));
            else
                lines.Add(encoding.GetString(bytes[range]));
        }
        return [.. lines];
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
                    // [Emuera改修:MEM-13R40 2026-08-23]
                    // active Lazy ERBは本文を起動時に全件retainedさせず、index/fallback側のdirect readへ渡す。
                    // ERH/CSVやinactive modeは従来どおりPreloadへ保持し、encoding/BOM判定も変えない。
                    if (childPath.Extension.Equals(".erb", StringComparison.OrdinalIgnoreCase)
                        && LazyErbPolicy.IsActiveTarget(childPath.FullName))
                    {
#if PERFORMANCE_METRICS
                        Interlocked.Increment(ref lazySkippedFileCount);
#endif
                        return;
                    }
                    files[childPath.FullName] = ReadFileLines(childPath.FullName, true);
#if PERFORMANCE_METRICS
                    Interlocked.Increment(ref cachedFileCount);
#endif
                });
            });
        }
        else
        {
            var key = path;
            var value = File.ReadAllLines(path, Config.Config.Encode);
            files[key] = value;
#if PERFORMANCE_METRICS
            Interlocked.Increment(ref cachedFileCount);
#endif
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
#if PERFORMANCE_METRICS
        Volatile.Write(ref cachedFileCount, 0);
        Volatile.Write(ref lazySkippedFileCount, 0);
#endif
    }
}
