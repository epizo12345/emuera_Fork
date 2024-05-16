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
    public SKPaint Paint;
}

class ConsoleDivElement : AConsoleDisplayNode
{
    public readonly List<ConsoleButtonString> _childNodes;

    readonly int _positionX;
    readonly int _positionY;

    readonly DisplayMode _display;

    StringStyle _stringStyle;
    readonly SKFont _font;

    readonly SKFont _fallbackFont;
    readonly List<TextsWithFont> _texts;

    Color? _backColor;

    BorderStyle? _borderStyle;
    Padding? _padding;

    public ConsoleDivElement(List<ConsoleButtonString> childNode,
                                DisplayMode display = DisplayMode.Relative,
                                int positionX = 0, int positionY = 0,
                                int width = -1, int height = -1,
                                Color? backcolor = null,
                                BorderStyle? borderStyle = null,
                                Padding? padding = null)
    {
        _childNodes = childNode;

        foreach (var node in childNode)
        {
            foreach (var child in node.StrArray)
            {
                child.Point = Point;
                child.Size = new SKSize();
            }
        }


        _display = display;
        _positionX = positionX;
        _positionY = positionY;
        Size = new SKSize(width, width);

        _positionX = positionX;
        _positionY = positionY;

        _backColor = backcolor;

        _borderStyle = borderStyle;

        _padding = padding;
    }

    public override bool CanDivide => false;

    public override void DrawTo(SKCanvas graph, SKPoint origin, bool isSelecting, bool isBackLog, TextDrawingMode mode, bool isButton = false)
    {
        if (Error)
            return;
        Point = _display switch
        {
            DisplayMode.Relative => new SKPoint(PointX + _positionX, (int)origin.Y + _positionY),
            DisplayMode.AbsoluteLeftTop => new SKPoint(_positionX, _positionY),
            DisplayMode.AbsoluteLeftBottom => new SKPoint(_positionX, GlobalStatic.Console.ClientHeight - Config.FontSize + _positionY),
            _ => throw new NotImplementedException($"{_display}はまだ実装されていません")
        };

        var autoWidth = Size?.Width == -1;
        var autoHeight = Size?.Height == -1;

        if (autoWidth || autoHeight)
        {
            var width = 0.0f;
            foreach (var node in _childNodes)
            {
                width += node.Width;
            }
            if (autoWidth)
            {
                Size = Size.Value with
                {
                    Width = width,
                };
            }
            if (autoHeight)
            {
                Size = Size.Value with
                {
                    Height = Config.LineHeight
                };
            }
        }

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

            if (JSONConfig.Data.UseButtonFocusBackgroundColor && isButton && !isBackLog)
            {
                if (!backcolor.HasValue)
                {
                    _backColor = Color.FromArgb(50, 50, 50);
                }

            }




            if (_backColor.HasValue)
            {
                var paint = new SKPaint()
                {
                    Color = _backColor.Value.ToSKColor(),
                };
                graph.DrawRect(SKRect.Create(Point.Value, Size.Value), paint);
            }

        }

        var paddingPoint = Point ?? new SKPoint();
        if (_padding.HasValue)
        {
            paddingPoint = new SKPoint(
                Point.Value.X + _padding.Value.Left,
                Point.Value.Y + _padding.Value.Top
                );
        }

        foreach (var childNode in _childNodes)
        {
            childNode.DrawTo(graph, paddingPoint, isBackLog, mode);
            paddingPoint.Offset(childNode.Width, 0);
        }

        if (_borderStyle.HasValue)
        {
            //graph.DrawRectangle(_borderStyle.Value.Pen, new Rectangle(Point, Size));
            graph.DrawRect(SKRect.Create(Point.Value, Size.Value), _borderStyle?.Paint);
        }
    }

    public override void SetWidth(StringMeasure sm, float subPixel)
    {
        if (Error)
        {
            Width = 0;
            return;
        }

        foreach (var childNode in _childNodes)
        {
            childNode.CalcWidth(sm, subPixel);
        }
    }
}