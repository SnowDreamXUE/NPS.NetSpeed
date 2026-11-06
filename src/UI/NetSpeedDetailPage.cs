using System;
using NotchPeninsula.Plugins;
using NPS.NetSpeed.Core;
using NPS.NetSpeed.Rendering;
using SkiaSharp;

namespace NPS.NetSpeed.UI;

/// <summary>
/// 右键展开的详情页：大号数值 + 6 秒曲线 + 模式切换。
/// 排版永远按 <see cref="MeasureWidth"/> / <see cref="MeasureHeight"/> 的目标尺寸算，
/// 再把整块内容缩放平移进当前 rect（宿主展开动画期间 rect 是插值尺寸）。
/// </summary>
internal sealed class NetSpeedDetailPage : IDetailPage
{
    private const float LayoutW = 360f;
    private const float LayoutH = 208f;

    private const float PaddingX = 18f;

    private const float ChipY = 14f;
    private const float ChipH = 26f;
    private static readonly SKRect SideChip = new(234f, ChipY, 290f, ChipY + ChipH);
    private static readonly SKRect RowChip = new(296f, ChipY, 352f, ChipY + ChipH);

    private readonly NetSpeedPlugin _plugin;
    private readonly float[] _downSamples = new float[NetSpeedState.HistoryCapacity];
    private readonly float[] _upSamples = new float[NetSpeedState.HistoryCapacity];
    private int _downUnit;
    private int _upUnit;

    public NetSpeedDetailPage(NetSpeedPlugin plugin) => _plugin = plugin;

    public float MeasureWidth() => LayoutW;
    public float MeasureHeight() => LayoutH;

    public void Draw(SKCanvas canvas, SKRect rect, WidgetFrame frame)
    {
        if (frame.Alpha == 0) return;
        if (rect.Width < 1f || rect.Height < 1f) return;

        float scale = Math.Min(Math.Min(rect.Width / LayoutW, rect.Height / LayoutH), 1f);
        if (!(scale > 0f)) return;

        canvas.Save();
        canvas.Translate(rect.MidX, rect.MidY);
        canvas.Scale(scale);
        canvas.Translate(-LayoutW * 0.5f, -LayoutH * 0.5f);
        DrawContent(canvas, frame);
        canvas.Restore();
    }

    private void DrawContent(SKCanvas canvas, WidgetFrame frame)
    {
        byte alpha = frame.Alpha;
        var text = frame.Theme.TextColor.WithAlpha(alpha);
        var sub = frame.Theme.SubTextColor.WithAlpha((byte)(alpha * 0.86f));
        var faint = frame.Theme.TextColor.WithAlpha((byte)(alpha * 0.08f));
        var divider = frame.Theme.SubTextColor.WithAlpha((byte)(alpha * 0.16f));
        var downAccent = SpeedPaint.Accent(frame.Theme.TextColor, SpeedPaint.DownHue, alpha);
        var upAccent = SpeedPaint.Accent(frame.Theme.TextColor, SpeedPaint.UpHue, alpha);

        int count = _plugin.State.CopyHistory(_downSamples, _upSamples);
        var snapshot = _plugin.State.Read();

        using (var title = MakePaint(15f, text, true))
        {
            canvas.DrawText("网络速度", PaddingX, 30f, title);
        }
        using (var caption = MakePaint(9.2f, sub, false))
        {
            canvas.DrawText("实时 · 100ms 采样 · 1s 滑动窗口", PaddingX, 47f, caption);
        }

        var mode = _plugin.State.Mode;
        DrawChip(canvas, SideChip, "并列", mode == SpeedMode.SideBySide, downAccent, faint, sub, alpha);
        DrawChip(canvas, RowChip, "双行", mode == SpeedMode.DualRow, downAccent, faint, sub, alpha);

        using (var rule = new SKPaint { IsAntialias = true, Color = divider })
        {
            canvas.DrawRect(PaddingX, 56f, LayoutW - PaddingX * 2f, 0.8f, rule);
        }

        DrawMetric(canvas, 66f, true, snapshot.DownBps, downAccent, text, sub, frame, count);
        DrawMetric(canvas, 122f, false, snapshot.UpBps, upAccent, text, sub, frame, count);

        using (var hint = MakePaint(9f, sub, false))
        {
            canvas.DrawText("左键点击岛上组件可快速切换 · 右键展开 / 收起", PaddingX, 200f, hint);
        }
    }

    private void DrawMetric(SKCanvas canvas, float top, bool down, float bps, SKColor accent,
        SKColor text, SKColor sub, WidgetFrame frame, int count)
    {
        int unitIndex = down ? _downUnit : _upUnit;
        SpeedFormat.Format(bps, ref unitIndex, out string number, out string unit);
        if (down) _downUnit = unitIndex;
        else _upUnit = unitIndex;

        using (var label = MakePaint(10.5f, accent, true))
        {
            canvas.DrawText(down ? "↓ 下行" : "↑ 上行", PaddingX, top + 16f, label);
        }

        using (var numberPaint = MakePaint(22f, text, true))
        {
            float baseline = top + 47f;
            canvas.DrawText(number, PaddingX, baseline, numberPaint);
            float numberWidth = numberPaint.MeasureText(number);

            using var unitPaint = MakePaint(10.5f, sub, false);
            canvas.DrawText(unit, PaddingX + numberWidth + 5f, baseline - 1.5f, unitPaint);
        }

        var samples = down ? _downSamples : _upSamples;
        var area = new SKRect(152f, top + 4f, LayoutW - PaddingX, top + 50f);
        SpeedPaint.DrawSparkline(canvas, area, samples, count, accent,
            frame.Theme.SubTextColor.WithAlpha((byte)(frame.Alpha * 0.18f)));
    }

    private static void DrawChip(SKCanvas canvas, SKRect rect, string label, bool active,
        SKColor accent, SKColor inactiveBackground, SKColor subText, byte alpha)
    {
        var background = active ? accent : inactiveBackground;
        using (var fill = new SKPaint { IsAntialias = true, Color = background })
        {
            canvas.DrawRoundRect(rect, 9f, 9f, fill);
        }

        var labelColor = active
            ? SpeedPaint.OnAccent(background).WithAlpha(alpha)
            : subText;
        using var textPaint = MakePaint(10.5f, labelColor, active);
        textPaint.TextAlign = SKTextAlign.Center;
        canvas.DrawText(label, rect.MidX, rect.MidY + 3.6f, textPaint);
    }

    /// <summary>详情页可能显示中文，优先借宿主正文字体 / 兜底字体，拿不到就交给 Skia 默认字体。</summary>
    private static SKPaint MakePaint(float size, SKColor color, bool bold)
    {
        var paint = new SKPaint
        {
            IsAntialias = true,
            TextSize = size,
            Color = color,
            Typeface = bold
                ? (PluginFont.Bold() ?? WidgetKit.Typeface(TextWeight.Bold))
                : (PluginFont.Normal() ?? WidgetKit.Typeface()),
        };
        return paint;
    }

    public WidgetHit HitTest(float x, float y, SKRect rect)
    {
        float scale = Math.Min(Math.Min(rect.Width / LayoutW, rect.Height / LayoutH), 1f);
        if (!(scale > 0f)) return WidgetHit.None;

        float lx = (x - rect.Width * 0.5f) / scale + LayoutW * 0.5f;
        float ly = (y - rect.Height * 0.5f) / scale + LayoutH * 0.5f;

        if (HitChip(SideChip, lx, ly)) return new WidgetHit("mode:0");
        if (HitChip(RowChip, lx, ly)) return new WidgetHit("mode:1");
        return WidgetHit.None;
    }

    private static bool HitChip(SKRect chip, float x, float y)
        => x >= chip.Left && x <= chip.Right && y >= chip.Top && y <= chip.Bottom;

    public void OnAction(string? action, float x, float y)
    {
        if (action == "mode:0") _plugin.SetMode(SpeedMode.SideBySide);
        else if (action == "mode:1") _plugin.SetMode(SpeedMode.DualRow);
    }
}
