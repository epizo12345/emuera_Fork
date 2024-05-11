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

    public void SetStyle()
    {
        //if (StaticConfig.UseImageBuffer)
        //{
        //    this.SetStyle(ControlStyles.Opaque, true);
        //    this.SetStyle(ControlStyles.AllPaintingInWmPaint, true);
        //    this.SetStyle(ControlStyles.UserPaint, true);
        //    this.SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
        //    this.SetStyle(ControlStyles.ResizeRedraw, false);
        //}
        //else
        //{
        //    this.SetStyle(ControlStyles.Opaque, false);
        //    this.SetStyle(ControlStyles.AllPaintingInWmPaint, true);
        //    this.SetStyle(ControlStyles.UserPaint, true);
        //    this.SetStyle(ControlStyles.OptimizedDoubleBuffer, false);
        //    this.SetStyle(ControlStyles.ResizeRedraw, false);
        //}
    }

}
