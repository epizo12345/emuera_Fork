using System;
using System.Drawing;
using SkiaSharp;

namespace MinorShift.Emuera.UI.Game.Image;

internal abstract class AbstractImage : IDisposable
{
    public const int MAX_IMAGESIZE = 8192;
    public virtual SKImage Image { get; set; }
    SKBitmap bitmap;
    public virtual SKBitmap Bitmap
    {
        get => bitmap;
        set => bitmap = value;
    }
    internal bool HasBitmapStorage => bitmap != null;
    protected SKCanvas canvas;

    internal int PixelWidth => Image?.Width ?? Bitmap?.Width ?? 0;
    internal int PixelHeight => Image?.Height ?? Bitmap?.Height ?? 0;

    internal SKColor GetPixelReadOnly(int x, int y)
    {
        if (bitmap != null)
            return bitmap.GetPixel(x, y);
        if (Image == null)
            throw new NullReferenceException();

        using SKPixmap pixels = Image.PeekPixels();
        if (pixels != null)
            return pixels.GetPixelColor(x, y);

        using SKBitmap pixel = new(new SKImageInfo(1, 1));
        if (!Image.ReadPixels(pixel.Info, pixel.GetPixels(), pixel.RowBytes, x, y))
            throw new InvalidOperationException("画像pixelの読み取りに失敗しました");
        return pixel.GetPixel(0, 0);
    }

    internal void Draw(SKCanvas target, SKRect source, SKRect destination, SKPaint paint = null)
    {
        if (Image != null)
            target.DrawImage(Image, source, destination, SKSamplingOptions.Default, paint);
        else if (Bitmap != null)
            target.DrawBitmap(Bitmap, source, destination, SKSamplingOptions.Default, paint);
    }

    internal void Draw(SKCanvas target, SKRect source, SKRect destination, SKSamplingOptions sampling, SKPaint paint = null)
    {
        if (Image != null)
            target.DrawImage(Image, source, destination, sampling, paint);
        else if (Bitmap != null)
            target.DrawBitmap(Bitmap, source, destination, sampling, paint);
    }

    internal void Draw(SKCanvas target, SKRect destination, SKPaint paint)
    {
        if (Image != null)
            target.DrawImage(Image, destination, SKSamplingOptions.Default, paint);
        else if (Bitmap != null)
            target.DrawBitmap(Bitmap, destination, SKSamplingOptions.Default, paint);
    }

    public abstract bool IsCreated { get; }

    public abstract void Dispose();
}
