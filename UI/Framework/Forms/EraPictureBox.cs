using System.Windows.Forms;
using SkiaSharp.Views.Desktop;

namespace MinorShift.Emuera.UI.Framework.Forms;

internal sealed class EraPictureBox : SKGLControl
{
    public EraPictureBox()
    {
        //背景描画カット
        SetStyle(ControlStyles.Opaque, true);


    }

}
