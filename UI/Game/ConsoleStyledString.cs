using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Runtime.Config.JSON;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Forms;

namespace MinorShift.Emuera.UI.Game;

public enum DisplayMode
{
    Relative,
    Absolute,//EM+EE互換
    AbsoluteLeftBottom,
    AbsoluteLeftTop
}

/// <summary>
/// 装飾付文字列。stringとStringStyleからなる。
/// </summary>
internal sealed class ConsoleStyledString : AConsoleColoredNode
{


    private ConsoleStyledString() { }
    public ConsoleStyledString(string str, StringStyle style)
    {
        //if ((StaticConfig.TextDrawingMode != TextDrawingMode.GRAPHICS) && (str.IndexOf('\t') >= 0))
        //    str = str.Replace("\t", "");
        Text = str;
        StringStyle = style;
        Font = FontFactory.GetFont(style.Fontname, style.FontStyle);
        Font.Subpixel = true;
        if (Font == null)
        {
            Error = true;
            return;
        }
        if (!Font.ContainsGlyphs(Text))
        {
            _fallbackFont = FontFactory.GetFont(Config.DefaultFont.Typeface.FamilyName, style.FontStyle);
            var isFallbackFont = true;
            var builder = new StringBuilder();
            _texts = [];
            foreach (var @char in Text)
            {
                if ((!isFallbackFont && Font.ContainsGlyph(@char)) ||
                    (isFallbackFont && !Font.ContainsGlyph(@char)))
                {
                    _texts.Add(builder.ToString());
                    builder.Clear();
                    isFallbackFont = !isFallbackFont;
                }

                builder.Append(@char);
            }
            if (builder.Length > 0)
            {
                _texts.Add(builder.ToString());
            }
        }
        Color = style.Color;
        ButtonColor = style.ButtonColor;
        colorChanged = style.ColorChanged;
        if (!colorChanged && Color != Config.ForeColor)
            colorChanged = true;
        PointX = -1;
        Width = -1;

    }

    public SKFont Font { get; private set; }
    SKFont _fallbackFont;
    List<string> _texts;//フォントフォールバック用
    public StringStyle StringStyle { get; private set; }
    public override bool CanDivide
    {
        get { return true; }
    }
    //単一のボタンフラグ
    //public bool IsButton { get; set; }
    //indexの文字数の前方文字列とindex以降の後方文字列に分割
    public ConsoleStyledString DivideAt(int index, StringMeasure sm)
    {
        //if ((index <= 0)||(index > Str.Length)||this.Error)
        //	return null;
        ConsoleStyledString ret = DivideAt(index);
        if (ret == null)
            return null;
        SetWidth(sm, XsubPixel);
        ret.SetWidth(sm, XsubPixel);
        return ret;
    }
    public ConsoleStyledString DivideAt(int index)
    {
        if (index <= 0 || index > Text.Length || Error)
            return null;
        string str = Text[index..];
        Text = Text[..index];
        ConsoleStyledString ret = new()
        {
            Font = Font,
            Text = str,
            Color = Color,
            ButtonColor = ButtonColor,
            colorChanged = colorChanged,
            StringStyle = StringStyle,
            XsubPixel = XsubPixel
        };
        return ret;
    }

    public override void SetWidth(StringMeasure sm, float subPixel)
    {
        if (Error)
        {
            Width = 0;
            return;
        }
        if (_texts == null)
        {

            Width = StringMeasure.GetDisplayLength(Text, Font);
        }
        else
        {
            var isFallbackFont = false;
            var offsetX = 0.0f;
            foreach (var text in _texts)
            {
                if (isFallbackFont)
                {
                    offsetX += _fallbackFont.GetGlyphWidths(text).Sum();
                    isFallbackFont = false;
                }
                else
                {
                    offsetX += _fallbackFont.GetGlyphWidths(text).Sum();
                    isFallbackFont = true;
                }
            }
            Width = (int)offsetX;
        }
        XsubPixel = subPixel;
    }

    public override void DrawTo(SKCanvas graph, int pointY, bool isSelecting, bool isBackLog, TextDrawingMode mode, bool isButton = false)
    {
        if (Error)
            return;

        var color = Color;
        Color? backcolor = null;
        if (isSelecting)
        {
            if (JSONConfig.Data.UseButtonFocusBackgroundColor)
            {
                if (!(Color.Yellow.R == color.R &&
                        Color.Yellow.G == color.G &&
                        Color.Yellow.B == color.B)
                 && !string.IsNullOrWhiteSpace(Text))
                {
                    backcolor = Color.Gray;
                }
            }
            color = ButtonColor;
        }
        else if (isBackLog && !colorChanged)
        {
            color = Config.LogColor;
        }


        if (mode == TextDrawingMode.GRAPHICS)
        {
            //graph.DrawString(Text, Font, new SolidBrush(color), point);
        }
        else
        {
            // if (JSONConfig.Data.UseButtonFocusBackgroundColor)
            // {
            //     if (isButton && !isBackLog)
            //     {
            //         if (!backcolor.HasValue)
            //         {
            //             backcolor = Color.FromArgb(50, 50, 50);
            //         }
            //         TextRenderer.DrawText(graph, Text.AsSpan(), Font, point, color, backColor: backcolor.Value, TextFormatFlags.NoPrefix);
            //     }
            //     else
            //     {
            //         TextRenderer.DrawText(graph, Text.AsSpan(), Font, point, color, TextFormatFlags.NoPrefix);
            //     }
            // }
            // else
            // {
            //     TextRenderer.DrawText(graph, Text.AsSpan(), Font, point, color, TextFormatFlags.NoPrefix);
            // }

            var paint = new SKPaint
            {
                Color = color.ToSKColor(),
                IsAntialias = false,
            };

            var point = new SKPoint(PointX, pointY);


            if (backcolor.HasValue)
            {
                var size = new SKSize(Font.Size, Font.Metrics.Descent);
                graph.DrawRect(SKRect.Create(point, size), new SKPaint() { Color = backcolor.Value.ToSKColor() });
            }


            if (_texts == null)
            {
                point.Offset(0, Math.Abs(Font.Metrics.Top));
                graph.DrawText(Text, point, SKTextAlign.Left, Font, paint);
            }
            else
            {
                var isFallbackFont = false;
                var offsetX = 0.0f;
                var fallbackOffsetPoint = point;
                fallbackOffsetPoint.Offset(0, Math.Abs(_fallbackFont.Metrics.Top));
                var normalOffsetPoint = point;
                normalOffsetPoint.Offset(0, Math.Abs(Font.Metrics.Top));
                foreach (var text in _texts)
                {
                    if (isFallbackFont)
                    {

                        graph.DrawText(text, fallbackOffsetPoint with { X = fallbackOffsetPoint.X + offsetX }, SKTextAlign.Left, _fallbackFont, paint);
                        offsetX += _fallbackFont.GetGlyphWidths(text, paint).Sum();
                        isFallbackFont = false;
                    }
                    else
                    {

                        graph.DrawText(text, normalOffsetPoint with { X = normalOffsetPoint.X + offsetX }, SKTextAlign.Left, Font, paint);
                        offsetX += Font.GetGlyphWidths(text, paint).Sum();
                        isFallbackFont = true;
                    }
                }
            }
        }

    }

}
