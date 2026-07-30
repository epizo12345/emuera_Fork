using System.Windows.Forms;
using SkiaSharp.Views.Desktop;

namespace MinorShift.Emuera.UI.Framework.Forms;

internal sealed class EraPictureBox : SKGLControl
{
    public EraPictureBox()
    {
        //背景描画カット
        SetStyle(ControlStyles.Opaque, true);

        // [Emuera改修:BUILD-02] 修正者: epizo
        // SkiaSharp 4の.NET 10用コントロールでは、旧版のVSyncプロパティが廃止された。
        // OpenGLの準備完了後にSwapIntervalを1へ設定し、従来どおり画面更新を垂直同期させる。
        Load += (_, _) =>
        {
            MakeCurrent();
            Context.SwapInterval = 1;
        };
    }

}
