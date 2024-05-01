using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Runtime.Config.JSON;
using System;
using System.Drawing;
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
internal sealed class ConsoleStyledString : AConsoleColoredPart
{


    private ConsoleStyledString() { }
    public ConsoleStyledString(string str, StringStyle style, int positionX = 0, int positionY = 0, DisplayMode display = DisplayMode.Relative, Color? backcolor = null)
    {
        //if ((StaticConfig.TextDrawingMode != TextDrawingMode.GRAPHICS) && (str.IndexOf('\t') >= 0))
        //    str = str.Replace("\t", "");
        Str = str;
        StringStyle = style;
        Font = FontFactory.GetFont(style.Fontname, style.FontStyle);
        if (Font == null)
        {
            Error = true;
            return;
        }
        Color = style.Color;
        ButtonColor = style.ButtonColor;
        colorChanged = style.ColorChanged;
        if (!colorChanged && Color != Config.ForeColor)
            colorChanged = true;
        PointX = -1;
        _positionX = positionX;
        _positionY = positionY;
        Display = display;
        Width = -1;

        if (display != DisplayMode.Relative || (_positionY != 0 && _positionX != 0))
        {
            isDiv = true;
        }
        else
        {
            _top = 0;
            _bottom = Config.FontSize;
        }

        _backColor = backcolor;
    }

    int _positionX;
    int _positionY;
    public DisplayMode Display;
    Color? _backColor;

    public Font Font { get; private set; }
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
        if (index <= 0 || index > Str.Length || Error)
            return null;
        string str = Str[index..];
        Str = Str[..index];
        ConsoleStyledString ret = new()
        {
            Font = Font,
            Str = str,
            Color = Color,
            ButtonColor = ButtonColor,
            colorChanged = colorChanged,
            StringStyle = StringStyle,
            XsubPixel = XsubPixel
        };
        return ret;
    }



    int _top;
    public override int Top => _top;

    int _bottom;
    public override int Bottom => _bottom;

    bool isDiv;


    public override void SetWidth(StringMeasure sm, float subPixel)
    {
        if (Error)
        {
            Width = 0;
            return;
        }
        Width = sm.GetDisplayLength(Str, Font);
        XsubPixel = subPixel;
    }

    public override void DrawTo(Graphics graph, int pointY, bool isSelecting, bool isBackLog, TextDrawingMode mode, bool isButton = false)
    {
        if (Error)
            return;
        Color color = Color;
        Color? backcolor = null;
        if (isSelecting)
        {
            if (JSONConfig.Data.UseButtonFocusBackgroundColor)
            {
                if (!(Color.Yellow.R == color.R &&
                        Color.Yellow.G == color.G &&
                        Color.Yellow.B == color.B)
                 && !string.IsNullOrWhiteSpace(Str))
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

        var point = Display switch
        {
            DisplayMode.Relative => new Point(PointX + _positionX, pointY + _positionY),
            DisplayMode.AbsoluteLeftTop => new Point(_positionX, _positionY),
            DisplayMode.AbsoluteLeftBottom => new Point(_positionX, GlobalStatic.Console.ClientHeight - Config.FontSize + _positionY),
            _ => throw new NotImplementedException($"{Display}はまだ実装されていません")
        };

        if (isDiv)
        {
            _top = point.Y;
            _bottom = _top + Config.LineHeight;
        }


        if (mode == TextDrawingMode.GRAPHICS)
        {
            graph.DrawString(Str, Font, new SolidBrush(color), point);
        }
        else
        {
            if (_backColor.HasValue)
            {
                TextRenderer.DrawText(graph, Str.AsSpan(), Font, point, color, backColor: _backColor.Value, TextFormatFlags.NoPrefix);
            }
            else
            {
                if (JSONConfig.Data.UseButtonFocusBackgroundColor)
                {
                    if (isButton && !isBackLog)
                    {
                        if (!backcolor.HasValue)
                        {
                            backcolor = Color.FromArgb(50, 50, 50);
                        }
                        TextRenderer.DrawText(graph, Str.AsSpan(), Font, point, color, backColor: backcolor.Value, TextFormatFlags.NoPrefix);
                    }
                    else
                    {
                        TextRenderer.DrawText(graph, Str.AsSpan(), Font, point, color, TextFormatFlags.NoPrefix);
                    }
                }
                else
                {
                    TextRenderer.DrawText(graph, Str.AsSpan(), Font, point, color, TextFormatFlags.NoPrefix);
                }
            }

        }

    }

}
