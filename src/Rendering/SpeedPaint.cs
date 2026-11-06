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

    /// <summary>矢量箭头，不依赖字体里有没有 “↓ / ↑” 字形。</summary>
    public static void DrawArrow(SKCanvas canvas, float cx, float cy, bool up, SKColor color, float scale = 1f)
    {
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color = color,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.25f * scale,
            StrokeCap = SKStrokeCap.Round,
        };

        float direction = up ? -1f : 1f;
        float half = 5.0f * scale;
        float wing = 2.9f * scale;
        float tipY = cy + half * direction;
        float tailY = cy - half * direction;

        canvas.DrawLine(cx, tailY, cx, tipY, paint);
        canvas.DrawLine(cx, tipY, cx - wing, tipY - 3.1f * scale * direction, paint);
        canvas.DrawLine(cx, tipY, cx + wing, tipY - 3.1f * scale * direction, paint);
    }

    /// <summary>最近 6 秒的折线趋势。只画线，不做面积填充，避免任何「条形 / 进度」观感。</summary>
    public static void DrawSparkline(SKCanvas canvas, SKRect area, float[] samples, int count,
        SKColor accent, SKColor baseline)
    {
        using (var lineBase = new SKPaint { IsAntialias = true, Color = baseline })
        {
            canvas.DrawRect(area.Left, area.Bottom - 0.6f, area.Width, 0.8f, lineBase);
        }
        if (count < 2) return;

        float max = 1024f;
        for (int i = 0; i < count; i++)
        {
            if (samples[i] > max) max = samples[i];
        }
        max *= 1.12f;

        using var path = new SKPath();
        for (int i = 0; i < count; i++)
        {
            float x = area.Left + area.Width * i / (count - 1);
            float y = area.Bottom - Math.Clamp(samples[i] / max, 0f, 1f) * area.Height;
            if (i == 0) path.MoveTo(x, y);
            else path.LineTo(x, y);
        }

        using var strokePaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.3f,
            StrokeCap = SKStrokeCap.Round,
            Color = accent,
        };
        canvas.DrawPath(path, strokePaint);
    }
}
