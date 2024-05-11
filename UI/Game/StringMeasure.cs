using MinorShift.Emuera.Runtime.Config;
using SkiaSharp;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace MinorShift.Emuera.UI.Game;


/// <summary>
/// テキスト長計測装置
/// 1819 必要になるたびにCreateGraphicsする方式をやめてあらかじめGraphicsを用意しておくことにする
/// </summary>
internal sealed class StringMeasure : IDisposable
{

    public static int GetDisplayLength(ReadOnlySpan<char> chars, SKFont f)
    {
        return (int)f.MeasureText(chars);
    }


    bool disposed;
    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
    }
}
