using MinorShift.Emuera.Runtime.Config;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace MinorShift.Emuera.UI.Game.Image;

internal sealed class GraphicsImage : AbstractImage
{
#if R0_F4G2
    internal string R0F4G2ContentSha256() => Bitmap is null ? "" : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Bitmap.Bytes));
#endif
    //public Bitmap Bitmap;
    //public IntPtr GDIhDC { get; protected set; }
    //protected Graphics g;
    //protected IntPtr hBitmap;
    //protected IntPtr hDefaultImg;

    public GraphicsImage()
    {
        canvas = null;
        Bitmap = null;
        //created = false;
        //locked = false;
    }
    Size size;
    SKPaint _brush;
    SKPaint _pen;
    SKFont _font;
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
        Bitmap = new SKBitmap(x, y);
        size = new Size(x, y);
        canvas = new SKCanvas(Bitmap);
    }

    internal void GCreateFromF(SKImage img, bool useGDI)
    {
        if (useGDI)
            throw new NotImplementedException();
        GDispose();
        Image = img ?? throw new ArgumentNullException(nameof(img));
        size = new Size(img.Width, img.Height);
    }

    // [Emuera改修:MEM-14A 2026-08-26]
    // file-backed Gは読み取り・描画中はSKImageのまま保持し、pixelを初めて変更する時だけ
    // writable SKBitmapへ一度だけ移行して、元画像とSpriteから見える内容を一致させる。
    void EnsureWritable()
    {
        if (Bitmap != null)
        {
            canvas ??= new SKCanvas(Bitmap);
            return;
        }
        if (Image == null)
            throw new NullReferenceException();

        SKImage oldImage = Image;
        SKBitmap bitmap = null;
        SKCanvas newCanvas = null;
        try
        {
            bitmap = new SKBitmap(oldImage.Width, oldImage.Height);
            newCanvas = new SKCanvas(bitmap);
            newCanvas.DrawImage(oldImage, new SKPoint(0, 0), SKSamplingOptions.Default, null);
        }
        catch
        {
            newCanvas?.Dispose();
            bitmap?.Dispose();
            throw;
        }

        SKCanvas oldCanvas = canvas;
        Bitmap = bitmap;
        canvas = newCanvas;
        Image = null;
        oldCanvas?.Dispose();
        oldImage.Dispose();
    }

    /// <summary>
    /// GCLEAR(int ID, int cARGB)
    /// エラーチェックは呼び出し元でのみ行う
    /// </summary>
    public void GClear(Color c)
    {
        EnsureWritable();
        canvas.Clear(c.ToSKColor());
    }

    /// <summary>
    /// GFILLRECTANGLE(int ID, int x, int y, int width, int height)
    /// エラーチェックは呼び出し元でのみ行う
    /// </summary>
    public void GFillRectangle(Rectangle rect)
    {
        EnsureWritable();
        if (_brush != null)
        {
            //canvas.FillRectangle(brush, rect);

            canvas.DrawRect(rect.ToSKRect(), _brush);
        }
        else
        {
            //using var b = new SolidBrush(Config.BackColor);
            //canvas.FillRectangle(b, rect);

            using (var paint = new SKPaint())
            {
                canvas.DrawRect(rect.ToSKRect(), paint);
            }
        }
    }

    List<SKPoint> _points;
    public void GDrawPolygon()
    {
        EnsureWritable();
        if (_points == null)
        {
            throw new NullReferenceException("DrawPolygonに渡されるPointsが空です");
        }
        SKPaint paint = _pen ?? new SKPaint();
        try
        {
            paint.Style = SKPaintStyle.Stroke;
            canvas.DrawPoints(SKPointMode.Polygon, [.. _points, _points[0]], paint);
        }
        finally
        {
            if (_pen == null)
                paint.Dispose();
        }
    }
    public void GFillPolygon()
    {
        EnsureWritable();
        if (_points == null)
        {
            throw new NullReferenceException("FillPolygonに渡されるPointsが空です");
        }
        SKPaint paint = _brush ?? new SKPaint();
        try
        {
            paint.Style = SKPaintStyle.Fill;

            // [Emuera改修:PERF-14N2 2026-08-26]
            // polygonのpathはこの描画だけで使う一時native object。fieldへ保持せず、描画完了後に解放する。
            using var path = new SKPath();
            foreach (var p in _points)
            {
                path.LineTo(p);
            }
            path.LineTo(_points[0]);
            canvas.DrawPath(path, paint);
        }
        finally
        {
            if (_brush == null)
                paint.Dispose();
        }
    }

    public void GDrawPolygonAddPoint(SKPoint point)
    {
        if (!IsCreated)
            throw new NullReferenceException();
        _points ??= [];
        _points.Add(point);
    }

    public void GDrawPolygonClearPoint()
    {
        if (!IsCreated)
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
        EnsureWritable();
        if (img is ASpriteSingle single && ReferenceEquals(single.BaseImage, this))
        {
            using SKImage snapshot = SKImage.FromBitmap(Bitmap);
            single.GraphicsDrawFromSnapshot(canvas, destRect, snapshot);
            return;
        }
        img.GraphicsDraw(canvas, destRect);
    }

    /// <summary>
    /// GDRAWCIMG(int ID, str imgName, int destX, int destY, int destWidth, int destHeight, float[][] cm)
    /// エラーチェックは呼び出し元でのみ行う
    /// </summary>
    public void GDrawCImg(ASprite img, Rectangle destRect, float[][] cm)
    {
        EnsureWritable();
        //挙動がよくわからないので4行目は単に無視する
        float[] skiaCM = [
            cm[0][0],cm[1][0],cm[2][0],cm[3][0],cm[0][4],
            cm[0][1],cm[1][1],cm[2][1],cm[3][1],cm[1][4],
            cm[0][2],cm[1][2],cm[2][2],cm[3][2],cm[2][4],
            cm[0][3],cm[1][3],cm[2][3],cm[3][3],cm[3][4],
        ];
        // [Emuera改修:PERF-14N2 2026-08-26]
        // ColorMatrix用filterは描画中だけ必要な一時native resource。paintへ設定したまま描画を完了し、
        // 既存のsampling / pixel semanticsを変えずに描画後だけ解放する。
        using var filter = SKColorFilter.CreateColorMatrix(skiaCM);
        if (img is ASpriteSingle single && ReferenceEquals(single.BaseImage, this))
        {
            using SKImage snapshot = SKImage.FromBitmap(Bitmap);
            single.GraphicsDrawFromSnapshot(canvas, destRect, snapshot, filter);
            return;
        }
        img.GraphicsDraw(canvas, destRect, filter);
    }

    /// <summary>
    /// GDRAWG(int ID, int srcID, int destX, int destY, int destWidth, int destHeight, int srcX, int srcY, int srcWidth, int srcHeight)
    /// エラーチェックは呼び出し元でのみ行う
    /// </summary>
    public void GDrawG(GraphicsImage srcGra, Rectangle destRect, Rectangle srcRect)
    {
        EnsureWritable();
        srcGra.Draw(canvas, srcRect.ToSKRect(), destRect.ToSKRect());
    }


    /// <summary>
    /// GDRAWG(int ID, int srcID, int destX, int destY, int destWidth, int destHeight, int srcX, int srcY, int srcWidth, int srcHeight, float[][] cm)
    /// エラーチェックは呼び出し元でのみ行う
    /// </summary>
    public void GDrawG(GraphicsImage srcGra, Rectangle destRect, Rectangle srcRect, float[][] cm)
    {
        EnsureWritable();
        ImageAttributes imageAttributes = new();
        ColorMatrix colorMatrix = new(cm);
        imageAttributes.SetColorMatrix(colorMatrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
        //g.DrawImage(img.Bitmap, destRect, srcRect, GraphicsUnit.Pixel, imageAttributes);なんでこのパターンないのさ
        //canvas.DrawImage(src.ToBitmap(), destRect, srcRect.X, srcRect.Y, srcRect.Width, srcRect.Height, GraphicsUnit.Pixel, imageAttributes);
        float[] skiaCM = [
            cm[0][0],cm[1][0],cm[2][0],cm[3][0],cm[0][4],
            cm[0][1],cm[1][1],cm[2][1],cm[3][1],cm[1][4],
            cm[0][2],cm[1][2],cm[2][2],cm[3][2],cm[2][4],
            cm[0][3],cm[1][3],cm[2][3],cm[3][3],cm[3][4],
        ];
        // [Emuera改修:PERF-14N2 2026-08-26]
        // ColorMatrix用filterは描画中だけ必要な一時native resource。描画完了後にDisposeし、既存のsampling / pixel / color-matrix semanticsは変更しない。
        using var filter = SKColorFilter.CreateColorMatrix(skiaCM);
        using (var paint = new SKPaint() { ColorFilter = filter })
        {
            srcGra.Draw(canvas, srcRect.ToSKRect(), destRect.ToSKRect(), paint);
        }
    }


    /// <summary>
    /// GDRAWGWITHMASK(int ID, int srcID, int maskID, int destX, int destY)
    /// エラーチェックは呼び出し元でのみ行う
    /// </summary>
    public void GDrawGWithMask(GraphicsImage srcGra, GraphicsImage maskGra, Point destPoint)
    {
        EnsureWritable();
        SKBitmap srcBitmap = srcGra.GetReadableBitmap(out bool disposeSrc);
        SKBitmap maskBitmap = maskGra.GetReadableBitmap(out bool disposeMask);
        using SKBitmap srcTemporary = disposeSrc ? srcBitmap : null;
        using SKBitmap maskTemporary = disposeMask ? maskBitmap : null;
        using var destImg = Bitmap.ToBitmap();
        using var srcImg = srcBitmap.ToBitmap();
        using var maskImg = maskBitmap.ToBitmap();
        byte[] srcBytes = BytesFromBitmap(srcImg);
        byte[] srcMaskBytes = BytesFromBitmap(maskImg);
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

    public void GDrawText(string text, SKPoint point)
    {
        EnsureWritable();
        // [Emuera改修:PERF-14N2 2026-08-26]
        // _fontがある場合はborrowed/cached objectなのでDraw側では解放しない。null時に作る一時fontだけを
        // 描画後に解放し、FontFactory等の所有権は変更しない。
        var font = _font ?? new SKFont();
        point.Offset(0, -font.Metrics.Top);
        SKPaint paint = _brush ?? new SKPaint();
        try
        {
            canvas.DrawText(text, point, font, paint);
        }
        finally
        {
            if (_brush == null)
                paint.Dispose();
            if (_font == null)
                font.Dispose();
        }
    }

    public void GSetFont(SKFont r)
    {
        _font = r;
    }
    public void GSetBrush(SKPaint r)
    {
        if (ReferenceEquals(_brush, r))
            return;
        if (_brush != null)
            _brush.Dispose();
        _brush = r;
    }
    public void GSetPen(SKPaint r)
    {
        if (ReferenceEquals(_pen, r))
            return;
        if (_pen != null)
            _pen.Dispose();
        _pen = r;
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
    SKBitmap GetReadableBitmap(out bool temporary)
    {
        if (Bitmap != null)
        {
            temporary = false;
            return Bitmap;
        }
        if (Image == null)
            throw new NullReferenceException();
        temporary = true;
        return SKBitmap.FromImage(Image);
    }
    /// <summary>
    /// GSETCOLOR(int ID, int cARGB, int x, int y)
    /// エラーチェックは呼び出し元でのみ行う
    /// </summary>
    public void GSetColor(Color c, int x, int y)
    {
        EnsureWritable();
        Bitmap.SetPixel(x, y, c.ToSKColor());
    }

    /// <summary>
    /// GGETCOLOR(int ID, int x, int y)
    /// エラーチェックは呼び出し元でのみ行う。特に画像範囲内であるかどうかチェックすること
    /// </summary>
    public SKColor GGetColor(int x, int y)
    {
        return GetPixelReadOnly(x, y);
    }

    public void SavePng(string filepath)
    {
        if (Image != null)
        {
            using SKData data = Image.Encode(SKEncodedImageFormat.Png, 100);
            using FileStream stream = File.Open(filepath, FileMode.Create, FileAccess.Write, FileShare.None);
            data.SaveTo(stream);
            return;
        }
        if (Bitmap == null)
            throw new NullReferenceException();
        using Bitmap bitmap = Bitmap.ToBitmap();
        bitmap.Save(filepath);
    }


    /// <summary>
    /// GDISPOSE(int ID)
    /// </summary>
    public void GDispose()
    {
        size = new Size(0, 0);
        if (canvas != null)
            canvas.Dispose();
        if (Bitmap != null)
            Bitmap.Dispose();
        if (Image != null)
            Image.Dispose();
        if (_brush != null)
            _brush.Dispose();
        if (_pen != null)
            _pen.Dispose();
        _points = null;
        canvas = null;
        Bitmap = null;
        Image = null;
        _brush = null;
        _pen = null;
        _font = null;
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
    public override bool IsCreated { get { return Image != null || Bitmap != null; } }
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
