using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Runtime.Config.JSON;
using MinorShift.Emuera.UI.Game;
using SkiaSharp;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace MinorShift.Emuera.UI;

static class FontFactory
{
    static Dictionary<string, SKTypeface> _typefaces = [];

    public static void LoadFontFolder()
    {
        var fontFolder = new DirectoryInfo(Program.ExeDir + "font");
        if (fontFolder.Exists)
        {
            foreach (var fontFile in fontFolder.EnumerateFiles("", SearchOption.AllDirectories))
            {
                var typeface = SKTypeface.FromFile(fontFile.FullName);
                if (typeface != null)
                {
                    _typefaces[typeface.FamilyName] = typeface;
                    _typefaces[fontFile.Name] = typeface;
                }
            }
        }
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
            if (style.HasFlag(FontStyle.Italic))
            {
                font.SkewX = -0.3f;
            }
            //font.Size *= fontSize.Value / font.Spacing;
            if (font == null)
            {
                return null;
            }

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

            fontDic.Add((familyName, fontSize.Value, style), font);
        }
        return fontDic[(familyName, fontSize.Value, style)];
    }
}
