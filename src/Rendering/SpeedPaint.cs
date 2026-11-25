using System;
using SkiaSharp;

namespace NPS.NetSpeed.Rendering;

/// <summary>配色与几何绘制工具。所有颜色都从宿主当帧主题派生，随文字黑白模式自适应。</summary>
internal static class SpeedPaint
{
    public static readonly SKColor DownHue = new(64, 140, 255);
    public static readonly SKColor UpHue = new(255, 148, 64);

    /// <summary>
    /// 强调色：把品牌色往主题文字色方向拉 30%。
    /// 黑字模式下自然变深、白字模式下自然变亮，两种底色都能保住对比度。
    /// </summary>
    public static SKColor Accent(SKColor textColor, SKColor hue, byte alpha)
        => Blend(hue, textColor, 0.30f).WithAlpha(alpha);

    public static SKColor Blend(SKColor from, SKColor to, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        byte r = (byte)Math.Clamp(from.Red + (to.Red - from.Red) * t, 0f, 255f);
        byte g = (byte)Math.Clamp(from.Green + (to.Green - from.Green) * t, 0f, 255f);
        byte b = (byte)Math.Clamp(from.Blue + (to.Blue - from.Blue) * t, 0f, 255f);
        return new SKColor(r, g, b);
    }

    /// <summary>用在强调色实心块上的文字色（跟着块本身的亮度走，而不是主题）。</summary>
    public static SKColor OnAccent(SKColor accentBackground)
    {
        float luminance = (0.299f * accentBackground.Red + 0.587f * accentBackground.Green +
                           0.114f * accentBackground.Blue) / 255f;
        return luminance > 0.62f ? new SKColor(20, 20, 20) : SKColors.White;
    }

    public static SKPaint CreateArrowPaint()
        => new()
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.25f,
            StrokeCap = SKStrokeCap.Round,
        };

    /// <summary>矢量箭头，不依赖字体里的 “↓ / ↑” 字形。</summary>
    public static void DrawArrow(SKCanvas canvas, float cx, float cy, bool up, SKPaint paint)
    {
        float direction = up ? -1f : 1f;
        const float half = 5f;
        const float wing = 2.9f;
        float tipY = cy + half * direction;
        float tailY = cy - half * direction;

        canvas.DrawLine(cx, tailY, cx, tipY, paint);
        canvas.DrawLine(cx, tipY, cx - wing, tipY - 3.1f * direction, paint);
        canvas.DrawLine(cx, tipY, cx + wing, tipY - 3.1f * direction, paint);
    }

    /// <summary>按时间顺序重建趋势线，保留路径缓冲供下一次更新。</summary>
    public static void UpdateSparkline(SKPath path, SKRect area, float[] samples, int count)
    {
        path.Rewind();
        if (count < 2) return;

        float max = 1024f;
        for (int i = 0; i < count; i++)
        {
            if (samples[i] > max) max = samples[i];
        }
        max *= 1.12f;

        for (int i = 0; i < count; i++)
        {
            float x = area.Left + area.Width * i / (count - 1);
            float y = area.Bottom - Math.Clamp(samples[i] / max, 0f, 1f) * area.Height;
            if (i == 0) path.MoveTo(x, y);
            else path.LineTo(x, y);
        }
    }
}
