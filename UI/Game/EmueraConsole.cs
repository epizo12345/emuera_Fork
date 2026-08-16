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
        displayLineList = [];
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
    public bool Enabled { get { return window.Created; } }

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
        var boottimeDebugStopwatch = Stopwatch.StartNew();
        using var fs = new FileStream(Program.ExeDir + "time.log", FileMode.Create);
        using var logWriter = new StreamWriter(fs);
        logWriter.WriteLine("Init:Start");
        _genericTimerStopwatch.Restart();

        //必要なソースファイルを事前にメモリに一気に読み込む
        logWriter.WriteLine("File:Preload:Start");
        Preload.Clear();
        await Preload.Load(Program.ErbDir);
        await Preload.Load(Program.CsvDir);

        logWriter.WriteLine("File:Preload:End " + boottimeDebugStopwatch.ElapsedMilliseconds + "ms");

        logWriter.WriteLine("Font:Load:Start " + boottimeDebugStopwatch.ElapsedMilliseconds + "ms");
        FontFactory.LoadFontFolder();
        logWriter.WriteLine("Font:Load:End " + boottimeDebugStopwatch.ElapsedMilliseconds + "ms");

        GlobalStatic.Console = this;
        // GlobalStatic.MainWindow = window;
        process = new GameProc.Process(this);
        GlobalStatic.Process = process;
        if (Program.DebugMode && Config.DebugShowWindow)
        {
            OpenDebugDialog();
            window.Focus();
        }
        ClearDisplay();
        logWriter.WriteLine("Process:Initialize:Start " + boottimeDebugStopwatch.ElapsedMilliseconds + "ms");
        if (!await process.Initialize(logWriter))
        {
            state = ConsoleState.Error;
            OutputLog(null);
            PrintFlush(false);
            RefreshStrings(true);
            if (Program.StartupTestMode)
                window.BeginInvoke(window.Close);
            return;
        }
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
    public void UpdateGeneration()
    {
        lastButtonGeneration = newButtonGeneration;
        updatedGeneration = true;
        gamepadFocusTargetsDirty = true;
    }
    public void forceUpdateGeneration()
    {
        newButtonGeneration++;
        lastButtonGeneration = newButtonGeneration;
        updatedGeneration = true;
        gamepadFocusTargetsDirty = true;
    }
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

    // [Emuera改修:GAMEPAD-V1]
    // 現在のInputRequestから選択可能要素を集め、画面更新時だけ上下左右リンクを
    // 再構築する。入力時は構築済みTargetを既存の決定・クリック経路へ渡す。
    private readonly List<GamepadFocusTarget> gamepadFocusTargets = [];
    private readonly GamepadNavigationGraph gamepadNavigationGraph = new();
    private bool gamepadFocusTargetsDirty = true;
    private int gamepadFocusTargetGeneration = int.MinValue;
    private long gamepadFocusTargetRequestId = -1;
    private int gamepadFocusLoggedGeneration = int.MinValue;
    private long gamepadFocusLoggedRequestId = -1;
    private int gamepadFocusLoggedGeometryHash;
    private bool hasGamepadFocusHistory;
    private string lastGamepadFocusInputKey;
    private long lastGamepadFocusRequestId = -1;
    private Rectangle lastGamepadFocusBounds;
    private GamepadFocusSourceType lastGamepadFocusSourceType;
    private int lastGamepadFocusGroupId = int.MinValue;
    private int lastGamepadFocusNavigationGroupId = int.MinValue;
    // [Emuera改修:GAMEPAD-V1] 別InputRequestへ遷移してから戻る画面用の履歴。
    // PostConfirmの同一画面再描画とは用途を分離し、上限を持つ短いstack/cacheだけを保持する。
    private const int GamepadReturnFocusStackCapacity = 24;
    private const int GamepadLastFocusCacheCapacity = 48;
    private readonly List<GamepadFocusHistoryEntry> gamepadReturnFocusStack = [];
    private readonly List<GamepadFocusHistoryEntry> gamepadLastFocusCache = [];
    private GamepadFocusHistoryEntry pendingGamepadFocusTransition;
    private GamepadFocusHistoryEntry pendingReturnFocusRestore;
    private long gamepadFocusHistoryOrder;
    // [Emuera改修:GAMEPAD-V1] Confirm直後の同一画面再描画だけに使う一時Anchor。
    // 通常のFocus履歴とは分け、別InputRequestへの無条件復元を防ぐ。
    private PostConfirmFocusAnchor postConfirmFocusAnchor;
    private static bool gamepadSelfTestsRun;
    private static readonly GamepadSelfTestSuite[] GamepadSelfTestSuites =
    [
        new("semantic Back", GetGamepadSemanticBackSelfTestCases),
        new("post-confirm focus", GetGamepadFocusPersistenceSelfTestCases),
        new("interactive target", GetGamepadInteractiveTargetSelfTestCases),
        new("logical button fragment", GetGamepadLogicalButtonSelfTestCases),
        new("HTML modal", GetGamepadHtmlModalSelfTestCases),
        new("page navigation", GetGamepadPageNavigationSelfTestCases),
        new("return focus history", GetGamepadReturnFocusHistorySelfTestCases),
    ];

    private readonly record struct GamepadSelfTestCase(string Name, bool Actual, bool Expected = true);
    private readonly record struct GamepadSelfTestSuite(string Name, Func<GamepadSelfTestCase[]> GetCases);
    private enum GamepadPageNavigationMode
    {
        Indexed,
        Directional,
    }

    private enum GamepadDirectionalPageTargetKind
    {
        None,
        Previous,
        Next,
    }

    private static readonly string[] GamepadBackButtonLabels =
    [
        "戻る",
        "帰る",
        "キャンセル",
        "CANCEL",
        "BACK",
        "RETURN",
        "EXIT",
        "QUIT",
        "閉じる",
        "やめる",
        "店を出る",
        "中止",
        "取消",
    ];

    private sealed class PostConfirmFocusAnchor
    {
        internal GamepadScreenTargetSnapshot Focus;
        internal long RequestId;
        internal int ButtonGeneration;
        internal List<GamepadScreenTargetSnapshot> Targets = [];
    }

    private enum GamepadFocusTransitionKind
    {
        Confirm,
        Back,
    }

    /// <summary>
    /// 入力要求IDに依存しない、現在のゲームパッドNavigation画面の軽量な構造指紋。
    /// 表示中のHPや名前などを同一性の必須条件にはせず、Interactive Targetの
    /// Input/Source/Group/Layout/Back性とおおまかな配置だけで照合する。
    /// </summary>
    private sealed class GamepadScreenFingerprint
    {
        internal bool HasModalScope;
        internal List<GamepadScreenTargetSnapshot> Targets = [];
        internal string DebugId;
    }

    private readonly struct GamepadScreenTargetSnapshot
    {
        internal GamepadScreenTargetSnapshot(GamepadFocusTarget target)
        {
            InputKey = GetGamepadInputKey(target.Button);
            NormalizedLabel = NormalizeGamepadSemanticText(target.Button?.ToString());
            SourceType = target.SourceType;
            LayoutType = target.LayoutType;
            GroupId = target.GroupId;
            NavigationGroupId = target.NavigationGroupId;
            Bounds = target.Bounds;
            Row = target.Row;
            Column = target.Column;
            IsBack = target.IsBack;
        }

        internal string InputKey { get; }
        internal string NormalizedLabel { get; }
        internal GamepadFocusSourceType SourceType { get; }
        internal GamepadFocusLayoutType LayoutType { get; }
        internal int GroupId { get; }
        internal int NavigationGroupId { get; }
        internal Rectangle Bounds { get; }
        internal int Row { get; }
        internal int Column { get; }
        internal bool IsBack { get; }
    }

    private sealed class GamepadFocusHistoryEntry
    {
        internal GamepadScreenFingerprint Fingerprint;
        internal GamepadScreenTargetSnapshot Focus;
        internal long RequestId;
        internal long Order;
        internal GamepadFocusTransitionKind TransitionKind;
    }

    // [Emuera改修:GAMEPAD-V1]
    // PAGE.0 / PAGE.1、または「前のページ」「次のページ」のような表示済み
    // ボタン群を、ゲーム固有の入力番号を知らずにLB/RBで選ぶための一時的な
    // 解析結果。入力要求ごとに再構築する。
    private sealed class GamepadPageNavigationSet
    {
        internal GamepadPageNavigationMode Mode;
        internal GamepadFocusSourceType SourceType;
        internal int GroupId;
        internal int NavigationGroupId;
        internal List<GamepadPageTarget> Targets = [];
        internal int CurrentPageIndex;
        internal GamepadFocusTarget PreviousTarget;
        internal GamepadFocusTarget NextTarget;
        internal bool HasAmbiguousDirectionalTarget;
    }

    private readonly struct GamepadPageTarget
    {
        internal GamepadPageTarget(int pageIndex, GamepadFocusTarget target)
        {
            PageIndex = pageIndex;
            Target = target;
        }

        internal int PageIndex { get; }
        internal GamepadFocusTarget Target { get; }
    }
    public ConsoleButtonString SelectingButton { get { return selectingButton; } }
    public bool ButtonIsSelected(ConsoleButtonString button) { return selectingButton == button; }

    /// <summary>
    /// ゲームパッド用のフォーカスを現在の入力待ち画面へ合わせる。
    /// マウスが既に有効なボタンを指している場合は、その選択を引き継ぐ。
    /// </summary>
    internal bool GamepadEnsureSelection()
    {
        if (state != ConsoleState.WaitInput || inputReq == null || !inputReq.NeedValue)
            return false;
        List<GamepadFocusTarget> targets = GetGamepadFocusTargets();
        if (targets.Count == 0)
        {
            if (postConfirmFocusAnchor != null)
            {
                WriteGamepadNavigationDiagnostic(
                    $"Next request: request={inputReq.ID} (anchor request={postConfirmFocusAnchor.RequestId})");
                WriteGamepadNavigationDiagnostic(
                    "Post-confirm focus restore rejected: reason=UI structure changed (no focus targets)");
                postConfirmFocusAnchor = null;
            }
            return false;
        }

        // InputRequest IDは画面再表示で変わるため、Confirm/Back直後にだけ前画面と
        // 現在画面のInteractive構造を照合する。ここではまだFocusを決めず、
        // PostConfirm → Return Stack → Last Focus Cacheの優先順位で後段へ渡す。
        GamepadScreenFingerprint currentScreen = CreateGamepadScreenFingerprint(targets);
        ResolvePendingGamepadFocusTransition(currentScreen);

        // Confirm may legitimately create a new InputRequest while redrawing
        // the same toggle/options screen. Try the short-lived Confirm anchor
        // before the ordinary same-request history; Cancel never creates this
        // anchor, so the existing Back behavior remains unchanged.
        if (postConfirmFocusAnchor != null)
        {
            if (TryRestorePostConfirmFocus(targets, out GamepadFocusTarget postConfirmRestored))
            {
                ApplyGamepadFocusSelection(postConfirmRestored);
                RememberGamepadFocus(postConfirmRestored);
                postConfirmFocusAnchor = null;
                return true;
            }
            postConfirmFocusAnchor = null;
        }

        bool sameInputRequest = inputReq.ID == lastGamepadFocusRequestId;
        GamepadFocusTarget current = gamepadNavigationGraph.Find(selectingButton);
        if (sameInputRequest && current != null && CanSelectGamepadButton(selectingButton))
        {
            RememberGamepadFocus(current);
            return false;
        }

        GamepadFocusTarget restored = null;
        // Input値は画面をまたいで一意ではない。別のInputRequestでは、前画面の
        // 0/1/2等を新画面のボタンへ復元してはいけない。
        if (sameInputRequest && hasGamepadFocusHistory
            && !string.IsNullOrEmpty(lastGamepadFocusInputKey))
        {
            restored = FindRestoredGamepadFocus(targets, true);
            restored ??= FindRestoredGamepadFocus(targets, false);
        }

        // 同じ入力待ちの再描画では以前の位置も考慮する。別の入力要求へ
        // 遷移した場合は前画面の座標で関係ない項目へ飛ばさず、先頭を使う。
        if (restored == null && hasGamepadFocusHistory && inputReq.ID == lastGamepadFocusRequestId
            && !lastGamepadFocusBounds.IsEmpty)
        {
            restored = FindNearestRememberedGamepadFocus(targets);
        }

        // Back/CancelによってReturn Stackの最上段へ戻った場合は、同じ論理画面と
        // 確認済みのEntryだけを使う。別画面の同じinput値だけでは復元しない。
        if (restored == null && pendingReturnFocusRestore != null)
        {
            GamepadFocusHistoryEntry returnEntry = pendingReturnFocusRestore;
            pendingReturnFocusRestore = null;
            if (TryRestoreGamepadFocusHistory(returnEntry, targets, out restored, out string restoreReason))
            {
                WriteGamepadNavigationDiagnostic(
                    $"Focus restored from return history: input={GetGamepadButtonInput(restored.Button)} "
                    + $"reason={restoreReason}");
            }
            else
            {
                WriteGamepadNavigationDiagnostic(
                    "Focus history target unavailable: source=return-stack fallback=initial-focus");
            }
        }

        // Return Stackに該当しない再訪問は、構造照合済みの小型Last Focus Cacheを
        // 利用する。キャッシュは同一画面らしい場合だけ照合し、初回表示には使わない。
        if (restored == null && TryFindGamepadLastFocusHistory(currentScreen,
                out GamepadFocusHistoryEntry cachedEntry, out int cacheSimilarity))
        {
            if (TryRestoreGamepadFocusHistory(cachedEntry, targets, out restored, out string cacheReason))
            {
                WriteGamepadNavigationDiagnostic(
                    $"Focus restored from screen history: input={GetGamepadButtonInput(restored.Button)} "
                    + $"similarity={cacheSimilarity}% reason={cacheReason}");
            }
            else
            {
                WriteGamepadNavigationDiagnostic(
                    "Focus history target unavailable: source=last-focus-cache fallback=initial-focus");
            }
        }

        restored ??= FindInitialGamepadFocus(targets);

        if (restored.IsModalForeground)
        {
            WriteGamepadNavigationDiagnostic(
                $"Initial modal focus: input={GetGamepadButtonInput(restored.Button)} "
                + $"text={SanitizeGamepadText(restored.Button.ToString())}");
        }

        ApplyGamepadFocusSelection(restored);
        RememberGamepadFocus(restored);
        return true;
    }

    private void ApplyGamepadFocusSelection(GamepadFocusTarget target)
    {
        selectingCBGButtonInt = -1;
        pointingString = null;
        selectingButton = target?.Button;
    }

    private static GamepadFocusTarget FindInitialGamepadFocus(List<GamepadFocusTarget> targets)
    {
        GamepadFocusTarget firstNormal = null;
        GamepadFocusTarget firstEnabled = null;
        for (int i = 0; i < targets.Count; i++)
        {
            GamepadFocusTarget target = targets[i];
            if (!target.Enabled || target.IsDirectionalFocusExcluded)
                continue;
            if (firstEnabled == null || CompareGamepadVisualOrder(target, firstEnabled) < 0)
                firstEnabled = target;
            if (target.IsBack)
                continue;
            if (firstNormal == null || CompareGamepadVisualOrder(target, firstNormal) < 0)
                firstNormal = target;
        }
        return firstNormal ?? firstEnabled ?? targets[0];
    }

    private void CapturePostConfirmFocusAnchor(GamepadFocusTarget target)
    {
        if (target == null || inputReq == null)
            return;

        PostConfirmFocusAnchor anchor = new()
        {
            Focus = new GamepadScreenTargetSnapshot(target),
            RequestId = inputReq.ID,
            ButtonGeneration = lastButtonGeneration,
            Targets = CreateGamepadFocusSnapshots(gamepadFocusTargets),
        };
        postConfirmFocusAnchor = anchor;

        WriteGamepadNavigationDiagnostic(
            $"Confirm focus anchor: input={GetGamepadButtonInput(target.Button)} request={anchor.RequestId} "
            + $"rect={FormatGamepadRectangle(anchor.Focus.Bounds)} row={anchor.Focus.Row} column={anchor.Focus.Column} "
            + $"source={target.SourceName} baseGroup={anchor.Focus.GroupId} navigationGroup={anchor.Focus.NavigationGroupId} "
            + $"generation={anchor.ButtonGeneration} targetCount={anchor.Targets.Count}");
    }

    private bool TryRestorePostConfirmFocus(List<GamepadFocusTarget> targets,
        out GamepadFocusTarget restored)
    {
        restored = null;
        PostConfirmFocusAnchor anchor = postConfirmFocusAnchor;
        if (anchor == null || inputReq == null)
            return false;

        bool sameScreen = IsPostConfirmSameScreen(anchor, targets, out int matchCount);
        GamepadFocusTarget exact = FindPostConfirmExactTarget(anchor, targets);
        bool targetMatch = exact != null;

        WriteGamepadNavigationDiagnostic(
            $"Next request: request={inputReq.ID} (anchor request={anchor.RequestId})");
        WriteGamepadNavigationDiagnostic(
            $"Post-confirm redraw comparison: same-screen={sameScreen} target-match={targetMatch} "
            + $"matches={matchCount}/{Math.Min(anchor.Targets.Count, targets.Count)} "
            + $"oldCount={anchor.Targets.Count} newCount={targets.Count}");

        if (!sameScreen)
        {
            WriteGamepadNavigationDiagnostic(
                "Post-confirm focus restore rejected: reason=UI structure changed");
            return false;
        }

        if (exact != null)
        {
            restored = exact;
            WriteGamepadNavigationDiagnostic(
                $"Focus restored: input={GetGamepadButtonInput(exact.Button)} "
                + "reason=same input/source/baseGroup and near-identical bounds");
            return true;
        }

        GamepadFocusTarget sameInput = FindPostConfirmSameInputTarget(anchor, targets);
        if (sameInput != null)
        {
            restored = sameInput;
            WriteGamepadNavigationDiagnostic(
                $"Focus restored: input={GetGamepadButtonInput(sameInput.Button)} "
                + "reason=same input/source/layout/baseGroup after redraw");
            return true;
        }

        restored = FindPostConfirmLaneFallback(anchor, targets);
        if (restored == null)
        {
            WriteGamepadNavigationDiagnostic(
                "Post-confirm focus restore rejected: reason=anchor target disappeared and no lane fallback exists");
            return false;
        }

        WriteGamepadNavigationDiagnostic(
            $"Focus restored: input={GetGamepadButtonInput(restored.Button)} "
            + "reason=same-screen redraw, anchor target disappeared, nearest vertical lane fallback");
        return true;
    }

    private static bool IsPostConfirmSameScreen(PostConfirmFocusAnchor anchor,
        List<GamepadFocusTarget> targets, out int matchCount)
    {
        matchCount = CountPostConfirmMatches(anchor, targets);
        bool sameTargetListSize = Math.Abs(anchor.Targets.Count - targets.Count)
            <= Math.Max(2, Math.Max(anchor.Targets.Count, targets.Count) / 3);
        int requiredMatches = Math.Max(1, (Math.Min(anchor.Targets.Count, targets.Count) * 3 + 3) / 4);
        return anchor.Targets.Count > 0 && targets.Count > 0
            && sameTargetListSize && matchCount >= requiredMatches;
    }

    private static int CountPostConfirmMatches(PostConfirmFocusAnchor anchor,
        List<GamepadFocusTarget> targets)
    {
        if (anchor.Targets.Count == 0 || targets.Count == 0)
            return 0;

        bool[] used = new bool[targets.Count];
        int matchCount = 0;
        for (int oldIndex = 0; oldIndex < anchor.Targets.Count; oldIndex++)
        {
            GamepadScreenTargetSnapshot oldTarget = anchor.Targets[oldIndex];
            for (int newIndex = 0; newIndex < targets.Count; newIndex++)
            {
                GamepadFocusTarget newTarget = targets[newIndex];
                if (used[newIndex] || newTarget.IsDirectionalFocusExcluded
                    || !IsPostConfirmTargetMatch(oldTarget, newTarget))
                    continue;
                used[newIndex] = true;
                matchCount++;
                break;
            }
        }
        return matchCount;
    }

    private static GamepadFocusTarget FindPostConfirmExactTarget(PostConfirmFocusAnchor anchor,
        List<GamepadFocusTarget> targets)
    {
        return FindGamepadNearestTarget(targets, anchor.Focus.Bounds, target =>
            !target.IsDirectionalFocusExcluded
                && string.Equals(GetGamepadInputKey(target.Button), anchor.Focus.InputKey,
                    StringComparison.Ordinal)
                && target.SourceType == anchor.Focus.SourceType
                && target.GroupId == anchor.Focus.GroupId
                && target.IsBack == anchor.Focus.IsBack
                && ArePostConfirmBoundsNear(target.Bounds, anchor.Focus.Bounds));
    }

    private static GamepadFocusTarget FindPostConfirmLaneFallback(PostConfirmFocusAnchor anchor,
        List<GamepadFocusTarget> targets)
    {
        Func<GamepadFocusTarget, bool> isCandidate = target => !target.IsDirectionalFocusExcluded
            && target.SourceType == anchor.Focus.SourceType
            && target.GroupId == anchor.Focus.GroupId
            && target.IsBack == anchor.Focus.IsBack;
        return FindGamepadVerticalLaneTarget(targets, anchor.Focus.Bounds,
                Math.Max(12, anchor.Focus.Bounds.Width / 2), isCandidate, false)
            ?? FindGamepadNearestTarget(targets, anchor.Focus.Bounds, isCandidate);
    }

    private static GamepadFocusTarget FindPostConfirmSameInputTarget(PostConfirmFocusAnchor anchor,
        List<GamepadFocusTarget> targets)
    {
        return FindGamepadNearestTarget(targets, anchor.Focus.Bounds, target =>
            !target.IsDirectionalFocusExcluded
                && string.Equals(GetGamepadInputKey(target.Button), anchor.Focus.InputKey,
                    StringComparison.Ordinal)
                && target.SourceType == anchor.Focus.SourceType
                && target.LayoutType == anchor.Focus.LayoutType
                && target.GroupId == anchor.Focus.GroupId
                && target.IsBack == anchor.Focus.IsBack,
            anchor.Focus.NavigationGroupId);
    }

    private static bool IsPostConfirmTargetMatch(GamepadScreenTargetSnapshot oldTarget,
        GamepadFocusTarget newTarget)
    {
        return string.Equals(oldTarget.InputKey, GetGamepadInputKey(newTarget.Button), StringComparison.Ordinal)
            && oldTarget.SourceType == newTarget.SourceType
            && oldTarget.GroupId == newTarget.GroupId
            && oldTarget.IsBack == newTarget.IsBack
            && ArePostConfirmBoundsNear(oldTarget.Bounds, newTarget.Bounds);
    }

    private static bool ArePostConfirmBoundsNear(Rectangle left, Rectangle right)
    {
        const int tolerance = 12;
        return Math.Abs(left.Left - right.Left) <= tolerance
            && Math.Abs(left.Top - right.Top) <= tolerance
            && Math.Abs(left.Width - right.Width) <= tolerance
            && Math.Abs(left.Height - right.Height) <= tolerance;
    }

    private static int CompareGamepadVisualOrder(GamepadFocusTarget left, GamepadFocusTarget right)
    {
        int result = left.Bounds.Top.CompareTo(right.Bounds.Top);
        if (result != 0)
            return result;
        result = left.Bounds.Left.CompareTo(right.Bounds.Left);
        return result != 0 ? result : left.Order.CompareTo(right.Order);
    }

    /// <summary>
    /// 実際のマウスヒット矩形を行へまとめた4方向ナビゲーション。
    /// 上下は同じ縦レーンの候補が見つかるまで先行視覚行を探索し、左右は同じ行だけを候補にする。
    /// </summary>
    internal bool GamepadMove(GamepadDirection direction)
    {
        if (direction == GamepadDirection.None || state != ConsoleState.WaitInput || inputReq == null
            || !inputReq.NeedValue
            || window.ScrollBar.Value != window.ScrollBar.Maximum)
            return false;

        GamepadEnsureSelection();
        List<GamepadFocusTarget> targets = GetGamepadFocusTargets();
        GamepadFocusTarget current = gamepadNavigationGraph.Find(selectingButton);
        if (current == null || !CanSelectGamepadButton(selectingButton))
            return false;

        GamepadFocusTarget best = current.GetNeighbor(direction);
        if (best == null || best.Button == selectingButton)
            return false;

        selectingCBGButtonInt = -1;
        pointingString = null;
        LogGamepadMove(direction, current, best);
        selectingButton = best.Button;
        RememberGamepadFocus(best);
        return true;
    }

    /// <summary>
    /// LB/RBで現在表示中のPAGE.nボタン群を前後移動する。現在ページを
    /// 視覚状態から一意に判断できない画面は、呼び出し元で従来のログ
    /// スクロールへフォールバックする。
    /// </summary>
    /// <param name="nextPage">trueなら次ページ(RB)、falseなら前ページ(LB)。</param>
    internal bool GamepadShoulderNavigatePage(bool nextPage)
    {
        if (state != ConsoleState.WaitInput || inputReq == null || !inputReq.NeedValue
            || window.ScrollBar.Value != window.ScrollBar.Maximum)
        {
            LogGamepadPageNavigationUnavailable("not at an active input screen");
            return false;
        }

        List<GamepadFocusTarget> targets = GetGamepadFocusTargets();
        GamepadFocusTarget currentFocus = gamepadNavigationGraph.Find(selectingButton);
        if (!TryFindGamepadPageNavigationSet(targets, currentFocus, out GamepadPageNavigationSet pageSet,
                out string unavailableReason))
        {
            LogGamepadPageNavigationUnavailable(unavailableReason);
            return false;
        }

        LogGamepadPageNavigationSet(pageSet);
        string direction = nextPage ? "Next" : "Previous";
        GamepadFocusTarget target;
        int targetPageIndex = -1;
        if (pageSet.Mode == GamepadPageNavigationMode.Directional)
        {
            target = nextPage ? pageSet.NextTarget : pageSet.PreviousTarget;
            if (target == null)
            {
                WriteGamepadNavigationDiagnostic(
                    $"Shoulder page navigation: mode=Directional direction={direction} "
                    + "target=<none> action=consume-no-op");
                // Directional Page UIの端では、従来のログスクロールへ流さない。
                return true;
            }

            WriteGamepadNavigationDiagnostic(
                $"Shoulder page navigation: mode=Directional direction={direction} "
                + $"targetInput={GetGamepadButtonInput(target.Button)} "
                + $"text={SanitizeGamepadText(target.Button.ToString())}");
        }
        else
        {
            target = FindGamepadAdjacentPageTarget(pageSet, nextPage, out targetPageIndex);
        }

        if (target == null)
        {
            WriteGamepadNavigationDiagnostic(
                $"Shoulder page navigation: mode=Indexed direction={direction} "
                + $"currentPage={pageSet.CurrentPageIndex} "
                + "targetPage=<none> boundary=True");
            // Indexed Page UIの端では、従来のログスクロールへ流さない。
            return true;
        }

        if (pageSet.Mode == GamepadPageNavigationMode.Indexed)
        {
            WriteGamepadNavigationDiagnostic(
                $"Shoulder page navigation: mode=Indexed direction={direction} "
                + $"currentPage={pageSet.CurrentPageIndex} targetPage={targetPageIndex} "
                + $"targetInput={GetGamepadButtonInput(target.Button)}");
        }

        // ページ遷移はON/OFFトグルの同一画面再描画ではない。旧ページの
        // Post-confirm anchorを残さず、ページボタン自体もFocus履歴にしない。
        postConfirmFocusAnchor = null;
        bool executed = ExecuteGamepadFocusTarget(target,
            nextPage ? "shoulder-page-next" : "shoulder-page-previous", rememberFocus: false,
            captureReturnFocus: false);
        // Directional型として認識できた操作は、実行対象が再描画競合で消えても
        // Log Scrollへフォールバックさせない。ページUIの肩ボタン操作を消費する。
        return executed || pageSet.Mode == GamepadPageNavigationMode.Directional;
    }

    private void LogGamepadPageNavigationUnavailable(string reason)
    {
        WriteGamepadNavigationDiagnostic(
            $"Shoulder page navigation: mode=None fallback=log-scroll reason={reason}");
    }

    private void LogGamepadPageNavigationSet(GamepadPageNavigationSet pageSet)
    {
        if (!Program.GamepadDebugMode || pageSet == null)
            return;

        if (pageSet.Mode == GamepadPageNavigationMode.Directional)
        {
            WriteGamepadNavigationDiagnostic(
                $"Directional page navigation detected: request={inputReq?.ID ?? -1} "
                + $"source={pageSet.SourceType} baseGroup={pageSet.GroupId} "
                + $"navigationGroup={pageSet.NavigationGroupId} "
                + $"previous={FormatGamepadDirectionalPageTarget(pageSet.PreviousTarget)} "
                + $"next={FormatGamepadDirectionalPageTarget(pageSet.NextTarget)}");
            return;
        }

        WriteGamepadNavigationDiagnostic(
            $"Page navigation detected: request={inputReq?.ID ?? -1} targets={pageSet.Targets.Count} "
            + $"source={pageSet.SourceType} baseGroup={pageSet.GroupId} "
            + $"navigationGroup={pageSet.NavigationGroupId}");
        for (int i = 0; i < pageSet.Targets.Count; i++)
        {
            GamepadPageTarget page = pageSet.Targets[i];
            WriteGamepadNavigationDiagnostic(
                $"Page target: page={page.PageIndex} input={GetGamepadButtonInput(page.Target.Button)} "
                + $"text={SanitizeGamepadText(page.Target.Button.ToString())}");
        }
        WriteGamepadNavigationDiagnostic(
            $"Page navigation currentPage={pageSet.CurrentPageIndex} "
                + "reason=unique non-default visual style among PAGE targets");
    }

    private static string FormatGamepadDirectionalPageTarget(GamepadFocusTarget target)
    {
        if (target?.Button == null)
            return "<none>";
        return $"input={GetGamepadButtonInput(target.Button)} "
            + $"text={SanitizeGamepadText(target.Button.ToString())}";
    }

    /// <summary>
    /// 左スティックをUIフォーカスとは別の、ゲーム側の1文字移動入力として渡す。
    /// Autoは現在表示中のボタンが方向記号付きのWASDまたは8462一式を公開している
    /// 場合だけ有効になるため、数値キーパッドや通常メニューを誤作動させない。
    /// </summary>
    internal bool TryGamepadDirectInput(GamepadDirection direction)
    {
        if (direction == GamepadDirection.None || state != ConsoleState.WaitInput || inputReq == null
            || !inputReq.NeedValue || !inputReq.OneInput)
            return false;

        GamepadDirectInputProfile profile = Program.GamepadDirectInput;
        if (profile == GamepadDirectInputProfile.Disabled)
            return false;
        if (profile == GamepadDirectInputProfile.Auto)
        {
            profile = DetectGamepadDirectInputProfile();
            if (profile == GamepadDirectInputProfile.Disabled)
                return false;
        }

        if (profile == GamepadDirectInputProfile.ArrowKeys)
        {
            if (!IsWaitingPrimitive)
                return false;
            Keys key = direction switch
            {
                GamepadDirection.Up => Keys.Up,
                GamepadDirection.Down => Keys.Down,
                GamepadDirection.Left => Keys.Left,
                GamepadDirection.Right => Keys.Right,
                _ => Keys.None,
            };
            if (key == Keys.None)
                return false;
            WriteGamepadNavigationDiagnostic($"Direct input: profile={profile}, direction={direction}, key={key}");
            InputMouseKey(3, (int)key, (int)key, 0, 0);
            return true;
        }

        string input = profile switch
        {
            GamepadDirectInputProfile.Wasd => direction switch
            {
                GamepadDirection.Up => "w",
                GamepadDirection.Down => "s",
                GamepadDirection.Left => "a",
                GamepadDirection.Right => "d",
                _ => string.Empty,
            },
            GamepadDirectInputProfile.Numpad8462 => direction switch
            {
                GamepadDirection.Up => "8",
                GamepadDirection.Down => "2",
                GamepadDirection.Left => "4",
                GamepadDirection.Right => "6",
                _ => string.Empty,
            },
            _ => string.Empty,
        };
        if (input.Length == 0 || (inputReq.InputType != InputType.StrValue && inputReq.InputType != InputType.IntValue))
            return false;

        WriteGamepadNavigationDiagnostic($"Direct input: profile={profile}, direction={direction}, input={input}");
        PressEnterKey(false, input, false);
        return true;
    }

    private GamepadDirectInputProfile DetectGamepadDirectInputProfile()
    {
        List<GamepadFocusTarget> targets = GetGamepadFocusTargets();
        if (HasDirectionalInputSet(targets, "w", "s", "a", "d"))
            return GamepadDirectInputProfile.Wasd;
        if (HasDirectionalInputSet(targets, "8", "2", "4", "6"))
            return GamepadDirectInputProfile.Numpad8462;
        return GamepadDirectInputProfile.Disabled;
    }

    private static bool HasDirectionalInputSet(List<GamepadFocusTarget> targets,
        string up, string down, string left, string right)
    {
        bool hasUp = false;
        bool hasDown = false;
        bool hasLeft = false;
        bool hasRight = false;
        for (int i = 0; i < targets.Count; i++)
        {
            GamepadFocusTarget target = targets[i];
            string input = GetGamepadButtonInput(target.Button);
            string text = target.Button.ToString() ?? string.Empty;
            if (!hasUp && string.Equals(input, up, StringComparison.OrdinalIgnoreCase)
                && HasDirectionMarker(text, GamepadDirection.Up))
                hasUp = true;
            else if (!hasDown && string.Equals(input, down, StringComparison.OrdinalIgnoreCase)
                && HasDirectionMarker(text, GamepadDirection.Down))
                hasDown = true;
            else if (!hasLeft && string.Equals(input, left, StringComparison.OrdinalIgnoreCase)
                && HasDirectionMarker(text, GamepadDirection.Left))
                hasLeft = true;
            else if (!hasRight && string.Equals(input, right, StringComparison.OrdinalIgnoreCase)
                && HasDirectionMarker(text, GamepadDirection.Right))
                hasRight = true;
        }
        return hasUp && hasDown && hasLeft && hasRight;
    }

    private static bool HasDirectionMarker(string text, GamepadDirection direction)
    {
        return direction switch
        {
            GamepadDirection.Up => text.Contains('↑'),
            GamepadDirection.Down => text.Contains('↓') || text.Contains('Ｖ') || text.Contains('▼'),
            GamepadDirection.Left => text.Contains('←') || text.Contains('＜'),
            GamepadDirection.Right => text.Contains('→') || text.Contains('＞'),
            _ => false,
        };
    }

    internal void GamepadConfirm()
    {
        if (ReturnFromGamepadBacklog())
            return;
        if (IsWaitingPrimitive)
        {
            GamepadFocusTarget current = GetCurrentGamepadFocusTarget();
            if (current != null)
            {
                CapturePostConfirmFocusAnchor(current);
                if (!ExecuteGamepadFocusTarget(current, "virtual-left-click"))
                    postConfirmFocusAnchor = null;
            }
            else
            {
                WriteGamepadNavigationDiagnostic(
                    $"Confirm: input=<none> text=<none> request={inputReq?.ID ?? -1} action=keyboard-enter-fallback");
                InputMouseKey(3, (int)Keys.Enter, (int)Keys.Enter, 0, 0);
            }
            return;
        }
        if (CanSelectGamepadButton(selectingButton))
        {
            string input = inputReq.InputType == InputType.IntValue
                ? selectingButton.Input.ToString()
                : selectingButton.Inputs;
            GamepadFocusTarget current = gamepadNavigationGraph.Find(selectingButton);
            if (current != null)
            {
                CapturePostConfirmFocusAnchor(current);
                if (!ExecuteGamepadFocusTarget(current, "ConsoleButton confirm path"))
                {
                    postConfirmFocusAnchor = null;
                    PressEnterKey(false, input, true);
                }
            }
            else
            {
                WriteGamepadNavigationDiagnostic(
                    $"Confirm: input={input} text={SanitizeGamepadText(selectingButton.ToString())} request={inputReq.ID} action=PressEnterKey target=<unresolved>");
                PressEnterKey(false, input, true);
            }
            return;
        }
        if (IsWaitingEnterKey)
            PressEnterKey(false, "", false);
    }

    internal void GamepadCancel()
    {
        if (ReturnFromGamepadBacklog())
            return;
        if (state == ConsoleState.WaitInput && inputReq != null && inputReq.NeedValue)
        {
            GamepadEnsureSelection();
            List<GamepadFocusTarget> targets = GetGamepadFocusTargets();
            GamepadFocusTarget current = gamepadNavigationGraph.Find(selectingButton);
            WriteGamepadNavigationDiagnostic(
                $"Cancel requested: current input={GetGamepadButtonInput(current?.Button ?? selectingButton)} "
                + $"current navigationGroup={current?.NavigationGroupId ?? -1} request={inputReq.ID} "
                + $"source={current?.SourceName ?? "<none>"} baseGroup={current?.GroupId ?? -1}");
            GamepadFocusTarget back = FindGamepadBackTarget(targets, current);
            LogGamepadBackCandidates(targets, current, back);
            if (back != null)
            {
                CaptureGamepadReturnFocus(current, GamepadFocusTransitionKind.Back, "semantic-back");
                if (back.IsModalBackdrop)
                    WriteGamepadNavigationDiagnostic(
                        $"Modal Cancel: input={GetGamepadButtonInput(back.Button)}");
                WriteGamepadNavigationDiagnostic(
                    $"Selected semantic back: input={GetGamepadButtonInput(back.Button)} "
                    + $"text=\"{SanitizeGamepadText(back.Button.ToString())}\" "
                    + $"navigationGroup={back.NavigationGroupId} execution="
                    + (IsWaitingPrimitive ? "virtual-left-click" : "ConsoleButton confirm path"));
                ExecuteGamepadFocusTarget(back,
                    IsWaitingPrimitive ? "virtual-left-click" : "ConsoleButton confirm path",
                    captureReturnFocus: false);
                return;
            }

            if (IsWaitingPrimitive)
            {
                CaptureGamepadReturnFocus(current, GamepadFocusTransitionKind.Back,
                    "fallback-escape-inputmousekey");
                WriteGamepadNavigationDiagnostic("No semantic back target\nFallback = Escape (INPUTMOUSEKEY).");
                InputMouseKey(3, (int)Keys.Escape, (int)Keys.Escape, 0, 0);
                return;
            }

            CaptureGamepadReturnFocus(current, GamepadFocusTransitionKind.Back,
                "fallback-right-click-escape");
        }
        WriteGamepadNavigationDiagnostic("No semantic back target\nFallback = RightClick/Escape.");
        KillMacro = true;
        PressEnterKey(true, "", false);
    }

    /// <summary>
    /// Execute a visible gamepad target through the same ConsoleButtonString
    /// path used by a focused target followed by GamepadConfirm.  Semantic
    /// Back must not invent an input number or choose a different keyboard
    /// shortcut based on the button label.
    /// </summary>
    private bool ExecuteGamepadFocusTarget(GamepadFocusTarget target, string diagnosticAction,
        bool rememberFocus = true, bool captureReturnFocus = true)
    {
        if (target == null || !CanSelectGamepadButton(target.Button))
            return false;

        if (captureReturnFocus)
            CaptureGamepadReturnFocus(target, GamepadFocusTransitionKind.Confirm, diagnosticAction);
        selectingCBGButtonInt = -1;
        pointingString = null;
        selectingButton = target.Button;
        if (rememberFocus)
            RememberGamepadFocus(target);
        LogGamepadConfirm(target, diagnosticAction);
        if (IsWaitingPrimitive)
            InputMouseKeyFromGamepad(target);
        else
            PressEnterKey(false, GetGamepadButtonInput(target.Button), true);
        return true;
    }

    private bool ReturnFromGamepadBacklog()
    {
        if (window.ScrollBar.Value == window.ScrollBar.Maximum)
            return false;
        window.ScrollBar.Value = window.ScrollBar.Maximum;
        RefreshStrings(true);
        return true;
    }

    private bool CanSelectGamepadButton(ConsoleButtonString button)
    {
        if (!IsGamepadInteractiveConsoleButton(button) || button.Generation != lastButtonGeneration
            || state != ConsoleState.WaitInput || inputReq == null || !inputReq.NeedValue)
            return false;
        return inputReq.InputType != InputType.IntValue || button.IsInteger;
    }

    // [Emuera改修:GAMEPAD-V1]
    // Input=0などの値ではなく、Emuera自身がクリック・決定対象として生成した
    // ConsoleButtonString.IsButtonだけを通常ConsoleのInteractive判定に使う。
    // 説明用のConsoleButtonString（IsButton=false）やTitleだけの非ボタンは除外する。
    private static bool IsGamepadInteractiveConsoleButton(ConsoleButtonString button)
    {
        return button != null && button.IsButton;
    }

    private List<GamepadFocusTarget> GetGamepadFocusTargets()
    {
        if (Program.GamepadDebugMode)
            RunGamepadSelfTests();
        long requestId = inputReq?.ID ?? -1;
        if (!gamepadFocusTargetsDirty && gamepadFocusTargetGeneration == lastButtonGeneration
            && gamepadFocusTargetRequestId == requestId)
            return gamepadFocusTargets;

        gamepadFocusTargets.Clear();
        gamepadFocusTargetGeneration = lastButtonGeneration;
        gamepadFocusTargetRequestId = requestId;
        gamepadFocusTargetsDirty = false;

        if (state != ConsoleState.WaitInput || inputReq == null || !inputReq.NeedValue)
        {
            LogGamepadFocusTargets();
            return gamepadFocusTargets;
        }

        Dictionary<(ConsoleButtonString Button, GamepadFocusSourceType SourceType, int GroupId), GamepadFocusTarget> byButton = [];
        int order = 0;

        // マウスと同じく、HTML Islandは通常表示より手前にあるものから収集する。
        foreach (var pair in _htmlElementListDict.Reverse())
        {
            List<ConsoleDisplayLine> lines = pair.Value;
            for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
            {
                ConsoleDisplayLine line = lines[lineIndex];
                if (!IsGamepadLineVisible(line))
                    continue;
                CollectGamepadNodes(line?.Buttons, null, GamepadFocusSourceType.HtmlIsland,
                    GamepadFocusLayoutType.Html, pair.Key, line, byButton, ref order);
            }
        }

        for (int lineIndex = 0; lineIndex < displayLineList.Count; lineIndex++)
        {
            ConsoleDisplayLine line = displayLineList[lineIndex];
            if (!IsGamepadLineVisible(line))
                continue;
            CollectGamepadNodes(line.Buttons, null, GamepadFocusSourceType.NormalDisplay,
                line.IsHtml ? GamepadFocusLayoutType.Html : GamepadFocusLayoutType.Console,
                0, line, byButton, ref order);
        }

        // A wrapped logical button can be represented by several distinct
        // ConsoleButtonString instances.  AddGamepadTarget already coalesces
        // repeated visits to the same instance; this second pass handles only
        // adjacent fragments that still carry the same logical input identity.
        CollapseGamepadLogicalButtonFragments(gamepadFocusTargets,
            Program.GamepadDebugMode ? WriteGamepadNavigationDiagnostic : null);

        for (int i = 0; i < gamepadFocusTargets.Count; i++)
            gamepadFocusTargets[i].IsBack = IsGamepadBackButton(gamepadFocusTargets[i]);
        ApplyGamepadHtmlModalNavigationScope(gamepadFocusTargets);
        gamepadNavigationGraph.Build(gamepadFocusTargets,
            Program.GamepadDebugMode ? WriteGamepadNavigationDiagnostic : null);
        LogGamepadFocusTargets();
        return gamepadFocusTargets;
    }

    private void CollectGamepadNodes(
        AConsoleDisplayNode[] nodes,
        ConsoleButtonString selectableButton,
        GamepadFocusSourceType sourceType,
        GamepadFocusLayoutType layoutType,
        int groupId,
        ConsoleDisplayLine parentLine,
        Dictionary<(ConsoleButtonString Button, GamepadFocusSourceType SourceType, int GroupId), GamepadFocusTarget> byButton,
        ref int order)
    {
        if (nodes == null)
            return;
        for (int i = 0; i < nodes.Length; i++)
            CollectGamepadNode(nodes[i], selectableButton, sourceType, layoutType, groupId, parentLine, byButton, ref order);
    }

    private void CollectGamepadNode(
        AConsoleDisplayNode node,
        ConsoleButtonString selectableButton,
        GamepadFocusSourceType sourceType,
        GamepadFocusLayoutType layoutType,
        int groupId,
        ConsoleDisplayLine parentLine,
        Dictionary<(ConsoleButtonString Button, GamepadFocusSourceType SourceType, int GroupId), GamepadFocusTarget> byButton,
        ref int order)
    {
        if (node == null)
            return;

        // 通常ConsoleはConsoleButtonString自体がクリック単位である。
        // 子のConsoleStyledStringを1つずつ収集すると、ボタン下の説明文まで
        // ボタンのFocus Boundsへ混入し、見た目の行数だけNavigationが増える。
        // Input=0もIsButton=trueなら正規の選択肢なので、値では判定しない。
        if (layoutType == GamepadFocusLayoutType.Console && node is ConsoleButtonString consoleButton)
        {
            if (!CanSelectGamepadButton(consoleButton)
                || !TryGetGamepadButtonBounds(consoleButton, out Rectangle buttonBounds, out RectangleF buttonRawBounds))
                return;
            AddGamepadTarget(consoleButton, sourceType, layoutType, groupId, parentLine,
                buttonBounds, buttonRawBounds, byButton, ref order);
            return;
        }

        if (node is ConsoleButtonString button)
        {
            if (button.IsButton)
                selectableButton = button;
            parentLine ??= button.ParentLine;
            CollectGamepadNodes(button.StrArray, selectableButton, sourceType, layoutType,
                groupId, parentLine, byButton, ref order);
        }
        else if (node is ConsoleDivElement div)
        {
            for (int i = 0; i < div._childNodes.Count; i++)
                CollectGamepadNode(div._childNodes[i], selectableButton, sourceType, GamepadFocusLayoutType.Html,
                    groupId, parentLine, byButton, ref order);
        }
        else if (selectableButton != null && CanSelectGamepadButton(selectableButton))
        {
            if (!TryGetGamepadNodeBounds(node, out Rectangle bounds, out RectangleF rawBounds))
                return;

            AddGamepadTarget(selectableButton, sourceType, layoutType, groupId, parentLine,
                bounds, rawBounds, byButton, ref order);
        }
    }

    private void AddGamepadTarget(
        ConsoleButtonString button,
        GamepadFocusSourceType sourceType,
        GamepadFocusLayoutType layoutType,
        int groupId,
        ConsoleDisplayLine parentLine,
        Rectangle bounds,
        RectangleF rawBounds,
        Dictionary<(ConsoleButtonString Button, GamepadFocusSourceType SourceType, int GroupId), GamepadFocusTarget> byButton,
        ref int order)
    {
        var key = (button, sourceType, groupId);
        ConsoleDisplayLine targetLine = button.ParentLine ?? parentLine;
        if (byButton.TryGetValue(key, out GamepadFocusTarget target))
        {
            target.Bounds = Rectangle.Union(target.Bounds, bounds);
            target.RawBounds = RectangleF.Union(target.RawBounds, rawBounds);
            if (layoutType == GamepadFocusLayoutType.Html)
                target.LayoutType = GamepadFocusLayoutType.Html;
        }
        else
        {
            target = new GamepadFocusTarget(button, sourceType, layoutType, groupId, targetLine,
                bounds, rawBounds, order++);
            byButton.Add(key, target);
            gamepadFocusTargets.Add(target);
        }
    }

    private void ApplyGamepadHtmlModalNavigationScope(List<GamepadFocusTarget> targets)
    {
        Size viewport = new(window.MainPicBox.Width, window.MainPicBox.Height);
        ApplyGamepadHtmlModalNavigationScope(targets, viewport,
            Program.GamepadDebugMode ? WriteGamepadNavigationDiagnostic : null);
    }

    private static void ApplyGamepadHtmlModalNavigationScope(
        List<GamepadFocusTarget> targets, Size viewport, Action<string> diagnostic)
    {
        if (targets == null || targets.Count == 0)
            return;

        for (int i = 0; i < targets.Count; i++)
        {
            targets[i].IsDirectionalFocusExcluded = false;
            targets[i].IsModalForeground = false;
            targets[i].IsModalBackdrop = false;
        }

        List<IGrouping<int, GamepadFocusTarget>> islandGroups = targets
            .Where(target => target.SourceType == GamepadFocusSourceType.HtmlIsland)
            .GroupBy(target => target.GroupId)
            .ToList();
        GamepadFocusTarget selectedBackdrop = null;
        List<GamepadFocusTarget> selectedForegroundTargets = null;
        int selectedForegroundGroup = int.MinValue;

        for (int i = 0; i < targets.Count; i++)
        {
            GamepadFocusTarget backdrop = targets[i];
            if (!IsHtmlModalBackdropCandidate(backdrop, viewport))
                continue;

            IGrouping<int, GamepadFocusTarget> foregroundGroup = islandGroups
                .Where(group => group.Key > backdrop.GroupId)
                .OrderByDescending(group => group.Key)
                .FirstOrDefault(group =>
                {
                    List<GamepadFocusTarget> choices = group
                        .Where(target => target.Enabled && !target.IsBack)
                        .ToList();
                    return choices.Count > 0
                        && choices.Any(target => !IsLargeHtmlTarget(target, viewport));
                });
            if (foregroundGroup == null)
                continue;

            List<GamepadFocusTarget> foregroundTargets = foregroundGroup.ToList();
            if (selectedBackdrop == null || foregroundGroup.Key > selectedForegroundGroup)
            {
                selectedBackdrop = backdrop;
                selectedForegroundTargets = foregroundTargets;
                selectedForegroundGroup = foregroundGroup.Key;
            }
        }

        if (selectedBackdrop == null || selectedForegroundTargets == null)
            return;

        HashSet<GamepadFocusTarget> foregroundSet = new(selectedForegroundTargets);
        for (int i = 0; i < targets.Count; i++)
        {
            GamepadFocusTarget target = targets[i];
            bool isForeground = foregroundSet.Contains(target);
            target.IsModalForeground = isForeground;
            target.IsDirectionalFocusExcluded = !isForeground;
            target.IsModalBackdrop = ReferenceEquals(target, selectedBackdrop);
        }

        if (diagnostic == null)
            return;

        int foregroundChoiceCount = selectedForegroundTargets.Count(target => !target.IsBack);
        diagnostic(
            $"Modal navigation scope detected: foreground baseGroup={selectedForegroundGroup} "
            + $"background overlay baseGroup={selectedBackdrop.GroupId} "
            + $"foregroundTargets={foregroundChoiceCount}");
        diagnostic(
            $"Background target excluded from directional focus: "
            + $"input={GetGamepadButtonInput(selectedBackdrop.Button)} "
            + $"reason=modal backdrop");
    }

    private static bool IsHtmlModalBackdropCandidate(GamepadFocusTarget target, Size viewport)
    {
        return target != null
            && target.SourceType == GamepadFocusSourceType.HtmlIsland
            && target.IsBack
            && IsLargeHtmlTarget(target, viewport);
    }

    private static bool IsLargeHtmlTarget(GamepadFocusTarget target, Size viewport)
    {
        if (target == null)
            return false;

        int viewportWidth = Math.Max(1, viewport.Width);
        int viewportHeight = Math.Max(1, viewport.Height);
        int widthThreshold = Math.Max(256, viewportWidth * 3 / 4);
        int heightThreshold = Math.Max(256, viewportHeight * 3 / 4);
        return target.Bounds.Width >= widthThreshold
            && target.Bounds.Height >= heightThreshold;
    }

    private static void CollapseGamepadLogicalButtonFragments(
        List<GamepadFocusTarget> targets, Action<string> diagnostic)
    {
        if (targets == null || targets.Count < 2)
            return;

        List<GamepadFocusTarget> logicalTargets = [];
        int index = 0;
        while (index < targets.Count)
        {
            GamepadFocusTarget representative = targets[index];
            List<GamepadFocusTarget> fragments = [representative];
            int next = index + 1;
            GamepadFocusTarget previous = representative;
            while (next < targets.Count
                && IsAdjacentLogicalButtonFragment(previous, targets[next]))
            {
                GamepadFocusTarget fragment = targets[next];
                representative.Bounds = Rectangle.Union(representative.Bounds, fragment.Bounds);
                representative.RawBounds = RectangleF.Union(representative.RawBounds, fragment.RawBounds);
                fragments.Add(fragment);
                previous = fragment;
                next++;
            }

            logicalTargets.Add(representative);
            if (fragments.Count > 1)
                LogLogicalButtonFragmentGroup(fragments, diagnostic);
            index = next;
        }

        if (logicalTargets.Count == targets.Count)
            return;
        targets.Clear();
        targets.AddRange(logicalTargets);
    }

    private static bool IsAdjacentLogicalButtonFragment(
        GamepadFocusTarget previous, GamepadFocusTarget candidate)
    {
        // HTML has its own DOM/Island identity and navigation rules.  The
        // heuristic is intentionally limited to the normal Console renderer.
        if (previous == null || candidate == null
            || previous.SourceType != GamepadFocusSourceType.NormalDisplay
            || candidate.SourceType != GamepadFocusSourceType.NormalDisplay
            || previous.LayoutType != GamepadFocusLayoutType.Console
            || candidate.LayoutType != GamepadFocusLayoutType.Console
            || previous.GroupId != candidate.GroupId)
            return false;

        ConsoleButtonString previousButton = previous.Button;
        ConsoleButtonString candidateButton = candidate.Button;
        if (previousButton == null || candidateButton == null
            || previousButton.Generation != candidateButton.Generation
            || GetGamepadInputKey(previousButton) != GetGamepadInputKey(candidateButton))
            return false;

        bool hasLineNumbers = previous.LineNo >= 0 && candidate.LineNo >= 0;
        if (hasLineNumbers
            && (candidate.LineNo < previous.LineNo
                || candidate.LineNo - previous.LineNo > 2))
            return false;

        int lineHeight = Math.Max(1, Config.LineHeight);
        if (candidate.Bounds.Top <= previous.Bounds.Top)
            return false;

        int verticalGap = candidate.Bounds.Top - previous.Bounds.Bottom;
        if (verticalGap > lineHeight * 2 || verticalGap < -lineHeight)
            return false;

        int xTolerance = Math.Max(12, lineHeight * 2);
        int widthTolerance = Math.Max(24, lineHeight * 4);
        bool similarLeft = Math.Abs(candidate.Bounds.Left - previous.Bounds.Left) <= xTolerance;
        bool similarWidth = Math.Abs(candidate.Bounds.Width - previous.Bounds.Width) <= widthTolerance;
        bool horizontallyOverlaps = candidate.Bounds.Left < previous.Bounds.Right
            && previous.Bounds.Left < candidate.Bounds.Right;
        return similarLeft && (similarWidth || horizontallyOverlaps);
    }

    private static void LogLogicalButtonFragmentGroup(
        List<GamepadFocusTarget> fragments, Action<string> diagnostic)
    {
        if (diagnostic == null || fragments == null || fragments.Count < 2)
            return;

        GamepadFocusTarget representative = fragments[0];
        StringBuilder lines = new();
        for (int i = 0; i < fragments.Count; i++)
        {
            if (i > 0)
                lines.Append(',');
            lines.Append(fragments[i].LineNo);
        }
        diagnostic(
            $"Logical button group: input={GetGamepadButtonInput(representative.Button)} "
            + $"fragments={fragments.Count} representativeLine={representative.LineNo} lines={lines}");
    }

    private static bool TryGetGamepadNodeBounds(AConsoleDisplayNode node, out Rectangle bounds, out RectangleF rawBounds)
    {
        rawBounds = new RectangleF(node.Point.X, node.Point.Y, node.Size.Width, node.Size.Height);
        int width = Math.Max(0, (int)Math.Ceiling(node.Size.Width));
        int height = Math.Max(0, (int)Math.Ceiling(node.Size.Height));
        if (width <= 0 || height <= 0)
        {
            bounds = Rectangle.Empty;
            return false;
        }
        bounds = new Rectangle((int)Math.Floor(node.Point.X), (int)Math.Floor(node.Point.Y), width, height);
        return true;
    }

    private static bool TryGetGamepadButtonBounds(ConsoleButtonString button,
        out Rectangle bounds, out RectangleF rawBounds)
    {
        bounds = Rectangle.Empty;
        rawBounds = RectangleF.Empty;
        if (button?.StrArray == null)
            return false;

        bool found = false;
        for (int i = 0; i < button.StrArray.Length; i++)
        {
            AConsoleDisplayNode child = button.StrArray[i];
            if (!TryGetGamepadNodeBounds(child, out Rectangle childBounds, out RectangleF childRawBounds))
                continue;
            if (!found)
            {
                bounds = childBounds;
                rawBounds = childRawBounds;
                found = true;
            }
            else
            {
                bounds = Rectangle.Union(bounds, childBounds);
                rawBounds = RectangleF.Union(rawBounds, childRawBounds);
            }
        }
        return found;
    }

    private static bool IsSameNavigationGroup(GamepadFocusTarget left, GamepadFocusTarget right)
    {
        return left != null && right != null
            && left.SourceType == right.SourceType
            && left.GroupId == right.GroupId
            && left.NavigationGroupId == right.NavigationGroupId;
    }

    private static bool IsSameNavigationGroup(GamepadFocusTarget target,
        GamepadFocusSourceType sourceType, int groupId, int navigationGroupId)
    {
        return target != null && target.SourceType == sourceType && target.GroupId == groupId
            && target.NavigationGroupId == navigationGroupId;
    }

    private GamepadFocusTarget FindRestoredGamepadFocus(List<GamepadFocusTarget> targets, bool sameGroupOnly)
    {
        for (int i = 0; i < targets.Count; i++)
        {
            GamepadFocusTarget target = targets[i];
            if (sameGroupOnly && !IsSameNavigationGroup(target, lastGamepadFocusSourceType,
                lastGamepadFocusGroupId, lastGamepadFocusNavigationGroupId))
                continue;
            if (string.Equals(GetGamepadInputKey(target.Button), lastGamepadFocusInputKey, StringComparison.Ordinal))
                return target;
        }
        return null;
    }

    private GamepadFocusTarget FindNearestRememberedGamepadFocus(List<GamepadFocusTarget> targets)
    {
        GamepadFocusTarget best = null;
        long bestDistance = long.MaxValue;
        for (int i = 0; i < targets.Count; i++)
        {
            GamepadFocusTarget target = targets[i];
            if (!IsSameNavigationGroup(target, lastGamepadFocusSourceType,
                lastGamepadFocusGroupId, lastGamepadFocusNavigationGroupId))
                continue;
            long distance = DistanceSquared(target, lastGamepadFocusBounds);
            if (best == null || distance < bestDistance
                || (distance == bestDistance && target.Order < best.Order))
            {
                best = target;
                bestDistance = distance;
            }
        }
        return best;
    }

    private GamepadFocusTarget GetCurrentGamepadFocusTarget()
    {
        GetGamepadFocusTargets();
        GamepadFocusTarget current = gamepadNavigationGraph.Find(selectingButton);
        if (current == null)
        {
            GamepadEnsureSelection();
            current = gamepadNavigationGraph.Find(selectingButton);
        }
        return current != null && CanSelectGamepadButton(current.Button) ? current : null;
    }

    private GamepadFocusTarget FindGamepadBackTarget(List<GamepadFocusTarget> targets, GamepadFocusTarget current)
    {
        GamepadFocusTarget best = null;
        int bestPriority = int.MaxValue;
        long bestDistance = long.MaxValue;
        int bestOrder = int.MaxValue;
        for (int i = 0; i < targets.Count; i++)
        {
            GamepadFocusTarget target = targets[i];
            if (!target.IsBack || !target.Enabled || !CanSelectGamepadButton(target.Button))
                continue;

            int priority = GetGamepadBackPriority(target, current);
            long distance = current == null ? long.MaxValue : DistanceSquared(target, current.Bounds);
            if (best == null || priority < bestPriority
                || (priority == bestPriority && distance < bestDistance)
                || (priority == bestPriority && distance == bestDistance && target.Order < bestOrder))
            {
                best = target;
                bestPriority = priority;
                bestDistance = distance;
                bestOrder = target.Order;
            }
        }
        return best;
    }

    private static int GetGamepadBackPriority(GamepadFocusTarget target, GamepadFocusTarget current)
    {
        if (current == null)
            return 2;
        if (IsSameNavigationGroup(target, current))
            return 0;
        if (target.SourceType == current.SourceType && target.GroupId == current.GroupId)
            return 1;
        return 2;
    }

    private void LogGamepadBackCandidates(List<GamepadFocusTarget> targets,
        GamepadFocusTarget current, GamepadFocusTarget selected)
    {
        if (!Program.GamepadDebugMode)
            return;

        WriteGamepadNavigationDiagnostic("Back candidates:");
        bool found = false;
        for (int i = 0; i < targets.Count; i++)
        {
            GamepadFocusTarget target = targets[i];
            if (!target.IsBack || !target.Enabled || !CanSelectGamepadButton(target.Button))
                continue;
            found = true;
            WriteGamepadNavigationDiagnostic(
                $"Back candidate: input={GetGamepadButtonInput(target.Button)} "
                + $"text=\"{SanitizeGamepadText(target.Button.ToString())}\" "
                + $"navigationGroup={target.NavigationGroupId} source={target.SourceName} "
                + $"baseGroup={target.GroupId} priority={GetGamepadBackPriority(target, current)} "
                + $"distance={(current == null ? "n/a" : DistanceSquared(target, current.Bounds).ToString())} "
                + $"selected={ReferenceEquals(target, selected)}");
        }
        if (!found)
            WriteGamepadNavigationDiagnostic("Back candidates: <none>");
    }

    private static bool IsGamepadBackButton(GamepadFocusTarget target)
    {
        if (target?.Button == null)
            return false;

        string text = NormalizeGamepadSemanticText(target.Button.ToString());
        string title = NormalizeGamepadSemanticText(target.Button.Title);
        string input = NormalizeGamepadSemanticText(GetGamepadButtonInput(target.Button));
        if (IsGamepadBackSemanticToken(input) || IsGamepadBackSemanticText(input))
            return true;
        return IsGamepadBackSemanticText(text) || IsGamepadBackSemanticText(title);
    }

    private static bool IsGamepadBackSemanticToken(string text)
    {
        return string.Equals(text, "CANCEL", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "BACK", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "RETURN", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "EXIT", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "QUIT", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGamepadBackSemanticText(string text)
    {
        string normalized = NormalizeGamepadSemanticText(text);
        if (IsGamepadBackSemanticToken(normalized))
            return true;
        foreach (string label in GamepadBackButtonLabels)
        {
            if (normalized.Contains(label, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Back判定専用の文字列正規化。表示文字列は変更せず、互換文字とボタン番号の
    /// 装飾だけを比較用に取り除く。これにより全角英字のCANCEL等も判定できる。
    /// </summary>
    private static string NormalizeGamepadSemanticText(string text)
    {
        string normalized = (text ?? string.Empty)
            .Normalize(NormalizationForm.FormKC)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();

        if (normalized.StartsWith('['))
        {
            int closeBracket = normalized.IndexOf(']');
            if (closeBracket > 1)
            {
                string prefix = normalized.Substring(1, closeBracket - 1).Trim();
                bool isButtonNumber = prefix.Length > 0;
                for (int i = 0; isButtonNumber && i < prefix.Length; i++)
                    isButtonNumber = prefix[i] >= '0' && prefix[i] <= '9';
                if (isButtonNumber)
                    normalized = normalized.Substring(closeBracket + 1).Trim();
            }
        }
        return normalized;
    }

    private static bool TryFindGamepadPageNavigationSet(List<GamepadFocusTarget> targets,
        GamepadFocusTarget currentFocus, out GamepadPageNavigationSet result, out string unavailableReason)
    {
        // Indexed型を先に評価する。既存のPAGE.N画面とDirectional型が同一の
        // InputRequestに混在しても、既存動作を優先する。
        if (TryFindGamepadIndexedPageNavigationSet(targets, currentFocus, out result,
                out string indexedReason))
        {
            unavailableReason = string.Empty;
            return true;
        }

        if (TryFindGamepadDirectionalPageNavigationSet(targets, currentFocus, out result,
                out string directionalReason))
        {
            unavailableReason = string.Empty;
            return true;
        }

        unavailableReason = directionalReason == "no interactive directional page targets"
            ? indexedReason
            : directionalReason;
        return false;
    }

    private static bool TryFindGamepadIndexedPageNavigationSet(List<GamepadFocusTarget> targets,
        GamepadFocusTarget currentFocus, out GamepadPageNavigationSet result, out string unavailableReason)
    {
        result = null;
        unavailableReason = "no interactive PAGE targets";
        if (targets == null || targets.Count == 0)
            return false;

        // HTML modal表示中はforegroundだけを候補にする。背後のPAGEボタンを
        // 押してmodalを壊さないことを、Directional Focus scopeと同じ基準で保証する。
        bool hasModalScope = targets.Any(target => target.IsModalForeground);
        IEnumerable<GamepadFocusTarget> scopedTargets = hasModalScope
            ? targets.Where(target => target.IsModalForeground)
            : targets.Where(target => !target.IsDirectionalFocusExcluded);

        Dictionary<(GamepadFocusSourceType SourceType, int GroupId, int NavigationGroupId),
            GamepadPageNavigationSet> sets = [];
        foreach (GamepadFocusTarget target in scopedTargets)
        {
            if (!target.Enabled || !TryGetGamepadPageIndex(target, out int pageIndex))
                continue;

            var key = (target.SourceType, target.GroupId, target.NavigationGroupId);
            if (!sets.TryGetValue(key, out GamepadPageNavigationSet pageSet))
            {
                pageSet = new GamepadPageNavigationSet
                {
                    Mode = GamepadPageNavigationMode.Indexed,
                    SourceType = target.SourceType,
                    GroupId = target.GroupId,
                    NavigationGroupId = target.NavigationGroupId,
                };
                sets.Add(key, pageSet);
            }
            pageSet.Targets.Add(new GamepadPageTarget(pageIndex, target));
        }

        List<GamepadPageNavigationSet> eligibleSets = [];
        foreach (GamepadPageNavigationSet pageSet in sets.Values)
        {
            if (pageSet.Targets.Count < 2)
                continue;

            pageSet.Targets.Sort((left, right) => left.PageIndex.CompareTo(right.PageIndex));
            bool hasDuplicateIndex = false;
            for (int i = 1; i < pageSet.Targets.Count; i++)
            {
                if (pageSet.Targets[i - 1].PageIndex == pageSet.Targets[i].PageIndex)
                {
                    hasDuplicateIndex = true;
                    break;
                }
            }
            if (hasDuplicateIndex)
                continue;

            if (!TryResolveCurrentGamepadPage(pageSet, out int currentPageIndex))
                continue;
            pageSet.CurrentPageIndex = currentPageIndex;
            eligibleSets.Add(pageSet);
        }

        if (eligibleSets.Count == 0)
        {
            unavailableReason = hasModalScope
                ? "modal foreground has no uniquely marked PAGE set"
                : "no PAGE set with a unique current-page visual marker";
            return false;
        }

        if (currentFocus != null)
        {
            List<GamepadPageNavigationSet> focusScopedSets = eligibleSets
                .Where(pageSet => pageSet.SourceType == currentFocus.SourceType
                    && pageSet.GroupId == currentFocus.GroupId
                    && pageSet.NavigationGroupId == currentFocus.NavigationGroupId)
                .ToList();
            if (focusScopedSets.Count == 1)
            {
                result = focusScopedSets[0];
                return true;
            }
            if (focusScopedSets.Count > 1)
            {
                unavailableReason = "multiple PAGE sets in the current navigation group";
                return false;
            }
        }

        if (eligibleSets.Count == 1)
        {
            result = eligibleSets[0];
            return true;
        }

        unavailableReason = "multiple PAGE sets with no focused navigation-group match";
        return false;
    }

    private static bool TryFindGamepadDirectionalPageNavigationSet(List<GamepadFocusTarget> targets,
        GamepadFocusTarget currentFocus, out GamepadPageNavigationSet result, out string unavailableReason)
    {
        result = null;
        unavailableReason = "no interactive directional page targets";
        if (targets == null || targets.Count == 0)
            return false;

        // Indexed型と同じforeground scopeを使う。Modal中は背後のConsole/HTMLを
        // 混ぜず、modal内のDirectional Targetだけを候補にする。
        bool hasModalScope = targets.Any(target => target.IsModalForeground);
        IEnumerable<GamepadFocusTarget> scopedTargets = hasModalScope
            ? targets.Where(target => target.IsModalForeground)
            : targets.Where(target => !target.IsDirectionalFocusExcluded);

        Dictionary<(GamepadFocusSourceType SourceType, int GroupId, int NavigationGroupId),
            GamepadPageNavigationSet> sets = [];
        foreach (GamepadFocusTarget target in scopedTargets)
        {
            if (!target.Enabled || target.Button == null)
                continue;

            GamepadDirectionalPageTargetKind kind =
                GetGamepadDirectionalPageTargetKind(target);
            if (kind == GamepadDirectionalPageTargetKind.None)
                continue;

            var key = (target.SourceType, target.GroupId, target.NavigationGroupId);
            if (!sets.TryGetValue(key, out GamepadPageNavigationSet pageSet))
            {
                pageSet = new GamepadPageNavigationSet
                {
                    Mode = GamepadPageNavigationMode.Directional,
                    SourceType = target.SourceType,
                    GroupId = target.GroupId,
                    NavigationGroupId = target.NavigationGroupId,
                };
                sets.Add(key, pageSet);
            }

            if (kind == GamepadDirectionalPageTargetKind.Previous)
            {
                if (pageSet.PreviousTarget != null)
                    pageSet.HasAmbiguousDirectionalTarget = true;
                else
                    pageSet.PreviousTarget = target;
            }
            else
            {
                if (pageSet.NextTarget != null)
                    pageSet.HasAmbiguousDirectionalTarget = true;
                else
                    pageSet.NextTarget = target;
            }
        }

        List<GamepadPageNavigationSet> eligibleSets = sets.Values
            .Where(pageSet => !pageSet.HasAmbiguousDirectionalTarget
                && (pageSet.PreviousTarget != null || pageSet.NextTarget != null))
            .ToList();
        if (eligibleSets.Count == 0)
        {
            unavailableReason = hasModalScope
                ? "modal foreground has no directional page targets"
                : "no interactive directional page targets";
            return false;
        }

        if (currentFocus != null)
        {
            List<GamepadPageNavigationSet> focusScopedSets = eligibleSets
                .Where(pageSet => pageSet.SourceType == currentFocus.SourceType
                    && pageSet.GroupId == currentFocus.GroupId
                    && pageSet.NavigationGroupId == currentFocus.NavigationGroupId)
                .ToList();
            if (focusScopedSets.Count == 1)
            {
                result = focusScopedSets[0];
                return true;
            }
            if (focusScopedSets.Count > 1)
            {
                unavailableReason = "multiple directional page sets in the current navigation group";
                return false;
            }
        }

        if (eligibleSets.Count == 1)
        {
            result = eligibleSets[0];
            return true;
        }

        unavailableReason = "multiple directional page sets with no focused navigation-group match";
        return false;
    }

    private static GamepadDirectionalPageTargetKind GetGamepadDirectionalPageTargetKind(
        GamepadFocusTarget target)
    {
        string normalized = NormalizeGamepadSemanticText(target?.Button?.ToString());
        if (normalized.Length == 0)
            return GamepadDirectionalPageTargetKind.None;

        // 表示ラベル全体を比較する。これにより「戻る」「次へ」などの通常ボタンや
        // 説明文中の語を、ページ移動ボタンとして誤認しない。
        if (normalized.Equals("前のページ", StringComparison.Ordinal)
            || normalized.Equals("前ページ", StringComparison.Ordinal)
            || normalized.Equals("PREVIOUS PAGE", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("PREV PAGE", StringComparison.OrdinalIgnoreCase))
        {
            return GamepadDirectionalPageTargetKind.Previous;
        }

        // 「後ろのページ」は参照ゲームの実際のHELP画面で使われている表記。
        if (normalized.Equals("次のページ", StringComparison.Ordinal)
            || normalized.Equals("次ページ", StringComparison.Ordinal)
            || normalized.Equals("後ろのページ", StringComparison.Ordinal)
            || normalized.Equals("NEXT PAGE", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("NEXT", StringComparison.OrdinalIgnoreCase))
        {
            return GamepadDirectionalPageTargetKind.Next;
        }

        return GamepadDirectionalPageTargetKind.None;
    }

    private static bool TryGetGamepadPageIndex(GamepadFocusTarget target, out int pageIndex)
    {
        pageIndex = -1;
        if (target?.Button == null)
            return false;

        // PAGE Navigationは画面に見えているButtonラベルだけを読む。Title/tooltipは
        // 説明用metadataなので、そこにPAGEという語があっても誤認しない。
        return TryParseGamepadPageLabel(NormalizeGamepadSemanticText(target.Button.ToString()), out pageIndex);
    }

    private static bool TryParseGamepadPageLabel(string normalizedText, out int pageIndex)
    {
        pageIndex = -1;
        const string pagePrefix = "PAGE";
        if (string.IsNullOrEmpty(normalizedText)
            || !normalizedText.StartsWith(pagePrefix, StringComparison.OrdinalIgnoreCase)
            || normalizedText.Length == pagePrefix.Length)
            return false;

        int index = pagePrefix.Length;
        if (normalizedText[index] == '.')
        {
            index++;
        }
        else if (char.IsWhiteSpace(normalizedText[index]))
        {
            while (index < normalizedText.Length && char.IsWhiteSpace(normalizedText[index]))
                index++;
        }
        else
        {
            // "PAGE"を含む説明文やPAGE0のような別ラベルは対象にしない。
            return false;
        }

        string pageNumber = normalizedText[index..].Trim();
        return pageNumber.Length > 0 && int.TryParse(pageNumber, out pageIndex) && pageIndex >= 0;
    }

    private static bool TryResolveCurrentGamepadPage(GamepadPageNavigationSet pageSet,
        out int currentPageIndex)
    {
        currentPageIndex = -1;
        int markedTargetCount = 0;
        for (int i = 0; i < pageSet.Targets.Count; i++)
        {
            GamepadPageTarget page = pageSet.Targets[i];
            if (!TryGetGamepadPageVisualMarker(page.Target.Button, out bool isMarked))
                return false;
            if (!isMarked)
                continue;
            markedTargetCount++;
            currentPageIndex = page.PageIndex;
        }

        // 元ゲームはcurrent pageだけSETCOLOR(aqua)、他はGETDEFCOLOR()で描画する。
        // 色名を固定せず、Config.ForeColorとの差があるPAGEターゲットが唯一の時だけ採用する。
        return markedTargetCount == 1;
    }

    private static bool TryGetGamepadPageVisualMarker(ConsoleButtonString button, out bool isMarked)
    {
        bool hasStyledText = false;
        isMarked = false;
        CollectGamepadPageVisualMarker(button, ref hasStyledText, ref isMarked);
        return hasStyledText;
    }

    private static void CollectGamepadPageVisualMarker(AConsoleDisplayNode node,
        ref bool hasStyledText, ref bool isMarked)
    {
        if (node == null)
            return;
        if (node is ConsoleStyledString styled)
        {
            if (!string.IsNullOrWhiteSpace(styled.Text))
            {
                hasStyledText = true;
                if (styled.StringStyle.Color != Config.ForeColor)
                    isMarked = true;
            }
            return;
        }
        if (node is ConsoleButtonString button)
        {
            for (int i = 0; i < button.StrArray.Length; i++)
                CollectGamepadPageVisualMarker(button.StrArray[i], ref hasStyledText, ref isMarked);
            return;
        }
        if (node is ConsoleDivElement div)
        {
            for (int i = 0; i < div._childNodes.Count; i++)
                CollectGamepadPageVisualMarker(div._childNodes[i], ref hasStyledText, ref isMarked);
        }
    }

    private static GamepadFocusTarget FindGamepadAdjacentPageTarget(GamepadPageNavigationSet pageSet,
        bool nextPage, out int targetPageIndex)
    {
        targetPageIndex = -1;
        if (pageSet == null || pageSet.Targets.Count == 0)
            return null;

        int currentPosition = -1;
        for (int i = 0; i < pageSet.Targets.Count; i++)
        {
            if (pageSet.Targets[i].PageIndex == pageSet.CurrentPageIndex)
            {
                currentPosition = i;
                break;
            }
        }
        if (currentPosition < 0)
            return null;

        int targetPosition = currentPosition + (nextPage ? 1 : -1);
        if (targetPosition < 0 || targetPosition >= pageSet.Targets.Count)
            return null;

        GamepadPageTarget target = pageSet.Targets[targetPosition];
        targetPageIndex = target.PageIndex;
        return target.Target;
    }

    private static void RunGamepadSelfTests()
    {
        if (gamepadSelfTestsRun)
            return;
        gamepadSelfTestsRun = true;

        for (int suiteIndex = 0; suiteIndex < GamepadSelfTestSuites.Length; suiteIndex++)
        {
            GamepadSelfTestSuite suite = GamepadSelfTestSuites[suiteIndex];
            GamepadSelfTestCase[] cases = suite.GetCases();
            bool passed = true;
            for (int caseIndex = 0; caseIndex < cases.Length; caseIndex++)
            {
                GamepadSelfTestCase testCase = cases[caseIndex];
                if (testCase.Actual == testCase.Expected)
                    continue;

                passed = false;
                WriteGamepadNavigationDiagnostic(
                    $"Gamepad self-test FAILED: suite={suite.Name} case={testCase.Name} "
                    + $"expected={testCase.Expected} actual={testCase.Actual}");
            }
            WriteGamepadNavigationDiagnostic($"Gamepad {suite.Name} self-test: "
                + (passed ? "PASS" : "WARNING"));
        }
    }

    private static GamepadSelfTestCase[] GetGamepadSemanticBackSelfTestCases()
    {
        (string Text, bool Expected)[] cases =
        [
            ("CANCEL", true),
            ("ＣＡＮＣＥＬ", true),
            ("[9]CANCEL", true),
            ("[9]ＣＡＮＣＥＬ", true),
            ("[ 0] ＣＡＮＣＥＬ", true),
            ("BACK", true),
            ("ＢＡＣＫ", true),
            ("RETURN", true),
            ("ＲＥＴＵＲＮ", true),
            ("キャンセル", true),
            ("戻る", true),
            ("[0] NEW GAME", false),
        ];

        GamepadSelfTestCase[] results = new GamepadSelfTestCase[cases.Length];
        for (int i = 0; i < cases.Length; i++)
        {
            (string text, bool expected) = cases[i];
            bool actual = IsGamepadBackSemanticText(text);
            results[i] = new GamepadSelfTestCase($"text=\"{text}\"", actual, expected);
        }
        return results;
    }

    private static GamepadSelfTestCase[] GetGamepadFocusPersistenceSelfTestCases()
    {
        List<GamepadFocusTarget> before = CreatePostConfirmSelfTestTargets(8, 0, false);
        PostConfirmFocusAnchor anchor = CreatePostConfirmSelfTestAnchor(before[5], before);
        List<GamepadFocusTarget> toggled = CreatePostConfirmSelfTestTargets(8, 0, false);
        bool caseA = IsPostConfirmSameScreen(anchor, toggled, out _)
            && FindPostConfirmExactTarget(anchor, toggled) == toggled[5];

        PostConfirmFocusAnchor secondAnchor = CreatePostConfirmSelfTestAnchor(toggled[5], toggled);
        List<GamepadFocusTarget> toggledAgain = CreatePostConfirmSelfTestTargets(8, 0, false);
        bool caseB = IsPostConfirmSameScreen(secondAnchor, toggledAgain, out _)
            && FindPostConfirmExactTarget(secondAnchor, toggledAgain) == toggledAgain[5];

        List<GamepadFocusTarget> differentScreen = CreatePostConfirmSelfTestTargets(3, 1, false);
        bool caseC = !IsPostConfirmSameScreen(anchor, differentScreen, out _)
            && FindPostConfirmExactTarget(anchor, differentScreen) == null;

        List<GamepadFocusTarget> oldZero = CreatePostConfirmSelfTestTargets(1, 0, false, 0);
        List<GamepadFocusTarget> sameInputDifferentScreen = CreatePostConfirmSelfTestTargets(1, 1, true, 0);
        PostConfirmFocusAnchor zeroAnchor = CreatePostConfirmSelfTestAnchor(oldZero[0], oldZero);
        bool caseD = !IsPostConfirmSameScreen(zeroAnchor, sameInputDifferentScreen, out _)
            && FindPostConfirmExactTarget(zeroAnchor, sameInputDifferentScreen) == null;

        List<GamepadFocusTarget> disappearingBefore = CreatePostConfirmSelfTestTargets(4, 0, false);
        PostConfirmFocusAnchor disappearingAnchor = CreatePostConfirmSelfTestAnchor(
            disappearingBefore[1], disappearingBefore);
        List<GamepadFocusTarget> disappearingAfter = CreatePostConfirmSelfTestTargets(4, 0, false);
        disappearingAfter.RemoveAt(1);
        bool caseE = IsPostConfirmSameScreen(disappearingAnchor, disappearingAfter, out _)
            && FindPostConfirmExactTarget(disappearingAnchor, disappearingAfter) == null
            && FindPostConfirmLaneFallback(disappearingAnchor, disappearingAfter) == disappearingAfter[1];

        List<GamepadFocusTarget> dynamicBefore = CreatePostConfirmSelfTestTargets(6, 2, false);
        PostConfirmFocusAnchor dynamicAnchor = CreatePostConfirmSelfTestAnchor(dynamicBefore[4], dynamicBefore);
        List<GamepadFocusTarget> dynamicAfter = CreatePostConfirmSelfTestTargets(6, 2, false);
        for (int i = 0; i < dynamicAfter.Count; i++)
            dynamicAfter[i].Bounds = new Rectangle(dynamicAfter[i].Bounds.X + 24,
                dynamicAfter[i].Bounds.Y, dynamicAfter[i].Bounds.Width, dynamicAfter[i].Bounds.Height);
        bool caseF = !IsPostConfirmSameScreen(dynamicAnchor, dynamicAfter, out _)
            && TryMatchGamepadScreenFingerprints(CreateGamepadScreenFingerprint(dynamicBefore),
                CreateGamepadScreenFingerprint(dynamicAfter), out _, out _);

        List<GamepadFocusTarget> sameInputBefore = CreatePostConfirmSelfTestTargets(8, 3, false, 0);
        PostConfirmFocusAnchor sameInputAnchor = CreatePostConfirmSelfTestAnchor(
            sameInputBefore[6], sameInputBefore);
        List<GamepadFocusTarget> sameInputAfter = CreatePostConfirmSelfTestTargets(8, 3, false, 0);
        sameInputAfter[6] = CreatePostConfirmSelfTestTarget("6", 3, 20, 6);
        sameInputAfter[7] = CreatePostConfirmSelfTestTarget("+", 3, 188, 7);
        bool caseG = IsPostConfirmSameScreen(sameInputAnchor, sameInputAfter, out _)
            && FindPostConfirmExactTarget(sameInputAnchor, sameInputAfter) == null
            && FindPostConfirmSameInputTarget(sameInputAnchor, sameInputAfter) == sameInputAfter[6]
            && FindPostConfirmLaneFallback(sameInputAnchor, sameInputAfter) == sameInputAfter[7];

        return
        [
            new("toggle", caseA),
            new("repeat", caseB),
            new("screen-change", caseC),
            new("same-input-screen-change", caseD),
            new("target-disappeared-lane", caseE),
            new("dynamic-screen-rejected", caseF),
            new("same-input-before-position-fallback", caseG),
        ];
    }

    private static List<GamepadFocusTarget> CreatePostConfirmSelfTestTargets(int count,
        int groupId, bool lastIsBack, int firstInput = 1)
    {
        List<GamepadFocusTarget> targets = [];
        for (int i = 0; i < count; i++)
        {
            ConsoleButtonString button = new(null, [], (firstInput + i).ToString());
            Rectangle bounds = new(220, 20 + i * 24, 160, 18);
            GamepadFocusTarget target = new(button, GamepadFocusSourceType.NormalDisplay,
                GamepadFocusLayoutType.Console, groupId, null, bounds, bounds, i);
            target.IsBack = lastIsBack && i == count - 1;
            targets.Add(target);
        }
        return targets;
    }

    private static GamepadFocusTarget CreatePostConfirmSelfTestTarget(string input, int groupId,
        int y, int order)
    {
        ConsoleButtonString button = new(null, [], input);
        Rectangle bounds = new(220, y, 160, 18);
        return new GamepadFocusTarget(button, GamepadFocusSourceType.NormalDisplay,
            GamepadFocusLayoutType.Console, groupId, null, bounds, bounds, order);
    }

    private static PostConfirmFocusAnchor CreatePostConfirmSelfTestAnchor(
        GamepadFocusTarget target, List<GamepadFocusTarget> targets)
    {
        PostConfirmFocusAnchor anchor = new()
        {
            Focus = new GamepadScreenTargetSnapshot(target),
            RequestId = 1,
            ButtonGeneration = 1,
            Targets = CreateGamepadFocusSnapshots(targets),
        };
        return anchor;
    }

    private static GamepadSelfTestCase[] GetGamepadReturnFocusHistorySelfTestCases()
    {
        // A: A → B → Back → A。Aで最後に選んだ5番へ戻る。
        List<GamepadFocusTarget> screenA = CreateReturnFocusHistorySelfTestTargets(
            ["0", "1", "2", "3", "4", "5", "6"], groupId: 10);
        GamepadFocusHistoryEntry entryA = CreateReturnFocusHistorySelfTestEntry(screenA[5], screenA);
        List<GamepadFocusTarget> screenAReturned = CreateReturnFocusHistorySelfTestTargets(
            ["0", "1", "2", "3", "4", "5", "6"], groupId: 10);
        bool caseA = TryMatchGamepadScreenFingerprints(entryA.Fingerprint,
                CreateGamepadScreenFingerprint(screenAReturned), out _, out _)
            && TryRestoreGamepadFocusHistory(entryA, screenAReturned, out GamepadFocusTarget restoredA,
                out _)
            && restoredA == screenAReturned[5];

        // B: A → B → C → Back → B → Back → A。stack最上段から順に復元する。
        List<GamepadFocusTarget> screenB = CreateReturnFocusHistorySelfTestTargets(
            ["10", "11", "12", "13", "14"], groupId: 20);
        GamepadFocusHistoryEntry entryB = CreateReturnFocusHistorySelfTestEntry(screenB[3], screenB);
        List<GamepadFocusHistoryEntry> returnStack = [entryA, entryB];
        List<GamepadFocusTarget> screenBReturned = CreateReturnFocusHistorySelfTestTargets(
            ["10", "11", "12", "13", "14"], groupId: 20);
        bool caseBFirst = TryMatchGamepadScreenFingerprints(returnStack[^1].Fingerprint,
                CreateGamepadScreenFingerprint(screenBReturned), out _, out _)
            && TryRestoreGamepadFocusHistory(returnStack[^1], screenBReturned,
                out GamepadFocusTarget restoredB, out _)
            && restoredB == screenBReturned[3];
        if (caseBFirst)
            returnStack.RemoveAt(returnStack.Count - 1);
        bool caseBSecond = returnStack.Count == 1
            && TryMatchGamepadScreenFingerprints(returnStack[^1].Fingerprint,
                CreateGamepadScreenFingerprint(screenAReturned), out _, out _)
            && TryRestoreGamepadFocusHistory(returnStack[^1], screenAReturned,
                out GamepadFocusTarget restoredAAgain, out _)
            && restoredAAgain == screenAReturned[5];
        bool caseB = caseBFirst && caseBSecond;

        // C: 同じinput=0でも、Back性・ラベル・構造が異なる画面へは適用しない。
        List<GamepadFocusTarget> sameInputDifferentScreen = CreateReturnFocusHistorySelfTestTargets(
            ["CANCEL"], groupId: 30, firstIsBack: true);
        bool caseC = !TryMatchGamepadScreenFingerprints(
            CreateReturnFocusHistorySelfTestEntry(
                CreateReturnFocusHistorySelfTestTargets(["通常項目"], groupId: 30)[0],
                CreateReturnFocusHistorySelfTestTargets(["通常項目"], groupId: 30)).Fingerprint,
            CreateGamepadScreenFingerprint(sameInputDifferentScreen), out _, out _);

        // D: 同一画面のConfirm再描画は既存PostConfirmの同一画面判定が優先される。
        List<GamepadFocusTarget> toggleBefore = CreatePostConfirmSelfTestTargets(6, 40, false);
        PostConfirmFocusAnchor toggleAnchor = CreatePostConfirmSelfTestAnchor(toggleBefore[4], toggleBefore);
        List<GamepadFocusTarget> toggleAfter = CreatePostConfirmSelfTestTargets(6, 40, false);
        bool caseD = IsPostConfirmSameScreen(toggleAnchor, toggleAfter, out _)
            && FindPostConfirmExactTarget(toggleAnchor, toggleAfter) == toggleAfter[4];

        // E: modalの0/1は背景screenと別fingerprint。Cancel後は背景Entryだけを復元する。
        List<GamepadFocusTarget> modalTargets = CreateReturnFocusHistorySelfTestTargets(
            ["はい", "いいえ"], groupId: 90, modalForeground: true);
        bool caseE = !TryMatchGamepadScreenFingerprints(entryA.Fingerprint,
                CreateGamepadScreenFingerprint(modalTargets), out _, out _)
            && TryRestoreGamepadFocusHistory(entryA, screenAReturned,
                out GamepadFocusTarget modalReturn, out _)
            && modalReturn == screenAReturned[5];

        // F: 元の6が消えても、同じ縦レーンで近い7へフォールバックする。
        List<GamepadFocusTarget> disappearBefore = CreateReturnFocusHistorySelfTestTargets(
            ["5", "6", "7", "8"], groupId: 50);
        GamepadFocusHistoryEntry disappearEntry = CreateReturnFocusHistorySelfTestEntry(disappearBefore[1],
            disappearBefore);
        List<GamepadFocusTarget> disappearAfter = CreateReturnFocusHistorySelfTestTargets(
            ["5", "7", "8"], groupId: 50);
        bool caseF = TryMatchGamepadScreenFingerprints(disappearEntry.Fingerprint,
                CreateGamepadScreenFingerprint(disappearAfter), out _, out _)
            && TryRestoreGamepadFocusHistory(disappearEntry, disappearAfter,
                out GamepadFocusTarget disappearedFallback, out string disappearedReason)
            && disappearedFallback == disappearAfter[1]
            && disappearedReason.Contains("vertical lane", StringComparison.Ordinal);

        // G: 履歴がない初回表示では従来のInitial Focusを使う。
        List<GamepadFocusTarget> firstVisit = CreateReturnFocusHistorySelfTestTargets(
            ["0", "1", "戻る"], groupId: 60, lastIsBack: true);
        bool caseG = FindInitialGamepadFocus(firstVisit) == firstVisit[0];

        // H: 1件程度の増減は、共通するInteractive構造が十分なら同一screenとみなす。
        List<GamepadFocusTarget> dynamicBefore = CreateReturnFocusHistorySelfTestTargets(
            ["0", "1", "2", "3", "4", "5"], groupId: 70);
        List<GamepadFocusTarget> dynamicAfter = CreateReturnFocusHistorySelfTestTargets(
            ["0", "1", "2", "3", "4", "5", "6"], groupId: 70);
        bool caseH = TryMatchGamepadScreenFingerprints(
            CreateReturnFocusHistorySelfTestEntry(dynamicBefore[4], dynamicBefore).Fingerprint,
            CreateGamepadScreenFingerprint(dynamicAfter), out _, out _);

        // I: 入力構造が大きく違う別screenには履歴を適用しない。
        List<GamepadFocusTarget> unrelatedScreen = CreateReturnFocusHistorySelfTestTargets(
            ["20", "21", "22", "23", "24", "25"], groupId: 80);
        bool caseI = !TryMatchGamepadScreenFingerprints(
            CreateReturnFocusHistorySelfTestEntry(dynamicBefore[4], dynamicBefore).Fingerprint,
            CreateGamepadScreenFingerprint(unrelatedScreen), out _, out _);

        return
        [
            new("return", caseA),
            new("nested-stack", caseB),
            new("same-input-rejected", caseC),
            new("post-confirm-priority", caseD),
            new("modal-background", caseE),
            new("target-disappeared", caseF),
            new("first-visit", caseG),
            new("dynamic-screen", caseH),
            new("structure-mismatch", caseI),
        ];
    }

    private static List<GamepadFocusTarget> CreateReturnFocusHistorySelfTestTargets(string[] labels,
        int groupId, bool lastIsBack = false, bool firstIsBack = false, bool modalForeground = false)
    {
        List<GamepadFocusTarget> targets = [];
        for (int i = 0; i < labels.Length; i++)
        {
            ConsoleStyledString styled = new(labels[i],
                new StringStyle(Config.ForeColor, FontStyle.Regular, null));
            long input = long.TryParse(labels[i], out long numericInput) ? numericInput : i;
            ConsoleButtonString button = new(null, [styled], input);
            Rectangle bounds = new(160, 40 + i * 24, 180, 18);
            GamepadFocusTarget target = new(button, GamepadFocusSourceType.NormalDisplay,
                GamepadFocusLayoutType.Console, groupId, null, bounds, bounds, i)
            {
                NavigationGroupId = 0,
                IsBack = (firstIsBack && i == 0) || (lastIsBack && i == labels.Length - 1),
                IsModalForeground = modalForeground,
            };
            targets.Add(target);
        }
        return targets;
    }

    private static GamepadFocusHistoryEntry CreateReturnFocusHistorySelfTestEntry(
        GamepadFocusTarget target, List<GamepadFocusTarget> targets)
    {
        return new GamepadFocusHistoryEntry
        {
            Fingerprint = CreateGamepadScreenFingerprint(targets),
            Focus = new GamepadScreenTargetSnapshot(target),
            RequestId = 1,
            Order = 1,
        };
    }

    private static GamepadSelfTestCase[] GetGamepadInteractiveTargetSelfTestCases()
    {
        ConsoleButtonString button0 = new(null, [], 0);
        ConsoleButtonString description1 = new(null, []);
        ConsoleButtonString description2 = new(null, []);
        ConsoleButtonString description3 = new(null, []);
        ConsoleButtonString button1 = new(null, [], 1);

        List<GamepadFocusTarget> consoleTargets = [];
        AddInteractiveSelfTestTarget(consoleTargets, button0, GamepadFocusLayoutType.Console, 0);
        if (IsGamepadInteractiveConsoleButton(description1))
            AddInteractiveSelfTestTarget(consoleTargets, description1, GamepadFocusLayoutType.Console, 1);
        if (IsGamepadInteractiveConsoleButton(description2))
            AddInteractiveSelfTestTarget(consoleTargets, description2, GamepadFocusLayoutType.Console, 2);
        if (IsGamepadInteractiveConsoleButton(description3))
            AddInteractiveSelfTestTarget(consoleTargets, description3, GamepadFocusLayoutType.Console, 3);
        AddInteractiveSelfTestTarget(consoleTargets, button1, GamepadFocusLayoutType.Console, 4);

        GamepadNavigationGraph consoleGraph = new();
        consoleGraph.Build(consoleTargets, null);
        bool caseA = consoleTargets.Count == 2
            && consoleTargets[0].Down == consoleTargets[1]
            && consoleTargets[1].Up == consoleTargets[0];
        bool caseB = IsGamepadInteractiveConsoleButton(button0);
        bool caseC = !IsGamepadInteractiveConsoleButton(description1)
            && !IsGamepadInteractiveConsoleButton(description2)
            && !IsGamepadInteractiveConsoleButton(description3);

        ConsoleButtonString htmlButton0 = new(null, [], 0);
        ConsoleButtonString htmlText = new(null, []);
        ConsoleButtonString htmlButton1 = new(null, [], 1);
        List<GamepadFocusTarget> htmlTargets = [];
        AddInteractiveSelfTestTarget(htmlTargets, htmlButton0, GamepadFocusLayoutType.Html, 0);
        if (IsGamepadInteractiveConsoleButton(htmlText))
            AddInteractiveSelfTestTarget(htmlTargets, htmlText, GamepadFocusLayoutType.Html, 1);
        AddInteractiveSelfTestTarget(htmlTargets, htmlButton1, GamepadFocusLayoutType.Html, 2);
        GamepadNavigationGraph htmlGraph = new();
        htmlGraph.Build(htmlTargets, null);
        bool caseD = htmlTargets.Count == 2
            && htmlTargets[0].Down == htmlTargets[1]
            && htmlTargets[1].Up == htmlTargets[0];

        return
        [
            new("console-description-skip", caseA),
            new("input-zero-button", caseB),
            new("plain-text-skip", caseC),
            new("html-text-skip", caseD),
        ];
    }

    private static void AddInteractiveSelfTestTarget(List<GamepadFocusTarget> targets,
        ConsoleButtonString button, GamepadFocusLayoutType layoutType, int row)
    {
        Rectangle bounds = new(120, row * 24, 180, 18);
        targets.Add(new GamepadFocusTarget(button, GamepadFocusSourceType.NormalDisplay,
            layoutType, 0, null, bounds, bounds, targets.Count));
    }

    private static GamepadSelfTestCase[] GetGamepadLogicalButtonSelfTestCases()
    {
        // Each block mirrors a wrapped Console button: one input header plus
        // three description fragments.  The fragments are separate objects,
        // but DivideAt() preserves their input/generation identity.
        List<GamepadFocusTarget> blocks = [];
        ConsoleButtonString firstFragment = null;
        int order = 0;
        for (int input = 0; input < 3; input++)
        {
            for (int fragment = 0; fragment < 4; fragment++)
            {
                int lineNo = 201 + input * 5 + fragment;
                ConsoleButtonString button = new(null, [], input);
                if (firstFragment == null)
                    firstFragment = button;
                blocks.Add(CreateLogicalButtonSelfTestTarget(button, lineNo,
                    120, input * 100 + fragment * 18, order++));
            }
        }

        CollapseGamepadLogicalButtonFragments(blocks, null);
        GamepadNavigationGraph graph = new();
        graph.Build(blocks, null);
        bool blocksCollapsed = blocks.Count == 3;
        bool linksCorrect = blocksCollapsed
            && blocks[0].Down == blocks[1]
            && blocks[1].Up == blocks[0]
            && blocks[1].Down == blocks[2]
            && blocks[2].Up == blocks[1];
        bool representativePreserved = blocksCollapsed
            && blocks[0].Button == firstFragment
            && GetGamepadButtonInput(blocks[0].Button) == "0";

        // The same input in a distant region must remain two targets.  This
        // guards against the forbidden global "same Input = same target" rule.
        List<GamepadFocusTarget> distantSameInput =
        [
            CreateLogicalButtonSelfTestTarget(new(null, [], 5), 300, 120, 0, 0),
            CreateLogicalButtonSelfTestTarget(new(null, [], 5), 330, 500, 400, 1),
        ];
        CollapseGamepadLogicalButtonFragments(distantSameInput, null);
        bool distantPreserved = distantSameInput.Count == 2;

        return
        [
            new("blocks-collapsed", blocksCollapsed),
            new("links", linksCorrect),
            new("representative", representativePreserved),
            new("distant-same-input-preserved", distantPreserved),
        ];
    }

    private static GamepadFocusTarget CreateLogicalButtonSelfTestTarget(
        ConsoleButtonString button, int lineNo, int x, int y, int order)
    {
        ConsoleDisplayLine line = new([button], true, false);
        line.LineNo = lineNo;
        Rectangle bounds = new(x, y, 180, 16);
        return new GamepadFocusTarget(button, GamepadFocusSourceType.NormalDisplay,
            GamepadFocusLayoutType.Console, 0, line, bounds, bounds, order);
    }

    private static GamepadSelfTestCase[] GetGamepadHtmlModalSelfTestCases()
    {
        List<GamepadFocusTarget> modalTargets =
        [
            CreateHtmlModalSelfTestTarget(new(null, [], "キャンセル"), 98, 0, 0, 1000, 600, 10, 0),
            CreateHtmlModalSelfTestTarget(new(null, [], 0), 99, 450, 200, 100, 20, 11, 1),
            CreateHtmlModalSelfTestTarget(new(null, [], 1), 99, 450, 230, 100, 20, 12, 2),
        ];
        for (int i = 0; i < modalTargets.Count; i++)
            modalTargets[i].IsBack = IsGamepadBackButton(modalTargets[i]);
        ApplyGamepadHtmlModalNavigationScope(modalTargets, new Size(1000, 600), null);
        GamepadNavigationGraph modalGraph = new();
        modalGraph.Build(modalTargets, null);
        GamepadFocusTarget initial = FindInitialGamepadFocus(modalTargets);
        bool caseA = initial == modalTargets[1]
            && modalTargets[1].Down == modalTargets[2]
            && modalTargets[2].Up == modalTargets[1]
            && modalGraph.Find(modalTargets[1].Button) == modalTargets[1];
        bool caseB = modalTargets[0].IsModalBackdrop
            && modalTargets[0].IsDirectionalFocusExcluded
            && modalTargets[0].IsBack
            && GetGamepadButtonInput(modalTargets[0].Button) == "キャンセル";

        List<GamepadFocusTarget> largeButtonOnly =
        [
            CreateHtmlModalSelfTestTarget(new(null, [], 7), 10, 0, 0, 1000, 600, 20, 0),
        ];
        for (int i = 0; i < largeButtonOnly.Count; i++)
            largeButtonOnly[i].IsBack = IsGamepadBackButton(largeButtonOnly[i]);
        ApplyGamepadHtmlModalNavigationScope(largeButtonOnly, new Size(1000, 600), null);
        bool caseC = !largeButtonOnly[0].IsDirectionalFocusExcluded;

        List<GamepadFocusTarget> independentIslands =
        [
            CreateHtmlModalSelfTestTarget(new(null, [], 2), 20, 100, 100, 120, 20, 30, 0),
            CreateHtmlModalSelfTestTarget(new(null, [], 3), 21, 400, 100, 120, 20, 31, 1),
        ];
        for (int i = 0; i < independentIslands.Count; i++)
            independentIslands[i].IsBack = IsGamepadBackButton(independentIslands[i]);
        ApplyGamepadHtmlModalNavigationScope(independentIslands, new Size(1000, 600), null);
        bool caseD = independentIslands.All(target =>
            !target.IsDirectionalFocusExcluded && !target.IsModalForeground);

        return
        [
            new("foreground-focus-links", caseA),
            new("modal-cancel-preserved", caseB),
            new("large-button-preserved", caseC),
            new("independent-islands-preserved", caseD),
        ];
    }

    private static GamepadSelfTestCase[] GetGamepadPageNavigationSelfTestCases()
    {
        // A: PAGE.0/PAGE.1 とヒント。選択されたPAGEだけが非デフォルト色になる
        // 実ゲームのSETCOLOR(... aqua ...)構造を、色名を固定せずに再現する。
        List<GamepadFocusTarget> twoPagesAtZero =
        [
            CreateGamepadPageSelfTestTarget("[100] PAGE.0", 100, 0, 0, marked: true),
            CreateGamepadPageSelfTestTarget("[101] PAGE.1", 101, 100, 1),
            CreateGamepadPageSelfTestTarget("[200] ヒント", 200, 200, 2),
        ];
        bool caseA0 = TryFindGamepadPageNavigationSet(twoPagesAtZero, null, out GamepadPageNavigationSet setAtZero,
            out _) && setAtZero.CurrentPageIndex == 0
            && FindGamepadAdjacentPageTarget(setAtZero, true, out int nextPageAtZero) == twoPagesAtZero[1]
            && nextPageAtZero == 1;

        List<GamepadFocusTarget> twoPagesAtOne =
        [
            CreateGamepadPageSelfTestTarget("[100] PAGE.0", 100, 0, 0),
            CreateGamepadPageSelfTestTarget("[101] PAGE.1", 101, 100, 1, marked: true),
            CreateGamepadPageSelfTestTarget("[200] ヒント", 200, 200, 2),
        ];
        bool caseA1 = TryFindGamepadPageNavigationSet(twoPagesAtOne, null, out GamepadPageNavigationSet setAtOne,
            out _) && setAtOne.CurrentPageIndex == 1
            && FindGamepadAdjacentPageTarget(setAtOne, false, out int previousPageAtOne) == twoPagesAtOne[0]
            && previousPageAtOne == 0;

        // B: 3ページ以上ではソート済みの隣接ページだけを選択する。
        List<GamepadFocusTarget> threePages =
        [
            CreateGamepadPageSelfTestTarget("PAGE.0", 10, 0, 0),
            CreateGamepadPageSelfTestTarget("PAGE 1", 11, 100, 1, marked: true),
            CreateGamepadPageSelfTestTarget("PAGE.2", 12, 200, 2),
        ];
        bool caseB = TryFindGamepadPageNavigationSet(threePages, null, out GamepadPageNavigationSet threePageSet,
            out _) && threePageSet.CurrentPageIndex == 1
            && FindGamepadAdjacentPageTarget(threePageSet, false, out int previousPage) == threePages[0]
            && previousPage == 0
            && FindGamepadAdjacentPageTarget(threePageSet, true, out int nextPage) == threePages[2]
            && nextPage == 2;

        // C: 端はページ実行を返さず、呼び出し元にもログスクロールへ渡さない。
        bool caseC = caseA0
            && FindGamepadAdjacentPageTarget(setAtZero, false, out _) == null
            && TryFindGamepadPageNavigationSet(
                [
                    CreateGamepadPageSelfTestTarget("PAGE.0", 10, 0, 0),
                    CreateGamepadPageSelfTestTarget("PAGE.1", 11, 100, 1),
                    CreateGamepadPageSelfTestTarget("PAGE.2", 12, 200, 2, marked: true),
                ], null, out GamepadPageNavigationSet setAtLastPage, out _)
            && FindGamepadAdjacentPageTarget(setAtLastPage, true, out _) == null;

        // D/E: Indexed型では、説明文や単発ボタンを有効化しない。
        bool caseD = !TryFindGamepadPageNavigationSet(
            [CreateGamepadPageSelfTestTarget("[1] メニュー", 1, 0, 0)], null, out _, out _);
        bool caseE = !TryFindGamepadPageNavigationSet(
            [
                CreateGamepadPageSelfTestTarget("[1] PAGE HELP", 1, 0, 0),
                CreateGamepadPageSelfTestTarget("[2] PAGEについて", 2, 100, 1, marked: true),
            ], null, out _, out _);

        // Directional A/D: ページ端では片方向しかなくても有効。戻るはPreviousにしない。
        List<GamepadFocusTarget> directionalFirstPage =
        [
            CreateGamepadPageSelfTestTarget("[1000] 戻る", 1000, 0, 0),
            CreateGamepadPageSelfTestTarget("[1009] 次のページ", 1009, 100, 1),
        ];
        bool directionalA = TryFindGamepadPageNavigationSet(directionalFirstPage, null,
                out GamepadPageNavigationSet directionalFirstSet, out _)
            && directionalFirstSet.Mode == GamepadPageNavigationMode.Directional
            && directionalFirstSet.PreviousTarget == null
            && directionalFirstSet.NextTarget == directionalFirstPage[1];

        // Directional B: 前後が同時に見える中間ページ。
        List<GamepadFocusTarget> directionalMiddlePage =
        [
            CreateGamepadPageSelfTestTarget("[1000] 前のページ", 1000, 0, 0),
            CreateGamepadPageSelfTestTarget("[1001] 戻る", 1001, 100, 1),
            CreateGamepadPageSelfTestTarget("[1009] 次のページ", 1009, 200, 2),
        ];
        bool directionalB = TryFindGamepadPageNavigationSet(directionalMiddlePage, null,
                out GamepadPageNavigationSet directionalMiddleSet, out _)
            && directionalMiddleSet.Mode == GamepadPageNavigationMode.Directional
            && directionalMiddleSet.PreviousTarget == directionalMiddlePage[0]
            && directionalMiddleSet.NextTarget == directionalMiddlePage[2];

        // Directional C: 最終ページではPreviousだけが存在する。
        List<GamepadFocusTarget> directionalLastPage =
        [
            CreateGamepadPageSelfTestTarget("[1000] 前のページ", 1000, 0, 0),
            CreateGamepadPageSelfTestTarget("[1001] 戻る", 1001, 100, 1),
        ];
        bool directionalC = TryFindGamepadPageNavigationSet(directionalLastPage, null,
                out GamepadPageNavigationSet directionalLastSet, out _)
            && directionalLastSet.Mode == GamepadPageNavigationMode.Directional
            && directionalLastSet.PreviousTarget == directionalLastPage[0]
            && directionalLastSet.NextTarget == null;

        // Directional E: 通常画面の「次へ」や「戻る」だけはページUIにしない。
        bool directionalE = !TryFindGamepadPageNavigationSet(
            [
                CreateGamepadPageSelfTestTarget("[1] 次へ", 1, 0, 0),
                CreateGamepadPageSelfTestTarget("[2] 戻る", 2, 100, 1),
            ], null, out _, out _);

        // F: modal foregroundがある時、背後のPAGE群は探索対象から除外する。
        List<GamepadFocusTarget> modalTargets =
        [
            CreateGamepadPageSelfTestTarget("PAGE.0", 100, 0, 0, marked: true),
            CreateGamepadPageSelfTestTarget("PAGE.1", 101, 100, 1),
            CreateGamepadPageSelfTestTarget("Yes", 1, 400, 2, modalForeground: true, groupId: 99),
            CreateGamepadPageSelfTestTarget("No", 2, 500, 3, modalForeground: true, groupId: 99),
        ];
        bool caseF = !TryFindGamepadPageNavigationSet(modalTargets, modalTargets[2], out _, out _);

        // Directional H: modalの背後にあるDirectional Targetも探索対象外にする。
        List<GamepadFocusTarget> modalDirectionalTargets =
        [
            CreateGamepadPageSelfTestTarget("前のページ", 1000, 0, 0),
            CreateGamepadPageSelfTestTarget("次のページ", 1009, 100, 1),
            CreateGamepadPageSelfTestTarget("Yes", 1, 400, 2, modalForeground: true, groupId: 99),
            CreateGamepadPageSelfTestTarget("No", 2, 500, 3, modalForeground: true, groupId: 99),
        ];
        bool directionalH = !TryFindGamepadPageNavigationSet(modalDirectionalTargets,
            modalDirectionalTargets[2], out _, out _);

        return
        [
            new("indexed-first-page", caseA0),
            new("indexed-second-page", caseA1),
            new("indexed-three-pages", caseB),
            new("indexed-boundaries", caseC),
            new("indexed-no-pages", caseD),
            new("indexed-page-word-only", caseE),
            new("indexed-modal-background-excluded", caseF),
            new("directional-first-page", directionalA),
            new("directional-middle-page", directionalB),
            new("directional-last-page", directionalC),
            new("directional-ambiguous-labels", directionalE),
            new("directional-modal-background-excluded", directionalH),
        ];
    }

    private static GamepadFocusTarget CreateGamepadPageSelfTestTarget(string text, long input, int x,
        int order, bool marked = false, bool modalForeground = false, int groupId = 0)
    {
        Color markerColor = Config.ForeColor == Color.Aqua ? Color.Fuchsia : Color.Aqua;
        ConsoleStyledString styled = new(text,
            new StringStyle(marked ? markerColor : Config.ForeColor, FontStyle.Regular, null));
        ConsoleButtonString button = new(null, [styled], input);
        ConsoleDisplayLine line = new([button], true, false);
        line.LineNo = 600 + order;
        Rectangle bounds = new(x, 400, 90, 18);
        GamepadFocusTarget target = new(button, GamepadFocusSourceType.NormalDisplay,
            GamepadFocusLayoutType.Console, groupId, line, bounds, bounds, order)
        {
            NavigationGroupId = 0,
            IsModalForeground = modalForeground,
        };
        return target;
    }

    private static GamepadFocusTarget CreateHtmlModalSelfTestTarget(
        ConsoleButtonString button, int groupId, int x, int y, int width, int height,
        int lineNo, int order)
    {
        ConsoleDisplayLine line = new([button], true, false);
        line.LineNo = lineNo;
        Rectangle bounds = new(x, y, width, height);
        return new GamepadFocusTarget(button, GamepadFocusSourceType.HtmlIsland,
            GamepadFocusLayoutType.Html, groupId, line, bounds, bounds, order);
    }

    private static string SanitizeGamepadText(string text)
    {
        return (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
    }

    private static string GetGamepadButtonInput(ConsoleButtonString button)
    {
        if (button == null)
            return string.Empty;
        return button.IsInteger ? button.Input.ToString() : button.Inputs ?? string.Empty;
    }

    private static long DistanceSquared(GamepadFocusTarget target, Rectangle bounds)
    {
        long dx = target.CenterX - (bounds.Left + bounds.Width / 2);
        long dy = target.CenterY - (bounds.Top + bounds.Height / 2);
        return dx * dx + dy * dy;
    }

    private static GamepadFocusTarget FindGamepadVerticalLaneTarget(List<GamepadFocusTarget> targets,
        Rectangle anchorBounds, int laneTolerance, Func<GamepadFocusTarget, bool> isCandidate,
        bool includeAnchorRowInBelow)
    {
        int anchorCenterX = anchorBounds.Left + anchorBounds.Width / 2;
        int anchorCenterY = anchorBounds.Top + anchorBounds.Height / 2;
        GamepadFocusTarget below = null;
        GamepadFocusTarget above = null;
        long belowDistance = long.MaxValue;
        long aboveDistance = long.MaxValue;
        for (int i = 0; i < targets.Count; i++)
        {
            GamepadFocusTarget target = targets[i];
            if (!isCandidate(target) || Math.Abs(target.CenterX - anchorCenterX) > laneTolerance)
                continue;
            long distance = Math.Abs((long)target.CenterY - anchorCenterY);
            if ((includeAnchorRowInBelow ? target.CenterY >= anchorCenterY : target.CenterY > anchorCenterY)
                && (below == null || distance < belowDistance
                    || (distance == belowDistance && target.Order < below.Order)))
            {
                below = target;
                belowDistance = distance;
            }
            else if (target.CenterY < anchorCenterY
                && (above == null || distance < aboveDistance
                    || (distance == aboveDistance && target.Order < above.Order)))
            {
                above = target;
                aboveDistance = distance;
            }
        }
        return below ?? above;
    }

    private static GamepadFocusTarget FindGamepadNearestTarget(List<GamepadFocusTarget> targets,
        Rectangle anchorBounds, Func<GamepadFocusTarget, bool> isCandidate,
        int? preferredNavigationGroupId = null)
    {
        GamepadFocusTarget best = null;
        int bestNavigationPriority = int.MaxValue;
        long bestDistance = long.MaxValue;
        for (int i = 0; i < targets.Count; i++)
        {
            GamepadFocusTarget target = targets[i];
            if (!isCandidate(target))
                continue;
            int navigationPriority = preferredNavigationGroupId.HasValue
                && target.NavigationGroupId != preferredNavigationGroupId.Value ? 1 : 0;
            long distance = DistanceSquared(target, anchorBounds);
            if (best == null || navigationPriority < bestNavigationPriority
                || (navigationPriority == bestNavigationPriority && distance < bestDistance)
                || (navigationPriority == bestNavigationPriority && distance == bestDistance
                    && target.Order < best.Order))
            {
                best = target;
                bestNavigationPriority = navigationPriority;
                bestDistance = distance;
            }
        }
        return best;
    }

    private void RememberGamepadFocus(GamepadFocusTarget target)
    {
        if (target == null || inputReq == null)
            return;
        hasGamepadFocusHistory = true;
        lastGamepadFocusInputKey = GetGamepadInputKey(target.Button);
        lastGamepadFocusRequestId = inputReq.ID;
        lastGamepadFocusBounds = target.Bounds;
        lastGamepadFocusSourceType = target.SourceType;
        lastGamepadFocusGroupId = target.GroupId;
        lastGamepadFocusNavigationGroupId = target.NavigationGroupId;
    }

    private void CaptureGamepadReturnFocus(GamepadFocusTarget target,
        GamepadFocusTransitionKind transitionKind, string action)
    {
        if (target == null || inputReq == null || gamepadFocusTargets.Count == 0)
            return;

        GamepadFocusHistoryEntry entry = CreateGamepadFocusHistoryEntry(target, transitionKind);
        if (entry == null || entry.Fingerprint.Targets.Count == 0)
            return;

        pendingGamepadFocusTransition = entry;
        StoreGamepadLastFocusEntry(entry);
        WriteGamepadNavigationDiagnostic(
            $"Leaving screen: fingerprint={entry.Fingerprint.DebugId} focusInput={GetGamepadButtonInput(target.Button)} "
            + $"rect={FormatGamepadRectangle(target.Bounds)} action={action}");
        if (transitionKind == GamepadFocusTransitionKind.Back)
            WriteGamepadNavigationDiagnostic("Back/Cancel transition detected");
    }

    private GamepadFocusHistoryEntry CreateGamepadFocusHistoryEntry(GamepadFocusTarget target,
        GamepadFocusTransitionKind transitionKind)
    {
        if (target?.Button == null || inputReq == null)
            return null;

        return new GamepadFocusHistoryEntry
        {
            Fingerprint = CreateGamepadScreenFingerprint(gamepadFocusTargets),
            Focus = new GamepadScreenTargetSnapshot(target),
            RequestId = inputReq.ID,
            Order = ++gamepadFocusHistoryOrder,
            TransitionKind = transitionKind,
        };
    }

    private void ResolvePendingGamepadFocusTransition(GamepadScreenFingerprint currentScreen)
    {
        GamepadFocusHistoryEntry pending = pendingGamepadFocusTransition;
        if (pending == null || currentScreen == null || currentScreen.Targets.Count == 0)
            return;

        pendingGamepadFocusTransition = null;
        bool sameScreen = TryMatchGamepadScreenFingerprints(pending.Fingerprint, currentScreen,
            out int similarity, out _);
        if (sameScreen)
        {
            WriteGamepadNavigationDiagnostic(
                $"Focus history transition ignored: reason=same-screen redraw fingerprint={currentScreen.DebugId} "
                + $"similarity={similarity}%");
            return;
        }

        if (pending.TransitionKind == GamepadFocusTransitionKind.Back)
        {
            if (gamepadReturnFocusStack.Count > 0)
            {
                GamepadFocusHistoryEntry returnEntry = gamepadReturnFocusStack[^1];
                if (TryMatchGamepadScreenFingerprints(returnEntry.Fingerprint, currentScreen,
                        out int returnSimilarity, out _))
                {
                    gamepadReturnFocusStack.RemoveAt(gamepadReturnFocusStack.Count - 1);
                    pendingReturnFocusRestore = returnEntry;
                    WriteGamepadNavigationDiagnostic(
                        $"Screen history match: fingerprint={currentScreen.DebugId} similarity={returnSimilarity}% "
                        + "returnEntry=True");
                    return;
                }
            }

            WriteGamepadNavigationDiagnostic(
                $"Focus history rejected: reason=return-stack structure mismatch fingerprint={currentScreen.DebugId}");
            return;
        }

        PushGamepadReturnFocusEntry(pending);
    }

    private void PushGamepadReturnFocusEntry(GamepadFocusHistoryEntry entry)
    {
        if (entry == null)
            return;
        gamepadReturnFocusStack.Add(entry);
        while (gamepadReturnFocusStack.Count > GamepadReturnFocusStackCapacity)
            gamepadReturnFocusStack.RemoveAt(0);
        WriteGamepadNavigationDiagnostic(
            $"Return history push: depth={gamepadReturnFocusStack.Count} screen={entry.Fingerprint.DebugId} "
            + $"focusInput={GetGamepadButtonInputKeyForDiagnostic(entry.Focus.InputKey)}");
    }

    private void StoreGamepadLastFocusEntry(GamepadFocusHistoryEntry entry)
    {
        if (entry?.Fingerprint == null || entry.Fingerprint.Targets.Count == 0)
            return;

        for (int i = gamepadLastFocusCache.Count - 1; i >= 0; i--)
        {
            if (!TryMatchGamepadScreenFingerprints(gamepadLastFocusCache[i].Fingerprint,
                    entry.Fingerprint, out _, out _))
            {
                continue;
            }
            gamepadLastFocusCache.RemoveAt(i);
            break;
        }
        gamepadLastFocusCache.Add(entry);
        while (gamepadLastFocusCache.Count > GamepadLastFocusCacheCapacity)
            gamepadLastFocusCache.RemoveAt(0);
    }

    private bool TryFindGamepadLastFocusHistory(GamepadScreenFingerprint currentScreen,
        out GamepadFocusHistoryEntry entry, out int similarity)
    {
        entry = null;
        similarity = 0;
        for (int i = gamepadLastFocusCache.Count - 1; i >= 0; i--)
        {
            GamepadFocusHistoryEntry candidate = gamepadLastFocusCache[i];
            if (!TryMatchGamepadScreenFingerprints(candidate.Fingerprint, currentScreen,
                    out int candidateSimilarity, out _))
            {
                continue;
            }
            if (entry == null || candidateSimilarity > similarity
                || (candidateSimilarity == similarity && candidate.Order > entry.Order))
            {
                entry = candidate;
                similarity = candidateSimilarity;
            }
        }
        return entry != null;
    }

    private static GamepadScreenFingerprint CreateGamepadScreenFingerprint(
        List<GamepadFocusTarget> targets)
    {
        GamepadScreenFingerprint fingerprint = new();
        List<GamepadFocusTarget> scope = GetGamepadFocusHistoryScope(targets, out bool hasModalScope);
        fingerprint.HasModalScope = hasModalScope;
        scope.Sort(CompareGamepadVisualOrder);
        fingerprint.Targets = CreateGamepadFocusSnapshots(scope);
        fingerprint.DebugId = CreateGamepadScreenFingerprintDebugId(fingerprint);
        return fingerprint;
    }

    private static List<GamepadScreenTargetSnapshot> CreateGamepadFocusSnapshots(
        List<GamepadFocusTarget> targets)
    {
        List<GamepadScreenTargetSnapshot> snapshots = [];
        for (int i = 0; i < targets.Count; i++)
            snapshots.Add(new GamepadScreenTargetSnapshot(targets[i]));
        return snapshots;
    }

    private static List<GamepadFocusTarget> GetGamepadFocusHistoryScope(
        List<GamepadFocusTarget> targets, out bool hasModalScope)
    {
        hasModalScope = targets != null && targets.Any(target => target.IsModalForeground);
        List<GamepadFocusTarget> scope = [];
        if (targets == null)
            return scope;
        for (int i = 0; i < targets.Count; i++)
        {
            GamepadFocusTarget target = targets[i];
            if (target?.Button == null || !target.Enabled)
                continue;
            if (hasModalScope ? !target.IsModalForeground : target.IsDirectionalFocusExcluded)
                continue;
            scope.Add(target);
        }
        return scope;
    }

    private static string CreateGamepadScreenFingerprintDebugId(GamepadScreenFingerprint fingerprint)
    {
        uint hash = 2166136261;
        for (int i = 0; i < fingerprint.Targets.Count; i++)
        {
            GamepadScreenTargetSnapshot target = fingerprint.Targets[i];
            hash = AddGamepadFingerprintHash(hash, target.InputKey);
            hash = AddGamepadFingerprintHash(hash, ((int)target.SourceType).ToString());
            hash = AddGamepadFingerprintHash(hash, ((int)target.LayoutType).ToString());
            hash = AddGamepadFingerprintHash(hash, target.GroupId.ToString());
            hash = AddGamepadFingerprintHash(hash, target.NavigationGroupId.ToString());
            hash = AddGamepadFingerprintHash(hash, target.IsBack ? "B" : "N");
        }
        return $"m={(fingerprint.HasModalScope ? 1 : 0)}:n={fingerprint.Targets.Count}:h={hash:X8}";
    }

    private static uint AddGamepadFingerprintHash(uint hash, string value)
    {
        string text = value ?? string.Empty;
        for (int i = 0; i < text.Length; i++)
        {
            hash ^= text[i];
            hash *= 16777619;
        }
        return hash;
    }

    private static bool TryMatchGamepadScreenFingerprints(GamepadScreenFingerprint previous,
        GamepadScreenFingerprint current, out int similarity, out int matchCount)
    {
        similarity = 0;
        matchCount = 0;
        if (previous == null || current == null || previous.Targets.Count == 0 || current.Targets.Count == 0
            || previous.HasModalScope != current.HasModalScope)
        {
            return false;
        }

        int previousCount = previous.Targets.Count;
        int currentCount = current.Targets.Count;
        int minCount = Math.Min(previousCount, currentCount);
        int maxCount = Math.Max(previousCount, currentCount);
        if (Math.Abs(previousCount - currentCount) > Math.Max(2, maxCount * 2 / 5))
            return false;

        bool[] used = new bool[currentCount];
        int labelMatches = 0;
        int geometryMatches = 0;
        for (int oldIndex = 0; oldIndex < previousCount; oldIndex++)
        {
            GamepadScreenTargetSnapshot oldTarget = previous.Targets[oldIndex];
            for (int newIndex = 0; newIndex < currentCount; newIndex++)
            {
                GamepadScreenTargetSnapshot newTarget = current.Targets[newIndex];
                if (used[newIndex] || !IsGamepadScreenStructureTargetMatch(oldTarget, newTarget))
                    continue;
                used[newIndex] = true;
                matchCount++;
                if (string.Equals(oldTarget.NormalizedLabel, newTarget.NormalizedLabel,
                        StringComparison.OrdinalIgnoreCase))
                {
                    labelMatches++;
                }
                if (AreGamepadFingerprintBoundsCompatible(oldTarget.Bounds, newTarget.Bounds))
                    geometryMatches++;
                break;
            }
        }

        similarity = matchCount * 100 / minCount;
        // 表示条件で1項目程度が増減しても同一論理画面を見失わないよう、3項目
        // 以上では概ね2/3以上の構造一致を要求する。1～2項目画面は下の補助一致を
        // 必須にして、同じinputだけの誤一致を防ぐ。
        int requiredMatches = minCount <= 2 ? minCount : Math.Max(2, (minCount * 2 + 2) / 3);
        if (matchCount < requiredMatches)
            return false;

        // 1～2ボタン画面はInputだけの偶然一致を特に避ける。3ボタン以上は
        // Input/Source/Groupの高い一致率に加え、ラベルまたは配置の補助一致を求める。
        if (minCount <= 2)
            return labelMatches > 0 || geometryMatches > 0;
        return labelMatches > 0 || geometryMatches >= Math.Min(2, matchCount);
    }

    private static bool IsGamepadScreenStructureTargetMatch(GamepadScreenTargetSnapshot left,
        GamepadScreenTargetSnapshot right)
    {
        return string.Equals(left.InputKey, right.InputKey, StringComparison.Ordinal)
            && left.SourceType == right.SourceType
            && left.LayoutType == right.LayoutType
            && left.GroupId == right.GroupId
            && left.NavigationGroupId == right.NavigationGroupId
            && left.IsBack == right.IsBack;
    }

    private static bool AreGamepadFingerprintBoundsCompatible(Rectangle left, Rectangle right)
    {
        int horizontalTolerance = Math.Max(24, Math.Max(left.Width, right.Width) / 2);
        int verticalTolerance = Math.Max(72, Math.Max(left.Height, right.Height) * 4);
        return Math.Abs(left.Left - right.Left) <= horizontalTolerance
            && Math.Abs(left.Top - right.Top) <= verticalTolerance
            && Math.Abs(left.Width - right.Width) <= horizontalTolerance
            && Math.Abs(left.Height - right.Height) <= Math.Max(12, Math.Max(left.Height, right.Height));
    }

    private static string GetGamepadButtonInputKeyForDiagnostic(string inputKey)
    {
        if (string.IsNullOrEmpty(inputKey) || inputKey.Length < 3)
            return inputKey ?? string.Empty;
        return inputKey[2..];
    }

    private static bool TryRestoreGamepadFocusHistory(GamepadFocusHistoryEntry entry,
        List<GamepadFocusTarget> targets, out GamepadFocusTarget restored, out string reason)
    {
        restored = null;
        reason = string.Empty;
        if (entry == null)
            return false;

        List<GamepadFocusTarget> scope = GetGamepadFocusHistoryScope(targets, out _);
        restored = FindGamepadHistoryInputTarget(entry, scope);
        if (restored != null)
        {
            reason = "same input/source/baseGroup and near bounds";
            return true;
        }

        restored = FindGamepadHistoryLabelTarget(entry, scope);
        if (restored != null)
        {
            reason = "same normalized label/source/baseGroup and near bounds";
            return true;
        }

        restored = FindGamepadHistoryLaneFallback(entry, scope);
        if (restored != null)
        {
            reason = "history target disappeared; nearest same vertical lane";
            return true;
        }

        restored = FindGamepadHistoryNearestTarget(entry, scope);
        if (restored != null)
        {
            reason = "history target disappeared; nearest target in the same panel";
            return true;
        }
        return false;
    }

    private static GamepadFocusTarget FindGamepadHistoryInputTarget(GamepadFocusHistoryEntry entry,
        List<GamepadFocusTarget> targets)
    {
        return FindGamepadNearestTarget(targets, entry.Focus.Bounds, target =>
            IsGamepadHistoryPanelMatch(entry, target)
                && string.Equals(entry.Focus.InputKey, GetGamepadInputKey(target.Button),
                    StringComparison.Ordinal)
                && AreGamepadFingerprintBoundsCompatible(entry.Focus.Bounds, target.Bounds),
            entry.Focus.NavigationGroupId);
    }

    private static GamepadFocusTarget FindGamepadHistoryLabelTarget(GamepadFocusHistoryEntry entry,
        List<GamepadFocusTarget> targets)
    {
        if (string.IsNullOrEmpty(entry.Focus.NormalizedLabel))
            return null;

        return FindGamepadNearestTarget(targets, entry.Focus.Bounds, target =>
            IsGamepadHistoryPanelMatch(entry, target)
                && string.Equals(entry.Focus.NormalizedLabel,
                    NormalizeGamepadSemanticText(target.Button.ToString()), StringComparison.OrdinalIgnoreCase)
                && AreGamepadFingerprintBoundsCompatible(entry.Focus.Bounds, target.Bounds),
            entry.Focus.NavigationGroupId);
    }

    private static GamepadFocusTarget FindGamepadHistoryLaneFallback(GamepadFocusHistoryEntry entry,
        List<GamepadFocusTarget> targets)
    {
        return FindGamepadVerticalLaneTarget(targets, entry.Focus.Bounds,
            Math.Max(16, entry.Focus.Bounds.Width / 2),
            target => IsGamepadHistoryPanelMatch(entry, target), true);
    }

    private static GamepadFocusTarget FindGamepadHistoryNearestTarget(GamepadFocusHistoryEntry entry,
        List<GamepadFocusTarget> targets)
    {
        return FindGamepadNearestTarget(targets, entry.Focus.Bounds,
            target => IsGamepadHistoryPanelMatch(entry, target), entry.Focus.NavigationGroupId);
    }

    private static bool IsGamepadHistoryPanelMatch(GamepadFocusHistoryEntry entry,
        GamepadFocusTarget target)
    {
        return target != null && !target.IsDirectionalFocusExcluded && target.Enabled
            && target.SourceType == entry.Focus.SourceType
            && target.LayoutType == entry.Focus.LayoutType
            && target.GroupId == entry.Focus.GroupId
            && target.IsBack == entry.Focus.IsBack;
    }

    private static string GetGamepadInputKey(ConsoleButtonString button)
    {
        return button.IsInteger ? "I:" + button.Input : "S:" + (button.Inputs ?? string.Empty);
    }

    private void LogGamepadFocusTargets()
    {
        if (!Program.GamepadDebugMode)
            return;

        int geometryHash = GetGamepadFocusGeometryHash();
        if (gamepadFocusLoggedGeneration == gamepadFocusTargetGeneration
            && gamepadFocusLoggedRequestId == gamepadFocusTargetRequestId
            && gamepadFocusLoggedGeometryHash == geometryHash)
            return;

        gamepadFocusLoggedGeneration = gamepadFocusTargetGeneration;
        gamepadFocusLoggedRequestId = gamepadFocusTargetRequestId;
        gamepadFocusLoggedGeometryHash = geometryHash;
        WriteGamepadNavigationDiagnostic($"Gamepad focus targets: generation={gamepadFocusTargetGeneration}, request={gamepadFocusTargetRequestId}, count={gamepadFocusTargets.Count}");
        for (int i = 0; i < gamepadFocusTargets.Count; i++)
        {
            GamepadFocusTarget target = gamepadFocusTargets[i];
            string text = SanitizeGamepadText(target.Button.ToString());
            WriteGamepadNavigationDiagnostic(
                $"{i}: input={GetGamepadButtonInput(target.Button)}, text={text}, source={target.SourceName}, layout={target.LayoutType}, baseGroup={target.GroupId}, group={target.NavigationGroupId}, line={target.LineNo}, rawPoint={FormatGamepadRawRectangle(target.RawBounds)}, rect={FormatGamepadRectangle(target.Bounds)}, row={target.Row}, column={target.Column}, enabled={target.Enabled}, back={target.IsBack}, directionalExcluded={target.IsDirectionalFocusExcluded}, modalForeground={target.IsModalForeground}, modalBackdrop={target.IsModalBackdrop}, links=[U:{DescribeGamepadLink(target.Up)},D:{DescribeGamepadLink(target.Down)},L:{DescribeGamepadLink(target.Left)},R:{DescribeGamepadLink(target.Right)}]");
        }
    }

    private int GetGamepadFocusGeometryHash()
    {
        HashCode hash = new();
        for (int i = 0; i < gamepadFocusTargets.Count; i++)
        {
            GamepadFocusTarget target = gamepadFocusTargets[i];
            hash.Add(target.SourceType);
            hash.Add(target.GroupId);
            hash.Add(target.LineNo);
            hash.Add(target.Bounds);
            hash.Add(target.RawBounds);
        }
        return hash.ToHashCode();
    }

    private void LogGamepadMove(GamepadDirection direction, GamepadFocusTarget from, GamepadFocusTarget to)
    {
        if (!Program.GamepadDebugMode)
            return;
        WriteGamepadNavigationDiagnostic(
            $"Move {direction}: from input={GetGamepadButtonInput(from.Button)} line={from.LineNo} source={from.SourceName} islandId={from.GroupId} rect={FormatGamepadRectangle(from.Bounds)} to input={GetGamepadButtonInput(to.Button)} line={to.LineNo} source={to.SourceName} islandId={to.GroupId} rect={FormatGamepadRectangle(to.Bounds)}");
    }

    private void LogGamepadConfirm(GamepadFocusTarget target, string action)
    {
        if (!Program.GamepadDebugMode)
            return;
        WriteGamepadNavigationDiagnostic(
            $"Confirm: input={GetGamepadButtonInput(target.Button)} text={SanitizeGamepadText(target.Button.ToString())} request={inputReq?.ID ?? -1} source={target.SourceName} islandId={target.GroupId} line={target.LineNo} row={target.Row} column={target.Column} action={action}");
    }

    private static string DescribeGamepadLink(GamepadFocusTarget target)
    {
        return target == null ? "-" : $"{GetGamepadButtonInput(target.Button)}@{target.Row},{target.Column}";
    }

    private static string FormatGamepadRectangle(Rectangle bounds)
    {
        return $"({bounds.X},{bounds.Y},{bounds.Width},{bounds.Height})";
    }

    private static string FormatGamepadRawRectangle(RectangleF bounds)
    {
        return $"({bounds.X:0.##},{bounds.Y:0.##},{bounds.Width:0.##},{bounds.Height:0.##})";
    }

    private static void WriteGamepadNavigationDiagnostic(string message)
    {
        if (!Program.GamepadDebugMode)
            return;
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "gamepad-debug.log");
            File.AppendAllText(path, $"{DateTime.Now:O} {message}{Environment.NewLine}");
        }
        catch
        {
            // 診断ログの失敗はゲーム入力を止めない。
        }
    }

    private bool IsGamepadLineVisible(ConsoleDisplayLine line)
    {
        if (line == null || line.LineNo < 0)
            return true;
        int bottom = window.ScrollBar.Value - 1;
        int top = bottom - (window.MainPicBox.Height / Config.LineHeight + 1);
        if (top < 0)
            top = 0;
        return line.LineNo >= top && line.LineNo <= bottom;
    }

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
        gamepadFocusTargetsDirty = true;
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
                Color c = cbgButtonMap.Bitmap.GetPixel(mapPoint.X, mapPoint.Y).ToDrawingColor();
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

    /// <summary>
    /// ゲームパッドの仮想フォーカスを、実マウスカーソルを動かさずに
    /// INPUTMOUSEKEYの左クリックとしてERBへ渡す。
    /// </summary>
    private void InputMouseKeyFromGamepad(GamepadFocusTarget target)
    {
        if (!IsWaitingPrimitive || target == null || !CanSelectGamepadButton(target.Button))
            return;

        selectingButton = target.Button;
        Point center = new(target.CenterX, target.CenterY);

        process.SetResultArray(0, 5);
        process.SetResultsArray("", 5);
        if (target.Button.IsInteger)
            process.SetResultArray(target.Button.Input, 5);
        else
            process.SetResultsArray(target.Button.Inputs, 5);

        // MouseDown()と同じRESULT座標系（Yは画面下端基準）を使用する。
        int resultY = center.Y - ClientHeight;
        process.InputResult5(1, (int)MouseButtons.Left, center.X, resultY, -1);
        WriteGamepadNavigationDiagnostic(
            $"Gamepad virtual left click: input={GetGamepadButtonInput(target.Button)} source={target.SourceName} islandId={target.GroupId} line={target.LineNo} center=({center.X},{center.Y})");

        inProcess = true;
        try
        {
            RunEmueraProgram(null);
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

    public void SetEmueraVersionInfo(string str)
    {
        window.TextBox.Text = str;
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
        // 選択ハイライトだけの再描画では座標グラフを作り直さない。
        // 表示行が増減した場合と、新しい入力要求／ボタン世代では別途再構築される。
        if (lastDrawnLineNo != lineNo)
            gamepadFocusTargetsDirty = true;
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
                Color c = cbgButtonMap.Bitmap.GetPixel(mapPoint.X, mapPoint.Y).ToDrawingColor();
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
        if (genericTimer != null)
            genericTimer.Dispose();
        //timer = null;
        //stringMeasure.Dispose();
    }


}
