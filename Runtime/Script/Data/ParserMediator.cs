using MinorShift.Emuera.GameView;
using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Runtime.Script.Statements;
using MinorShift.Emuera.Runtime.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Linq;

namespace MinorShift.Emuera;

//1756 新設。ParserやLexicalAnalyzerなどが知りたい情報をまとめる
//本当は引数として渡すべきなのかもしれないが全てのParserの引数を書きなおすのが面倒なのでstatic
internal static partial class ParserMediator
{
    internal sealed record BootstrapWarningSummary(int Count, int KindCount, string IdentitySha256, string KindCounts, string Examples);
    static ParserMediator()
    {
        RenameDic = [];
    }

    /// <summary>
    /// emuera.config等で発生した警告
    /// Initializeより前に発生する
    /// </summary>
    /// <param name="str"></param>
    /// <param name="?"></param>
    public static void ConfigWarn(string str, ScriptPosition? pos, int level, string stack, [CallerMemberName] string source = "")
    {
        if (level < Config.DisplayWarningLevel && !Program.AnalysisMode)
            return;
        Enqueue(new ParserWarning(str, pos, level, stack, source));
    }

    static EmueraConsole console;
    public static void Initialize(EmueraConsole console)
    {
        ParserMediator.console = console;
    }

    #region Rename
    public static Dictionary<string, string> RenameDic { get; private set; }
    //1756 Process.Load.csより移動
    public static void LoadEraExRenameFile(string filepath)
    {
        if (!File.Exists(filepath))
        {
            return;
        }
        if (RenameDic.Count > 0)
            RenameDic.Clear();

        var fileLine = File.ReadAllLines(filepath, Config.Encode);
        ScriptPosition? pos = null;
        Regex regex = unEscapedCommaRegex();
        try
        {
            var lineNo = 0;
            foreach (var line in fileLine)
            {
                pos = new ScriptPosition(filepath, lineNo);
                if (line.StartsWith(';'))
                    continue;
                var tokens = regex.Split(line);
                if (tokens.Length == 2)
                {
                    //右がERB中の表記、左が変換先になる。
                    string key = $"[[{tokens[1].Trim()}]]";
                    string value = tokens[0].Trim();
                    RenameDic[key] = value;
                }
                lineNo++;
            }
        }
        catch (Exception e)
        {
            throw new CodeEE(e.Message, pos);
        }
    }
    #endregion

    public static void Warn(string str, ScriptPosition? pos, int level)
    {
        Warn(str, pos, level, null, "Warn");
    }

    public static void Warn(string str, ScriptPosition? pos, int level, string stack, [CallerMemberName] string source = "")
    {
        if (level < Config.DisplayWarningLevel && !Program.AnalysisMode)
            return;
        if (console != null && !console.RunERBFromMemory)
        {
            Enqueue(new ParserWarning(str, pos, level, stack, source));
        }
    }

    /// <summary>
    /// Parser中での警告出力
    /// </summary>
    /// <param name="str"></param>
    /// <param name="line"></param>
    /// <param name="level">警告レベル.0:軽微なミス.1:無視できる行.2:行が実行されなければ無害.3:致命的</param>
    public static void Warn(string str, LogicalLine line, int level, bool isError, bool isBackComp)
    {
        Warn(str, line, level, isError, isBackComp, null, "WarnLine");
    }

    public static void Warn(string str, LogicalLine line, int level, bool isError, bool isBackComp, string stack, [CallerMemberName] string source = "")
    {
        if (isError)
        {
            line.IsError = true;
            line.ErrMes = str;
        }
        if (level < Config.DisplayWarningLevel && !Program.AnalysisMode)
            return;
        if (isBackComp && !Config.WarnBackCompatibility)
            return;
        if (console != null && !console.RunERBFromMemory)
            Enqueue(new ParserWarning(str, line.Position, level, stack, source));
        //				console.PrintWarning(str, line.Position, level);
    }

    // [Emuera改修:WARN-02]
    // 並列解析中は複数スレッドから警告が届く。ConcurrentQueueなら警告を欠落・破損させず、
    // 解析後に画面側の1か所から順に取り出せる。警告を隠すための変更ではない。
    // 参照: プロジェクト資料/06_コード案内.md
    private static readonly ConcurrentQueue<ParserWarning> warningList = [];
    private static readonly ConcurrentQueue<ParserWarning> bootstrapWarningList = [];
    private static bool captureBootstrapWarnings;

    static void Enqueue(ParserWarning warning)
    {
        warningList.Enqueue(warning);
        if (captureBootstrapWarnings)
            bootstrapWarningList.Enqueue(warning);
    }

    internal static void BeginBootstrapDiagnostics()
    {
        captureBootstrapWarnings = true;
        while (bootstrapWarningList.TryDequeue(out _)) { }
    }

    internal static BootstrapWarningSummary GetBootstrapWarningSummary()
    {
        static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal).Replace("\t", "\\t", StringComparison.Ordinal);
        static string Relative(ScriptPosition? position)
        {
            string path = position?.Filename ?? string.Empty;
            if (Path.IsPathRooted(path) && !string.IsNullOrEmpty(Program.ExeDir))
                path = Path.GetRelativePath(Program.ExeDir, path);
            return path.Replace('\\', '/');
        }

        string[] rows = bootstrapWarningList.Select(warning =>
                $"{warning.Source}\t{warning.WarningLevel}\t{Relative(warning.WarningPos)}\t{warning.WarningPos?.LineNo ?? 0}\t{Escape(warning.WarningMes)}")
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        string[] kinds = bootstrapWarningList.GroupBy(warning => $"{warning.Source}:L{warning.WarningLevel}", StringComparer.Ordinal)
            .Select(group => $"{group.Key}={group.Count()}")
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        return new(rows.Length, kinds.Length, Hash(rows), string.Join('|', kinds), string.Join("\n", rows.Take(5)));
    }

    static string Hash(IEnumerable<string> rows) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', rows))));

    public static bool HasWarning { get { return !warningList.IsEmpty; } }
    public static void FlushWarningList()
    {
        while (warningList.TryDequeue(out ParserWarning warning))
        {
            console.PrintWarning(warning.WarningMes, warning.WarningPos, warning.WarningLevel);
            if (warning.StackTrace != null)
            {
                string[] stacks = warning.StackTrace.Split('\n');
                for (int j = 0; j < stacks.Length; j++)
                {
                    console.PrintSystemLine(stacks[j]);
                }
            }
        }
    }

    private sealed class ParserWarning
    {
        public ParserWarning(string mes, ScriptPosition? pos, int level, string stackTrace, string source)
        {
            WarningMes = mes;
            WarningPos = pos;
            WarningLevel = level;
            StackTrace = stackTrace;
            Source = source;
        }
        public string WarningMes;
        public ScriptPosition? WarningPos;
        public int WarningLevel;
        public string StackTrace;
        public string Source;
    }

    [GeneratedRegex(@"(?<!\\),")]
    private static partial Regex unEscapedCommaRegex();
}
