using MinorShift.Emuera.Runtime.Config;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace MinorShift.Emuera.UI.Game.Image;

internal sealed class GraphicsImage : AbstractImage
{
    //public Bitmap Bitmap;
    //public IntPtr GDIhDC { get; protected set; }
    //protected Graphics g;
    //protected IntPtr hBitmap;
    //protected IntPtr hDefaultImg;

    public GraphicsImage(int id)
    {
        ID = id;
        canvas = null;
        SKBitmap = null;
        //created = false;
        //locked = false;
    }
    public readonly int ID;
    Size size;
    Brush brush;
    Pen pen;
    SKFont font;
    //Bitmap b;
    //Graphics g;


    ////bool created;
    ////bool locked;
    //public void LockGraphics()
    //{
    //	//if (locked)
    //	//	return;
    //	//g = Graphics.FromImage(b);
    //	//locked = true;
    //}
    //public void UnlockGraphics()
    //{
    //	//if (!locked)
    //	//	return;
    //	//g.Dispose();
    //	//g = null;
    //	//locked = false;
    //}

    #region Bitmap書き込み・作成

    /// <summary>
    /// GCREATE(int ID, int width, int height)
    /// Graphicsの基礎となるBitmapを作成する。エラーチェックは呼び出し元でのみ行う
    /// </summary>
    public void GCreate(int x, int y, bool useGDI)
    {
        if (useGDI)
            throw new NotImplementedException();
        GDispose();
        SKBitmap = new SKBitmap(x, y);
        size = new Size(x, y);
        canvas = new SKCanvas(SKBitmap);
    }

    internal void GCreateFromF(SKBitmap bmp, bool useGDI)
    {
        if (useGDI)
            throw new NotImplementedException();
        GDispose();
        SKBitmap = new SKBitmap(bmp.Width, bmp.Height);
        size = new Size(bmp.Width, bmp.Height);
        canvas = new SKCanvas(SKBitmap);
        canvas.DrawBitmap(bmp, new SKPoint(0, 0));
    }

    /// <summary>
    /// GCLEAR(int ID, int cARGB)
    /// エラーチェックは呼び出し元でのみ行う
    /// </summary>
    public void GClear(Color c)
    {
        if (canvas == null)
            throw new NullReferenceException();
        canvas.Clear(c.ToSKColor());
    }

    /// <summary>
    /// GFILLRECTANGLE(int ID, int x, int y, int width, int height)
    /// エラーチェックは呼び出し元でのみ行う
    /// </summary>
    public void GFillRectangle(Rectangle rect)
    {
        if (canvas == null)
            throw new NullReferenceException();
        if (brush != null)
        {
            //canvas.FillRectangle(brush, rect);

            var paint = new SKPaint();
            canvas.DrawRect(rect.ToSKRect(), paint);
        }
        else
        {
            //using var b = new SolidBrush(Config.BackColor);
            //canvas.FillRectangle(b, rect);

            var paint = new SKPaint();
            canvas.DrawRect(rect.ToSKRect(), paint);
        }
    }

    List<Point> _points;
    public void GDrawPolygon()
    {
        if (canvas == null)
            throw new NullReferenceException();
        if (_points == null)
        {
            throw new NullReferenceException("DrawPolygonに渡されるPointsが空です");
        }
        throw new NotImplementedException();
        if (pen != null)
        {
            //canvas.DrawPolygon(pen, _points.ToArray());
        }
        else
        {
            // using var p = new Pen(Config.ForeColor);
            // canvas.DrawPolygon(p, _points.ToArray());
        }
    }
    public void GFillPolygon()
    {
        if (canvas == null)
            throw new NullReferenceException();
        if (_points == null)
        {
            throw new NullReferenceException("FillPolygonに渡されるPointsが空です");
        }
        throw new NotImplementedException();
        // if (brush != null)
        // {
        //     canvas.FillPolygon(brush, _points.ToArray());
        // }
        // else
        // {
        //     using var b = new SolidBrush(Config.ForeColor);
        //     canvas.FillPolygon(b, _points.ToArray());
        // }
    }

    public void GDrawPolygonAddPoint(Point point)
    {
        if (canvas == null)
            throw new NullReferenceException();
        _points ??= [];
        _points.Add(point);
    }

    public void GDrawPolygonClearPoint()
    {
        if (canvas == null)
            throw new NullReferenceException();
        if (_points == null)
        {
            _points = [];
        }
        else
        {
            _points.Clear();
        }
    }

    /// <summary>
    /// GDRAWCIMG(int ID, str imgName, int destX, int destY, int destWidth, int destHeight)
    /// エラーチェックは呼び出し元でのみ行う
    /// </summary>
    public void GDrawCImg(ASprite img, Rectangle destRect)
    {
        if (canvas == null)
            throw new NullReferenceException();
        img.GraphicsDraw(canvas, destRect);
    }

    /// <summary>
    /// GDRAWCIMG(int ID, str imgName, int destX, int destY, int destWidth, int destHeight, float[][] cm)
    /// エラーチェックは呼び出し元でのみ行う
    /// </summary>
    public void GDrawCImg(ASprite img, Rectangle destRect, float[][] cm)
    {
        if (canvas == null)
            throw new NullReferenceException();
        var rAll = cm[4][0];
        var gAll = cm[4][1];
        var bAll = cm[4][2];
        var aAll = cm[4][3];
        var wAll = cm[4][4];
        float[] skiaCM = [
            cm[0][0],cm[1][0],cm[2][0],cm[3][0],cm[0][4],
            cm[0][1],cm[1][1],cm[2][1],cm[3][1],cm[1][4],
            cm[0][2],cm[1][2],cm[2][2],cm[3][2],cm[2][4],
            cm[0][3],cm[1][3],cm[2][3],cm[3][3],cm[3][4],
        ];
        var filter = SKColorFilter.CreateColorMatrix(skiaCM);
        img.GraphicsDraw(canvas, destRect, filter);
    }

    /// <summary>
    /// GDRAWG(int ID, int srcID, int destX, int destY, int destWidth, int destHeight, int srcX, int srcY, int srcWidth, int srcHeight)
    /// エラーチェックは呼び出し元でのみ行う
    /// </summary>
    public void GDrawG(GraphicsImage srcGra, Rectangle destRect, Rectangle srcRect)
    {
        if (canvas == null)
            throw new NullReferenceException();
        var src = srcGra.GetBitmap();
        //canvas.DrawImage(src.ToBitmap(), destRect, srcRect, GraphicsUnit.Pixel);
        canvas.DrawBitmap(src, new SKPoint(0, 0));
    }


    /// <summary>
    /// GDRAWG(int ID, int srcID, int destX, int destY, int destWidth, int destHeight, int srcX, int srcY, int srcWidth, int srcHeight, float[][] cm)
    /// エラーチェックは呼び出し元でのみ行う
    /// </summary>
    public void GDrawG(GraphicsImage srcGra, Rectangle destRect, Rectangle srcRect, float[][] cm)
    {
        if (canvas == null)
            throw new NullReferenceException();
        var src = srcGra.GetBitmap();
        ImageAttributes imageAttributes = new();
        ColorMatrix colorMatrix = new(cm);
        imageAttributes.SetColorMatrix(colorMatrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
        //g.DrawImage(img.Bitmap, destRect, srcRect, GraphicsUnit.Pixel, imageAttributes);なんでこのパターンないのさ
        //canvas.DrawImage(src.ToBitmap(), destRect, srcRect.X, srcRect.Y, srcRect.Width, srcRect.Height, GraphicsUnit.Pixel, imageAttributes);
        canvas.DrawBitmap(src, new SKPoint(0, 0));
    }


    /// <summary>
    /// GDRAWGWITHMASK(int ID, int srcID, int maskID, int destX, int destY)
    /// エラーチェックは呼び出し元でのみ行う
    /// </summary>
    public void GDrawGWithMask(GraphicsImage srcGra, GraphicsImage maskGra, Point destPoint)
    {
        if (canvas == null)
            throw new NullReferenceException();
        var destImg = GetBitmap().ToBitmap();
        byte[] srcBytes = BytesFromBitmap(srcGra.GetBitmap().ToBitmap());
        byte[] srcMaskBytes = BytesFromBitmap(maskGra.GetBitmap().ToBitmap());
        //Rectangle destRect = new Rectangle(destPoint.X, destPoint.Y, srcGra.Width, srcGra.Height);

        BitmapData bmpData =
            destImg.LockBits(new Rectangle(0, 0, destImg.Width, destImg.Height),
            ImageLockMode.ReadWrite,
            PixelFormat.Format32bppArgb);
        try
        {
            nint ptr = bmpData.Scan0;
            byte[] pixels = new byte[bmpData.Stride * destImg.Height];
            Marshal.Copy(ptr, pixels, 0, pixels.Length);


            for (int y = 0; y < srcGra.Height; y++)
            {

                int destIndex = ((destPoint.Y + y) * destImg.Width + destPoint.X) * 4;
                int srcIndex = ((0 + y) * srcGra.Width + 0) * 4;
                for (int x = 0; x < srcGra.Width; x++)
                {
                    if (srcMaskBytes[srcIndex] == 255)//完全不透明
                    {
                        pixels[destIndex++] = srcBytes[srcIndex++];
                        pixels[destIndex++] = srcBytes[srcIndex++];
                        pixels[destIndex++] = srcBytes[srcIndex++];
                        pixels[destIndex++] = srcBytes[srcIndex++];
                    }
                    else if (srcMaskBytes[srcIndex] == 0)//完全透明
                    {
                        destIndex += 4;
                        srcIndex += 4;
                    }
                    else//半透明 alpha/255ではなく（alpha+1）/256で計算しているがたぶん誤差
                    {
                        int mask = srcMaskBytes[srcIndex]; mask++;
                        pixels[destIndex] = (byte)(srcBytes[srcIndex] * mask + pixels[destIndex] * (256 - mask) >> 8); srcIndex++; destIndex++;
                        pixels[destIndex] = (byte)(srcBytes[srcIndex] * mask + pixels[destIndex] * (256 - mask) >> 8); srcIndex++; destIndex++;
                        pixels[destIndex] = (byte)(srcBytes[srcIndex] * mask + pixels[destIndex] * (256 - mask) >> 8); srcIndex++; destIndex++;
                        pixels[destIndex] = (byte)(srcBytes[srcIndex] * mask + pixels[destIndex] * (256 - mask) >> 8); srcIndex++; destIndex++;
                    }
                }
            }

            // Bitmapへコピー
            Marshal.Copy(pixels, 0, ptr, pixels.Length);
        }
        finally
        {
            destImg.UnlockBits(bmpData);
        }
    }

    public void GSetFont(SKFont r)
    {
        if (font != null)
            font.Dispose();
        font = r;
    }
    public void GSetBrush(Brush r)
    {
        if (brush != null)
            brush.Dispose();
        brush = r;
    }
    public void GSetPen(Pen r)
    {
        if (pen != null)
            pen.Dispose();
        pen = r;
    }




    private static byte[] BytesFromBitmap(Bitmap bmp)
    {
        BitmapData bmpData = bmp.LockBits(
          new Rectangle(0, 0, bmp.Width, bmp.Height),
          ImageLockMode.ReadOnly,  // 書き込むときはReadAndWriteで
          PixelFormat.Format32bppArgb
        );
        if (bmpData.Stride < 0)
            throw new Exception();//変な形式のが送られてくることはありえないはずだが一応
        byte[] pixels = new byte[bmpData.Stride * bmp.Height];
        try
        {
            nint ptr = bmpData.Scan0;
            Marshal.Copy(ptr, pixels, 0, pixels.Length);
        }
        finally
        {
            bmp.UnlockBits(bmpData);

        }
        return pixels;
    }
    #endregion
    #region Bitmap読み込み・削除
    /// <summary>
    /// 未作成ならエラー
    /// </summary>
    public SKBitmap GetBitmap()
    {
        if (SKBitmap == null)
            throw new NullReferenceException();
        //UnlockGraphics();
        return SKBitmap;
    }
    /// <summary>
    /// GSETCOLOR(int ID, int cARGB, int x, int y)
    /// エラーチェックは呼び出し元でのみ行う
    /// </summary>
    public void GSetColor(Color c, int x, int y)
    {
        if (SKBitmap == null)
            throw new NullReferenceException();
        //UnlockGraphics();
        SKBitmap.SetPixel(x, y, c.ToSKColor());
    }

    /// <summary>
    /// GGETCOLOR(int ID, int x, int y)
    /// エラーチェックは呼び出し元でのみ行う。特に画像範囲内であるかどうかチェックすること
    /// </summary>
    public SKColor GGetColor(int x, int y)
    {
        if (SKBitmap == null)
            throw new NullReferenceException();
        //UnlockGraphics();
        return SKBitmap.GetPixel(x, y);
    }


    /// <summary>
    /// GDISPOSE(int ID)
    /// </summary>
    public void GDispose()
    {
        size = new Size(0, 0);
        if (SKBitmap == null)
            return;
        if (canvas != null)
            canvas.Dispose();
        if (SKBitmap != null)
            SKBitmap.Dispose();
        if (brush != null)
            brush.Dispose();
        if (pen != null)
            pen.Dispose();
        if (font != null)
            font.Dispose();
        _points = null;
        canvas = null;
        SKBitmap = null;
        brush = null;
        pen = null;
        font = null;
    }

    public override void Dispose()
    {
        GDispose();
        GC.SuppressFinalize(this);

    }

    ~GraphicsImage()
    {
        Dispose();
    }
    #endregion

    #region 状態判定（Bitmap読み書きを伴わない）
    public override bool IsCreated { get { return canvas != null; } }
    /// <summary>
    /// int GWIDTH(int ID)
    /// </summary>
    public int Width { get { return size.Width; } }
    /// <summary>
    /// int GHEIGHT(int ID)
    /// </summary>
    public int Height { get { return size.Height; } }




    #endregion


}
