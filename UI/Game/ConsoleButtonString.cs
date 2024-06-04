using MinorShift.Emuera.GameView;
using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Runtime.Utils;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;

namespace MinorShift.Emuera.UI.Game;

/// <summary>
/// ボタン。1つ以上の装飾付文字列（ConsoleStyledString）からなる。
/// </summary>
internal sealed class ConsoleButtonString : AConsoleDisplayNode
{
    public ConsoleButtonString(EmueraConsole console, AConsoleDisplayNode[] strs)
    {
        parent = console;
        strArray = strs;
        var lineCount = 1;
        foreach (var node in strArray)
        {
            if (node is BrNode)
            {
                lineCount++;
            }
        }
        LineCount = lineCount;
        IsButton = false;
        PointX = -1;
        Width = -1;
        ErrPos = null;
    }
    public ConsoleButtonString(EmueraConsole console, AConsoleDisplayNode[] strs, long input)
        : this(console, strs)
    {
        Input = input;
        Inputs = input.ToString();
        IsButton = true;
        IsInteger = true;
        if (console != null)
        {
            Generation = parent.NewButtonGeneration;
            console.UpdateGeneration();
        }
        ErrPos = null;
    }
    public ConsoleButtonString(EmueraConsole console, AConsoleDisplayNode[] strs, string inputs)
        : this(console, strs)
    {
        Inputs = inputs;
        IsButton = true;
        IsInteger = false;
        if (console != null)
        {
            Generation = parent.NewButtonGeneration;
            console.UpdateGeneration();
        }
        ErrPos = null;
    }

    public ConsoleButtonString(EmueraConsole console, AConsoleDisplayNode[] strs, long input, string inputs)
        : this(console, strs)
    {
        Input = input;
        Inputs = inputs;
        IsButton = true;
        IsInteger = true;
        if (console != null)
        {
            Generation = parent.NewButtonGeneration;
            console.UpdateGeneration();
        }
        ErrPos = null;
    }
    public ConsoleButtonString(EmueraConsole console, AConsoleDisplayNode[] strs, string inputs, ScriptPosition? pos)
        : this(console, strs)
    {
        Inputs = inputs;
        IsButton = true;
        IsInteger = false;
        if (console != null)
        {
            Generation = parent.NewButtonGeneration;
            console.UpdateGeneration();
        }
        ErrPos = pos;
    }

    AConsoleDisplayNode[] strArray;
    public AConsoleDisplayNode[] StrArray { get { return strArray; } }
    EmueraConsole parent;

    public ConsoleDisplayLine ParentLine { get; set; }
    public bool IsButton { get; private set; }
    public bool IsInteger { get; private set; }
    public long Input { get; private set; }
    public string Inputs { get; private set; }
    public bool PointXisLocked { get; set; }
    public long Generation { get; private set; }
    public ScriptPosition? ErrPos { get; set; }
    public string Title { get; set; }

    public int LineCount { get; set; } = 1;


    public int RelativePointX { get; private set; }

    public override bool CanDivide => throw new NotImplementedException();

    public void LockPointX(int rel_px)
    {
        PointX = rel_px * Config.FontSize / 100;
        XsubPixel = rel_px * Config.FontSize / 100.0f - PointX;
        PointXisLocked = true;
        RelativePointX = rel_px;
    }

    //indexの文字数の前方文字列とindex以降の後方文字列に分割
    public ConsoleButtonString DivideAt(int divIndex, StringMeasure sm)
    {
        if (divIndex <= 0)
            return null;
        List<AConsoleDisplayNode> cssListA = [];
        List<AConsoleDisplayNode> cssListB = [];
        int index = 0;
        int cssIndex;
        bool b = false;
        for (cssIndex = 0; cssIndex < strArray.Length; cssIndex++)
        {
            if (b)
            {
                cssListB.Add(strArray[cssIndex]);
                continue;
            }
            int length = strArray[cssIndex].Text.Length;
            if (divIndex < index + length)
            {
                ConsoleStyledString oldcss = strArray[cssIndex] as ConsoleStyledString;
                if (oldcss == null || !oldcss.CanDivide)
                    throw new ExeEE("文字列分割異常");
                ConsoleStyledString newCss = oldcss.DivideAt(divIndex - index, sm);
                cssListA.Add(oldcss);
                if (newCss != null)
                    cssListB.Add(newCss);
                b = true;
                continue;
            }
            else if (divIndex == index + length)
            {
                cssListA.Add(strArray[cssIndex]);
                b = true;
                continue;
            }
            index += length;
            cssListA.Add(strArray[cssIndex]);
        }
        if (cssIndex >= strArray.Length && cssListB.Count == 0)
            return null;
        AConsoleDisplayNode[] cssArrayA = new AConsoleDisplayNode[cssListA.Count];
        AConsoleDisplayNode[] cssArrayB = new AConsoleDisplayNode[cssListB.Count];
        cssListA.CopyTo(cssArrayA);
        cssListB.CopyTo(cssArrayB);
        strArray = cssArrayA;
        ConsoleButtonString ret = new(null, cssArrayB);
        CalcWidth(sm, XsubPixel);
        ret.CalcWidth(sm, 0);
        CalcPointX(PointX);
        ret.CalcPointX(PointX + Width);
        ret.parent = parent;
        ret.ParentLine = ParentLine;
        ret.IsButton = IsButton;
        ret.IsInteger = IsInteger;
        ret.Input = Input;
        ret.Inputs = Inputs;
        ret.Generation = Generation;
        ret.ErrPos = ErrPos;
        ret.Title = Title;
        return ret;
    }

    public void CalcWidth(StringMeasure sm, float subpixel)
    {
        Width = 0;
        if (strArray != null && strArray.Length > 0)
        {
            Width = 0;
            foreach (AConsoleDisplayNode css in strArray)
            {
                if (css.Width <= 0)
                    css.SetWidth(sm, subpixel);
                Width += css.Width;
                subpixel = css.XsubPixel;
            }
            if (Width <= 0)
                Width = -1;
        }
        XsubPixel = subpixel;
    }

    /// <summary>
    /// 先にCalcWidthすること。
    /// </summary>
    /// <param name="sm"></param>
    public void CalcPointX(int pointx)
    {
        int px = pointx;
        if (!PointXisLocked)
            PointX = px;
        else
            px = PointX;
        for (int i = 0; i < strArray.Length; i++)
        {
            strArray[i].PointX = px;
            px += strArray[i].Width;
        }
        if (strArray.Length > 0)
        {
            PointX = strArray[0].PointX;
            Width = strArray[^1].PointX + strArray[^1].Width - PointX;
            //if (Width < 0)
            //	Width = -1;
        }
    }

    internal void ShiftPositionX(int shiftX)
    {
        PointX += shiftX;
        foreach (AConsoleDisplayNode css in strArray)
            css.PointX += shiftX;
    }

    public void DrawTo(SKCanvas graph, SKPoint point, bool isBackLog, TextDrawingMode mode)
    {
        bool isSelecting = IsButton && parent.ButtonIsSelected(this);

        if (point.X == float.NegativeInfinity)
        {
            point.X = PointX;
        }

        var offset = point;
        foreach (var css in strArray)
        {
            if (css is BrNode)
            {
                point.X = offset.X + Config.DrawingParam_ShapePositionShift;
                point.Offset(0, Config.LineHeight);
            }
            else
            {
                css.DrawTo(graph, point, isSelecting, isBackLog, mode, IsButton);

                point.Offset(css.Width, 0);
                offset = point;
            }

        }
    }

    readonly static StringBuilder builder = new();
    public override string ToString()
    {
        if (strArray == null)
            return "";
        builder.Clear();
        foreach (var css in strArray)
            builder.Append(css.ToString());
        return builder.ToString();
    }

    public override void DrawTo(SKCanvas graph, SKPoint point, bool isSelecting, bool isBackLog, TextDrawingMode mode, bool isButton = false)
    {
        DrawTo(graph, point, isBackLog, mode);
    }

    public override void SetWidth(StringMeasure sm, float subPixel)
    {
        CalcWidth(sm, subPixel);
    }
}
