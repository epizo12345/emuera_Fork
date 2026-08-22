using MinorShift.Emuera.GameProc;
using MinorShift.Emuera.GameProc.Function;
using MinorShift.Emuera.GameView;
using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Runtime.Config.JSON;
using MinorShift.Emuera.Runtime.Script.Data;
using MinorShift.Emuera.Runtime.Script.Parser;
using MinorShift.Emuera.Runtime.Script.Statements;
using MinorShift.Emuera.Runtime.Script.Statements.Expression;
using MinorShift.Emuera.Runtime.Script.Statements.Variable;
using MinorShift.Emuera.Runtime.Utils;
using MinorShift.Emuera.Sub;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using MinorShift.Emuera.UI.Framework;
using System.Linq;
using System.Threading;
using System.Collections.Concurrent;

namespace MinorShift.Emuera.Runtime.Script.Loader;

internal sealed class ErbLoader
{
    public ErbLoader(EmueraConsole main, ExpressionMediator exm, Process proc)
    {
        output = main;
        parentProcess = proc;
        this.exm = exm;
    }
    readonly Process parentProcess;
    readonly ExpressionMediator exm;
    readonly EmueraConsole output;
    // [Emuera改修:WARN-06]
    // 並列解析中に「同じファイルの未定義関数警告はまとめる」という情報を共有するための集合。
    // 値のbyteには意味がなく、キーが存在するかだけを見る。
    // 参照: プロジェクト資料/06_コード案内.md
    readonly ConcurrentDictionary<string, byte> ignoredFNFWarningFiles = new(StringComparer.OrdinalIgnoreCase);
    int ignoredFNFWarningCount;

    int enabledLineCount;
    LabelDictionary labelDic;

    // [Emuera改修:MEM-13R39 2026-08-22]
    // 口上まとめだけは起動時に関数/$/宣言のstubを登録し、本文を実行直前まで生成しない。
    // 通常ERBへper-lineの管理情報を追加せず、FunctionLabelLine identityはこの外部表で保持する。
    readonly ConcurrentDictionary<FunctionLabelLine, LazyKojoFile> lazyKojoLabels = [];
    readonly ConcurrentDictionary<string, LazyKojoFile> lazyKojoFiles = new(StringComparer.OrdinalIgnoreCase);
    private int lazyKojoFileCount;
    private int lazyKojoFallbackFileCount;
    private int lazyKojoHydratedFileCount;
    public int LazyKojoFileCount => Volatile.Read(ref lazyKojoFileCount);
    public int LazyKojoFallbackFileCount => Volatile.Read(ref lazyKojoFallbackFileCount);
    public int LazyKojoHydratedFileCount => Volatile.Read(ref lazyKojoHydratedFileCount);

    enum LazyKojoState
    {
        Unloaded,
        Loading,
        Loaded,
        Failed,
    }

    sealed class LazyKojoFile
    {
        public required string FilePath;
        public required string FileName;
        public required int FileIndex;
        public required long Length;
        public required DateTime LastWriteTimeUtc;
        public readonly Dictionary<int, FunctionLabelLine> Functions = [];
        public readonly Dictionary<int, GotoLabelLine> GotoLabels = [];
        public LazyKojoState State;
    }

    // 複数スレッドから更新するため、読み書きはInterlocked/Volatile経由で行う。
    int hasError;
    public long EnumerationMilliseconds { get; private set; }
    public long PrimaryParseMilliseconds { get; private set; }
    public long LabelSetupMilliseconds { get; private set; }
    public long ScriptParseMilliseconds { get; private set; }
    /// <summary>
    /// 複数のファイルを読む
    /// </summary>
    /// <param name="filepath"></param>
    public async Task<bool> LoadErbDir(string erbDir, bool displayReport, LabelDictionary labelDictionary)
    {
        //1.713 labelDicをnewする位置を変更。
        //checkScript();の時点でExpressionPerserがProcess.instance.LabelDicを必要とするから。
        labelDic = labelDictionary;
        labelDic.Initialized = false;
        lazyKojoLabels.Clear();
        lazyKojoFiles.Clear();
        Volatile.Write(ref lazyKojoFileCount, 0);
        Volatile.Write(ref lazyKojoFallbackFileCount, 0);
        Volatile.Write(ref lazyKojoHydratedFileCount, 0);
        var enumerationStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var erbFiles = Config.Config.GetFiles(erbDir, "*.ERB");
        EnumerationMilliseconds = enumerationStopwatch.ElapsedMilliseconds;
        PerformanceMetrics.MarkStartup("ErbEnumerated");
        ConcurrentDictionary<string, byte> isOnlyEvent = new(Config.Config.StrComper);
        hasError = 0;
        var stageStopwatch = System.Diagnostics.Stopwatch.StartNew();
#if DEBUG
        var starttime = System.Diagnostics.Stopwatch.StartNew();
#endif
        try
        {
            labelDic.RemoveAll();

            ConcurrentQueue<string> logQueue = [];

            // [Emuera改修:START-01]
            // ERBは別々のファイルなので複数CPUで同時に読む。ただし完了順は毎回変わるため、
            // 先に元の列挙順番号(FileIndex)を付け、同名関数の優先順が変わらないようにする。
            // 参照: プロジェクト資料/06_コード案内.md
            var indexedErbFiles = erbFiles.Select((erb, index) => (Erb: erb, FileIndex: index + 1)).ToArray();
            var task = Task.Run(() => Parallel.ForEach(indexedErbFiles, item =>
            {
                var erb = item.Erb;
                string filename = erb.Key;
                string file = erb.Value;
                loadErb(file, filename, item.FileIndex, isOnlyEvent);
#if DEBUG
                if (displayReport)
                    logQueue.Enqueue(string.Format(LocalizationManager.SystemLine.ElapsedTimeLoad, starttime.ElapsedMilliseconds, filename));
#else
                if (displayReport)
                    logQueue.Enqueue(string.Format(LocalizationManager.SystemLine.LoadingFile, filename));
#endif
            }));

            var source = new CancellationTokenSource();
            if (displayReport)
            {
                var locks = new Lock();
                await Task.Run(() =>
                {
                    while (source.IsCancellationRequested)
                    {
                        if (logQueue.TryDequeue(out var log))
                        {
                            lock (locks)
                            {
                                output.PrintSystemLine(log);
                            }
                        }
                    }
                }, source.Token);
            }
            await task;
            source.Cancel();
            PrimaryParseMilliseconds = stageStopwatch.ElapsedMilliseconds;


            ParserMediator.FlushWarningList();
#if DEBUG
            output.PrintSystemLine(string.Format(LocalizationManager.SystemLine.ElapsedTime, starttime.ElapsedMilliseconds));
#endif
            if (displayReport)
                output.PrintSystemLine(LocalizationManager.SystemLine.BuildingUserFunc);
            stageStopwatch.Restart();
            setLabelsArg();
            LabelSetupMilliseconds = stageStopwatch.ElapsedMilliseconds;
            ParserMediator.FlushWarningList();
            labelDic.Initialized = true;
#if DEBUG
            output.PrintSystemLine(string.Format(LocalizationManager.SystemLine.ElapsedTime, starttime.ElapsedMilliseconds));
#endif
            if (displayReport)
                output.PrintSystemLine(LocalizationManager.SystemLine.CheckingSyntax);

            stageStopwatch.Restart();
            await Task.Run(() => ParseScript());
            ScriptParseMilliseconds = stageStopwatch.ElapsedMilliseconds;
#if PERFORMANCE_METRICS
            ErbStartupProfiler.SetScriptWallMilliseconds(ScriptParseMilliseconds);
            ErbStartupProfiler.Write();
#endif

            ParserMediator.FlushWarningList();

#if DEBUG
            output.PrintSystemLine(string.Format(LocalizationManager.SystemLine.ElapsedTime, starttime.ElapsedMilliseconds));
#endif
            if (displayReport)
                output.PrintSystemLine(LocalizationManager.SystemLine.LoadComplete);
        }
        catch (Exception e)
        {
            ParserMediator.FlushWarningList();
            System.Media.SystemSounds.Hand.Play();
            output.PrintError(string.Format(LocalizationManager.Error.UnexpectedErrorFrom, AssemblyData.EmueraVersionText));
            output.PrintError(e.GetType().ToString() + ":" + e.Message);
            return false;
        }
        finally
        {
            parentProcess.scaningLine = null;
        }
        isOnlyEvent.Clear();
        return Volatile.Read(ref hasError) == 0;
    }

    /// <summary>
    /// 指定されたファイルを読み込む
    /// </summary>
    /// <param name="filename"></param>
    public async Task<bool> LoadErbList(IEnumerable<string> paths, LabelDictionary labelDictionary)
    {
        string fname;
        ConcurrentDictionary<string, byte> isOnlyEvent = new(Config.Config.StrComper);
        hasError = 0;
        labelDic = labelDictionary;
        labelDic.Initialized = false;


        int fileIndex = 0;
        foreach (var fpath in paths)
        {
            fileIndex++;
            if (fpath.StartsWith(Program.ErbDir, Config.Config.SCIgnoreCase) && !Program.AnalysisMode)
                fname = Path.GetRelativePath(Program.ErbDir, fpath);
            else
                fname = fpath;
            if (Program.AnalysisMode)
            {
                output.PrintSystemLine(string.Format(LocalizationManager.SystemLine.LoadingFile, fname)); ;
            }
            await Task.Run(() =>
            {
                loadErb(fpath, fname, fileIndex, isOnlyEvent);
            });
        }

        if (Program.AnalysisMode)
            output.NewLine();
        ParserMediator.FlushWarningList();
        setLabelsArg();
        ParserMediator.FlushWarningList();
        labelDic.Initialized = true;

        await Task.Run(() => ParseScript());

        ParserMediator.FlushWarningList();
        parentProcess.scaningLine = null;
        isOnlyEvent.Clear();
        return Volatile.Read(ref hasError) == 0;
    }

    private sealed class PPState
    {
        bool skip;
        bool done;
        public bool Disabled;
        readonly Stack<bool> disabledStack = new();
        readonly Stack<bool> doneStack = new();
        readonly Stack<string> ppMatch = new();

        internal void AddKeyWord(string token, string token2, ScriptPosition? position)
        {
            //bool token2enabled = string.IsNullOrEmpty(token2);
            switch (token)
            {
                case "SKIPSTART":
                    if (!string.IsNullOrEmpty(token2))
                    {
                        ParserMediator.Warn(string.Format(LocalizationManager.Error.HasTooManyArg, token), position, 1);
                        break;
                    }
                    if (skip)
                    {
                        ParserMediator.Warn(LocalizationManager.Error.DuplicateSkipstart, position, 1);
                        break;
                    }
                    ppMatch.Push("SKIPEND");
                    disabledStack.Push(Disabled);
                    doneStack.Push(done);
                    skip = true;
                    Disabled = true;
                    done = false;
                    break;
                case "IF_DEBUG":
                    if (!string.IsNullOrEmpty(token2))
                    {
                        ParserMediator.Warn(string.Format(LocalizationManager.Error.HasTooManyArg, token), position, 1);
                        break;
                    }
                    ppMatch.Push("ELSEIF");
                    disabledStack.Push(Disabled);
                    doneStack.Push(done);
                    Disabled = !Program.DebugMode;
                    done = !Disabled;
                    break;
                case "IF_NDEBUG":
                    if (!string.IsNullOrEmpty(token2))
                    {
                        ParserMediator.Warn(string.Format(LocalizationManager.Error.HasTooManyArg, token), position, 1);
                        break;
                    }
                    ppMatch.Push("ELSEIF");
                    disabledStack.Push(Disabled);
                    doneStack.Push(done);
                    Disabled = Program.DebugMode;
                    done = !Disabled;
                    break;
                case "IF":
                    if (string.IsNullOrEmpty(token2))
                    {
                        ParserMediator.Warn(string.Format(LocalizationManager.Error.MissingArguments, token), position, 1);
                        break;
                    }
                    ppMatch.Push("ELSEIF");
                    disabledStack.Push(Disabled);
                    doneStack.Push(done);
                    Disabled = GlobalStatic.IdentifierDictionary.GetMacro(token2) == null;
                    done = !Disabled;
                    break;
                case "ELSEIF":
                    if (string.IsNullOrEmpty(token2))
                    {
                        ParserMediator.Warn(string.Format(LocalizationManager.Error.MissingArguments, token), position, 1);
                        break;
                    }
                    if (ppMatch.Count == 0 || ppMatch.Pop() != "ELSEIF")
                    {
                        ParserMediator.Warn(string.Format(LocalizationManager.Error.IsInvalid, "[ELSEIF]"), position, 1);
                        break;
                    }
                    ppMatch.Push("ELSEIF");
                    Disabled = done || GlobalStatic.IdentifierDictionary.GetMacro(token2) == null;
                    done |= !Disabled;
                    break;
                case "ELSE":
                    if (!string.IsNullOrEmpty(token2))
                    {
                        ParserMediator.Warn(string.Format(LocalizationManager.Error.HasTooManyArg, token), position, 1);
                        break;
                    }
                    if (ppMatch.Count == 0 || ppMatch.Pop() != "ELSEIF")
                    {
                        ParserMediator.Warn(string.Format(LocalizationManager.Error.IsInvalid, "[ELSE]"), position, 1);
                        break;
                    }
                    ppMatch.Push("ENDIF");
                    Disabled = done;
                    done = true;
                    break;

                case "SKIPEND":
                    {
                        if (!string.IsNullOrEmpty(token2))
                        {
                            ParserMediator.Warn(string.Format(LocalizationManager.Error.HasTooManyArg, token), position, 1);
                            break;
                        }
                        string match = ppMatch.Count == 0 ? "" : ppMatch.Pop();
                        if (match != "SKIPEND")
                        {
                            ParserMediator.Warn(LocalizationManager.Error.UnexpectedSkipend, position, 1);
                            break;
                        }
                        skip = false;
                        Disabled = disabledStack.Pop();
                        done = doneStack.Pop();
                    }
                    break;
                case "ENDIF":
                    {
                        if (!string.IsNullOrEmpty(token2))
                        {
                            ParserMediator.Warn(string.Format(LocalizationManager.Error.HasTooManyArg, token), position, 1);
                            break;
                        }
                        string match = ppMatch.Count == 0 ? "" : ppMatch.Pop();
                        if (match != "ENDIF" && match != "ELSEIF")
                        {
                            ParserMediator.Warn(LocalizationManager.Error.UnexpectedMacroEndif, position, 1);
                            break;
                        }
                        Disabled = disabledStack.Pop();
                        done = doneStack.Pop();
                    }
                    break;
                default:
                    ParserMediator.Warn(LocalizationManager.Error.UnrecognizedPreprosessor, position, 1);
                    break;
            }
            if (skip)
                Disabled = true;
        }

        internal void FileEnd(ScriptPosition? position)
        {
            if (ppMatch.Count != 0)
            {
                string match = ppMatch.Pop();
                if (match == "ELSEIF")
                    match = "ENDIF";
                ParserMediator.Warn(string.Format(LocalizationManager.Error.TheresNo, match), position, 1);
            }
        }
    }

    private static bool IsLazyKojoPath(string filename)
    {
        if (Program.AnalysisMode || Program.DebugMode)
            return false;
        string path = filename.Replace('\\', '/');
        return path.StartsWith("口上/口上まとめ/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLazyKojoDangerousLine(string line)
    {
        string trimmed = line.TrimStart();
        if (trimmed.StartsWith("#FUNCTION", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("#PRI", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("#LATER", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("#ONLY", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("#SINGLE", StringComparison.OrdinalIgnoreCase))
            return true;
        if (!trimmed.StartsWith('@'))
            return false;
        try
        {
            CharStream stream = new(trimmed);
            stream.ShiftNext();
            string labelName = LexicalAnalyzer.ReadSingleIdentifier(stream);
            return IdentifierDictionary.IsEventLabelName(labelName)
                || IdentifierDictionary.IsSystemLabelName(labelName);
        }
        catch
        {
            return true;
        }
    }

    private static bool IsLazyKojoSafe(string filepath)
    {
        try
        {
            using EraStreamReader reader = new(Config.Config.UseRenameFile && ParserMediator.RenameDic != null);
            if (!reader.Open(filepath, Path.GetFileName(filepath)))
                return false;
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (IsLazyKojoDangerousLine(line))
                    return false;
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool TryLoadLazyErb(string filepath, string filename, int fileIndex, ConcurrentDictionary<string, byte> isOnlyEvent)
    {
        // [Emuera改修:MEM-13R39 2026-08-22]
        // Program.ErbDir基準の相対pathで口上まとめだけを選び、関数名・引数・#DIM等のmetadataを
        // 起動時に既存parserでindexする。危険な構造は従来eagerへ戻し、通常ERBへper-line overheadを加えない。
        if (!IsLazyKojoPath(filename))
            return false;
        if (!IsLazyKojoSafe(filepath))
        {
            Interlocked.Increment(ref lazyKojoFallbackFileCount);
            return false;
        }

        LazyKojoFile file = new()
        {
            FilePath = filepath,
            FileName = filename,
            FileIndex = labelDic.RegisterFile(filename, fileIndex),
            Length = new FileInfo(filepath).Length,
            LastWriteTimeUtc = File.GetLastWriteTimeUtc(filepath),
            State = LazyKojoState.Unloaded,
        };
        if (!BuildLazyIndex(file, isOnlyEvent))
        {
            Interlocked.Exchange(ref hasError, 1);
            return true;
        }
        lazyKojoFiles[filename] = file;
        Interlocked.Increment(ref lazyKojoFileCount);
        return true;
    }

    private bool BuildLazyIndex(LazyKojoFile file, ConcurrentDictionary<string, byte> isOnlyEvent)
    {
        using var eReader = new EraStreamReader(Config.Config.UseRenameFile && ParserMediator.RenameDic != null);
        if (!eReader.OpenOnCache(file.FilePath, file.FileName))
            return false;

        PPState ppstate = new();
        LogicalLine nextLine = new NullLine();
        LogicalLine lastLine = new NullLine();
        FunctionLabelLine lastLabelLine = null;
        CharStream st;
        while ((st = eReader.ReadEnabledLine(ppstate.Disabled)) != null)
        {
            ScriptPosition position = new(eReader.FileId, eReader.LineNo);
            if (st.Current == '[' && st.Next != '[')
            {
                st.ShiftNext();
                string token = LexicalAnalyzer.ReadSingleIdentifier(st);
                LexicalAnalyzer.SkipWhiteSpace(st);
                string token2 = LexicalAnalyzer.ReadSingleIdentifier(st);
                ppstate.AddKeyWord(token, token2, position);
                continue;
            }
            if (ppstate.Disabled)
                continue;
            if (st.Current == '#')
            {
                if (lastLine is not FunctionLabelLine funcLine)
                    return false;
                if (!LogicalLineParser.ParseSharpLine(funcLine, st, position, isOnlyEvent))
                    Interlocked.Exchange(ref hasError, 1);
                continue;
            }
            if (st.Current == '$' || st.Current == '@')
            {
                bool isFunction = st.Current == '@';
                nextLine = LogicalLineParser.ParseLabelLine(st, position, output);
                if (isFunction)
                {
                    if (nextLine is not FunctionLabelLine label || label.IsEvent || label.IsSystem)
                        return false;
                    lastLabelLine = label;
                    file.Functions[eReader.LineNo] = label;
                    labelDic.AddLabel(label, file.FileIndex);
                    lazyKojoLabels[label] = file;
                }
                else if (nextLine is GotoLabelLine gotoLabel)
                {
                    gotoLabel.ParentLabelLine = lastLabelLine;
                    file.GotoLabels[eReader.LineNo] = gotoLabel;
                    if (lastLabelLine != null && !labelDic.AddLabelDollar(gotoLabel))
                        ParserMediator.Warn(LocalizationManager.Error.LabelIsAlreadyDefined, position, 2);
                }
                else
                    return false;
                nextLine.ParentLabelLine = lastLabelLine;
                lastLine.NextLine = nextLine;
                lastLine = nextLine;
                continue;
            }
            // 本文InstructionLineは作らず、次のstub/ファイル終端へだけchainをつなぐ。
        }
        ppstate.FileEnd(new ScriptPosition(eReader.FileId, -1));
        lastLine.NextLine = new NullLine();
        return true;
    }

    public bool EnsureLazyLoaded(FunctionLabelLine label)
    {
        if (!lazyKojoLabels.TryGetValue(label, out LazyKojoFile file))
            return true;
        if (file.State == LazyKojoState.Loaded)
            return true;
        if (file.State == LazyKojoState.Loading || file.State == LazyKojoState.Failed)
            return file.State == LazyKojoState.Loading;
        file.State = LazyKojoState.Loading;
        try
        {
            if (new FileInfo(file.FilePath).Length != file.Length
                || File.GetLastWriteTimeUtc(file.FilePath) != file.LastWriteTimeUtc)
                throw new CodeEE("口上まとめのLazy対象ERBが起動後に変更されました。コードを再読込してください。");
            if (!HydrateLazyFile(file))
                throw new CodeEE("口上まとめERBのLazy hydrationに失敗しました。");
            file.State = LazyKojoState.Loaded;
            Interlocked.Increment(ref lazyKojoHydratedFileCount);
            WriteLazyDiagnostic($"Loaded\t{file.FileName}\tfunctions={file.Functions.Count}\tgotos={file.GotoLabels.Count}");
            return true;
        }
        catch (Exception e)
        {
            file.State = LazyKojoState.Failed;
            WriteLazyDiagnostic($"Failed\t{file.FileName}\t{e.Message}");
            ParserMediator.Warn(e.Message, label, 2, true, false);
            return false;
        }
    }

    private bool HydrateLazyFile(LazyKojoFile file)
    {
        // [Emuera改修:MEM-13R39 2026-08-22]
        // 初回実行はERB 1ファイル単位で既存stubへ本文chainを接続する。Preload.Clear後も動くよう、
        // 起動時cache(OpenOnCache)を使わず現ファイルを直接開き、index時の長さ・更新時刻と照合する。
        using var eReader = new EraStreamReader(Config.Config.UseRenameFile && ParserMediator.RenameDic != null);
        if (!eReader.Open(file.FilePath, file.FileName))
            return false;
        PPState ppstate = new();
        LogicalLine lastLine = null;
        FunctionLabelLine currentLabel = null;
        CharStream st;
        List<(LogicalLine From, LogicalLine To)> links = [];
        List<(GotoLabelLine Label, FunctionLabelLine Parent)> gotoParents = [];
        while ((st = eReader.ReadEnabledLine(ppstate.Disabled)) != null)
        {
            ScriptPosition position = new(eReader.FileId, eReader.LineNo);
            if (st.Current == '[' && st.Next != '[')
            {
                st.ShiftNext();
                string token = LexicalAnalyzer.ReadSingleIdentifier(st);
                LexicalAnalyzer.SkipWhiteSpace(st);
                string token2 = LexicalAnalyzer.ReadSingleIdentifier(st);
                ppstate.AddKeyWord(token, token2, position);
                continue;
            }
            if (ppstate.Disabled)
                continue;
            if (st.Current == '#')
                continue;
            LogicalLine nextLine;
            if (st.Current == '@')
            {
                LogicalLine parsed = LogicalLineParser.ParseLabelLine(st, position, output);
                if (parsed is not FunctionLabelLine || !file.Functions.TryGetValue(eReader.LineNo, out currentLabel))
                    return false;
                nextLine = currentLabel;
            }
            else if (st.Current == '$')
            {
                LogicalLine parsed = LogicalLineParser.ParseLabelLine(st, position, output);
                if (parsed is not GotoLabelLine || !file.GotoLabels.TryGetValue(eReader.LineNo, out GotoLabelLine gotoLabel))
                    return false;
                nextLine = gotoLabel;
                gotoParents.Add((gotoLabel, currentLabel));
            }
            else
            {
                nextLine = LogicalLineParser.ParseLine(st, position, output, currentLabel);
                if (nextLine == null)
                    continue;
                nextLine.ParentLabelLine = currentLabel;
            }
            if (lastLine != null)
                links.Add((lastLine, nextLine));
            lastLine = nextLine;
        }
        if (lastLine == null)
            return false;
        links.Add((lastLine, new NullLine()));
        foreach ((LogicalLine from, LogicalLine to) in links)
            from.NextLine = to;
        foreach ((GotoLabelLine label, FunctionLabelLine parent) in gotoParents)
            label.ParentLabelLine = parent;
        foreach (FunctionLabelLine function in file.Functions.Values)
            ParseFunctionWithCatch(function);
        return true;
    }

    private bool IsLazyLabel(FunctionLabelLine label) => lazyKojoLabels.ContainsKey(label);

    private static void WriteLazyDiagnostic(string message)
    {
        try
        {
            File.AppendAllText(Path.Combine(Program.ExeDir, "phase13r39-lazy-kojo.log"),
                $"{DateTime.UtcNow:O}\t{message}{Environment.NewLine}");
        }
        catch
        {
            // 診断ログは試験補助であり、実行結果を変えない。
        }
    }

    /// <summary>
    /// ファイル一つを読む
    /// </summary>
    /// <param name="filepath"></param>
    private void loadErb(string filepath, string filename, int fileIndex, ConcurrentDictionary<string, byte> isOnlyEvent)
    {
        if (TryLoadLazyErb(filepath, filename, fileIndex, isOnlyEvent))
            return;
#if PERFORMANCE_METRICS
        ErbStartupFileProfile profile = ErbStartupProfiler.BeginFile(filename);
#endif
        //一部ファイルの再読み込み時の処理用
        fileIndex = labelDic.RegisterFile(filename, fileIndex);
        using var eReader = new EraStreamReader(Config.Config.UseRenameFile && ParserMediator.RenameDic != null
#if PERFORMANCE_METRICS
            , profile
#endif
            );

        if (!eReader.OpenOnCache(filepath, filename))
        {
            output.PrintError(string.Format(LocalizationManager.Error.FailedOpenFile, eReader.Filename));
        }
        var ppstate = new PPState();
        LogicalLine nextLine = new NullLine();
        LogicalLine lastLine = new NullLine();
        FunctionLabelLine lastLabelLine = null;
        CharStream st = null;
        ScriptPosition? position = null;
        int funcCount = 0;
        if (Program.AnalysisMode)
            output.PrintSystemLine("　");
        while ((st = eReader.ReadEnabledLine(ppstate.Disabled)) != null)
        {
#if PERFORMANCE_METRICS
            if (profile != null)
                profile.ReadEnabledLineReturns++;
#endif
            position = new ScriptPosition(eReader.FileId, eReader.LineNo);
            //rename処理をEraStreamReaderに移管
            //変換できなかった[[～～]]についてはLexAnalyzerがエラーを投げる
            if (st.Current == '[' && st.Next != '[')
            {
#if PERFORMANCE_METRICS
                if (profile != null)
                    profile.PreprocessorLines++;
#endif
                st.ShiftNext();
                string token = LexicalAnalyzer.ReadSingleIdentifier(st);
                LexicalAnalyzer.SkipWhiteSpace(st);
                string token2 = LexicalAnalyzer.ReadSingleIdentifier(st);
                if (string.IsNullOrEmpty(token) || st.Current != ']')
                    ParserMediator.Warn(LocalizationManager.Error.InvalidSBrackets, position, 1);
                ppstate.AddKeyWord(token, token2, position);
                st.ShiftNext();
                if (!st.EOS)
                    ParserMediator.Warn(string.Format(LocalizationManager.Error.IgnoreAfterPreprosessor, token), position, 1);
                continue;
            }
            //if ((skip) || (Program.DebugMode && ifndebug) || (!Program.DebugMode && ifdebug))
            //	continue;
            if (ppstate.Disabled)
                continue;
            //ここまでプリプロセッサ

            if (st.Current == '#')
            {
#if PERFORMANCE_METRICS
                if (profile != null)
                    profile.SharpLines++;
#endif
                if (lastLine == null || lastLine is not FunctionLabelLine funcLine)
                {
                    ParserMediator.Warn(LocalizationManager.Error.InvalidSharp, position, 1);
                    continue;
                }
                bool sharpResult = LogicalLineParser.ParseSharpLine(funcLine, st, position, isOnlyEvent);
                if (!sharpResult)
                    // 並列中の単純な hasError = 1 は競合し得るので、確実に1を書き込む。
                    Interlocked.Exchange(ref hasError, 1);
                continue;
            }
            if (st.Current == '$' || st.Current == '@')
            {
#if PERFORMANCE_METRICS
                if (profile != null)
                    profile.LabelLines++;
#endif
                bool isFunction = st.Current == '@';
                nextLine = LogicalLineParser.ParseLabelLine(st, position, output);
                if (isFunction)
                {
                    var label = nextLine as FunctionLabelLine;
                    lastLabelLine = label;
                    if (label is InvalidLabelLine)
                    {
                        Interlocked.Exchange(ref hasError, 1);
                        ParserMediator.Warn(nextLine.ErrMes, position, 2);
                        labelDic.AddInvalidLabel(label);
                    }
                    else// if (label is FunctionLabelLine)
                    {
                        labelDic.AddLabel(label, fileIndex);
                        if (!label.IsEvent && (Config.Config.WarnNormalFunctionOverloading || Program.AnalysisMode))
                        {
                            FunctionLabelLine seniorLabel = labelDic.GetSameNameLabel(label);
                            if (seniorLabel != null)
                            {
                                //output.NewLine();
                                ParserMediator.Warn(string.Format(LocalizationManager.Error.FuncIsAlreadyDefined, label.LabelName, seniorLabel.Position.Value.Filename, seniorLabel.Position.Value.LineNo.ToString()), position, 1);
                                funcCount = -1;
                            }
                        }
                        funcCount++;
                        if (Program.AnalysisMode && Config.Config.PrintCPerLine > 0 && funcCount % Config.Config.PrintCPerLine == 0)
                        {
                            output.NewLine();
                            output.PrintSystemLine("　");
                        }
                    }
                }
                else
                {
                    if (nextLine is GotoLabelLine gotoLabel)
                    {
                        gotoLabel.ParentLabelLine = lastLabelLine;
                        if (lastLabelLine != null && !labelDic.AddLabelDollar(gotoLabel))
                        {
                            ScriptPosition? pos = labelDic.GetLabelDollar(gotoLabel.LabelName, lastLabelLine).Position;
                            ParserMediator.Warn(string.Format(LocalizationManager.Error.LabelIsAlreadyDefined, gotoLabel.LabelName, pos.Value.Filename, pos.Value.LineNo.ToString()), position, 2);
                        }
                    }
                }
                if (nextLine is InvalidLine)
                {
                    Interlocked.Exchange(ref hasError, 1);
                    ParserMediator.Warn(nextLine.ErrMes, position, 2);
                }
            }
            else
            {
#if PERFORMANCE_METRICS
                if (profile != null)
                    profile.ScriptLines++;
#endif
                //1808alpha006 処理位置変更
                ////全置換はここで対応
                ////1756beta1+++　最初に全置換してしまうと関数定義を_Renameでとか論外なことができてしまうので永久封印した
                //if (ParserMediator.RenameDic != null && st.CurrentEqualTo("[[") && (rowLine.TrimEnd().IndexOf("]]") == rowLine.TrimEnd().Length - 2))
                //{
                //    string replacedLine = st.Substring();
                //    foreach (KeyValuePair<string, string> pair in ParserMediator.RenameDic)
                //        replacedLine = replacedLine.Replace(pair.Key, pair.Value);
                //    st = new StringStream(replacedLine);
                //}
                if (lastLabelLine == null)
                    ParserMediator.Warn(LocalizationManager.Error.LineBeforeFunc, position, 1);
                nextLine = LogicalLineParser.ParseLine(st, position, output, lastLabelLine);
                if (nextLine == null)
                    continue;
                if (nextLine is InvalidLine)
                {
                    Interlocked.Exchange(ref hasError, 1);
                    ParserMediator.Warn(nextLine.ErrMes, position, 2);
                }
                else if (JSONConfig.Game.UseNewRandom &&
                    nextLine is InstructionLine instruction)
                {
                    switch (instruction.FunctionCode)
                    {
                        case FunctionCode.RANDOMIZE:
                            ParserMediator.Warn(LocalizationManager.Error.IgnoreRandomize, position, 0);
                            break;
                        case FunctionCode.DUMPRAND:
                            ParserMediator.Warn(LocalizationManager.Error.CanNotUseDumprand, position, 0);
                            break;
                        case FunctionCode.INITRAND:
                            ParserMediator.Warn(LocalizationManager.Error.CanNotUseInitrand, position, 0);
                            break;
                        default:
                            break;
                    }
                }
            }

            nextLine.ParentLabelLine = lastLabelLine;

#if PERFORMANCE_METRICS
            if (profile != null)
                profile.LogicalLines++;
#endif
            lastLine = addLine(nextLine, lastLine);
        }
        addLine(new NullLine(), lastLine);
        position = new ScriptPosition(eReader.FileId, -1);
        ppstate.FileEnd(position);
#if PERFORMANCE_METRICS
        ErbStartupProfiler.CompleteFile(profile);
#endif
        return;
    }

    private LogicalLine addLine(LogicalLine nextLine, LogicalLine lastLine)
    {
        if (nextLine == null)
            return null;
        Interlocked.Increment(ref enabledLineCount);
        lastLine.NextLine = nextLine;
        return nextLine;
    }

    private void setLabelsArg()
    {
        List<FunctionLabelLine> labelList = labelDic.GetAllLabels(false);
        if (Program.AnalysisMode || labelList.Count < 2)
        {
            foreach (FunctionLabelLine label in labelList)
                ParseLabelWithCatch(label);
        }
        else
        {
            // [Emuera改修:START-02]
            // 各関数の「引数定義」は独立しているため同時解析できる。
            // 解析中行はProcess側でスレッド別に保持し、偽の行番号・偽警告を防ぐ。
            parentProcess.SetParallelScanning(true);
            try
            {
                Parallel.ForEach(labelList, ParseLabelWithCatch);
            }
            finally
            {
                parentProcess.SetParallelScanning(false);
            }
        }
        labelDic.SortLabels();
    }

    private void ParseLabelWithCatch(FunctionLabelLine label)
    {
        try
        {
            if (label.Arg != null)
                return;
            parentProcess.scaningLine = label;
            parseLabel(label);
        }
        catch (Exception exc)
        {
            System.Media.SystemSounds.Hand.Play();
            string errmes = exc.Message;
            if (exc is not EmueraException)
                errmes = exc.GetType().ToString() + ":" + errmes;
            ParserMediator.Warn(string.Format(LocalizationManager.Error.FuncArgError, label.LabelName, errmes), label, 2, true, false);
            label.ErrMes = LocalizationManager.Error.CalledFailedFunc;
            label.IsError = true;
        }
        finally
        {
            parentProcess.scaningLine = null;
        }
    }

    private void parseLabel(FunctionLabelLine label)
    {
        WordCollection wc = label.PopRowArgs();
        string errMes;
        SingleTerm[] subNames;
        VariableTerm[] args = [];
        SingleTerm[] defs = [];
        int maxArg = -1;
        int maxArgs = -1;
        //1807 非イベント関数のシステム関数については警告レベル低下＆エラー解除＆引数を設定するように。
        if (label.IsEvent)
        {
            if (!wc.EOL)
                ParserMediator.Warn(string.Format(LocalizationManager.Error.EventFuncHasArg, label.LabelName), label, 2, true, false);
            //label.SubNames = subNames;
            label.Arg = args;
            label.Def = defs;
            label.ArgLength = -1;
            label.ArgsLength = -1;
            return;
        }

        if (!wc.EOL)
        {
            if (label.IsSystem)
                ParserMediator.Warn(string.Format(LocalizationManager.Error.SystemFuncHasArg, label.LabelName), label, 2, true, false);
            SymbolWord symbol = wc.Current as SymbolWord;
            wc.ShiftNext();
            if (symbol == null)
            { errMes = LocalizationManager.Error.WrongArgFormat; goto err; }
            if (symbol.Type == '[')//TODO:subNames 結局実装しないかも
            {
                var subNamesRow = ExpressionParser.ReduceArguments(wc, ArgsEndWith.RightBracket, false);
                if (subNamesRow.Count == 0)
                { errMes = LocalizationManager.Error.CanNotEmptyFuncSBrackets; goto err; }
                subNames = new SingleTerm[subNamesRow.Count];
                for (int i = 0; i < subNamesRow.Count; i++)
                {
                    if (subNamesRow[i] == null)
                    { errMes = LocalizationManager.Error.CannotOmitFuncArg; goto err; }
                    AExpression term = subNamesRow[i].Restructure(exm);
                    subNames[i] = term as SingleTerm;
                    if (subNames[i] == null)
                    { errMes = LocalizationManager.Error.FuncDefineArgOnlyConst; goto err; }
                }
                symbol = wc.Current as SymbolWord;
                if (!wc.EOL && symbol == null)
                { errMes = LocalizationManager.Error.WrongArgFormat; goto err; }
                wc.ShiftNext();
            }
            if (!wc.EOL)
            {
                List<AExpression> argsRow;
                if (symbol.Type == ',')
                    argsRow = ExpressionParser.ReduceArguments(wc, ArgsEndWith.EoL, true);
                else if (symbol.Type == '(')
                    argsRow = ExpressionParser.ReduceArguments(wc, ArgsEndWith.RightParenthesis, true);
                else
                { errMes = LocalizationManager.Error.WrongArgFormat; goto err; }
                int length = argsRow.Count / 2;
                args = new VariableTerm[length];
                defs = new SingleTerm[length];
                for (int i = 0; i < length; i++)
                {
                    SingleTerm def = null;
                    AExpression term = argsRow[i * 2];
                    //引数読み取り時点で判別されないといけない
                    //if (term == null)
                    //{ errMes = "関数定義の引数は省略できません"; goto err; }
                    if (!(term.Restructure(exm) is VariableTerm vTerm) || vTerm.Identifier.IsConst)
                    { errMes = LocalizationManager.Error.ArgCanOnlyAssignableVar; goto err; }
                    else if (!vTerm.Identifier.IsReference)//参照型なら添え字不要
                    {
                        if (vTerm is VariableNoArgTerm)
                        { errMes = string.Format(LocalizationManager.Error.ArgHasNotSubscript, vTerm.Identifier.Name); goto err; }
                        if (!vTerm.isAllConst)
                        { errMes = LocalizationManager.Error.ArgSubscriptOnlyConst; goto err; }
                    }
                    for (int j = 0; j < i; j++)
                    {
                        if (vTerm.checkSameTerm(args[j]))
                            ParserMediator.Warn(string.Format(LocalizationManager.Error.DuplicateArg, i + 1, vTerm.GetFullString(), j + 1), label, 1, false, false);
                    }
                    if (vTerm.Identifier.Code == VariableCode.ARG)
                    {
                        if (maxArg < vTerm.getEl1forArg + 1)
                            maxArg = vTerm.getEl1forArg + 1;
                    }
                    else if (vTerm.Identifier.Code == VariableCode.ARGS)
                    {
                        if (maxArgs < vTerm.getEl1forArg + 1)
                            maxArgs = vTerm.getEl1forArg + 1;
                    }
                    bool canDef = vTerm.Identifier.Code == VariableCode.ARG || vTerm.Identifier.Code == VariableCode.ARGS || vTerm.Identifier.IsPrivate;
                    term = argsRow[i * 2 + 1];
                    if (term is NullTerm)
                    {
                        if (canDef)// && label.ArgOptional)
                        {
                            if (vTerm.GetOperandType() == typeof(long))
                                def = new SingleLongTerm(0);
                            else
                                def = new SingleStrTerm("");
                        }
                    }
                    else
                    {
                        def = term.Restructure(exm) as SingleTerm;
                        if (def == null)
                        { errMes = LocalizationManager.Error.ArgCanOnlyConst; goto err; }
                        if (!canDef)
                        { errMes = LocalizationManager.Error.ArgCanOnlyPrivVar; goto err; }
                        else if (vTerm.Identifier.IsReference)
                        { errMes = LocalizationManager.Error.RefArgCanNotInitialize; goto err; }
                        if (vTerm.GetOperandType() != def.GetOperandType())
                        { errMes = LocalizationManager.Error.NotMatchTypeArgAndInitialValue; goto err; }
                    }
                    args[i] = vTerm;
                    defs[i] = def;
                }

            }
        }
        if (!wc.EOL)
        { errMes = LocalizationManager.Error.WrongArgFormat; goto err; }

        //label.SubNames = subNames;
        label.Arg = args;
        label.Def = defs;
        label.ArgLength = maxArg;
        label.ArgsLength = maxArgs;
        return;
    err:
        ParserMediator.Warn(string.Format(LocalizationManager.Error.FuncArgError, label.LabelName, errMes), label, 2, true, false);
        return;
    }


    public volatile bool useCallForm;

    /// <summary>
    /// 事前処理したファイルをさらに解析し実行可能な状態にする
    /// </summary>
    private void ParseScript()
    {
        int usedLabelCount = 0;
        int labelDepth = -1;
        List<FunctionLabelLine> labelList = labelDic.GetAllLabels(true);
        HashSet<FunctionLabelLine> parsedLabels = [];
        // [Emuera改修:START-03]
        // CALLFORM系があるゲームは、到達判定だけでは呼び先を絞れないため残りも解析する。
        // その大量の残り関数だけを最後に並列解析し、通常の評価順や実行順は変えない。
        bool parseRemainingInParallel = false;
        int remainingLabelCount = 0;
        int parallelLabelCount = 0;
        while (true)
        {
            labelDepth++;
            int countInDepth = 0;
            foreach (FunctionLabelLine label in labelList)
            {
                if (label.Depth != labelDepth)
                    continue;
                if (IsLazyLabel(label))
                {
                    parsedLabels.Add(label);
                    continue;
                }
                usedLabelCount++;
                countInDepth++;
                ParseFunctionWithCatch(label);
                parsedLabels.Add(label);
            }
            // 動的な関数呼び出しが見つかった時点で、未解析関数も全て解析対象になる。
            // 現在の深さは従来どおり順番に解析し、残りだけを並列化する。
            if (useCallForm && !Program.AnalysisMode)
            {
                parseRemainingInParallel = true;
                break;
            }
            if (countInDepth == 0)
                break;
        }
        labelDepth = -1;
        var ignoredFNCWarningFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int ignoredFNCWarningCount = 0;

        bool ignoreAll = false;
        DisplayWarningFlag notCalledWarning = Config.Config.FunctionNotCalledWarning;
        switch (notCalledWarning)
        {
            case DisplayWarningFlag.IGNORE:
            case DisplayWarningFlag.LATER:
                ignoreAll = true;
                break;
        }
        if (useCallForm)
        {//callform系が使われたら全ての関数が呼び出されたとみなす。
            if (Program.AnalysisMode)
                output.PrintSystemLine(LocalizationManager.Error.BeNotFuncCheckBecauseUseCallform);
            List<FunctionLabelLine> remainingLabels = labelList.Where(label => !parsedLabels.Contains(label)).ToList();
            remainingLabelCount = remainingLabels.Count;
            if (parseRemainingInParallel)
            {
                parallelLabelCount = remainingLabels.Count;
                // この並列化は起動時の構文解析だけ。ゲーム実行中の関数順序は変更しない。
                parentProcess.SetParallelScanning(true);
                try
                {
                    Parallel.ForEach(remainingLabels, ParseFunctionWithCatch);
                }
                finally
                {
                    parentProcess.SetParallelScanning(false);
                }
            }
            else
            {
                foreach (FunctionLabelLine label in remainingLabels)
                    ParseFunctionWithCatch(label);
            }
        }
        else
        {
            bool ignoreUncalledFunction = Config.Config.IgnoreUncalledFunction;
            foreach (FunctionLabelLine label in labelList)
            {
                if (label.Depth != labelDepth)
                    continue;
                if (IsLazyLabel(label))
                    continue;
                //解析モード時は呼ばれなかったものをここで解析
                if (Program.AnalysisMode)
                    ParseFunctionWithCatch(label);
                bool ignore = false;
                if (notCalledWarning == DisplayWarningFlag.ONCE)
                {
                    string filename = label.Position.Value.Filename;

                    if (!string.IsNullOrEmpty(filename))
                    {
                        if (ignoredFNCWarningFiles.Contains(filename))
                        {
                            ignore = true;
                        }
                        else
                        {
                            ignore = false;
                            ignoredFNCWarningFiles.Add(filename);
                        }
                    }
                    //break;
                }
                if (ignoreAll || ignore)
                    ignoredFNCWarningCount++;
                else
                    ParserMediator.Warn(string.Format(LocalizationManager.Error.FuncNeverCalled, label.LabelName), label, 1, false, false);
                if (!ignoreUncalledFunction)
                    ParseFunctionWithCatch(label);
                else
                {
                    if (!(label.NextLine is NullLine) && !(label.NextLine is FunctionLabelLine))
                    {
                        if (!label.NextLine.IsError)
                        {
                            label.NextLine.IsError = true;
                            label.NextLine.ErrMes = "呼び出されないはずの関数が呼ばれた";
                        }
                    }
                }
            }
        }
        if (Program.AnalysisMode && (warningDic.Keys.Count > 0 || GlobalStatic.tempDic.Keys.Count > 0))
        {
            output.PrintError(LocalizationManager.Error.UndefinedFunctions);
            if (warningDic.Keys.Count > 0)
            {
                output.PrintError(LocalizationManager.Error.GeneralFunc);
                foreach (string labelName in warningDic.Keys)
                {
                    output.PrintError($"　　{labelName}: {warningDic[labelName]}回");
                }
            }
            if (GlobalStatic.tempDic.Keys.Count > 0)
            {
                output.PrintError(LocalizationManager.Error.SentenceFunc);
                foreach (string labelName in GlobalStatic.tempDic.Keys)
                {
                    output.PrintError($"　　{labelName}: {GlobalStatic.tempDic[labelName]}回");
                }
            }
        }
        else
        {
            if (ignoredFNCWarningCount > 0 && Config.Config.DisplayWarningLevel <= 1 && notCalledWarning != DisplayWarningFlag.IGNORE)
                output.PrintError(string.Format(LocalizationManager.Error.IgnoredUndefinedFuncCall, ignoredFNFWarningCount));
            if (ignoredFNFWarningCount > 0 && Config.Config.DisplayWarningLevel <= 2 && notCalledWarning != DisplayWarningFlag.IGNORE)
                output.PrintError(string.Format(LocalizationManager.Error.IgnoredUndefinedFuncCall, ignoredFNFWarningCount));
        }
        ParserMediator.FlushWarningList();
#if PERFORMANCE_METRICS
        ErbStartupProfiler.SetScriptInfo(labelList.Count, parsedLabels.Count, remainingLabelCount, parallelLabelCount);
#endif
        if (Config.Config.DisplayReport)
            output.PrintError(string.Format(LocalizationManager.Error.TotalFunc, enabledLineCount, labelDic.Count, usedLabelCount));
        if (Config.Config.AllowFunctionOverloading && Config.Config.WarnFunctionOverloading)
        {
            List<string> overloadedList = GlobalStatic.IdentifierDictionary.GetOverloadedList(labelDic);
            if (overloadedList.Count > 0)
            {
                output.NewLine();
                output.PrintError(LocalizationManager.Error.OverWriteSystemFuncWarn1);
                foreach (string funcname in overloadedList)
                {
                    output.PrintSystemLine(string.Format(LocalizationManager.Error.OverWriteSystemFuncWarn2, funcname));
                }
                output.PrintSystemLine(LocalizationManager.Error.OverWriteSystemFuncWarn3);
                output.NewLine();
                output.PrintSystemLine(LocalizationManager.Error.OverWriteSystemFuncWarn4);
                output.PrintSystemLine(LocalizationManager.Error.OverWriteSystemFuncWarn5);
                output.PrintSystemLine(LocalizationManager.Error.OverWriteSystemFuncWarn6);
                output.PrintSystemLine(LocalizationManager.Error.OverWriteSystemFuncWarn7);
            }
        }

    }

    public Dictionary<string, long> warningDic = [];
    private void printFunctionNotFoundWarning(string str, InstructionLine line, int level, bool isError)
    {
        if (Program.AnalysisMode)
        {
            if (warningDic.TryGetValue(str, out long value))
                warningDic[str] = ++value;
            else
                warningDic.Add(str, 1);
            return;
        }
        if (isError)
        {
            line.IsError = true;
            line.ErrMes = str;
        }
        if (level < Config.Config.DisplayWarningLevel)
            return;
        bool ignore = false;
        DisplayWarningFlag warnFlag = Config.Config.FunctionNotFoundWarning;
        if (warnFlag == DisplayWarningFlag.IGNORE)
            ignore = true;
        else if (warnFlag == DisplayWarningFlag.DISPLAY)
            ignore = false;
        else if (warnFlag == DisplayWarningFlag.ONCE)
        {

            string filename = line.Position.Value.Filename;
            if (!string.IsNullOrEmpty(filename))
            {
                if (ignoredFNFWarningFiles.ContainsKey(filename))
                {
                    ignore = true;
                }
                else
                {
                    ignore = false;
                    ignoredFNFWarningFiles.TryAdd(filename, 0);
                }
            }
        }
        if (ignore && !Program.AnalysisMode)
        {
            Interlocked.Increment(ref ignoredFNFWarningCount);
            return;
        }
        ParserMediator.Warn(str, line, level, isError, false);
    }

    private void ParseFunctionWithCatch(FunctionLabelLine label)
    {//ここでエラーを捕まえることは本来はないはず。ExeEE相当。
#if PERFORMANCE_METRICS
        if (ErbStartupProfiler.TimingEnabled)
        {
            ErbStartupFunctionProfile profile = new();
            long totalStart = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                setArgument(label, profile);
                profile.SetArgumentTicks = System.Diagnostics.Stopwatch.GetTimestamp() - start;
                start = System.Diagnostics.Stopwatch.GetTimestamp();
                nestCheck(label, profile);
                profile.NestCheckTicks = System.Diagnostics.Stopwatch.GetTimestamp() - start;
                start = System.Diagnostics.Stopwatch.GetTimestamp();
                setJumpTo(label, profile);
                profile.SetJumpToTicks = System.Diagnostics.Stopwatch.GetTimestamp() - start;
            }
            catch (Exception exc)
            {
                System.Media.SystemSounds.Hand.Play();
                string errmes = exc is EmueraException ? exc.Message : exc.GetType().ToString() + ":" + exc.Message;
                ParserMediator.Warn("@" + label.LabelName + " の解析中にエラー:" + errmes, label, 2, true, false, exc is not EmueraException ? exc.StackTrace : null);
                label.ErrMes = LocalizationManager.Error.CalledFailedFunc;
                System.Windows.Forms.Application.DoEvents();
            }
            finally
            {
                profile.TotalTicks = System.Diagnostics.Stopwatch.GetTimestamp() - totalStart;
                ErbStartupProfiler.RecordFunction(profile);
                parentProcess.scaningLine = null;
            }
            return;
        }
#endif
        try
        {
            setArgument(label);
            nestCheck(label);
            setJumpTo(label);
        }
        catch (Exception exc)
        {
            System.Media.SystemSounds.Hand.Play();
            //1756beta2+v6.1 修正の効率化のために何かパース関係でハンドリングできてないエラーが出た場合はスタックトレースを投げるようにした
            string errmes = exc is EmueraException ? exc.Message : exc.GetType().ToString() + ":" + exc.Message;
            ParserMediator.Warn("@" + label.LabelName + " の解析中にエラー:" + errmes, label, 2, true, false, exc is not EmueraException ? exc.StackTrace : null);
            label.ErrMes = LocalizationManager.Error.CalledFailedFunc;
            System.Windows.Forms.Application.DoEvents();
        }
        finally
        {
            parentProcess.scaningLine = null;
        }

    }

    private void setArgument(FunctionLabelLine label
#if PERFORMANCE_METRICS
        , ErbStartupFunctionProfile profile = null
#endif
        )
    {
        //1周目/3周
        //引数の解析とか
        LogicalLine nextLine = label;
        bool inMethod = label.IsMethod;
        while (true)
        {
            nextLine = nextLine.NextLine;
            parentProcess.scaningLine = nextLine;
            if (nextLine is not InstructionLine func)
            {
                if (nextLine is NullLine or FunctionLabelLine)
                    break;
                continue;
            }
            if (inMethod)
            {
                if (!func.Function.IsMethodSafe())
                {
                    ParserMediator.Warn(string.Format(LocalizationManager.Error.CanNotUseInUserFunc, func.Function.Name), nextLine, 2, true, false);
                    continue;
                }
            }
            bool forceSetArgument = func.Function.IsForceSetArg();
            if (Config.Config.NeedReduceArgumentOnLoad || Program.AnalysisMode || forceSetArgument)
            {
#if PERFORMANCE_METRICS
                ErbStartupProfiler.RecordArgument(func.FunctionCode, forceSetArgument);
#endif
                ArgumentParser.SetArgumentTo(func);
            }
        }
    }

    private void nestCheck(FunctionLabelLine label
#if PERFORMANCE_METRICS
        , ErbStartupFunctionProfile profile = null
#endif
        )
    {
        //2周目/3周
        //IF-ELSEIF-ENDIF、REPEAT-RENDの対応チェックなど
        //PRINTDATA系もここでチェック
        LogicalLine nextLine = label;
        List<InstructionLine> tempLineList = [];
        Stack<InstructionLine> nestStack = new();
        Stack<InstructionLine> SelectcaseStack = new();
        InstructionLine pairLine = null;
        while (true)
        {
            nextLine = nextLine.NextLine;
            parentProcess.scaningLine = nextLine;
            if (nextLine is NullLine or FunctionLabelLine)
                break;
            if (nextLine is not InstructionLine)
            {
                if (nextLine is GotoLabelLine)
                {
                    InstructionLine currentBaseFunc = nestStack.Count == 0 ? null : nestStack.Peek();
                    if (currentBaseFunc != null)
                    {
                        if (currentBaseFunc.FunctionCode == FunctionCode.PRINTDATA
                            || currentBaseFunc.FunctionCode == FunctionCode.PRINTDATAL
                            || currentBaseFunc.FunctionCode == FunctionCode.PRINTDATAW
                            || currentBaseFunc.FunctionCode == FunctionCode.PRINTDATAD
                            || currentBaseFunc.FunctionCode == FunctionCode.PRINTDATADL
                            || currentBaseFunc.FunctionCode == FunctionCode.PRINTDATADW
                            || currentBaseFunc.FunctionCode == FunctionCode.PRINTDATAK
                            || currentBaseFunc.FunctionCode == FunctionCode.PRINTDATAKL
                            || currentBaseFunc.FunctionCode == FunctionCode.PRINTDATAKW
                            || currentBaseFunc.FunctionCode == FunctionCode.STRDATA
                            || currentBaseFunc.FunctionCode == FunctionCode.DATALIST
                            || currentBaseFunc.FunctionCode == FunctionCode.TRYCALLLIST
                            || currentBaseFunc.FunctionCode == FunctionCode.TRYJUMPLIST
                            || currentBaseFunc.FunctionCode == FunctionCode.TRYGOTOLIST)
                        //|| (currentBaseFunc.FunctionCode == FunctionCode.SELECTCASE))
                        {
                            ParserMediator.Warn(currentBaseFunc.Function.Name + "構文中に$ラベルを定義することはできません", nextLine, 2, true, false);
                        }
                    }
                }
                continue;
            }
            var func = nextLine as InstructionLine;
            var baseFunc = nestStack.Count == 0 ? null : nestStack.Peek();
            if (baseFunc != null)
            {
                if (baseFunc.Function.IsPrintData() || baseFunc.FunctionCode == FunctionCode.STRDATA)
                {
                    if (func.FunctionCode != FunctionCode.DATA && func.FunctionCode != FunctionCode.DATAFORM && func.FunctionCode != FunctionCode.DATALIST
                        && func.FunctionCode != FunctionCode.ENDLIST && func.FunctionCode != FunctionCode.ENDDATA)
                    {
                        ParserMediator.Warn(baseFunc.Function.Name + "構文に使用できない命令\'" + func.Function.Name + "\'が含まれています", func, 2, true, false);
                        continue;
                    }
                }
                else if (baseFunc.FunctionCode == FunctionCode.DATALIST)
                {
                    if (func.FunctionCode != FunctionCode.DATA && func.FunctionCode != FunctionCode.DATAFORM && func.FunctionCode != FunctionCode.ENDLIST)
                    {
                        ParserMediator.Warn("DATALIST構文に使用できない命令\'" + func.Function.Name + "\'が含まれています", func, 2, true, false);
                        continue;
                    }
                }
                else if (baseFunc.FunctionCode == FunctionCode.TRYCALLLIST || baseFunc.FunctionCode == FunctionCode.TRYJUMPLIST || baseFunc.FunctionCode == FunctionCode.TRYGOTOLIST)
                {
                    if (func.FunctionCode != FunctionCode.FUNC && func.FunctionCode != FunctionCode.ENDFUNC)
                    {
                        ParserMediator.Warn(baseFunc.Function.Name + "構文に使用できない命令\'" + func.Function.Name + "\'が含まれています", func, 2, true, false);
                        continue;
                    }
                }
                else if (baseFunc.FunctionCode == FunctionCode.SELECTCASE)
                {
                    if (baseFunc.IfCaseList.Count == 0 && func.FunctionCode != FunctionCode.CASE && func.FunctionCode != FunctionCode.CASEELSE && func.FunctionCode != FunctionCode.ENDSELECT)
                    {
                        ParserMediator.Warn("SELECTCASE構文の分岐の外に命令\'" + func.Function.Name + "\'が含まれています", func, 2, true, false);
                        continue;
                    }
                }
            }
            switch (func.FunctionCode)
            {
                case FunctionCode.REPEAT:
                    foreach (InstructionLine iLine in nestStack)
                    {
                        if (iLine.FunctionCode == FunctionCode.REPEAT)
                        {
                            ParserMediator.Warn(LocalizationManager.Error.NestedRepeat, func, 1, false, false);
                        }
                        else if (iLine.FunctionCode == FunctionCode.FOR)
                        {
                            VariableTerm cnt = (iLine.Argument as SpForNextArgment).Cnt;
                            if (cnt.Identifier.Name == "COUNT" && cnt.isAllConst && cnt.getEl1forArg == 0)
                            {
                                ParserMediator.Warn("カウンタ変数にCOUNT:0を用いたFOR文の中でREPEATが呼び出されています", func, 1, false, false);
                            }
                        }
                    }
                    if (func.IsError)
                        break;
                    nestStack.Push(func);
                    break;
                case FunctionCode.IF:
                    nestStack.Push(func);
                    func.IfCaseList = [];
                    func.IfCaseList.AddFirst(func);
                    break;
                case FunctionCode.SELECTCASE:
                    nestStack.Push(func);
                    func.IfCaseList = [];
                    SelectcaseStack.Push(func);
                    break;
                case FunctionCode.FOR:
                    //ネストエラーチェックのためにコストはかかるが、ここでチェックする
                    if (func.Argument == null)
                        ArgumentParser.SetArgumentTo(func);
                    //上で引数解析がなされていることは保証されているので、
                    //それでこれがfalseになるのは、引数解析でエラーが起きた場合のみ
                    if (func.Argument != null)
                    {
                        VariableTerm Cnt = (func.Argument as SpForNextArgment).Cnt;
                        if (Cnt.Identifier.Name == "COUNT")
                        {
                            foreach (InstructionLine iLine in nestStack)
                            {
                                if (iLine.FunctionCode == FunctionCode.REPEAT && Cnt.isAllConst && Cnt.getEl1forArg == 0)
                                {
                                    ParserMediator.Warn("REPEAT文の中でカウンタ変数にCOUNT:0を用いたFORが使われています（無限ループの恐れがあります）", func, 1, false, false);
                                }
                                else if (iLine.FunctionCode == FunctionCode.FOR)
                                {
                                    VariableTerm destCnt = (iLine.Argument as SpForNextArgment).Cnt;
                                    if (destCnt.Identifier.Name == "COUNT" && Cnt.isAllConst && destCnt.isAllConst && destCnt.getEl1forArg == Cnt.getEl1forArg)
                                    {
                                        ParserMediator.Warn($"カウンタ変数にCOUNT:{Cnt.getEl1forArg}を用いたFOR文が入れ子にされています（無限ループの恐れがあります）", func, 1, false, false);
                                    }
                                }
                            }
                        }
                    }
                    if (func.IsError)
                        break;
                    nestStack.Push(func);
                    break;
                case FunctionCode.WHILE:
                case FunctionCode.TRYCGOTO:
                case FunctionCode.TRYCJUMP:
                case FunctionCode.TRYCCALL:
                case FunctionCode.TRYCGOTOFORM:
                case FunctionCode.TRYCJUMPFORM:
                case FunctionCode.TRYCCALLFORM:
                case FunctionCode.DO:
                    nestStack.Push(func);
                    break;
                case FunctionCode.BREAK:
                case FunctionCode.CONTINUE:
                    InstructionLine[] array = [.. nestStack];
                    for (int i = 0; i < array.Length; i++)
                    {
                        if (array[i].FunctionCode == FunctionCode.REPEAT
                            || array[i].FunctionCode == FunctionCode.FOR
                            || array[i].FunctionCode == FunctionCode.WHILE
                            || array[i].FunctionCode == FunctionCode.DO)
                        {
                            pairLine = array[i];
                            break;
                        }
                    }
                    if (pairLine == null)
                    {
                        ParserMediator.Warn("REPEAT, FOR, WHILE, DOの中以外で" + func.Function.Name + "文が使われました", func, 2, true, false);
                        break;
                    }
                    func.JumpTo = pairLine;
                    break;

                case FunctionCode.ELSEIF:
                case FunctionCode.ELSE:
                    {
                        //1.725 Stack<T>.Peek()はStackが空の時はnullを返す仕様だと思いこんでおりました。
                        InstructionLine ifLine = nestStack.Count == 0 ? null : nestStack.Peek();
                        if (ifLine == null || ifLine.FunctionCode != FunctionCode.IF)
                        {
                            ParserMediator.Warn("IF～ENDIFの外で" + func.Function.Name + "文が使われました", func, 2, true, false);
                            break;
                        }
                        if (ifLine.IfCaseList.Last.Value.FunctionCode == FunctionCode.ELSE)
                            ParserMediator.Warn("ELSE文より後で" + func.Function.Name + "文が使われました", func, 1, false, false);
                        ifLine.IfCaseList.AddLast(func);
                    }
                    break;
                case FunctionCode.ENDIF:
                    {
                        var ifLine = nestStack.Count == 0 ? null : nestStack.Peek();
                        if (ifLine == null || ifLine.FunctionCode != FunctionCode.IF)
                        {
                            ParserMediator.Warn(LocalizationManager.Error.UnexpectedEndif, func, 2, true, false);
                            break;
                        }
                        foreach (var ifelseifLine in ifLine.IfCaseList)
                        {
                            ifelseifLine.JumpTo = func;
                        }
                        nestStack.Pop();
                    }
                    break;
                case FunctionCode.CASE:
                case FunctionCode.CASEELSE:
                    {
                        InstructionLine selectLine = nestStack.Count == 0 ? null : nestStack.Peek();
                        if (selectLine == null || selectLine.FunctionCode != FunctionCode.SELECTCASE && SelectcaseStack.Count == 0)
                        {
                            ParserMediator.Warn("SELECTCASE～ENDSELECTの外で" + func.Function.Name + "文が使われました", func, 2, true, false);
                            break;
                        }
                        else if (selectLine.FunctionCode != FunctionCode.SELECTCASE && SelectcaseStack.Count > 0)
                        {
                            do
                            {
                                ParserMediator.Warn(selectLine.Function.Name + "文に対応する" + FunctionIdentifier.getMatchFunction(selectLine.FunctionCode) + "がない状態で" + func.Function.Name + "文に到達しました", func, 2, true, false);
                                //これを跨いでIF等が閉じられることがないようにする。
                                nestStack.Pop();
                                //if (nestStack.Count > 0)　//空になってるかは下で判定できるので、これを見る必要がない
                                selectLine = nestStack.Count == 0 ? null : nestStack.Peek(); //ちなみにnullになることはない（SELECTCASEがない場合は上で弾けるから）
                            } while (selectLine != null && selectLine.FunctionCode != FunctionCode.SELECTCASE);
                            break;
                        }
                        if (selectLine.IfCaseList.Count > 0 &&
                            selectLine.IfCaseList.Last.Value.FunctionCode == FunctionCode.CASEELSE)
                            ParserMediator.Warn("CASEELSE文より後で" + func.Function.Name + "文が使われました", func, 1, false, false);
                        selectLine.IfCaseList.AddLast(func);
                    }
                    break;
                case FunctionCode.ENDSELECT:
                    {
                        InstructionLine selectLine = nestStack.Count == 0 ? null : nestStack.Peek();
                        if (selectLine == null || selectLine.FunctionCode != FunctionCode.SELECTCASE && SelectcaseStack.Count == 0)
                        {
                            ParserMediator.Warn(LocalizationManager.Error.UnexpectedEndselect, func, 2, true, false);
                            break;
                        }
                        else if (selectLine.FunctionCode != FunctionCode.SELECTCASE && SelectcaseStack.Count > 0)
                        {
                            do
                            {
                                ParserMediator.Warn(selectLine.Function.Name + "文に対応する" + FunctionIdentifier.getMatchFunction(selectLine.FunctionCode) + "がない状態で" + func.Function.Name + "文に到達しました", func, 2, true, false);
                                //これを跨いでIF等が閉じられることがないようにする。
                                nestStack.Pop();
                                //if (nestStack.Count > 0)　//空になってるかは下で判定できるので、これを見る必要がない
                                selectLine = nestStack.Count == 0 ? null : nestStack.Peek(); //ちなみにnullになることはない（SELECTCASEがない場合は上で弾けるから）
                            } while (selectLine != null && selectLine.FunctionCode != FunctionCode.SELECTCASE);
                            //とりあえず、対応するSELECTCASE跨ぎは閉じる
                            SelectcaseStack.Pop();
                            //こっちでも抜かないとSELECTCASEが2つのENDSELECTに対応してしまう
                            nestStack.Pop();
                            break;
                        }
                        nestStack.Pop();
                        SelectcaseStack.Pop();
                        selectLine.JumpTo = func;
                        if (selectLine.IsError)
                            break;
                        var term = (selectLine.Argument as ExpressionArgument).Term;
                        if (term == null)
                        {
                            ParserMediator.Warn("SELECTCASEの引数がありません", selectLine, 2, true, false);
                            break;
                        }
                        foreach (var caseLine in selectLine.IfCaseList)
                        {
                            caseLine.JumpTo = func;
                            if (caseLine.IsError)
                                continue;
                            if (caseLine.FunctionCode == FunctionCode.CASEELSE)
                                continue;
                            var caseExps = (caseLine.Argument as CaseArgument).CaseExps;
                            if (caseExps.Length == 0)
                                ParserMediator.Warn("CASEの引数がありません", caseLine, 2, true, false);

                            foreach (var exp in caseExps)
                            {
                                if (exp.GetOperandType() != term.GetOperandType())
                                    ParserMediator.Warn(LocalizationManager.Error.NotMatchCaseTypeAndSelectcaseType, caseLine, 2, true, false);
                            }

                        }
                    }
                    break;
                case FunctionCode.REND:
                case FunctionCode.NEXT:
                case FunctionCode.WEND:
                case FunctionCode.LOOP:
                    FunctionCode parentFunc = FunctionIdentifier.getParentFunc(func.FunctionCode);
                    //if (parentFunc == FunctionCode.__NULL__)
                    //    throw new ExeEE("何か変？");
                    if (nestStack.Count == 0
                        || nestStack.Peek().FunctionCode != parentFunc)
                    {
                        ParserMediator.Warn("対応する" + parentFunc.ToString() + "の無い" + func.Function.Name + "文です", func, 2, true, false);
                        break;
                    }
                    pairLine = nestStack.Pop();//REPEAT
                    func.JumpTo = pairLine;
                    pairLine.JumpTo = func;
                    break;
                case FunctionCode.CATCH:
                    pairLine = nestStack.Count == 0 ? null : nestStack.Peek();
                    if (pairLine == null
                        || pairLine.FunctionCode != FunctionCode.TRYCGOTO
                        && pairLine.FunctionCode != FunctionCode.TRYCCALL
                        && pairLine.FunctionCode != FunctionCode.TRYCJUMP
                        && pairLine.FunctionCode != FunctionCode.TRYCGOTOFORM
                        && pairLine.FunctionCode != FunctionCode.TRYCCALLFORM
                        && pairLine.FunctionCode != FunctionCode.TRYCJUMPFORM)
                    {
                        ParserMediator.Warn(LocalizationManager.Error.MissingTryc, func, 2, true, false);
                        break;
                    }
                    pairLine = nestStack.Pop();//TRYC
                    pairLine.JumpToEndCatch = func;//TRYCにCATCHの位置を教える
                    nestStack.Push(func);
                    break;
                case FunctionCode.ENDCATCH:
                    if (nestStack.Count == 0
                        || nestStack.Peek().FunctionCode != FunctionCode.CATCH)
                    {
                        ParserMediator.Warn(LocalizationManager.Error.UnexpectedEndcatch, func, 2, true, false);
                        break;
                    }
                    pairLine = nestStack.Pop();//CATCH
                    pairLine.JumpToEndCatch = func;//CATCHにENDCATCHの位置を教える
                    break;
                case FunctionCode.PRINTDATA:
                case FunctionCode.PRINTDATAL:
                case FunctionCode.PRINTDATAW:
                case FunctionCode.PRINTDATAD:
                case FunctionCode.PRINTDATADL:
                case FunctionCode.PRINTDATADW:
                case FunctionCode.PRINTDATAK:
                case FunctionCode.PRINTDATAKL:
                case FunctionCode.PRINTDATAKW:
                    {
                        foreach (var iLine in nestStack)
                        {
                            if (iLine.Function.IsPrintData())
                            {
                                ParserMediator.Warn(LocalizationManager.Error.NestedPrintdata, func, 2, true, false);
                                break;
                            }
                            if (iLine.FunctionCode == FunctionCode.STRDATA)
                            {
                                ParserMediator.Warn(LocalizationManager.Error.StrdataInsidePrintdata, func, 2, true, false);
                                break;
                            }
                        }
                        if (func.IsError)
                            break;
                        func.dataList = [];
                        nestStack.Push(func);
                        break;
                    }
                case FunctionCode.STRDATA:
                    {
                        foreach (var iLine in nestStack)
                        {
                            if (iLine.FunctionCode == FunctionCode.STRDATA)
                            {
                                ParserMediator.Warn(LocalizationManager.Error.NestedStrdata, func, 2, true, false);
                                break;
                            }
                            if (iLine.Function.IsPrintData())
                            {
                                ParserMediator.Warn(LocalizationManager.Error.PrintdataInsideStrdata, func, 2, true, false);
                                break;
                            }
                        }
                        if (func.IsError)
                            break;
                        func.dataList = [];
                        nestStack.Push(func);
                        break;
                    }
                case FunctionCode.DATALIST:
                    {
                        var pline = nestStack.Count == 0 ? null : nestStack.Peek();
                        if (pline == null || !pline.Function.IsPrintData() && pline.FunctionCode != FunctionCode.STRDATA)
                        {
                            ParserMediator.Warn(LocalizationManager.Error.UnexpectedDatalist, func, 2, true, false);
                            break;
                        }
                        tempLineList = [];
                        nestStack.Push(func);

                        break;
                    }
                case FunctionCode.ENDLIST:
                    {
                        if (nestStack.Count == 0 || nestStack.Peek().FunctionCode != FunctionCode.DATALIST)
                        {
                            ParserMediator.Warn(LocalizationManager.Error.UnexpectedEndlist, func, 2, true, false);
                            break;
                        }
                        if (tempLineList.Count == 0)
                            ParserMediator.Warn(LocalizationManager.Error.DatalistDataIsMissing, func, 1, false, false);
                        nestStack.Pop();
                        nestStack.Peek().dataList.Add(tempLineList);
                        break;
                    }
                case FunctionCode.DATA:
                case FunctionCode.DATAFORM:
                    {
                        InstructionLine pdata = nestStack.Count == 0 ? null : nestStack.Peek();
                        if (pdata == null || !pdata.Function.IsPrintData() && pdata.FunctionCode != FunctionCode.DATALIST && pdata.FunctionCode != FunctionCode.STRDATA)
                        {
                            ParserMediator.Warn("対応するPRINTDATA系命令のない" + func.Function.Name + "です", func, 2, true, false);
                            break;
                        }
                        List<InstructionLine> iList = [];
                        if (pdata.FunctionCode != FunctionCode.DATALIST)
                        {
                            iList.Add(func);
                            pdata.dataList.Add(iList);
                        }
                        else
                            tempLineList.Add(func);
                        break;
                    }
                case FunctionCode.ENDDATA:
                    {
                        InstructionLine pline = nestStack.Count == 0 ? null : nestStack.Peek();
                        if (pline == null || !pline.Function.IsPrintData() && pline.FunctionCode != FunctionCode.STRDATA)
                        {
                            ParserMediator.Warn("対応するPRINTDATA系命令もしくはSTRDATAのない" + func.Function.Name + "です", func, 2, true, false);
                            break;
                        }
                        if (pline.FunctionCode == FunctionCode.DATALIST)
                            ParserMediator.Warn(LocalizationManager.Error.DatalistNotClosed, func, 2, true, false);
                        if (pline.dataList.Count == 0)
                            ParserMediator.Warn(pline.Function.Name + "命令に表示データがありません（この命令は無視されます）", func, 1, false, false);
                        pline.JumpTo = func;
                        nestStack.Pop();
                        break;
                    }
                case FunctionCode.TRYCALLLIST:
                case FunctionCode.TRYJUMPLIST:
                case FunctionCode.TRYGOTOLIST:
                    foreach (InstructionLine iLine in nestStack)
                    {
                        if (iLine.FunctionCode == FunctionCode.TRYCALLLIST || iLine.FunctionCode == FunctionCode.TRYJUMPLIST || iLine.FunctionCode == FunctionCode.TRYGOTOLIST)
                        {
                            ParserMediator.Warn(LocalizationManager.Error.NestedTrycalllist, func, 2, true, false);
                            break;
                        }
                    }
                    if (func.IsError)
                        break;
                    func.callList = [];
                    nestStack.Push(func);
                    break;
                case FunctionCode.FUNC:
                    {
                        InstructionLine pFunc = nestStack.Count == 0 ? null : nestStack.Peek();
                        if (pFunc == null ||
                            pFunc.FunctionCode != FunctionCode.TRYCALLLIST && pFunc.FunctionCode != FunctionCode.TRYJUMPLIST && pFunc.FunctionCode != FunctionCode.TRYGOTOLIST)
                        {
                            ParserMediator.Warn("対応するTRYCALLLIST系命令のない" + func.Function.Name + "です", func, 2, true, false);
                            break;
                        }
                        if (func.Argument == null)
                        {
                            ParserMediator.Warn("TRYCALLLIST系命令中に無効な" + func.Function.Name + "が存在します", pFunc, 2, true, false);
                            break;
                        }
                        if (pFunc.FunctionCode == FunctionCode.TRYGOTOLIST)
                        {
                            var spCallArg = func.Argument as SpCallArgment;
                            if (spCallArg.SubNames.Count != 0)
                            {
                                ParserMediator.Warn(LocalizationManager.Error.TrygotolistToSBrackets, func, 2, true, false);
                                break;
                            }
                            if (spCallArg.RowArgs.Count != 0)
                            {
                                ParserMediator.Warn(LocalizationManager.Error.TrygotolistTargetHasArg, func, 2, true, false);
                                break;
                            }
                        }
                        pFunc.callList.Add(func);
                        break;
                    }
                case FunctionCode.ENDFUNC:
                    var pf = nestStack.Count == 0 ? null : nestStack.Peek();
                    if (pf == null ||
                        pf.FunctionCode != FunctionCode.TRYCALLLIST && pf.FunctionCode != FunctionCode.TRYJUMPLIST && pf.FunctionCode != FunctionCode.TRYGOTOLIST)
                    {
                        ParserMediator.Warn("対応するTRYCALLLIST系命令のない" + func.Function.Name + "です", func, 2, true, false);
                        break;
                    }
                    pf.JumpTo = func;
                    nestStack.Pop();
                    break;
                case FunctionCode.NOSKIP:
                    foreach (var iLine in nestStack)
                    {
                        if (iLine.FunctionCode == FunctionCode.NOSKIP)
                        {
                            ParserMediator.Warn(LocalizationManager.Error.NestedNoskip, func, 2, true, false);
                            break;
                        }
                    }
                    if (func.IsError)
                        break;
                    nestStack.Push(func);
                    break;
                case FunctionCode.ENDNOSKIP:
                    var pfunc = nestStack.Count == 0 ? null : nestStack.Peek();
                    if (pfunc == null ||
                        pfunc.FunctionCode != FunctionCode.NOSKIP)
                    {
                        ParserMediator.Warn("対応するNOSKIP系命令のない" + func.Function.Name + "です", func, 2, true, false);
                        break;
                    }
                    //エラーハンドリング用
                    pfunc.JumpTo = func;
                    func.JumpTo = pfunc;
                    nestStack.Pop();
                    break;
            }

        }

        while (nestStack.Count != 0)
        {
            var func = nestStack.Pop();
            string funcName = func.Function.Name;
            string funcMatch = FunctionIdentifier.getMatchFunction(func.FunctionCode);
            if (func != null)
                ParserMediator.Warn(string.Format(LocalizationManager.Error.MissingCorresponding, funcMatch, funcName), func, 2, true, false);
            else
                ParserMediator.Warn(LocalizationManager.Error.DefaultError, func, 2, true, false);
        }
        //使ったスタックをクリア
        SelectcaseStack.Clear();
    }

    private void setJumpTo(FunctionLabelLine label
#if PERFORMANCE_METRICS
        , ErbStartupFunctionProfile profile = null
#endif
        )
    {
        //3周目/3周
        //フロー制御命令のジャンプ先を設定
        LogicalLine nextLine = label;
        int depth = label.Depth;
        if (depth < 0)
            depth = -2;
        while (true)
        {
            nextLine = nextLine.NextLine;
            if (!(nextLine is InstructionLine func))
            {
                if (nextLine is NullLine || nextLine is FunctionLabelLine)
                    break;
                continue;
            }
            if (func.IsError)
                continue;
            parentProcess.scaningLine = func;

            if (func.Function.Instruction != null)
            {
                string FunctionNotFoundName = null;
                try
                {
                    bool localUseCallForm = false;
                    func.Function.Instruction.SetJumpTo(ref localUseCallForm, func, depth, ref FunctionNotFoundName);
                    if (localUseCallForm)
                        useCallForm = true;
                }
                catch (CodeEE e)
                {
                    ParserMediator.Warn(e.Message, func, 2, true, false);
                    continue;
                }
                if (FunctionNotFoundName != null)
                {
                    if (!Program.AnalysisMode)
                        printFunctionNotFoundWarning("指定された関数名\"@" + FunctionNotFoundName + "\"は存在しません", func, 2, true);
                    else
                        printFunctionNotFoundWarning(FunctionNotFoundName, func, 2, true);
                }
                continue;
            }
            if (func.FunctionCode == FunctionCode.TRYCALLLIST || func.FunctionCode == FunctionCode.TRYJUMPLIST)
                useCallForm = true;
        }
    }

}
