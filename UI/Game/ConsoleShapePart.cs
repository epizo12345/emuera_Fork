using MinorShift.Emuera.Runtime.Config;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using System;
using System.Drawing;
using System.Text;

namespace MinorShift.Emuera.UI.Game;

abstract class ConsoleShapePart : AConsoleColoredNode
{
    static public ConsoleShapePart CreateShape(string shapeType, int[] param, Color color, Color bcolor, bool colorchanged)
    {
        string type = shapeType.ToLower();
        colorchanged = colorchanged || color != Config.ForeColor;
        StringBuilder sb = new();
        sb.Append("<shape type='");
        sb.Append(type);
        sb.Append("' param='");
        for (int i = 0; i < param.Length; i++)
        {
            sb.Append(param[i]);
            if (i < param.Length - 1)
                sb.Append(", ");
        }
        sb.Append('\'');
        if (colorchanged)
        {
            sb.Append(" color='");
            sb.Append(HtmlManager.GetColorToString(color));
            sb.Append('\'');
        }
        if (bcolor != Config.FocusColor)
        {
            sb.Append(" bcolor='");
            sb.Append(HtmlManager.GetColorToString(bcolor));
            sb.Append('\'');
        }
        sb.Append('>');
        ConsoleShapePart ret = null;
        int lineHeight = Config.FontSize;
        float[] paramPixel = new float[param.Length];
        for (int i = 0; i < param.Length; i++)
        {
            paramPixel[i] = (float)param[i] * lineHeight / 100f;
        }
        RectangleF rectF;

        switch (type)
        {
            case "space":
                if (paramPixel.Length == 1 && paramPixel[0] >= 0)
                {
                    rectF = new RectangleF(0, 0, paramPixel[0], lineHeight);
                    ret = new ConsoleSpacePart(rectF);
                }
                break;
            case "rect":
                if (paramPixel.Length == 1 && paramPixel[0] > 0)
                {
                    rectF = new RectangleF(0, 0, paramPixel[0], lineHeight);
                    ret = new ConsoleRectangleShapePart(rectF);
                }
                else if (paramPixel.Length == 4)
                {
                    rectF = new RectangleF(paramPixel[0], paramPixel[1], paramPixel[2], paramPixel[3]);
                    //1820a12 サイズ上限撤廃
                    if (rectF.X >= 0 && rectF.Width > 0 && rectF.Height > 0)
                    //	rectF.Y >= 0 && (rectF.Y + rectF.Height) <= lineHeight)
                    {
                        ret = new ConsoleRectangleShapePart(rectF);
                    }
                }
                break;
            case "polygon":
                break;
        }
        if (ret == null)
        {
            ret = new ConsoleErrorShapePart(sb.ToString());
        }
        ret.AltText = sb.ToString();
        ret.Color = color;
        ret.ButtonColor = bcolor;
        ret.colorChanged = colorchanged;
        return ret;
    }

    public override bool CanDivide
    {
        get { return false; }
    }

    public override string ToString()
    {
        if (AltText == null)
            return "";
        return AltText;
    }
}

internal sealed class ConsoleRectangleShapePart : ConsoleShapePart
{
    public ConsoleRectangleShapePart(RectangleF theRect)
    {
        Text = "";
        originalRectF = theRect;
        WidthF = theRect.X + theRect.Width;
        rect.Y = (int)theRect.Y;
        //if (rect.Y == 0 && theRect.Y >= 0.001f)
        //	rect.Y = 1;
        rect.Height = (int)theRect.Height;
        if (rect.Height == 0 && theRect.Height >= 0.001f)
            rect.Height = 1;
        top = Math.Min(0, rect.Y);
        bottom = Math.Max(Config.FontSize, rect.Y + rect.Height);
    }
    private readonly int top;
    private readonly int bottom;
    public override int Top { get { return top; } }
    public override int Bottom { get { return bottom; } }
    readonly RectangleF originalRectF;
    bool visible;
    Rectangle rect;
    public override void DrawTo(SKCanvas graph, SKPoint point, bool isSelecting, bool isBackLog, TextDrawingMode mode, bool isButton = false)
    {
        if (!visible)
            return;
        if (point.X == float.NegativeInfinity)
        {
            point.X = PointX;
        }
        var targetRect = rect.ToSKRect();
        targetRect.Offset((int)point.X - Config.DrawingParam_ShapePositionShift, (int)point.Y);

        Color dcolor = isSelecting ? ButtonColor : Color;

        var paint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = dcolor.ToSKColor()
        };
        graph.DrawRect(targetRect, paint);
    }
    public override void SetWidth(StringMeasure sm, float subPixel)
    {
        float widF = subPixel + WidthF;
        Width = (int)widF;
        XsubPixel = widF - Width;
        rect.X = (int)(subPixel + originalRectF.X);
        rect.Width = Width - rect.X;
        rect.X += Config.DrawingParam_ShapePositionShift;
        visible = rect.X >= 0 && rect.Width > 0;// && rect.Y >= 0 && (rect.Y + rect.Height) <= Config.FontSize);
    }
}

internal sealed class ConsoleSpacePart : ConsoleShapePart
{
    public ConsoleSpacePart(RectangleF theRect)
    {
        Text = "";
        WidthF = theRect.Width;
        //Width = width;
    }

    public override void DrawTo(SKCanvas graph, SKPoint point, bool isSelecting, bool isBackLog, TextDrawingMode mode, bool isButton = false) { }

    public override void SetWidth(StringMeasure sm, float subPixel)
    {
        float widF = subPixel + WidthF;
        Width = (int)widF;
        XsubPixel = widF - Width;
    }
}

internal sealed class ConsoleErrorShapePart : ConsoleShapePart
{
    public ConsoleErrorShapePart(string errMes)
    {
        Text = errMes;
        AltText = errMes;
    }

    public override void DrawTo(SKCanvas graph, SKPoint point, bool isSelecting, bool isBackLog, TextDrawingMode mode, bool isButton = false)
    {
        if (point.X == float.NegativeInfinity)
        {
            point.X = PointX;
        }
        point.Offset(0, -Config.DefaultFont.Metrics.Top);
        graph.DrawText(Text, point, SKTextAlign.Left, Config.DefaultFont, new SKPaint() { Color = Config.ForeColor.ToSKColor() });
    }
    public override void SetWidth(StringMeasure sm, float subPixel)
    {
        if (Error)
        {
            Width = 0;
            return;
        }
        Width = StringMeasure.GetDisplayLength(Text, Config.DefaultFont);
        XsubPixel = subPixel;
    }
}
