using MinorShift.Emuera.UI.Framework.Forms;
namespace MinorShift.Emuera.Forms
{
    partial class MainWindow
    {
        /// <summary>
        /// 必要なデザイナ変数です。
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// 使用中のリソースをすべてクリーンアップします。
        /// </summary>
        /// <param name="disposing">マネージ リソースが破棄される場合 true、破棄されない場合は false です。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows フォーム デザイナで生成されたコード

        /// <summary>
        /// デザイナ サポートに必要なメソッドです。このメソッドの内容を
        /// コード エディタで変更しないでください。
        /// </summary>
        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainWindow));
            vScrollBar = new System.Windows.Forms.VScrollBar();
            menuStrip = new System.Windows.Forms.MenuStrip();
            fileToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            rebootToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            デバッグモードで再起動ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            ログを保存するSToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            ログをクリップボードにコピーToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            タイトルへ戻るTToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            コードを読み直すcToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            フォルダを読み直すFToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            ファイルを読み直すFToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            exitToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            デバッグToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            デバッグウインドウを開くToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            デバッグ情報の更新ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            ツールToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            ウィンドウ幅のロック変更ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            設定ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            EmuVerToolStripTextBox = new System.Windows.Forms.ToolStripTextBox();
            openFileDialog = new System.Windows.Forms.OpenFileDialog();
            saveFileDialog = new System.Windows.Forms.SaveFileDialog();
            folderSelectDialog = new System.Windows.Forms.FolderBrowserDialog();
            richTextBox1 = new System.Windows.Forms.RichTextBox();
            AutoVerbMenu = new System.Windows.Forms.ContextMenuStrip(components);
            マクロToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            マクロ01ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            マクロ02ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            マクロ03ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            マクロ04ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            マクロ05ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            マクロ06ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            マクロ07ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            マクロ08ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            マクロ09ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            マクロ10ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            マクロ11ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            マクロ12ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            マクログループToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            グループ0ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            グループ1ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            グループ2ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            グループ3ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            グループ4ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            グループ5ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            グループ6ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            グループ7ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            グループ8ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            グループ9ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            切り取り = new System.Windows.Forms.ToolStripMenuItem();
            コピー = new System.Windows.Forms.ToolStripMenuItem();
            貼り付け = new System.Windows.Forms.ToolStripMenuItem();
            削除 = new System.Windows.Forms.ToolStripMenuItem();
            toolStripSeparator2 = new System.Windows.Forms.ToolStripSeparator();
            実行 = new System.Windows.Forms.ToolStripMenuItem();
            toolTipButton = new System.Windows.Forms.ToolTip(components);
            timerKeyMacroChanged = new System.Windows.Forms.Timer(components);
            labelMacroGroupChanged = new System.Windows.Forms.Label();
            mainPicBox = new EraPictureBox();
            menuStrip.SuspendLayout();
            AutoVerbMenu.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)mainPicBox).BeginInit();
            SuspendLayout();
            // 
            // vScrollBar
            // 
            vScrollBar.Dock = System.Windows.Forms.DockStyle.Right;
            vScrollBar.Enabled = false;
            vScrollBar.LargeChange = 1;
            vScrollBar.Location = new System.Drawing.Point(968, 48);
            vScrollBar.Maximum = 0;
            vScrollBar.Name = "vScrollBar";
            vScrollBar.Size = new System.Drawing.Size(18, 708);
            vScrollBar.TabIndex = 1;
            vScrollBar.Scroll += vScrollBar_Scroll;
            // 
            // menuStrip
            // 
            menuStrip.GripMargin = new System.Windows.Forms.Padding(3);
            menuStrip.ImageScalingSize = new System.Drawing.Size(24, 24);
            menuStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] { fileToolStripMenuItem, デバッグToolStripMenuItem, ツールToolStripMenuItem, 設定ToolStripMenuItem, EmuVerToolStripTextBox });
            menuStrip.Location = new System.Drawing.Point(0, 0);
            menuStrip.Name = "menuStrip";
            menuStrip.Padding = new System.Windows.Forms.Padding(12, 2, 0, 2);
            menuStrip.Size = new System.Drawing.Size(986, 48);
            menuStrip.TabIndex = 3;
            menuStrip.Text = "menuStrip1";
            // 
            // fileToolStripMenuItem
            // 
            fileToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] { rebootToolStripMenuItem, デバッグモードで再起動ToolStripMenuItem, ログを保存するSToolStripMenuItem, ログをクリップボードにコピーToolStripMenuItem, タイトルへ戻るTToolStripMenuItem, コードを読み直すcToolStripMenuItem, フォルダを読み直すFToolStripMenuItem, ファイルを読み直すFToolStripMenuItem, exitToolStripMenuItem });
            fileToolStripMenuItem.ForeColor = System.Drawing.SystemColors.MenuText;
            fileToolStripMenuItem.Name = "fileToolStripMenuItem";
            fileToolStripMenuItem.Size = new System.Drawing.Size(98, 44);
            fileToolStripMenuItem.Text = "ファイル(&F)";
            // 
            // rebootToolStripMenuItem
            // 
            rebootToolStripMenuItem.Name = "rebootToolStripMenuItem";
            rebootToolStripMenuItem.Size = new System.Drawing.Size(321, 34);
            rebootToolStripMenuItem.Text = "再起動(&R)";
            rebootToolStripMenuItem.Click += rebootToolStripMenuItem_Click;
            // 
            // デバッグモードで再起動ToolStripMenuItem
            // 
            デバッグモードで再起動ToolStripMenuItem.Name = "デバッグモードで再起動ToolStripMenuItem";
            デバッグモードで再起動ToolStripMenuItem.Size = new System.Drawing.Size(321, 34);
            デバッグモードで再起動ToolStripMenuItem.Text = "デバッグモードで再起動";
            デバッグモードで再起動ToolStripMenuItem.Click += デバッグモードで再起動ToolStripMenuItem_Click;
            // 
            // ログを保存するSToolStripMenuItem
            // 
            ログを保存するSToolStripMenuItem.Name = "ログを保存するSToolStripMenuItem";
            ログを保存するSToolStripMenuItem.Size = new System.Drawing.Size(321, 34);
            ログを保存するSToolStripMenuItem.Text = "ログを保存する...(&S)";
            ログを保存するSToolStripMenuItem.Click += ログを保存するSToolStripMenuItem_Click;
            // 
            // ログをクリップボードにコピーToolStripMenuItem
            // 
            ログをクリップボードにコピーToolStripMenuItem.Name = "ログをクリップボードにコピーToolStripMenuItem";
            ログをクリップボードにコピーToolStripMenuItem.Size = new System.Drawing.Size(321, 34);
            ログをクリップボードにコピーToolStripMenuItem.Text = "ログをクリップボードにコピー(&C)";
            ログをクリップボードにコピーToolStripMenuItem.Click += ログをクリップボードにコピーToolStripMenuItem_Click;
            // 
            // タイトルへ戻るTToolStripMenuItem
            // 
            タイトルへ戻るTToolStripMenuItem.Name = "タイトルへ戻るTToolStripMenuItem";
            タイトルへ戻るTToolStripMenuItem.Size = new System.Drawing.Size(321, 34);
            タイトルへ戻るTToolStripMenuItem.Text = "タイトル画面へ戻る(&T)";
            タイトルへ戻るTToolStripMenuItem.Click += タイトルへ戻るTToolStripMenuItem_Click;
            // 
            // コードを読み直すcToolStripMenuItem
            // 
            コードを読み直すcToolStripMenuItem.Name = "コードを読み直すcToolStripMenuItem";
            コードを読み直すcToolStripMenuItem.Size = new System.Drawing.Size(321, 34);
            コードを読み直すcToolStripMenuItem.Text = "全コードを読み直す(&C)";
            コードを読み直すcToolStripMenuItem.Click += コードを読み直すcToolStripMenuItem_Click;
            // 
            // フォルダを読み直すFToolStripMenuItem
            // 
            フォルダを読み直すFToolStripMenuItem.Name = "フォルダを読み直すFToolStripMenuItem";
            フォルダを読み直すFToolStripMenuItem.Size = new System.Drawing.Size(321, 34);
            フォルダを読み直すFToolStripMenuItem.Text = "フォルダを読み直す(&F)";
            フォルダを読み直すFToolStripMenuItem.Click += フォルダを読み直すFToolStripMenuItem_Click;
            // 
            // ファイルを読み直すFToolStripMenuItem
            // 
            ファイルを読み直すFToolStripMenuItem.Name = "ファイルを読み直すFToolStripMenuItem";
            ファイルを読み直すFToolStripMenuItem.Size = new System.Drawing.Size(321, 34);
            ファイルを読み直すFToolStripMenuItem.Text = "ファイルを読み直す(&A)";
            ファイルを読み直すFToolStripMenuItem.Click += ファイルを読み直すFToolStripMenuItem_Click;
            // 
            // exitToolStripMenuItem
            // 
            exitToolStripMenuItem.Name = "exitToolStripMenuItem";
            exitToolStripMenuItem.Size = new System.Drawing.Size(321, 34);
            exitToolStripMenuItem.Text = "終了(&X)";
            exitToolStripMenuItem.Click += exitToolStripMenuItem_Click;
            // 
            // デバッグToolStripMenuItem
            // 
            デバッグToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] { デバッグウインドウを開くToolStripMenuItem, デバッグ情報の更新ToolStripMenuItem });
            デバッグToolStripMenuItem.ForeColor = System.Drawing.SystemColors.MenuText;
            デバッグToolStripMenuItem.Name = "デバッグToolStripMenuItem";
            デバッグToolStripMenuItem.Size = new System.Drawing.Size(106, 44);
            デバッグToolStripMenuItem.Text = "デバッグ(&D)";
            デバッグToolStripMenuItem.Visible = false;
            // 
            // デバッグウインドウを開くToolStripMenuItem
            // 
            デバッグウインドウを開くToolStripMenuItem.Name = "デバッグウインドウを開くToolStripMenuItem";
            デバッグウインドウを開くToolStripMenuItem.ShortcutKeys = System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.D;
            デバッグウインドウを開くToolStripMenuItem.Size = new System.Drawing.Size(341, 34);
            デバッグウインドウを開くToolStripMenuItem.Text = "デバッグウインドウを開く";
            デバッグウインドウを開くToolStripMenuItem.Click += デバッグウインドウを開くToolStripMenuItem_Click;
            // 
            // デバッグ情報の更新ToolStripMenuItem
            // 
            デバッグ情報の更新ToolStripMenuItem.Name = "デバッグ情報の更新ToolStripMenuItem";
            デバッグ情報の更新ToolStripMenuItem.ShortcutKeys = System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.R;
            デバッグ情報の更新ToolStripMenuItem.Size = new System.Drawing.Size(341, 34);
            デバッグ情報の更新ToolStripMenuItem.Text = "デバッグ情報の更新";
            デバッグ情報の更新ToolStripMenuItem.Click += デバッグ情報の更新ToolStripMenuItem_Click;
            // 
            // ツールToolStripMenuItem
            // 
            ツールToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] { ウィンドウ幅のロック変更ToolStripMenuItem });
            ツールToolStripMenuItem.ForeColor = System.Drawing.SystemColors.MenuText;
            ツールToolStripMenuItem.Name = "ツールToolStripMenuItem";
            ツールToolStripMenuItem.Size = new System.Drawing.Size(68, 44);
            ツールToolStripMenuItem.Text = "ツール";
            // 
            // ウィンドウ幅のロック変更ToolStripMenuItem
            // 
            ウィンドウ幅のロック変更ToolStripMenuItem.Name = "ウィンドウ幅のロック変更ToolStripMenuItem";
            ウィンドウ幅のロック変更ToolStripMenuItem.Size = new System.Drawing.Size(285, 34);
            ウィンドウ幅のロック変更ToolStripMenuItem.Text = "ウィンドウ幅のロック変更";
            ウィンドウ幅のロック変更ToolStripMenuItem.Click += ウィンドウ幅のロック変更ToolStripMenuItem_Click;
            // 
            // 設定ToolStripMenuItem
            // 
            設定ToolStripMenuItem.ForeColor = System.Drawing.SystemColors.MenuText;
            設定ToolStripMenuItem.Name = "設定ToolStripMenuItem";
            設定ToolStripMenuItem.Size = new System.Drawing.Size(84, 44);
            設定ToolStripMenuItem.Text = "設定(&S)";
            設定ToolStripMenuItem.Click += コンフィグCToolStripMenuItem_Click;
            // 
            // EmuVerToolStripTextBox
            // 
            EmuVerToolStripTextBox.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            EmuVerToolStripTextBox.BackColor = System.Drawing.Color.WhiteSmoke;
            EmuVerToolStripTextBox.BorderStyle = System.Windows.Forms.BorderStyle.None;
            EmuVerToolStripTextBox.Font = new System.Drawing.Font("Yu Gothic UI", 16.2F);
            EmuVerToolStripTextBox.ForeColor = System.Drawing.SystemColors.GrayText;
            EmuVerToolStripTextBox.Margin = new System.Windows.Forms.Padding(5, 0, 5, 0);
            EmuVerToolStripTextBox.Name = "EmuVerToolStripTextBox";
            EmuVerToolStripTextBox.ReadOnly = true;
            EmuVerToolStripTextBox.ShortcutsEnabled = false;
            EmuVerToolStripTextBox.Size = new System.Drawing.Size(600, 44);
            EmuVerToolStripTextBox.Text = "Emuera Ver. 0.000+v00.0";
            EmuVerToolStripTextBox.TextBoxTextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            // 
            // openFileDialog
            // 
            openFileDialog.FileName = "save99.sav";
            openFileDialog.Filter = "save files (save*.sav)|save*.sav|All files (*.*)|*.*";
            // 
            // saveFileDialog
            // 
            saveFileDialog.Filter = "log files (*.log)|*.log|All files (*.*)|*.*";
            // 
            // folderSelectDialog
            // 
            folderSelectDialog.Description = "読み直すフォルダを選択してください";
            // 
            // richTextBox1
            // 
            richTextBox1.BackColor = System.Drawing.SystemColors.Window;
            richTextBox1.BorderStyle = System.Windows.Forms.BorderStyle.None;
            richTextBox1.ContextMenuStrip = AutoVerbMenu;
            richTextBox1.DetectUrls = false;
            richTextBox1.Dock = System.Windows.Forms.DockStyle.Bottom;
            richTextBox1.Font = new System.Drawing.Font("ＭＳ ゴシック", 32.4F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Pixel);
            richTextBox1.Location = new System.Drawing.Point(0, 730);
            richTextBox1.Margin = new System.Windows.Forms.Padding(0);
            richTextBox1.MaxLength = 32767;
            richTextBox1.Multiline = false;
            richTextBox1.Name = "richTextBox1";
            richTextBox1.Size = new System.Drawing.Size(968, 26);
            richTextBox1.TabIndex = 4;
            richTextBox1.Text = "";
            richTextBox1.TextChanged += richTextBox1_TextChanged;
            richTextBox1.KeyDown += richTextBox1_KeyDown;
            // 
            // AutoVerbMenu
            // 
            AutoVerbMenu.ImageScalingSize = new System.Drawing.Size(20, 20);
            AutoVerbMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] { マクロToolStripMenuItem, マクログループToolStripMenuItem, toolStripSeparator1, 切り取り, コピー, 貼り付け, 削除, toolStripSeparator2, 実行 });
            AutoVerbMenu.Name = "AutoVerbMenu";
            AutoVerbMenu.ShowImageMargin = false;
            AutoVerbMenu.Size = new System.Drawing.Size(184, 240);
            AutoVerbMenu.Opened += AutoVerbMenu_Opened;
            // 
            // マクロToolStripMenuItem
            // 
            マクロToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] { マクロ01ToolStripMenuItem, マクロ02ToolStripMenuItem, マクロ03ToolStripMenuItem, マクロ04ToolStripMenuItem, マクロ05ToolStripMenuItem, マクロ06ToolStripMenuItem, マクロ07ToolStripMenuItem, マクロ08ToolStripMenuItem, マクロ09ToolStripMenuItem, マクロ10ToolStripMenuItem, マクロ11ToolStripMenuItem, マクロ12ToolStripMenuItem });
            マクロToolStripMenuItem.Name = "マクロToolStripMenuItem";
            マクロToolStripMenuItem.Size = new System.Drawing.Size(183, 32);
            マクロToolStripMenuItem.Text = "マクロ";
            // 
            // マクロ01ToolStripMenuItem
            // 
            マクロ01ToolStripMenuItem.Name = "マクロ01ToolStripMenuItem";
            マクロ01ToolStripMenuItem.ShortcutKeys = System.Windows.Forms.Keys.F1;
            マクロ01ToolStripMenuItem.Size = new System.Drawing.Size(216, 34);
            マクロ01ToolStripMenuItem.Tag = "";
            マクロ01ToolStripMenuItem.Text = "マクロ01";
            // 
            // マクロ02ToolStripMenuItem
            // 
            マクロ02ToolStripMenuItem.Name = "マクロ02ToolStripMenuItem";
            マクロ02ToolStripMenuItem.ShortcutKeys = System.Windows.Forms.Keys.F2;
            マクロ02ToolStripMenuItem.Size = new System.Drawing.Size(216, 34);
            マクロ02ToolStripMenuItem.Text = "マクロ02";
            // 
            // マクロ03ToolStripMenuItem
            // 
            マクロ03ToolStripMenuItem.Name = "マクロ03ToolStripMenuItem";
            マクロ03ToolStripMenuItem.ShortcutKeys = System.Windows.Forms.Keys.F3;
            マクロ03ToolStripMenuItem.Size = new System.Drawing.Size(216, 34);
            マクロ03ToolStripMenuItem.Text = "マクロ03";
            // 
            // マクロ04ToolStripMenuItem
            // 
            マクロ04ToolStripMenuItem.Name = "マクロ04ToolStripMenuItem";
            マクロ04ToolStripMenuItem.ShortcutKeys = System.Windows.Forms.Keys.F4;
            マクロ04ToolStripMenuItem.Size = new System.Drawing.Size(216, 34);
            マクロ04ToolStripMenuItem.Text = "マクロ04";
            // 
            // マクロ05ToolStripMenuItem
            // 
            マクロ05ToolStripMenuItem.Name = "マクロ05ToolStripMenuItem";
            マクロ05ToolStripMenuItem.ShortcutKeys = System.Windows.Forms.Keys.F5;
            マクロ05ToolStripMenuItem.Size = new System.Drawing.Size(216, 34);
            マクロ05ToolStripMenuItem.Text = "マクロ05";
            // 
            // マクロ06ToolStripMenuItem
            // 
            マクロ06ToolStripMenuItem.Name = "マクロ06ToolStripMenuItem";
            マクロ06ToolStripMenuItem.ShortcutKeys = System.Windows.Forms.Keys.F6;
            マクロ06ToolStripMenuItem.Size = new System.Drawing.Size(216, 34);
            マクロ06ToolStripMenuItem.Text = "マクロ06";
            // 
            // マクロ07ToolStripMenuItem
            // 
            マクロ07ToolStripMenuItem.Name = "マクロ07ToolStripMenuItem";
            マクロ07ToolStripMenuItem.ShortcutKeys = System.Windows.Forms.Keys.F7;
            マクロ07ToolStripMenuItem.Size = new System.Drawing.Size(216, 34);
            マクロ07ToolStripMenuItem.Text = "マクロ07";
            // 
            // マクロ08ToolStripMenuItem
            // 
            マクロ08ToolStripMenuItem.Name = "マクロ08ToolStripMenuItem";
            マクロ08ToolStripMenuItem.ShortcutKeys = System.Windows.Forms.Keys.F8;
            マクロ08ToolStripMenuItem.Size = new System.Drawing.Size(216, 34);
            マクロ08ToolStripMenuItem.Text = "マクロ08";
            // 
            // マクロ09ToolStripMenuItem
            // 
            マクロ09ToolStripMenuItem.Name = "マクロ09ToolStripMenuItem";
            マクロ09ToolStripMenuItem.ShortcutKeys = System.Windows.Forms.Keys.F9;
            マクロ09ToolStripMenuItem.Size = new System.Drawing.Size(216, 34);
            マクロ09ToolStripMenuItem.Text = "マクロ09";
            // 
            // マクロ10ToolStripMenuItem
            // 
            マクロ10ToolStripMenuItem.Name = "マクロ10ToolStripMenuItem";
            マクロ10ToolStripMenuItem.ShortcutKeys = System.Windows.Forms.Keys.F10;
            マクロ10ToolStripMenuItem.Size = new System.Drawing.Size(216, 34);
            マクロ10ToolStripMenuItem.Text = "マクロ10";
            // 
            // マクロ11ToolStripMenuItem
            // 
            マクロ11ToolStripMenuItem.Name = "マクロ11ToolStripMenuItem";
            マクロ11ToolStripMenuItem.ShortcutKeys = System.Windows.Forms.Keys.F11;
            マクロ11ToolStripMenuItem.Size = new System.Drawing.Size(216, 34);
            マクロ11ToolStripMenuItem.Text = "マクロ11";
            // 
            // マクロ12ToolStripMenuItem
            // 
            マクロ12ToolStripMenuItem.Name = "マクロ12ToolStripMenuItem";
            マクロ12ToolStripMenuItem.ShortcutKeys = System.Windows.Forms.Keys.F12;
            マクロ12ToolStripMenuItem.Size = new System.Drawing.Size(216, 34);
            マクロ12ToolStripMenuItem.Text = "マクロ12";
            // 
            // マクログループToolStripMenuItem
            // 
            マクログループToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] { グループ0ToolStripMenuItem, グループ1ToolStripMenuItem, グループ2ToolStripMenuItem, グループ3ToolStripMenuItem, グループ4ToolStripMenuItem, グループ5ToolStripMenuItem, グループ6ToolStripMenuItem, グループ7ToolStripMenuItem, グループ8ToolStripMenuItem, グループ9ToolStripMenuItem });
            マクログループToolStripMenuItem.Name = "マクログループToolStripMenuItem";
            マクログループToolStripMenuItem.Size = new System.Drawing.Size(183, 32);
            マクログループToolStripMenuItem.Text = "マクログループ";
            // 
            // グループ0ToolStripMenuItem
            // 
            グループ0ToolStripMenuItem.Name = "グループ0ToolStripMenuItem";
            グループ0ToolStripMenuItem.Size = new System.Drawing.Size(179, 34);
            グループ0ToolStripMenuItem.Tag = "0";
            グループ0ToolStripMenuItem.Text = "グループ0";
            グループ0ToolStripMenuItem.Click += グループToolStripMenuItem_Click;
            // 
            // グループ1ToolStripMenuItem
            // 
            グループ1ToolStripMenuItem.Name = "グループ1ToolStripMenuItem";
            グループ1ToolStripMenuItem.Size = new System.Drawing.Size(179, 34);
            グループ1ToolStripMenuItem.Tag = "1";
            グループ1ToolStripMenuItem.Text = "グループ1";
            グループ1ToolStripMenuItem.Click += グループToolStripMenuItem_Click;
            // 
            // グループ2ToolStripMenuItem
            // 
            グループ2ToolStripMenuItem.Name = "グループ2ToolStripMenuItem";
            グループ2ToolStripMenuItem.Size = new System.Drawing.Size(179, 34);
            グループ2ToolStripMenuItem.Tag = "2";
            グループ2ToolStripMenuItem.Text = "グループ2";
            グループ2ToolStripMenuItem.Click += グループToolStripMenuItem_Click;
            // 
            // グループ3ToolStripMenuItem
            // 
            グループ3ToolStripMenuItem.Name = "グループ3ToolStripMenuItem";
            グループ3ToolStripMenuItem.Size = new System.Drawing.Size(179, 34);
            グループ3ToolStripMenuItem.Tag = "3";
            グループ3ToolStripMenuItem.Text = "グループ3";
            グループ3ToolStripMenuItem.Click += グループToolStripMenuItem_Click;
            // 
            // グループ4ToolStripMenuItem
            // 
            グループ4ToolStripMenuItem.Name = "グループ4ToolStripMenuItem";
            グループ4ToolStripMenuItem.Size = new System.Drawing.Size(179, 34);
            グループ4ToolStripMenuItem.Tag = "4";
            グループ4ToolStripMenuItem.Text = "グループ4";
            グループ4ToolStripMenuItem.Click += グループToolStripMenuItem_Click;
            // 
            // グループ5ToolStripMenuItem
            // 
            グループ5ToolStripMenuItem.Name = "グループ5ToolStripMenuItem";
            グループ5ToolStripMenuItem.Size = new System.Drawing.Size(179, 34);
            グループ5ToolStripMenuItem.Tag = "5";
            グループ5ToolStripMenuItem.Text = "グループ5";
            グループ5ToolStripMenuItem.Click += グループToolStripMenuItem_Click;
            // 
            // グループ6ToolStripMenuItem
            // 
            グループ6ToolStripMenuItem.Name = "グループ6ToolStripMenuItem";
            グループ6ToolStripMenuItem.Size = new System.Drawing.Size(179, 34);
            グループ6ToolStripMenuItem.Tag = "6";
            グループ6ToolStripMenuItem.Text = "グループ6";
            グループ6ToolStripMenuItem.Click += グループToolStripMenuItem_Click;
            // 
            // グループ7ToolStripMenuItem
            // 
            グループ7ToolStripMenuItem.Name = "グループ7ToolStripMenuItem";
            グループ7ToolStripMenuItem.Size = new System.Drawing.Size(179, 34);
            グループ7ToolStripMenuItem.Tag = "7";
            グループ7ToolStripMenuItem.Text = "グループ7";
            グループ7ToolStripMenuItem.Click += グループToolStripMenuItem_Click;
            // 
            // グループ8ToolStripMenuItem
            // 
            グループ8ToolStripMenuItem.Name = "グループ8ToolStripMenuItem";
            グループ8ToolStripMenuItem.Size = new System.Drawing.Size(179, 34);
            グループ8ToolStripMenuItem.Tag = "8";
            グループ8ToolStripMenuItem.Text = "グループ8";
            グループ8ToolStripMenuItem.Click += グループToolStripMenuItem_Click;
            // 
            // グループ9ToolStripMenuItem
            // 
            グループ9ToolStripMenuItem.Name = "グループ9ToolStripMenuItem";
            グループ9ToolStripMenuItem.Size = new System.Drawing.Size(179, 34);
            グループ9ToolStripMenuItem.Tag = "9";
            グループ9ToolStripMenuItem.Text = "グループ9";
            グループ9ToolStripMenuItem.Click += グループToolStripMenuItem_Click;
            // 
            // toolStripSeparator1
            // 
            toolStripSeparator1.Name = "toolStripSeparator1";
            toolStripSeparator1.Size = new System.Drawing.Size(180, 6);
            // 
            // 切り取り
            // 
            切り取り.Enabled = false;
            切り取り.Name = "切り取り";
            切り取り.ShortcutKeys = System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.X;
            切り取り.Size = new System.Drawing.Size(183, 32);
            切り取り.Text = "切り取り";
            切り取り.Click += 切り取り_Click;
            // 
            // コピー
            // 
            コピー.Enabled = false;
            コピー.Name = "コピー";
            コピー.ShortcutKeys = System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.C;
            コピー.Size = new System.Drawing.Size(183, 32);
            コピー.Text = "コピー";
            コピー.Click += コピー_Click;
            // 
            // 貼り付け
            // 
            貼り付け.Enabled = false;
            貼り付け.Name = "貼り付け";
            貼り付け.ShortcutKeys = System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.V;
            貼り付け.Size = new System.Drawing.Size(183, 32);
            貼り付け.Text = "貼り付け";
            貼り付け.Click += 貼り付け_Click;
            // 
            // 削除
            // 
            削除.Enabled = false;
            削除.Name = "削除";
            削除.ShortcutKeys = System.Windows.Forms.Keys.Delete;
            削除.Size = new System.Drawing.Size(183, 32);
            削除.Text = "削除";
            削除.Click += 削除_Click;
            // 
            // toolStripSeparator2
            // 
            toolStripSeparator2.Name = "toolStripSeparator2";
            toolStripSeparator2.Size = new System.Drawing.Size(180, 6);
            // 
            // 実行
            // 
            実行.Enabled = false;
            実行.Name = "実行";
            実行.Size = new System.Drawing.Size(183, 32);
            実行.Text = "実行";
            実行.Click += 実行_Click;
            // 
            // toolTipButton
            // 
            toolTipButton.BackColor = System.Drawing.Color.White;
            toolTipButton.OwnerDraw = true;
            toolTipButton.Draw += toolTipButton_Draw;
            toolTipButton.Popup += toolTipButton_Popup;
            // 
            // timerKeyMacroChanged
            // 
            timerKeyMacroChanged.Tick += timerKeyMacroChanged_Tick;
            // 
            // labelMacroGroupChanged
            // 
            labelMacroGroupChanged.AutoSize = true;
            labelMacroGroupChanged.BackColor = System.Drawing.Color.Black;
            labelMacroGroupChanged.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            labelMacroGroupChanged.Location = new System.Drawing.Point(0, 672);
            labelMacroGroupChanged.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            labelMacroGroupChanged.Name = "labelMacroGroupChanged";
            labelMacroGroupChanged.Size = new System.Drawing.Size(61, 27);
            labelMacroGroupChanged.TabIndex = 5;
            labelMacroGroupChanged.Text = "label1";
            labelMacroGroupChanged.Visible = false;
            // 
            // mainPicBox
            // 
            mainPicBox.BackColor = System.Drawing.Color.Black;
            mainPicBox.Dock = System.Windows.Forms.DockStyle.Fill;
            mainPicBox.Location = new System.Drawing.Point(0, 48);
            mainPicBox.Margin = new System.Windows.Forms.Padding(0);
            mainPicBox.Name = "mainPicBox";
            mainPicBox.Size = new System.Drawing.Size(968, 682);
            mainPicBox.TabIndex = 0;
            mainPicBox.TabStop = false;
            mainPicBox.Paint += mainPicBox_Paint;
            mainPicBox.MouseDown += mainPicBox_MouseDown;
            mainPicBox.MouseLeave += mainPicBox_MouseLeave;
            mainPicBox.MouseMove += mainPicBox_MouseMove;
            // 
            // MainWindow
            // 
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Inherit;
            BackColor = System.Drawing.Color.Black;
            ClientSize = new System.Drawing.Size(986, 756);
            Controls.Add(labelMacroGroupChanged);
            Controls.Add(mainPicBox);
            Controls.Add(richTextBox1);
            Controls.Add(vScrollBar);
            Controls.Add(menuStrip);
            DoubleBuffered = true;
            ForeColor = System.Drawing.SystemColors.HighlightText;
            Icon = (System.Drawing.Icon)resources.GetObject("$this.Icon");
            MainMenuStrip = menuStrip;
            Margin = new System.Windows.Forms.Padding(4);
            Name = "MainWindow";
            SizeGripStyle = System.Windows.Forms.SizeGripStyle.Hide;
            Text = "Emuera";
            FormClosing += MainWindow_FormClosing;
            Shown += Init;
            menuStrip.ResumeLayout(false);
            menuStrip.PerformLayout();
            AutoVerbMenu.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)mainPicBox).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private System.Windows.Forms.VScrollBar vScrollBar;
        private System.Windows.Forms.MenuStrip menuStrip;
        private System.Windows.Forms.ToolStripMenuItem fileToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem rebootToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem exitToolStripMenuItem;
        private System.Windows.Forms.OpenFileDialog openFileDialog;
        private System.Windows.Forms.ToolStripMenuItem 設定ToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem タイトルへ戻るTToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem コードを読み直すcToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem ログを保存するSToolStripMenuItem;
        private System.Windows.Forms.SaveFileDialog saveFileDialog;
        private EraPictureBox mainPicBox;
        private System.Windows.Forms.ToolStripMenuItem ログをクリップボードにコピーToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem ファイルを読み直すFToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem フォルダを読み直すFToolStripMenuItem;
        private System.Windows.Forms.FolderBrowserDialog folderSelectDialog;
        private System.Windows.Forms.RichTextBox richTextBox1;
        private System.Windows.Forms.ToolStripMenuItem デバッグToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem デバッグウインドウを開くToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem デバッグ情報の更新ToolStripMenuItem;
        private System.Windows.Forms.ContextMenuStrip AutoVerbMenu;
        private System.Windows.Forms.ToolStripMenuItem 切り取り;
        private System.Windows.Forms.ToolStripMenuItem コピー;
        private System.Windows.Forms.ToolStripMenuItem 貼り付け;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
        private System.Windows.Forms.ToolStripMenuItem 実行;
        private System.Windows.Forms.ToolStripMenuItem マクロToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem マクロ01ToolStripMenuItem;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator2;
        private System.Windows.Forms.ToolStripMenuItem マクロ02ToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem マクロ03ToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem マクロ04ToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem マクロ05ToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem マクロ06ToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem マクロ07ToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem マクロ08ToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem マクロ09ToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem マクロ10ToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem マクロ11ToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem マクロ12ToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem 削除;
        private System.Windows.Forms.ToolTip toolTipButton;
        private System.Windows.Forms.Timer timerKeyMacroChanged;
        private System.Windows.Forms.Label labelMacroGroupChanged;
        private System.Windows.Forms.ToolStripMenuItem マクログループToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem グループ0ToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem グループ1ToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem グループ2ToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem グループ3ToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem グループ4ToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem グループ5ToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem グループ6ToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem グループ7ToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem グループ8ToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem グループ9ToolStripMenuItem;
        private System.Windows.Forms.ToolStripTextBox EmuVerToolStripTextBox;
        private System.Windows.Forms.ToolStripMenuItem ツールToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem ウィンドウ幅のロック変更ToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem デバッグモードで再起動ToolStripMenuItem;
    }
}

