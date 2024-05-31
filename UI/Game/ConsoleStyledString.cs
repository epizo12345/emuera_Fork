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

public struct TextsWithFont
{
    public string Text;
    public SKFont Font;
    public float Width;
    public float offsetY;
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
        str = str.Replace("\t", "", StringComparison.Ordinal);
        Text = str;
        StringStyle = style;
        Font = FontFactory.GetFont(style.Fontname, style.FontStyle);
        if (Font == null)
        {
            Error = true;
            return;
        }

        if (!Font.ContainsGlyphs(Text))
        {
            var fontManager = SKFontManager.Default;
            var builder = new StringBuilder();
            SKTypeface nowTypeFace = null;
            _texts = [];
            foreach (var rune in Text.EnumerateRunes())
            {
                builder.Append(rune);

                SKTypeface typeface;
                if (Font.ContainsGlyph(rune.Value))
                {
                    typeface = Font.Typeface;
                }
                else if (Config.DefaultFont.ContainsGlyph(rune.Value))
                {
                    typeface = Config.DefaultFont.Typeface;
                }
                else
                {
                    typeface = fontManager.MatchCharacter(rune.Value);
                }

                if (typeface != nowTypeFace)
                {
                    _texts.Add(CreateTextWithFont(typeface, builder.ToString()));
                    builder.Clear();
                }
            }

            if (builder.Length > 0)
            {
                _texts.Add(CreateTextWithFont(nowTypeFace, builder.ToString()));
            }
        }

        Color = style.Color;
        ButtonColor = style.ButtonColor;
        colorChanged = style.ColorChanged;
        if (!colorChanged && Color != Config.ForeColor)
            colorChanged = true;
        PointX = -1;
        Width = -1;

        TextsWithFont CreateTextWithFont(SKTypeface typeface, string t)
        {
            var font = new SKFont(typeface, Font.Size);
            font.Size *= Font.Size / font.Spacing;
            if (StringStyle.FontStyle.HasFlag(FontStyle.Italic))
            {
                font.SkewX = -0.3f;
            }

            var textsWithFont = new TextsWithFont()
            {
                Text = t,
                Font = font
            };

            textsWithFont.Width = textsWithFont.Font.GetGlyphWidths(textsWithFont.Text).Sum();
            return textsWithFont;
        }
    }

    public SKFont Font { get; private set; }
    List<TextsWithFont> _texts;//フォントフォールバック用
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
            var offsetX = 0.0f;
            foreach (var text in _texts)
            {
                offsetX += text.Width;
            }
            Width = (int)offsetX;
        }
        XsubPixel = subPixel;
        Size = new SKSize(Width, Config.LineHeight);
    }

    public override void DrawTo(SKCanvas graph, SKPoint origin, bool isSelecting, bool isBackLog, TextDrawingMode mode, bool isButton = false)
    {
        if (Error)
            return;

        var color = Color;
        SKColor? backcolor = null;
        if (isSelecting)
        {
            if (JSONConfig.Data.UseButtonFocusBackgroundColor)
            {
                if (!(Color.Yellow.R == color.R &&
                        Color.Yellow.G == color.G &&
                        Color.Yellow.B == color.B)
                 && !string.IsNullOrWhiteSpace(Text))
                {
                    backcolor = SKColors.Gray;
                }
            }
            color = ButtonColor;
        }
        else if (isBackLog && !colorChanged)
        {
            color = Config.LogColor;
        }

        var paint = new SKPaint
        {
            Color = color.ToSKColor()
        };

        var point = new SKPoint(origin.X, origin.Y);

        if (origin.X == float.NegativeInfinity)//旧来の位置決め方式
        {
            point.X = PointX + Config.DrawingParam_ShapePositionShift;
        }

        Point = point;

        if (backcolor.HasValue)
        {
            var size = new SKSize(Width, Font.Size);
            graph.DrawRect(SKRect.Create(point, size), new SKPaint() { Color = backcolor.Value });
        }

        if (_texts == null)
        {
            point.Offset(0, Math.Abs(Font.Metrics.Top));
            graph.DrawText(Text, point, SKTextAlign.Left, Font, paint);
        }
        else
        {
            foreach (var text in _texts)
            {
                var offsetPoint = point with { Y = point.Y + Math.Abs(text.Font.Metrics.Top) };
                graph.DrawText(text.Text, offsetPoint, SKTextAlign.Left, text.Font, paint);

                point.Offset(text.Width, 0);
            }
        }


        if (StringStyle.HasUnderline)
        {
            var underlinePosition = Point;
            underlinePosition.Offset(0, -Font.Metrics.Top + (Font.Metrics.UnderlinePosition ?? 0));

            var width = paint.StrokeWidth;
            paint.StrokeWidth = Font.Metrics.UnderlineThickness ?? 1;
            graph.DrawLine(underlinePosition, underlinePosition + new SKPoint(Width, 0), paint);
            paint.StrokeWidth = width;
        }

        if (StringStyle.HasStrikeout)
        {
            var strikeoutPosition = Point;
            strikeoutPosition.Offset(0, -Font.Metrics.Top + (Font.Metrics.StrikeoutPosition ?? 0));

            var width = paint.StrokeWidth;
            paint.StrokeWidth = Font.Metrics.StrikeoutThickness ?? 1;
            graph.DrawLine(strikeoutPosition, strikeoutPosition + new SKPoint(Width, 0), paint);
            paint.StrokeWidth = width;
        }

    }
}
