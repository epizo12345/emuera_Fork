using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using MinorShift.Emuera.UI.Game.Image;
using SkiaSharp;

namespace Emuera.ConfigRegressionTests;

// [Emuera改修:DRAW-06 2026-10-28]
// 元Emuera(GDI+のColorMatrix)を基準に、GDRAWG/GDRAWCIMGのカラーマトリクス結果を画素単位で比べる。
// 行列は画像合成.ERBの@カラーマトリクス初期化から、5行目にマイナスの平行移動を持つ色と持たない色を採った。
internal static class ColorMatrixGdiRegression
{
    // ERBの値そのまま(Creator.MethodのReadColormatrixと同じく/256する)。
    private static readonly (string Name, long[,] Matrix)[] Cases =
    [
        ("灰", new long[,] { { 128, 128, 128, 0, 0 }, { 128, 128, 128, 0, 0 }, { 128, 128, 128, 0, 0 }, { 0, 0, 0, 256, 0 }, { -64, -64, -64, 0, 256 } }),
        ("黒", new long[,] { { 91, 91, 91, 0, 0 }, { 180, 180, 180, 0, 0 }, { 35, 35, 35, 0, 0 }, { 0, 0, 0, 256, 0 }, { -76, -76, -76, 0, 256 } }),
        ("栗", new long[,] { { -47, 75, 9, 0, 0 }, { 247, 210, 349, 0, 0 }, { 100, 15, -57, 0, 0 }, { 0, 0, 0, 256, 0 }, { -89, -89, -89, 0, 256 } }),
        ("桃", new long[,] { { 0, 91, -146, 0, 0 }, { 0, 218, 366, 0, 0 }, { 256, -54, 36, 0, 0 }, { 0, 0, 0, 256, 0 }, { 0, 0, 0, 0, 256 } }),
        ("橙", new long[,] { { -238, 170, 14, 0, 0 }, { 385, 180, 625, 0, 0 }, { 204, 1, -287, 0, 0 }, { 0, 0, 0, 256, 0 }, { -12, -12, -12, 0, 256 } }),
        ("正の平行移動", new long[,] { { 256, 0, 0, 0, 0 }, { 0, 256, 0, 0, 0 }, { 0, 0, 256, 0, 0 }, { 0, 0, 0, 256, 0 }, { 64, 32, 16, 0, 256 } }),
    ];

    // 不透明色だけを使う。半透明はGDI+とSkiaのpremultiply差が混ざるため対象外。
    private static readonly Color[] Pixels =
    [
        Color.FromArgb(255, 0, 0, 0), Color.FromArgb(255, 255, 255, 255), Color.FromArgb(255, 30, 60, 230),
        Color.FromArgb(255, 200, 40, 90), Color.FromArgb(255, 128, 128, 128), Color.FromArgb(255, 12, 250, 70),
    ];

    // GDI+とSkiaの丸め差だけを許す。平行移動が抜けると数十単位ずれるので見逃さない。
    private const int Tolerance = 2;

    internal static void GDrawGMatchesGdiPlus() => VerifyAll(useSprite: false);
    internal static void GDrawCImgMatchesGdiPlus() => VerifyAll(useSprite: true);

    private static void VerifyAll(bool useSprite)
    {
        foreach (var (name, matrix) in Cases)
        {
            float[][] cm = ToFloatMatrix(matrix);
            Color[] expected = DrawWithGdiPlus(cm);
            SKColor[] actual = DrawWithEmuera(cm, useSprite);
            for (int i = 0; i < Pixels.Length; i++)
            {
                Color e = expected[i];
                SKColor a = actual[i];
                if (Math.Abs(e.R - a.Red) > Tolerance || Math.Abs(e.G - a.Green) > Tolerance ||
                    Math.Abs(e.B - a.Blue) > Tolerance || Math.Abs(e.A - a.Alpha) > Tolerance)
                    throw new InvalidOperationException(
                        $"{name} pixel{i} {Pixels[i]}: GDI+ <{e.A},{e.R},{e.G},{e.B}>, Emuera <{a.Alpha},{a.Red},{a.Green},{a.Blue}>");
            }
        }
    }

    private static float[][] ToFloatMatrix(long[,] matrix)
    {
        var cm = new float[5][];
        for (int x = 0; x < 5; x++)
        {
            cm[x] = new float[5];
            for (int y = 0; y < 5; y++)
                cm[x][y] = matrix[x, y] / 256f;
        }
        return cm;
    }

    private static Color[] DrawWithGdiPlus(float[][] cm)
    {
        using var src = new Bitmap(Pixels.Length, 1, PixelFormat.Format32bppArgb);
        using var dst = new Bitmap(Pixels.Length, 1, PixelFormat.Format32bppArgb);
        for (int i = 0; i < Pixels.Length; i++)
            src.SetPixel(i, 0, Pixels[i]);
        using (var g = Graphics.FromImage(dst))
        using (var attributes = new ImageAttributes())
        {
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            attributes.SetColorMatrix(new ColorMatrix(cm), ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
            g.DrawImage(src, new Rectangle(0, 0, Pixels.Length, 1), 0, 0, Pixels.Length, 1, GraphicsUnit.Pixel, attributes);
        }
        var result = new Color[Pixels.Length];
        for (int i = 0; i < Pixels.Length; i++)
            result[i] = dst.GetPixel(i, 0);
        return result;
    }

    private static SKColor[] DrawWithEmuera(float[][] cm, bool useSprite)
    {
        using var source = new GraphicsImage();
        using var destination = new GraphicsImage();
        source.GCreate(Pixels.Length, 1, false);
        destination.GCreate(Pixels.Length, 1, false);
        for (int i = 0; i < Pixels.Length; i++)
            source.Bitmap.SetPixel(i, 0, new SKColor(Pixels[i].R, Pixels[i].G, Pixels[i].B, Pixels[i].A));
        var rect = new Rectangle(0, 0, Pixels.Length, 1);
        if (useSprite)
        {
            using var sprite = new SpriteG("COLOR_MATRIX_GDI", source, rect);
            destination.GDrawCImg(sprite, rect, cm);
        }
        else
            destination.GDrawG(source, rect, rect, cm);
        var result = new SKColor[Pixels.Length];
        for (int i = 0; i < Pixels.Length; i++)
            result[i] = destination.Bitmap.GetPixel(i, 0);
        return result;
    }
}
