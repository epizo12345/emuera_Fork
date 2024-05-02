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
        Width = -1;

    }

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
        Width = sm.GetDisplayLength(Text, Font);
        XsubPixel = subPixel;
    }

    public override void DrawTo(Graphics graph, int pointY, bool isSelecting, bool isBackLog, TextDrawingMode mode, bool isButton = false)
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

        var point = new Point(PointX, pointY);

        if (mode == TextDrawingMode.GRAPHICS)
        {
            graph.DrawString(Text, Font, new SolidBrush(color), point);
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
                    TextRenderer.DrawText(graph, Text.AsSpan(), Font, point, color, backColor: backcolor.Value, TextFormatFlags.NoPrefix);
                }
                else
                {
                    TextRenderer.DrawText(graph, Text.AsSpan(), Font, point, color, TextFormatFlags.NoPrefix);
                }
            }
            else
            {
                TextRenderer.DrawText(graph, Text.AsSpan(), Font, point, color, TextFormatFlags.NoPrefix);
            }

        }

    }

}
