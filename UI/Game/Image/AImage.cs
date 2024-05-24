using System;
using System.Drawing;
using SkiaSharp;

namespace MinorShift.Emuera.UI.Game.Image;

internal abstract class AbstractImage : IDisposable
{
    public const int MAX_IMAGESIZE = 8192;
    public virtual SKImage Image { get; set; }
    public virtual SKBitmap Bitmap { get; set; }
    protected SKCanvas canvas;

    public abstract bool IsCreated { get; }

    public abstract void Dispose();
}