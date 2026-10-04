using System;
using System.IO;
using System.Reflection;
using MinorShift.Emuera.UI.Game.Image;
using SkiaSharp;

namespace Emuera.ConfigRegressionTests;

// 同じfixture・入力を基準版と候補版で実行し、Image/Bitmap両保持形式の再読込みを比較する。
internal static class CsvReloadComparison
{
    private static readonly MethodInfo CreateFromCsv = typeof(AppContents).GetMethod(
        "CreateFromCsv", BindingFlags.NonPublic | BindingFlags.Static)!;

    internal static void CheckReload(bool explicitRectangle, bool convertToBitmap)
    {
        string directory = Path.Combine(Path.GetTempPath(), "EmueraCsvReload-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        SpriteF? first = null;
        SpriteF? second = null;
        try
        {
            string path = Path.Combine(directory, "sheet.png");
            using (var bitmap = new SKBitmap(8, 6))
            {
                bitmap.Erase(SKColors.Gold);
                using var image = SKImage.FromBitmap(bitmap);
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(path, data.ToArray());
            }
            first = Load("CSV_RELOAD_FIRST", directory, explicitRectangle);
            Check(first != null && first.IsCreated, "first sprite was not created");
            AbstractImage parent = first!.BaseImage;
            if (convertToBitmap)
            {
                _ = parent.Bitmap;
                Check(parent.Image == null && parent.HasBitmapStorage, "parent was not Bitmap-backed");
            }
            else
                Check(parent.Image != null && !parent.HasBitmapStorage, "parent was not Image-backed");

            second = Load("CSV_RELOAD_SECOND", directory, explicitRectangle);
            Check(second != null && second.IsCreated, "second sprite was not created");
            Check(ReferenceEquals(parent, second!.BaseImage), "reload used a different parent");
            int expectedWidth = explicitRectangle ? 4 : 8;
            int expectedHeight = explicitRectangle ? 3 : 6;
            Check(second.SrcRectangle.X == (explicitRectangle ? 2 : 0) &&
                  second.SrcRectangle.Y == (explicitRectangle ? 1 : 0) &&
                  second.SrcRectangle.Width == expectedWidth &&
                  second.SrcRectangle.Height == expectedHeight,
                $"rectangle differs: {second.SrcRectangle}");
            Check(parent.PixelWidth == 8 && parent.PixelHeight == 6, "parent dimensions differ");
            Check(second.SpriteGetColor(0, 0) == SKColors.Gold &&
                  second.SpriteGetColor(expectedWidth - 1, expectedHeight - 1) == SKColors.Gold,
                "reloaded sprite pixels differ");
            Console.WriteLine($"CSV_RELOAD rectangle={(explicitRectangle ? "explicit" : "omitted")} storage={(convertToBitmap ? "Bitmap" : "Image")} parentShared=true size={expectedWidth}x{expectedHeight}");
        }
        finally
        {
            first?.Dispose();
            second?.Dispose();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static SpriteF? Load(string name, string directory, bool explicitRectangle)
    {
        string[] tokens = explicitRectangle
            ? [name, "sheet.png", "2", "1", "4", "3"]
            : [name, "sheet.png"];
        try
        {
            return (SpriteF?)CreateFromCsv.Invoke(null,
                [tokens, directory + Path.DirectorySeparatorChar, null, null]);
        }
        catch (TargetInvocationException error) when (error.InnerException != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
