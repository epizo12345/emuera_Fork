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
            if (base.Bitmap != null) return base.Bitmap;
            if (Image == null) return null;

            base.Bitmap = SKBitmap.FromImage(this.Image);
            Image.Dispose();
            Image = null;
            return base.Bitmap;
        }
        set => base.Bitmap = value;
    }

    public override void Dispose()
    {
        if (base.Bitmap == null && Image == null)
        {
            GC.SuppressFinalize(this);
            return;
        }
        if (canvas != null)
        {
            canvas.Dispose();
            canvas = null;
        }
        if (base.Bitmap != null)
        {
            base.Bitmap.Dispose();
            base.Bitmap = null;
        }
        Image?.Dispose();
        Image = null;
        GC.SuppressFinalize(this);
    }

    ~ConstImage()
    {
        Dispose();
    }


    public override bool IsCreated
    {
        get { return Image != null || base.Bitmap != null; }
    }
}
