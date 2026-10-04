using System;
using System.Drawing;
using System.IO;
using MinorShift.Emuera.UI.Game.Image;
using SkiaSharp;

namespace Emuera.ConfigRegressionTests;

// 期待画素はフレーム正規化やSpriteAnimeの描画を使わず、元の配置とclipから求める。
internal static class AnimationCandidateRegression
{
    private static readonly Rectangle SourceCrop = new(3, 2, 4, 3);
    private static readonly Rectangle Destination = new(2, 3, 8, 6);

    internal static void FileBackedColorCrop() => CompareFileBackedCrop(Point.Empty);

    internal static void FileBackedNegativeOffset() => CompareFileBackedCrop(new Point(-1, -1));

    private static void CompareFileBackedCrop(Point frameOffset)
    {
        string path = CreateSheetPng();
        using var expectedSource = SKBitmap.Decode(path);
        using var parent = new ConstImage(path);
        parent.CreateFrom(SKImage.FromEncodedData(path) ?? throw new InvalidOperationException("fixture PNG decode failed"));
        Assert(parent.Image != null && !parent.HasBitmapStorage, "file parent must retain SKImage");
        using var sprite = new SpriteAnime("file-backed-animation", SourceCrop.Size);
        Assert(sprite.AddFrame(parent, SourceCrop, frameOffset, 60000), "frame registration");
        using var plain = new SKBitmap(14, 12);
        using var filtered = new SKBitmap(14, 12);
        using var plainCanvas = new SKCanvas(plain);
        using var filteredCanvas = new SKCanvas(filtered);
        using var swapRedBlue = CreateRedBlueSwap();
        plain.Erase(SKColors.Transparent);
        filtered.Erase(SKColors.Transparent);
        sprite.ResetTime();
        sprite.GraphicsDraw(plainCanvas, Destination);
        sprite.ResetTime();
        sprite.GraphicsDraw(filteredCanvas, Destination, swapRedBlue);

        // 4×3の元フレームを2倍で置いてから、アニメの8×6領域で切る。
        int originalX = Destination.X + frameOffset.X * 2;
        int originalY = Destination.Y + frameOffset.Y * 2;
        for (int y = 0; y < 12; y++)
            for (int x = 0; x < 14; x++)
            {
                SKColor expected = SKColors.Transparent;
                if (x >= Destination.Left && x < Destination.Right &&
                    y >= Destination.Top && y < Destination.Bottom &&
                    x >= originalX && x < originalX + SourceCrop.Width * 2 &&
                    y >= originalY && y < originalY + SourceCrop.Height * 2)
                {
                    int sourceX = SourceCrop.X + (x - originalX) / 2;
                    int sourceY = SourceCrop.Y + (y - originalY) / 2;
                    expected = expectedSource.GetPixel(sourceX, sourceY);
                }
                CheckPixel(expected, plain.GetPixel(x, y), $"plain offset={frameOffset} ({x},{y})");
                SKColor expectedFiltered = expected.Alpha == 0 ? expected :
                    new SKColor(expected.Blue, expected.Green, expected.Red, expected.Alpha);
                CheckPixel(expectedFiltered, filtered.GetPixel(x, y), $"swap offset={frameOffset} ({x},{y})");
            }

        SKColor firstFiltered = filtered.GetPixel(Destination.X, Destination.Y);
        Assert(firstFiltered == (frameOffset.IsEmpty
            ? new SKColor(155, 65, 80, 255)
            : new SKColor(140, 90, 100, 255)), "hand-calculated first color");
        Assert(parent.Image != null && !parent.HasBitmapStorage, "rendering must not materialize file parent");
    }

    internal static void EdgeTouchDrawsNothing()
    {
        string path = CreateSheetPng();
        using var parent = new ConstImage(path);
        parent.CreateFrom(SKImage.FromEncodedData(path) ?? throw new InvalidOperationException("fixture PNG decode failed"));
        using var swapRedBlue = CreateRedBlueSwap();
        foreach (Point offset in new[]
        {
            new Point(-2, 0), new Point(4, 0), new Point(0, -2), new Point(0, 3)
        })
        {
            using var sprite = new SpriteAnime("edge-touch", new Size(4, 3));
            Assert(sprite.AddFrame(parent, new Rectangle(3, 2, 2, 2), offset, 60000), $"edge frame {offset}");
            using var plain = new SKBitmap(14, 12);
            using var filtered = new SKBitmap(14, 12);
            using var plainCanvas = new SKCanvas(plain);
            using var filteredCanvas = new SKCanvas(filtered);
            plain.Erase(SKColors.Transparent);
            filtered.Erase(SKColors.Transparent);
            sprite.ResetTime();
            sprite.GraphicsDraw(plainCanvas, Destination);
            sprite.ResetTime();
            sprite.GraphicsDraw(filteredCanvas, Destination, swapRedBlue);
            for (int y = 0; y < 12; y++)
                for (int x = 0; x < 14; x++)
                {
                    Assert(plain.GetPixel(x, y).Alpha == 0, $"plain edge={offset} ({x},{y})");
                    Assert(filtered.GetPixel(x, y).Alpha == 0, $"swap edge={offset} ({x},{y})");
                }
        }
    }

    private static string CreateSheetPng()
    {
        string fixtureDirectory = Path.Combine(Environment.CurrentDirectory, "image-regression-fixture");
        Directory.CreateDirectory(fixtureDirectory);
        string path = Path.Combine(fixtureDirectory, "sheet.png");
        using var bitmap = new SKBitmap(8, 6);
        for (int y = 0; y < 6; y++)
            for (int x = 0; x < 8; x++)
                bitmap.SetPixel(x, y, x == 5 && y == 3 ? SKColors.Transparent :
                    new SKColor((byte)(20 + x * 20), (byte)(15 + y * 25), (byte)(200 - x * 15), 255));
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, encoded.ToArray());
        return path;
    }

    private static SKColorFilter CreateRedBlueSwap() => SKColorFilter.CreateColorMatrix([
        0, 0, 1, 0, 0,
        0, 1, 0, 0, 0,
        1, 0, 0, 0, 0,
        0, 0, 0, 1, 0]);

    private static void CheckPixel(SKColor expected, SKColor actual, string label)
    {
        if (expected.Alpha == 0)
            Assert(actual.Alpha == 0, label + " expected transparent, actual " + actual);
        else
            Assert(expected == actual, label + " expected " + expected + ", actual " + actual);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
