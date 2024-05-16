using MinorShift.Emuera.Runtime.Config;
using SkiaSharp;
using System.Drawing;

namespace MinorShift.Emuera.UI.Game;

/// <summary>
/// 描画の最小単位
/// </summary>
abstract class AConsoleDisplayNode
{
    public bool Error { get; protected set; }

    public string Text { get; protected set; }
    public string AltText { get; protected set; }
    public int PointX { get; set; }
    public float XsubPixel { get; set; }
    public float WidthF { get; set; }
    public int Width { get; set; }
    public virtual int Top { get { return 0; } }
    public virtual int Bottom { get { return Config.FontSize; } }
    public abstract bool CanDivide { get; }
    public SKSize? Size;
    public SKPoint? Point;

    public abstract void DrawTo(SKCanvas graph, SKPoint point, bool isSelecting, bool isBackLog, TextDrawingMode mode, bool isButton = false);

    public abstract void SetWidth(StringMeasure sm, float subPixel);
    public override string ToString()
    {
        if (Text == null)
            return "";
        return Text;
    }
}

/// <summary>
/// 色つき
/// </summary>
abstract class AConsoleColoredNode : AConsoleDisplayNode
{
    protected Color Color { get; set; }
    protected Color ButtonColor { get; set; }
    protected bool colorChanged;
}
