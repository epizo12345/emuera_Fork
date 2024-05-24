using MinorShift.Emuera.Runtime.Config.JSON;
using MinorShift.Emuera.Runtime.Utils;
using MinorShift.Emuera.Sub;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;

namespace MinorShift.Emuera.UI.Game.Image;



internal abstract class ASprite : AContentItem, IDisposable
{
    public ASprite(string name, Size size)
        : base(name)
    {
        if (size.Width < 0)
            size.Width = -size.Width;
        if (size.Height < 0)
            size.Height = -size.Height;
        DestBaseSize = size;
    }
    public abstract SKColor SpriteGetColor(int x, int y);
    /// <summary>
    /// 出力される標準のサイズ。正の値のみ。
    /// </summary>
    public readonly Size DestBaseSize;

    /// <summary>
    /// 出力時の位置調整。拡大縮小して出力する場合には同じ比率で調整する。
    /// </summary>
    public Point DestBasePosition;


    public abstract void GraphicsDraw(SKCanvas g, Point offset);
    public abstract void GraphicsDraw(SKCanvas g, Rectangle destRect);
    public abstract void GraphicsDraw(SKCanvas g, Rectangle destRect, SKColorFilter attr);
    public abstract void Dispose();
    public void Move(Point point) { DestBasePosition.Offset(point); }
}


internal abstract class ASpriteSingle : ASprite
{
    public ASpriteSingle(string name, AbstractImage img, Rectangle rect)
        : base(name, rect.Size)
    {
        SrcRectangle = rect;
        BaseImage = img;
    }
    public AbstractImage BaseImage;

    /// <summary>
    /// ソース画像上の位置を指定する四角形。Width, Heightは負の値をとり得る
    /// </summary>
    public readonly Rectangle SrcRectangle;

    SKImage _image;
    SKImage Image
    {
        get
        {
            if (BaseImage != null && BaseImage.IsCreated)
            {
                if (BaseImage.Image != null)
                {
                    return BaseImage.Image;
                }
                else
                {
                    _image?.Dispose();
                    _image = SKImage.FromBitmap(BaseImage.Bitmap);
                    return _image;
                }
            }
            return null;
        }
    }

    public override bool IsCreated
    {
        get { return BaseImage != null && BaseImage.IsCreated; }
    }
    public override SKColor SpriteGetColor(int x, int y)
    {
        if (Image == null)
            return SKColors.Transparent;
        int bmpX = x + SrcRectangle.X;
        int bmpY = y + SrcRectangle.Y;
        if (bmpX < 0 || bmpX >= Image.Width || bmpY < 0 || bmpY >= Image.Height)
            return SKColors.Transparent;

        var pixMap = Image.PeekPixels();
        return pixMap.GetPixelColor(bmpX, bmpY);
    }
    public override void Dispose()
    {
        BaseImage = null;
    }

    SKPaint _paint = new();

    public override void GraphicsDraw(SKCanvas g, Point offset)
    {
        offset.Offset(DestBasePosition);
        // g.DrawImage(Bitmap.ToBitmap(), new Rectangle(offset, DestBaseSize), SrcRectangle, GraphicsUnit.Pixel);
        //g.DrawBitmap(Bitmap, SrcRectangle.ToSKRect(), SKRect.Create(offset.ToSKPoint(), SrcRectangle.Size.ToSKSize()), _paint);
        g.DrawImage(Image, SrcRectangle.ToSKRect(), SKRect.Create(offset.ToSKPoint(), SrcRectangle.Size.ToSKSize()), JSONConfig.SamplingOptions, _paint);
    }


    public override void GraphicsDraw(SKCanvas g, Rectangle destRect)
    {
        if (!DestBasePosition.IsEmpty)
        {
            destRect.X = destRect.X + DestBasePosition.X * destRect.Width / SrcRectangle.Width;
            destRect.Y = destRect.Y + DestBasePosition.Y * destRect.Height / SrcRectangle.Height;
        }
        //g.DrawImage(Bitmap.ToBitmap(), destRect, SrcRectangle, GraphicsUnit.Pixel);

        var sx = Math.Sign(destRect.Width);
        var sy = Math.Sign(destRect.Height);
        if (sx != 1 || sy != 1)
        {
            var flipedBitmap = new SKBitmap(Math.Abs(destRect.Width), Math.Abs(destRect.Height));
            using var canvas = new SKCanvas(flipedBitmap);

            canvas.Scale(sx, sy, flipedBitmap.Width / 2, flipedBitmap.Height / 2);
            canvas.DrawImage(Image, SrcRectangle.ToSKRect(), SKRect.Create(new SKPoint(), flipedBitmap.Info.Size), JSONConfig.SamplingOptions);
            //canvas.DrawBitmap(Bitmap, SrcRectangle.ToSKRect(), SKRect.Create(new SKPoint(), flipedBitmap.Info.Size), _paint);

            var point = destRect.Location.ToSKPoint();
            if (sx < 0)
            {
                point.Offset(-flipedBitmap.Width, 0);
            }
            if (sy < 0)
            {
                point.Offset(0, -flipedBitmap.Width);
            }

            g.DrawBitmap(flipedBitmap, point, _paint);
        }
        else
        {
            g.DrawImage(Image, SrcRectangle.ToSKRect(), destRect.ToSKRect(), JSONConfig.SamplingOptions, _paint);
            //g.DrawBitmap(Bitmap, SrcRectangle.ToSKRect(), destRect.ToSKRect(), _paint);
        }
    }

    public override void GraphicsDraw(SKCanvas g, Rectangle destRect, SKColorFilter attr)
    {
        if (!DestBasePosition.IsEmpty)
        {
            destRect.X = destRect.X + DestBasePosition.X * destRect.Width / SrcRectangle.Width;
            destRect.Y = destRect.Y + DestBasePosition.Y * destRect.Height / SrcRectangle.Height;
        }
        //g.DrawImage(Bitmap, destRect, SrcRectangle, GraphicsUnit.Pixel, attr);←このパターンがない
        //g.DrawImage(Bitmap.ToBitmap(), destRect, SrcRectangle.X, SrcRectangle.Y, SrcRectangle.Width, SrcRectangle.Height, GraphicsUnit.Pixel, attr);

        var sx = Math.Sign(destRect.Width);
        var sy = Math.Sign(destRect.Height);
        if (sx != 1 || sy != 1)
        {
            var flipedBitmap = new SKBitmap(Math.Abs(destRect.Width), Math.Abs(destRect.Height));
            using var canvas = new SKCanvas(flipedBitmap);

            canvas.Scale(sx, sy, flipedBitmap.Width / 2, flipedBitmap.Height / 2);
            canvas.DrawImage(Image, SrcRectangle.ToSKRect(), SKRect.Create(new SKPoint(), flipedBitmap.Info.Size));
            var point = destRect.Location.ToSKPoint();
            if (sx < 0)
            {
                point.Offset(-flipedBitmap.Width, 0);
            }
            if (sy < 0)
            {
                point.Offset(0, -flipedBitmap.Width);
            }

            _paint.ColorFilter = attr;
            g.DrawBitmap(flipedBitmap, point, _paint);
            _paint.ColorFilter = null;
        }
        else
        {
            _paint.ColorFilter = attr;
            g.DrawImage(Image, SrcRectangle.ToSKRect(), destRect.ToSKRect(), _paint);
            _paint.ColorFilter = null;
        }

    }

}

/// <summary>
/// ERB中で作るGを元にしたSprite。GDI非対応
/// </summary>
internal sealed class SpriteG : ASpriteSingle
{
    public SpriteG(string name, GraphicsImage gra, Rectangle rect)
        : base(name, gra, rect)
    {
    }

}

/// <summary>
/// ConstImage(csvから作るファイル占有型ベースイメージ)をもとにしたSprite
/// </summary>
internal sealed class SpriteF : ASpriteSingle
{
    public SpriteF(string name, ConstImage image, Rectangle rect, Point pos)
        : base(name, image, rect)
    {
        DestBasePosition = pos;
    }
}

/// <summary>
/// AnimeするSprite。中身はほぼSprite
/// </summary>
internal sealed class SpriteAnime : ASprite
{
    public SpriteAnime(string name, Size size)
        : base(name, size)
    {
        FrameList = [];
        totaltime = 0;
    }
    private sealed class AnimeFrame : IDisposable
    {
        public int index;
        public AbstractImage BaseImage;
        public Rectangle SrcRectangle;
        public Point Offset;
        public int DelayTimeMs;
        public void Normalize(Size parentSize)
        {
            Rectangle rect = Rectangle.Intersect(new Rectangle(Offset, SrcRectangle.Size), new Rectangle(new Point(), parentSize));
            if (rect.IsEmpty)
            {
                BaseImage = null;
                return;
            }
            Offset.X = rect.X;
            Offset.Y = rect.Y;
            SrcRectangle.Width = rect.Width;
            SrcRectangle.Height = rect.Height;
        }
        public void Dispose()
        {
            BaseImage = null;
        }
    }
    List<AnimeFrame> FrameList;
    public long totaltime;

    internal bool AddFrame(AbstractImage parentImage, Rectangle rect, Point pos, int delay)
    {
        AnimeFrame frame = new()
        {
            index = FrameList.Count,
            BaseImage = parentImage,
            SrcRectangle = rect,
            Offset = pos
        };
        if (delay <= 0)
            delay = 1;
        frame.DelayTimeMs = delay;
        frame.Normalize(DestBaseSize);
        totaltime += delay;
        FrameList.Add(frame);
        return true;
    }

    /// <summary>
    /// アニメの経過時間を削除して最初からやり直す
    /// </summary>
    internal void ResetTime()
    {
        StartTime = DateTime.Now;
        lastFrameTime = DateTime.Now;
        lastFrame = -1;
    }

    /// <summary>
    /// 開始時間調整用の値。ミリ秒でUInt32の範囲まで想定。
    /// </summary>
    DateTime StartTime;
    DateTime lastFrameTime;
    int lastFrame = -1;
    private AnimeFrame GetCurrentFrame()
    {
        if (totaltime <= 0)
            return null;
#if DEBUG
        if (FrameList.Count == 0)
            throw new ExeEE("totaltime > 0なのにFrameListが空");
        if (lastFrame >= FrameList.Count)
            throw new ExeEE("SpriteAnime:最終フレームが範囲外");
#endif
        //一度もフレーム取得したことがない場合は現在時間を記録して最初のフレームを返す。
        if (lastFrame == -1)
        {
            StartTime = DateTime.Now;
            lastFrame = 0;
            return FrameList[0];
        }
        //時間経過なしに複数回呼ばれた場合はさっき返したフレームをもう一度返す。
        if (DateTime.Now == lastFrameTime && lastFrame >= 0)
            return FrameList[lastFrame];
        //StartTimeからの経過時間(ms)をtotaltimeで剰余計算
        var elapsedTime = (DateTime.Now - StartTime).Milliseconds % totaltime;

        foreach (AnimeFrame frame in FrameList)
        {
            elapsedTime -= frame.DelayTimeMs;
            if (elapsedTime <= 0)
            {
                lastFrame = frame.index;
                return frame;
            }
        }
        //ここまでこないはず
        throw new ExeEE("SpriteAnime:時間外参照");
    }

    public override bool IsCreated
    {
        get { return true; }
    }

    public override void Dispose()
    {
        foreach (var frame in FrameList)
            frame.Dispose();
        FrameList.Clear();
        totaltime = 0;
        lastFrame = -1;
    }


    public override SKColor SpriteGetColor(int x, int y)
    {
        throw new NotSupportedException();
        //Bitmap bmp = this.Bitmap;
        //if (bmp == null)
        //	return Color.Transparent;
        //int bmpX = x + SrcRectangle.X;
        //int bmpY = y + SrcRectangle.Y;
        //if (bmpX < 0 || bmpX >= bmp.Width || bmpY < 0 || bmpY >= bmp.Height)
        //	return Color.Transparent;

        //return bmp.GetPixel(bmpX, bmpY);
    }


    public override void GraphicsDraw(SKCanvas g, Point offset)
    {
        AnimeFrame frame = GetCurrentFrame();
        if (frame == null || frame.BaseImage == null || !frame.BaseImage.IsCreated)
            return;
        offset.Offset(DestBasePosition);
        offset.Offset(frame.Offset);
        Rectangle destRect = new(offset, frame.SrcRectangle.Size);
        //g.DrawImage(frame.BaseImage.SKBitmap.ToBitmap(), destRect, frame.SrcRectangle, GraphicsUnit.Pixel);

        g.DrawBitmap(frame.BaseImage.Bitmap, new SKPoint(0, 0));
        return;
    }

    public override void GraphicsDraw(SKCanvas g, Rectangle destRect)
    {
        AnimeFrame frame = GetCurrentFrame();
        if (frame == null || frame.BaseImage == null || !frame.BaseImage.IsCreated)
            return;
        destRect.X = destRect.X + (DestBasePosition.X + frame.Offset.X) * destRect.Width / DestBaseSize.Width;
        destRect.Y = destRect.Y + (DestBasePosition.Y + frame.Offset.Y) * destRect.Height / DestBaseSize.Height;
        destRect.Width = frame.SrcRectangle.Width * destRect.Width / DestBaseSize.Width;
        destRect.Height = frame.SrcRectangle.Height * destRect.Height / DestBaseSize.Height;
        //g.DrawImage(frame.BaseImage.SKBitmap.ToBitmap(), destRect, frame.SrcRectangle, GraphicsUnit.Pixel);

        g.DrawBitmap(frame.BaseImage.Bitmap, new SKPoint(0, 0));
    }

    public override void GraphicsDraw(SKCanvas g, Rectangle destRect, SKColorFilter attr)
    {
        AnimeFrame frame = GetCurrentFrame();
        if (frame == null || frame.BaseImage == null || !frame.BaseImage.IsCreated)
            return;
        destRect.X = destRect.X + (DestBasePosition.X + frame.Offset.X) * destRect.Width / DestBaseSize.Width;
        destRect.Y = destRect.Y + (DestBasePosition.Y + frame.Offset.Y) * destRect.Height / DestBaseSize.Height;
        destRect.Width = frame.SrcRectangle.Width * destRect.Width / DestBaseSize.Width;
        destRect.Height = frame.SrcRectangle.Height * destRect.Height / DestBaseSize.Height;
        //g.DrawImage(frame.BaseImage.Bitmap, destRect, SrcRectangle, GraphicsUnit.Pixel, attr);←このパターンがない
        //g.DrawImage(frame.BaseImage.SKBitmap.ToBitmap(), destRect, frame.SrcRectangle.X, frame.SrcRectangle.Y, frame.SrcRectangle.Width, frame.SrcRectangle.Height, GraphicsUnit.Pixel, attr);

        g.DrawBitmap(frame.BaseImage.Bitmap, new SKPoint(0, 0));
    }

}
