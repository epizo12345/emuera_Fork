using System;
using System.Drawing;
using System.Reflection;
using MinorShift.Emuera.UI.Game.Image;
using SkiaSharp;
using RuntimeConfig = MinorShift.Emuera.Runtime.Config.Config;
using JSONConfig = MinorShift.Emuera.Runtime.Config.JSON.JSONConfig;

namespace Emuera.ConfigRegressionTests;

// [Emuera改修:DRAW-07/DRAW-08 2026-10-28]
// 元Emuera（GDI+）との描画差異の修正を確認する。設定ファイルは読まないので、背景色と画像補間は試験中だけ差し替えて戻す。
internal static class DrawingDiffRegression
{
    // GSETBRUSH未設定のGFILLRECTANGLEは背景色で塗る（旧実装は不透明の黒）。
    internal static void FillRectangleWithoutBrushUsesBackColor()
    {
        PropertyInfo prop = typeof(RuntimeConfig).GetProperty(nameof(RuntimeConfig.BackColor))!;
        Color saved = RuntimeConfig.BackColor;
        try
        {
            prop.SetValue(null, Color.FromArgb(255, 10, 20, 30));
            using var g = new GraphicsImage();
            g.GCreate(2, 2, false);
            g.GFillRectangle(new Rectangle(0, 0, 2, 2));
            SKColor c = g.GGetColor(1, 1);
            if (c != new SKColor(10, 20, 30, 255))
                throw new InvalidOperationException($"expected background color 0A141E, got {c}");
        }
        finally { prop.SetValue(null, saved); }
    }

    // GDRAWGの拡大縮小は設定の画像補間に従う（旧実装は補間なし固定）。カラーマトリクス付きも同じ。
    internal static void DrawGScalingFollowsSamplingSetting()
    {
        PropertyInfo prop = typeof(JSONConfig).GetProperty(nameof(JSONConfig.SamplingOptions))!;
        SKSamplingOptions saved = JSONConfig.SamplingOptions;
        try
        {
            prop.SetValue(null, new SKSamplingOptions(SKFilterMode.Linear));
            CheckInterpolated(withMatrix: false, "linear");
            CheckInterpolated(withMatrix: true, "linear with color matrix");
            prop.SetValue(null, new SKSamplingOptions(SKFilterMode.Nearest));
            byte[] nearest = DrawScaled(withMatrix: false);
            foreach (byte r in nearest)
                if (r != 0 && r != 255)
                    throw new InvalidOperationException($"nearest setting should not interpolate: {string.Join(",", nearest)}");
        }
        finally { prop.SetValue(null, saved); }
    }

    static void CheckInterpolated(bool withMatrix, string label)
    {
        byte[] r = DrawScaled(withMatrix);
        bool hasMiddle = false;
        foreach (byte v in r)
            if (v > 20 && v < 235)
                hasMiddle = true;
        // 端は元の色のまま、途中に中間色がある（黒→白で単調に増える）こと。
        if (!hasMiddle || r[0] > 20 || r[^1] < 235)
            throw new InvalidOperationException($"{label}: expected interpolation, got {string.Join(",", r)}");
        for (int i = 1; i < r.Length; i++)
            if (r[i] < r[i - 1])
                throw new InvalidOperationException($"{label}: expected monotonic gradient, got {string.Join(",", r)}");
    }

    // 黒白2pxを8pxへ拡大し、各画素のR値を返す。
    static byte[] DrawScaled(bool withMatrix)
    {
        using var src = new GraphicsImage();
        src.GCreate(2, 1, false);
        src.GSetColor(Color.FromArgb(255, 0, 0, 0), 0, 0);
        src.GSetColor(Color.FromArgb(255, 255, 255, 255), 1, 0);
        using var dst = new GraphicsImage();
        dst.GCreate(8, 1, false);
        var destRect = new Rectangle(0, 0, 8, 1);
        var srcRect = new Rectangle(0, 0, 2, 1);
        if (withMatrix)
        {
            float[][] identity = [[1, 0, 0, 0, 0], [0, 1, 0, 0, 0], [0, 0, 1, 0, 0], [0, 0, 0, 1, 0], [0, 0, 0, 0, 1]];
            dst.GDrawG(src, destRect, srcRect, identity);
        }
        else
            dst.GDrawG(src, destRect, srcRect);
        var result = new byte[8];
        for (int x = 0; x < 8; x++)
            result[x] = dst.GGetColor(x, 0).Red;
        return result;
    }
}
