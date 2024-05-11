using System;
using System.Drawing;
using SkiaSharp;

namespace MinorShift.Emuera.UI.Game.Image;

internal abstract class AbstractImage : IDisposable
{
    public const int MAX_IMAGESIZE = 8192;
    public SKBitmap SKBitmap;
    public nint GDIhDC { get; protected set; }
    protected SKCanvas canvas;

    public abstract bool IsCreated { get; }

    public abstract void Dispose();
}