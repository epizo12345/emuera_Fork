using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MinorShift.Emuera.UI.Game.Image;
using SkiaSharp;

namespace Emuera.ConfigRegressionTests;

// CSV実装そのものを同時実行し、親画像の共有・再読込み・失敗再試行を判定する。
internal static class CsvCacheRegression
{
    private static readonly MethodInfo CreateFromCsv = typeof(AppContents).GetMethod(
        "CreateFromCsv", BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo GetOrLoadParentImage = typeof(AppContents).GetMethod(
        "GetOrLoadParentImage", BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo RemoveFailedParentImage = typeof(AppContents).GetMethod(
        "RemoveFailedParentImage", BindingFlags.NonPublic | BindingFlags.Static)!;

    internal static void ParallelParentsShareOneImage()
    {
        string directory = NewFixtureDirectory();
        try
        {
            WritePng(Path.Combine(directory, "shared.png"), SKColors.Crimson);
            WritePng(Path.Combine(directory, "other.png"), SKColors.RoyalBlue);
            const int workerCount = 16;
            using var gate = new Barrier(workerCount + 1);
            Task<SpriteF>[] tasks = Enumerable.Range(0, workerCount).Select(index =>
                Task.Factory.StartNew(() =>
                {
                    if (!gate.SignalAndWait(TimeSpan.FromSeconds(30)))
                        throw new TimeoutException("CSV worker did not reach the start gate");
                    return Load($"CSV_SHARED_{index}", directory, "shared.png")!;
                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
            if (!gate.SignalAndWait(TimeSpan.FromSeconds(30)))
                throw new TimeoutException("CSV start gate timed out");
            Task.WaitAll(tasks);
            SpriteF[] sprites = tasks.Select(task => task.Result).ToArray();
            try
            {
                int parents = sprites.Select(sprite => sprite.BaseImage)
                    .Distinct(ReferenceEqualityComparer.Instance).Count();
                Console.WriteLine($"CSV_PARALLEL spriteCount={sprites.Length} distinctParents={parents}");
                Check(parents == 1, $"same PNG produced {parents} parent objects");
                SpriteF other = Load("CSV_OTHER", directory, "other.png")!;
                try
                {
                    Check(!ReferenceEquals(other.BaseImage, sprites[0].BaseImage), "different PNGs shared a parent");
                    Check(other.SpriteGetColor(0, 0) == SKColors.RoyalBlue, "other PNG pixel differs");
                }
                finally { other.Dispose(); }
                sprites[0].Dispose();
                Check(sprites[1].IsCreated && sprites[1].SpriteGetColor(0, 0) == SKColors.Crimson,
                    "disposing one sprite damaged another sprite's image");
            }
            finally { foreach (SpriteF sprite in sprites) sprite.Dispose(); }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    internal static void ReloadAfterBitmapConversion()
    {
        string directory = NewFixtureDirectory();
        try
        {
            WritePng(Path.Combine(directory, "bitmap.png"), SKColors.Gold);
            SpriteF first = Load("CSV_BITMAP_FIRST", directory, "bitmap.png")!;
            var parent = first.BaseImage;
            _ = parent.Bitmap;
            Check(parent.Image == null && parent.Bitmap != null, "parent was not converted to Bitmap storage");
            SpriteF second = Load("CSV_BITMAP_SECOND", directory, "bitmap.png", true)!;
            try
            {
                Check(ReferenceEquals(parent, second.BaseImage), "reload did not use cached parent");
                Check(second.SrcRectangle.Width == 8 && second.SrcRectangle.Height == 8, "bitmap-backed dimensions differ");
                Check(second.SpriteGetColor(0, 0) == SKColors.Gold, "bitmap-backed pixel differs");
            }
            finally { first.Dispose(); second.Dispose(); }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    internal static void FailedPathsCanBeRetried()
    {
        string directory = NewFixtureDirectory();
        try
        {
            Check(Load("CSV_MISSING", directory, "missing.png") == null, "missing image unexpectedly loaded");
            File.WriteAllBytes(Path.Combine(directory, "corrupt.png"), [1, 2, 3, 4]);
            Check(Load("CSV_CORRUPT", directory, "corrupt.png") == null, "corrupt image unexpectedly loaded");
            WritePng(Path.Combine(directory, "missing.png"), SKColors.Green);
            WritePng(Path.Combine(directory, "corrupt.png"), SKColors.Blue);
            SpriteF missing = Load("CSV_MISSING_RETRY", directory, "missing.png")!;
            SpriteF corrupt = Load("CSV_CORRUPT_RETRY", directory, "corrupt.png")!;
            try
            {
                Check(missing.SpriteGetColor(0, 0) == SKColors.Green, "missing path retry failed");
                Check(corrupt.SpriteGetColor(0, 0) == SKColors.Blue, "corrupt path retry failed");
            }
            finally { missing.Dispose(); corrupt.Dispose(); }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    internal static void OneDecoderPerPath()
    {
        string directory = NewFixtureDirectory();
        try
        {
            string firstPath = Path.Combine(directory, "first.png");
            string secondPath = Path.Combine(directory, "second.png");
            WritePng(firstPath, SKColors.Red);
            WritePng(secondPath, SKColors.Blue);
            int firstDecodes = 0;
            int secondDecodes = 0;
            const int workerCount = 16;
            using var start = new Barrier(workerCount + 1);
            Task<ConstImage>[] workers = Enumerable.Range(0, workerCount).Select(index =>
                Task.Factory.StartNew(() =>
                {
                    if (!start.SignalAndWait(TimeSpan.FromSeconds(30))) throw new TimeoutException();
                    string path = index % 2 == 0 ? firstPath : secondPath;
                    return GetCached(path, () =>
                    {
                        if (index % 2 == 0) Interlocked.Increment(ref firstDecodes);
                        else Interlocked.Increment(ref secondDecodes);
                        using var bitmap = new SKBitmap(8, 8);
                        bitmap.Erase(index % 2 == 0 ? SKColors.Red : SKColors.Blue);
                        var image = new ConstImage(path);
                        image.CreateFrom(SKImage.FromBitmap(bitmap));
                        return image;
                    });
                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
            if (!start.SignalAndWait(TimeSpan.FromSeconds(30))) throw new TimeoutException();
            Task.WaitAll(workers);
            Check(firstDecodes == 1 && secondDecodes == 1,
                $"winning decoder counts were {firstDecodes}/{secondDecodes}");
            Check(workers.Where((_, index) => index % 2 == 0)
                .All(worker => ReferenceEquals(worker.Result, workers[0].Result)), "first path parents differ");
            Check(workers.Where((_, index) => index % 2 == 1)
                .All(worker => ReferenceEquals(worker.Result, workers[1].Result)), "second path parents differ");
            Check(!ReferenceEquals(workers[0].Result, workers[1].Result), "different paths were mixed");
            Console.WriteLine($"CSV_DECODES first={firstDecodes} second={secondDecodes}");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    internal static void ExceptionRetryKeepsSuccess()
    {
        string path = Path.Combine(NewFixtureDirectory(), "exception.png");
        try
        {
            bool failed = false;
            try { GetCached(path, () => throw new IOException("controlled decode failure")); }
            catch (IOException) { failed = true; }
            Check(failed, "decode exception was swallowed");
            WritePng(path, SKColors.Purple);
            int successfulDecodes = 0;
            ConstImage success = GetCached(path, () =>
            {
                Interlocked.Increment(ref successfulDecodes);
                var image = new ConstImage(path);
                image.CreateFrom(SKImage.FromEncodedData(path));
                return image;
            });
            ConstImage after = GetCached(path, () => throw new InvalidOperationException("success was removed"));
            Check(ReferenceEquals(success, after) && successfulDecodes == 1,
                "exception cleanup removed or replaced a successful registration");
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    internal static void DifferentPathsDoNotBlock()
    {
        string directory = NewFixtureDirectory();
        string slowPath = Path.Combine(directory, "slow.png");
        string fastPath = Path.Combine(directory, "fast.png");
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        try
        {
            WritePng(slowPath, SKColors.Red);
            WritePng(fastPath, SKColors.Blue);
            Task<ConstImage> slow = Task.Run(() => GetCached(slowPath, () =>
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
                var image = new ConstImage(slowPath);
                image.CreateFrom(SKImage.FromEncodedData(slowPath));
                return image;
            }));
            Check(entered.Wait(TimeSpan.FromSeconds(10)), "slow decoder did not enter");
            Task<ConstImage> fast = Task.Run(() => GetCached(fastPath, () =>
            {
                var image = new ConstImage(fastPath);
                image.CreateFrom(SKImage.FromEncodedData(fastPath));
                return image;
            }));
            Check(fast.Wait(TimeSpan.FromSeconds(3)) && fast.Result.GetPixelReadOnly(0, 0) == SKColors.Blue,
                "different image path was blocked by slow decode");
            release.Set();
            Check(slow.Wait(TimeSpan.FromSeconds(10)), "slow decoder did not complete");
        }
        finally
        {
            release.Set();
            Directory.Delete(directory, recursive: true);
        }
    }

    internal static void StaleFailureCannotEraseSuccess()
    {
        string directory = NewFixtureDirectory();
        string path = Path.Combine(directory, "race.png");
        try
        {
            WritePng(path, SKColors.Pink);
            var cache = GetCache();
            var failed = new Lazy<ConstImage>(() => null!);
            Check(cache.TryAdd(path, failed), "test path was already cached");
            Check(GetCached(path, () => throw new InvalidOperationException()) == null,
                "failed lazy did not return null");
            ConstImage success = GetCached(path, () =>
            {
                var image = new ConstImage(path);
                image.CreateFrom(SKImage.FromEncodedData(path));
                return image;
            });
            // 古い失敗呼出しが成功登録後に除去へ到達する順序を固定する。
            RemoveFailedParentImage.Invoke(null, [path, failed]);
            Check(ReferenceEquals(success, GetCached(path,
                () => throw new InvalidOperationException("new successful cache entry was erased"))),
                "stale failure erased a newer success");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    internal static void FailedPathsDoNotAccumulate()
    {
        string directory = NewFixtureDirectory();
        try
        {
            int before = GetCache().Count;
            for (int index = 0; index < 40; index++)
                Check(Load($"CSV_FAIL_{index}", directory, $"missing{index}.png") == null,
                    "missing image unexpectedly loaded");
            Check(GetCache().Count == before, "failed path Lazy objects remained in the cache");
            Console.WriteLine($"CSV_FAILED_PATHS count=40 retainedDelta={GetCache().Count - before}");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    internal static void LoadContentsRetainsSharedParent()
    {
        string directory = NewFixtureDirectory();
        var contentDir = typeof(MinorShift.Emuera.Program).GetProperty(
            "ContentDir", BindingFlags.Public | BindingFlags.Static)!;
        string previous = (string?)contentDir.GetValue(null) ?? string.Empty;
        try
        {
            WritePng(Path.Combine(directory, "shared.png"), SKColors.Orange);
            File.WriteAllText(Path.Combine(directory, "first.csv"), "CSV_LIFE_FIRST,shared.png", System.Text.Encoding.UTF8);
            File.WriteAllText(Path.Combine(directory, "second.csv"), "CSV_LIFE_SECOND,shared.png", System.Text.Encoding.UTF8);
            contentDir.SetValue(null, directory + Path.DirectorySeparatorChar);
            Check(AppContents.LoadContents() == null, "initial CSV load failed");
            SpriteF first = (SpriteF)AppContents.GetSprite("CSV_LIFE_FIRST")!;
            SpriteF second = (SpriteF)AppContents.GetSprite("CSV_LIFE_SECOND")!;
            Check(first != null && second != null && ReferenceEquals(first.BaseImage, second!.BaseImage),
                "LoadContents did not share parent");
            var parent = second!.BaseImage;
            AppContents.SpriteDispose("CSV_LIFE_FIRST");
            Check(second.IsCreated && second.SpriteGetColor(0, 0) == SKColors.Orange,
                "first sprite disposal damaged second");
            AppContents.SpriteDispose("CSV_LIFE_SECOND");
            Check(parent.IsCreated, "sprite disposal destroyed cached parent");
            Check(AppContents.LoadContents() == null, "CSV reload failed");
            SpriteF reloaded = (SpriteF)AppContents.GetSprite("CSV_LIFE_FIRST")!;
            Check(reloaded != null && ReferenceEquals(parent, reloaded.BaseImage),
                "CSV reload did not use existing cache");
        }
        finally
        {
            AppContents.SpriteDispose("CSV_LIFE_FIRST");
            AppContents.SpriteDispose("CSV_LIFE_SECOND");
            contentDir.SetValue(null, previous);
            Directory.Delete(directory, recursive: true);
        }
    }

    internal static void FortyCsvFilesShareOneParent()
    {
        string directory = NewFixtureDirectory();
        var contentDir = typeof(MinorShift.Emuera.Program).GetProperty(
            "ContentDir", BindingFlags.Public | BindingFlags.Static)!;
        string previous = (string?)contentDir.GetValue(null) ?? string.Empty;
        try
        {
            WritePng(Path.Combine(directory, "shared.png"), SKColors.DarkCyan);
            for (int index = 0; index < 40; index++)
                File.WriteAllText(Path.Combine(directory, $"{index:D3}.csv"),
                    $"CSV_40_{index:D3},shared.png", System.Text.Encoding.UTF8);
            int cachedBefore = GetCache().Count;
            contentDir.SetValue(null, directory + Path.DirectorySeparatorChar);
            Check(AppContents.LoadContents() == null, "parallel CSV load failed");
            SpriteF[] sprites = Enumerable.Range(0, 40)
                .Select(index => (SpriteF)AppContents.GetSprite($"CSV_40_{index:D3}")!).ToArray();
            int parents = sprites.Select(sprite => sprite.BaseImage)
                .Distinct(ReferenceEqualityComparer.Instance).Count();
            int cacheDelta = GetCache().Count - cachedBefore;
            Console.WriteLine($"CSV_40 spriteCount={sprites.Length} distinctParents={parents} cacheDelta={cacheDelta}");
            Check(sprites.All(sprite => sprite.IsCreated) && parents == 1 && cacheDelta == 1,
                "parallel CSV loader retained duplicate parents or cache entries");
        }
        finally
        {
            for (int index = 0; index < 40; index++) AppContents.SpriteDispose($"CSV_40_{index:D3}");
            contentDir.SetValue(null, previous);
            Directory.Delete(directory, recursive: true);
        }
    }

    private static ConstImage GetCached(string path, Func<ConstImage> load)
    {
        try { return (ConstImage)GetOrLoadParentImage.Invoke(null, [path, load])!; }
        catch (TargetInvocationException error) when (error.InnerException != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
    }

    private static ConcurrentDictionary<string, Lazy<ConstImage>> GetCache() =>
        (ConcurrentDictionary<string, Lazy<ConstImage>>)typeof(AppContents)
            .GetField("resourceDic", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

    private static SpriteF? Load(string name, string directory, string filename, bool explicitRectangle = false)
    {
        try
        {
            return (SpriteF?)CreateFromCsv.Invoke(null, [explicitRectangle
                ? new[] { name, filename, "0", "0", "8", "8" }
                : new[] { name, filename },
                directory + Path.DirectorySeparatorChar, null, null]);
        }
        catch (TargetInvocationException error) when (error.InnerException != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
    }

    private static string NewFixtureDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "EmueraCsvCache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void WritePng(string path, SKColor color)
    {
        using var bitmap = new SKBitmap(8, 8);
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, encoded.ToArray());
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
