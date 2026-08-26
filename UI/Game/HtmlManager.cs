using AngleSharp.Dom;
using MinorShift.Emuera.GameView;
using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Runtime.Utils;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Text.RegularExpressions;
using MinorShift.Emuera.UI.Framework;
using System.Globalization;

namespace MinorShift.Emuera.UI.Game;

//TODO:1810～
/* Emuera用Htmlもどきが実装すべき要素
	 * (できるだけhtmlとConsoleDisplayLineとの1:1対応を目指す。<b>と<strong>とか同じ結果になるタグを重複して実装しない)
	 * <p align=""></p> ALIGNMENT命令相当・行頭から行末のみ・行末省略可
	 * <nobr></nobr> PRINTSINGLE相当・行頭から行末のみ・行末省略可
	 * <b><i><u><s> フォント各種・オーバーラップ問題は保留
	 * <button value=""></button> ボタン化・htmlでは明示しない限りボタン化しない
	 * <font face="" color="" bcolor=""></font> フォント指定 色指定 ボタン選択中色指定
	 * 追加<!-- --> コメント
	 * <nonbutton title='～～'> 
	 * <img src='～～' srcb='～～'> 
	 * <shape type='rect' param='0,0,0,0'> 
	 * エスケープ
	 * &amp; &gt; &lt; &quot; &apos; &<>"' ERBにおける利便性を考えると属性値の指定には"よりも'を使いたい。HTML4.1にはないがaposを入れておく
	 * &#nn; &#xnn; Unicode参照 #xFFFF以上は却下
	 */
/* このクラスがサポートすべきもの
	 * html から ConsoleDisplayLine[] //主に表示用
	 * ConsoleDisplayLine[] から html //現在の表示をstr化して保存？
	 * html から ConsoleDisplayLine[] を経て html //表示を行わずに改行が入る位置のチェックができるかも
	 * html から PlainText(非エスケープ)//
	 * Text から エスケープ済Text
	 */
/// <summary>
/// EmueraConsoleのなんちゃってHtml解決用クラス
/// </summary>
internal static partial class HtmlManager
{
    static readonly Dictionary<char, string> repDic = new()
    {
        { '&', "&amp;" },
        { '>', "&gt;" },
        { '<', "&lt;" },
        { '\"', "&quot;" },
        { '\'', "&apos;" }
    };

    class DivState
    {
        public bool IsDiv;//divタグの解析中
        public int PosX;
        public int PosY;
        public DisplayMode Display;
        public SKColor BackgroundColor;

        public int Width = -1;
        public int Height = -1;

        public bool HasBorder = false;
        public int BorderWidth;
        public SKColor BorderColor;
        public SKRect? Padding;
    }

    /// <summary>
    /// 表示行からhtmlへの変換
    /// </summary>
    /// <param name="lines"></param>
    /// <returns></returns>
    public static string DisplayLine2Html(ConsoleDisplayLine[] lines, bool needPandN)
    {
        if (lines == null || lines.Length == 0)
            return "";
        StringBuilder b = new();
        if (needPandN)
        {
            switch (lines[0].Align)
            {
                case DisplayLineAlignment.LEFT:
                    b.Append("<p align='left'>");
                    break;
                case DisplayLineAlignment.CENTER:
                    b.Append("<p align='center'>");
                    break;
                case DisplayLineAlignment.RIGHT:
                    b.Append("<p align='right'>");
                    break;
            }
            b.Append("<nobr>");
        }
        for (int dispCounter = 0; dispCounter < lines.Length; dispCounter++)
        {
            if (dispCounter != 0)
                b.Append("<br>");
            ConsoleButtonString[] buttons = lines[dispCounter].Buttons;
            for (int buttonCounter = 0; buttonCounter < buttons.Length; buttonCounter++)
            {
                string titleValue = null;
                if (!string.IsNullOrEmpty(buttons[buttonCounter].Title))
                    titleValue = Escape(buttons[buttonCounter].Title);
                bool hasTag = buttons[buttonCounter].IsButton || titleValue != null
                    || buttons[buttonCounter].PointXisLocked;
                if (hasTag)
                {
                    if (buttons[buttonCounter].IsButton)
                    {
                        string attrValue = Escape(buttons[buttonCounter].Inputs);
                        b.Append("<button value='");
                        b.Append(attrValue);
                        b.Append('\'');
                    }
                    else
                    {
                        b.Append("<nonbutton");
                    }
                    if (titleValue != null)
                    {
                        b.Append(" title='");
                        b.Append(titleValue);
                        b.Append('\'');
                    }
                    if (buttons[buttonCounter].PointXisLocked)
                    {
                        b.Append(" pos='");
                        b.Append(buttons[buttonCounter].RelativePointX);
                        b.Append('\'');
                    }
                    b.Append('>');
                }
                AConsoleDisplayNode[] parts = buttons[buttonCounter].StrArray;
                for (int cssCounter = 0; cssCounter < parts.Length; cssCounter++)
                {
                    if (parts[cssCounter] is ConsoleStyledString)
                    {
                        ConsoleStyledString css = parts[cssCounter] as ConsoleStyledString;
                        b.Append(getStringStyleStartingTag(css.StringStyle));
                        b.Append(Escape(css.Text));
                        b.Append(getClosingStyleStartingTag(css.StringStyle));
                    }
                    else if (parts[cssCounter] is ConsoleImagePart)
                    {
                        b.Append(parts[cssCounter].AltText);
                        //ConsoleImagePart img = (ConsoleImagePart)parts[cssCounter];
                        //b.Append("<img src='");
                        //b.Append(Escape(img.ResourceName));
                        //if(img.ButtonResourceName != null)
                        //{
                        //	b.Append("' srcb='");
                        //	b.Append(Escape(img.ButtonResourceName));
                        //}
                        //b.Append("'>");
                    }
                    else if (parts[cssCounter] is ConsoleShapePart)
                    {
                        b.Append(parts[cssCounter].AltText);
                    }

                }
                if (hasTag)
                {
                    if (buttons[buttonCounter].IsButton)
                        b.Append("</button>");
                    else
                        b.Append("</nonbutton>");
                }

            }
        }
        if (needPandN)
        {
            b.Append("</nobr>");
            b.Append("</p>");
        }
        return b.ToString();
    }

    public static string[] HtmlTagSplit(string str)
    {
        List<string> strList = [];
        CharStream st = new(str);
        int found;
        while (!st.EOS)
        {
            found = st.Find('<');
            if (found < 0)
            {
                strList.Add(st.Substring());
                break;
            }
            else if (found > 0)
            {
                strList.Add(st.Substring(st.CurrentPosition, found));
                st.CurrentPosition += found;
            }
            found = st.Find('>');
            if (found < 0)
                return null;
            found++;
            strList.Add(st.Substring(st.CurrentPosition, found));
            st.CurrentPosition += found;
        }
        string[] ret = new string[strList.Count];
        strList.CopyTo(ret);
        return ret;
    }

    /// <summary>
    /// htmlから表示行の作成
    /// </summary>
    /// <param name="str">htmlテキスト</param>
    /// <param name="sm"></param>
    /// <param name="console">実際の表示に使わないならnullにする</param>
    /// <returns></returns>
    /// 
    static readonly AngleSharp.Html.Parser.HtmlParser parser = new();
    public static ConsoleDisplayLine[] Html2DisplayLine(string str, StringMeasure sm, EmueraConsole console, bool lineEnd)
    {
        // [Emuera改修:HTML-01] Phase 12C / 2026-08-19
        // strict color-only FONTだけをfast path対象とし、対象外は必ずAngleSharpへfallbackする。
        // compatibility優先でgrammarを狭くした経路であり、表示意味論を拡張・変更しない。
        // The fast path accepts only a deliberately narrow color-only FONT grammar.
        // Anything outside that grammar falls through to the existing AngleSharp path unchanged.
        if (TryBuildColorOnlyFontFastPath(str, sm, console, lineEnd, out ConsoleDisplayLine[] fastResult))
            return fastResult;

        {
            var doc = parser.ParseDocument($"<body>{str.ReplaceLineEndings("<br>")}</body>");
            var body = doc.Body;
            var nodes = body.ChildNodes;
            var noBR = false;
            var align = DisplayLineAlignment.LEFT;
            var list = ParseNode(nodes, new StringStyle(Config.ForeColor, FontStyle.Regular, Config.FontName), new DivState(), align);

            List<AConsoleDisplayNode> ParseNode(INodeList nodes, StringStyle stringStyle, DivState divState, DisplayLineAlignment alignment)
            {
                if (nodes == null)
                {
                    return null;
                }
                var nodeList = new List<AConsoleDisplayNode>();
                var buttonList = new List<ConsoleButtonString>();
                foreach (var node in nodes)
                {
                    switch (node.NodeName)
                    {
                        case "P":
                            {
                                var elem = node as Element;

                                var alignStr = elem.GetAttribute("align");
                                align = alignStr.ToLower(CultureInfo.InvariantCulture) switch
                                {
                                    "left" => DisplayLineAlignment.LEFT,
                                    "center" => DisplayLineAlignment.CENTER,
                                    "right" => DisplayLineAlignment.RIGHT,
                                    _ => throw new CodeEE($"ALIGNMENTのキーワード\"{str}\"は未定義です")
                                };
                                return ParseNode(node.ChildNodes, stringStyle, divState, align);
                            }
                        case "NOBR":
                            {
                                noBR = true;
                                return ParseNode(node.ChildNodes, stringStyle, divState, align);
                            }
                        case "BR":
                            {
                                nodeList.Add(new BrNode());
                            }
                            break;
                        case "BUTTON":
                            {
                                var elem = node as Element;

                                ConsoleButtonString button;

                                var isButton = true;

                                string inputStr = null;
                                var inputInt = -1;
                                var valueText = elem.GetAttribute("value");
                                if (valueText != null)
                                {
                                    if (!int.TryParse(valueText, out inputInt))
                                    {
                                        inputStr = valueText;
                                    }
                                }
                                else
                                {
                                    isButton = false;
                                }

                                var xpos = 0;
                                var lockXpos = false;

                                var xposStr = elem.GetAttribute("pos");
                                if (!string.IsNullOrEmpty(xposStr))
                                {
                                    xpos = int.Parse(xposStr, CultureInfo.InvariantCulture);
                                    lockXpos = true;
                                }

                                var title = elem.GetAttribute("title")?.Replace("<br>", "\n", StringComparison.Ordinal);


                                var c = ParseNode(node.ChildNodes, stringStyle, divState, align);

                                if (isButton)
                                {
                                    if (inputStr == null)
                                    {
                                        button = new ConsoleButtonString(console, [
                                            ..c
                                        ], inputInt);
                                    }
                                    else
                                    {
                                        button = new ConsoleButtonString(console, [
                                            ..c
                                        ], inputStr);
                                    }
                                }
                                else
                                {
                                    button = new ConsoleButtonString(console, [
                                        ..c
                                    ]);
                                }

                                button.Title = title;
                                if (lockXpos)
                                {
                                    button.LockPointX(xpos);
                                }


                                nodeList.Add(button);
                            }
                            break;
                        case "NONBUTTON":
                            {
                                var elem = node as Element;

                                ConsoleButtonString button;

                                var xpos = 0;
                                var lockXpos = false;

                                var xposStr = elem.GetAttribute("pos");
                                if (!string.IsNullOrEmpty(xposStr))
                                {
                                    xpos = int.Parse(xposStr, CultureInfo.InvariantCulture);
                                    lockXpos = true;
                                }

                                var title = elem.GetAttribute("title")?.Replace("<br>", "\n", StringComparison.Ordinal); ;

                                var c = ParseNode(node.ChildNodes, stringStyle, divState, align);

                                button = new ConsoleButtonString(console, [
                                    ..c
                                ])
                                {
                                    Title = title
                                };

                                if (lockXpos)
                                {
                                    button.LockPointX(xpos);
                                }


                                nodeList.Add(button);
                            }
                            break;
                        case "FONT":
                            {
                                var elem = node as Element;

                                var style = stringStyle;
                                var face = elem.GetAttribute("face");
                                var color = ParseColor(elem.GetAttribute("color"), style.Color.ToSKColor());
                                var bcolor = ParseColor(elem.GetAttribute("bcolor"), style.ButtonColor.ToSKColor());
                                var fontSize = ParseSizeValue(elem.GetAttribute("size"), Config.FontSize);
                                style.Fontname = face;
                                style.Color = color.ToDrawingColor();
                                style.ButtonColor = bcolor.ToDrawingColor();
                                style.FontSize = fontSize;

                                var c = ParseNode(node.ChildNodes, style, divState, align);

                                nodeList.AddRange(c);
                            }
                            break;
                        case "IMG":
                            {
                                var elem = node as Element;

                                var src = elem.GetAttribute("src");
                                var srcb = elem.GetAttribute("srcb");
                                var height = ParseSizeValue(elem.GetAttribute("height"), Config.FontSize);
                                var width = ParseSizeValue(elem.GetAttribute("width"), 0);
                                var ypos = ParseSizeValue(elem.GetAttribute("ypos"), 0);
                                var xpos = ParseSizeValue(elem.GetAttribute("xpos"), 0);

                                var display = DisplayMode.Relative;
                                {
                                    var diplayStr = elem.GetAttribute("display");
                                    if (!string.IsNullOrEmpty(diplayStr))
                                    {
                                        display = ParseDisplayValue(diplayStr);
                                    }
                                }

                                nodeList.Add(new ConsoleImagePart(src, srcb, height, width, ypos, xpos, display: display));
                            }
                            break;
                        case "SHAPE":
                            {
                                var elem = node as Element;

                                var type = elem.GetAttribute("type");

                                int[] param = null;
                                var paramStr = elem.GetAttribute("param");
                                if (!string.IsNullOrEmpty(paramStr))
                                {
                                    var values = paramStr.Split(',');
                                    param = new int[values.Length];
                                    for (int i = 0; i < values.Length; i++)
                                    {
                                        if (!int.TryParse(values[i], out param[i]))
                                            throw new CodeEE("<" + node.NodeName + ">タグの param 属性の属性値が数値として解釈できません");
                                    }
                                }

                                var color = Config.ForeColor.ToSKColor();
                                var colorStr = elem.GetAttribute("color");
                                if (colorStr != null)
                                {
                                    color = ParseColor(colorStr);
                                }

                                var bcolor = Config.FocusColor.ToSKColor();
                                var bcolorStr = elem.GetAttribute("bcolor");
                                if (bcolorStr != null)
                                {
                                    bcolor = ParseColor(bcolorStr);
                                }

                                nodeList.Add(ConsoleShapePart.CreateShape(type, param, color.ToDrawingColor(), bcolor.ToDrawingColor(), colorStr != null));

                                //勝手にタグを閉じて中に要素を取り込むことがある
                                if (node.ChildNodes.Length > 0)
                                {
                                    nodeList.AddRange(
                                        ParseNode(node.ChildNodes, stringStyle, divState, align)
                                    );
                                }
                            }
                            break;
                        case "DIV":
                            {
                                var style = divState;

                                var elem = node as Element;

                                BorderStyle? borderStyle = null;
                                {
                                    var hasBorder = false;
                                    var borderWidth = elem.GetAttribute("border_width");
                                    if (!string.IsNullOrEmpty(borderWidth))
                                    {
                                        hasBorder = true;
                                        style.BorderWidth = ParseSizeValue(borderWidth);
                                    }
                                    var borderColor = ParseColor(elem.GetAttribute("border_color"), SKColors.White);

                                    if (hasBorder)
                                    {
                                        borderStyle = new BorderStyle()
                                        {
                                            Color = borderColor,
                                            StrokeWidth = style.BorderWidth
                                        };
                                    }
                                }

                                var display = DisplayMode.Relative;
                                {
                                    var diplayStr = elem.GetAttribute("display");
                                    if (!string.IsNullOrEmpty(diplayStr))
                                    {
                                        display = ParseDisplayValue(diplayStr);
                                    }
                                }

                                var position = SKPoint.Empty;
                                {
                                    position.X = ParseSizeValue(elem.GetAttribute("xpos"), 0);
                                    position.Y = ParseSizeValue(elem.GetAttribute("ypos"), 0);
                                }

                                var size = new SKSize();
                                {
                                    size.Width = ParseSizeValue(elem.GetAttribute("width"), -1);
                                    size.Height = ParseSizeValue(elem.GetAttribute("height"), -1);
                                }

                                var padding = SKRect.Empty;
                                {
                                    var allStr = elem.GetAttribute("padding");
                                    if (!string.IsNullOrEmpty(allStr))
                                    {
                                        var all = ParseSizeValue(allStr);
                                        padding.Top = all;
                                        padding.Bottom = all;
                                        padding.Left = all;
                                        padding.Right = all;
                                    }
                                }

                                var backcolor = SKColor.Empty;
                                {
                                    var backcolorStr = elem.GetAttribute("background_color");
                                    if (!string.IsNullOrEmpty(backcolorStr))
                                    {
                                        backcolor = ParseColor(backcolorStr, divState.BackgroundColor);
                                    }
                                }

                                var hoverBackColor = SKColor.Empty;
                                {
                                    var hoverBackColorStr = elem.GetAttribute("hover-background-color");
                                    if (!string.IsNullOrEmpty(hoverBackColorStr))
                                    {
                                        hoverBackColor = ParseColor(hoverBackColorStr, divState.BackgroundColor);
                                    }
                                }


                                var c = ParseNode(node.ChildNodes, stringStyle, style, align);

                                nodeList.Add(new ConsoleDivElement(
                                    [.. c],
                                    hoverBackColor,
                                    display: display,
                                    borderStyle: borderStyle,
                                    position: position,
                                    size: size,
                                    padding: padding,
                                    backcolor: backcolor
                                ));
                            }
                            break;
                        case "#text":
                            {
                                var text = node.Text();
                                nodeList.Add(
                                    new ConsoleStyledString(text, stringStyle)
                                );
                            }
                            break;
                        case "B":
                            {
                                var style = stringStyle;
                                style.FontStyle = FontStyle.Bold;

                                var c = ParseNode(node.ChildNodes, style, divState, align);
                                nodeList.AddRange(c);
                            }
                            break;
                        case "I":
                            {
                                var style = stringStyle;
                                style.FontStyle = FontStyle.Italic;

                                var c = ParseNode(node.ChildNodes, style, divState, align);
                                nodeList.AddRange(c);

                            }
                            break;
                        case "U":
                            {
                                var style = stringStyle;
                                style.HasUnderline = true;

                                var c = ParseNode(node.ChildNodes, style, divState, align);
                                nodeList.AddRange(c);

                            }
                            break;
                        case "S":
                            {
                                var style = stringStyle;
                                style.HasStrikeout = true;

                                var c = ParseNode(node.ChildNodes, style, divState, align);
                                nodeList.AddRange(c);

                            }
                            break;
                        case "#comment":
                            break;
                        default:
                            {
                                throw new Exception($"タグ名 {node.NodeName} は解釈出来ません");
                            }
                    }
                }

                return nodeList;
            }

            var buttonList = new List<ConsoleButtonString>();
            var innerList = new List<AConsoleDisplayNode>();
            foreach (var node in list)
            {
                if (node is BrNode)
                {
                    if (innerList.Count > 0)
                    {
                        buttonList.Add(new ConsoleButtonString(console, [.. innerList]));
                        innerList.Clear();
                    }
                    buttonList.Add(null);
                }
                else if (node is ConsoleButtonString cbs)
                {
                    if (innerList.Count > 0)
                    {
                        buttonList.Add(new ConsoleButtonString(console, [.. innerList]));
                        innerList.Clear();
                    }
                    buttonList.Add(cbs);
                }
                else
                {
                    innerList.Add(node);
                }
            }

            if (innerList.Count > 0)
            {
                buttonList.Add(new ConsoleButtonString(console, [.. innerList]));
                innerList.Clear();
            }

            var ret = PrintStringBuffer.ButtonsToDisplayLines(buttonList, sm, noBR, false);

            if (ret.Length > 0)
            {
                ret[^1].IsLineEnd = lineEnd;
            }

            foreach (var line in ret)
            {
                line.SetAlignment(align);
            }

            return ret;
        }
    }


    // [Emuera改修:HTML-01] Phase 12C / 2026-08-19
    // strict color-only FONT専用。対象外は従来parserへfallbackする。
    //
    // This is intentionally narrower than the HTML syntax accepted by AngleSharp.
    // It only handles text plus:
    //   <font color = '#RRGGBB'>text</font>
    // with ordinary ASCII-space variation around '=' and before '>'.
    //
    // Before allocating any Console display objects, the entire raw input is validated.
    // If validation fails, the caller uses the original AngleSharp path.
    private const string ColorOnlyFontClose = "</font>";

    private static bool TryBuildColorOnlyFontFastPath(
        string raw,
        StringMeasure sm,
        EmueraConsole console,
        bool lineEnd,
        out ConsoleDisplayLine[] result)
    {
        result = null;

        if (!IsColorOnlyFontFastPathEligible(raw))
            return false;

        var defaultStyle = new StringStyle(Config.ForeColor, FontStyle.Regular, Config.FontName);
        var nodes = new List<AConsoleDisplayNode>(32);
        int position = 0;

        while (position < raw.Length)
        {
            int nextTag = raw.IndexOf('<', position);
            if (nextTag < 0)
            {
                ReadOnlySpan<char> tail = raw.AsSpan(position);
                if (!tail.IsEmpty)
                    nodes.Add(new ConsoleStyledString(tail.ToString(), defaultStyle));
                break;
            }

            ReadOnlySpan<char> plain = raw.AsSpan(position, nextTag - position);
            if (!plain.IsEmpty)
                nodes.Add(new ConsoleStyledString(plain.ToString(), defaultStyle));

            // Eligibility was validated in the first pass, so these cannot fail.
            _ = TryReadColorOnlyFontOpening(
                raw,
                nextTag,
                out int rgb,
                out int textStart);

            int closeStart = raw.IndexOf('<', textStart);
            ReadOnlySpan<char> innerText = raw.AsSpan(textStart, closeStart - textStart);

            if (!innerText.IsEmpty)
            {
                // Match the existing FONT branch for a color-only FONT.
                // GetAttribute("face") is null, therefore the copied StringStyle's
                // Fontname becomes null in the existing implementation as well.
                var style = defaultStyle;
                style.Fontname = null;
                style.Color = new SKColor((uint)rgb)
                    .WithAlpha(byte.MaxValue)
                    .ToDrawingColor();
                style.ButtonColor = defaultStyle.ButtonColor;
                style.FontSize = Config.FontSize;

                nodes.Add(new ConsoleStyledString(innerText.ToString(), style));
            }

            position = closeStart + ColorOnlyFontClose.Length;
        }

        if (nodes.Count == 0)
        {
            result = [];
            return true;
        }

        var buttonList = new List<ConsoleButtonString>(1)
        {
            new ConsoleButtonString(console, [.. nodes])
        };

        result = PrintStringBuffer.ButtonsToDisplayLines(
            buttonList,
            sm,
            false,
            false);

        if (result.Length > 0)
            result[^1].IsLineEnd = lineEnd;

        foreach (var line in result)
            line.SetAlignment(DisplayLineAlignment.LEFT);

        return true;
    }

    private static bool IsColorOnlyFontFastPathEligible(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return false;

        int fontCount = 0;
        int position = 0;

        while (position < raw.Length)
        {
            int nextTag = raw.IndexOf('<', position);

            if (nextTag < 0)
            {
                return fontCount > 0 &&
                    !ColorOnlyFontTextContainsUnsafeChar(raw.AsSpan(position));
            }

            if (ColorOnlyFontTextContainsUnsafeChar(
                raw.AsSpan(position, nextTag - position)))
            {
                return false;
            }

            if (!TryReadColorOnlyFontOpening(
                raw,
                nextTag,
                out _,
                out int textStart))
            {
                return false;
            }

            int closeStart = raw.IndexOf('<', textStart);
            if (closeStart < 0 ||
                !raw.AsSpan(closeStart)
                    .StartsWith(ColorOnlyFontClose.AsSpan(), StringComparison.Ordinal))
            {
                return false;
            }

            if (ColorOnlyFontTextContainsUnsafeChar(
                raw.AsSpan(textStart, closeStart - textStart)))
            {
                return false;
            }

            fontCount++;
            position = closeStart + ColorOnlyFontClose.Length;
        }

        return fontCount > 0;
    }

    private static bool TryReadColorOnlyFontOpening(
        string raw,
        int start,
        out int rgb,
        out int textStart)
    {
        rgb = 0;
        textStart = 0;

        int p = start;

        if (p < 0 || p + 5 > raw.Length ||
            !raw.AsSpan(p, 5).SequenceEqual("<font".AsSpan()))
        {
            return false;
        }
        p += 5;

        if (p >= raw.Length || raw[p] != ' ')
            return false;

        while (p < raw.Length && raw[p] == ' ')
            p++;

        if (p + 5 > raw.Length ||
            !raw.AsSpan(p, 5).SequenceEqual("color".AsSpan()))
        {
            return false;
        }
        p += 5;

        while (p < raw.Length && raw[p] == ' ')
            p++;

        if (p >= raw.Length || raw[p] != '=')
            return false;
        p++;

        while (p < raw.Length && raw[p] == ' ')
            p++;

        if (p + 9 > raw.Length ||
            raw[p] != '\'' ||
            raw[p + 1] != '#')
        {
            return false;
        }

        if (!TryParseColorOnlyFontHex(raw.AsSpan(p + 2, 6), out rgb))
            return false;

        if (raw[p + 8] != '\'')
            return false;
        p += 9;

        while (p < raw.Length && raw[p] == ' ')
            p++;

        if (p >= raw.Length || raw[p] != '>')
            return false;

        textStart = p + 1;
        return true;
    }

    private static bool TryParseColorOnlyFontHex(
        ReadOnlySpan<char> text,
        out int value)
    {
        value = 0;

        if (text.Length != 6)
            return false;

        foreach (char ch in text)
        {
            int digit;

            if (ch >= '0' && ch <= '9')
                digit = ch - '0';
            else if (ch >= 'A' && ch <= 'F')
                digit = ch - 'A' + 10;
            else if (ch >= 'a' && ch <= 'f')
                digit = ch - 'a' + 10;
            else
                return false;

            value = (value << 4) | digit;
        }

        return true;
    }

    private static bool ColorOnlyFontTextContainsUnsafeChar(
        ReadOnlySpan<char> text)
    {
        foreach (char ch in text)
        {
            if (char.IsSurrogate(ch))
                return true;

            switch (ch)
            {
                case '&':
                case '\0':
                case '\r':
                case '\n':
                case '\f':
                case '\u0085':
                case '\u2028':
                case '\u2029':
                    return true;
            }

            if (ch < ' ' && ch != '\t')
                return true;
        }

        return false;
    }

    private static SKColor ParseColor(string colorStr, SKColor def)
    {
        SKColor color;
        if (string.IsNullOrEmpty(colorStr))
        {
            color = def;
        }
        else
        {
            color = ParseColor(colorStr);
        }

        return color;
    }

    private static SKColor ParseColor(string colorStr)
    {
        SKColor color;
        color = new SKColor((uint)stringToColorInt32(colorStr));
        color = color.WithAlpha(byte.MaxValue);

        return color;
    }

    static int ParseSizeValue(string value, int def)
    {
        int xpos = def;
        if (!string.IsNullOrEmpty(value))
        {
            xpos = ParseSizeValue(value);
        }

        return xpos;
    }

    private static int ParseSizeValue(string value)
    {
        int xpos;
        if (value.EndsWith("px", StringComparison.OrdinalIgnoreCase))
        {
            xpos = int.Parse(value.AsSpan()[..^2], CultureInfo.InvariantCulture);
        }
        else
        {
            xpos = int.Parse(value, CultureInfo.InvariantCulture) * Config.FontSize / 100;
        }

        return xpos;
    }

    public static string Html2PlainText(string str)
    {
        string ret = HtmlTagRegex().Replace(str, "");
        return Unescape(ret);
    }

    public static string Escape(string str)
    {
        return System.Web.HttpUtility.HtmlEncode(str);
    }

    public static string Unescape(string str)
    {
        return System.Web.HttpUtility.HtmlDecode(str);
    }

    public static string GetColorToString(Color color)
    {
        StringBuilder b = new();
        b.Append('#');
        int colorValue = color.R * 0x10000 + color.G * 0x100 + color.B;
        b.Append(colorValue.ToString("X6", CultureInfo.InvariantCulture));
        return b.ToString();
    }
    private static string getStringStyleStartingTag(StringStyle style)
    {
        bool fontChanged = !((style.Fontname == null || style.Fontname == Config.FontName) && !style.ColorChanged && style.ButtonColor == Config.FocusColor);
        if (!fontChanged && style.FontStyle == FontStyle.Regular)
            return "";
        StringBuilder b = new();
        if (fontChanged)
        {
            b.Append("<font");
            if (style.Fontname != null && style.Fontname != Config.FontName)
            {
                b.Append(" face='");
                b.Append(Escape(style.Fontname));
                b.Append('\'');
            }
            if (style.ColorChanged)
            {
                b.Append(" color='#");
                int colorValue = style.Color.R * 0x10000 + style.Color.G * 0x100 + style.Color.B;
                b.Append(colorValue.ToString("X6", CultureInfo.InvariantCulture));
                b.Append('\'');
            }
            if (style.ButtonColor != Config.FocusColor)
            {
                b.Append(" bcolor='#");
                int colorValue = style.ButtonColor.R * 0x10000 + style.ButtonColor.G * 0x100 + style.ButtonColor.B;
                b.Append(colorValue.ToString("X6", CultureInfo.InvariantCulture));
                b.Append('\'');
            }
            b.Append('>');
        }
        if (style.FontStyle != FontStyle.Regular)
        {
            if ((style.FontStyle & FontStyle.Strikeout) != FontStyle.Regular)
                b.Append("<s>");
            if ((style.FontStyle & FontStyle.Underline) != FontStyle.Regular)
                b.Append("<u>");
            if ((style.FontStyle & FontStyle.Italic) != FontStyle.Regular)
                b.Append("<i>");
            if ((style.FontStyle & FontStyle.Bold) != FontStyle.Regular)
                b.Append("<b>");
        }

        return b.ToString();
    }

    private static string getClosingStyleStartingTag(StringStyle style)
    {
        bool fontChanged = !((style.Fontname == null || style.Fontname == Config.FontName) && !style.ColorChanged && style.ButtonColor == Config.FocusColor);
        if (!fontChanged && style.FontStyle == FontStyle.Regular)
            return "";
        StringBuilder b = new();
        if (style.FontStyle != FontStyle.Regular)
        {
            if ((style.FontStyle & FontStyle.Bold) != FontStyle.Regular)
                b.Append("</b>");
            if ((style.FontStyle & FontStyle.Italic) != FontStyle.Regular)
                b.Append("</i>");
            if ((style.FontStyle & FontStyle.Underline) != FontStyle.Regular)
                b.Append("</u>");
            if ((style.FontStyle & FontStyle.Strikeout) != FontStyle.Regular)
                b.Append("</s>");
        }
        if (fontChanged)
            b.Append("</font>");
        return b.ToString();
    }
    static DisplayMode ParseDisplayValue(string value)
    {
        return value switch
        {
            "relative" => DisplayMode.Relative,
            "absolute" => DisplayMode.Absolute,
            "absolute-lefttop" => DisplayMode.AbsoluteLeftTop,
            "absolute-leftbottom" => DisplayMode.AbsoluteLeftBottom,
            _ => throw new Exception("displayの値が解釈できません")
        };
    }

    private static int stringToColorInt32(string str)
    {
        if (str.Length == 0)
            throw new CodeEE(LocalizationManager.Error.RequireColorCode);
        int i;
        if (str[0] == '#')
        {
            var colorvalue = str.AsSpan()[1..];
            try
            {
                i = int.Parse(colorvalue, NumberStyles.HexNumber);
                if (i < 0 || i > 0xFFFFFF)
                    throw new CodeEE($"{colorvalue}は適切な色指定の範囲外です");
            }
            catch
            {
                throw new CodeEE($"{colorvalue}は数値として解釈できません");
            }
        }
        else
        {
            Color color = Color.FromName(str);
            if (color.A == 0)//色名として解釈失敗 エラー確定
            {
                if (str.Equals("transparent", StringComparison.OrdinalIgnoreCase))
                    throw new CodeEE(LocalizationManager.Error.TransparentUnsupported);
                try
                {
                    i = Convert.ToInt32(str, 16);
                }
                catch//16進数でもない
                {
                    throw new CodeEE("指定された色名\"" + str + "\"は無効な色名です");
                }
                //#RRGGBBを意図したのかもしれない
                throw new CodeEE("指定された色名\"" + str + "\"は無効な色名です(16進数で色を指定する場合には数値の前に#が必要です)");
            }
            i = color.R * 0x10000 + color.G * 0x100 + color.B;
        }
        return i;
    }

    [GeneratedRegex("\\<[^<]*\\>")]
    private static partial Regex HtmlTagRegex();
}
