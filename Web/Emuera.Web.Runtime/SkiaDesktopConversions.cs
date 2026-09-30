using SkiaSharp;
using DrawingColor = System.Drawing.Color;
using DrawingPoint = System.Drawing.Point;
using DrawingRectangle = System.Drawing.Rectangle;
using DrawingSize = System.Drawing.Size;

namespace SkiaSharp.Views.Desktop;

internal static class BrowserSkiaConversions
{
    public static SKColor ToSKColor(this DrawingColor value) =>
        new(value.R, value.G, value.B, value.A);

    public static DrawingColor ToDrawingColor(this SKColor value) =>
        DrawingColor.FromArgb(value.Alpha, value.Red, value.Green, value.Blue);

    public static SKPoint ToSKPoint(this DrawingPoint value) => new(value.X, value.Y);
    public static SKSize ToSKSize(this DrawingSize value) => new(value.Width, value.Height);
    public static SKRect ToSKRect(this DrawingRectangle value) =>
        new(value.Left, value.Top, value.Right, value.Bottom);
}
