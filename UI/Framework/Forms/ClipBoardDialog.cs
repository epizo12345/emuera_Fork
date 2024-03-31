using System.Drawing;
using System.Text;
using System.Windows.Forms;
using MinorShift.Emuera.GameView;
using MinorShift.Emuera.Runtime.Config;

namespace MinorShift.Emuera.Forms
{
    internal sealed partial class ClipBoardDialog : Form
    {
        public ClipBoardDialog()
        {
            InitializeComponent();
            if (textBox1.Width != Config.WindowX)
            {
                this.ClientSize = new Size(Config.WindowX, 480);
                textBox1.Width = Config.WindowX;
            }
            textBox1.Font = Config.DefaultFont;
        }

        public void Setup(EmueraConsole console)
        {
            textBox1.Text = console.GetLog();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.A | Keys.Control))
                textBox1.SelectAll();
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
