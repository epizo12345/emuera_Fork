using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using MinorShift.Emuera;
using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Runtime.Config.JSON;
using MinorShift.Emuera.UI;
using MinorShift.Emuera.UI.Game;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

struct BorderStyle
{
    public Pen Pen;
}

class ConsoleDivElement : AConsoleDisplayNode
{
    readonly List<AConsoleDisplayNode> _childNodes;

    readonly int _positionX;
    readonly int _positionY;
    public Size Size;
    public Point Point;

    readonly DisplayMode _display;

    StringStyle _stringStyle;
    readonly SKFont _font;

    readonly SKFont _fallbackFont;
    readonly List<TextsWithFont> _texts;

    Color? _backColor;

    BorderStyle? _borderStyle;
    Padding? _padding;

    public ConsoleDivElement(List<AConsoleDisplayNode> childNode, string text,
                                StringStyle stringStyle,
                                DisplayMode display = DisplayMode.Relative,
                                int positionX = 0, int positionY = 0,
                                int width = -1, int height = -1,
                                Color? backcolor = null,
                                BorderStyle? borderStyle = null,
                                Padding? padding = null)
    {
        _childNodes = childNode;

        Text = text;


        _stringStyle = stringStyle;
        _font = FontFactory.GetFont(stringStyle);
        _fallbackFont = FontFactory.GetFont(Config.DefaultFont.Typeface.FamilyName, stringStyle.FontStyle);

        if (text.Contains('\n'))
        {
            var lines = text.Split('\n');
            var offsetY = 0;
            foreach (var line in lines)
            {
                _childNodes.Add(new ConsoleDivElement([], line, stringStyle, display, positionX, positionY + offsetY, width, height, backcolor, borderStyle, padding));
                offsetY += (int)_font.Size;
            }
            Text = "";
        }

        if (!_font.ContainsGlyphs(Text))
        {
            _texts = [];
            var useFallbackFont = true;
            var builder = new StringBuilder();
            var f = _fallbackFont;
            foreach (var c in Text)
            {
                if ((useFallbackFont && _font.ContainsGlyph(c)) ||
                    (!useFallbackFont && !_font.ContainsGlyph(c)))
                {
                    var paragrah = builder.ToString();
                    builder.Clear();
                    _texts.Add(new TextsWithFont
                    {
                        Font = f,
                        Text = paragrah,
                        Width = f.MeasureText(paragrah),
                    });
                    if (useFallbackFont)
                    {
                        f = _font;
                        useFallbackFont = false;
                    }
                    else
                    {
                        f = _fallbackFont;
                        useFallbackFont = true;
                    }
                }

                builder.Append(c);
            }
            if (builder.Length > 0)
            {
                var paragrah = builder.ToString();
                _texts.Add(new TextsWithFont
                {
                    Font = f,
                    Text = paragrah,
                    Width = f.MeasureText(paragrah),
                });
            }

        }


        _display = display;
        _positionX = positionX;
        _positionY = positionY;
        Size = new Size(width, height);
        var autoWidth = Size.Width == -1;
        var autoHeight = Size.Height == -1;

        if (autoWidth || autoHeight)
        {
            var autoSize = _font.MeasureText(Text);
            if (autoWidth)
            {
                Size.Width = (int)autoSize;
            }
            if (autoHeight)
            {
                Size.Height = (int)_font.Size;
            }
        }

        _positionX = positionX;
        _positionY = positionY;

        _backColor = backcolor;

        _borderStyle = borderStyle;

        _padding = padding;
    }

    public override bool CanDivide => false;

    public override void DrawTo(SKCanvas graph, int pointY, bool isSelecting, bool isBackLog, TextDrawingMode mode, bool isButton = false)
    {
        if (Error)
            return;
        Point = _display switch
        {
            DisplayMode.Relative => new Point(PointX + _positionX, pointY + _positionY),
            DisplayMode.AbsoluteLeftTop => new Point(_positionX, _positionY),
            DisplayMode.AbsoluteLeftBottom => new Point(_positionX, GlobalStatic.Console.ClientHeight - Config.FontSize + _positionY),
            _ => throw new NotImplementedException($"{_display}はまだ実装されていません")
        };


        {
            var color = _stringStyle.Color;
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
                color = _stringStyle.ButtonColor;
            }

            if (isBackLog)
            {
                color = Config.LogColor;
            }


            if (mode == TextDrawingMode.GRAPHICS)
            {
                //graph.DrawString(Text, _font, new SolidBrush(color), Point);
            }
            else
            {
                if (JSONConfig.Data.UseButtonFocusBackgroundColor && isButton && !isBackLog)
                {
                    if (!backcolor.HasValue)
                    {
                        _backColor = Color.FromArgb(50, 50, 50);
                    }

                }

                var paddingPoint = new SKPoint(Point.X, Point.Y);
                if (_padding.HasValue)
                {
                    paddingPoint = new SKPoint(
                        Point.X + _padding.Value.Left,
                        Point.Y + _padding.Value.Top
                        );
                }

                var paint = new SKPaint()
                {
                    Color = color.ToSKColor()
                };

                if (_backColor.HasValue)
                {
                    graph.DrawRect(SKRect.Create(Point.ToSKPoint(), Size.ToSKSize()), paint);
                }




                if (_texts == null)
                {
                    paddingPoint.Offset(0, Math.Abs(_font.Metrics.Top));
                    graph.DrawText(Text, paddingPoint, SKTextAlign.Left, _font, paint);
                }
                else
                {
                    foreach (var text in _texts)
                    {
                        var offsetPoint = paddingPoint;
                        offsetPoint.Offset(0, Math.Abs(text.Font.Metrics.Top) + text.offsetY);
                        graph.DrawText(text.Text, offsetPoint, SKTextAlign.Left, text.Font, paint);

                        paddingPoint.Offset(text.Width, 0);
                    }
                }

            }
        }

        foreach (var childNode in _childNodes)
        {
            childNode.DrawTo(graph, pointY, isSelecting, isBackLog, mode, isButton);
        }

        if (_borderStyle.HasValue)
        {
            //graph.DrawRectangle(_borderStyle.Value.Pen, new Rectangle(Point, Size));
            graph.DrawRect(SKRect.Create(Point.ToSKPoint(), Size.ToSKSize()), new SKPaint());
        }
    }

    public override void SetWidth(StringMeasure sm, float subPixel)
    {
        if (Error)
        {
            Width = 0;
            return;
        }
        Width = StringMeasure.GetDisplayLength(Text, _font);

        foreach (var childNode in _childNodes)
        {
            childNode.SetWidth(sm, subPixel);
        }
    }
}