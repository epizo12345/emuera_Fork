using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.UI.Game.Image;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using System;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Text;
namespace MinorShift.Emuera.UI.Game;

sealed class ConsoleImagePart : AConsoleDisplayNode
{

    public ConsoleImagePart(string resName, string resNameb, int raw_height, int raw_width, int raw_ypos, int raw_xpos, DisplayMode display = DisplayMode.Relative)
    {
        top = 0;
        bottom = Config.FontSize;
        Text = "";
        ResourceName = resName ?? "";
        ButtonResourceName = resNameb;
        cImage = AppContents.GetSprite(ResourceName);
        //if (cImage != null && !cImage.IsCreated)
        //	cImage = null;
        if (cImage == null)
        {
            Text = AltText;
            return;
        }
        int height;

        if (raw_height == 0)
        {
            //HTMLで高さが指定されていない又は0が指定された場合、フォントサイズをそのまま高さ(px単位)として使用する。
            height = Config.FontSize;
        }
        else
        {
            height = raw_height;
        }
        //幅が指定されていない又は0が指定された場合、元画像の縦横比を維持するように幅(px単位)を設定する。1未満は端数としてXsubpixelに記録。
        //負の値が指定される可能性があるが、最終的なWidthは正の値になるようにあとで調整する。
        if (raw_width == 0)
        {
            Width = cImage.DestBaseSize.Width * height / cImage.DestBaseSize.Height;
            XsubPixel = (float)cImage.DestBaseSize.Width * height / cImage.DestBaseSize.Height - Width;
        }
        else
        {
            Width = raw_width;
            XsubPixel = (float)Config.FontSize * raw_width / 100f - Width;
        }
        top = raw_ypos;
        destRect = new Rectangle(0, top, Width, height);
        if (destRect.Width < 0)
        {
            destRect.X = -destRect.Width;
            Width = -destRect.Width;
        }
        if (destRect.Height < 0)
        {
            destRect.Y = destRect.Y - destRect.Height;
            height = -destRect.Height;
        }
        bottom = top + height;
        //if(top > 0)
        //	top = 0;
        //if(bottom < Config.FontSize)
        //	bottom = Config.FontSize;
        if (ButtonResourceName != null)
        {
            cImageB = AppContents.GetSprite(ButtonResourceName);
            //if (cImageB != null && !cImageB.IsCreated)
            //	cImageB = null;
        }

        _display = display;
        _positionX = raw_xpos;
        _positionY = raw_ypos;
        Size = new SKSize(Width, height);
    }

    string _altText;

    new string AltText
    {
        get
        {
            if (_altText == null)
            {
                var sb = new DefaultInterpolatedStringHandler();
                sb.AppendLiteral("<img src='");
                sb.AppendFormatted(ResourceName);
                if (ButtonResourceName != null)
                {
                    sb.AppendLiteral("' srcb='");
                    sb.AppendFormatted(ButtonResourceName);
                }
                if ((bottom - top) != 0)
                {
                    sb.AppendLiteral("' height='");
                    sb.AppendFormatted(bottom - top);
                }
                if (Width != 0)
                {
                    sb.AppendLiteral("' width='");
                    sb.AppendFormatted(Width);
                }
                if (_positionY != 0)
                {
                    sb.AppendLiteral("' ypos='");
                    sb.AppendFormatted(_positionY);
                }
                sb.AppendLiteral("'>");
                AltText = sb.ToString();
            }
            return _altText;
        }
        set
        {
            _altText = value;
        }
    }


    private readonly ASprite cImage;
    private readonly ASprite cImageB;
    private readonly int top;
    private readonly int bottom;
    private readonly Rectangle destRect;
    //#pragma warning disable CS0649 // フィールド 'ConsoleImagePart.ia' は割り当てられません。常に既定値 null を使用します。
    //		private readonly ImageAttributes ia;
    //#pragma warning restore CS0649 // フィールド 'ConsoleImagePart.ia' は割り当てられません。常に既定値 null を使用します。
    public readonly string ResourceName;
    public readonly string ButtonResourceName;
    public override int Top { get { return top; } }
    public override int Bottom { get { return bottom; } }

    DisplayMode _display;
    int _positionX;
    int _positionY;

    public override bool CanDivide { get { return false; } }
    public override void SetWidth(StringMeasure sm, float subPixel)
    {
        if (Error)
        {
            Width = 0;
            return;
        }
        if (cImage != null)
            return;
        Width = StringMeasure.GetDisplayLength(Text, Config.DefaultFont);
        XsubPixel = subPixel;
    }

    public override string ToString()
    {
        if (AltText == null)
            return "";
        return AltText;
    }

    public override void DrawTo(SKCanvas graph, SKPoint point, bool isSelecting, bool isBackLog, TextDrawingMode mode, bool isButton = false)
    {
        if (Error)
            return;
        ASprite img = cImage;
        if (isSelecting && cImageB != null)
            img = cImageB;

        if (img != null && img.IsCreated)
        {
            Point = point;

            var rect = destRect;
            //PointX微調整
            switch (_display)
            {
                case DisplayMode.Relative:
                    rect.X = destRect.X + (int)point.X;
                    rect.Y = destRect.Y + (int)point.Y;
                    break;
                case DisplayMode.AbsoluteLeftTop:
                    rect.X = _positionX;
                    rect.Y = _positionY;
                    break;
                case DisplayMode.AbsoluteLeftBottom:
                    rect.X = _positionX;
                    rect.Y = GlobalStatic.Console.ClientHeight + _positionY;
                    break;
                default:
                    throw new NotImplementedException();
            }
            img.GraphicsDraw(graph, rect);
        }
        else
        {
            point.Offset(0, -Config.DefaultFont.Metrics.Top);
            graph.DrawText(AltText, point, SKTextAlign.Left, Config.DefaultFont, new SKPaint() { Color = Config.ForeColor.ToSKColor() });
        }
    }
}
