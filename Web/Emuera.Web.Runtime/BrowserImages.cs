using MinorShift.Emuera.Web.Runtime;
using MinorShift.Emuera.Runtime.Config.JSON;
using SkiaSharp;
using System.Drawing;

namespace MinorShift.Emuera.UI.Game.Image;

internal abstract class AbstractImage : IDisposable
{
    public const int MAX_IMAGESIZE = 8192;
    public SKImage? Image { get; protected set; }
    public abstract bool IsCreated { get; }
    public virtual void Dispose() { Image?.Dispose(); Image = null; }
}

internal sealed class ConstImage(string name) : AbstractImage
{
    public string Name { get; } = name;
    public void CreateFrom(SKImage image) => Image = image;
    public override bool IsCreated => Image is not null;
}

internal abstract class ASprite : IDisposable
{
    protected ASprite(string name, Size size) { Name = name; DestBaseSize = size; }
    public string Name { get; }
    public abstract bool IsCreated { get; }
    public Size DestBaseSize { get; }
    public Point DestBasePosition;
    public void Move(Point point) => DestBasePosition.Offset(point);
    public virtual void Dispose() { }
    public SKColor SpriteGetColor(int x, int y) => throw new UnsupportedRuntimeFeatureException("image API");
}

internal sealed class SpriteF : ASprite
{
    public SpriteF(string name, ConstImage image, Rectangle rect, Point offset) : base(name, rect.Size)
    {
        ParentImage = image;
        SourceRectangle = rect;
        DestBasePosition = offset;
    }
    internal ConstImage ParentImage { get; }
    internal Rectangle SourceRectangle { get; }
    // ConstImageは不変なので、このSpriteFの同じData URLを使い回す。
    internal string? CachedWebDataUrl;
    public override bool IsCreated => ParentImage.IsCreated;
}

internal sealed class SpriteG(string name, GraphicsImage image, Rectangle rect) : ASprite(name, rect.Size)
{
    readonly GraphicsImage parent = image;
    internal GraphicsImage ParentImage => parent;
    internal Rectangle SourceRectangle => rect;
    // 親画像の内容が変わるため、URLは同じContentGenerationに限って再利用する。
    internal string? CachedWebDataUrl;
    internal long CachedWebDataUrlGeneration = long.MinValue;
    public override bool IsCreated => parent.IsCreated;
}

internal sealed class SpriteAnime(string name, Size size) : ASprite(name, size)
{
    internal sealed record Frame(AbstractImage ParentImage, Rectangle SourceRectangle, Point Offset, int Delay);
    readonly List<Frame> frames = [];
    public long totaltime;
    public int FrameCount => frames.Count;
    internal IReadOnlyList<Frame> BootstrapFrames => frames;
    internal Frame? CurrentFrame
    {
        get
        {
            // フレームは時間で変わるため、静止SpriteのData URL cacheには載せない。
            if (frames.Count == 0 || totaltime <= 0) return null;
            long elapsed = Environment.TickCount64 % totaltime;
            foreach (Frame frame in frames)
            {
                elapsed -= frame.Delay;
                if (elapsed < 0) return frame;
            }
            return frames[^1];
        }
    }
    public override bool IsCreated => true;
    public bool AddFrame(AbstractImage image, Rectangle rect, Point offset, int delay)
    {
        if (delay <= 0)
            delay = 1;
        Rectangle clipped = Rectangle.Intersect(new Rectangle(offset, rect.Size), new Rectangle(Point.Empty, DestBaseSize));
        if (clipped.IsEmpty)
            image = null!;
        else
        {
            offset = clipped.Location;
            rect.Width = clipped.Width;
            rect.Height = clipped.Height;
        }
        frames.Add(new(image, rect, offset, delay));
        totaltime += delay;
        return true;
    }
    public void ResetTime() => throw new UnsupportedRuntimeFeatureException("animated image playback");
    public override void Dispose() { frames.Clear(); totaltime = 0; }
}

internal sealed class GraphicsImage : AbstractImage
{
    // Webの画像URL cacheを内容更新時に失効させる世代番号。SKImage自体は不変。
    long contentGeneration;
    // GSETBRUSHで渡されるpaintはGraphicsImage所有。fontは共有Factoryから借りる。
    SKPaint? brush;
    SKFont? font;
    internal long ContentGeneration => contentGeneration;
    public override bool IsCreated => Image is not null;
    public int Width => Image?.Width ?? 0;
    public int Height => Image?.Height ?? 0;
    public void GCreate(int x, int y, bool useGDI)
    {
        using SKSurface surface = SKSurface.Create(new SKImageInfo(x, y, SKColorType.Rgba8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.Transparent);
        Image = surface.Snapshot();
        MarkContentChanged();
    }
    public void GCreateFromF(SKImage image, bool useGDI)
    {
        Image = image;
        MarkContentChanged();
    }
    public void GClear(Color color)
    {
        if (Image is null) return;
        using SKSurface surface = SKSurface.Create(new SKImageInfo(Image.Width, Image.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        surface.Canvas.Clear(new SKColor(color.R, color.G, color.B, color.A));
        Image.Dispose();
        Image = surface.Snapshot();
        MarkContentChanged();
    }
    public void GFillRectangle(Rectangle rect) => throw Unsupported();
    public void GDrawPolygon() => throw Unsupported();
    public void GFillPolygon() => throw Unsupported();
    public void GDrawPolygonAddPoint(SKPoint point) => throw Unsupported();
    public void GDrawPolygonClearPoint() => throw Unsupported();
    public void GDrawCImg(ASprite image, Rectangle rect) => DrawSprite(image, rect, null);
    public void GDrawCImg(ASprite image, Rectangle rect, float[][] matrix)
    {
        float[] values =
        [
            matrix[0][0], matrix[1][0], matrix[2][0], matrix[3][0], matrix[0][4],
            matrix[0][1], matrix[1][1], matrix[2][1], matrix[3][1], matrix[1][4],
            matrix[0][2], matrix[1][2], matrix[2][2], matrix[3][2], matrix[2][4],
            matrix[0][3], matrix[1][3], matrix[2][3], matrix[3][3], matrix[3][4]
        ];
        using SKColorFilter filter = SKColorFilter.CreateColorMatrix(values);
        using var paint = new SKPaint { ColorFilter = filter };
        DrawSprite(image, rect, paint);
    }
    public void GDrawG(GraphicsImage source, Rectangle destination, Rectangle sourceRect) => throw Unsupported();
    public void GDrawG(GraphicsImage source, Rectangle destination, Rectangle sourceRect, float[][] matrix) => throw Unsupported();
    public void GDrawGWithMask(GraphicsImage source, GraphicsImage mask, Point destination) => throw Unsupported();
    public void GDrawText(string text, SKPoint point)
    {
        if (Image is null) throw new NullReferenceException();
        SKImage current = Image;
        using SKSurface surface = SKSurface.Create(new SKImageInfo(current.Width, current.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        surface.Canvas.DrawImage(current, 0, 0);
        using SKFont? temporaryFont = font is null ? new SKFont() : null;
        using SKPaint? temporaryBrush = brush is null ? new SKPaint() : null;
        SKFont drawFont = font ?? temporaryFont!;
        SKPaint drawBrush = brush ?? temporaryBrush!;
        point.Offset(0, -drawFont.Metrics.Top);
        surface.Canvas.DrawText(text, point, drawFont, drawBrush);
        SKImage replacement = surface.Snapshot();
        current.Dispose();
        Image = replacement;
        // SKImageは不変なので、描画結果をsnapshotへ置き換え、SpriteGのURLキャッシュを世代で失効させる。
        MarkContentChanged();
    }
    // FontFactoryの共有fontを借りるだけなので、GraphicsImage.Disposeでは実体を解放しない。
    public void GSetFont(SKFont value) => font = value;
    public void GSetBrush(SKPaint value)
    {
        if (ReferenceEquals(brush, value)) return;
        // GSETBRUSHで作られたpaintはGraphicsImageが所有し、置換時と破棄時に解放する。
        brush?.Dispose();
        brush = value;
    }
    public void GSetPen(SKPaint paint) => throw Unsupported();
    public void GSetColor(Color color, int x, int y) => throw Unsupported();
    public SKColor GGetColor(int x, int y) => throw Unsupported();
    public void SavePng(string path) => throw Unsupported();
    public void GDispose() => Dispose();
    public override void Dispose()
    {
        bool hadImage = Image is not null;
        Image?.Dispose();
        Image = null;
        brush?.Dispose();
        brush = null;
        // FontFactoryのfontは共有キャッシュから借りているため、参照だけを消して実体は破棄しない。
        font = null;
        if (hadImage) MarkContentChanged();
    }

    void DrawSprite(ASprite sprite, Rectangle destination, SKPaint? paint)
    {
        if (Image is null || !sprite.IsCreated) return;
        (SKImage source, Rectangle sourceRectangle) = sprite switch
        {
            SpriteF file => (file.ParentImage.Image!, file.SourceRectangle),
            SpriteG generated => (generated.ParentImage.Image!, generated.SourceRectangle),
            _ => throw new UnsupportedRuntimeFeatureException("animated image drawing")
        };
        if (!sprite.DestBasePosition.IsEmpty)
        {
            destination.X += sprite.DestBasePosition.X * destination.Width / sourceRectangle.Width;
            destination.Y += sprite.DestBasePosition.Y * destination.Height / sourceRectangle.Height;
        }
        using SKSurface surface = SKSurface.Create(new SKImageInfo(Image.Width, Image.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        surface.Canvas.DrawImage(Image, 0, 0);
        var sourceRect = new SKRect(sourceRectangle.Left, sourceRectangle.Top, sourceRectangle.Right, sourceRectangle.Bottom);
        var destinationRect = new SKRect(destination.Left, destination.Top, destination.Right, destination.Bottom);
        surface.Canvas.DrawImage(source, sourceRect, destinationRect, JSONConfig.SamplingOptions, paint);
        SKImage replacement = surface.Snapshot();
        Image.Dispose();
        Image = replacement;
        MarkContentChanged();
    }

    void MarkContentChanged() => contentGeneration = unchecked(contentGeneration + 1);
    static UnsupportedRuntimeFeatureException Unsupported() => new("image API");
}
