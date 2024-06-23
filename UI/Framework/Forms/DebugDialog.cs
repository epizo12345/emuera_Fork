using MinorShift.Emuera.GameProc;
using MinorShift.Emuera.GameView;
using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Runtime.Script.Parser;
using MinorShift.Emuera.Runtime.Script.Statements.Expression;
using MinorShift.Emuera.Runtime.Utils;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using MinorShift.Emuera.UI.Framework;
using System.ComponentModel;
using MinorShift.Emuera.Runtime.Config.JSON;
using System.Text.Json;

namespace MinorShift.Emuera.Forms;

public partial class DebugDialog : Form
{
    public DebugDialog()
    {
        InitializeComponent();

        this.TopMost = Config.DebugWindowTopMost;
        int width = Math.Max(this.MinimumSize.Width, Config.DebugWindowWidth);
        int height = Math.Max(this.MinimumSize.Height, Config.DebugWindowHeight);

        this.Size = new Size(width, height);
        if (Config.DebugSetWindowPos)
        {
            this.StartPosition = FormStartPosition.Manual;
            this.Location = new Point(Config.DebugWindowPosX, Config.DebugWindowPosY);
        }

        updateSize();
        checkBoxTopMost.Checked = this.TopMost;
        loadWatchList();

        var consoleHistoryFilePath = Program.ExeDir + "console_history.json";
        if (File.Exists(consoleHistoryFilePath))
        {
            history = JsonSerializer.Deserialize<List<string>>(File.OpenRead(consoleHistoryFilePath));
            selectedIndex = history.Count;
        }
        else
        {
            history = [];
        }

        Localize();



        this.Disposed += (_, a) =>
        {
            var config = ConfigData.Instance;
            config.GetDebugItem(ConfigCode.DebugWindowWidth).SetValue(Width);
            Config.DebugWindowWidth = Width;
            config.GetDebugItem(ConfigCode.DebugWindowHeight).SetValue(Height);
            Config.DebugWindowHeight = Height;
            config.SaveDebugConfig();

            var historyJson = JsonSerializer.Serialize(history);
            File.WriteAllText(consoleHistoryFilePath, historyJson);
            JSONConfig.Save();
        };
    }
    private Process emuera;
    private EmueraConsole mainConsole;

    internal void SetParent(EmueraConsole console, Process process)
    {
        emuera = process;
        mainConsole = console;
    }

    public string ConsoleText
    {
        get { return textBoxConsole.Text; }
        set { textBoxConsole.Text = value; }
    }
    public string TraceText
    {
        get { return textBoxTrace.Text; }
        set { textBoxTrace.Text = value; }
    }
    public void AddTraceText(string str)
    {
        this.SuspendLayout();
        textBoxTrace.Text += str;
        this.ResumeLayout(false);
    }

    public void UpdateData()
    {
        if (tabControlMain.SelectedTab == tabPageWatch)
            updateVarWatch();
        else if (tabControlMain.SelectedTab == tabPageTrace)
            updateTrace();
        else if (tabControlMain.SelectedTab == tabPageConsole)
            updateConsole();
    }

    private void tabControlMain_Selected(object sender, TabControlEventArgs e)
    {
        UpdateData();
        updateSize();
    }

    private void updateTrace()
    {
        string str = mainConsole.GetDebugTraceLog(false);
        if (str != null)
            textBoxTrace.Text = str;
        //textBoxTrace.SelectionStart = textBoxTrace.Text.Length;
        //textBoxTrace.Focus();
        //textBoxTrace.ScrollToCaret();
    }

    private void updateConsole()
    {
        textBoxConsole.Text = mainConsole.DebugConsoleLog;
        //textBoxConsole.SelectionStart = textBoxConsole.Text.Length;
        //textBoxConsole.Focus();
        //textBoxConsole.ScrollToCaret();
    }

    private void updateVarWatch()
    {
        GlobalStatic.Process.saveCurrentState(false);

        for (int i = 0; i < watchView.RowCount - 1; i++)
        {
            watchView[1, i].Value = getValueString((string)watchView[0, i].Value);
        }
        GlobalStatic.Process.clearMethodStack();
        GlobalStatic.Process.loadPrevState();
        this.Update();
    }
    private string getValueString(string str)
    {
        if ((emuera == null) || (GlobalStatic.EMediator == null))
            return "";
        if (string.IsNullOrEmpty(str))
            return "";
        mainConsole.RunERBFromMemory = true;
        try
        {
            if (Config.UseRenameFile)
            {
                str = Rename.RenameString(str);
            }
            CharStream st = new(str);
            WordCollection wc = LexicalAnalyzer.Analyse(st, LexEndWith.EoL, LexAnalyzeFlag.None);
            AExpression term = ExpressionParser.ReduceExpressionTerm(wc, TermEndWith.EoL);
            SingleTerm value = term.GetValue(GlobalStatic.EMediator);
            return value.ToString();
        }
        catch (CodeEE e)
        {
            return e.Message;
        }
        catch (Exception e)
        {
            return e.GetType().ToString() + ":" + e.Message;
        }
        finally
        {
            mainConsole.RunERBFromMemory = false;
        }

    }

    private void checkBoxTopMost_CheckedChanged(object sender, EventArgs e)
    {
        this.TopMost = checkBoxTopMost.Checked;
    }

    private void button1_Click(object sender, EventArgs e)
    {
        this.Close();
    }

    private void 閉じるToolStripMenuItem_Click(object sender, EventArgs e)
    {
        this.Close();
    }

    private void ウォッチリストの読込ToolStripMenuItem_Click(object sender, EventArgs e)
    {
        loadWatchList();
        updateVarWatch();
    }

    private void ウォッチリストの保存ToolStripMenuItem_Click(object sender, EventArgs e)
    {
        saveWatchList();
    }


    private readonly string watchFilepath = Program.DebugDir + "watchlist.csv";
    private readonly string consoleFilepath = Program.DebugDir + "console.log";

    private void saveData()
    {
        saveWatchList();

        StreamWriter writer = null;
        //トレースの仕様をいじってるうちに保存する意味が無いものになった
        //try
        //{
        //    writer = new StreamWriter(traceFilepath, false, StaticConfig.Encode);
        //    writer.Write(mainConsole.GetDebugTraceLog(true));
        //}
        //catch
        //{
        //    MessageBox.Show("トレースログの保存に失敗しました", "デバッグウインドウ");
        //    return;
        //}
        //finally
        //{
        //    if (writer != null)
        //        writer.Close();
        //}
        //writer = null;
        try
        {
            writer = new StreamWriter(consoleFilepath, false, Config.Encode);
            writer.Write(mainConsole.DebugConsoleLog);
        }
        catch
        {
            MessageBox.Show(LocalizationManager.MsgBox.FailedOutputLogError, LocalizationManager.MsgBox.FailedOutputLog);
            return;
        }
        finally
        {
            if (writer != null)
                writer.Close();
        }
    }

    private void saveWatchList()
    {
        StreamWriter writer = null;
        try
        {
            writer = new StreamWriter(watchFilepath, false, Config.Encode);
            foreach (DataGridViewRow lvi in watchView.Rows)
                if (!string.IsNullOrEmpty((string)lvi.Cells[0].Value))
                    writer.WriteLine((string)(lvi.Cells[0].Value));
        }
        catch
        {
            MessageBox.Show("変数ウォッチリストの保存に失敗しました", "デバッグウインドウ");
            return;
        }
        finally
        {
            if (writer != null)
                writer.Close();
        }
    }

    private void loadWatchList()
    {
        if (!File.Exists(watchFilepath))
            return;
        List<string> saveStrList = [];

        StreamReader reader = null;
        try
        {
            reader = new StreamReader(watchFilepath, Config.Encode);
            string line = null;
            while ((line = reader.ReadLine()) != null)
                if (line.Length > 0)
                    saveStrList.Add(line);
        }
        catch
        {
            MessageBox.Show("変数ウォッチリストの読込に失敗しました", "デバッグウインドウ");
            return;
        }
        finally
        {
            if (reader != null)
                reader.Close();
        }

        watchView.Rows.Clear();
        foreach (var str in saveStrList)
        {
            if (!string.IsNullOrEmpty(str))
            {
                watchView.Rows.Add([str, ""]);
            }
        }
        watchView.Columns[0].Width = JSONConfig.User.WatchListWidth[0];
        watchView.Columns[1].Width = JSONConfig.User.WatchListWidth[1];
        watchView.ColumnWidthChanged += (_, a) =>
        {
            JSONConfig.User.WatchListWidth[a.Column.Index] = a.Column.Width;
        };
    }

    private void DebugDialog_Activated(object sender, EventArgs e)
    {
        UpdateData();
    }


    private void updateSize()
    {
        if (this.WindowState == FormWindowState.Minimized)
            return;

        if (tabControlMain.SelectedTab == tabPageConsole)
        {//タブ切り替え直後の時点ではtabPageConsole.Heightは更新されていないのでtabControlMain.Heightより推定するしかない
         //textBoxConsole.Height = tabPageConsole.Height - textBoxCommand.Height - 9;
            textBoxConsole.Height = tabControlMain.Height - 26 - textBoxCommand.Height - 9;
        }
    }

    private void DebugDialog_Resize(object sender, EventArgs e)
    {
        if (this.WindowState == FormWindowState.Minimized)
            return;
        //環境依存かもしれない。誰かに指摘されたら考えよう。
        tabControlMain.Height = this.Size.Height - 103;
        updateSize();
    }


    private void DebugDialog_FormClosing(object sender, FormClosingEventArgs e)
    {
        saveData();
    }

    private void button2_Click(object sender, EventArgs e)
    {
        //これをクリックする時点で情報が最新でないことは普通ないので実はあんまり意味が無い。
        //最新の情報であることを確認するためのボタンってことで
        UpdateData();
    }

    private void textBoxCommand_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Return)
        {
            e.SuppressKeyPress = true;
            if (!mainConsole.IsInProcess && textBoxCommand.Text.Length > 0)
            {
                mainConsole.DebugPrint(textBoxCommand.Text);
                mainConsole.DebugNewLine();
                mainConsole.DebugCommand(textBoxCommand.Text, false, true);
                updateConsole();
                textBoxConsole.SelectionStart = textBoxConsole.Text.Length;
                textBoxConsole.Focus();
                textBoxConsole.ScrollToCaret();
                updateInputs();
                textBoxCommand.Focus();
            }
            return;
        }
        if (e.KeyCode == Keys.Up)
        {
            e.SuppressKeyPress = true;
            if (mainConsole.IsInProcess)
                return;
            movePrev(-1);
            return;
        }
        if (e.KeyCode == Keys.Down)
        {
            e.SuppressKeyPress = true;
            if (mainConsole.IsInProcess)
                return;
            movePrev(1);
            return;
        }
    }
    readonly List<string> history;
    int selectedIndex;
    void updateInputs()
    {
        var input = textBoxCommand.Text;
        if (string.IsNullOrEmpty(input))
            return;

        history.Add(input);
        selectedIndex = history.Count;
        textBoxCommand.Text = "";
    }
    void movePrev(int move)
    {
        selectedIndex += move;
        if (selectedIndex < 0)
        {
            selectedIndex = 0;
        }

        if (selectedIndex < history.Count)
        {
            textBoxCommand.Text = history[selectedIndex];
        }
        else
        {
            selectedIndex = history.Count;
            textBoxCommand.Text = "";
        }

        textBoxCommand.SelectionStart = 0;
        textBoxCommand.SelectionLength = textBoxCommand.Text.Length;


        return;
    }

    private void 設定ToolStripMenuItem1_Click(object sender, EventArgs e)
    {
        bool tempTopMost = TopMost;
        this.TopMost = false;
        DebugConfigDialog dialog = new()
        {
            StartPosition = FormStartPosition.CenterParent
        };
        dialog.SetConfig(this);
        dialog.ShowDialog();
        this.TopMost = tempTopMost;
    }

    private void Localize()
    {
        this.Text = LocalizationManager.DebugDialog.Title;

        this.toolStripMenuItem1.Text = LocalizationManager.MainWindow.File;
        this.ウォッチリストの保存ToolStripMenuItem.Text = LocalizationManager.DebugDialog.File_SaveWatchList;
        this.ウォッチリストの読込ToolStripMenuItem.Text = LocalizationManager.DebugDialog.File_LoadWatchList;
        this.閉じるToolStripMenuItem.Text = LocalizationManager.DebugDialog.Close;

        this.設定ToolStripMenuItem.Text = LocalizationManager.DebugDialog.Setting;
        this.設定ToolStripMenuItem1.Text = LocalizationManager.DebugDialog.Setting_Config;

        this.tabPageWatch.Text = LocalizationManager.DebugDialog.VariableWatch;
        this.watchView.Columns[0].HeaderText = LocalizationManager.DebugDialog.VariableWatch_Object;
        this.watchView.Columns[1].HeaderText = LocalizationManager.DebugDialog.VariableWatch_Value;

        this.tabPageTrace.Text = LocalizationManager.DebugDialog.StackTrace;
        this.tabPageConsole.Text = LocalizationManager.DebugDialog.Console;

        this.checkBoxTopMost.Text = LocalizationManager.DebugDialog.StayOnTop;
        this.button2.Text = LocalizationManager.DebugDialog.UpdateData;
        this.button1.Text = LocalizationManager.DebugDialog.Close;
    }
    private void watchView_CellValueChanged(object sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex >= 0 && e.ColumnIndex == 0)
        {
            var watch = (string)watchView[0, e.RowIndex].Value;
            if (string.IsNullOrEmpty(watch))
            {
                if ((e.RowIndex + 1) != watchView.Rows.Count)
                    watchView.Rows.RemoveAt(e.RowIndex);
            }
            else
            {
                watchView[1, e.RowIndex].Value = getValueString(watch);
            }
        }

    }
}
