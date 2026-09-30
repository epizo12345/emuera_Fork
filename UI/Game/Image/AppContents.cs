using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Runtime.Utils;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Linq;
using MinorShift.Emuera.UI.Framework;
using System.Security.Cryptography;
using System.Text;

namespace MinorShift.Emuera.UI.Game.Image;

static class AppContents
{
    static readonly ConcurrentDictionary<string, AbstractImage> resourceDic = new(Config.StrComper);
    static readonly ConcurrentDictionary<string, ASprite> imageDictionary = new(Config.StrComper);
    static readonly ConcurrentDictionary<long, GraphicsImage> gList = [];
    //static public T GetContent<T>(string name)where T :AContentItem
    //{
    //	if (name == null)
    //		return null;
    //	name = name.ToUpper();
    //	if (!itemDic.ContainsKey(name))
    //		return null;
    //	return itemDic[name] as T;
    //}
    static public GraphicsImage GetGraphics(long i)
    {
        if (gList.TryGetValue(i, out GraphicsImage value))
            return value;
        var g = new GraphicsImage();
        gList[i] = g;
        return g;
    }

    static public ASprite GetSprite(string name)
    {
        if (name == null)
            return null;
        name = name.ToUpper();
        if (!imageDictionary.TryGetValue(name, out ASprite value))
            return null;
        return value;
    }

#if WEB_RUNTIME
    internal static string GetSpriteDataUrl(string name)
    {
        ASprite sprite = GetSprite(name);
        if (sprite is SpriteF cachedFile && cachedFile.IsCreated)
        {
            if (cachedFile.CachedWebDataUrl is not null)
            {
                return cachedFile.CachedWebDataUrl;
            }
        }
        if (sprite is SpriteG cachedGenerated && cachedGenerated.IsCreated)
        {
            long generation = cachedGenerated.ParentImage.ContentGeneration;
            if (cachedGenerated.CachedWebDataUrl is not null)
            {
                if (cachedGenerated.CachedWebDataUrlGeneration == generation)
                {
                    return cachedGenerated.CachedWebDataUrl;
                }
            }
        }
        if (sprite is SpriteAnime animation)
        {
            SpriteAnime.Frame frame = animation.CurrentFrame;
            SKImage animatedSource = frame?.ParentImage.Image;
            if (animatedSource is null) return null;
            using SKSurface surface = SKSurface.Create(new SKImageInfo(animation.DestBaseSize.Width, animation.DestBaseSize.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
            surface.Canvas.Clear(SKColors.Transparent);
            var sourceRect = new SKRect(frame.SourceRectangle.Left, frame.SourceRectangle.Top, frame.SourceRectangle.Right, frame.SourceRectangle.Bottom);
            var destinationRect = new SKRect(frame.Offset.X, frame.Offset.Y, frame.Offset.X + frame.SourceRectangle.Width, frame.Offset.Y + frame.SourceRectangle.Height);
            surface.Canvas.DrawImage(animatedSource, sourceRect, destinationRect);
            using SKImage rendered = surface.Snapshot();
            using SKData renderedData = rendered.Encode(SKEncodedImageFormat.Png, 100);
            if (renderedData is null)
            {
                return null;
            }
            return "data:image/png;base64," + Convert.ToBase64String(renderedData.ToArray());
        }
        SKImage source = sprite switch
        {
            SpriteF single => single.ParentImage.Image,
            SpriteG generated => generated.ParentImage.Image,
            _ => null
        };
        if (source is null)
            return null;
        SKImage image = source;
        bool dispose = false;
        Rectangle rectangle = sprite switch
        {
            SpriteF single => single.SourceRectangle,
            SpriteG generated => generated.SourceRectangle,
            _ => new Rectangle(0, 0, source.Width, source.Height)
        };
        if (rectangle.X != 0 || rectangle.Y != 0 || rectangle.Width != source.Width || rectangle.Height != source.Height)
        {
            image = source.Subset(new SKRectI(rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom));
            dispose = true;
        }
        try
        {
            using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
            if (data is null)
            {
                return null;
            }
            string url = "data:image/png;base64," + Convert.ToBase64String(data.ToArray());
            if (sprite is SpriteF file)
            {
                file.CachedWebDataUrl = url;
            }
            else if (sprite is SpriteG generated)
            {
                generated.CachedWebDataUrl = url;
                generated.CachedWebDataUrlGeneration = generated.ParentImage.ContentGeneration;
            }
            return url;
        }
        finally
        {
            if (dispose) image.Dispose();
        }
    }
#endif

    static public void SpriteDispose(string name)
    {
        if (name == null)
            return;
        name = name.ToUpper();
        if (!imageDictionary.TryGetValue(name, out ASprite value))
            return;
        value.Dispose();
        imageDictionary.TryRemove(name, out _);
    }

    static public void CreateSpriteG(string imgName, GraphicsImage parent, Rectangle rect)
    {
        if (string.IsNullOrEmpty(imgName))
            throw new ArgumentOutOfRangeException(nameof(imgName));
        SpriteG newCImg = new(imgName, parent, rect);
        imageDictionary[imgName] = newCImg;
    }

    internal static void CreateSpriteAnime(string imgName, int w, int h)
    {
        if (string.IsNullOrEmpty(imgName))
            throw new ArgumentOutOfRangeException(nameof(imgName));
        SpriteAnime newCImg = new(imgName, new Size(w, h));
        imageDictionary[imgName] = newCImg;
    }

    static public Exception LoadContents()
    {
        if (!Directory.Exists(Program.ContentDir))
            return null;
        try
        {
            //resourcesフォルダ内の全てのcsvファイルを探索する
            var csvFiles = Directory.EnumerateFiles(Program.ContentDir, "*", SearchOption.AllDirectories);
            csvFiles.AsParallel()
                .Where(path => Path.GetExtension(path).Equals(".csv", StringComparison.OrdinalIgnoreCase))
                .ForAll(path =>
                {
                    //アニメスプライト宣言。nullでないとき、フレーム追加モード
                    SpriteAnime currentAnime = null;
                    string directory = Path.GetDirectoryName(path) + Path.DirectorySeparatorChar;
                    string filename = Path.GetFileName(path);
                    string[] lines = Preload.ReadFileLines(path, true);
                    int lineNo = 0;
                    foreach (var line in lines)
                    {
                        lineNo++;
                        if (line.Length == 0)
                            continue;
                        string str = line.Trim();
                        if (str.Length == 0 || str.StartsWith(';'))
                            continue;
                        string[] tokens = str.Split(',');
                        //AContentItem item = CreateFromCsv(tokens);
                        ScriptPosition? sp = new(filename, lineNo);
                        if (CreateFromCsv(tokens, directory, currentAnime, sp) is ASprite item)
                        {
                            //アニメスプライト宣言ならcurrentAnime上書きしてフレーム追加モードにする。そうでないならnull
                            currentAnime = item as SpriteAnime;
                            if (!imageDictionary.TryAdd(item.Name, item))
                            {
                                ParserMediator.Warn(string.Format(LocalizationManager.Error.SpriteNameAlreadyUsed, item.Name), sp, 0);
                                item.Dispose();
                            }
                        }
                    }
                });
        }
        catch (Exception e)
        {
            return e;
        }
        return null;
    }

    internal static int ParentImageCount => resourceDic.Count;
    internal static int SpriteCount => imageDictionary.Count;
    internal static int AnimationCount => imageDictionary.Values.Count(value => value is SpriteAnime);
    internal static int AnimationFrameCount => imageDictionary.Values.OfType<SpriteAnime>().Sum(value => value.FrameCount);
    static string[] BootstrapParentRows => resourceDic.Select(pair =>
    {
#if WEB_RUNTIME
        int width = pair.Value.Image?.Width ?? 0;
        int height = pair.Value.Image?.Height ?? 0;
#else
        int width = pair.Value.PixelWidth;
        int height = pair.Value.PixelHeight;
#endif
        return $"P\t{Path.GetRelativePath(Program.ContentDir, pair.Key).Replace('\\', '/')}\t{width}\t{height}";
    }).ToArray();
    static string NormalizeResourcePath(string path) => string.IsNullOrEmpty(path)
        ? string.Empty
        : Path.GetRelativePath(Program.ContentDir, path).Replace('\\', '/');
    static IEnumerable<string> GetBootstrapSpriteRows()
    {
        foreach (var pair in imageDictionary)
        {
            ASprite sprite = pair.Value;
            string parent = string.Empty;
            Rectangle source = Rectangle.Empty;
            if (sprite is SpriteF single)
            {
#if WEB_RUNTIME
                parent = NormalizeResourcePath(single.ParentImage.Name);
                source = single.SourceRectangle;
#else
                parent = NormalizeResourcePath((single.BaseImage as ConstImage)?.Name ?? string.Empty);
                source = single.SrcRectangle;
#endif
            }
            yield return $"S\t{pair.Key}\t{sprite.GetType().Name}\t{parent}\t{source.X}\t{source.Y}\t{source.Width}\t{source.Height}\t{sprite.DestBaseSize.Width}\t{sprite.DestBaseSize.Height}\t{sprite.DestBasePosition.X}\t{sprite.DestBasePosition.Y}\t{(sprite is SpriteAnime anime ? anime.FrameCount : 0)}";
            if (sprite is not SpriteAnime animation)
                continue;
#if WEB_RUNTIME
            foreach (var frame in animation.BootstrapFrames.Select((value, index) => (value, index)))
                yield return $"F\t{pair.Key}\t{frame.index}\t{NormalizeResourcePath((frame.value.ParentImage as ConstImage)?.Name ?? string.Empty)}\t{frame.value.SourceRectangle.X}\t{frame.value.SourceRectangle.Y}\t{frame.value.SourceRectangle.Width}\t{frame.value.SourceRectangle.Height}\t{frame.value.Offset.X}\t{frame.value.Offset.Y}\t{frame.value.Delay}";
#else
            foreach (var frame in animation.BootstrapFrames.Select((value, index) => (value, index)))
                yield return $"F\t{pair.Key}\t{frame.index}\t{NormalizeResourcePath(frame.value.ParentName)}\t{frame.value.SourceRectangle.X}\t{frame.value.SourceRectangle.Y}\t{frame.value.SourceRectangle.Width}\t{frame.value.SourceRectangle.Height}\t{frame.value.Offset.X}\t{frame.value.Offset.Y}\t{frame.value.Delay}";
#endif
        }
    }
    static string[] BootstrapSpriteRows => GetBootstrapSpriteRows().ToArray();
    static string HashRows(IEnumerable<string> rows) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', rows.OrderBy(value => value, StringComparer.Ordinal)))));
    internal static string ParentIdentitySha256 => HashRows(BootstrapParentRows);
    internal static string SpriteIdentitySha256 => HashRows(BootstrapSpriteRows);
    internal static string SpriteNameSha256 => HashRows(imageDictionary.Keys);
    internal static int NonZeroSpriteOffsetCount => imageDictionary.Values.Count(value => !value.DestBasePosition.IsEmpty);
    internal static long SpriteWidthTotal => imageDictionary.Values.Sum(value => (long)value.DestBaseSize.Width);
    internal static long SpriteHeightTotal => imageDictionary.Values.Sum(value => (long)value.DestBaseSize.Height);
    internal static long SpriteOffsetXTotal => imageDictionary.Values.Sum(value => (long)value.DestBasePosition.X);
    internal static long SpriteOffsetYTotal => imageDictionary.Values.Sum(value => (long)value.DestBasePosition.Y);
    internal static long SpriteSourceXTotal => imageDictionary.Values.OfType<SpriteF>().Sum(value =>
#if WEB_RUNTIME
        (long)value.SourceRectangle.X
#else
        (long)value.SrcRectangle.X
#endif
    );
    internal static long SpriteSourceYTotal => imageDictionary.Values.OfType<SpriteF>().Sum(value =>
#if WEB_RUNTIME
        (long)value.SourceRectangle.Y
#else
        (long)value.SrcRectangle.Y
#endif
    );
#if WEB_RUNTIME
    internal static long AnimationSourceXTotal => imageDictionary.Values.OfType<SpriteAnime>().SelectMany(value => value.BootstrapFrames).Sum(value => (long)value.SourceRectangle.X);
    internal static long AnimationSourceYTotal => imageDictionary.Values.OfType<SpriteAnime>().SelectMany(value => value.BootstrapFrames).Sum(value => (long)value.SourceRectangle.Y);
    internal static long AnimationOffsetXTotal => imageDictionary.Values.OfType<SpriteAnime>().SelectMany(value => value.BootstrapFrames).Sum(value => (long)value.Offset.X);
    internal static long AnimationOffsetYTotal => imageDictionary.Values.OfType<SpriteAnime>().SelectMany(value => value.BootstrapFrames).Sum(value => (long)value.Offset.Y);
    internal static long AnimationDelayTotal => imageDictionary.Values.OfType<SpriteAnime>().SelectMany(value => value.BootstrapFrames).Sum(value => (long)value.Delay);
#else
    internal static long AnimationSourceXTotal => imageDictionary.Values.OfType<SpriteAnime>().SelectMany(value => value.BootstrapFrames).Sum(value => (long)value.SourceRectangle.X);
    internal static long AnimationSourceYTotal => imageDictionary.Values.OfType<SpriteAnime>().SelectMany(value => value.BootstrapFrames).Sum(value => (long)value.SourceRectangle.Y);
    internal static long AnimationOffsetXTotal => imageDictionary.Values.OfType<SpriteAnime>().SelectMany(value => value.BootstrapFrames).Sum(value => (long)value.Offset.X);
    internal static long AnimationOffsetYTotal => imageDictionary.Values.OfType<SpriteAnime>().SelectMany(value => value.BootstrapFrames).Sum(value => (long)value.Offset.Y);
    internal static long AnimationDelayTotal => imageDictionary.Values.OfType<SpriteAnime>().SelectMany(value => value.BootstrapFrames).Sum(value => (long)value.Delay);
#endif
    internal static string BootstrapIdentitySha256 => HashRows(BootstrapParentRows.Concat(BootstrapSpriteRows));

#if WEB_RUNTIME
    internal static void ResetBootstrapResources()
    {
        foreach (ASprite sprite in imageDictionary.Values)
            sprite.Dispose();
        foreach (AbstractImage image in resourceDic.Values)
            image.Dispose();
        imageDictionary.Clear();
        resourceDic.Clear();
        UnloadGraphicList();
    }
#endif

    //タイトルに戻る時用（コードの変更はないので、動的に作られた分だけ削除）
    static public void UnloadGraphicList()
    {
        foreach (var graph in gList.Values)
            graph.GDispose();
        gList.Clear();
    }

    /// <summary>
    /// resourcesフォルダ中のcsvの1行を読んで新しいリソースを作る(or既存のアニメーションスプライトに1フレーム追加する)
    /// </summary>
    /// <param name="tokens"></param>
    /// <param name="dir"></param>
    /// <param name="currentAnime"></param>
    /// <param name="sp"></param>
    /// <returns></returns>
    static private ASprite CreateFromCsv(string[] tokens, string dir, SpriteAnime currentAnime, ScriptPosition? sp)
    {
        if (tokens.Length < 2)
            return null;
        string name = tokens[0].Trim();//
        string arg2 = tokens[1];//画像ファイル名
        if (name.Length == 0 || arg2.Length == 0)
            return null;
        //アニメーションスプライト宣言
        if (arg2.Equals("ANIME", StringComparison.OrdinalIgnoreCase))
        {
            if (tokens.Length < 4)
            {
                ParserMediator.Warn(LocalizationManager.Error.NotDeclaredAnimationSpriteSize, sp, 1);
                return null;
            }
            //w,h
            int[] sizeValue = new int[2];
            bool sccs = true;
            for (int i = 0; i < 2; i++)
                sccs &= int.TryParse(tokens[i + 2], out sizeValue[i]);
            if (!sccs || sizeValue[0] <= 0 || sizeValue[1] <= 0 || sizeValue[0] > AbstractImage.MAX_IMAGESIZE || sizeValue[1] > AbstractImage.MAX_IMAGESIZE)
            {
                ParserMediator.Warn(LocalizationManager.Error.InvalidAnimationSpriteSize, sp, 1);
                return null;
            }
            SpriteAnime anime = new(name, new Size(sizeValue[0], sizeValue[1]));

            return anime;
        }
        //アニメ宣言以外（アニメ用フレーム含む

        if (arg2.IndexOf('.') < 0)
        {
            ParserMediator.Warn(string.Format(LocalizationManager.Error.MissingSecondArgumentExtension, arg2), sp, 1);
            return null;
        }
        string parentName = dir + arg2;


        //親画像のロードConstImage
        if (!resourceDic.TryGetValue(parentName, out AbstractImage value))
        {
            string filepath = parentName;

            var skImage = SKImage.FromEncodedData(filepath);
            if (skImage == null)
            {
                ParserMediator.Warn(string.Format(LocalizationManager.Error.FailedLoadFile, arg2), sp, 1);
                return null;
            }

            if (skImage.Width > AbstractImage.MAX_IMAGESIZE || skImage.Height > AbstractImage.MAX_IMAGESIZE)
            {
                //1824-2 すでに8192以上の幅を持つ画像を利用したバリアントが存在してしまっていたため、警告しつつ許容するように変更
                //	bmp.Dispose();
                ParserMediator.Warn(string.Format(LocalizationManager.Error.TooLargeImageFile, AbstractImage.MAX_IMAGESIZE.ToString(), arg2), sp, 1);
                //return null;
            }
            ConstImage img = new(parentName);
            img.CreateFrom(skImage);
            if (!img.IsCreated)
            {
                ParserMediator.Warn(string.Format(LocalizationManager.Error.FailedCreateResource, arg2), sp, 1);
                return null;
            }

            value = img;
            resourceDic.TryAdd(parentName, value);
        }
        if (value is not ConstImage parentImage || !parentImage.IsCreated)
        {
            ParserMediator.Warn(string.Format(LocalizationManager.Error.SpriteCreateFromFailedResource, arg2), sp, 1);
            return null;
        }
        var rect = new Rectangle(new Point(0, 0), new Size(parentImage.Image.Width, parentImage.Image.Height));
        Point pos = new();
        int delay = 1000;
        //name,parentname, x,y,w,h ,offset_x,offset_y, delayTime
        if (tokens.Length >= 6)//x,y,w,h
        {
            int[] rectValue = new int[4];
            bool sccs = true;
            for (int i = 0; i < 4; i++)
                sccs &= int.TryParse(tokens[i + 2], out rectValue[i]);
            if (sccs)
            {
                rect = new Rectangle(rectValue[0], rectValue[1], rectValue[2], rectValue[3]);
                if (rect.Width <= 0 || rect.Height <= 0)
                {
                    ParserMediator.Warn(string.Format(LocalizationManager.Error.SpriteSizeIsNegatibe, name), sp, 1);
                    return null;
                }
                if (!rect.IntersectsWith(new Rectangle(0, 0, parentImage.Image.Width, parentImage.Image.Height)))
                {
                    ParserMediator.Warn(string.Format(LocalizationManager.Error.OoRParentImage, name), sp, 1);
                    return null;
                }
            }
            if (tokens.Length >= 8)
            {
                sccs = true;
                for (int i = 0; i < 2; i++)
                    sccs &= int.TryParse(tokens[i + 6], out rectValue[i]);
                if (sccs)
                    pos = new Point(rectValue[0], rectValue[1]);
                if (tokens.Length >= 9)
                {
                    sccs = int.TryParse(tokens[8], out delay);
                    if (sccs && delay <= 0)
                    {
                        ParserMediator.Warn(string.Format(LocalizationManager.Error.FrameTimeIsNegative, name), sp, 1);
                        return null;
                    }
                }
            }
        }
        //既存のスプライトに対するフレーム追加
        if (currentAnime != null && currentAnime.Name == name)
        {
            if (!currentAnime.AddFrame(parentImage, rect, pos, delay))
            {
                ParserMediator.Warn(string.Format(LocalizationManager.Error.FailedAddSpriteFrame, arg2), sp, 1);
                return null;
            }
            return null;
        }

        //新規スプライト定義
        ASprite image = new SpriteF(name, parentImage, rect, pos);
        return image;
    }



}
