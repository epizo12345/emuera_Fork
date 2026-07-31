using System;
using MinorShift.Emuera.GameView;
using MinorShift.Emuera.Runtime.Config;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MinorShift.Emuera.Forms;

internal sealed partial class ClipBoardDialog : Form
{
    // [Emuera改修:CLIPBOARD-01] 修正者: epizo
    // 大量ログをテキスト欄へ入れている間だけWindowsの再描画を止めるために使う。
    // ゲームのログ生成は省略せず、画面へ何度も途中描画する時間だけを減らす。
    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    private static partial void SendMessage(IntPtr hWnd, int msg, IntPtr wp, IntPtr lp);
    private const int WM_SETREDRAW = 0x000B;

    public ClipBoardDialog()
    {
        InitializeComponent();
        if (textBox1.Width != Config.WindowX)
        {
            this.ClientSize = new Size(Config.WindowX, 480);
            textBox1.Width = Config.WindowX;
        }
    }

    public void Setup(EmueraConsole console)
    {
        try
        {
            // 再描画を止めても、ログ文字列の取得とテキスト欄への設定は従来どおり行う。
            SendMessage(textBox1.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
            textBox1.Text = console.GetLog();
        }
        finally
        {
            // 例外が起きた場合も再描画停止を残さず、次の表示を正常に戻す。
            SendMessage(textBox1.Handle, WM_SETREDRAW, new IntPtr(1), IntPtr.Zero);
            textBox1.Invalidate();
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.A | Keys.Control))
            textBox1.SelectAll();
        return base.ProcessCmdKey(ref msg, keyData);
    }
}
