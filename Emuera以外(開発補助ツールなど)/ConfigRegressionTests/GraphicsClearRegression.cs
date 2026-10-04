using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using MinorShift.Emuera.UI.Game.Image;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace Emuera.ConfigRegressionTests;

internal static class GraphicsClearRegression
{
    private const int Width = 19;
    private const int Height = 13;

    internal static void Verify()
    {
        using var source = new SKBitmap(Width, Height);
        source.Erase(SKColors.Red);
        string sourcePath = Path.Combine(Path.GetTempPath(), "emuera-gclear-source-" + Guid.NewGuid().ToString("N") + ".png");
        using (var encodedSource = source.Encode(SKEncodedImageFormat.Png, 100))
            File.WriteAllBytes(sourcePath, encodedSource.ToArray());
        try
        {
            VerifyClear(sourcePath, "transparent", Color.Transparent);
            VerifyClear(sourcePath, "semitransparent", Color.FromArgb(128, 40, 90, 170));
            VerifyClear(sourcePath, "opaque", Color.FromArgb(255, 4, 140, 230));
            VerifyPartialWriteRetainsOldPixels();
        }
        finally { File.Delete(sourcePath); }
        Console.WriteLine("GCLEAR_FULL_PIXELS_STATE_SPRITE_SAVE=PASS");
    }

    private static void VerifyClear(string sourcePath, string caseName, Color clearColor)
    {
        // 各色でPNGから別のImage backingを作り、初回GCLEARの移行経路を必ず通す。
        using var graph = new GraphicsImage();
        graph.GCreateFromF(SKImage.FromEncodedData(sourcePath), false);
        using var sprite = new SpriteG("GCLEAR_REGRESSION", graph, new Rectangle(0, 0, Width, Height));
        using var brush = new SKPaint { Color = SKColors.Yellow };
        using var pen = new SKPaint { Color = SKColors.Blue };
        using var font = new SKFont();
        graph.GSetBrush(brush);
        graph.GSetPen(pen);
        graph.GSetFont(font);
        graph.GDrawPolygonAddPoint(new SKPoint(1, 2));
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object points = typeof(GraphicsImage).GetField("_points", flags)!.GetValue(graph)!;
        if (!ReferenceEquals(sprite.BaseImage, graph) ||
            graph.Image == null || graph.Bitmap != null || graph.HasBitmapStorage ||
            graph.Width != Width || graph.Height != Height)
            throw new Exception($"{caseName}: first clear did not start with Image-only backing");
        graph.GClear(clearColor);
        CheckState(graph, brush, pen, font, points, flags, caseName);
        string firstHash = CheckAllPixels(graph, sprite, clearColor, false, caseName + " first");
        graph.GSetColor(Color.Lime, 7, 6);
        string partialHash = CheckAllPixels(graph, sprite, clearColor, true, caseName + " partial write");
        CheckState(graph, brush, pen, font, points, flags, caseName);
        string partialSavedHash = SaveAndHash(graph, clearColor, true, caseName + " partial saved");
        if (partialSavedHash != partialHash)
            throw new Exception($"{caseName}: partial write changed during PNG round-trip");
        graph.GClear(clearColor);
        string repeatedHash = CheckAllPixels(graph, sprite, clearColor, false, caseName + " repeated");
        CheckState(graph, brush, pen, font, points, flags, caseName);
        if (repeatedHash != firstHash)
            throw new Exception($"{caseName}: repeated clear differs from first clear");
        string savedHash = SaveAndHash(graph, clearColor, false, caseName + " saved");
        Console.WriteLine("GCLEAR_CASE=" + JsonSerializer.Serialize(new
        {
            name = caseName, width = Width, height = Height, initialBacking = "Image-only",
            checkedPixels = Width * Height, firstHash, partialHash, partialSavedHash, repeatedHash, savedHash,
            fullSpriteCheck = true, statePreserved = true, partialWriteChecked = true
        }));
    }

    private static void CheckState(GraphicsImage graph, SKPaint brush, SKPaint pen, SKFont font,
        object points, BindingFlags flags, string caseName)
    {
        var polygonPoints = (System.Collections.Generic.List<SKPoint>)points;
        if (graph.Width != Width || graph.Height != Height || !graph.HasBitmapStorage || graph.Image != null ||
            polygonPoints.Count != 1 || polygonPoints[0] != new SKPoint(1, 2) ||
            brush.Color != SKColors.Yellow || pen.Color != SKColors.Blue ||
            !ReferenceEquals(brush, typeof(GraphicsImage).GetField("_brush", flags)!.GetValue(graph)) ||
            !ReferenceEquals(pen, typeof(GraphicsImage).GetField("_pen", flags)!.GetValue(graph)) ||
            !ReferenceEquals(font, typeof(GraphicsImage).GetField("_font", flags)!.GetValue(graph)) ||
            !ReferenceEquals(points, typeof(GraphicsImage).GetField("_points", flags)!.GetValue(graph)))
            throw new Exception($"{caseName}: dimensions, backing, or drawing state changed");
    }

    private static string CheckAllPixels(GraphicsImage graph, SpriteG sprite, Color clearColor,
        bool hasPartialWrite, string stage)
    {
        string graphHash = HashPixels((x, y) => graph.GGetColor(x, y), clearColor, stage, hasPartialWrite);
        string spriteHash = HashPixels((x, y) => sprite.SpriteGetColor(x, y), clearColor, stage + " sprite", hasPartialWrite);
        if (graphHash != spriteHash)
            throw new Exception($"{stage}: existing Sprite and G pixels differ");
        return graphHash;
    }

    private static string HashPixels(Func<int, int, SKColor> readPixel, Color clearColor, string stage,
        bool hasPartialWrite = false)
    {
        var bytes = new byte[Width * Height * 4];
        SKColor expected = clearColor.ToSKColor();
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
        {
            SKColor actual = readPixel(x, y);
            bool isWrittenPixel = hasPartialWrite && x == 7 && y == 6;
            // alpha=0のRGBは保存形式で正規化されるため不問。半透明RGBは丸め1まで許す。
            bool matches = isWrittenPixel ? actual == SKColors.Lime :
                expected.Alpha == 0 ? actual.Alpha == 0 : SameColor(actual, expected);
            if (!matches)
                throw new Exception($"{stage}: pixel ({x},{y}) expected {expected}, got {actual}");
            int offset = (y * Width + x) * 4;
            bytes[offset] = actual.Red;
            bytes[offset + 1] = actual.Green;
            bytes[offset + 2] = actual.Blue;
            bytes[offset + 3] = actual.Alpha;
        }
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static string SaveAndHash(GraphicsImage graph, Color clearColor, bool hasPartialWrite, string stage)
    {
        string path = Path.Combine(Path.GetTempPath(), "emuera-gclear-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            graph.SavePng(path);
            using var saved = SKBitmap.Decode(path);
            if (saved == null || saved.Width != Width || saved.Height != Height)
                throw new Exception($"{stage}: saved PNG dimensions differ");
            return HashPixels((x, y) => saved.GetPixel(x, y), clearColor, stage, hasPartialWrite);
        }
        finally { File.Delete(path); }
    }

    private static void VerifyPartialWriteRetainsOldPixels()
    {
        using var partialSource = new SKBitmap(Width, Height);
        partialSource.Erase(SKColors.Crimson);
        using var partial = new GraphicsImage();
        partial.GCreateFromF(SKImage.FromBitmap(partialSource), false);
        partial.GSetColor(Color.Lime, 2, 2);
        if (partial.GGetColor(0, 0) != SKColors.Crimson || partial.GGetColor(2, 2) != SKColors.Lime)
            throw new Exception("normal partial write lost old pixels");
    }

    private static bool SameColor(SKColor actual, SKColor expected) =>
        actual.Alpha == expected.Alpha &&
        Math.Abs(actual.Red - expected.Red) <= 1 &&
        Math.Abs(actual.Green - expected.Green) <= 1 &&
        Math.Abs(actual.Blue - expected.Blue) <= 1;
}
