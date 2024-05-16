using System;
using System.Drawing;
using SkiaSharp;

namespace MinorShift.Emuera.UI.Game.Image;


internal sealed class ConstImage : AbstractImage
{
    public ConstImage(string name)
    { Name = name; }


    public readonly string Name;

    internal void CreateFrom(SKImage img)
    {
        if (Image != null)
            throw new Exception();
        try
        {
            Image = img;
        }
        catch
        {
            return;
        }
        return;
    }

    public override SKBitmap SKBitmap
    {
        get
        {
            base.SKBitmap ??= SKBitmap.FromImage(this.Image);
            return base.SKBitmap;
        }
        set => base.SKBitmap = value;
    }

    public override void Dispose()
    {
        if (SKBitmap == null)
            return;
        if (canvas != null)
        {
            canvas.Dispose();
            canvas = null;
        }
        if (SKBitmap != null)
        {
            SKBitmap.Dispose();
            SKBitmap = null;
        }
    }

    ~ConstImage()
    {
        Dispose();
    }


    public override bool IsCreated
    {
        get { return Image != null; }
    }
}
