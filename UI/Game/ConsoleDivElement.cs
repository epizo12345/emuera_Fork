using System;
using System.Collections.Generic;
using AngleSharp.Dom;
using MinorShift.Emuera;
using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Runtime.Config.JSON;
using MinorShift.Emuera.UI.Game;
using SkiaSharp;

#nullable enable
struct BorderStyle
{
    public SKPaint Paint;
}

class ConsoleDivElement : AConsoleDisplayNode
{
    public readonly List<AConsoleDisplayNode> _childNodes;

    readonly SKPoint _position;

    readonly DisplayMode _display;

    SKColor? _backColor;
    readonly SKColor _hoverBackColor;

    BorderStyle? _borderStyle;
    SKRect? _padding;

    public ConsoleDivElement(List<AConsoleDisplayNode> childNode,
                                SKColor hoverBackColor,
                                DisplayMode display = DisplayMode.Relative,
                                SKPoint position = default,
                                SKSize size = default,
                                SKColor? backcolor = null,
                                BorderStyle? borderStyle = null,
                                SKRect? padding = null)
    {
        _childNodes = childNode;
        foreach (var node in childNode)
        {
            if (node != null)
            {
                node.Point = Point;
                if (node is ConsoleButtonString cbs)
                {
                    foreach (var child in cbs.StrArray)
                    {
                        child.Point = Point;
                        child.Size = new SKSize(-1, -1);
                    }
                }

            }
        }


        _display = display;
        _position = position;
        Size = size;

        _backColor = backcolor;
        _hoverBackColor = hoverBackColor;

        _borderStyle = borderStyle;

        _padding = padding;
    }

    public override bool CanDivide => false;

    public override void DrawTo(SKCanvas canvas, SKPoint origin, bool isSelecting, bool isBackLog, TextDrawingMode mode, bool isButton = false)
    {
        if (Error)
            return;
        Point = _display switch
        {
            DisplayMode.Relative => new SKPoint(origin.X + _position.X, (int)origin.Y + _position.Y),
            DisplayMode.AbsoluteLeftTop => _position,
            DisplayMode.AbsoluteLeftBottom => new SKPoint(_position.X, GlobalStatic.Console.ClientHeight - Config.FontSize + _position.Y),
            _ => throw new NotImplementedException($"{_display}はまだ実装されていません")
        };

        var autoWidth = Size.Width == -1;
        var autoHeight = Size.Height == -1;

        if (autoWidth || autoHeight)
        {
            var width = 0.0f;
            var maxWidth = 0.0f;
            var lineCount = 1;
            foreach (var node in _childNodes)
            {
                if (node is BrNode)
                {
                    maxWidth = MathF.Max(maxWidth, width);
                    width = 0.0f;

                    lineCount++;
                }
                else
                {
                    width += node.Width;
                }
            }
            maxWidth = MathF.Max(maxWidth, width);

            var paddingSize = new SKSize();
            if (_padding.HasValue)
            {
                paddingSize.Width = _padding.Value.Left + _padding.Value.Right;
                paddingSize.Height = _padding.Value.Top + _padding.Value.Bottom;
            }

            if (autoWidth)
            {
                Size.Width = maxWidth + paddingSize.Width;
            }
            if (autoHeight)
            {
                Size.Height = Config.LineHeight * lineCount + paddingSize.Height;
            }
        }

        {

            if (JSONConfig.Game.UseButtonFocusBackgroundColor && isButton && !isBackLog)
            {
                _backColor = new SKColor(50, 50, 50);
            }

            if (_backColor.HasValue)
            {
                var paint = new SKPaint()
                {
                    Color = _backColor.Value,
                };
                canvas.DrawRect(SKRect.Create(Point, Size), paint);
            }

            if (isSelecting && _hoverBackColor != SKColor.Empty)
            {
                var paint = new SKPaint()
                {
                    Color = _hoverBackColor,
                };
                canvas.DrawRect(SKRect.Create(Point, Size), paint);
            }

        }

        var paddingOrigin = Point;
        if (_padding.HasValue)
        {
            paddingOrigin.Offset(_padding.Value.Left, _padding.Value.Top);
        }

        if (_borderStyle.HasValue)
        {
            canvas.DrawRect(SKRect.Create(Point, Size), _borderStyle?.Paint);
        }

        var drawPoint = paddingOrigin;
        foreach (var childNode in _childNodes)
        {
            if (childNode is BrNode)
            {
                drawPoint = drawPoint with
                {
                    X = paddingOrigin.X,
                    Y = drawPoint.Y + Config.LineHeight
                };
            }
            else
            {
                childNode.DrawTo(canvas, drawPoint, isSelecting, isBackLog, mode);
                drawPoint.Offset(childNode.Width, 0);
            }
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
            if (childNode is ConsoleButtonString cbs)
            {
                cbs.CalcWidth(sm, subPixel);
            }
            else
            {
                childNode?.SetWidth(sm, subPixel);
            }
        }
    }
}