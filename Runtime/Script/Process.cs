using MinorShift.Emuera.GameView;
using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Runtime.Script;
using MinorShift.Emuera.Runtime.Script.Data;
using MinorShift.Emuera.Runtime.Script.Loader;
using MinorShift.Emuera.Runtime.Script.Parser;
using MinorShift.Emuera.Runtime.Script.Statements;
using MinorShift.Emuera.Runtime.Script.Statements.Expression;
using MinorShift.Emuera.Runtime.Script.Statements.Function;
using MinorShift.Emuera.Runtime.Script.Statements.Variable;
using MinorShift.Emuera.Runtime.Utils;
using MinorShift.Emuera.UI.Game.Image;
using Runtime.SQL;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MinorShift.Emuera.UI.Framework;
using System.Reflection.Emit;
using System.Reflection;
using System.Runtime.Loader;

namespace MinorShift.Emuera.GameProc;


internal sealed partial class Process(EmueraConsole view)
{

    public LogicalLine getCurrentLine { get { return state.CurrentLine; } }

    /// <summary>
    /// @~~と$~~を集めたもの。CALL命令などで使う
    /// 実行順序はLogicalLine自身が保持する。
    /// </summary>
    LabelDictionary labelDic;
    public LabelDictionary LabelDictionary { get { return labelDic; } }

    /// <summary>
    /// 変数全部。スクリプト中で必要になる変数は（ユーザーが直接触れないものも含め）この中にいれる
    /// </summary>
    private VariableEvaluator vEvaluator;
    public VariableEvaluator VEvaluator { get { return vEvaluator; } }
    public int ExecutedLineCount => state?.lineCount ?? 0;
    // [Emuera改修:MEASURE-03]
    // マクロ後の変数状態を保存形式と同じ並びでハッシュ化し、比較試験に使う入口。
    // セーブファイル自体は作成・変更しない。通常プレイからは呼ばれない。
    internal string GetBenchmarkStateHash() => vEvaluator.GetBenchmarkStateHash();
    private ExpressionMediator exm;
    private GameBase gamebase;
    readonly EmueraConsole console = view;
    private IdentifierDictionary idDic;
    ProcessState state;
    ProcessState originalState;//リセットする時のために
    private ErbLoader erbLoader;
    bool noError;
    //色々あって復活させてみる
    bool initialiing;
    public bool inInitializeing { get { return initialiing; } }

    public async Task<bool> Initialize(StreamWriter logWriter)
    {
        var stopWatch = new Stopwatch();
        stopWatch.Start();
        LexicalAnalyzer.UseMacro = false;
        state = new ProcessState(console);
        originalState = state;
        initialiing = true;
        try
        {
            logWriter.WriteLine($"Proc:Init:Start {stopWatch.ElapsedMilliseconds}ms");
            logWriter.WriteLine($"Proc:Init:Parser:Start {stopWatch.ElapsedMilliseconds}ms");
            ParserMediator.Initialize(console);
            //コンフィグファイルに関するエラーの処理（コンフィグファイルはこの関数に入る前に読込済み）
            if (ParserMediator.HasWarning)
            {
                ParserMediator.FlushWarningList();
                if (Dialog.ShowPrompt(LocalizationManager.MsgBox.ConfigError, LocalizationManager.MsgBox.ConfigFileError))
                {
                    console.PrintSystemLine(LocalizationManager.SystemLine.SelectExitConfigMB);
                    return false;
                }
            }
            logWriter.WriteLine($"Proc:Init:Parser:End {stopWatch.ElapsedMilliseconds}ms");

            logWriter.WriteLine($"Proc:Init:Image:Start {stopWatch.ElapsedMilliseconds}ms");
            //リソースフォルダ読み込み
            var err = await Task.Run(AppContents.LoadContents);
            if (err != null)
            {
                ParserMediator.FlushWarningList();
                console.PrintSystemLine(LocalizationManager.SystemLine.ResourceReadError);
                console.Print(err.ToString());
                return false;
            }
            ParserMediator.FlushWarningList();
            logWriter.WriteLine($"Proc:Init:Image:End {stopWatch.ElapsedMilliseconds}ms");
            // [Emuera改修:MEASURE-01] 起動時間を区間別に調べる目印。通常版では空処理。
            PerformanceMetrics.MarkStartup("ResourcesPrepared");


            logWriter.WriteLine($"Proc:Init:KeyMacro:Start {stopWatch.ElapsedMilliseconds}ms");
            //キーマクロ読み込み
            if (Config.UseKeyMacro && !Program.AnalysisMode)
            {
                if (File.Exists(KeyMacro.macroPath))
                {
                    if (Config.DisplayReport)
                        console.PrintSystemLine(LocalizationManager.SystemLine.LoadingMacro);
                    if(!KeyMacro.LoadMacroFile(KeyMacro.macroPath))
                        console.PrintSystemLine(LocalizationManager.Error.MacroLoadingError);
                }
            }
            logWriter.WriteLine($"Proc:Init:KeyMacro:End {stopWatch.ElapsedMilliseconds}ms");

            logWriter.WriteLine($"Proc:Init:Replace:Start {stopWatch.ElapsedMilliseconds}ms");
            //_replace.csv読み込み
            if (Config.UseReplaceFile && !Program.AnalysisMode)
            {
                if (File.Exists(Program.CsvDir + "_Replace.csv"))
                {
                    if (Config.DisplayReport)
                        console.PrintSystemLine(LocalizationManager.SystemLine.LoadingReplace);
                    ConfigData.Instance.LoadReplaceFile(Program.CsvDir + "_Replace.csv");
                    if (ParserMediator.HasWarning)
                    {
                        ParserMediator.FlushWarningList();
                        if (Dialog.ShowPrompt(LocalizationManager.MsgBox.ReplaceError, LocalizationManager.MsgBox.ReplaceFileError))
                        {
                            console.PrintSystemLine(LocalizationManager.SystemLine.SelectExitReplaceMB);
                            return false;
                        }
                    }
                }
            }
            Config.SetReplace(ConfigData.Instance);

            logWriter.WriteLine($"Proc:Init:Replace:End {stopWatch.ElapsedMilliseconds}ms");

            //ここでBARを設定すれば、いいことに気づいた予感
            console.setStBar(Config.DrawLineString);

            logWriter.WriteLine($"Proc:Init:Rename:Load:Start {stopWatch.ElapsedMilliseconds}ms");
            //_rename.csv読み込み
            if (Config.UseRenameFile)
            {
                if (File.Exists(Program.CsvDir + "_Rename.csv"))
                {
                    if (Config.DisplayReport || Program.AnalysisMode)
                        console.PrintSystemLine(LocalizationManager.SystemLine.LoadingRename);
                    ParserMediator.LoadEraExRenameFile(Program.CsvDir + "_Rename.csv");
                }
                else
                    console.PrintError(LocalizationManager.SystemLine.MissingRename);
            }
            logWriter.WriteLine($"Proc:Init:Rename:Load:End {stopWatch.ElapsedMilliseconds}ms");

            if (!Config.DisplayReport)
            {
                console.PrintSingleLine(Config.LoadLabel);
                console.RefreshStrings(true);
            }
            //gamebase.csv読み込み
            gamebase = new GameBase();
            if (!await Task.Run(() => gamebase.LoadGameBaseCsv(Program.CsvDir + "GAMEBASE.CSV")))
            {
                ParserMediator.FlushWarningList();
                console.PrintSystemLine(LocalizationManager.SystemLine.GamebaseError);
                return false;
            }
            console.SetWindowTitle(gamebase.ScriptWindowTitle);
            GlobalStatic.GameBaseData = gamebase;
            logWriter.WriteLine($"Proc:Init:MainCSV:End {stopWatch.ElapsedMilliseconds}ms");

            logWriter.WriteLine($"Proc:Init:EtcCSV:Start {stopWatch.ElapsedMilliseconds}ms");
            //前記以外のcsvを全て読み込み
            var constant = new ConstantData();
            constant.LoadData(Program.CsvDir, console, Config.DisplayReport);
            GlobalStatic.ConstantData = constant;
            TrainName = constant.GetCsvNameList(VariableCode.TRAINNAME);
            logWriter.WriteLine($"Proc:Init:EtcCSV:End {stopWatch.ElapsedMilliseconds}ms");
            PerformanceMetrics.MarkStartup("CsvLoaded"); // CSV読込完了の目印


            vEvaluator = new VariableEvaluator(gamebase, constant);
            GlobalStatic.VEvaluator = vEvaluator;

            idDic = new IdentifierDictionary(vEvaluator.VariableData);
            GlobalStatic.IdentifierDictionary = idDic;

            StrForm.Initialize();
            VariableParser.Initialize();

            exm = new ExpressionMediator(this, vEvaluator, console);
            GlobalStatic.EMediator = exm;

            logWriter.WriteLine($"Proc:Init:ERH:Start {stopWatch.ElapsedMilliseconds}ms");

            labelDic = new LabelDictionary();
            GlobalStatic.LabelDictionary = labelDic;
            ErhLoader hLoader = new(console, idDic, this);

            LexicalAnalyzer.UseMacro = false;

            //ERH読込
            if (!await Task.Run(() => hLoader.LoadHeaderFiles(Program.ErbDir, Config.DisplayReport)))
            {
                ParserMediator.FlushWarningList();
                console.PrintSystemLine(LocalizationManager.SystemLine.ErhLoadingError);
                return false;
            }
            LexicalAnalyzer.UseMacro = idDic.UseMacro();
            logWriter.WriteLine($"Proc:Init:ERH:End {stopWatch.ElapsedMilliseconds}ms");
            PerformanceMetrics.MarkStartup("ErhLoaded"); // ERH読込完了の目印


            //TODO:ユーザー定義変数用のcsvの適用

            //ERB読込
            logWriter.WriteLine($"Proc:Init:ERB:Start {stopWatch.ElapsedMilliseconds}ms");
            erbLoader = new ErbLoader(console, exm, this);
            if (Program.AnalysisMode)
                noError = await erbLoader.LoadErbList(Program.AnalysisFiles, labelDic);
            else
                noError = await erbLoader.LoadErbDir(Program.ErbDir, Config.DisplayReport, labelDic);
            logWriter.WriteLine($"Proc:Init:ERB:Enumeration {erbLoader.EnumerationMilliseconds}ms");
            logWriter.WriteLine($"Proc:Init:ERB:PrimaryParse {erbLoader.PrimaryParseMilliseconds}ms");
            logWriter.WriteLine($"Proc:Init:ERB:LabelSetup {erbLoader.LabelSetupMilliseconds}ms");
            logWriter.WriteLine($"Proc:Init:ERB:ScriptParse {erbLoader.ScriptParseMilliseconds}ms");
            logWriter.WriteLine($"Proc:Init:ERB:LazyErb files={erbLoader.LazyErbFileCount} fallback={erbLoader.LazyErbFallbackFileCount}");
            logWriter.WriteLine($"Proc:Init:ERB:DeferredEager count={erbLoader.DeferredEagerCount}");
            logWriter.WriteLine($"Proc:Init:ERB:End {stopWatch.ElapsedMilliseconds}ms");
            PerformanceMetrics.MarkStartup("ErbParsed"); // ERB解析完了の目印

            SQL.SetUpTempDB();

            initSystemProcess();
            initialiing = false;

            logWriter.WriteLine($"Proc:Init:End {stopWatch.ElapsedMilliseconds}ms");
        }
        catch (Exception e)
        {
            handleException(e, null, true);
            console.PrintSystemLine(LocalizationManager.Error.InitFatalError);
            return false;
        }
        if (labelDic == null)
        {
            return false;
        }
        state.Begin(BeginType.TITLE);
        return true;
    }

    // [Emuera改修:MEM-13R39 2026-08-22]
    // 通常モードではactive erbLoaderのLazy表を使い、eagerのDebug/Analysisやreload中のloader不在時は
    // 追加処理なしで従来経路を通す。呼び出し側の引数評価・ScopeInより前に判定できる境界を保つ。
    internal bool EnsureFunctionReady(FunctionLabelLine label) => erbLoader?.EnsureFunctionReady(label) ?? true;

    public async Task ReloadErbAll()
    {
        await Preload.Load(Program.ErbDir);
        await Preload.Load(Program.CsvDir);
        saveCurrentState(false);
        state.SystemState = SystemStateCode.System_Reloaderb;
        erbLoader = new(console, exm, this);
        await erbLoader.LoadErbDir(Program.ErbDir, false, labelDic);
        console.ReadAnyKey();
    }

    public async Task ReloadPartialErb(List<string> paths)
    {
        // [Emuera改修:MEM-13R39.1 2026-08-23]
        // active erbLoaderは通常モードで起動時の未hydrate stubとLazy対応表を所有するため、
        // active Lazy対象を含む再読込ではpartial loaderに置換せず、全体を一体で再構築する。
        // Debug/AnalysisではLazyが無効なので、従来どおりpartial reloadを維持する。
        if (erbLoader?.HasRuntimeLazyState == true || paths.Any(LazyErbPolicy.IsActiveTarget))
        {
            // [Emuera改修:MEM-13R41I 2026-08-24]
            // active loaderのLazy/Deferred表はFunctionLabelLine identityを保持する。
            // 別partialLoaderで一部fileだけ差し替えるとold label参照が残るため、runtime stateがある間はfull reloadへ統一する。
            await ReloadErbAll();
            return;
        }
        saveCurrentState(false);
        state.SystemState = SystemStateCode.System_Reloaderb;
        await Preload.Load(paths);
        // [Emuera改修:MEM-13R39.1 2026-08-23]
        // Lazy対象外のpartial reloadだけはlocal loaderに限定し、active Lazy managerを保持する。
        ErbLoader partialLoader = new(console, exm, this);
        await partialLoader.LoadErbList(paths, labelDic);
        console.ReadAnyKey();
    }

    public async Task ReloadErbFolder(string dirPath)
    {
        // [Emuera改修:MEM-13R41F 2026-08-24]
        // ファイルが削除されて列挙結果から消えていても、configured Lazy directoryとのscope交差で
        // full reloadへ昇格し、古いstubをLabelDictionaryへ残さない。親folderの扱いはSearchSubdirectoryに従う。
        if (erbLoader?.HasRuntimeLazyState == true
            || LazyErbPolicy.RequiresFullReloadForDirectory(dirPath, Config.SearchSubdirectory))
        {
            // [Emuera改修:MEM-13R41I 2026-08-24]
            // folder reloadも同じloader ownership規則に揃え、削除済みLazy fileと旧label identityの双方を残さない。
            await ReloadErbAll();
            return;
        }
        var serachOption = SearchOption.TopDirectoryOnly;
        if (Config.SearchSubdirectory)
        {
            serachOption = SearchOption.AllDirectories;
        }

        string[] erbFiles = Directory.EnumerateFiles(dirPath, "", serachOption)
            .Where(x => Path.GetExtension(x).Equals(".erb", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        saveCurrentState(false);
        state.SystemState = SystemStateCode.System_Reloaderb;
        await Preload.Load(dirPath);
        // [Emuera改修:MEM-13R39.1 2026-08-23]
        // active Lazy対象外のfolderだけは従来どおりlocal loaderで部分再読込する。
        ErbLoader partialLoader = new(console, exm, this);
        await partialLoader.LoadErbList(erbFiles, labelDic);
        console.ReadAnyKey();
    }

    public void SetCommnds(Int64 count)
    {
        coms = new List<long>((int)count);
        isCTrain = true;
        Int64[] selectcom = vEvaluator.SELECTCOM_ARRAY;
        if (count >= selectcom.Length)
        {
            throw new CodeEE(LocalizationManager.Error.CalltrainArgMoreThanSelectcom);
        }
        for (int i = 0; i < (int)count; i++)
        {
            coms.Add(selectcom[i + 1]);
        }
    }

    public bool ClearCommands()
    {
        coms.Clear();
        count = 0;
        isCTrain = false;
        skipPrint = true;
        return callFunction("CALLTRAINEND", false, false);
    }

    public void InputResult5(int r0, int r1, int r2, int r3, int r4)
    {
        long[] result = vEvaluator.RESULT_ARRAY;
        result[0] = r0;
        result[1] = r1;
        result[2] = r2;
        result[3] = r3;
        result[4] = r4;
    }
    public void InputInteger(Int64 i)
    {
        vEvaluator.RESULT = i;
    }
    public void SetResultArray(long i, int index)
    {
        vEvaluator.RESULT_ARRAY[index] = i;
    }
    public void InputSystemInteger(Int64 i)
    {
        systemResult = i;
    }
    public void InputString(string s)
    {
        vEvaluator.RESULTS = s;
    }

    public void SetResultsArray(string s, int index)
    {
        vEvaluator.RESULTS_ARRAY[index] = s;
    }
    public void CompileScript()
    {
        var ab = new PersistedAssemblyBuilder(new AssemblyName("MyAssembly"), typeof(object).Assembly);
        var mob = ab.DefineDynamicModule("MyModule");
        var tb = mob.DefineType("MyType", TypeAttributes.Public | TypeAttributes.Class);
        systemProcessDictionary[state.SystemState]();
        var line = state.CurrentLine;

        if (line is FunctionLabelLine functionLabelLine)
        {
            var functionName = functionLabelLine.LabelName;
            var args = functionLabelLine.Arg.Select(x => x.GetOperandType()).ToArray();
            args = [];

            var meb = tb.DefineMethod(functionName, MethodAttributes.Public | MethodAttributes.Static,
                                                                    typeof(object), args);
            var il = meb.GetILGenerator();

            line = functionLabelLine.NextLine;
            while (line != null)
            {
                if (line is InstructionLine instructionLine)
                {
                    il.EmitWriteLine(instructionLine.ToString());
                }
                line = line.NextLine;
            }

            il.Emit(OpCodes.Ldstr, "OK");
            il.Emit(OpCodes.Ret);

            tb.CreateType();
            ab.Save(functionName + ".dll");


            var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(functionName + ".dll"));
            var method = assembly.GetType("MyType").GetMethod(functionName);
            Console.WriteLine(method.Invoke(null, []));
        }
    }

    readonly Stopwatch startTime = new();

    public void DoScript()
    {
        startTime.Restart();
        state.lineCount = 0;
        bool systemProcRunning = true;
        try
        {
            while (true)
            {
                methodStack = 0;
                systemProcRunning = true;
                while (state.ScriptEnd && console.IsRunning)
                    runSystemProc();
                if (!console.IsRunning)
                    break;
                systemProcRunning = false;
                runScriptProc();
            }
        }
        catch (Exception ec)
        {
            LogicalLine currentLine = state.ErrorLine;
            if (currentLine != null && currentLine is NullLine)
                currentLine = null;
            if (systemProcRunning)
                handleExceptionInSystemProc(ec, currentLine, true);
            else
                handleException(ec, currentLine, true);
        }
    }

    public void BeginTitle()
    {
        vEvaluator.ResetData();
        state = originalState;
        state.Begin(BeginType.TITLE);
    }

    public void UpdateCheckInfiniteLoopState()
    {
        startTime.Restart();
        state.lineCount = 0;
    }

    private void checkInfiniteLoop()
    {
        //うまく動かない。BEEP音が鳴るのを止められないのでこの処理なかったことに（1.51）
        ////フリーズ防止。処理中でも履歴を見たりできる
        //System.Windows.Forms.Application.DoEvents();
        ////System.Threading.Thread.Sleep(0);

        //if (!console.Enabled)
        //{
        //    //DoEvents()の間にウインドウが閉じられたらおしまい。
        //    console.ReadAnyKey();
        //    return;
        //}
        var elapsedTime = startTime.ElapsedMilliseconds;
        if (elapsedTime < Config.InfiniteLoopAlertTime)
            return;
        LogicalLine currentLine = state.CurrentLine;
        if ((currentLine == null) || (currentLine is NullLine))
            return;//現在の行が特殊な状態ならスルー
        if (!console.Enabled)
            return;//クローズしてるとMessageBox.Showができないので。
        var text = string.Format(
            LocalizationManager.MsgBox.TooLongLoop,
            currentLine.Position.Value.Filename, currentLine.Position.Value.LineNo, state.lineCount, elapsedTime);
        if (Dialog.ShowPrompt(LocalizationManager.MsgBox.InfiniteLoop, text))
        {
            throw new CodeEE(LocalizationManager.Error.SelectExitInfiniteLoopMB);
        }
        else
        {
            state.lineCount = 0;
            startTime.Restart();
        }
    }

    int methodStack;
    public SingleTerm GetValue(SuperUserDefinedMethodTerm udmt)
    {
        methodStack++;
        if (methodStack > 100)
        {
            //StackOverflowExceptionはcatchできない上に再現性がないので発生前に一定数で打ち切る。
            //環境によっては100以前にStackOverflowExceptionがでるかも？
            throw new CodeEE(LocalizationManager.Error.OverflowFuncStack);
        }
        SingleTerm ret = null;
        int temp_current = state.currentMin;
        state.currentMin = state.functionCount;
        udmt.Call.updateRetAddress(state.CurrentLine);
        try
        {
            state.IntoFunction(udmt.Call, udmt.Argument, exm);
            //do whileの中でthrow されたエラーはここではキャッチされない。
            //#functionを全て抜けてDoScriptでキャッチされる。
            runScriptProc();
            ret = state.MethodReturnValue;
        }
        finally
        {
            if (udmt.Call.TopLabel.hasPrivDynamicVar)
                udmt.Call.TopLabel.ScopeOut();
            //1756beta2+v3:こいつらはここにないとデバッグコンソールで式中関数が事故った時に大事故になる
            state.currentMin = temp_current;
            methodStack--;
        }
        return ret;
    }

    public void clearMethodStack()
    {
        methodStack = 0;
    }

    public int MethodStack()
    {
        return methodStack;
    }

    // [Emuera改修:WARN-04]
    // 並列解析中の「今どの行を解析しているか」を作業スレッドごとに分ける。
    // 1つの共有変数だと、別スレッドの行番号を使って誤警告を出すことがある。
    // 通常の逐次実行時は従来どおりsequentialScanningLineを使う。
    // 参照: プロジェクト資料/06_コード案内.md
    [ThreadStatic]
    private static LogicalLine parallelScanningLine;
    private LogicalLine sequentialScanningLine;
    private volatile bool useThreadLocalScanningLine;
    public LogicalLine scaningLine
    {
        get => useThreadLocalScanningLine ? parallelScanningLine : sequentialScanningLine;
        set
        {
            if (useThreadLocalScanningLine)
                parallelScanningLine = value;
            else
                sequentialScanningLine = value;
        }
    }

    internal void SetParallelScanning(bool enabled)
    {
        useThreadLocalScanningLine = enabled;
        if (!enabled)
            parallelScanningLine = null;
    }
    internal LogicalLine GetScaningLine()
    {
        if (scaningLine != null)
            return scaningLine;
        LogicalLine line = state.ErrorLine;
        if (line == null)
            return null;
        return line;
    }


    private void handleExceptionInSystemProc(Exception exc, LogicalLine current, bool playSound)
    {
        console.ThrowError(playSound);
        if (exc is CodeEE)
        {
            console.PrintError(string.Format(LocalizationManager.Error.FuncEndError));
            console.Print(string.Format(LocalizationManager.SystemLine.EnvironmentInfo,
                AssemblyData.EmueraVersionText, GlobalStatic.GameBaseData?.ScriptWindowTitle));
            console.PrintError(exc.Message);
        }
        else if (exc is ExeEE)
        {
            console.PrintError(string.Format(LocalizationManager.Error.FuncEndEmueraError));
            console.Print(string.Format(LocalizationManager.SystemLine.EnvironmentInfo,
                AssemblyData.EmueraVersionText, GlobalStatic.GameBaseData?.ScriptWindowTitle));
            console.PrintError(exc.Message);
        }
        else
        {
            console.PrintError(string.Format(LocalizationManager.Error.FuncEndUnexpectedError));
            console.Print(string.Format(LocalizationManager.SystemLine.EnvironmentInfo,
                AssemblyData.EmueraVersionText, GlobalStatic.GameBaseData?.ScriptWindowTitle));
            console.PrintError(exc.GetType().ToString() + ":" + exc.Message);
            string[] stack = exc.StackTrace.Split('\n');
            for (int i = 0; i < stack.Length; i++)
            {
                console.PrintError(stack[i]);
            }
        }
    }

    private void handleException(Exception exc, LogicalLine current, bool playSound)
    {
        console.ThrowError(playSound);
        ScriptPosition? position = null;
        if ((exc is EmueraException ee) && (ee.Position != null))
            position = ee.Position;
        else if ((current != null) && (current.Position != null))
            position = current.Position;
        string posString = "";
        if (position != null)
        {
            if (position.Value.LineNo >= 0)
                posString = string.Format(LocalizationManager.Error.ErrorFileAndLine, position.Value.Filename, position.Value.LineNo.ToString());
            else
                posString = string.Format(LocalizationManager.Error.ErrorFile, position.Value.Filename);

        }
        if (exc is CodeEE)
        {
            if (position != null)
            {
                if (current is InstructionLine procline && procline.FunctionCode == FunctionCode.THROW)
                {
                    console.PrintErrorButton(string.Format(LocalizationManager.Error.HasThrow, posString), position);
                    printRawLine(position);
                    console.PrintError(string.Format(LocalizationManager.Error.ThrowMessage, exc.Message));
                }
                else
                {
                    console.PrintErrorButton(string.Format(LocalizationManager.Error.HasError, posString), position);
                    console.Print(string.Format(LocalizationManager.SystemLine.EnvironmentInfo,
                        AssemblyData.EmueraVersionText, GlobalStatic.GameBaseData?.ScriptWindowTitle));
                    printRawLine(position);
                    console.PrintError(string.Format(LocalizationManager.Error.ErrorMessage, exc.Message));
                }
                console.PrintError(string.Format(LocalizationManager.Error.ErrorInFunc, current.ParentLabelLine.LabelName, current.ParentLabelLine.Position.Value.Filename, current.ParentLabelLine.Position.Value.LineNo.ToString()));
                console.PrintError(LocalizationManager.Error.FuncCallStack);
                LogicalLine parent;
                int depth = 0;
                while ((parent = state.GetReturnAddressSequensial(depth++)) != null)
                {
                    if (parent.Position != null)
                    {
                        console.PrintErrorButton(string.Format(LocalizationManager.Error.ErrorFuncStack, parent.Position.Value.Filename, parent.Position.Value.LineNo.ToString(), parent.ParentLabelLine.LabelName), parent.Position);
                    }
                }
            }
            else
            {
                console.PrintError(string.Format(LocalizationManager.Error.HasError, posString));
                console.Print(string.Format(LocalizationManager.SystemLine.EnvironmentInfo,
                    AssemblyData.EmueraVersionText, GlobalStatic.GameBaseData?.ScriptWindowTitle));
                console.PrintError(exc.Message);
            }
        }
        else if (exc is ExeEE)
        {
            console.PrintError(string.Format(LocalizationManager.Error.HasEmueraError, posString));
            console.Print(string.Format(LocalizationManager.SystemLine.EnvironmentInfo,
                AssemblyData.EmueraVersionText, GlobalStatic.GameBaseData?.ScriptWindowTitle));
            console.PrintError(exc.Message);
        }
        else
        {
            console.PrintError(string.Format(LocalizationManager.Error.HasUnexpectedError, posString));
            console.Print(string.Format(LocalizationManager.SystemLine.EnvironmentInfo,
                AssemblyData.EmueraVersionText, GlobalStatic.GameBaseData?.ScriptWindowTitle));
            console.PrintError(exc.GetType() + ":" + exc.Message);
            string[] stack = exc.StackTrace.Split('\n');
            for (int i = 0; i < stack.Length; i++)
            {
                console.PrintError(stack[i]);
            }
        }
    }

    public void printRawLine(ScriptPosition? position)
    {
        string str = getRawTextFormFilewithLine(position);
        if (!string.IsNullOrEmpty(str))
            console.PrintError(str);
    }

    public static string getRawTextFormFilewithLine(ScriptPosition? position)
    {
        string extents = position.Value.Filename[^4..].ToLower();
        if (extents == ".erb")
        {
            return File.Exists(Program.ErbDir + position.Value.Filename)
                ? position.Value.LineNo > 0 ? File.ReadLines(Program.ErbDir + position.Value.Filename, Config.Encode).Skip(position.Value.LineNo - 1).First() : ""
                : "";
        }
        else if (extents == ".csv")
        {
            return File.Exists(Program.CsvDir + position.Value.Filename)
                ? position.Value.LineNo > 0 ? File.ReadLines(Program.CsvDir + position.Value.Filename, Config.Encode).Skip(position.Value.LineNo - 1).First() : ""
                : "";
        }
        else
            return "";
    }

}
