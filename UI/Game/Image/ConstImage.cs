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

    public override SKBitmap Bitmap
    {
        get
        {
            base.Bitmap ??= SKBitmap.FromImage(this.Image);
            return base.Bitmap;
        }
        set => base.Bitmap = value;
    }

    public override void Dispose()
    {
        if (Bitmap == null)
            return;
        if (canvas != null)
        {
            canvas.Dispose();
            canvas = null;
        }
        if (Bitmap != null)
        {
            Bitmap.Dispose();
            Bitmap = null;
        }
        Image?.Dispose();
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
