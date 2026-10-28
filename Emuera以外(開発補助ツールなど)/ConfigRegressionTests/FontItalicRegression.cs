using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Windows.Forms;
using MinorShift.Emuera.UI;
using SkiaSharp;

namespace Emuera.ConfigRegressionTests;

// [Emuera改修:DRAW-09 2026-10-28]
// 通常の文字をドット絵で描くフォント（ＭＳ ゴシック18pxなど）の斜体は、元Emuera（GDIのTextRenderer）と同じくアンチエイリアスなしで描く。
// 滑らかに描くフォントの斜体は従来どおり。
internal static class FontItalicRegression
{
    const string BitmapFont = "ＭＳ ゴシック";
    const int Size = 18;

    internal static void BitmapFontItalicIsAliasedAndMatchesGdi()
    {
        if (!SKFontManager.Default.FontFamilies.Contains(BitmapFont))
            throw new Program.TestUnavailableException(BitmapFont + " is not installed");
        SKFont regular = FontFactory.GetFont(BitmapFont, FontStyle.Regular, Size);
        SKFont italic = FontFactory.GetFont(BitmapFont, FontStyle.Italic, Size);
        if (italic.SkewX != -0.3f)
            throw new InvalidOperationException($"italic skew changed: {italic.SkewX}");
        if (italic.Edging != SKFontEdging.Alias)
            throw new InvalidOperationException($"bitmap font italic should be aliased, got {italic.Edging}");
        if (regular.SkewX != 0)
            throw new InvalidOperationException("regular font must not be skewed");

        // GDIの斜体と線の量（インク）が近いこと。にじんでいた旧実装は約129%だった。
        const string text = "斜体の行 Italic 0123";
        double gdiInk = InkGdi(text);
        double skiaInk = InkSkia(text, italic);
        double ratio = skiaInk / gdiInk;
        Console.WriteLine($"ITALIC_INK gdi={gdiInk:F0} skia={skiaInk:F0} ratio={ratio:F3}");
        if (ratio < 0.9 || ratio > 1.1)
            throw new InvalidOperationException($"italic ink ratio {ratio:F3} is far from GDI");
    }

    internal static void SmoothFontItalicKeepsAntialias()
    {
        const string smoothFont = "メイリオ";
        if (!SKFontManager.Default.FontFamilies.Contains(smoothFont))
            throw new Program.TestUnavailableException(smoothFont + " is not installed");
        SKFont italic = FontFactory.GetFont(smoothFont, FontStyle.Italic, 23);
        if (italic.Edging == SKFontEdging.Alias)
            throw new InvalidOperationException("smooth font italic must keep anti-aliasing");
        if (italic.SkewX != -0.3f)
            throw new InvalidOperationException($"italic skew changed: {italic.SkewX}");
    }

    static double InkGdi(string text)
    {
        using var bmp = new Bitmap(300, 30, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Black);
            using var f = new Font(BitmapFont, Size, FontStyle.Italic, GraphicsUnit.Pixel);
            TextRenderer.DrawText(g, text, f, new Point(4, 4), Color.White, TextFormatFlags.NoPrefix);
        }
        double ink = 0;
        for (int y = 0; y < bmp.Height; y++)
            for (int x = 0; x < bmp.Width; x++)
            {
                Color c = bmp.GetPixel(x, y);
                ink += (c.R + c.G + c.B) / 765.0;
            }
        return ink;
    }

    static double InkSkia(string text, SKFont font)
    {
        using var bmp = new SKBitmap(300, 30);
        using (var canvas = new SKCanvas(bmp))
        using (var paint = new SKPaint { Color = SKColors.White })
        {
            canvas.Clear(SKColors.Black);
            canvas.DrawText(text, new SKPoint(4, 4 - font.Metrics.Top), SKTextAlign.Left, font, paint);
        }
        double ink = 0;
        for (int y = 0; y < bmp.Height; y++)
            for (int x = 0; x < bmp.Width; x++)
            {
                SKColor c = bmp.GetPixel(x, y);
                ink += (c.Red + c.Green + c.Blue) / 765.0;
            }
        return ink;
    }
}
