using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using MinorShift.Emuera.Runtime.Config;
using System.Globalization;
using System.Net;

namespace MinorShift.Emuera.Web.Runtime;

internal static class BrowserHtmlParser
{
    static readonly HtmlParser Parser = new();

    internal static string PlainText(string html) => Parser.ParseDocument($"<body>{html}</body>").Body?.TextContent ?? string.Empty;

    internal static bool Append(
        string html,
        BrowserDisplayStyle baseStyle,
        Func<string, string?> imageResolver,
        Action<BrowserDisplayPart> add,
        Action lineBreak)
    {
        IDocument document = Parser.ParseDocument($"<body>{html.ReplaceLineEndings("<br>")}</body>");
        ParseNodes(document.Body?.ChildNodes ?? throw new InvalidDataException("HTML body is missing"), baseStyle, null, imageResolver, add, lineBreak);
        // Native's NOBR disables automatic wrapping for the whole HTML_PRINT,
        // while explicit BR nodes still produce physical lines.
        return document.QuerySelector("nobr") is not null;
    }

    static void ParseNodes(
        INodeList nodes,
        BrowserDisplayStyle style,
        BrowserDisplayLayout? layout,
        Func<string, string?> imageResolver,
        Action<BrowserDisplayPart> add,
        Action lineBreak)
    {
        foreach (INode node in nodes)
        {
            if (node.NodeType == NodeType.Text)
            {
                string text = WebUtility.HtmlDecode(node.TextContent);
                if (text.Length != 0)
                    add(new(BrowserDisplayPartKind.Text, text, Style: style, Layout: layout));
                continue;
            }

            if (node is not IElement element)
                continue;
            string name = element.LocalName.ToUpperInvariant();
            switch (name)
            {
                case "BR":
                    lineBreak();
                    break;
                case "NOBR":
                    ParseNodes(element.ChildNodes, style, layout, imageResolver, add, lineBreak);
                    break;
                case "P":
                    ParseNodes(element.ChildNodes, style, layout, imageResolver, add, lineBreak);
                    break;
                case "FONT":
                    ParseNodes(element.ChildNodes, FontStyle(element, style), layout, imageResolver, add, lineBreak);
                    break;
                case "B":
                case "STRONG":
                    ParseNodes(element.ChildNodes, style with { Bold = true }, layout, imageResolver, add, lineBreak);
                    break;
                case "I":
                case "EM":
                    ParseNodes(element.ChildNodes, style with { Italic = true }, layout, imageResolver, add, lineBreak);
                    break;
                case "U":
                    ParseNodes(element.ChildNodes, style with { Underline = true }, layout, imageResolver, add, lineBreak);
                    break;
                case "S":
                case "STRIKE":
                    ParseNodes(element.ChildNodes, style with { Strikeout = true }, layout, imageResolver, add, lineBreak);
                    break;
                case "DIV":
                    BrowserDisplayStyle groupStyle = ElementStyle(element, style);
                    add(new(
                        BrowserDisplayPartKind.Group,
                        Style: groupStyle,
                        Layout: MergeLayout(layout, element),
                        Children: ParseChildren(element, groupStyle, imageResolver)));
                    break;
                case "IMG":
                    add(ImagePart(element, style, layout, imageResolver));
                    break;
                case "SHAPE":
                    add(ShapePart(element, style));
                    // The desktop parser accepts a shape with children when an HTML
                    // producer happened to close the tag late.
                    ParseNodes(element.ChildNodes, style, layout, imageResolver, add, lineBreak);
                    break;
                case "BUTTON":
                case "NONBUTTON":
                    AddButton(element, name == "BUTTON", style, layout, imageResolver, add);
                    break;
                case "SCRIPT":
                case "STYLE":
                    throw new UnsupportedRuntimeFeatureException($"unsafe HTML element {name}");
                default:
                    throw new UnsupportedRuntimeFeatureException($"HTML element {name}");
            }
        }
    }

    static void AddButton(
        IElement element,
        bool selectableElement,
        BrowserDisplayStyle style,
        BrowserDisplayLayout? layout,
        Func<string, string?> imageResolver,
        Action<BrowserDisplayPart> add)
    {
        string? input = selectableElement ? element.GetAttribute("value") : null;
        bool selectable = input is not null;
        string text = WebUtility.HtmlDecode(element.TextContent);
        add(new(
            selectable ? BrowserDisplayPartKind.Button : BrowserDisplayPartKind.NonButton,
            text,
            input,
            selectable && long.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
            Style: style,
            Layout: layout,
            Tooltip: element.GetAttribute("title")?.Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase),
            Children: ParseChildren(element, style, imageResolver),
            LockedX: element.GetAttribute("pos") is { Length: > 0 } pos
                ? int.Parse(pos, CultureInfo.InvariantCulture) * Config.FontSize / 100 : null));
    }

    static IReadOnlyList<BrowserDisplayPart> ParseChildren(
        IElement element,
        BrowserDisplayStyle style,
        Func<string, string?> imageResolver)
    {
        List<BrowserDisplayPart> children = [];
        ParseNodes(
            element.ChildNodes,
            style,
            null,
            imageResolver,
            children.Add,
            () => children.Add(new(BrowserDisplayPartKind.Break)));
        return children;
    }

    static BrowserDisplayPart ImagePart(IElement element, BrowserDisplayStyle style, BrowserDisplayLayout? layout, Func<string, string?> imageResolver)
    {
        string source = element.GetAttribute("src") ?? string.Empty;
        return new(
            BrowserDisplayPartKind.Image,
            source,
            ImageDataUrl: imageResolver(source),
            Height: ParseSize(element.GetAttribute("height"), Config.FontSize),
            Width: ParseSize(element.GetAttribute("width"), 0),
            Style: style,
            Layout: MergeLayout(layout, element),
            Tooltip: element.GetAttribute("title"));
    }

    static BrowserDisplayPart ShapePart(IElement element, BrowserDisplayStyle style)
    {
        string type = element.GetAttribute("type")?.Trim().ToLowerInvariant() ?? string.Empty;
        int[] values;
        try
        {
            values = (element.GetAttribute("param") ?? string.Empty)
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(value => int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture))
                .ToArray();
        }
        catch (Exception) when (type is "rect" or "space")
        {
            return new(BrowserDisplayPartKind.Text, ShapeFallback(element), Style: style);
        }

        return CreateShapePart(type, values, style, ShapeFallback(element), element.GetAttribute("color"), element.GetAttribute("bcolor"));
    }

    internal static BrowserDisplayPart ShapePart(string type, int[] parameters, BrowserDisplayStyle style)
    {
        string fallback = ShapeFallback(type, parameters, style);
        return CreateShapePart(type.ToLowerInvariant(), parameters, style, fallback);
    }

    static BrowserDisplayPart CreateShapePart(string type, int[] values, BrowserDisplayStyle style, string fallback, string? color = null, string? buttonColor = null)
    {
        // Native validates source units before integer drawing dimensions are truncated.
        bool validRect = values is [var oneUnit] && oneUnit > 0
            || values is [var leftUnit, _, var widthUnit, var heightUnit] && leftUnit >= 0 && widthUnit > 0 && heightUnit > 0;
        values = values.Select(value => (int)((float)value * Config.FontSize / 100f)).ToArray();
        int x;
        int y;
        int width;
        int height;
        bool filled;
        if (type == "space" && values is [var space] && space >= 0)
        {
            x = y = 0;
            width = space;
            height = Config.FontSize;
            filled = false;
        }
        else if (type == "rect" && validRect && values is [var one])
        {
            x = y = 0;
            width = one;
            height = Config.FontSize;
            filled = true;
        }
        else if (type == "rect" && validRect && values is [var left, var top, var rectWidth, var rectHeight])
        {
            x = left;
            y = top;
            width = rectWidth;
            height = Math.Max(1, rectHeight);
            filled = true;
        }
        else
        {
            return new(BrowserDisplayPartKind.Text, fallback, Style: style);
        }

        int lineTop = Math.Min(0, y);
        int lineBottom = Math.Max(Config.FontSize, y + height);
        BrowserDisplayStyle shapeStyle = style with
        {
            Background = filled ? ParseColor(color, style.Foreground) : null,
            ButtonColor = ParseColor(buttonColor, style.ButtonColor)
        };
        return new(
            BrowserDisplayPartKind.Shape,
            Width: x + width,
            Height: lineBottom - lineTop,
            Style: shapeStyle,
            Layout: new(BrowserDisplayMode.Relative, x, y - lineTop, width, height),
            Tooltip: fallback);
    }

    static string ShapeFallback(string type, int[] parameters, BrowserDisplayStyle style)
    {
        var builder = new System.Text.StringBuilder($"<shape type='{type.ToLowerInvariant()}' param='{string.Join(", ", parameters)}'");
        if (style.Foreground != ToCss(Config.ForeColor)) builder.Append(" color='").Append(style.Foreground).Append('\'');
        if (style.ButtonColor != ToCss(Config.FocusColor)) builder.Append(" bcolor='").Append(style.ButtonColor).Append('\'');
        return builder.Append('>').ToString();
    }

    static string ShapeFallback(IElement element)
    {
        string type = element.GetAttribute("type") ?? string.Empty;
        string parameter = element.GetAttribute("param") ?? string.Empty;
        return $"<shape type='{type}' param='{parameter}'>";
    }

    static string ToCss(System.Drawing.Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    static BrowserDisplayStyle FontStyle(IElement element, BrowserDisplayStyle style) => style with
    {
        Foreground = ParseColor(element.GetAttribute("color"), style.Foreground),
        ButtonColor = ParseColor(element.GetAttribute("bcolor"), style.ButtonColor),
        FontName = element.GetAttribute("face") ?? style.FontName,
        FontSize = ParseSize(element.GetAttribute("size"), style.FontSize)
    };

    static BrowserDisplayStyle ElementStyle(IElement element, BrowserDisplayStyle style) => style with
    {
        Background = ParseColor(element.GetAttribute("background_color"), null)
    };

    static BrowserDisplayLayout MergeLayout(BrowserDisplayLayout? parent, IElement element)
    {
        BrowserDisplayLayout current = parent ?? new();
        string? display = element.GetAttribute("display");
        BrowserDisplayMode mode = display?.ToLowerInvariant() switch
        {
            null or "" => current.Mode,
            "relative" => BrowserDisplayMode.Relative,
            "absolute" => BrowserDisplayMode.Absolute,
            "absolute-lefttop" => BrowserDisplayMode.AbsoluteLeftTop,
            "absolute-leftbottom" => BrowserDisplayMode.AbsoluteLeftBottom,
            _ => throw new UnsupportedRuntimeFeatureException($"HTML display {display}")
        };
        return current with
        {
            Mode = mode,
            X = current.X + ParseSize(element.GetAttribute("xpos"), 0),
            Y = current.Y + ParseSize(element.GetAttribute("ypos"), 0),
            Width = ParseNullableSize(element.GetAttribute("width")) ?? current.Width,
            Height = ParseNullableSize(element.GetAttribute("height")) ?? current.Height,
            Padding = ParseSize(element.GetAttribute("padding"), current.Padding),
            BorderWidth = ParseNullableSize(element.GetAttribute("border_width")),
            BorderColor = ParseColor(element.GetAttribute("border_color"), "#FFFFFF"),
            ExplicitPosition = current.ExplicitPosition || element.HasAttribute("xpos") || element.HasAttribute("ypos") || element.HasAttribute("display"),
        };
    }

    internal static int ParseSize(string? value, int fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        bool pixels = value.EndsWith("px", StringComparison.OrdinalIgnoreCase);
        string number = pixels ? value[..^2] : value;
        if (!int.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out int raw))
            throw new InvalidDataException($"HTML size is invalid: {value}");
        return pixels ? raw : raw * Config.FontSize / 100;
    }

    static int? ParseNullableSize(string? value) => string.IsNullOrWhiteSpace(value) ? null : ParseSize(value, 0);

    static string? ParseColor(string? value, string? fallback)
    {
        if (string.IsNullOrEmpty(value)) return fallback;
        string color = value;
        if (color.StartsWith('#'))
        {
            if (int.TryParse(color.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb)
                && rgb is >= 0 and <= 0xFFFFFF)
                return $"#{rgb:X6}";
        }
        else
        {
            System.Drawing.Color named = System.Drawing.Color.FromName(color);
            if (named.A != 0)
                return $"#{named.R:X2}{named.G:X2}{named.B:X2}";
        }
        throw new InvalidDataException($"HTML color is invalid: {value}");
    }
}
