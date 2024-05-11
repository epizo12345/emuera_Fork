using System;
using System.Drawing;
using SkiaSharp;

namespace MinorShift.Emuera.UI.Game.Image;


internal sealed class ConstImage : AbstractImage
{
    public ConstImage(string name)
    { Name = name; }


    public readonly string Name;

    internal void CreateFrom(SKBitmap bmp, bool useGDI)
    {
        if (SKBitmap != null)
            throw new Exception();
        try
        {
            SKBitmap = bmp;
        }
        catch
        {
            return;
        }
        return;
    }
    //public void Load(bool useGDI)
    //{
    //	if (Loaded)
    //		return;
    //	try
    //	{
    //		Bitmap = new Bitmap(Filepath);
    //		if (useGDI)
    //		{
    //			hBitmap = Bitmap.GetHbitmap();
    //			g = Graphics.FromImage(Bitmap);
    //			GDIhDC = g.GetHdc();
    //			hDefaultImg = GDI.SelectObject(GDIhDC, hBitmap);
    //		}
    //		Loaded = true;
    //		Enabled = true;
    //	}
    //	catch
    //	{
    //		return;
    //	}
    //	return;
    //}

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
        get { return SKBitmap != null; }
    }
}
