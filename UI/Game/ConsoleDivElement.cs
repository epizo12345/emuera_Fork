using System;
using System.Drawing;
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
    readonly AConsoleDisplayNode[] _childNodes;

    readonly int _positionX;
    readonly int _positionY;
    public Size Size;
    public Point Point;

    readonly DisplayMode _display;

    StringStyle _stringStyle;
    readonly SKFont _font;
    Color? _backColor;

    BorderStyle? _borderStyle;
    Padding? _padding;

    public ConsoleDivElement(AConsoleDisplayNode[] childNode, string text,
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

        _display = display;
        _positionX = positionX;
        _positionY = positionY;
        Size = new Size(width, height);
        var autoWidth = Size.Width == -1;
        var autoHeight = Size.Height == -1;

        if (autoWidth || autoHeight)
        {
            var autoSize = _font.MeasureText(text);
            if (autoWidth)
            {
                Size.Width = (int)autoSize;
            }
            if (autoHeight)
            {
                Size.Height = (int)_font.Metrics.XMax;
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
                if (_backColor.HasValue)
                {
                    //graph.FillRectangle(new SolidBrush(_backColor.Value), new Rectangle(Point, Size));
                    graph.DrawRect(SKRect.Create(Point.ToSKPoint(), Size.ToSKSize()), new SKPaint());
                }

                //TextRenderer.DrawText(graph, Text.AsSpan(), _font, paddingPoint, color, TextFormatFlags.NoPrefix);


                paddingPoint.Offset(0, Math.Abs(_font.Metrics.Top));

                graph.DrawText(Text, paddingPoint, SKTextAlign.Left, new SKFont(), new SKPaint());

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