using System.Collections.Generic;
using System.Drawing;
using MinorShift.Emuera;

namespace Emuera
{
    static class FontFactory
    {
        static readonly Dictionary<(string fontname, FontStyle fontStyle), Font> fontDic = [];

        public static Font GetFont(string requestFontName, FontStyle style)
        {
            string fontname = requestFontName;
            if (string.IsNullOrEmpty(requestFontName))
                fontname = Config.FontName;
            if (!fontDic.ContainsKey((fontname, style)))
            {
                var font = new Font(fontname, Config.FontSize, style, GraphicsUnit.Pixel);
                if (font == null)
                {
                    return null;
                }

                fontDic.Add((fontname, style), font);
            }
            return fontDic[(fontname, style)];
        }
    }
}