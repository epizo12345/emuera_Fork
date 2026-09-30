using MinorShift.Emuera.Runtime;
using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Web.Runtime;
using SkiaSharp;
using System.Drawing;
using System.Text;

namespace MinorShift.Emuera.UI
{
    internal static class FontFactory
    {
        const string PortableFamily = "DotGothic16";
        const string PortableFontResource = "MinorShift.Emuera.Web.Runtime.Fonts.DotGothic16-Regular.ttf";
        public static List<string> AvailableFonts { get; } = ["sans-serif", PortableFamily];
        public static bool CheckExternalFont(string fontname) => FamilyNameMatches(fontname, PortableFamily);

        static readonly Dictionary<(string Family, float Size, FontStyle Style), SKFont> fonts = [];
        static readonly Lazy<SKTypeface> portableTypeface = new(LoadPortableTypeface);

        public static SKFont GetFont(string familyName, FontStyle style, float? fontSize = null)
        {
            if (string.IsNullOrEmpty(familyName)) familyName = Config.FontName;
            fontSize ??= Config.FontSize;
            var key = (familyName, fontSize.Value, style);
            if (fonts.TryGetValue(key, out SKFont cached)) return cached;

            SKFontStyle skStyle = style.HasFlag(FontStyle.Bold) ? SKFontStyle.Bold : SKFontStyle.Normal;
            bool isPortable = FamilyNameMatches(familyName, PortableFamily);
            SKTypeface typeface = isPortable
                ? portableTypeface.Value
                : SKTypeface.FromFamilyName(familyName, skStyle)
                    ?? SKTypeface.FromFamilyName("sans-serif", skStyle)
                    ?? SKTypeface.Default;
            if (!isPortable && OperatingSystem.IsBrowser() && !FamilyNameMatches(familyName, typeface.FamilyName))
            {
                // 既定Typefaceの共有インスタンスは残し、一時的に解決した別Typefaceだけを解放する。
                if (!ReferenceEquals(typeface, SKTypeface.Default)) typeface.Dispose();
                typeface = portableTypeface.Value;
                isPortable = true;
            }

            var font = new SKFont(typeface, fontSize.Value);
            if (isPortable && style.HasFlag(FontStyle.Bold)) font.Embolden = true;
            if (style.HasFlag(FontStyle.Italic)) font.SkewX = -0.3f;
            fonts.Add(key, font);
            // ブラウザに設定書体がない場合、WASM標準書体では100px文字のbaseline計算が表示枠をはみ出すため、同梱OFL書体へ切り替える。
            // 返すSKFontはキャッシュ所有で、GraphicsImageは借用参照だけを保持する。
            return font;
        }

        static SKTypeface LoadPortableTypeface()
        {
            using Stream stream = typeof(FontFactory).Assembly.GetManifestResourceStream(PortableFontResource)
                ?? throw new InvalidOperationException($"Embedded font missing: {PortableFontResource}");
            return SKTypeface.FromStream(stream)
                ?? throw new InvalidOperationException($"Embedded font could not be loaded: {PortableFontResource}");
        }

        static bool FamilyNameMatches(string requested, string resolved)
        {
            // Browser WASMではNormalizeが未対応のため、必要な全角ASCIIと空白だけを明示的に揃える。
            string Normalize(string value)
            {
                var result = new StringBuilder(value.Length);
                foreach (char character in value)
                {
                    if (character is ' ' or '\u3000') continue;
                    result.Append(character is >= '\uFF01' and <= '\uFF5E' ? (char)(character - 0xFEE0) : character);
                }
                return result.ToString();
            }
            string requestedName = Normalize(requested), resolvedName = Normalize(resolved);
            if (string.Equals(requestedName, resolvedName, StringComparison.OrdinalIgnoreCase)) return true;
            return IsMsGothic(requestedName) && IsMsGothic(resolvedName);

            static bool IsMsGothic(string name) => string.Equals(name, "MSゴシック", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "MSGOTHIC", StringComparison.OrdinalIgnoreCase);
        }
    }
}

namespace MinorShift.Emuera.UI.Framework
{
    internal static class Dialog
    {
        public static void Show(string title, string message) =>
            throw new UnsupportedRuntimeFeatureException("dialog", $"{title}: {message}");

        public static bool ShowPrompt(string title, string message) =>
            throw new UnsupportedRuntimeFeatureException("dialog prompt", $"{title}: {message}");

        public static bool ShowInfiniteLoopPrompt(string title, string message) =>
            throw new UnsupportedRuntimeFeatureException("LONG_RUNNING_PROMPT_NOT_IMPLEMENTED", $"{title}: {message}");
    }
}

namespace MinorShift.Emuera.UI.Game
{
    internal enum DisplayLineAlignment { LEFT, CENTER, RIGHT }

    internal struct StringStyle
    {
        public Color Color;
        public Color ButtonColor;
        public bool ColorChanged;
        public FontStyle FontStyle;
        public string Fontname;
        public int FontSize;
    }

    internal sealed class ConsoleDisplayLine(BrowserDisplayLine line)
    {
        internal BrowserDisplayLine Line { get; } = line;
    }

    internal static class HtmlManager
    {
        public static string[] HtmlTagSplit(string value) => throw new UnsupportedRuntimeFeatureException("HTML");
        public static string DisplayLine2Html(ConsoleDisplayLine[] lines, bool needPandN)
        {
            if (lines.Length == 0) return string.Empty;
            var html = new System.Text.StringBuilder();
            if (needPandN) html.Append("<p align='").Append(lines[0].Line.Alignment).Append("'><nobr>");
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0) html.Append("<br>");
                foreach (BrowserDisplayPart part in lines[i].Line.Parts) AppendPart(html, part);
            }
            if (needPandN) html.Append("</nobr></p>");
            return html.ToString();
        }

        static void AppendPart(System.Text.StringBuilder html, BrowserDisplayPart part)
        {
            if (part.Kind is BrowserDisplayPartKind.Image or BrowserDisplayPartKind.Shape)
                throw new UnsupportedRuntimeFeatureException("HTML display history image/shape");
            if (part.Kind == BrowserDisplayPartKind.Break) { html.Append("<br>"); return; }
            bool button = part.Kind == BrowserDisplayPartKind.Button;
            bool nonbutton = part.Kind == BrowserDisplayPartKind.NonButton;
            if (button || nonbutton)
            {
                html.Append(button ? "<button" : "<nonbutton");
                if (button) html.Append(" value='").Append(Escape(part.Input ?? string.Empty)).Append("'");
                if (part.Tooltip is not null) html.Append(" title='").Append(Escape(part.Tooltip)).Append("'");
                html.Append('>');
            }
            if (part.Children is not null)
                foreach (BrowserDisplayPart child in part.Children) AppendPart(html, child);
            else
            {
                if (part.Style?.Bold == true) html.Append("<b>");
                if (part.Style?.Italic == true) html.Append("<i>");
                if (part.Style?.Underline == true) html.Append("<u>");
                html.Append(Escape(part.Text));
                if (part.Style?.Underline == true) html.Append("</u>");
                if (part.Style?.Italic == true) html.Append("</i>");
                if (part.Style?.Bold == true) html.Append("</b>");
            }
            if (button || nonbutton) html.Append(button ? "</button>" : "</nonbutton>");
        }

        public static string Html2PlainText(string value) => BrowserHtmlParser.PlainText(value);
        public static string Escape(string value) => System.Web.HttpUtility.HtmlEncode(value);
    }
}

namespace MinorShift.Emuera.GameView
{
    internal enum ConsoleRedraw { None, Normal }
}
