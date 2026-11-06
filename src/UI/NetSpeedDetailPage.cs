using System;
using NotchPeninsula.Plugins;
using NPS.NetSpeed.Core;
using NPS.NetSpeed.Rendering;
using SkiaSharp;

namespace NPS.NetSpeed.UI;

/// <summary>
/// 右键展开的详情页：采样间隔 / 模式 / 网卡选择 + 大号数值 + 6 秒曲线。
/// 排版永远按 <see cref="MeasureWidth"/> / <see cref="MeasureHeight"/> 的目标尺寸算，
/// 再把整块内容缩放平移进当前 rect（宿主展开动画期间 rect 是插值尺寸）。
/// </summary>
internal sealed class NetSpeedDetailPage : IDetailPage
{
    private const float LayoutW = 360f;
    private const float LayoutH = 262f;

    private const float PaddingX = 18f;

    // 芯片行在标题下方独占一行（y40），避免与左上角标题「网络速度」重叠
    private const float ChipY = 40f;
    private const float ChipH = 26f;
    private const float IntervalChipW = 40f;
    private const float IntervalChipGap = 3f;

    // 采样间隔 5 档芯片：100ms / 500ms / 1秒 / 5秒 / 10秒，排在模式芯片左侧
    private static readonly SKRect[] IntervalChips = BuildIntervalChips();

    private static SKRect[] BuildIntervalChips()
    {
        var rects = new SKRect[NetSpeedPlugin.IntervalLabels.Length];
        for (int i = 0; i < rects.Length; i++)
        {
            float left = PaddingX + i * (IntervalChipW + IntervalChipGap);
            rects[i] = new SKRect(left, ChipY, left + IntervalChipW, ChipY + ChipH);
        }
        return rects;
    }

    private static readonly SKRect SideChip = new(244f, ChipY, 296f, ChipY + ChipH);
    private static readonly SKRect RowChip = new(300f, ChipY, 352f, ChipY + ChipH);

    // 网卡选择行：显示当前统计网卡，点击循环切换「全部 → 各候选网卡 → 全部」
    private static readonly SKRect NicRow = new(PaddingX, 74f, LayoutW - PaddingX, 98f);
    private const float NicValueX = 52f;        // 网卡名起始 x
    private const float NicValueMaxRight = 270f; // 网卡名可用宽度右界（右侧留给「点击切换」提示）

    // 火绒流量监控行：点击拉起火绒 NetFlow.exe（回退主界面 wsctrl.exe）
    private static readonly SKRect FlowRow = new(PaddingX, 104f, LayoutW - PaddingX, 128f);

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
            caption.TextAlign = SKTextAlign.Right;
            canvas.DrawText($"实时 · {NetSpeedPlugin.IntervalLabels[_plugin.IntervalIndex]} 采样", LayoutW - PaddingX, 30f, caption);
        }

        var mode = _plugin.State.Mode;
        var labels = NetSpeedPlugin.IntervalLabels;
        for (int i = 0; i < labels.Length; i++)
            DrawChip(canvas, IntervalChips[i], labels[i], _plugin.IntervalIndex == i, downAccent, faint, sub, alpha);
        DrawChip(canvas, SideChip, "并列", mode == SpeedMode.SideBySide, downAccent, faint, sub, alpha);
        DrawChip(canvas, RowChip, "双行", mode == SpeedMode.DualRow, downAccent, faint, sub, alpha);

        DrawNicRow(canvas, text, sub, faint, alpha);
        DrawFlowRow(canvas, text, sub, faint);

        using (var rule = new SKPaint { IsAntialias = true, Color = divider })
        {
            canvas.DrawRect(PaddingX, 136f, LayoutW - PaddingX * 2f, 0.8f, rule);
        }

        DrawMetric(canvas, 144f, true, snapshot.DownBps, downAccent, text, sub, frame, count);
        DrawMetric(canvas, 196f, false, snapshot.UpBps, upAccent, text, sub, frame, count);

        using (var hint = MakePaint(9f, sub, false))
        {
            canvas.DrawText("左键点击岛上组件可快速切换 · 右键展开 / 收起", PaddingX, 252f, hint);
        }
    }

    /// <summary>网卡选择行：浅色圆角条，左侧固定「网卡」，中间当前网卡名（超宽截断），右侧切换提示。</summary>
    private void DrawNicRow(SKCanvas canvas, SKColor text, SKColor sub, SKColor faint, byte alpha)
    {
        using (var fill = new SKPaint { IsAntialias = true, Color = faint })
        {
            canvas.DrawRoundRect(NicRow, 8f, 8f, fill);
        }

        using (var label = MakePaint(10.5f, sub, false))
        {
            canvas.DrawText("网卡", NicRow.Left + 10f, NicRow.MidY + 3.6f, label);
        }

        using (var value = MakePaint(10.5f, text, true))
        {
            canvas.Save();
            canvas.ClipRect(SKRect.Create(NicValueX, NicRow.Top, NicValueMaxRight - NicValueX, NicRow.Height));
            canvas.DrawText(_plugin.NicLabel, NicValueX, NicRow.MidY + 3.6f, value);
            canvas.Restore();
        }

        using (var tip = MakePaint(9f, sub, false))
        {
            tip.TextAlign = SKTextAlign.Right;
            canvas.DrawText("点击切换 ▸", NicRow.Right - 10f, NicRow.MidY + 3.8f, tip);
        }
    }

    /// <summary>火绒流量监控行：浅色圆角条，左侧固定标签，右侧「点击打开 ▸」提示。</summary>
    private static void DrawFlowRow(SKCanvas canvas, SKColor text, SKColor sub, SKColor faint)
    {
        using (var fill = new SKPaint { IsAntialias = true, Color = faint })
        {
            canvas.DrawRoundRect(FlowRow, 8f, 8f, fill);
        }

        using (var label = MakePaint(10.5f, sub, false))
        {
            canvas.DrawText("火绒", FlowRow.Left + 10f, FlowRow.MidY + 3.6f, label);
        }

        using (var value = MakePaint(10.5f, text, true))
        {
            canvas.DrawText("打开流量监控", FlowRow.Left + 52f, FlowRow.MidY + 3.6f, value);
        }

        using (var tip = MakePaint(9f, sub, false))
        {
            tip.TextAlign = SKTextAlign.Right;
            canvas.DrawText("点击打开 ▸", FlowRow.Right - 10f, FlowRow.MidY + 3.8f, tip);
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
            canvas.DrawText(down ? "↓ 下行" : "↑ 上行", PaddingX, top + 14f, label);
        }

        using (var numberPaint = MakePaint(22f, text, true))
        {
            float baseline = top + 45f;
            canvas.DrawText(number, PaddingX, baseline, numberPaint);
            float numberWidth = numberPaint.MeasureText(number);

            using var unitPaint = MakePaint(10.5f, sub, false);
            canvas.DrawText(unit, PaddingX + numberWidth + 5f, baseline - 1.5f, unitPaint);
        }

        var samples = down ? _downSamples : _upSamples;
        var area = new SKRect(152f, top + 4f, LayoutW - PaddingX, top + 48f);
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

        for (int i = 0; i < IntervalChips.Length; i++)
        {
            if (HitChip(IntervalChips[i], lx, ly)) return new WidgetHit($"interval:{i}");
        }
        if (HitChip(SideChip, lx, ly)) return new WidgetHit("mode:0");
        if (HitChip(RowChip, lx, ly)) return new WidgetHit("mode:1");
        if (HitChip(NicRow, lx, ly)) return new WidgetHit("nic:next");
        if (HitChip(FlowRow, lx, ly)) return new WidgetHit("open:netflow");
        return WidgetHit.None;
    }

    private static bool HitChip(SKRect chip, float x, float y)
        => x >= chip.Left && x <= chip.Right && y >= chip.Top && y <= chip.Bottom;

    public void OnAction(string? action, float x, float y)
    {
        if (action is not null && action.StartsWith("interval:", StringComparison.Ordinal))
        {
            if (int.TryParse(action.AsSpan("interval:".Length), out int index))
                _plugin.SetSampleInterval(index);
            return;
        }
        if (action == "nic:next")
        {
            _plugin.CycleNic();
            return;
        }
        if (action == "open:netflow")
        {
            _plugin.OpenHuorongMonitor();
            return;
        }
        if (action == "mode:0") _plugin.SetMode(SpeedMode.SideBySide);
        else if (action == "mode:1") _plugin.SetMode(SpeedMode.DualRow);
    }
}
