using MinorShift.Emuera.Runtime.Config;
using SkiaSharp;
using System;
using System.Drawing;
using System.Windows.Forms;
using MinorShift.Emuera.Runtime.Utils;

namespace MinorShift.Emuera.UI.Game;


/// <summary>
/// テキスト長計測装置
/// 1819 必要になるたびにCreateGraphicsする方式をやめてあらかじめGraphicsを用意しておくことにする
/// </summary>
internal sealed class StringMeasure : IDisposable
{

    public static int GetDisplayLength(ReadOnlySpan<char> chars, SKFont f)
    {
        long measureStart = PerformanceMetrics.StartTiming();
        int length = (int)f.MeasureText(chars);
        PerformanceMetrics.AddMeasureText(measureStart);
        return length;
    }


    bool disposed;
    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
    }
}
