using MinorShift.Emuera.GameView;
using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Runtime.Script;
using MinorShift.Emuera.Runtime.Utils;
using MinorShift.Emuera.UI;
using MinorShift.Emuera.UI.Framework.Forms;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime;
using System.Threading.Tasks;
using System.Windows.Forms;
using MinorShift.Emuera.UI.Framework;
using MinorShift.Emuera.Runtime.Config.JSON;
using MinorShift.Emuera.GameProc.Function;

#nullable enable

namespace MinorShift.Emuera.Forms;

internal sealed partial class MainWindow : Form
{
    public MainWindow(string[] args)
    {
        InitializeComponent();
        this._args = args;

        if (Program.DebugMode)
        {
            デバッグモードで再起動ToolStripMenuItem.Visible = false;
            デバッグToolStripMenuItem.Visible = true;
        }


        initControlSizeAndLocation();
        richTextBox1.ForeColor = Config.ForeColor;
        richTextBox1.BackColor = Config.BackColor;
        mainPicBox.BackColor = Config.BackColor;//これは実際には使用されないはず

        BackColor = Config.BackColor;

        //richTextBox1.Font = Config.DefaultFont;
        richTextBox1.LanguageOption = RichTextBoxLanguageOptions.UIFonts;
        folderSelectDialog.SelectedPath = Program.ErbDir;
        folderSelectDialog.ShowNewFolderButton = false;

        openFileDialog.InitialDirectory = Program.ErbDir;
        openFileDialog.Filter = "ERBファイル (*.erb)|*.erb";
        openFileDialog.FileName = "";
        openFileDialog.Multiselect = true;
        openFileDialog.RestoreDirectory = true;

        string Emuera_verInfo = AssemblyData.EmueraVersionText;
        EmuVerToolStripTextBox.Text = Emuera_verInfo;

        console = new EmueraConsole(this);
        macroMenuItems[0] = マクロ01ToolStripMenuItem;
        macroMenuItems[1] = マクロ02ToolStripMenuItem;
        macroMenuItems[2] = マクロ03ToolStripMenuItem;
        macroMenuItems[3] = マクロ04ToolStripMenuItem;
        macroMenuItems[4] = マクロ05ToolStripMenuItem;
        macroMenuItems[5] = マクロ06ToolStripMenuItem;
        macroMenuItems[6] = マクロ07ToolStripMenuItem;
        macroMenuItems[7] = マクロ08ToolStripMenuItem;
        macroMenuItems[8] = マクロ09ToolStripMenuItem;
        macroMenuItems[9] = マクロ10ToolStripMenuItem;
        macroMenuItems[10] = マクロ11ToolStripMenuItem;
        macroMenuItems[11] = マクロ12ToolStripMenuItem;
        foreach (ToolStripMenuItem item in macroMenuItems)
            item.Click += new EventHandler(マクロToolStripMenuItem_Click);

        richTextBox1.MouseWheel += new System.Windows.Forms.MouseEventHandler(richTextBox1_MouseWheel);
        mainPicBox.MouseWheel += new System.Windows.Forms.MouseEventHandler(richTextBox1_MouseWheel);
        vScrollBar.MouseWheel += new System.Windows.Forms.MouseEventHandler(richTextBox1_MouseWheel);
        Localize();
    }
    private readonly ToolStripMenuItem[] macroMenuItems = new ToolStripMenuItem[KeyMacro.MaxFkey];
    public EraPictureBox MainPicBox { get { return mainPicBox; } }
    public VScrollBar ScrollBar { get { return vScrollBar; } }
    public RichTextBox TextBox { get { return richTextBox1; } }
    public ToolTip ToolTip { get { return toolTipButton; } }
    private EmueraConsole console;

    // [Emuera改修:GAMEPAD-V1]
    // Windows入力層は初回ゲーム画面の描画完了後に生成する。コンストラクタや
    // 初回Handle生成中にポーリング・Raw Input登録を始めると起動表示を変えるため。
    private GamepadManager? gamepadManager;
    private System.Windows.Forms.Timer? gamepadTimer;
    private bool gamepadProcessing;
    private bool gamepadManagerReady;
    private bool gamepadWindowActive = true;
    private bool gamepadDialogActive;
    private nint gamepadRawInputHandle;

    private bool RegisterGamepadRawInput(bool force = false)
    {
        GamepadManager? manager = gamepadManager;
        if (!gamepadManagerReady || manager == null || !IsHandleCreated)
            return false;

        nint handle = Handle;
        if (!force && gamepadRawInputHandle == handle)
            return true;

        bool success = manager.RegisterRawInput(handle);
        if (success)
            gamepadRawInputHandle = handle;
        return success;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // 初回表示前はgamepadManagerReadyがfalseなので、既存のWinForms起動順を
        // 変えない。後のHandle再作成時だけRaw Inputを再登録する。
        RegisterGamepadRawInput();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        gamepadRawInputHandle = nint.Zero;
        base.OnHandleDestroyed(e);
    }

    protected override void OnDeactivate(EventArgs e)
    {
        gamepadWindowActive = false;
        if (gamepadManagerReady && gamepadManager is GamepadManager manager)
            manager.OnWindowDeactivated();
        base.OnDeactivate(e);
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        gamepadWindowActive = true;
        if (!gamepadManagerReady || gamepadManager is not GamepadManager manager)
            return;

        manager.OnWindowActivated();
        bool rawInputRegistered = RegisterGamepadRawInput(force: true);
        if (!gamepadDialogActive && gamepadTimer != null && !gamepadTimer.Enabled)
            gamepadTimer.Start();
        manager.LogLifecycleDiagnostic($"Window Activated recovery: backend={manager.ActiveBackend}, "
            + $"connected={manager.IsConnected}, timerEnabled={gamepadTimer?.Enabled == true}, "
            + $"Raw Input re-registration={(rawInputRegistered ? "SUCCESS" : "SKIPPED/FAILED")}.");
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        //1823 INPUTMOUSEKEY Key入力全てを捕まえてERB側で処理する
        //if (console != null && console.IsWaitingPrimitiveKey)
        if (console != null && console.IsWaitingPrimitive)
        {
            return false;
        }
        if ((keyData & Keys.KeyCode) == Keys.B && (keyData & Keys.Modifiers & Keys.Control) == Keys.Control)
        {
            if (WindowState != FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Minimized;
                return true;
            }
        }
        else if (((keyData & Keys.KeyCode) == Keys.C && (keyData & Keys.Modifiers & Keys.Control) == Keys.Control) || (keyData & Keys.KeyCode) == Keys.Insert && (keyData & Keys.Modifiers & Keys.Control) == Keys.Control)
        {
            if (string.IsNullOrEmpty(richTextBox1.SelectedText))
            {
                // [Emuera改修:CLIPBOARD-02] 修正者: epizo
                // 閉じたログ表示画面をその場で破棄し、大きなログと画面部品をメモリへ残さない。
                using ClipBoardDialog dialog = new()
                {
                    StartPosition = FormStartPosition.CenterParent
                };
                dialog.Setup(console);
                dialog.ShowDialog();
                return true;
            }
        }
        else if (((keyData & Keys.KeyCode) == Keys.V && (keyData & Keys.Modifiers & Keys.Control) == Keys.Control) || (keyData & Keys.KeyCode) == Keys.Insert && (keyData & Keys.Modifiers & Keys.Shift) == Keys.Shift)
        {
            var dateObject = Clipboard.GetDataObject();
            if (dateObject == null || !Clipboard.ContainsText())
                return true;
            else
            {
                if (dateObject.GetDataPresent(DataFormats.Text) == true)
                    richTextBox1.Paste(DataFormats.GetFormat(DataFormats.UnicodeText));
                return true;
            }
        }
        //else if (((int)keyData == (int)Keys.Control + (int)Keys.D) && Program.DebugMode)
        //{
        //    console.OpenDebugDialog();
        //    return true;
        //}
        //else if (((int)keyData == (int)Keys.Control + (int)Keys.R) && Program.DebugMode)
        //{
        //    if ((console.DebugDialog != null) && (console.DebugDialog.Created))
        //        console.DebugDialog.UpdateData();
        //}
        #region EE_AnchorのCB機能移植
        else if (Keys.Up == (keyData & Keys.KeyCode) && ((keyData & Keys.Modifiers & Keys.Control) == Keys.Control))
        {
            if (JSONConfig.User.CBUseClipboard && console.CBProc.ScrollUp(1)) return true;
        }
        else if (Keys.Down == (keyData & Keys.KeyCode) && ((keyData & Keys.Modifiers & Keys.Control) == Keys.Control))
        {
            if (JSONConfig.User.CBUseClipboard && console.CBProc.ScrollDown(1)) return true;
        }
        #endregion

        else if (Config.UseKeyMacro)
        {
            int keyCode = (int)(keyData & Keys.KeyCode);
            bool shiftPressed = (keyData & Keys.Modifiers) == Keys.Shift;
            bool ctrlPressed = (keyData & Keys.Modifiers) == Keys.Control;
            bool unPressed = (keyData & Keys.Modifiers) == 0;
            if (keyCode >= (int)Keys.F1 && keyCode <= (int)Keys.F12)
            {
                int macroNum = keyCode - (int)Keys.F1;
                if (shiftPressed)
                {
                    if (!string.IsNullOrEmpty(richTextBox1.Text))
                        KeyMacro.SetMacro(macroNum, macroGroup, richTextBox1.Text);
                    return true;
                }
                else if (unPressed)
                {
                    richTextBox1.Text = KeyMacro.GetMacro(macroNum, macroGroup);
                    richTextBox1.SelectionStart = richTextBox1.Text.Length;
                    return true;
                }
            }
            else if (ctrlPressed)
            {
                int newGroupNum = -1;
                if (keyCode >= (int)Keys.D0 && keyCode <= (int)Keys.D9)
                    newGroupNum = keyCode - (int)Keys.D0;
                else if (keyCode >= (int)Keys.NumPad0 && keyCode <= (int)Keys.NumPad9)
                    newGroupNum = keyCode - (int)Keys.NumPad0;
                if (newGroupNum >= 0)
                {
                    setNewMacroGroup(newGroupNum);
                }
            }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }


    protected override void WndProc(ref Message m)
    {
        const int WM_INPUT = 0x00FF;
        const int WM_INPUT_DEVICE_CHANGE = 0x00FE;
        const int WM_SYSCOMMAND = 0x112;
        //const int WM_MOUSEWHEEL = 0x020A;
        const int SC_MOVE = 0xf010;
        const int SC_MAXIMIZE = 0xf030;

        GamepadManager? manager = gamepadManager;
        if (m.Msg == WM_INPUT && gamepadManagerReady && manager != null)
            manager.ProcessRawInput(m.LParam);
        else if (m.Msg == WM_INPUT_DEVICE_CHANGE && gamepadManagerReady && manager != null)
            manager.NotifyRawInputDeviceChange(unchecked((uint)m.WParam.ToInt64()), m.LParam);

        // WM_SYSCOMMAND (SC_MOVE) を無視することでフォームを移動できないようにする
        switch (m.Msg)
        {
            case WM_SYSCOMMAND:
                {
                    int wparam = m.WParam.ToInt32() & 0xfff0;
                    switch (wparam)
                    {
                        case SC_MOVE:
                            // if (WindowState == FormWindowState.Maximized)
                            //     return;
                            break;
                        case SC_MAXIMIZE:
                            if (Screen.AllScreens.Length == 1)
                            {
                                MaximizedBounds = new Rectangle(Left, 0, Config.WindowX, Screen.PrimaryScreen!.WorkingArea.Height);
                            }
                            else
                            {
                                for (int i = 0; i < Screen.AllScreens.Length; i++)
                                {
                                    if (Left >= Screen.AllScreens[i].Bounds.Left && Left < Screen.AllScreens[i].Bounds.Right)
                                    {
                                        MaximizedBounds = new Rectangle(Left - Screen.AllScreens[i].Bounds.Left, Screen.AllScreens[i].Bounds.Top, Config.WindowX, Screen.AllScreens[i].WorkingArea.Height);
                                        break;
                                    }
                                }
                            }
                            break;
                    }
                    break;
                }

                //MouseWheelイベントをここで処理しようと思ったけどなんかここまで来ない (Windows 7)
                //case WM_MOUSEWHEEL:
                //	{
                //		if (!vScrollBar.Enabled)
                //			break;
                //		if (console == null)
                //			break;
                //		//int wparam_hiword = m.WParam.ToInt32() >> 16;
                //		int move = (m.WParam.ToInt32() >> 16) / 120 * -1;
                //		if ((vScrollBar.Value == vScrollBar.Maximum && move > 0) || (vScrollBar.Value == vScrollBar.Minimum && move < 0))
                //			break;
                //		int value = vScrollBar.Value + move;
                //		if (value >= vScrollBar.Maximum)
                //			vScrollBar.Value = vScrollBar.Maximum;
                //		else if (value <= vScrollBar.Minimum)
                //			vScrollBar.Value = vScrollBar.Minimum;
                //		else
                //			vScrollBar.Value = value;
                //		bool force_refresh = (vScrollBar.Value == vScrollBar.Maximum) || (vScrollBar.Value == vScrollBar.Minimum);

                //		//ボタンとの関係をチェック
                //		if (Config.UseMouse)
                //			force_refresh = console.MoveMouse(mainPicBox.PointToClient(Control.MousePosition)) || force_refresh;
                //		//上端でも下端でもなくボタン選択状態のアップデートも必要ないなら描画を控えめに。
                //		console.RefreshStrings(force_refresh);

                //		break;
                //	}
        }
        base.WndProc(ref m);
    }

    private async void Init(object sender, EventArgs e)
    {
        await console.Initialize();
#if STARTUP_MEMORY_TRIM
        BeginInvoke(CompleteStartup);
#else
        CompleteStartup();
#endif
    }

    private void CompleteStartup()
    {
        // [Emuera改修:START-04]
        // 起動完了時のmanaged memoryが2GiB以上の大規模構成だけ整理する。
        // 2GiB未満ではskipし、必要ならMSBuild propertyで無効化できる。
        // 参照: プロジェクト資料/06_コード案内.md
#if STARTUP_MEMORY_TRIM
        const long startupMemoryTrimThresholdBytes = 2L * 1024 * 1024 * 1024;
        if (GC.GetTotalMemory(false) >= startupMemoryTrimThresholdBytes)
        {
            PerformanceMetrics.MarkStartup("MemoryTrimStart");
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            PerformanceMetrics.MarkStartup("MemoryTrimEnd");
        }
        else
        {
            PerformanceMetrics.MarkStartup("MemoryTrimSkipped");
        }
#endif
        PerformanceMetrics.MarkStartup("InputReady");
        PerformanceMetrics.WriteStartup();

        // [Emuera改修:GAMEPAD-V1]
        // 元Emueraの初回表示・描画完了後に、メッセージキューを一度戻してから
        // ゲームパッド入力層を始動する。これにより起動中のフォームHandle生成や
        // 子コントロール描画をゲームパッド処理が先行して変えない。
        if (!Program.StartupTestMode)
            BeginInvoke(InitializeGamepadAfterStartup);
        if (Program.StartupTestMode)
            BeginInvoke(Close);
    }

    private void InitializeGamepadAfterStartup()
    {
        if (gamepadManagerReady || IsDisposed || Disposing)
            return;

        GamepadManager manager = new(Program.GamepadDebugMode);
        gamepadManager = manager;
        gamepadManagerReady = true;

        if (!manager.IsAvailable)
            return;

        RegisterGamepadRawInput();
        gamepadTimer = new System.Windows.Forms.Timer
        {
            Interval = 33,
        };
        gamepadTimer.Tick += gamepadTimer_Tick;
        gamepadTimer.Start();
    }

    /// <summary>
    /// 1819 リサイズ時の処理を全廃しAnchor&Dock処理にマルナゲ
    /// 初期設定のみここで行う。ついでに再起動時の位置・サイズ処理も追加
    /// </summary>
    private void initControlSizeAndLocation()
    {
        //Windowのサイズ設定
        int winWidth = Config.WindowX + vScrollBar.Width;
        int winHeight = Config.WindowY;
        bool winMaximize = false;
        if (Config.SizableWindow)
        {
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            winMaximize = Config.WindowMaximixed;
        }
        else
        {
            FormBorderStyle = FormBorderStyle.Fixed3D;
            MaximizeBox = false;
        }

        int menuHeight;
        if (Config.UseMenu)
        {
            menuStrip.Enabled = true;
            menuStrip.Visible = true;
            winHeight += menuStrip.Height;
            menuHeight = menuStrip.Height;
        }
        else
        {
            menuStrip.Enabled = false;
            menuStrip.Visible = false;
            menuHeight = 0;
        }
        //Windowの位置設定
        if (Config.SetWindowPos)
        {
            StartPosition = FormStartPosition.Manual;
            Location = new Point(Config.WindowPosX, Config.WindowPosY);
        }
        else if (!winMaximize)
        {
            StartPosition = FormStartPosition.Manual;
        }
        ClientSize = new Size(winWidth, winHeight);

        //EmuVerToolStripTextBox.Location = new Point(Config.WindowX - vScrollBar.Width - EmuVerToolStripTextBox.Width, 3);

        mainPicBox.Location = new Point(0, menuHeight);
        mainPicBox.Size = new Size(Config.WindowX, winHeight - menuHeight - Config.LineHeight);

        var font = new Font(Config.FontName, Config.FontSize, GraphicsUnit.Pixel);
        var size = Config.FontSize * (Config.FontSize / (float)font.Height);
        richTextBox1.Font = new Font(Config.FontName, size, GraphicsUnit.Pixel);
        richTextBox1.Location = new Point(0, winHeight - Config.LineHeight);
        richTextBox1.Size = new Size(Config.WindowX, Config.LineHeight);
        vScrollBar.Location = new Point(winWidth - vScrollBar.Size.Width, menuHeight);
        vScrollBar.Size = new Size(vScrollBar.Size.Width, winHeight - menuHeight);

        int minimamY = 100;
        if (minimamY < menuHeight + Config.LineHeight * 2)
            minimamY = menuHeight + Config.LineHeight * 2;
        if (minimamY > Height)
            minimamY = Height;
        int maximamY = 2560;
        if (maximamY < Height)
            maximamY = Height;
        MinimumSize = new Size(Width, minimamY);
        MaximumSize = new Size(Width, maximamY);
        if (winMaximize)
            WindowState = FormWindowState.Maximized;
    }

    private void mainPicBox_MouseMove(object sender, MouseEventArgs e)
    {
        if (!Config.UseMouse)
            return;
        if (console == null)
            return;
        if (console.MoveMouse(e.Location))
            console.RefreshStrings(true);
    }

    bool changeTextbyMouse;
    private bool gamepadMacroPending;
    private string? gamepadMacroPendingOriginalText;
    private string? gamepadMacroPendingOriginalLastInput;
    private bool settingGamepadMacroText;

    #region EE_AnchorのCB機能移植
    private void mainPicBox_MouseClickCBCheck(object sender, System.Windows.Forms.MouseEventArgs e)
    {
        if (JSONConfig.User.CBUseClipboard)
        {
            if (e.Button == MouseButtons.Left) console.CBProc.Check(ClipboardProcessor.CBTriggers.LeftClick);
            else if (e.Button == MouseButtons.Middle) console.CBProc.Check(ClipboardProcessor.CBTriggers.MiddleClick);
        }
    }

    private void mainPicBox_MouseDoubleClickCBCheck(object sender, System.Windows.Forms.MouseEventArgs e)
    {
        if (JSONConfig.User.CBUseClipboard && e.Button == MouseButtons.Left) console.CBProc.Check(ClipboardProcessor.CBTriggers.DoubleLeftClick);
    }
    #endregion

    private void mainPicBox_MouseDown(object sender, MouseEventArgs e)
    {
        richTextBox1.Focus();//画面をクリックしてもテキストボックスからフォーカスが外れないようにする

        if (!Config.UseMouse)
            return;
        if (console == null || console.IsInProcess)
            return;
        if (console.IsWaitingPrimitive)
        //			if (console.IsWaitingPrimitiveMouse)
        {
            ProcessPrimitiveMouseInput(e.Location, e.Button);
            return;
        }
        bool isBacklog = vScrollBar.Value != vScrollBar.Maximum;

        // [Emuera改修:MOUSE-01]
        // サイドボタンを「指定文言を含む現在のゲーム内ボタンを押す」操作へ変換する。
        // ゲーム画面を直接めくるのではなく、通常クリックと同じ入力を渡すので互換性を保てる。
        // 過去ログ表示中は誤選択を避け、まず最新行へ戻すだけにする。
        // 参照: プロジェクト資料/06_コード案内.md
        if (e.Button == MouseButtons.XButton1 || e.Button == MouseButtons.XButton2)
        {
            if (isBacklog)
            {
                vScrollBar.Value = vScrollBar.Maximum;
                console.RefreshStrings(true);
                return;
            }

            string targetText = e.Button == MouseButtons.XButton1
                ? JSONConfig.User.MouseXButton1ButtonText
                : JSONConfig.User.MouseXButton2ButtonText;
            if (console.TryGetCurrentButtonInputByText(targetText, out string input))
            {
                changeTextbyMouse = console.IsWaintingOnePhrase;
                richTextBox1.Text = input;
                if (console.IsWaintingOnePhrase)
                    last_inputed = "";
                PressEnterKey(false, true);
            }
            return;
        }

        string str = console.SelectedString;

        if (isBacklog)
            if ((e.Button == MouseButtons.Left) || (e.Button == MouseButtons.Right))
            {
                vScrollBar.Value = vScrollBar.Maximum;
                console.RefreshStrings(true);
            }
        if (console.IsWaitingEnterKey && str == null)
        {
            if (isBacklog)
                return;

            if (console.IsError)
            {
                if (e.Button == MouseButtons.Left)
                {
                    PressEnterKey(false, true);
                    return;
                }
            }

            if (e.Button == MouseButtons.Right)
                PressEnterKey(true, true);
            else if (e.Button == MouseButtons.Left)
                PressEnterKey(false, true);
            return;
        }
        //左が押されたなら選択。
        if (str != null && ((e.Button & MouseButtons.Left) == MouseButtons.Left))
        {
            changeTextbyMouse = console.IsWaintingOnePhrase;
            richTextBox1.Text = str;
            //念のため
            if (console.IsWaintingOnePhrase)
                last_inputed = "";
            //右が押しっぱなしならスキップ追加。
            if ((Control.MouseButtons & MouseButtons.Right) == MouseButtons.Right)
                PressEnterKey(true, true);
            else
                PressEnterKey(false, true);
            return;
        }
    }

    private void vScrollBar_Scroll(object sender, ScrollEventArgs e)
    {
        //上端でも下端でもないなら描画を控えめに。
        if (console == null)
            return;
        console.RefreshStrings((vScrollBar.Value == vScrollBar.Maximum) || (vScrollBar.Value == vScrollBar.Minimum));
    }

    public void PressEnterKey(bool mesSkip, bool inputsByMouse)
    {
        if (console == null || console.IsInProcess)
            return;
        ClearGamepadMacroPending(restore: false);
        //if (console.inProcess)
        //{
        //	richTextBox1.Text = "";
        //	return;
        //}
        string str = richTextBox1.Text;
        if (console.IsWaintingOnePhrase && last_inputed.Length > 0)
        {
            str = str.Remove(0, last_inputed.Length);
            last_inputed = "";
        }
        changeTextbyMouse = false;
        updateInputs(str);
        console.PressEnterKey(mesSkip, str, inputsByMouse);
    }

    readonly string[] prevInputs = new string[100];
    int selectedInputs = 100;
    int lastSelected = 100;
    void updateInputs(string cur)
    {
        if (string.IsNullOrEmpty(cur))
        {
            richTextBox1.Text = "";
            return;
        }
        if (selectedInputs == prevInputs.Length || cur != prevInputs[^1])
        {
            for (int i = 0; i < prevInputs.Length - 1; i++)
            {
                prevInputs[i] = prevInputs[i + 1];
            }
            prevInputs[^1] = cur;
            //1729a eramakerと同じ処理系に変更 1730a 再修正
            if (selectedInputs > 0 && selectedInputs != prevInputs.Length && cur == prevInputs[selectedInputs - 1])
                lastSelected = --selectedInputs;
            else
                lastSelected = 100;
        }
        else
        {
            lastSelected = selectedInputs;
        }
        richTextBox1.Text = "";
        selectedInputs = prevInputs.Length;
    }

    void movePrev(int move)
    {
        if (move == 0)
            return;
        //if((selectedInputs != prevInputs.Length) &&(prevInputs[selectedInputs] != richTextBox1.Text))
        //	selectedInputs =  prevInputs.Length;
        int next;
        if (lastSelected != prevInputs.Length && selectedInputs == prevInputs.Length)
        {
            if (move == -1)
                move = 0;
            next = lastSelected + move;
            lastSelected = prevInputs.Length;
        }
        else
            next = selectedInputs + move;
        if ((next < 0) || (next > prevInputs.Length))
            return;
        if (next == prevInputs.Length)
        {
            selectedInputs = next;
            richTextBox1.Text = "";
            return;
        }
        if (string.IsNullOrEmpty(prevInputs[next]))
            if (++next == prevInputs.Length)
                return;

        selectedInputs = next;
        richTextBox1.Text = prevInputs[next];
        richTextBox1.SelectionStart = 0;
        richTextBox1.SelectionLength = richTextBox1.Text.Length;
        return;
    }

    private void exitToolStripMenuItem_Click(object sender, EventArgs e)
    {
        var result = MessageBox.Show(LocalizationManager.MsgBox.ExitAsk, LocalizationManager.MsgBox.Exit, MessageBoxButtons.OKCancel);
        if (result != DialogResult.OK)
            return;
        Close();

    }

    private void rebootToolStripMenuItem_Click(object sender, EventArgs e)
    {
        var result = MessageBox.Show(LocalizationManager.MsgBox.RestartAsk, LocalizationManager.MsgBox.Restart, MessageBoxButtons.OKCancel);
        if (result != DialogResult.OK)
            return;
        Reboot();
    }

    //private void loadToolStripMenuItem_Click(object sender, EventArgs e)
    //{
    //    openFileDialog.InitialDirectory = StaticConfig.SavDir;
    //    DialogResult result = openFileDialog.ShowDialog();
    //    string filepath = openFileDialog.FileName;
    //    if (!File.Exists(filepath))
    //    {
    //        MessageBox.Show("ファイルがありません", "File Not Found");
    //        return;
    //    }
    //}

    readonly string[] _args = [];
    public void Reboot()
    {
        //新たにアプリケーションを起動する
        Process.Start(Application.ExecutablePath, _args);

        //現在のアプリケーションを終了する
        Close();
    }

    public void GotoTitle()
    {
        if (console == null)
            return;
        console.GotoTitle();
    }

    public async Task ReloadErb()
    {
        if (console == null)
            return;
        await console.ReloadErb();
    }

    private void mainPicBox_MouseLeave(object sender, EventArgs e)
    {
        if (Config.UseMouse)
            console.LeaveMouse();
    }

    private void gamepadTimer_Tick(object? sender, EventArgs e)
    {
        GamepadManager? manager = gamepadManager;
        if (gamepadProcessing || console == null || manager == null)
            return;

        GamepadAction action = manager.Poll();
        if (!gamepadWindowActive)
            return;
        if (manager.IsConnected && console.GamepadEnsureSelection())
            console.RefreshStrings(true);
        if (action.Kind == GamepadActionKind.None)
            return;
        if (console.IsInProcess
            && action.Kind != GamepadActionKind.Escape
            && action.Kind != GamepadActionKind.OpenSettings)
            return;

        gamepadProcessing = true;
        try
        {
            switch (action.Kind)
            {
                case GamepadActionKind.Direction:
                    if (action.DirectionSource == GamepadDirectionSource.LeftStick
                        && console.TryGamepadDirectInput(action.Direction))
                        break;
                    if (console.GamepadMove(action.Direction))
                        console.RefreshStrings(true);
                    break;
                case GamepadActionKind.Confirm:
                    if (gamepadMacroPending)
                        ExecuteGamepadMacroPending();
                    else
                        console.GamepadConfirm();
                    break;
                case GamepadActionKind.Cancel:
                    if (gamepadMacroPending)
                        ClearGamepadMacroPending(restore: true);
                    else
                        console.GamepadCancel();
                    break;
                case GamepadActionKind.Escape:
                    ProcessEscapeInput();
                    break;
                case GamepadActionKind.OpenSettings:
                    ShowConfigDialog(openGamepadTab: true);
                    break;
                case GamepadActionKind.ScrollUp:
                    if (!console.GamepadShoulderNavigatePage(nextPage: false))
                        ScrollLogByGamepad(-1);
                    break;
                case GamepadActionKind.ScrollDown:
                    if (!console.GamepadShoulderNavigatePage(nextPage: true))
                        ScrollLogByGamepad(1);
                    break;
                case GamepadActionKind.Macro1:
                    HandleGamepadMacro(0);
                    break;
                case GamepadActionKind.Macro2:
                    HandleGamepadMacro(1);
                    break;
                case GamepadActionKind.Macro3:
                    HandleGamepadMacro(2);
                    break;
            }
        }
        finally
        {
            gamepadProcessing = false;
        }
    }

    private void ScrollLogByGamepad(int direction)
    {
        if (!vScrollBar.Enabled || direction == 0)
            return;

        int move = direction * vScrollBar.SmallChange * Config.ScrollHeight;
        if ((vScrollBar.Value == vScrollBar.Maximum && move > 0)
            || (vScrollBar.Value == vScrollBar.Minimum && move < 0))
            return;

        int value = vScrollBar.Value + move;
        if (value >= vScrollBar.Maximum)
            vScrollBar.Value = vScrollBar.Maximum;
        else if (value <= vScrollBar.Minimum)
            vScrollBar.Value = vScrollBar.Minimum;
        else
            vScrollBar.Value = value;
        console.RefreshStrings(vScrollBar.Value == vScrollBar.Maximum || vScrollBar.Value == vScrollBar.Minimum);
    }

    private void コンフィグCToolStripMenuItem_Click(object sender, EventArgs e)
    {
        ShowConfigDialog();
    }

    public void ShowConfigDialog(bool openGamepadTab = false)
    {

        if (console == null || GlobalStatic.Console == null)
            return;
        GamepadManager? manager = gamepadManager;
        bool timerWasEnabled = gamepadTimer?.Enabled == true;
        gamepadDialogActive = true;
        gamepadTimer?.Stop();
        manager?.ResetForConfiguration();
        ConfigDialogResult result = ConfigDialogResult.Cancel;
        try
        {
            using ConfigDialog dialog = new(manager)
            {
                StartPosition = FormStartPosition.CenterParent
            };
            dialog.SetConfig(this, openGamepadTab);
            dialog.ShowDialog(this);
            result = dialog.Result;
        }
        finally
        {
            manager?.ResetForConfiguration();
            gamepadDialogActive = false;
            if (timerWasEnabled && gamepadTimer != null && !gamepadTimer.Enabled)
                gamepadTimer.Start();
        }
        if (result == ConfigDialogResult.SaveReboot)
            Reboot();
    }

    private void タイトルへ戻るTToolStripMenuItem_Click(object sender, EventArgs e)
    {
        if (console == null)
            return;
        if (console.IsInProcess)
        {
            MessageBox.Show(LocalizationManager.MsgBox.NotAvailableDuringScript);
            return;
        }
        if (console.notToTitle)
        {
            if (console.byError)
                MessageBox.Show(LocalizationManager.MsgBox.ErrorInAnalysisMode);
            else
                MessageBox.Show(LocalizationManager.MsgBox.CanNotReturnToTitle);
            return;
        }
        var result = MessageBox.Show(LocalizationManager.MsgBox.ReturnToTitleAsk, LocalizationManager.MsgBox.ReturnToTitle, MessageBoxButtons.OKCancel);
        if (result != DialogResult.OK)
            return;
        GotoTitle();
    }

    private async void コードを読み直すcToolStripMenuItem_Click(object sender, EventArgs e)
    {
        if (console == null)
            return;
        if (console.IsInProcess)
        {
            MessageBox.Show(LocalizationManager.MsgBox.NotAvailableDuringScript);
            return;
        }
        var result = MessageBox.Show(LocalizationManager.MsgBox.ReloadErbAsk, LocalizationManager.MsgBox.ReloadErb, MessageBoxButtons.OKCancel);
        if (result != DialogResult.OK)
            return;
        await ReloadErb();

    }

    private void mainPicBox_Paint(object sender, SKPaintGLSurfaceEventArgs e)
    {
        if (console == null)
            return;
        console.OnPaint(e.Surface.Canvas);
    }

    private void ログを保存するSToolStripMenuItem_Click(object sender, EventArgs e)
    {
        if (console == null)
            return;
        saveFileDialog.InitialDirectory = Program.ExeDir;
        DateTime time = DateTime.Now;
        string fname = time.ToString("yyyyMMdd-HHmmss");
        fname += ".log";
        saveFileDialog.FileName = fname;
        DialogResult result = saveFileDialog.ShowDialog();
        if (result == DialogResult.OK)
        {
            console.OutputLog(Path.GetFullPath(saveFileDialog.FileName));
        }
    }

    private void ログをクリップボードにコピーToolStripMenuItem_Click(object sender, EventArgs e)
    {
        try
        {
            // [Emuera改修:CLIPBOARD-02] 修正者: epizo
            // メニューから開いた場合もCtrl+Cの場合と同じく、閉じた直後に必ず破棄する。
            using ClipBoardDialog dialog = new();
            dialog.Setup(console);
            dialog.ShowDialog();
        }
        catch (Exception)
        {
            MessageBox.Show(LocalizationManager.MsgBox.CanNotOpenClipboard);
            return;
        }
    }

    private async void ファイルを読み直すFToolStripMenuItem_Click(object sender, EventArgs e)
    {
        if (console == null)
            return;
        if (console.IsInProcess)
        {
            MessageBox.Show(LocalizationManager.MsgBox.NotAvailableDuringScript);
            return;
        }
        DialogResult result = openFileDialog.ShowDialog();
        List<string> filepath = [];
        if (result == DialogResult.OK)
        {
            foreach (string fname in openFileDialog.FileNames)
            {
                if (!File.Exists(fname))
                {
                    MessageBox.Show(LocalizationManager.MsgBox.FileNotFound, "File Not Found");
                    return;
                }
                if (!Path.GetExtension(fname).Equals(".ERB", StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show(LocalizationManager.MsgBox.IsNotErb, LocalizationManager.MsgBox.FileFormatError);
                    return;
                }
                if (fname.StartsWith(Program.ErbDir, StringComparison.OrdinalIgnoreCase))
                    filepath.Add(Program.ErbDir + fname[Program.ErbDir.Length..]);
                else
                    filepath.Add(fname);
            }
            await console.ReloadPartialErb(filepath);
        }
    }

    private void MainWindow_FormClosing(object sender, FormClosingEventArgs e)
    {
        gamepadTimer?.Stop();
        gamepadTimer?.Dispose();
        gamepadManager?.Dispose();
        if (Config.UseKeyMacro)
            KeyMacro.SaveMacro();
        if (console != null)
        {
            //ほっとしても勝手に閉じるが、その場合はDebugDialogのClosingイベントが発生しない
            if (Program.DebugMode && (console.DebugDialog != null) && console.DebugDialog.Created)
                console.DebugDialog.Close();
            console.Dispose();
        }
    }

    private async void フォルダを読み直すFToolStripMenuItem_Click(object sender, EventArgs e)
    {
        if (console == null)
            return;
        if (console.IsInProcess)
        {
            MessageBox.Show(LocalizationManager.MsgBox.NotAvailableDuringScript);
            return;
        }
        //List<KeyValuePair<string, string>> filepath = new List<KeyValuePair<string, string>>();
        if (folderSelectDialog.ShowDialog() == DialogResult.OK)
        {
            await console.ReloadFolder(folderSelectDialog.SelectedPath);
        }
    }

    void richTextBox1_MouseWheel(object? sender, System.Windows.Forms.MouseEventArgs e)
    {
        //if (!Config.UseMouse)
        //	return;
        if (!vScrollBar.Enabled)
            return;
        if (console == null)
            return;

        if (console.IsWaitingPrimitive)
        //			if (console.IsWaitingPrimitiveMouse)
        {
            console.MouseWheel(mainPicBox.PointToClient(Control.MousePosition), e.Delta);
            return;
        }
        //e.Deltaには大きな値が入っているので符号のみ採用する
        int move = -Math.Sign(e.Delta) * vScrollBar.SmallChange * Config.ScrollHeight;
        #region EE_AnchorのCB機能移植
        //Clipboard scroll only when using ctrl
        if (JSONConfig.User.CBUseClipboard && ModifierKeys == Keys.Control)
        {
            if (move > 0) console.CBProc.ScrollDown(move);
            else if (move < 0) console.CBProc.ScrollUp(-move);
            return;
        }
        #endregion

        //スクロールが必要ないならリターンする
        if ((vScrollBar.Value == vScrollBar.Maximum && move > 0) || (vScrollBar.Value == vScrollBar.Minimum && move < 0))
            return;
        int value = vScrollBar.Value + move;
        if (value >= vScrollBar.Maximum)
            vScrollBar.Value = vScrollBar.Maximum;
        else if (value <= vScrollBar.Minimum)
            vScrollBar.Value = vScrollBar.Minimum;
        else
            vScrollBar.Value = value;
        bool force_refresh = (vScrollBar.Value == vScrollBar.Maximum) || (vScrollBar.Value == vScrollBar.Minimum);

        //ボタンとの関係をチェック
        if (Config.UseMouse)
            force_refresh = console.MoveMouse(mainPicBox.PointToClient(Control.MousePosition)) || force_refresh;
        //上端でも下端でもなくボタン選択状態のアップデートも必要ないなら描画を控えめに。
        console.RefreshStrings(force_refresh);
    }

    private bool textBox_flag = true;
    private string last_inputed = "";

    public void update_lastinput()
    {
        richTextBox1.TextChanged -= new EventHandler(richTextBox1_TextChanged);
        richTextBox1.KeyDown -= new KeyEventHandler(richTextBox1_KeyDown);
        System.Windows.Forms.Application.DoEvents();
        richTextBox1.TextChanged += new EventHandler(richTextBox1_TextChanged);
        richTextBox1.KeyDown += new KeyEventHandler(richTextBox1_KeyDown);
        last_inputed = richTextBox1.Text;
    }

    public void clear_richText()
    {
        richTextBox1.Clear();
    }

    private void richTextBox1_TextChanged(object? sender, EventArgs e)
    {
        if (settingGamepadMacroText)
            return;
        if (console == null || console.IsInProcess)
            return;
        if (!textBox_flag)
            return;
        if (!console.IsWaintingOnePhrase && !console.IsWaitAnyKey)
            return;
        if (string.IsNullOrEmpty(richTextBox1.Text))
            return;
        if (changeTextbyMouse)
            return;
        //テキストの削除orテキストに変化がない場合は入力されたとみなさない
        if (richTextBox1.Text.Length <= last_inputed.Length)
        {
            last_inputed = richTextBox1.Text;
            return;
        }
        textBox_flag = false;
        if (console.IsWaitAnyKey)
        {
            richTextBox1.Clear();
            last_inputed = "";
        }
        //if (richTextBox1.Text.Length > 1)
        //    richTextBox1.Text = richTextBox1.Text.Remove(1);
        PressEnterKey(false, false);
        textBox_flag = true;
    }

    private void ProcessEscapeInput()
    {
        if (console == null)
            return;
        if (console.IsWaitingPrimitive)
        {
            ProcessPrimitiveMouseInput(mainPicBox.PointToClient(Cursor.Position), MouseButtons.Right);
            return;
        }
        console.KillMacro = true;
        if (!console.IsInProcess)
            PressEnterKey(true, false);
    }

    private void HandleGamepadMacro(int macroIndex)
    {
        if (!Config.UseKeyMacro || console == null || console.IsInProcess || console.IsWaitingPrimitive)
            return;

        JSONUserConfigData user = JSONConfig.User;
        int group = Math.Clamp(macroIndex switch
        {
            0 => user.GamepadMacro1Group,
            1 => user.GamepadMacro2Group,
            _ => user.GamepadMacro3Group,
        }, 0, KeyMacro.MaxGroup - 1);
        int fkey = Math.Clamp(macroIndex switch
        {
            0 => user.GamepadMacro1FKey,
            1 => user.GamepadMacro2FKey,
            _ => user.GamepadMacro3FKey,
        }, 1, KeyMacro.MaxFkey);
        string macro = KeyMacro.GetMacro(fkey - 1, group);
        if (macro.Length == 0)
            return;

        if (!gamepadMacroPending)
        {
            gamepadMacroPendingOriginalText = richTextBox1.Text;
            gamepadMacroPendingOriginalLastInput = last_inputed;
        }
        last_inputed = string.Empty;
        settingGamepadMacroText = true;
        try
        {
            richTextBox1.Text = macro;
            richTextBox1.SelectionStart = richTextBox1.Text.Length;
        }
        finally
        {
            settingGamepadMacroText = false;
        }

        if (user.GamepadMacroImmediateExecution)
            PressEnterKey(false, false);
        else
            gamepadMacroPending = true;
    }

    private void ExecuteGamepadMacroPending()
    {
        if (!gamepadMacroPending)
            return;
        PressEnterKey(false, false);
    }

    private void ClearGamepadMacroPending(bool restore)
    {
        if (!gamepadMacroPending)
            return;

        string? originalText = gamepadMacroPendingOriginalText;
        string? originalLastInput = gamepadMacroPendingOriginalLastInput;
        gamepadMacroPending = false;
        gamepadMacroPendingOriginalText = null;
        gamepadMacroPendingOriginalLastInput = null;
        if (!restore || originalText == null)
            return;

        settingGamepadMacroText = true;
        try
        {
            richTextBox1.Text = originalText;
            richTextBox1.SelectionStart = richTextBox1.Text.Length;
            last_inputed = originalLastInput ?? string.Empty;
        }
        finally
        {
            settingGamepadMacroText = false;
        }
    }

    private void ProcessPrimitiveMouseInput(Point point, MouseButtons button)
    {
        if (console == null || console.IsInProcess || !console.IsWaitingPrimitive)
            return;
        console.MouseDown(point, button);
    }

    private void richTextBox1_KeyDown(object? sender, KeyEventArgs e)
    {
        //1823 INPUTMOUSEKEY Key入力全てを捕まえてERB側で処理する
        //if (console.IsWaitingPrimitiveKey)
        if (console.IsWaitingPrimitive)
        {
            e.SuppressKeyPress = true;
            console.PressPrimitiveKey(e.KeyCode, e.KeyData, e.Modifiers);
            return;
        }
        if ((int)e.KeyData == (int)Keys.PageUp || (int)e.KeyData == (int)Keys.PageDown)
        {
            e.SuppressKeyPress = true;
            int move = 10;
            if ((int)e.KeyData == (int)Keys.PageUp)
                move *= -1;
            //スクロールが必要ないならリターンする
            if ((vScrollBar.Value == vScrollBar.Maximum && move > 0) || (vScrollBar.Value == vScrollBar.Minimum && move < 0))
                return;
            int value = vScrollBar.Value + move;
            if (value >= vScrollBar.Maximum)
                vScrollBar.Value = vScrollBar.Maximum;
            else if (value <= vScrollBar.Minimum)
                vScrollBar.Value = vScrollBar.Minimum;
            else
                vScrollBar.Value = value;
            //上端でも下端でもないなら描画を控えめに。
            console.RefreshStrings((vScrollBar.Value == vScrollBar.Maximum) || (vScrollBar.Value == vScrollBar.Minimum));
            return;
        }
        else if (vScrollBar.Value != vScrollBar.Maximum)
        {
            vScrollBar.Value = vScrollBar.Maximum;
            console.RefreshStrings(true);
        }
        if (e.KeyCode == Keys.Return)
        {
            e.SuppressKeyPress = true;
            if (!console.IsInProcess)
                PressEnterKey(false, false);
            return;
        }
        if (e.KeyCode == Keys.Escape)
        {
            e.SuppressKeyPress = true;
            ProcessEscapeInput();
            return;
        }
        if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Home || e.KeyCode == Keys.Back)
        {
            if ((richTextBox1.SelectionStart == 0 && richTextBox1.SelectedText.Length == 0) || richTextBox1.Text.Length == 0)
            {
                e.SuppressKeyPress = true;
                return;
            }
        }
        if (e.KeyCode == Keys.Right || e.KeyCode == Keys.End)
        {
            if (richTextBox1.SelectionStart == richTextBox1.Text.Length || richTextBox1.Text.Length == 0)
            {
                e.SuppressKeyPress = true;
                return;
            }
        }
        if (e.KeyCode == Keys.Up)
        {
            e.SuppressKeyPress = true;
            if (console.IsInProcess)
                return;
            movePrev(-1);
            return;
        }
        if (e.KeyCode == Keys.Down)
        {
            e.SuppressKeyPress = true;
            if (console.IsInProcess)
                return;
            movePrev(1);
            return;
        }
        if (e.KeyCode == Keys.Insert)
        {
            e.SuppressKeyPress = true;
            return;
        }
    }

    private void デバッグウインドウを開くToolStripMenuItem_Click(object sender, EventArgs e)
    {
        if (!Program.DebugMode)
            return;
        console.OpenDebugDialog();
    }

    private void デバッグ情報の更新ToolStripMenuItem_Click(object sender, EventArgs e)
    {
        if (!Program.DebugMode)
            return;
        if ((console.DebugDialog != null) && console.DebugDialog.Created)
            console.DebugDialog.UpdateData();
    }

    private void AutoVerbMenu_Opened(object sender, EventArgs e)
    {
        if ((console == null) || console.IsInProcess)
        {
            切り取り.Enabled = false;
            コピー.Enabled = false;
            貼り付け.Enabled = false;
            実行.Enabled = false;
            削除.Enabled = false;
            マクロToolStripMenuItem.Enabled = false;
            for (int i = 0; i < macroMenuItems.Length; i++)
                macroMenuItems[i].Enabled = false;
            return;
        }
        実行.Enabled = true;
        if (Config.UseKeyMacro)
        {
            マクロToolStripMenuItem.Enabled = true;

            for (int i = 0; i < macroMenuItems.Length; i++)
                macroMenuItems[i].Enabled = KeyMacro.GetMacro(i, macroGroup).Length > 0;
        }
        else
        {
            マクロToolStripMenuItem.Enabled = false;
            for (int i = 0; i < macroMenuItems.Length; i++)
                macroMenuItems[i].Enabled = false;
        }
        if (richTextBox1.SelectedText.Length > 0)
        {
            切り取り.Enabled = true;
            コピー.Enabled = true;
            削除.Enabled = true;
        }
        else
        {
            切り取り.Enabled = false;
            コピー.Enabled = false;
            削除.Enabled = false;
        }
        if (Clipboard.ContainsText())
            貼り付け.Enabled = true;
        else
            貼り付け.Enabled = false;

    }

    private void 切り取り_Click(object sender, EventArgs e)
    {
        if ((console == null) || console.IsInProcess || !切り取り.Enabled)
            return;
        if (richTextBox1.SelectedText.Length > 0)
            richTextBox1.Cut();
    }

    private void コピー_Click(object sender, EventArgs e)
    {
        if ((console == null) || console.IsInProcess || !コピー.Enabled)
            return;
        else if (richTextBox1.SelectedText.Length > 0)
            richTextBox1.Copy();
    }

    private void 貼り付け_Click(object sender, EventArgs e)
    {
        if ((console == null) || console.IsInProcess || !貼り付け.Enabled)
            return;
        if (Clipboard.GetDataObject() != null && Clipboard.ContainsText())
        {
            if (Clipboard.GetDataObject()!.GetDataPresent(DataFormats.Text))
                //Clipboard.SetText(Clipboard.GetText(TextDataFormat.UnicodeText));
                richTextBox1.Paste(DataFormats.GetFormat(DataFormats.UnicodeText));
            //richTextBox1.Paste();
            //if (richTextBox1.SelectedText.Length > 0)
            //    richTextBox1.SelectedText = "";
            //richTextBox1.AppendText(Clipboard.GetText());
        }
    }

    private void 削除_Click(object sender, EventArgs e)
    {
        if ((console == null) || console.IsInProcess || !削除.Enabled)
            return;
        if (richTextBox1.SelectedText.Length > 0)
            richTextBox1.SelectedText = "";
    }

    private void 実行_Click(object sender, EventArgs e)
    {
        if ((console == null) || console.IsInProcess || !実行.Enabled)
            return;
        PressEnterKey(false, false);
    }

    int macroGroup;
    private void マクロToolStripMenuItem_Click(object? sender, EventArgs e)
    {
        if ((console == null) || console.IsInProcess)
            return;
        if (!Config.UseKeyMacro)
            return;
        if (sender is ToolStripMenuItem item)
        {
            int fkeynum = (int)item.ShortcutKeys - (int)Keys.F1;
            string macro = KeyMacro.GetMacro(fkeynum, macroGroup);
            if (macro.Length > 0)
            {
                richTextBox1.Text = macro;
                richTextBox1.SelectionStart = richTextBox1.Text.Length;
            }
        }

    }

    private void グループToolStripMenuItem_Click(object sender, EventArgs e)
    {
        if ((console == null) || console.IsInProcess)
            return;
        if (!Config.UseKeyMacro)
            return;
        if (sender is ToolStripMenuItem item)
        {
            if (item.Tag is string tag)
            {
                setNewMacroGroup(int.Parse(tag));//とても無駄なキャスト&Parse
            }
            else
            {
                throw new Exception();
            }
        }
    }

    private void timerKeyMacroChanged_Tick(object sender, EventArgs e)
    {
        labelTimerCount++;
        if (labelTimerCount > 10)
        {
            timerKeyMacroChanged.Stop();
            timerKeyMacroChanged.Enabled = false;
            labelMacroGroupChanged.Visible = false;
        }
    }

    int labelTimerCount;
    private void setNewMacroGroup(int group)
    {
        labelTimerCount = 0;
        macroGroup = group;
        labelMacroGroupChanged.Text = KeyMacro.GetGroupName(group);
        timerKeyMacroChanged.Interval = 200;
        timerKeyMacroChanged.Enabled = true;
        timerKeyMacroChanged.Start();
        labelMacroGroupChanged.Location = new Point(4, richTextBox1.Location.Y - labelMacroGroupChanged.Height - 4);
        labelMacroGroupChanged.Visible = true;
    }

    Font? _tooltipFont;

    private void toolTipButton_Draw(object sender, DrawToolTipEventArgs e)
    {
        e.DrawBackground();
        e.DrawBorder();

        TextRenderer.DrawText(e.Graphics, e.ToolTipText, _tooltipFont, new Point(2, 2), Color.Black);
    }

    private void toolTipButton_Popup(object sender, PopupEventArgs e)
    {
        _tooltipFont ??= new Font(Config.DefaultFont.Typeface.FamilyName, (int)Math.Max(13, Config.DefaultFont.Size * 0.6));

        var toolTip = (ToolTip)sender;
        var size = TextRenderer.MeasureText(toolTip.GetToolTip(e.AssociatedControl), _tooltipFont);

        e.ToolTipSize = Size.Add(size, new Size(4, 4));
    }

    bool _isWidthLocked = true;
    private void ウィンドウ幅のロック変更ToolStripMenuItem_Click(object sender, EventArgs e)
    {
        if (_isWidthLocked)
        {
            _isWidthLocked = false;
            MinimumSize = new Size(0, 0);
            MaximumSize = new Size(int.MaxValue,
                                    int.MaxValue);
        }
        else
        {
            _isWidthLocked = true;

            if (Config.SizableWindow)
            {
                MinimumSize = Size with
                {
                    Height = 0
                };
                MaximumSize = Size with
                {
                    Height = int.MaxValue
                };
            }
            else
            {
                MinimumSize = Size;
                MaximumSize = Size;
            }

            ConfigData.Instance.GetConfigItem(ConfigCode.WindowX).SetValue(mainPicBox.Width);
            ConfigData.Instance.GetConfigItem(ConfigCode.WindowY).SetValue(mainPicBox.Height + Config.LineHeight);
            ConfigData.Instance.SaveConfig();
        }
    }


    private void Localize()
    {
        fileToolStripMenuItem.Text = LocalizationManager.MainWindow.File;
        rebootToolStripMenuItem.Text = LocalizationManager.MainWindow.Restart;
        デバッグモードで再起動ToolStripMenuItem.Text = LocalizationManager.MainWindow.RestartDebugMode;
        ログを保存するSToolStripMenuItem.Text = LocalizationManager.MainWindow.SaveLog;
        ログをクリップボードにコピーToolStripMenuItem.Text = LocalizationManager.MainWindow.CopyLogToClipboard;
        タイトルへ戻るTToolStripMenuItem.Text = LocalizationManager.MainWindow.BackToTitle;
        コードを読み直すcToolStripMenuItem.Text = LocalizationManager.MainWindow.ReloadAllScripts;
        フォルダを読み直すFToolStripMenuItem.Text = LocalizationManager.MainWindow.ReloadFolder;
        ファイルを読み直すFToolStripMenuItem.Text = LocalizationManager.MainWindow.ReloadScriptFile;
        exitToolStripMenuItem.Text = LocalizationManager.MainWindow.Exit;
        openFileDialog.Filter = LocalizationManager.MainWindow.FileFilter + " (*.erb)|*.erb";

        デバッグToolStripMenuItem.Text = LocalizationManager.MainWindow.Debug;
        デバッグウインドウを開くToolStripMenuItem.Text = LocalizationManager.MainWindow.OpenDebugWindow;
        デバッグ情報の更新ToolStripMenuItem.Text = LocalizationManager.MainWindow.UpdateDebugInfo;

        ツールToolStripMenuItem.Text = LocalizationManager.MainWindow.Tools;
        ウィンドウ幅のロック変更ToolStripMenuItem.Text = LocalizationManager.MainWindow.ToggleWidthLock;
        クリップボードにコピーToolStripMenuItem.Text = LocalizationManager.MainWindow.CopyToClipboard;

        設定ToolStripMenuItem.Text = LocalizationManager.MainWindow.Settings;

        toolStripMenuItem1.Text = LocalizationManager.MainWindow.Language;

        this.マクロToolStripMenuItem.Text = LocalizationManager.MainWindow.ContextMenu_KeyMacro;
        for (int i = 0; i < this.マクロToolStripMenuItem.DropDownItems.Count; i++)
            this.マクロToolStripMenuItem.DropDownItems[i].Text = LocalizationManager.MainWindow.ContextMenu_KeyMacro + i.ToString("D2");
        this.マクログループToolStripMenuItem.Text = LocalizationManager.MainWindow.ContextMenu_KeyMacroGroup;
        for (int i = 0; i < this.マクログループToolStripMenuItem.DropDownItems.Count; i++)
            this.マクログループToolStripMenuItem.DropDownItems[i].Text = KeyMacro.GetGroupName(i);

        this.切り取り.Text = LocalizationManager.MainWindow.ContextMenu_Cut;
        this.コピー.Text = LocalizationManager.MainWindow.ContextMenu_Copy;
        this.貼り付け.Text = LocalizationManager.MainWindow.ContextMenu_Paste;
        this.削除.Text = LocalizationManager.MainWindow.ContextMenu_Delete;
        this.実行.Text = LocalizationManager.MainWindow.ContextMenu_Execute;
    }

    private void デバッグモードで再起動ToolStripMenuItem_Click(object sender, EventArgs e)
    {
        //新たにアプリケーションを起動する
        Process.Start(Application.ExecutablePath, [.. _args, "-Debug"]);

        //現在のアプリケーションを終了する
        Application.ExitThread();
    }

    private void englishToolStripMenuItem_Click(object sender, EventArgs e)
    {
        LocalizationManager.SetLanguage(LocalizationManager.English);
        Localize();
    }

    private void japaneseToolStripMenuItem_Click(object sender, EventArgs e)
    {
        LocalizationManager.SetLanguage(LocalizationManager.Japanese);
        Localize();
    }

    private void chineseToolStripMenuItem_Click(object sender, EventArgs e)
    {
        LocalizationManager.SetLanguage(LocalizationManager.ChineseSimplified);
        Localize();
    }

    private void koreanToolStripMenuItem_Click(object sender, EventArgs e)
    {
        //Not implemented yet
        return;
        //LocalizationManager.SetLanguage(Korean);
        //Localize();
    }

    private void クリップボードにコピーToolStripMenuItem_Click(object sender, EventArgs e)
    {
        if (console == null || GlobalStatic.Console == null)
            return;
        if (クリップボードにコピーToolStripMenuItem.Checked)
            console.CBProc.Init();
        else
            console.CBProc.Reset();
        JSONConfig.User.CBUseClipboard = クリップボードにコピーToolStripMenuItem.Checked;
        JSONConfig.Save();
    }

    public void SetMacroGroupNames()
    {
        for (int i = 0; i < this.マクログループToolStripMenuItem.DropDownItems.Count; i++)
            this.マクログループToolStripMenuItem.DropDownItems[i].Text = KeyMacro.GetGroupName(i);
    }
}
