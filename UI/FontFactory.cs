using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Runtime.Config.JSON;
using MinorShift.Emuera.UI.Game;
using SkiaSharp;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Linq;

namespace MinorShift.Emuera.UI;

static class FontFactory
{
    static Dictionary<string, SKTypeface> _typefaces = [];
    
    //フォントを事前読み込み
    public static readonly List<string> AvailableFonts =
        new InstalledFontCollection().Families.Where(FontCheck).Select(x => x.Name).ToList();
    
    private static bool FontCheck(FontFamily ff)
    {
        return ff.IsStyleAvailable(FontStyle.Regular) &&
               ff.IsStyleAvailable(FontStyle.Bold) &&
               ff.IsStyleAvailable(FontStyle.Italic) &&
               ff.IsStyleAvailable(FontStyle.Strikeout) &&
               ff.IsStyleAvailable(FontStyle.Underline);
    }

    public static void LoadFontFolder()
    {
        var fontFolder = new DirectoryInfo(Program.ExeDir + "font");
        
        if (!fontFolder.Exists)
            return;

        foreach (var fontFile in fontFolder.EnumerateFiles("", SearchOption.AllDirectories))
        {
            if(!(fontFile.Extension.Equals(".ttf") || fontFile.Extension.Equals(".otf")))
                continue;
            
            var typeface = SKTypeface.FromFile(fontFile.FullName);
            
            if (typeface == null)
                continue;
            AvailableFonts.Add(typeface.FamilyName);
            _typefaces[typeface.FamilyName] = typeface;
            _typefaces[fontFile.Name] = typeface;
        }
        AvailableFonts.Sort();
    }

    static readonly Dictionary<(string fontname, float fontSize, FontStyle fontStyle), SKFont> fontDic = [];

    public static bool CheckExternalFont(string fontname)
    {
        return _typefaces.ContainsKey(fontname);
    }

    public static SKFont GetFont(StringStyle stringStyle)
    {
        return GetFont(stringStyle.Fontname, stringStyle.FontStyle, stringStyle.FontSize);
    }
    public static SKFont GetFont(string familyName, FontStyle style, float? fontSize = null)
    {
        if (string.IsNullOrEmpty(familyName))
            familyName = Config.FontName;
        fontSize ??= Config.FontSize;
        if (!fontDic.ContainsKey((familyName, fontSize.Value, style)))
        {
            if (!_typefaces.TryGetValue(familyName, out SKTypeface typeface))
            {
                var fontStyle = SKFontStyle.Normal;
                if (style.HasFlag(FontStyle.Bold))
                {
                    fontStyle = SKFontStyle.Bold;
                }

                typeface = SKTypeface.FromFamilyName(familyName, fontStyle);
            }

            var font = new SKFont(typeface, fontSize.Value);
            //font.Size *= fontSize.Value / font.Spacing;
            if (font == null)
            {
                return null;
            }

            ApplyAntialiasSetting(font);
            if (style.HasFlag(FontStyle.Italic))
                ApplyItalic(font);

            fontDic.Add((familyName, fontSize.Value, style), font);
        }
        return fontDic[(familyName, fontSize.Value, style)];
    }

    /// <summary>設定画面の文字のアンチエイリアス設定をフォントに反映する。</summary>
    public static void ApplyAntialiasSetting(SKFont font)
    {
        switch (JSONConfig.Game.FontAntialias)
        {
            case FontAntialias.None:
                font.Hinting = SKFontHinting.None;
                font.Edging = SKFontEdging.Alias;
                break;
            case FontAntialias.Normal:
                break;
            case FontAntialias.Full:
                font.Hinting = SKFontHinting.Full;
                font.Edging = SKFontEdging.SubpixelAntialias;
                break;
        }
    }

    // [Emuera改修:DRAW-09 2026-10-28]
    // 斜体は書体を傾けて作る（ＭＳ ゴシック等に斜体の書体がないため）。傾けた文字は内蔵のドット絵ではなく輪郭から描かれるので、
    // 通常の文字をドット絵（アンチエイリアスなし）で描くフォントでは斜体だけにじんでいた。元Emuera（GDI）の斜体はくっきりしているため、
    // 通常の文字がドット絵で描かれるフォントと大きさでは、斜体もアンチエイリアスなしで描く。滑らかに描くフォントは従来どおり。
    // 判定はフォント・大きさ・設定の組み合わせごとに1回、試し描きして中間の濃さが出るかで行う。
    // 太字はWindowsが普通の書体から擬似的に作る場合があり（ＭＳ ゴシック等）、その試し描きには中間の濃さが混ざるため、
    // 判定は同じフォントの普通の太さの書体で行う（太字の斜体も元Emueraと同じくアンチエイリアスなしにする）。
    static readonly Dictionary<(SKTypeface, float, SKFontEdging, SKFontHinting), bool> bitmapRenderCache = [];
    static readonly Dictionary<string, SKTypeface> regularTypefaceCache = [];
    static readonly object bitmapRenderLock = new();

    /// <summary>斜体にする。ApplyAntialiasSettingの後に呼ぶ。</summary>
    public static void ApplyItalic(SKFont font)
    {
        bool bitmap = RendersAsBitmap(font);
        font.SkewX = -0.3f;
        if (bitmap)
            font.Edging = SKFontEdging.Alias;
    }

    static SKTypeface RegularTypefaceOf(SKTypeface typeface)
    {
        if (typeface == null || typeface.FontStyle.Weight <= (int)SKFontStyleWeight.Normal)
            return typeface;
        string family = typeface.FamilyName;
        lock (bitmapRenderLock)
        {
            if (regularTypefaceCache.TryGetValue(family, out SKTypeface cached))
                return cached ?? typeface;
        }
        SKTypeface regular = SKTypeface.FromFamilyName(family, SKFontStyle.Normal);
        // 見つからず別のフォントが返った場合（外部フォント等）は元の書体で判定する。
        if (regular != null && regular.FamilyName != family)
            regular = null;
        lock (bitmapRenderLock)
            regularTypefaceCache[family] = regular;
        return regular ?? typeface;
    }

    static bool RendersAsBitmap(SKFont font)
    {
        if (font.Edging == SKFontEdging.Alias)
            return true;
        SKTypeface probeTypeface = RegularTypefaceOf(font.Typeface);
        var key = (probeTypeface, font.Size, font.Edging, font.Hinting);
        lock (bitmapRenderLock)
        {
            if (bitmapRenderCache.TryGetValue(key, out bool cached))
                return cached;
        }
        bool result = true;
        int size = System.Math.Max(8, (int)System.Math.Ceiling(font.Size));
        using (var probe = new SKFont(probeTypeface, font.Size) { Edging = font.Edging, Hinting = font.Hinting })
        using (var bitmap = new SKBitmap(new SKImageInfo(size * 4, size * 2, SKColorType.Alpha8, SKAlphaType.Premul)))
        using (var canvas = new SKCanvas(bitmap))
        using (var paint = new SKPaint { Color = SKColors.White })
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawText("あAg", new SKPoint(1, 1 - probe.Metrics.Top), SKTextAlign.Left, probe, paint);
            foreach (byte alpha in bitmap.GetPixelSpan())
            {
                if (alpha != 0 && alpha != 255)
                {
                    result = false;
                    break;
                }
            }
        }
        lock (bitmapRenderLock)
            bitmapRenderCache[key] = result;
        return result;
    }
}
