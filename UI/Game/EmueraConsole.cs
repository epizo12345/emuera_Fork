//using System.Drawing.Imaging;
using MinorShift.Emuera.Forms;
//using MinorShift.Emuera.GameData;
using MinorShift.Emuera.GameProc.Function;
using MinorShift.Emuera.Runtime;
using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Runtime.Script.Parser;
using MinorShift.Emuera.Runtime.Script.Statements;
using MinorShift.Emuera.Runtime.Script.Statements.Expression;
using MinorShift.Emuera.Runtime.Utils;
#if LEGACY_ORACLE
using MinorShift.Emuera.Runtime.Diagnostics;
#endif
using MinorShift.Emuera.UI.Game;
using MinorShift.Emuera.UI.Game.Image;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using MinorShift.Emuera.UI.Framework;
using MinorShift.Emuera.UI;
using System.Linq;
using System.Threading;
using MinorShift.Emuera.Runtime.Config.JSON;

namespace MinorShift.Emuera.GameView;

//入出力待ちの状況。
internal enum ConsoleState
{
    Initializing = 0,
    Quit = 5,//QUIT
    Error = 6,//Exceptionによる強制終了
    Running = 7,
    WaitInput = 20,
    Sleep = 21,//DoEvents

    //WaitKey = 1,//WAIT
    //WaitSystemInteger = 2,//Systemが要求するInput
    //WaitInteger = 3,//INPUT
    //WaitString = 4,//INPUTS
    //WaitIntegerWithTimer = 8,
    //WaitStringWithTimer = 9,
    //Timeout = 10,
    //Timeouts = 11,
    //WaitKeyWithTimer = 12,
    //WaitKeyWithTimerF = 13,
    //WaitOneInteger = 14,
    //WaitOneString = 15,
    //WaitOneIntegerWithTimer = 16,
    //WaitOneStringWithTimer = 17,
    //WaitAnyKey = 18,

}

internal enum ConsoleRedraw
{
    None = 0,
    Normal = 1,
}

internal sealed partial class EmueraConsole : IDisposable
{

    public EmueraConsole(MainWindow parent)
    {
        window = parent;

        #region EE_AnchorのCB機能移植
        CBProc = new ClipboardProcessor(parent);
        #endregion


        //1.713 この段階でsetStBarを使用してはいけない
        //setStBar(StaticConfig.DrawLineString);
        state = ConsoleState.Initializing;
        if (Config.FPS > 0)
            msPerFrame = 1000 / (uint)Config.FPS;
        displayLineList = new();
        printBuffer = new PrintStringBuffer(this);

        genericTimer = new();
        genericTimer.Elapsed += tickTimer;
        genericTimer.Interval = 10;
        genericTimer.Enabled = false;
        genericTimer.SynchronizingObject = parent;
        CBG_Clear();//文字列描画用ダミー追加

        redrawTimer = new(TimeSpan.FromMilliseconds(10));
        redrawTask = new Task(
            async () =>
            {
                while (await redrawTimer.WaitForNextTickAsync())
                {
                    if (isRedrawEnabled)
                    {
                        //描画が重いと入力が処理できないので、描画毎に入力を捌く
                        Application.DoEvents();

                        //画面再描画(アニメーション処理)
                        Draw();
                    }
                }
            }
        );
    }
    #region 1823 cbg関連
    private readonly List<ClientBackGroundImage> cbgList = [];
    private GraphicsImage cbgButtonMap;
    private int selectingCBGButtonInt = -1;
    private int lastSelectingCBGButtonInt = -1;
    //ConsoleButtonString selectingButton = null;
    //ConsoleButtonString lastSelectingButton = null;

    sealed class ClientBackGroundImage : IComparable<ClientBackGroundImage>
    {
        /// <summary>
        /// zdepth == 0は文字列用ダミーなので他で使ってはいけない
        /// </summary>
        /// <param name="zdepth"></param>
        internal ClientBackGroundImage(int zdepth)
        { this.zdepth = zdepth; }
        public ASprite Img;
        public ASprite ImgB;
        public int x;
        public int y;
        public readonly int zdepth;
        public bool isButton;
        public int buttonValue;
        public string tooltipString;
        public int CompareTo(ClientBackGroundImage other)
        {
            if (other == null)
                return -1;
            //逆順でSort
            return -zdepth.CompareTo(other.zdepth);
        }
    }
    public void CBG_Clear()
    {
        foreach (ClientBackGroundImage cimg in cbgList)
        {
            //使い捨て無名Imageを一応disposeしておく
            if (cimg.Img != null && cimg.Img.Name.Length == 0)
                cimg.Img.Dispose();
        }
        cbgList.Clear();
        CBG_ClearBMap();
        cbgList.Add(new ClientBackGroundImage(0));
    }

    public void CBG_ClearRange(int zmin, int zmax)
    {
        if (zmin > zmax)
            return;
        for (int i = 0; i < cbgList.Count; i++)
        {
            ClientBackGroundImage cimg = cbgList[i];
            if (cimg.zdepth < zmin || cimg.zdepth > zmax || cimg.zdepth == 0)//0はダミーなので削除しない
                continue;

            //使い捨て無名Imageを一応disposeしておく
            if (cimg.Img != null && cimg.Img.Name.Length == 0)
                cimg.Img.Dispose();
            cbgList.RemoveAt(i);
            i--;
        }
    }

    public void CBG_ClearButton()
    {
        for (int i = 0; i < cbgList.Count; i++)
        {
            ClientBackGroundImage cimg = cbgList[i];
            if (!cimg.isButton)
                continue;

            //使い捨て無名Imageを一応disposeしておく
            if (cimg.Img != null && cimg.Img.Name.Length == 0)
                cimg.Img.Dispose();
            cbgList.RemoveAt(i);
            i--;
        }
        CBG_ClearBMap();
    }

    public void CBG_ClearBMap()
    {
        cbgButtonMap = null;
        selectingCBGButtonInt = -1;
        lastSelectingCBGButtonInt = -1;
    }

    public bool CBG_SetGraphics(GraphicsImage gra, int x, int y, int zdepth)
    {
        if (gra == null || !gra.IsCreated)
            return false;
        return CBG_SetImage(new SpriteG("", gra, new Rectangle(0, 0, gra.Width, gra.Height)), x, y, zdepth);
    }
    public bool CBG_SetImage(ASprite image, int x, int y, int zdepth)
    {
        if (image == null || !image.IsCreated)
            return false;
        ArgumentOutOfRangeException.ThrowIfZero(zdepth);
        ClientBackGroundImage cbg = new(zdepth)
        {
            Img = image,
            x = x,
            y = y
        };
        //cbg.zdepth = zdepth;
        cbgList.Add(cbg);
        cbgList.Sort();
        return true;
    }

    public bool CBG_SetButtonMap(GraphicsImage gra)
    {
        if (gra == null || !gra.IsCreated)
            return false;
        if (cbgButtonMap == gra)
            return false;
        cbgButtonMap = gra;
        selectingCBGButtonInt = -1;
        lastSelectingCBGButtonInt = -1;
        return true;
    }

    public bool CBG_SetButtonImage(int buttonValue, ASprite imageN, ASprite imageB, int x, int y, int zdepth, string tooltip = null)
    {
        ArgumentOutOfRangeException.ThrowIfZero(zdepth);
        ClientBackGroundImage cbg = new(zdepth)
        {
            Img = imageN,
            ImgB = imageB,
            x = x,
            y = y,
            //cbg.zdepth = zdepth;
            isButton = true,
            buttonValue = buttonValue,
            tooltipString = tooltip
        };
        cbgList.Add(cbg);
        cbgList.Sort();
        return true;
    }
    public int ClientWidth { get { return window.MainPicBox.Width; } }
    public int ClientHeight { get { return window.MainPicBox.Height; } }
    #endregion

    const string ErrorButtonsText = "__openFileWithDebug__";
    private readonly MainWindow window;
    #region EE_AnchorのCB機能移植
    public readonly ClipboardProcessor CBProc;
    #endregion


    MinorShift.Emuera.GameProc.Process process;
    ConsoleState state = ConsoleState.Initializing;
    public bool Enabled { get {
#if R0_F6C
        if (Program.R0F6CDisplayCapture) return true;
#endif
        return window.Created;
    } }

    /// <summary>
    /// 現在、Emueraがアクティブかどうか
    /// </summary>
    internal bool IsActive
    { get { return !(window == null || !window.Created || Form.ActiveForm == null); } }

    /// <summary>
    /// スクリプトが継続中かどうか
    /// 入力系はメッセージスキップやマクロも含めてIsInProcessを参照すべき
    /// </summary>
    internal bool IsRunning
    {
        get
        {
            if (state == ConsoleState.Initializing)
                return true;
            return state == ConsoleState.Running || runningERBfromMemory;
        }
    }

    internal bool IsInProcess
    {
        get
        {
            if (state == ConsoleState.Initializing)
                return true;
            if (state == ConsoleState.Sleep)
                return true;
            if (inProcess)
                return true;
            return state == ConsoleState.Running || runningERBfromMemory;
        }
    }

    internal bool IsError
    {
        get
        {
            return state == ConsoleState.Error;
        }
    }

    internal bool IsWaitingEnterKey
    {
        get
        {
            if ((state == ConsoleState.Quit) || (state == ConsoleState.Error))
                return true;
            if (state == ConsoleState.WaitInput)
                return inputReq.InputType == InputType.AnyKey || inputReq.InputType == InputType.EnterKey;
            return false;
        }
    }

    internal bool IsWaitAnyKey
    {
        get
        {
            return state == ConsoleState.WaitInput && inputReq.InputType == InputType.AnyKey;
        }
    }

    internal bool IsWaintingOnePhrase
    {
        get
        {
            return state == ConsoleState.WaitInput && inputReq.OneInput;
        }
    }

    internal bool IsRunningTimer
    {
        get
        {
            return state == ConsoleState.WaitInput && inputReq.Timelimit > 0 && !isTimeout;
        }
    }

    internal bool IsWaitingPrimitive
    {
        get
        {
            if (state == ConsoleState.WaitInput)
                return inputReq.InputType == InputType.PrimitiveMouseKey;
            return false;
        }
    }

    internal string SelectedString
    {
        get
        {
            if (selectingButton == null)
                return null;
            if (state == ConsoleState.Error)
                return selectingButton.Inputs;
            if (state != ConsoleState.WaitInput)
                return null;
            if (inputReq.InputType == InputType.IntValue && selectingButton.IsInteger)
                return selectingButton.Input.ToString();
            if (inputReq.InputType == InputType.StrValue)
                return selectingButton.Inputs;
            return null;
        }
    }

    // [Emuera改修:MOUSE-02]
    // 画面上の「現在有効なボタン」だけを探し、指定候補に合う最初の入力値を返す。
    // 普通のボタンは表示文（「次のページ」「NEXT」など）で探す。
    // HTMLボタンは表示文に番号が出ない場合があるため、実際にゲームへ渡す値（「1007」など）とも完全一致で比べる。
    // 絶対位置へ描くHTML Island（調教画面など）は通常の表示行とは別に保存されるため、両方を検索する。
    // 昔の画面に残ったボタンや、数値入力中の文字列ボタンは対象外にする。
    // 参照: プロジェクト資料/06_コード案内.md
    internal bool TryGetCurrentButtonInputByText(string targetTexts, out string input)
    {
        input = null;
        if (state != ConsoleState.WaitInput || !inputReq.NeedValue || string.IsNullOrWhiteSpace(targetTexts))
            return false;

        string[] targets = targetTexts.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (targets.Length == 0)
            return false;

        // 通常のマウス選択と同じく、画面へ重ねて描くHTML Islandを先に探す。
        foreach (var (_, islandLines) in _htmlElementListDict.Reverse())
        {
            for (int lineIndex = islandLines.Count - 1; lineIndex >= 0; lineIndex--)
            {
                ConsoleButtonString[] buttons = islandLines[lineIndex].Buttons;
                for (int buttonIndex = buttons.Length - 1; buttonIndex >= 0; buttonIndex--)
                    if (TryGetCurrentButtonInput(buttons[buttonIndex], targets, out input))
                        return true;
            }
        }

        // 普通のPRINT系で作られた表示行を、新しいものから順に探す。
        for (int lineIndex = displayLineList.Count - 1; lineIndex >= 0; lineIndex--)
        {
            ConsoleButtonString[] buttons = displayLineList[lineIndex].Buttons;
            for (int buttonIndex = buttons.Length - 1; buttonIndex >= 0; buttonIndex--)
                if (TryGetCurrentButtonInput(buttons[buttonIndex], targets, out input))
                    return true;
        }
        return false;
    }

    // HTMLのdiv内にさらにdivやbuttonが入るため、子要素も再帰的に調べる。
    private bool TryGetCurrentButtonInput(AConsoleDisplayNode node, string[] targets, out string input)
    {
        input = null;
        if (node == null)
            return false;

        if (node is ConsoleButtonString button)
        {
            if (button.IsButton && button.Generation == lastButtonGeneration
                && (inputReq.InputType != InputType.IntValue || button.IsInteger))
            {
                string buttonText = button.ToString();
                string buttonInput = inputReq.InputType == InputType.IntValue ? button.Input.ToString() : button.Inputs;
                bool targetMatched = Array.Exists(targets, target =>
                    buttonText.Contains(target, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(buttonInput, target, StringComparison.OrdinalIgnoreCase));
                if (targetMatched)
                {
                    input = buttonInput;
                    return true;
                }
            }

            for (int childIndex = button.StrArray.Length - 1; childIndex >= 0; childIndex--)
                if (TryGetCurrentButtonInput(button.StrArray[childIndex], targets, out input))
                    return true;
        }
        else if (node is ConsoleDivElement div)
        {
            for (int childIndex = div._childNodes.Count - 1; childIndex >= 0; childIndex--)
                if (TryGetCurrentButtonInput(div._childNodes[childIndex], targets, out input))
                    return true;
        }
        return false;
    }

    public async Task Initialize()
    {
        Program.ProbeNextRuntimeHost("ConsoleInitialize");
        var boottimeDebugStopwatch = Stopwatch.StartNew();
#if LEGACY_ORACLE
        using FileStream? fs = string.IsNullOrWhiteSpace(Program.LegacyOraclePath)
            ? new FileStream(Program.ExeDir + "time.log", FileMode.Create)
            : null;
        using var logWriter = fs is null ? new StreamWriter(Stream.Null) : new StreamWriter(fs);
#else
        using var fs = new FileStream(Program.ExeDir + "time.log", FileMode.Create);
        using var logWriter = new StreamWriter(fs);
#endif
        logWriter.WriteLine("Init:Start");
        _genericTimerStopwatch.Restart();

        //必要なソースファイルを事前にメモリに一気に読み込む
        logWriter.WriteLine("File:Preload:Start");
        Preload.Clear();
        await Preload.Load(Program.ErbDir);
        await Preload.Load(Program.CsvDir);
        Program.ProbeNextRuntimeHost("FixturePreloaded");
#if PERFORMANCE_METRICS
        logWriter.WriteLine($"File:Preload:Cached={Preload.CachedFileCount} LazySkipped={Preload.LazySkippedFileCount}");
#endif

        logWriter.WriteLine("File:Preload:End " + boottimeDebugStopwatch.ElapsedMilliseconds + "ms");

        logWriter.WriteLine("Font:Load:Start " + boottimeDebugStopwatch.ElapsedMilliseconds + "ms");
        FontFactory.LoadFontFolder();
        logWriter.WriteLine("Font:Load:End " + boottimeDebugStopwatch.ElapsedMilliseconds + "ms");

        GlobalStatic.Console = this;
        // GlobalStatic.MainWindow = window;
        process = new GameProc.Process(this);
        GlobalStatic.Process = process;
        Program.ProbeNextRuntimeHost("LegacyProcessCreated");
        if (Program.DebugMode && Config.DebugShowWindow)
        {
            OpenDebugDialog();
            window.Focus();
        }
        ClearDisplay();
        logWriter.WriteLine("Process:Initialize:Start " + boottimeDebugStopwatch.ElapsedMilliseconds + "ms");
        Program.ProbeNextRuntimeHost("LegacyProcessInitialize");
        if (!await process.Initialize(logWriter))
        {
            Program.ProbeNextRuntimeHost("LegacyProcessInitializeFailed");
#if LEGACY_ORACLE
            if (!string.IsNullOrWhiteSpace(Program.LegacyOraclePath))
            {
                LegacyOracleExporter.Write(process.LabelDictionary, Program.LegacyOraclePath, Program.ErbDir, process.LegacyErbBaseline, process.LegacyPreprocessorDiagnostics);
                window.BeginInvoke(window.Close);
                return;
            }
#endif
            state = ConsoleState.Error;
            OutputLog(null);
            PrintFlush(false);
            RefreshStrings(true);
            if (Program.StartupTestMode)
                window.BeginInvoke(window.Close);
            return;
        }
        if (!string.IsNullOrWhiteSpace(Program.NextRuntimeHostProbePath))
        {
            Program.ProbeNextRuntimeHost("FixtureLoaded");
            var probe = Program.NextRuntimeProductionOnly
                ? process.RunNextRuntimeProductionProbe(Program.ProbeNextRuntimeHost)
                : process.RunNextRuntimeHostProbe(Program.ProbeNextRuntimeHost) + process.RunNextRuntimeBuiltinProbe(Program.ProbeNextRuntimeHost) + process.RunNextRuntimeExpressionMethodProbe(Program.ProbeNextRuntimeHost) + process.RunNextRuntimeFrameBridgeProbe(Program.ProbeNextRuntimeHost) + process.RunNextRuntimeBoundaryProbe(Program.ProbeNextRuntimeHost) + process.RunNextRuntimeProductionProbe(Program.ProbeNextRuntimeHost) + process.RunNextRuntimeWaitSessionProbe(Program.ProbeNextRuntimeHost);
            File.WriteAllText(Program.NextRuntimeHostProbePath, probe);
            if (Program.NextRuntimeProductionOnly)
            {
                File.WriteAllText(Program.NextRuntimeHostProbePath + ".memory-stages.tsv", process.ExportNextRuntimeProductionMemoryStages());
                File.WriteAllText(Program.NextRuntimeHostProbePath + ".preparation-timing.tsv", process.ExportNextRuntimeProductionTimingStages());
                File.WriteAllText(Program.NextRuntimeHostProbePath + ".readiness.txt", process.ExportNextRuntimeProductionReadiness());
            }
            Program.ProbeNextRuntimeHost("ProbeComplete");
            window.BeginInvoke(new Action(() => { window.Close(); Application.ExitThread(); }));
            return;
        }
        if (!string.IsNullOrWhiteSpace(Program.NextRuntimeLegacyManifestPath))
        {
            File.WriteAllText(Program.NextRuntimeLegacyManifestPath, process.ExportNextRuntimeLegacyManifest());
            window.BeginInvoke(window.Close);
            return;
        }
#if LEGACY_ORACLE
        if (!string.IsNullOrWhiteSpace(Program.LegacyOraclePath))
        {
            LegacyOracleExporter.Write(process.LabelDictionary, Program.LegacyOraclePath, Program.ErbDir, process.LegacyErbBaseline, process.LegacyPreprocessorDiagnostics);
            window.BeginInvoke(window.Close);
            return;
        }
#endif
        logWriter.WriteLine("Process:Initialize:End " + boottimeDebugStopwatch.ElapsedMilliseconds + "ms");
        logWriter.WriteLine("MacroNames:Start " + boottimeDebugStopwatch.ElapsedMilliseconds + "ms");
        window.SetMacroGroupNames();
        logWriter.WriteLine("MacroNames:End " + boottimeDebugStopwatch.ElapsedMilliseconds + "ms");
        logWriter.WriteLine("RunProgram:Start " + boottimeDebugStopwatch.ElapsedMilliseconds + "ms");
        // [Emuera改修:START-06]
        // タイトル作成用ERBの実行中は、途中経過を何度も画面へ描かず表示データだけ作る。
        // ERB実行後にRefreshStrings(true)を1回呼び、完成したタイトル画面を必ず表示する。
        // 普通のゲーム処理やタイトルの内容を省略するものではない。
        // 参照: プロジェクト資料/06_コード案内.md
        suppressInitialPaint = true;
        try
        {
            RunEmueraProgram("");
        }
        finally
        {
            suppressInitialPaint = false;
        }
        logWriter.WriteLine("RunProgram:End " + boottimeDebugStopwatch.ElapsedMilliseconds + "ms");
        logWriter.WriteLine("RunProgram:Lines " + process.ExecutedLineCount);
        logWriter.WriteLine("Refresh:Start " + boottimeDebugStopwatch.ElapsedMilliseconds + "ms");
        RefreshStrings(true);
        logWriter.WriteLine("Refresh:End " + boottimeDebugStopwatch.ElapsedMilliseconds + "ms");
        PerformanceMetrics.MarkStartup("TitleDisplayed");

        logWriter.WriteLine("Preload:Clear:Start " + boottimeDebugStopwatch.ElapsedMilliseconds + "ms");
        Preload.Clear();
        logWriter.WriteLine("Preload:Clear:End " + boottimeDebugStopwatch.ElapsedMilliseconds + "ms");

        logWriter.WriteLine("Init:End " + boottimeDebugStopwatch.ElapsedMilliseconds + "ms");
        logWriter.Flush();

        // 自動起動試験では、完成した画面内容をログへ残して検査できるようにする。
        if (Program.StartupTestMode)
        {
            OutputLog(Program.ExeDir + "startup-test.log");
        }

        Debug.WriteLine($"GC:{GC.GetTotalMemory(true):N0}");
        Debug.WriteLine($"WorkingSet:{Environment.WorkingSet:N0}");
    }


    public void Quit() { state = ConsoleState.Quit; }
    public void ThrowTitleError(bool error)
    {
        state = ConsoleState.Error;
        notToTitle = true;
        byError = error;
    }
    public void ThrowError(bool playSound)
    {
        if (playSound)
            System.Media.SystemSounds.Hand.Play();
        forceUpdateGeneration();
        UseUserStyle = false;
        PrintFlush(false);
        RefreshStrings(false);
        state = ConsoleState.Error;
    }

    public bool notToTitle;
    public bool byError;
    //public ScriptPosition? ErrPos = null;

    #region button関連
    bool lastButtonIsInput = true;
    public bool updatedGeneration;
    int lastButtonGeneration;//最後に追加された選択肢の世代。これと世代が一致しない選択肢は選択できない。
    int newButtonGeneration;//次に追加される選択肢の世代。Input又はInputsごとに増加
                            //public int LastButtonGeneration { get { return lastButtonGeneration; } }
    public int NewButtonGeneration { get { return newButtonGeneration; } }
    public void UpdateGeneration() { lastButtonGeneration = newButtonGeneration; updatedGeneration = true; }
    public void forceUpdateGeneration() { newButtonGeneration++; lastButtonGeneration = newButtonGeneration; updatedGeneration = true; }
    LogicalLine lastInputLine;

    private void newGeneration()
    {
        //値の入力を求められない時は更新は必要ないはず
        if (state != ConsoleState.WaitInput || !inputReq.NeedValue)
            return;
        if (!updatedGeneration && process.getCurrentLine != lastInputLine)
        {
            //ボタン無しで次の入力に来たなら強制で世代更新
            lastButtonGeneration = newButtonGeneration;
        }
        else
            updatedGeneration = false;
        lastInputLine = process.getCurrentLine;
        //古い選択肢を選択できないように。INPUTで使った選択肢をINPUTSには流用できないように。
        if (inputReq.InputType == InputType.IntValue)
        {
            if (lastButtonGeneration == newButtonGeneration)
                unchecked { newButtonGeneration++; }
            else if (!lastButtonIsInput)
                lastButtonGeneration = newButtonGeneration;
            lastButtonIsInput = true;
        }
        if (inputReq.InputType == InputType.StrValue)
        {
            if (lastButtonGeneration == newButtonGeneration)
                unchecked { newButtonGeneration++; }
            else if (lastButtonIsInput)
                lastButtonGeneration = newButtonGeneration;
            lastButtonIsInput = false;
        }
    }

    /// <summary>
    /// 選択中のボタン。INPUTやINPUTSに対応したものでなければならない
    /// </summary>
    ConsoleButtonString selectingButton;
    ConsoleButtonString lastSelectingButton;
    public ConsoleButtonString SelectingButton { get { return selectingButton; } }
    public bool ButtonIsSelected(ConsoleButtonString button) { return selectingButton == button; }

    /// <summary>
    /// ToolTip表示したフラグ
    /// </summary>
    bool tooltipUsed;
    /// <summary>
    /// マウスの直下にあるテキスト。ボタンであってもよい。
    /// ToolTip表示用。世代無視、履歴中も表示
    /// </summary>
    ConsoleButtonString pointingString;
    ConsoleButtonString lastPointingString;
    #endregion

    #region Input & Timer系

    //bool hasDefValue = false;
    //Int64 defNum;
    //string defStr;

    private InputRequest inputReq;
    public void Await(int time)
    {
        if (!Enabled || state != ConsoleState.Running)
        {
            this.Quit();
            return;
        }
        RefreshStrings(true);
        state = ConsoleState.Sleep;
        process.UpdateCheckInfiniteLoopState();
        System.Windows.Forms.Application.DoEvents();
        long awaitStart = PerformanceMetrics.StartTiming();
        if (time > 0)
            System.Threading.Thread.Sleep(time);
        PerformanceMetrics.AddAwait(time, awaitStart);
        ////DoEvents()の間にウインドウが閉じられたらおしまい。
        //if (!Enabled || state != ConsoleState.Sleep)
        //{
        //	ReadAnyKey();
        //	return;
        //}

        state = ConsoleState.Running;
    }

    public void WaitInput(InputRequest req)
    {
        #region EE_AnchorのCB機能移植
        if (JSONConfig.User.CBUseClipboard)
            CBProc.Check(ClipboardProcessor.CBTriggers.InputWait);
        #endregion

        state = ConsoleState.WaitInput;
        inputReq = req;
#if R0_F6C
        // R0-F6C's headless oracle has no attached Process; the wait state itself is the checkpoint.
        if (Program.R0F6CMode && process is null)
            return;
#endif
        process.TraceR1_4IDifferentialInputBoundary();
        if (req.Timelimit > 0)
        {
            if (req.OneInput)
                window.update_lastinput();
            presetTimer();
            //				setTimer();
        }
        //updateMousePosition();
        //Point point = window.MainPicBox.PointToClient(Control.MousePosition);
        //if (window.MainPicBox.ClientRectangle.Contains(point))
        //{
        //	PrintFlush(false);
        //	MoveMouse(point);
        //}
    }

#if R0_F6C
    internal object R0F6CInputState() => new
    {
        State = state.ToString(),
        Present = inputReq is not null,
        Kind = inputReq?.InputType.ToString() ?? "NONE",
        OneInput = inputReq?.OneInput ?? false,
        NeedValue = inputReq?.NeedValue ?? false
    };
#endif

    public void ReadAnyKey(bool anykey = false, bool stopMesskip = false)
    {
        #region EE_AnchorのCB機能移植
        if (JSONConfig.User.CBUseClipboard)
            CBProc.Check(ClipboardProcessor.CBTriggers.AnyKeyWait);
        #endregion

        InputRequest req = new();
        if (!anykey)
            req.InputType = InputType.EnterKey;
        else
            req.InputType = InputType.AnyKey;
        req.StopMesskip = stopMesskip;
        inputReq = req;
        state = ConsoleState.WaitInput;
        process.TraceR1_4IDifferentialInputBoundary();
        process.NeedWaitToEventComEnd = false;
    }


    /// <summary>
    /// INPUT中のアニメーション用タイマー
    /// </summary>
    PeriodicTimer redrawTimer;
    bool isRedrawEnabled;
    Task redrawTask;

    private void Draw()
    {
#if R0_F6C
        if (Program.R0F6CMode)
            return;
#endif
        //INPUT待ちでないとき、又はタイマー付きINPUT状態の場合はこれ以外の処理に任せる
        if (state != ConsoleState.WaitInput || genericTimer.Enabled)
        {
            return;
        }
        window.Invoke(window.Refresh);//OnPaint発行
    }

    /// <summary>
    /// アニメーション用タイマーの設定。0以下の値を指定するとタイマー停止
    /// </summary>
    public void setRedrawTimer(int tickcount)
    {
        if (tickcount <= 0)
        {
            isRedrawEnabled = false;
            return;
        }

        if (redrawTask.Status == TaskStatus.Created)
        {
            redrawTask.Start();
        }

        isRedrawEnabled = true;
        redrawTimer.Period = TimeSpan.FromMilliseconds(tickcount);
    }



    System.Timers.Timer genericTimer = new();
    Int64 timerID = -1;
    readonly Stopwatch _genericTimerStopwatch = new();//現在のタイマーを開始した時のミリ秒数（WinmmTimer.TickCount基準）
    Int64 timer_endTime;//現在のタイマーを終了する時のTickCountミリ秒数
    bool isTimeout;
    public bool IsTimeOut { get { return isTimeout; } }

    /// <summary>
    /// 1824 TINPUT時に直接タイマーをセットせずに最初の再描画が終わってからタイマーをセットする（そうしないとTINPUTと再描画だけでループしてしまうので）
    /// </summary>
    bool need_settimer;

    private void presetTimer()
    {
        need_settimer = true;

        if (inputReq.DisplayTime)
        {
            var remainingMs = inputReq.Timelimit - _genericTimerStopwatch.ElapsedMilliseconds;
            PrintSingleLine($"{LocalizationManager.SystemLine.Remaining} {remainingMs} ms");
        }
    }
    private void setTimer()
    {
        isTimeout = false;
        timerID = inputReq.ID;
        genericTimer.Enabled = true;
        _genericTimerStopwatch.Restart();
        timer_endTime = inputReq.Timelimit;
    }

    //汎用
    private void tickTimer(object sender, EventArgs e)
    {
        if (!genericTimer.Enabled)
            return;
        if (state != ConsoleState.WaitInput || inputReq.Timelimit <= 0 || timerID != inputReq.ID)
        {
            stopTimer();
            return;
        }
        var elapsedMs = _genericTimerStopwatch.ElapsedMilliseconds;
        if (elapsedMs >= timer_endTime)
        {
            endTimer();
            return;
        }

        if (inputReq.DisplayTime)
        {
            var remainingMs = inputReq.Timelimit - _genericTimerStopwatch.ElapsedMilliseconds;
            window.Invoke(() => changeLastLine($"{LocalizationManager.SystemLine.Remaining} {remainingMs / 1000.0f:0.0}"));
        }
    }

    private void stopTimer()
    {
        //if (state == ConsoleState.WaitKeyWithTimerF && countTime < timeLimit)
        //{
        //	wait_timeout = true;
        //	while (countTime < timeLimit)
        //	{
        //		Application.DoEvents();
        //	}
        //	wait_timeout = false;
        //}
        genericTimer.Enabled = false;
        //timer.Dispose();
    }

    /// <summary>
    /// tickTimerからのみ呼ぶ
    /// </summary>
    private void endTimer()
    {
        stopTimer();
        isTimeout = true;
        if (IsWaitingPrimitive)
        {
            //callEmueraProgramは呼び出し先で行う。
            InputMouseKey(4, 0, 0, 0, 0);
            return;
        }
        if (inputReq.DisplayTime)
            changeLastLine(inputReq.TimeUpMes);
        else if (inputReq.TimeUpMes != null)
            PrintSingleLine(inputReq.TimeUpMes);
        window.Invoke(() =>
        {
            RunEmueraProgram("");//ディフォルト入力の処理はcallEmueraProgram側で
            if (state == ConsoleState.WaitInput && inputReq.NeedValue)
            {
                Point point = window.MainPicBox.PointToClient(Control.MousePosition);
                if (window.MainPicBox.ClientRectangle.Contains(point))
                    MoveMouse(point);
            }
            RefreshStrings(true);
        });

    }

    public void forceStopTimer()
    {
        if (genericTimer.Enabled)
        {
            genericTimer.Enabled = false;
        }
    }
    #endregion

    #region Call系
    /// <summary>
    /// スクリプト実行。RefreshStringsはしないので呼び出し側がすること
    /// </summary>
    /// <param name="input"></param>
    private void RunEmueraProgram(string input)
    {
        //入力文字列の表示処理を行わない場合はstr == null
        if (input != null)
        {
            //INPUT文字列をPRINTする処理など
            if (!doInputToEmueraProgram(input))
                return;
            if (state == ConsoleState.Error)
                return;
        }
        state = ConsoleState.Running;
        //process.CompileScript();
        process.DoScript();
        if (state == ConsoleState.Running)
        {//RunningならProcessは処理を継続するべき
            state = ConsoleState.Error;
            PrintError(LocalizationManager.Error.ProgramStatusError);
        }
        if (state == ConsoleState.Error && !noOutputLog)
            OutputLog(Program.ExeDir + "emuera.log");
        PrintFlush(false);
        //1819 Refreshは呼び出し側で行う
        //RefreshStrings(false);
        newGeneration();
    }

    private bool doInputToEmueraProgram(string str)
    {
        if (state == ConsoleState.WaitInput)
        {
            Int64 inputValue;

            switch (inputReq.InputType)
            {
                case InputType.IntValue:
                    if (string.IsNullOrEmpty(str) && inputReq.HasDefValue && !IsRunningTimer)
                    {
                        inputValue = inputReq.DefIntValue;
                        str = inputValue.ToString();
                    }
                    else if (!Int64.TryParse(str, out inputValue))
                        return false;
                    if (inputReq.IsSystemInput)
                        process.InputSystemInteger(inputValue);
                    else
                        process.InputInteger(inputValue);
                    break;
                case InputType.StrValue:
                    if (string.IsNullOrEmpty(str) && inputReq.HasDefValue && !IsRunningTimer)
                        str = inputReq.DefStrValue;
                    //空入力と時間切れ
                    if (str == null)
                        str = "";
                    process.InputString(str);
                    break;
            }
            stopTimer();
        }
        Print(str);
        PrintFlush(false);
        return true;
    }
    #endregion

    #region 入力系
    readonly string[] spliter = ["\\n", "\r\n", "\n", "\r"];//本物の改行コードが来ることは無いはずだけど一応

    public bool MesSkip;
    private bool inProcess;
    // [Emuera改修:MACRO-01]
    // trueの間も入力・ERB・文字列・表示行はすべて処理し、実ウィンドウの再描画だけを間引く。
    // 参照: プロジェクト資料/06_コード案内.md
    private bool batchingMacroDisplay;
    volatile public bool KillMacro;

    internal void MouseWheel(Point point, int delta)
    {
        if (!IsWaitingPrimitive)
            return;
        //pointはクライアント左上基準の座標。
        //clientPointをクライアント左下基準の座標に置き換え
        Point clientPoint = point;
        clientPoint.Y = point.Y - ClientHeight;
        InputMouseKey(2, delta, clientPoint.X, clientPoint.Y, 0);
    }

    internal void MouseDown(Point point, MouseButtons button)
    {
        if (!IsWaitingPrimitive)
            return;
        //pointはクライアント左上基準の座標。
        //clientPointをクライアント左下基準の座標に置き換え
        Point clientPoint = point;
        clientPoint.Y = point.Y - ClientHeight;
        int buttonNum = -1;
        if (cbgButtonMap != null && cbgButtonMap.IsCreated)
        {
            //マップ画像の左上基準の座標に置き換え
            Point mapPoint = clientPoint;
            mapPoint.Y = clientPoint.Y + cbgButtonMap.Height;
            if (mapPoint.X >= 0 && mapPoint.Y >= 0 && mapPoint.X < cbgButtonMap.Width && mapPoint.Y < cbgButtonMap.Height)
            {
                Color c = cbgButtonMap.GGetColor(mapPoint.X, mapPoint.Y).ToDrawingColor();
                if (c.A == 255)
                {
                    buttonNum = c.ToArgb() & 0xFFFFFF;
                }
            }

        }
        InputMouseKey(1, (int)button, clientPoint.X, clientPoint.Y, buttonNum);
    }

    //1823 Key入力を捕まえる
    internal void PressPrimitiveKey(Keys keycode, Keys keydata, Keys keymod)
    {
        if (IsWaitingPrimitive)
        {
            InputMouseKey(3, (int)keycode, (int)keydata, 0, 0);
            window.TextBox.Clear();
        }
    }

    //1823 Key入力を捕まえる
    internal void InputMouseKey(int type, int result1, int result2, int result3, int result4)
    {
        if (type != 0) //マウス入力を捕まえた
        {
            var pos = window.Invoke(() => window.MainPicBox.PointToClient(Cursor.Position));
            var button = FindButton(pos.X, pos.Y);

            //結果の初期化
            process.SetResultArray(0, 5);
            process.SetResultsArray("", 5);

            if (button != null)
            {
                if (button.IsInteger)
                {
                    process.SetResultArray(button.Input, 5);
                }
                else
                {
                    process.SetResultsArray(button.Inputs, 5);
                }
            }
        }

        process.InputResult5(type, result1, result2, result3, result4);

        inProcess = true;
        try
        {
            //1823 Escキーもマクロも右クリックも不可。単純に押されたキーを送るのみ。
            RunEmueraProgram(null);
            if (state == ConsoleState.WaitInput && inputReq.NeedValue)
            {
                window.Invoke(() =>
                {
                    Point point = window.MainPicBox.PointToClient(Control.MousePosition);
                    if (window.MainPicBox.ClientRectangle.Contains(point))
                        MoveMouse(point);
                });
            }
        }
        finally
        {
            inProcess = false;
        }
        RefreshStrings(true);
    }

    public void PressEnterKey(bool keySkip, string input, bool changedByMouse)
    {
        MesSkip = keySkip;
        if ((state == ConsoleState.Running) || (state == ConsoleState.Initializing))
            return;
        else if (state == ConsoleState.Quit)
        {
            window.Close();
            return;
        }
        else if (state == ConsoleState.Error)
        {
            if (input == ErrorButtonsText && selectingButton != null && selectingButton.ErrPos != null)
            {
                OpenErrorFile(selectingButton.ErrPos);
                return;
            }
            window.Close();
            return;
        }
#if DEBUG
        if (state != ConsoleState.WaitInput || inputReq == null)
            throw new ExeEE("");
#endif
        KillMacro = false;
        long inputLoopStart = 0;
        try
        {
            string[] text;
            if (changedByMouse)//1823 マウスによって入力されたならマクロ解析を行わない
            { text = [input]; }
            else
            {
                if (input.Length > 1 && !inputReq.OneInput && input.StartsWith('@'))
                {
                    doSystemCommand(input);
                    return;
                }
                if (inputReq.InputType == InputType.Void)
                    return;
                if (genericTimer.Enabled &&
                    (inputReq.InputType == InputType.AnyKey || inputReq.InputType == InputType.EnterKey))
                    stopTimer();
                //if((inputReq.InputType == InputType.IntValue || inputReq.InputType == InputType.StrValue)
                if (input.Contains('(', StringComparison.Ordinal))
                {
                    PerformanceMetrics.BeginMacro(input);
                    long expansionStart = PerformanceMetrics.StartTiming();
                    input = parseInput(new CharStream(input), false);
                    PerformanceMetrics.AddExpansion(expansionStart);
                }
                text = input.Split(spliter, StringSplitOptions.None);
                PerformanceMetrics.SetExpandedInputCount(text.Length);
            }

            inProcess = true;
            // 展開結果が複数入力のときだけ描画集約を有効化。単発の手入力には影響させない。
            batchingMacroDisplay = text.Length > 1;
            inputLoopStart = PerformanceMetrics.StartTiming();
            for (int i = 0; i < text.Length; i++)
            {
                PerformanceMetrics.RecordInputDispatch();
                long handoffStart = PerformanceMetrics.StartTiming();
                string inputs = text[i];
                if (inputs.Contains("\\e", StringComparison.Ordinal))
                {
                    inputs = inputs.Replace("\\e", "", StringComparison.Ordinal);//\eの除去
                    MesSkip = true;
                }

                if (inputReq.OneInput && (!Config.AllowLongInputByMouse || !changedByMouse) && inputs.Length > 1)
                    inputs = inputs.Remove(1);
                //1819 TODO:入力無効系（強制待ちTWAIT）でスキップとマクロを止めるかそのままか
                //現在はそのまま。強制待ち中はスキップの開始もできないのにスキップ中なら飛ばせる。
                if (inputReq.InputType == InputType.Void)
                {
                    i--;
                    inputs = "";
                }
                PerformanceMetrics.AddInputHandoff(handoffStart);
                long erbStart = PerformanceMetrics.StartTiming();
                RunEmueraProgram(inputs);
                PerformanceMetrics.AddErb(erbStart);
                RefreshStrings(false);
                while (MesSkip && state == ConsoleState.WaitInput)
                {
                    //TODO:入力無効を通していいか？スキップ停止をマクロでは飛ばせていいのか？
                    if (inputReq.NeedValue)
                        break;
                    if (inputReq.StopMesskip)
                        break;
                    erbStart = PerformanceMetrics.StartTiming();
                    RunEmueraProgram("");
                    PerformanceMetrics.AddErb(erbStart);
                    RefreshStrings(false);
                    //EscがマクロストップかつEscがスキップ開始だからEscでスキップを止められても即開始しちゃったりするからあんまり意味ないよね
                    //if (KillMacro)
                    //	goto endMacro;
                }
                MesSkip = false;
                if (state != ConsoleState.WaitInput)
                    break;
                //マクロループ時は待ち処理が起こらないのでここでシステムキューを捌く
                long eventStart = PerformanceMetrics.StartTiming();
                // [Emuera改修:MACRO-02]
                // 描画をまとめても入力ごとにWindowsのイベントを処理し、Esc中断と応答性を維持する。
                Application.DoEvents();
                PerformanceMetrics.AddLoopEvent(eventStart);
#if DEBUG
                if (state != ConsoleState.WaitInput || inputReq == null)
                    throw new ExeEE("");
#endif
                if (KillMacro)
                {
                    break;
                }
            }
        }
        finally
        {
            PerformanceMetrics.AddInputLoop(inputLoopStart);
            // 例外や中断でも必ず通常描画へ戻すためfinally内で解除する。
            batchingMacroDisplay = false;
            inProcess = false;
        }

        endMacro();

        void endMacro()
        {
            if (state == ConsoleState.WaitInput && inputReq.NeedValue)
            {
                Point point = window.MainPicBox.PointToClient(Control.MousePosition);
                if (window.MainPicBox.ClientRectangle.Contains(point))
                    MoveMouse(point);
            }
            // マクロ終了時は必ず最終画面を描く。途中描画を集約しても最終表示は欠けない。
            RefreshStrings(true);
            MacroResult result = PerformanceMetrics.FinishMacro(KillMacro);
            if (result != null)
            {
                string stateHash = process.GetBenchmarkStateHash();
                var displayState = GetBenchmarkDisplayState();
                PerformanceMetrics.WriteMacro(result, stateHash, displayState.Hash, displayState.LineCount);
            }
        }
    }

    private void OpenErrorFile(ScriptPosition? pos)
    {
        ProcessStartInfo pInfo = new()
        {
            FileName = Config.TextEditor
        };
        var ignoreCaseCmp = StringComparison.OrdinalIgnoreCase;
        string fname = pos.Value.Filename.ToUpper();
        if (fname.EndsWith(".CSV", ignoreCaseCmp))
        {
            if (fname.Contains(Program.CsvDir, ignoreCaseCmp))
                fname = fname.Replace(Program.CsvDir, "", ignoreCaseCmp);
            fname = Program.CsvDir + fname;
        }
        else
        {
            //解析モードの場合は見ているファイルがERB\の下にあるとは限らないかつフルパスを持っているのでこの補正はしなくてよい
            if (!Program.AnalysisMode)
            {
                if (fname.Contains(Program.ErbDir, ignoreCaseCmp))
                    fname = fname.Replace(Program.ErbDir, "", ignoreCaseCmp);
                fname = Path.Combine(Program.ErbDir + fname);
            }
        }
        switch (Config.EditorType)
        {
            case TextEditorType.SAKURA:
                pInfo.Arguments = "-Y=" + pos.Value.LineNo.ToString() + " \"" + fname + "\"";
                break;
            case TextEditorType.TERAPAD:
                pInfo.Arguments = "/jl=" + pos.Value.LineNo.ToString() + " \"" + fname + "\"";
                break;
            case TextEditorType.EMEDITOR:
                pInfo.Arguments = "/l " + pos.Value.LineNo.ToString() + " \"" + fname + "\"";
                break;
            case TextEditorType.USER_SETTING:
                if (!string.IsNullOrEmpty(Config.EditorArg) && Config.EditorArg != null)
                    pInfo.Arguments = Config.EditorArg + pos.Value.LineNo.ToString() + " \"" + fname + "\"";
                else
                    pInfo.Arguments = fname;
                break;
        }
        try
        {
            System.Diagnostics.Process.Start(pInfo);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            System.Media.SystemSounds.Hand.Play();
            PrintError(LocalizationManager.Error.FailedOpenEditor);
            forceUpdateGeneration();
        }
        return;
    }

    static string parseInput(CharStream st, bool isNest)
    {
        StringBuilder sb = new(20);
        StringBuilder num = new(20);
        bool hasRet = false;
        while (!st.EOS && (!isNest || st.Current != ')'))
        {
            if (st.Current == '(')
            {
                st.ShiftNext();
                string tstr = parseInput(st, true);

                if (!st.EOS)
                {
                    st.ShiftNext();
                    if (st.Current == '*')
                    {
                        st.ShiftNext();
                        while (char.IsNumber(st.Current))
                        {
                            num.Append(st.Current);
                            st.ShiftNext();
                        }
                        var numStr = num.ToString();
                        if (!string.IsNullOrEmpty(numStr))
                        {
                            var res = int.Parse(numStr);
                            for (int i = 0; i < res; i++)
                                sb.Append(tstr);
                            num.Remove(0, num.Length);
                        }
                    }
                    else
                        sb.Append(tstr);
                    continue;
                }
                else
                {
                    sb.Append(tstr);
                    break;
                }
            }
            else if (st.Current == '\\')
            {
                st.ShiftNext();
                switch (st.Current)
                {
                    case 'n':
                        if (!hasRet)
                            sb.Append('\n');
                        else
                            hasRet = false;
                        break;
                    case 'r':
                        sb.Append('\r');
                        break;
                    case 'e':
                        sb.Append("\\e\n");
                        hasRet = true;
                        break;
                    case '\n':
                        break;
                    default:
                        sb.Append(st.Current);
                        break;
                }
            }
            else
                sb.Append(st.Current);
            st.ShiftNext();
        }
        return sb.ToString();
    }


    bool runningERBfromMemory;
    /// <summary>
    /// 通常コンソールからのDebugコマンド、及びデバッグウインドウの変数ウォッチなど、
    /// *.ERBファイルが存在しないスクリプトを実行中
    /// 1750 IsDebugから改名
    /// </summary>
    public bool RunERBFromMemory { get { return runningERBfromMemory; } set { runningERBfromMemory = value; } }
    void doSystemCommand(string command)
    {
        if (genericTimer.Enabled)
        {
            PrintError(LocalizationManager.Error.CanNotInputTimerWait);
            PrintError("");//タイマー表示処理に消されちゃうかもしれないので
            RefreshStrings(true);
            return;
        }
        if (IsInProcess)
        {
            PrintError(LocalizationManager.Error.CanNotInputScriptRunning);
            RefreshStrings(true);
            return;
        }
        StringComparison sc = Config.StringComparison;
        Print(command);
        PrintFlush(false);
        RefreshStrings(true);
        string com = command[1..];
        if (com.Length == 0)
            return;
        if (com.Equals("REBOOT", sc))
        {
            window.Reboot();
            return;
        }
        else if (com.Equals("OUTPUT", sc) || com.Equals("OUTPUTLOG", sc))
        {
            this.OutputLog(Program.ExeDir + "emuera.log");
            return;
        }
        else if (com.Equals("QUIT", sc) || com.Equals("EXIT", sc))
        {
            window.Close();
            return;
        }
        else if (com.Equals("CONFIG", sc))
        {
            window.ShowConfigDialog();
            return;
        }
        else if (com.Equals("DEBUG", sc))
        {
            if (!Program.DebugMode)
            {
                PrintError(LocalizationManager.Error.CanNotUseDebugWindow);
                RefreshStrings(true);
                return;
            }
            OpenDebugDialog();
        }
        else
        {
            if (!Config.UseDebugCommand)
            {
                PrintError(LocalizationManager.Error.CanNotUseDebugCommand);
                RefreshStrings(true);
                return;
            }
            //処理をDebugMode系へ移動
            DebugCommand(com, Config.ChangeMasterNameIfDebug, false);
            PrintFlush(false);
        }
        RefreshStrings(true);
    }
    #endregion

    #region 描画系
    Stopwatch _frameDeltaTimer = Stopwatch.StartNew();
    // [Emuera改修:MACRO-03]
    // 複数入力マクロ中の実画面更新上限。ゲーム処理回数ではなく、見た目の更新回数だけを制限する。
    const uint MacroMaxFramesPerSecond = 30;
    uint msPerFrame = 1000 / 60;//60FPS
    ConsoleRedraw redraw = ConsoleRedraw.Normal;
    bool suppressInitialPaint;
    public ConsoleRedraw Redraw { get { return redraw; } }
    public void SetRedraw(Int64 i)
    {
        if ((i & 1) == 0)
            redraw = ConsoleRedraw.None;
        else
            redraw = ConsoleRedraw.Normal;
        if ((i & 2) != 0)
            RefreshStrings(true);
    }

    string debugTitle;
    public void SetWindowTitle(string str)
    {
        if (Program.DebugMode)
        {
            debugTitle = str;
            window.Text = str + " (Debug Mode)";
        }
        else
            window.Text = str;
    }

    public string GetWindowTitle()
    {
        if (Program.DebugMode && debugTitle != null)
            return debugTitle;
        return window.Text;
    }


    /// <summary>
    /// 1818以前のRefreshStringsからselectingButton部分を抽出
    /// ここでOnPaintを発行
    /// </summary>
    public void RefreshStrings(bool force_Paint)
    {
#if R0_B1
        if (MinorShift.Emuera.Runtime.Diagnostics.B1Proof.Active) return;
#endif
        long refreshStart = PerformanceMetrics.StartTiming();
        try
        {
        // 起動試験で初期描画を止めている間は、重いスクロール・OnPaintへ進まない。
        if (suppressInitialPaint)
            return;
        bool isBackLog = window.ScrollBar.Value != window.ScrollBar.Maximum;
        //ログ表示はREDRAWの設定に関係なく行うようにする
        if ((redraw == ConsoleRedraw.None) && (!force_Paint) && (!isBackLog))
            return;
        //選択中ボタンの適性チェック
        if (selectingButton != null)
        {
            //履歴表示中は選択肢無効→画面外に出てしまったボタンも履歴から選択できるように
            //if (isBackLog)
            //	selectingButton = null;
            //数値か文字列の入力待ち状態でなければ無効
            if (state != ConsoleState.Error && state != ConsoleState.WaitInput)
                selectingButton = null;
            else if ((state == ConsoleState.WaitInput) && !inputReq.NeedValue)
                selectingButton = null;
            //選択肢が最新でないなら無効
            else if (selectingButton.Generation != lastButtonGeneration)
                selectingButton = null;
        }
        if (!force_Paint)
        {//forceならば確実に再描画。
         //履歴表示中でなく、最終行を表示済みであり、選択中ボタンが変更されていないなら更新不要
            if ((!isBackLog) && (lastDrawnLineNo == lineNo) && (lastSelectingButton == selectingButton))
                return;
            //まだ書き換えるタイミングでないなら次の更新を待ってみる。
            //複数入力マクロでは入力待ち状態も対象にし、設定FPSを尊重しつつ最大30FPSに抑える。
            // 通常は設定FPS、マクロ中だけはそれより速くても最大30FPSに丸める。
            uint refreshInterval = batchingMacroDisplay ? Math.Max(msPerFrame, 1000u / MacroMaxFramesPerSecond) : msPerFrame;
            if (_frameDeltaTimer.ElapsedMilliseconds < refreshInterval &&
                (batchingMacroDisplay || state == ConsoleState.Running || state == ConsoleState.Initializing))
                return;
        }

        if (forceTextBoxColor)
        {
            var sec = _genericTimerStopwatch.ElapsedMilliseconds;
            //色変化が速くなりすぎないように一定時間以内の再呼び出しは強制待ちにする
            if (_drawStopwatch == null)
            {
                _drawStopwatch = Stopwatch.StartNew();
            }
            else
            {
                while (_drawStopwatch.ElapsedMilliseconds < msPerFrame)
                {
                    Application.DoEvents();
                }
            }
            window.TextBox.BackColor = this.bgColor.ToDrawingColor();

            _drawStopwatch.Restart();
        }
        window.Invoke(() =>
        {
            //描画が重いと入力が処理できないので、描画毎に入力を捌く
            long eventStart = PerformanceMetrics.StartTiming();
            Application.DoEvents();
            PerformanceMetrics.AddRefreshEvent(eventStart);

            verticalScrollBarUpdate();
            window.MainPicBox.Refresh();//OnPaint発行
        });
        }
        finally
        {
            PerformanceMetrics.AddRefresh(refreshStart);
        }
    }


    SortedDictionary<int, List<ConsoleDisplayLine>> _htmlElementListDict = new();

    /// <summary>
    /// 1818以前のRefreshStringsの後半とm_RefreshStringsを融合
    /// 全面Clear法のみにしたのでさっぱりした。ダブルバッファリングはOnPaintが勝手にやるはず
    /// </summary>
    /// <param name="graph"></param>
    public void OnPaint(SKCanvas graph)
    {
        //描画中にEmueraが閉じられると廃棄されたPictureBoxにアクセスしてしまったりするので
        //OnPaintからgraphをもらった直後だから大丈夫だとは思うけど一応
        if (!this.Enabled)
            return;
        long paintStart = PerformanceMetrics.StartTiming();
        try
        {

        //デバッグ用。描画が超重い環境を想定1
        //Task.Delay(100).Wait();

        //1824 アニメスプライト用・現在フレームの時間を決定
        _frameDeltaTimer.Restart();

        bool isBackLog = window.ScrollBar.Value != window.ScrollBar.Maximum;
        int pointY = window.MainPicBox.Height - Config.LineHeight;


        int bottomLineNo = window.ScrollBar.Value - 1;
        int topLineNo = bottomLineNo - (pointY / Config.LineHeight + 1);
        if (topLineNo < 0)
            topLineNo = 0;
        pointY -= (bottomLineNo - topLineNo) * Config.LineHeight;

        graph.Clear(this.bgColor);
        //1823 cbg追加
        for (int j = 0; j < cbgList.Count; j++)
        {
            if (cbgList[j].zdepth == 0)
            {
                //1823以前の文字列描画
                for (int i = topLineNo;
                i <= bottomLineNo &&
                i < displayLineList.Count;//何処かで非同期にDisplayLineListを触ってるやつがいる気がする...
                i++)
                {
                    displayLineList[i].DrawTo(graph, pointY, isBackLog, true, Config.TextDrawingMode);
                    pointY += Config.LineHeight;
                }
                continue;
            }
            ASprite img = cbgList[j].Img;
            if (cbgList[j].isButton && cbgList[j].buttonValue == selectingCBGButtonInt)
                img = cbgList[j].ImgB;
            if (img == null || !img.IsCreated)
                continue;
            img.GraphicsDraw(graph, new Point(cbgList[j].x, cbgList[j].y + window.MainPicBox.Height - img.DestBaseSize.Height));
            //Bitmap bmp = img.Bitmap;
            //graph.DrawImage(bmp,
            //	new Rectangle(cbgList[j].x + img.DestBasePosition.X, window.MainPicBox.Height - img.SrcRectangle.Height + cbgList[j].y + img.DestBasePosition.Y, img.SrcRectangle.Width, img.SrcRectangle.Height),
            //	img.SrcRectangle, GraphicsUnit.Pixel);
        }

        //真のHTML描画
        foreach (var (_, list) in _htmlElementListDict)
        {
            var y = 0;
            foreach (var elem in list)
            {
                elem.DrawTo(graph, y, false, false, Config.TextDrawingMode);
                y += Config.LineHeight;

            }
        }

        //ToolTip描画
        if (lastPointingString != pointingString || lastSelectingCBGButtonInt != selectingCBGButtonInt)
        {
            if (tooltipUsed)
                window.ToolTip.RemoveAll();
            string title = null;
            if (pointingString != null)
                title = pointingString.Title;
            else if (selectingCBGButtonInt > 0)
            {
                foreach (var cbg in cbgList)
                {
                    if (!cbg.isButton || cbg.buttonValue != selectingCBGButtonInt)
                        continue;
                    if (string.IsNullOrEmpty(cbg.tooltipString))
                        continue;
                    title = cbg.tooltipString;
                    break;
                }
            }
            if (!string.IsNullOrEmpty(title))
            {
                if (tooltip_duration == 0)
                {
                    window.ToolTip.SetToolTip(window.MainPicBox, title);
                }
                else
                {
                    if (window.ToolTip.InitialDelay == 0)
                    {
                        Point mousePos = window.MainPicBox.PointToClient(MainWindow.MousePosition);
                        window.ToolTip.Show(title, window.MainPicBox, new Point(mousePos.X, mousePos.Y + 18), tooltip_duration);
                    }
                    else
                    {
                        System.Threading.SynchronizationContext context = System.Threading.SynchronizationContext.Current;
                        System.Threading.Tasks.Task.Run(async () =>
                        {
                            ConsoleButtonString savedPointingString = pointingString;
                            await System.Threading.Tasks.Task.Delay(window.ToolTip.InitialDelay);
                            context.Post((state) =>
                            {
                                MoveMouse(GetMousePosition());
                                if (lastPointingString == savedPointingString)
                                {
                                    Point mousePos = window.MainPicBox.PointToClient(MainWindow.MousePosition);
                                    window.ToolTip.Show(title, window.MainPicBox, new Point(mousePos.X, mousePos.Y + 18), tooltip_duration);
                                }
                            }, null);
                        });
                    }
                }
                tooltipUsed = true;
            }
            lastPointingString = pointingString;
            lastSelectingCBGButtonInt = selectingCBGButtonInt;
        }
        if (isBackLog)
            lastDrawnLineNo = -1;
        else
            lastDrawnLineNo = lineNo;
        lastSelectingButton = selectingButton;
        /*デバッグ用。描画が超重い環境を想定2
			System.Threading.Thread.Sleep(50);
			*/
        forceTextBoxColor = false;
        if (need_settimer)
        {
            need_settimer = false;
            setTimer();
        }
        }
        finally
        {
            PerformanceMetrics.AddPaint(paintStart);
        }
    }

    public void SetToolTipColor(Color foreColor, Color backColor)
    {
        window.ToolTip.ForeColor = foreColor;
        window.ToolTip.BackColor = backColor;

    }
    public void SetToolTipDelay(int delay)
    {
        window.ToolTip.InitialDelay = delay;
    }

    int tooltip_duration;
    public void SetToolTipDuration(int duration)
    {
        tooltip_duration = duration;
    }


    //private Graphics getGraphics()
    //{
    //	//消したいが怖いので残し
    //	if (!window.Created)
    //		throw new ExeEE("存在しないウィンドウにアクセスした");
    //	//if (Config.UseImageBuffer)
    //	//	return Graphics.FromImage(window.MainPicBox.Image);
    //	//else
    //		return window.MainPicBox.CreateGraphics();
    //}

    #endregion

    #region DebugMode系
    DebugDialog dd;
    public DebugDialog DebugDialog { get { return dd; } }
    StringBuilder dConsoleLog = new("");
    public string DebugConsoleLog { get { return dConsoleLog.ToString(); } }
    List<string> dTraceLogList = [];
    public string GetDebugTraceLog(bool force)
    {
        //if (!dTraceLogChanged && !force)
        //	return null;
        StringBuilder builder = new("");
        LogicalLine line = process.GetScaningLine();
        builder.AppendLine(LocalizationManager.SystemLine.Processing);
        if ((line == null) || (line.Position == null))
        {
            builder.AppendLine(LocalizationManager.SystemLine.FileNone);
            builder.AppendLine(LocalizationManager.SystemLine.LineFuncNone);
            builder.AppendLine("");
        }
        else
        {
            builder.AppendLine(string.Format(LocalizationManager.SystemLine.FileName, line.Position.Value.Filename));
            builder.AppendLine(string.Format(LocalizationManager.SystemLine.LineFuncName, line.Position.Value.LineNo.ToString(), line.ParentLabelLine.LabelName));
            builder.AppendLine("");
        }
        builder.AppendLine(LocalizationManager.SystemLine.FuncCallStack);
        for (int i = dTraceLogList.Count - 1; i >= 0; i--)
        {
            builder.AppendLine(dTraceLogList[i]);
        }
        return builder.ToString();
    }
    public void OpenDebugDialog()
    {
        if (!Program.DebugMode)
            return;
        if (dd != null)
        {
            if (dd.Created)
            {
                dd.Focus();
                return;
            }
            else
            {
                dd.Dispose();
                dd = null;
            }
        }
        dd = new DebugDialog();
        dd.SetParent(this, process);
        dd.Show();
    }

    public void DebugPrint(string str)
    {
        if (!Program.DebugMode)
            return;
        dConsoleLog.Append(str);
    }

    public void DebugClear()
    {
        dConsoleLog.Remove(0, dConsoleLog.Length);
    }

    public void DebugNewLine()
    {
        if (!Program.DebugMode)
            return;
        dConsoleLog.Append(Environment.NewLine);
    }

    public void DebugAddTraceLog(string str)
    {
        //Emueraがデバッグモードで起動されていないなら無視
        //ERBファイル以外のもの(デバッグコマンド、変数ウォッチ)を実行中なら無視
        if (!Program.DebugMode || runningERBfromMemory)
            return;
        dTraceLogList.Add(str);
    }
    public void DebugRemoveTraceLog()
    {
        if (!Program.DebugMode || runningERBfromMemory)
            return;
        if (dTraceLogList.Count > 0)
            dTraceLogList.RemoveAt(dTraceLogList.Count - 1);
    }
    public void DebugClearTraceLog()
    {
        if (!Program.DebugMode || runningERBfromMemory)
            return;
        dTraceLogList.Clear();
    }

    public void DebugCommand(string com, bool munchkin, bool outputDebugConsole)
    {
        ConsoleState temp_state = state;
        runningERBfromMemory = true;
        //スクリプト等が失敗した場合に備えて念のための保存
        GlobalStatic.Process.saveCurrentState(false);
        try
        {
            LogicalLine line = null;
            if (!com.StartsWith('@') && !com.StartsWith('"') && !com.StartsWith('\\'))
            {
                if (Config.UseRenameFile)
                {
                    com = Rename.RenameString(com);
                }
                line = LogicalLineParser.ParseLine(com, null);
            }
            if (line == null || (line is InvalidLine))
            {
                WordCollection wc = LexicalAnalyzer.Analyse(new CharStream(com), LexEndWith.EoL, LexAnalyzeFlag.None);
                AExpression term = ExpressionParser.ReduceExpressionTerm(wc, TermEndWith.EoL);
                if (term == null)
                    throw new CodeEE(LocalizationManager.Error.CanNotInterpretedLine);
                if (term.GetOperandType() == typeof(Int64))
                {
                    if (outputDebugConsole)
                        com = "DEBUGPRINTFORML {" + com + "}";
                    else
                        com = "PRINTVL " + com;
                }
                else
                {
                    if (outputDebugConsole)
                        com = "DEBUGPRINTFORML %" + com + "%";
                    else
                        com = "PRINTFORMSL " + com;
                }
                line = LogicalLineParser.ParseLine(com, null);
            }
            if (line == null)
                throw new CodeEE(LocalizationManager.Error.CanNotInterpretedLine);
            if (line is InvalidLine)
                throw new CodeEE(line.ErrMes);
            if (!(line is InstructionLine))
                throw new CodeEE(LocalizationManager.Error.InvalidDebugCommand);
            InstructionLine func = (InstructionLine)line;
            if (func.Function.IsFlowContorol())
                throw new CodeEE(LocalizationManager.Error.CanNotUseFlowInstruction);
            //__METHOD_SAFE__をみるならいらないかも
            if (func.Function.IsWaitInput())
                throw new CodeEE(string.Format(LocalizationManager.Error.CanNotUseInstruction, func.Function.Name));
            //1750 __METHOD_SAFE__とほぼ条件同じだよねってことで
            if (!func.Function.IsMethodSafe())
                throw new CodeEE(string.Format(LocalizationManager.Error.CanNotUseInstruction, func.Function.Name));
            //1756 SIFの次に来てはいけないものはここでも不可。
            if (func.Function.IsPartial())
                throw new CodeEE(string.Format(LocalizationManager.Error.CanNotUseInstruction, func.Function.Name));
            switch (func.FunctionCode)
            {//取りこぼし
             //逆にOUTPUTLOG、QUITはDebugCommandの前に捕まえる
                case FunctionCode.PUTFORM:
                case FunctionCode.UPCHECK:
                case FunctionCode.CUPCHECK:
                case FunctionCode.SAVEDATA:
                    throw new CodeEE(string.Format(LocalizationManager.Error.CanNotUseInstruction, func.Function.Name));
            }
            ArgumentParser.SetArgumentTo(func);
            if (func.IsError)
                throw new CodeEE(func.ErrMes);
            process.DoDebugNormalFunction(func, munchkin);
            if (func.FunctionCode == FunctionCode.SET)
            {
                if (!outputDebugConsole)
                    PrintSingleLine(com);
                //DebugWindowのほうは少しくどくなるのでいらないかな
            }
        }
        catch (Exception e)
        {
            if (outputDebugConsole)
            {
                DebugPrint(e.Message);
                DebugNewLine();
            }
            else
                PrintError(e.Message);
            process.clearMethodStack();
        }
        finally
        {
            //確実に元の状態に戻す
            GlobalStatic.Process.loadPrevState();
            runningERBfromMemory = false;
            state = temp_state;
        }
    }
    #endregion

    #region Window.Form系

    internal Point GetMousePosition()
    {
        if (window == null || !window.Created)
            return new Point();
        //クライアント左上基準の座標取得
        Point pos = window.MainPicBox.PointToClient(Cursor.Position);
        //クライアント左下基準の座標に置き換え
        pos.Y -= ClientHeight;
        return pos;
    }

    /// <summary>
    /// マウス位置をボタンの選択状態に反映させる
    /// </summary>
    /// <param name="point"></param>
    /// <returns>この後でRefreshStringsが必要かどうか</returns>
    public bool MoveMouse(Point point)
    {

        if (cbgButtonMap != null && cbgButtonMap.IsCreated)
        {
            //pointはクライアント左上基準の座標。
            //clientPointをクライアント左下基準の座標に置き換え
            Point clientPoint = point;
            clientPoint.Y = point.Y - ClientHeight;
            int buttonNum = -1;
            //マップ画像の左上基準の座標に置き換え
            Point mapPoint = clientPoint;
            mapPoint.Y = mapPoint.Y + cbgButtonMap.Height;
            if (mapPoint.X >= 0 && mapPoint.Y >= 0 && mapPoint.X < cbgButtonMap.Width && mapPoint.Y < cbgButtonMap.Height)
            {
                Color c = cbgButtonMap.GGetColor(mapPoint.X, mapPoint.Y).ToDrawingColor();
                if (c.A == 255)
                {
                    buttonNum = c.ToArgb() & 0xFFFFFF;
                }
            }
            if (buttonNum >= 0)
            {
                bool ret = pointingString != null || selectingButton != null || buttonNum != selectingCBGButtonInt;
                selectingCBGButtonInt = buttonNum;
                pointingString = null;
                selectingButton = null;
                return ret;
            }
            else if (selectingCBGButtonInt >= 0)
            {
                selectingCBGButtonInt = -1;
                pointingString = null;
                selectingButton = null;
                return true;
            }
        }
        selectingCBGButtonInt = -1;
        ConsoleButtonString select = null;
        ConsoleButtonString pointing = null;
        bool canSelect = false;
        //数値か文字列の入力待ち状態でなければ選択中にはならない
        if (state == ConsoleState.Error)
            canSelect = true;
        else if (state == ConsoleState.WaitInput && inputReq.NeedValue)
            canSelect = true;
        //スクリプト実行中は無視//入力・マクロ処理中は無視
        if (this.IsInProcess)
            goto end;
        //履歴表示中は無視
        //if (window.ScrollBar.Value != window.ScrollBar.Maximum)
        //	goto end;

        pointing = FindButton(point.X, point.Y);


        //int posy_bottom2up = window.MainPicBox.Height - pointY;
        //int logNum = window.ScrollBar.Maximum - window.ScrollBar.Value;
        ////表示中の一番下の行番号
        //int curBottomLineNo = displayLineList.Count - logNum;
        //int curPointingLineNo = curBottomLineNo - (posy_bottom2up / Config.LineHeight + 1);
        //if ((curPointingLineNo < 0) || (curPointingLineNo >= displayLineList.Count))
        //	curLine = null;
        //else
        //	curLine =  displayLineList[curPointingLineNo];
        //if (curLine == null)
        //	goto end;

        //pointing = curLine.GetPointingButton(pointX);
        if ((pointing == null) || (pointing.Generation != lastButtonGeneration))
            canSelect = false;
        else if (!pointing.IsButton)
            canSelect = false;
        else if (state == ConsoleState.WaitInput && inputReq.InputType == InputType.IntValue && (!pointing.IsInteger))
            canSelect = false;
        end:
        if (canSelect)
            select = pointing;
        bool needRefresh = select != selectingButton || pointing != pointingString;
        pointingString = pointing;
        selectingButton = select;
        return needRefresh;


    }

    public ConsoleButtonString FindButton(int x, int y)
    {
        ConsoleDisplayLine curLine;
        ConsoleButtonString pointing = null;

        int bottomLineNo = window.ScrollBar.Value - 1;
        if (displayLineList.Count - 1 < bottomLineNo)
            bottomLineNo = displayLineList.Count - 1;//1820 この処理不要な気がするけどエラー報告があったので入れとく
        int topLineNo = bottomLineNo - (window.MainPicBox.Height / Config.LineHeight);
        if (topLineNo < 0)
            topLineNo = 0;



        int pointX = x;
        int pointY = y;

        //HTML Islandの探索
        foreach (var (_, list) in _htmlElementListDict.Reverse())
        {
            foreach (var elem in list)
            {
                foreach (var button in elem.Buttons)
                {
                    foreach (var part in button.StrArray)
                    {
                        pointing = findButton(pointX, pointY, button, null);
                        if (pointing != null)
                        {
                            return pointing;
                        }
                    }

                }
            }

        }

        //通常の描画領域の探索
        for (int i = bottomLineNo; i >= topLineNo; i--)
        {
            curLine = displayLineList[i];

            for (int b = 0; b < curLine.Buttons.Length; b++)
            {
                var button = curLine.Buttons[curLine.Buttons.Length - b - 1];
                if (button == null || button.StrArray == null)
                    continue;

                pointing = findButton(pointX, pointY, button, null);
                if (pointing != null)
                {
                    return pointing;
                }
            }
        }

        return pointing;


    }

    //子を再帰的に探索する
    static ConsoleButtonString findButton(int pointX, int pointY, AConsoleDisplayNode parent, ConsoleButtonString selectableButton)
    {
        if (parent == null) return null;

        if (parent is ConsoleButtonString cbs)
        {
            if (cbs.IsButton || !string.IsNullOrEmpty(cbs.Title))
                selectableButton = cbs;
            foreach (var node in cbs.StrArray)
            {
                var r = findButton(pointX, pointY, node, selectableButton);
                if (r != null)
                {
                    return r;
                }
            }
        }
        else if (parent is ConsoleDivElement div)
        {
            foreach (var node in div._childNodes)
            {
                var r = findButton(pointX, pointY, node, selectableButton);
                if (r != null)
                {
                    return r;
                }
            }
        }
        else
        {
            if ((parent.Point.X <= pointX) && (parent.Point.X + parent.Size.Width >= pointX) &&
                (pointY >= parent.Point.Y) && (pointY <= parent.Point.Y + parent.Size.Height))
            {
                if (selectableButton != null)
                    return selectableButton;
            }
        }

        return null;
    }

    public void LeaveMouse()
    {
        bool needRefresh = selectingButton != null || pointingString != null;
        selectingButton = null;
        pointingString = null;
        if (needRefresh)
        {
            RefreshStrings(true);
        }
    }

    private void verticalScrollBarUpdate()
    {
        long scrollStart = PerformanceMetrics.StartTiming();
        try
        {
        int max = displayLineList.Count;
        int move = max - window.ScrollBar.Maximum;
        if (move == 0)
            return;
        if (move > 0)
        {
            window.ScrollBar.Maximum = max;
            window.ScrollBar.Value += move;
        }
        else
        {
            if (max > window.ScrollBar.Value)
                window.ScrollBar.Value = max;
            window.ScrollBar.Maximum = max;
        }
        window.ScrollBar.Enabled = max > 0;
        }
        finally
        {
            PerformanceMetrics.AddScroll(scrollStart);
        }
    }
    #endregion



    public void GotoTitle()
    {
        forceStopTimer();
        ClearDisplay();
        //動的作成の分だけは削除する
        AppContents.UnloadGraphicList();
        redraw = ConsoleRedraw.Normal;
        UseUserStyle = false;
        userStyle = new StringStyle(Config.ForeColor, FontStyle.Regular, null);
        process.BeginTitle();
        ReadAnyKey(false, false);
        RunEmueraProgram("");
        RefreshStrings(true);
    }

    bool force_temporary;
    bool timer_suspended;
    ConsoleState prevState;
    InputRequest prevReq;

    public async Task ReloadErb()
    {
        if (state == ConsoleState.Error)
        {
            Dialog.Show(LocalizationManager.MsgBox.CanNotUseWhenError);
            return;
        }
        if (state == ConsoleState.Initializing)
        {
            Dialog.Show(LocalizationManager.MsgBox.CanNotUseWhenInitialize);
            return;
        }
        bool notRedraw = false;
        if (redraw == ConsoleRedraw.None)
        {
            notRedraw = true;
            redraw = ConsoleRedraw.Normal;
        }
        if (genericTimer.Enabled)
        {
            genericTimer.Enabled = false;
            timer_suspended = true;
        }
        prevState = state;
        prevReq = inputReq;
        state = ConsoleState.Initializing;
        PrintSingleLine(LocalizationManager.SystemLine.ReloadingErb, true);
        force_temporary = true;
        await process.ReloadErbAll();
        force_temporary = false;
        PrintSingleLine(LocalizationManager.SystemLine.ReloadCompleted, true);
        RefreshStrings(true);
        //強制的にボタン世代が切り替わるのを防ぐ
        updatedGeneration = true;
        if (notRedraw)
            redraw = ConsoleRedraw.None;
    }

    public void ReloadErbFinished()
    {
        state = prevState;
        inputReq = prevReq;
        PrintSingleLine(" ");
        if (timer_suspended)
        {
            timer_suspended = false;
            genericTimer.Enabled = true;
            //タイマー待機中の時間ずれは修正しない。タイマー中にリロードしたらほぼ強制タイムアウトする程度は仕様のうちであろう。
        }
    }

    public async Task ReloadPartialErb(List<string> path)
    {
        if (state == ConsoleState.Error)
        {
            Dialog.Show(LocalizationManager.MsgBox.CanNotUseWhenError);
            return;
        }
        if (state == ConsoleState.Initializing)
        {
            Dialog.Show(LocalizationManager.MsgBox.CanNotUseWhenInitialize);
            return;
        }
        bool notRedraw = false;
        if (redraw == ConsoleRedraw.None)
        {
            notRedraw = true;
            redraw = ConsoleRedraw.Normal;
        }
        if (genericTimer.Enabled)
        {
            genericTimer.Enabled = false;
            timer_suspended = true;
        }
        prevState = state;
        prevReq = inputReq;
        state = ConsoleState.Initializing;
        PrintSingleLine(LocalizationManager.SystemLine.ReloadingErb, true);
        force_temporary = true;
        await process.ReloadPartialErb(path);
        force_temporary = false;
        PrintSingleLine(LocalizationManager.SystemLine.ReloadCompleted, true);
        RefreshStrings(true);
        //強制的にボタン世代が切り替わるのを防ぐ
        updatedGeneration = true;
        if (notRedraw)
            redraw = ConsoleRedraw.None;
    }

    public async Task ReloadFolder(string erbPath)
    {
        if (state == ConsoleState.Error)
        {
            Dialog.Show(LocalizationManager.MsgBox.CanNotUseWhenError);
            return;
        }
        if (state == ConsoleState.Initializing)
        {
            Dialog.Show(LocalizationManager.MsgBox.CanNotUseWhenInitialize);
            return;
        }
        if (genericTimer.Enabled)
        {
            genericTimer.Enabled = false;
            timer_suspended = true;
        }
        bool notRedraw = false;
        if (redraw == ConsoleRedraw.None)
        {
            notRedraw = true;
            redraw = ConsoleRedraw.Normal;
        }
        prevState = state;
        prevReq = inputReq;
        state = ConsoleState.Initializing;
        PrintSingleLine(LocalizationManager.SystemLine.ReloadingErb, true);
        force_temporary = true;
        await process.ReloadErbFolder(erbPath);
        force_temporary = false;
        PrintSingleLine(LocalizationManager.SystemLine.ReloadCompleted, true);
        RefreshStrings(true);
        //強制的にボタン世代が切り替わるのを防ぐ
        updatedGeneration = true;
        if (notRedraw)
            redraw = ConsoleRedraw.None;
    }

    public void Dispose()
    {
        process?.FlushNextRuntimeSessionStartFaultTrace();
        if (genericTimer != null)
            genericTimer.Dispose();
        //timer = null;
        //stringMeasure.Dispose();
    }


}
