using System;
using System.Drawing;
using System.Windows.Forms;
using MinorShift.Emuera;
using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Runtime.Config.JSON;
using MinorShift.Emuera.UI;
using MinorShift.Emuera.UI.Game;

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
    readonly Font _font;
    readonly Color? _backColor;

    BorderStyle? _borderStyle;



    public ConsoleDivElement(AConsoleDisplayNode[] childNode, string text,
                                StringStyle stringStyle,
                                DisplayMode display = DisplayMode.Relative,
                                int positionX = 0, int positionY = 0,
                                int width = -1, int height = -1,
                                Color? backcolor = null,
                                BorderStyle? borderStyle = null)
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
            var autoSize = TextRenderer.MeasureText(text, _font);
            if (autoWidth)
            {
                Size.Width = autoSize.Width;
            }
            if (autoHeight)
            {
                Size.Height = autoSize.Height;
            }
        }

        _positionX = positionX;
        _positionY = positionY;

        _backColor = backcolor;

        _borderStyle = borderStyle;
    }

    public override bool CanDivide => false;

    public override void DrawTo(Graphics graph, int pointY, bool isSelecting, bool isBackLog, TextDrawingMode mode, bool isButton = false)
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
                graph.DrawString(Text, _font, new SolidBrush(color), Point);
            }
            else
            {
                if (_backColor.HasValue)
                {
                    graph.FillRectangle(new SolidBrush(_backColor.Value), new Rectangle(Point, Size));
                    TextRenderer.DrawText(graph, Text.AsSpan(), _font, Point, color, TextFormatFlags.NoPrefix);
                }
                else if (JSONConfig.Data.UseButtonFocusBackgroundColor && isButton && !isBackLog)
                {
                    if (!backcolor.HasValue)
                    {
                        backcolor = Color.FromArgb(50, 50, 50);
                    }
                    TextRenderer.DrawText(graph, Text.AsSpan(), _font, Point, color, backColor: backcolor.Value, TextFormatFlags.NoPrefix);

                }
                else
                {
                    TextRenderer.DrawText(graph, Text.AsSpan(), _font, Point, color, TextFormatFlags.NoPrefix);
                }

            }
        }

        foreach (var childNode in _childNodes)
        {
            childNode.DrawTo(graph, pointY, isSelecting, isBackLog, mode, isButton);
        }

        if (_borderStyle.HasValue)
        {
            graph.DrawRectangle(_borderStyle.Value.Pen, new Rectangle(Point, Size));
        }
    }

    public override void SetWidth(StringMeasure sm, float subPixel)
    {
        if (Error)
        {
            Width = 0;
            return;
        }
        Width = sm.GetDisplayLength(Text, _font);

        foreach (var childNode in _childNodes)
        {
            childNode.SetWidth(sm, subPixel);
        }
    }
}