using System;
using System.Drawing;
using System.Windows.Forms;

namespace MinorShift.Emuera.Forms
{
    public partial class ColorBox : UserControl
    {
        public ColorBox()
        {
            InitializeComponent();
        }
        public Color SelectingColor
        {
            get { return pictureBox1.BackColor; }
            set { pictureBox1.BackColor = value; }
        }
        public string LabelText
        {
            get { return label1.Text; }
            set { label1.Text = value; }
        }

        private void button_Click(object sender, EventArgs e)
        {
            colorDialog.Color = pictureBox1.BackColor;
            if (colorDialog.ShowDialog() == DialogResult.OK)
                pictureBox1.BackColor = colorDialog.Color;
        }
    }
}
