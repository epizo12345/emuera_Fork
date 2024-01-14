using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using System.IO;
using MinorShift._Library;
using MinorShift.Emuera.Sub;
using MinorShift.Emuera.GameData;
using MinorShift.Emuera.GameProc;
using MinorShift.Emuera.GameView;
using MinorShift.Emuera.Forms;
using System.Runtime.Versioning;

#nullable enable

namespace MinorShift.Emuera
{
	internal sealed partial class MainWindow : Form
	{
		readonly FormWindowState _rebootWinState;
		Action<MainWindow> _rebootCallback;

		public MainWindow(FormWindowState formWindowState, Point windowLocation, int windowHeight, Action<MainWindow> rebootCallback)
		{
			InitializeComponent();
			_rebootWinState = formWindowState;
			_rebootCallback = rebootCallback;

			if (Program.DebugMode)
				デバッグToolStripMenuItem.Visible = true;

			((EraPictureBox)mainPicBox).SetStyle();
			initControlSizeAndLocation(windowLocation, windowHeight);
			richTextBox1.ForeColor = Config.ForeColor;
			richTextBox1.BackColor = Config.BackColor;
			mainPicBox.BackColor = Config.BackColor;//これは実際には使用されないはず

			BackColor = Config.BackColor;

			richTextBox1.Font = Config.Font;
			richTextBox1.LanguageOption = RichTextBoxLanguageOptions.UIFonts;
			folderSelectDialog.SelectedPath = Program.ErbDir;
			folderSelectDialog.ShowNewFolderButton = false;

			openFileDialog.InitialDirectory = Program.ErbDir;
			openFileDialog.Filter = "ERBファイル (*.erb)|*.erb";
			openFileDialog.FileName = "";
			openFileDialog.Multiselect = true;
			openFileDialog.RestoreDirectory = true;

			string Emuera_verInfo = "Emuera Ver. " + emueraVer;
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
		}
		private readonly ToolStripMenuItem[] macroMenuItems = new ToolStripMenuItem[KeyMacro.MaxFkey];
		private readonly Version emueraVer = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
		public PictureBox MainPicBox { get { return mainPicBox; } }
		public VScrollBar ScrollBar { get { return vScrollBar; } }
		public RichTextBox TextBox { get { return richTextBox1; } }
		public string InternalEmueraVer { get { return emueraVer.ToString(); } }
		public string EmueraVerText { get { return EmuVerToolStripTextBox.Text; } }
		public ToolTip ToolTip { get { return toolTipButton; } }
		private EmueraConsole console;

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
				if (richTextBox1.SelectedText == "")
				{
					ClipBoardDialog dialog = new()
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
			else if (Config.UseKeyMacro)
			{
				int keyCode = (int)(keyData & Keys.KeyCode);
				bool shiftPressed = (keyData & Keys.Modifiers) == Keys.Shift;
				bool ctrlPressed = (keyData & Keys.Modifiers) == Keys.Control;
				bool unPressed = (int)(keyData & Keys.Modifiers) == 0;
				if (keyCode >= (int)Keys.F1 && keyCode <= (int)Keys.F12)
				{
					int macroNum = keyCode - (int)Keys.F1;
					if (shiftPressed)
					{
						if (richTextBox1.Text != "")
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
			const int WM_SYSCOMMAND = 0x112;
			//const int WM_MOUSEWHEEL = 0x020A;
			const int SC_MOVE = 0xf010;
			const int SC_MAXIMIZE = 0xf030;

			// WM_SYSCOMMAND (SC_MOVE) を無視することでフォームを移動できないようにする
			switch (m.Msg)
			{
				case WM_SYSCOMMAND:
					{
						int wparam = m.WParam.ToInt32() & 0xfff0;
						switch (wparam)
						{
							case SC_MOVE:
								if (WindowState == FormWindowState.Maximized)
									return;
								break;
							case SC_MAXIMIZE:
								if (Screen.AllScreens.Length == 1)
								{
									MaximizedBounds = new Rectangle(Left, 0, Config.WindowX, Screen.PrimaryScreen.WorkingArea.Height);
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

		private void Init(object sender, EventArgs e)
		{
			if (!Created)
				throw new Exception("初期化の呼び出しが早すぎて、コントロールが生成されていない");
			console.Initialize();
		}

		/// <summary>
		/// 1819 リサイズ時の処理を全廃しAnchor&Dock処理にマルナゲ
		/// 初期設定のみここで行う。ついでに再起動時の位置・サイズ処理も追加
		/// </summary>
		private void initControlSizeAndLocation(Point windowLocation, int windowHeight)
		{
			//Windowのサイズ設定
			int winWidth = Config.WindowX + vScrollBar.Width;
			int winHeight = Config.WindowY;
			bool winMaximize = false;
			if (Config.SizableWindow)
			{
				FormBorderStyle = FormBorderStyle.Sizable;
				MaximizeBox = true;
				winMaximize = Config.WindowMaximixed || _rebootWinState == FormWindowState.Maximized;
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
			else if (!winMaximize && windowLocation != new Point())
			{
				StartPosition = FormStartPosition.Manual;
				Location = windowLocation;
			}
			//Windowのサイズ設定・再起動時
			if (!winMaximize && (windowHeight > 0))
				winHeight = windowHeight;
			ClientSize = new Size(winWidth, winHeight);

			//EmuVerToolStripTextBox.Location = new Point(Config.WindowX - vScrollBar.Width - EmuVerToolStripTextBox.Width, 3);

			mainPicBox.Location = new Point(0, menuHeight);
			mainPicBox.Size = new Size(Config.WindowX, winHeight - menuHeight - Config.LineHeight);

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

		bool changeTextbyMouse = false;
		private void mainPicBox_MouseDown(object sender, MouseEventArgs e)
		{
			if (!Config.UseMouse)
				return;
			if (console == null || console.IsInProcess)
				return;
			if (console.IsWaitingPrimitive)
			//			if (console.IsWaitingPrimitiveMouse)
			{
				console.MouseDown(e.Location, e.Button);
				return;
			}
			bool isBacklog = vScrollBar.Value != vScrollBar.Maximum;
			string str = console.SelectedString;

			if (isBacklog)
				if ((e.Button == MouseButtons.Left) || (e.Button == MouseButtons.Right))
				{
					vScrollBar.Value = vScrollBar.Maximum;
					console.RefreshStrings(true);
				}
			if (console.IsWaitingEnterKey && !console.IsError && str == null)
			{
				if (isBacklog)
					return;
				if ((e.Button == MouseButtons.Left) || (e.Button == MouseButtons.Right))
				{
					if (e.Button == MouseButtons.Right)
						PressEnterKey(true, true);
					else
						PressEnterKey(false, true);
					return;
				}
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
			if (selectedInputs == prevInputs.Length || cur != prevInputs[prevInputs.Length - 1])
			{
				for (int i = 0; i < prevInputs.Length - 1; i++)
				{
					prevInputs[i] = prevInputs[i + 1];
				}
				prevInputs[prevInputs.Length - 1] = cur;
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
			var result = System.Windows.MessageBox.Show("ゲームを終了します", "終了", System.Windows.MessageBoxButton.OKCancel);
			if (result != System.Windows.MessageBoxResult.OK)
				return;
			Close();

		}

		private void rebootToolStripMenuItem_Click(object sender, EventArgs e)
		{
			var result = System.Windows.MessageBox.Show("ゲームを再起動します", "再起動", System.Windows.MessageBoxButton.OKCancel);
			if (result != System.Windows.MessageBoxResult.OK)
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

		public void Reboot()
		{
			console.forceStopTimer();
			_rebootCallback(this);
			Close();
		}

		public void GotoTitle()
		{
			if (console == null)
				return;
			console.GotoTitle();
		}

		public void ReloadErb()
		{
			if (console == null)
				return;
			console.ReloadErb();
		}

		private void mainPicBox_MouseLeave(object sender, EventArgs e)
		{
			if (console == null)
				return;
			if (Config.UseMouse)
				console.LeaveMouse();
		}

		private void コンフィグCToolStripMenuItem_Click(object sender, EventArgs e)
		{
			ShowConfigDialog();
		}

		public void ShowConfigDialog()
		{

			ConfigDialog dialog = new()
			{
				StartPosition = FormStartPosition.CenterParent
			};
			dialog.SetConfig(this);
			dialog.ShowDialog();
			if (dialog.Result == ConfigDialogResult.SaveReboot)
			{
				console.forceStopTimer();
				_rebootCallback(this);
				Close();
			}
		}

		private void タイトルへ戻るTToolStripMenuItem_Click(object sender, EventArgs e)
		{
			if (console == null)
				return;
			if (console.IsInProcess)
			{
				System.Windows.MessageBox.Show("スクリプト動作中には使用できません");
				return;
			}
			if (console.notToTitle)
			{
				if (console.byError)
					System.Windows.MessageBox.Show("コード解析でエラーが発見されたため、タイトルへは飛べません");
				else
					System.Windows.MessageBox.Show("解析モードのためタイトルへは飛べません");
				return;
			}
			var result = System.Windows.MessageBox.Show("タイトル画面へ戻ります", "タイトル画面に戻る", System.Windows.MessageBoxButton.OKCancel);
			if (result != System.Windows.MessageBoxResult.OK)
				return;
			GotoTitle();
		}

		private void コードを読み直すcToolStripMenuItem_Click(object sender, EventArgs e)
		{
			if (console == null)
				return;
			if (console.IsInProcess)
			{
				System.Windows.MessageBox.Show("スクリプト動作中には使用できません");
				return;
			}
			var result = System.Windows.MessageBox.Show("ERBファイルを読み直します", "ERBファイル読み直し", System.Windows.MessageBoxButton.OKCancel);
			if (result != System.Windows.MessageBoxResult.OK)
				return;
			ReloadErb();

		}

		private void mainPicBox_Paint(object sender, PaintEventArgs e)
		{
			if (console == null)
				return;
			console.OnPaint(e.Graphics);
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
				ClipBoardDialog dialog = new();
				dialog.Setup(console);
				dialog.ShowDialog();
			}
			catch (Exception)
			{
				System.Windows.MessageBox.Show("予期せぬエラーが発生したためクリップボードを開けません");
				return;
			}
		}

		private void ファイルを読み直すFToolStripMenuItem_Click(object sender, EventArgs e)
		{
			if (console == null)
				return;
			if (console.IsInProcess)
			{
				System.Windows.MessageBox.Show("スクリプト動作中には使用できません");
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
						System.Windows.MessageBox.Show("ファイルがありません", "File Not Found");
						return;
					}
					if (Path.GetExtension(fname).ToUpper() != ".ERB")
					{
						System.Windows.MessageBox.Show("ERBファイル以外は読み込めません", "ファイル形式エラー");
						return;
					}
					if (fname.StartsWith(Program.ErbDir, StringComparison.OrdinalIgnoreCase))
						filepath.Add(Program.ErbDir + fname.Substring(Program.ErbDir.Length));
					else
						filepath.Add(fname);
				}
				console.ReloadPartialErb(filepath);
			}
		}

		private void MainWindow_FormClosing(object sender, FormClosingEventArgs e)
		{
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

		private void フォルダを読み直すFToolStripMenuItem_Click(object sender, EventArgs e)
		{
			if (console == null)
				return;
			if (console.IsInProcess)
			{
				System.Windows.MessageBox.Show("スクリプト動作中には使用できません");
				return;
			}
			//List<KeyValuePair<string, string>> filepath = new List<KeyValuePair<string, string>>();
			if (folderSelectDialog.ShowDialog() == DialogResult.OK)
			{
				console.ReloadFolder(folderSelectDialog.SelectedPath);
			}
		}

		void richTextBox1_MouseWheel(object sender, System.Windows.Forms.MouseEventArgs e)
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

		private void richTextBox1_TextChanged(object sender, EventArgs e)
		{
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

		private void richTextBox1_KeyDown(object sender, KeyEventArgs e)
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
				console.KillMacro = true;
				if (!console.IsInProcess)
					PressEnterKey(true, false);
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
				if (Clipboard.GetDataObject().GetDataPresent(DataFormats.Text))
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

		int macroGroup = 0;
		private void マクロToolStripMenuItem_Click(object sender, EventArgs e)
		{
			if ((console == null) || console.IsInProcess)
				return;
			if (!Config.UseKeyMacro)
				return;
			ToolStripMenuItem item = (ToolStripMenuItem)sender;
			int fkeynum = (int)item.ShortcutKeys - (int)Keys.F1;
			string macro = KeyMacro.GetMacro(fkeynum, macroGroup);
			if (macro.Length > 0)
			{
				richTextBox1.Text = macro;
				richTextBox1.SelectionStart = richTextBox1.Text.Length;
			}
		}

		private void グループToolStripMenuItem_Click(object sender, EventArgs e)
		{
			if ((console == null) || console.IsInProcess)
				return;
			if (!Config.UseKeyMacro)
				return;
			ToolStripMenuItem item = (ToolStripMenuItem)sender;
			setNewMacroGroup(int.Parse(item.Tag as string));//とても無駄なキャスト&Parse
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

		int labelTimerCount = 0;
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

	}
}