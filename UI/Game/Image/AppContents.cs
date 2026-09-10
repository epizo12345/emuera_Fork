using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Runtime.Utils;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Linq;
using MinorShift.Emuera.UI.Framework;

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

#if R0_F4G1
    internal readonly record struct R0F4G1ImageState(string Kind, string Key, bool Present, bool Created, int Width, int Height);
    internal static R0F4G1ImageState R0F4G1GraphicsState(long id) => gList.TryGetValue(id, out var value)
        ? new("Graphics", id.ToString(System.Globalization.CultureInfo.InvariantCulture), true, value.IsCreated, value.Width, value.Height)
        : new("Graphics", id.ToString(System.Globalization.CultureInfo.InvariantCulture), false, false, 0, 0);
    internal static R0F4G1ImageState R0F4G1SpriteState(string name) => imageDictionary.TryGetValue(name.ToUpper(), out var value)
        ? new("Sprite", name, true, value.IsCreated, value.DestBaseSize.Width, value.DestBaseSize.Height)
        : new("Sprite", name, false, false, 0, 0);
#endif

#if R0_F4G2
    internal readonly record struct R0F4G2ResourceState(string Kind, string Key, bool Present, bool Created,
        int Width, int Height, int Frames, long TotalDelayMs, string ContentSha256);
    internal static R0F4G2ResourceState R0F4G2GraphicsState(long id) => gList.TryGetValue(id, out var value)
        ? new("Graphics", id.ToString(System.Globalization.CultureInfo.InvariantCulture), true, value.IsCreated,
            value.Width, value.Height, 0, 0, value.R0F4G2ContentSha256())
        : new("Graphics", id.ToString(System.Globalization.CultureInfo.InvariantCulture), false, false, 0, 0, 0, 0, "");
    internal static R0F4G2ResourceState R0F4G2SpriteState(string name) => imageDictionary.TryGetValue(name.ToUpper(), out var value)
        ? new("Sprite", name, true, value.IsCreated, value.DestBaseSize.Width, value.DestBaseSize.Height,
            value is SpriteAnime anime ? anime.R0F4G2FrameCount : 0,
            value is SpriteAnime timed ? timed.totaltime : 0, "")
        : new("Sprite", name, false, false, 0, 0, 0, 0, "");
#endif

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
            var csvFiles = Directory.EnumerateFiles(Program.ContentDir, "*.csv", SearchOption.AllDirectories);
            csvFiles.AsParallel()
                .Where(path => Path.GetExtension(path).Equals(".csv", StringComparison.OrdinalIgnoreCase))
                .ForAll(path =>
                {
                    //アニメスプライト宣言。nullでないとき、フレーム追加モード
                    SpriteAnime currentAnime = null;
                    string directory = Path.GetDirectoryName(path) + "\\";
                    string filename = Path.GetFileName(path);
                    string[] lines = File.ReadAllLines(path, Config.Encode);
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
    static private AContentItem CreateFromCsv(string[] tokens, string dir, SpriteAnime currentAnime, ScriptPosition? sp)
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
