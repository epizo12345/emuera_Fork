using System;
using System.Drawing;

namespace MinorShift.Emuera.Content;

internal abstract class AbstractImage : IDisposable
{
    public const int MAX_IMAGESIZE = 8192;
    public Bitmap Bitmap;
    public IntPtr GDIhDC { get; protected set; }
    protected Graphics g;

    public abstract bool IsCreated { get; }

    public abstract void Dispose();
}