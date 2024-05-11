using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.UI.Game;
using SkiaSharp;
using System.Collections.Generic;
using System.Drawing;

namespace MinorShift.Emuera.UI;

static class FontFactory
{
    static readonly Dictionary<(string fontname, float fontSize, FontStyle fontStyle), SKFont> fontDic = [];

    public static SKFont GetFont(StringStyle stringStyle)
    {
        return GetFont(stringStyle.Fontname, stringStyle.FontStyle);
    }
    public static SKFont GetFont(string requestFontName, FontStyle style, float? fontSize = null)
    {
        string fontname = requestFontName;
        if (string.IsNullOrEmpty(requestFontName))
            fontname = Config.FontName;
        fontSize ??= Config.FontSize;
        if (!fontDic.ContainsKey((fontname, fontSize.Value, style)))
        {
            var font = new SKFont(SKTypeface.FromFamilyName(fontname), fontSize.Value);
            if (font == null)
            {
                return null;
            }

            fontDic.Add((fontname, fontSize.Value, style), font);
        }
        return fontDic[(fontname, fontSize.Value, style)];
    }
}
