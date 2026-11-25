using System;
using NotchPeninsula.Plugins;
using NPS.NetSpeed.Core;
using NPS.NetSpeed.Rendering;
using SkiaSharp;

namespace NPS.NetSpeed.UI;

/// <summary>
/// 右键展开的详情页：采样间隔 / 模式 / 网卡选择 + 大号数值 + 最近 60 个样本的曲线。
/// 排版永远按 <see cref="MeasureWidth"/> / <see cref="MeasureHeight"/> 的目标尺寸算，
/// 再把整块内容缩放平移进当前 rect（宿主展开动画期间 rect 是插值尺寸）。
/// </summary>
internal sealed class NetSpeedDetailPage : IDetailPage, IDisposable
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

    private const float DownTop = 144f;
    private const float UpTop = 196f;
    private static readonly SKRect DownChart = ChartArea(DownTop);
    private static readonly SKRect UpChart = ChartArea(UpTop);
    private static readonly string[] IntervalCaptions = Array.ConvertAll(
        NetSpeedPlugin.IntervalLabels, label => $"实时 · {label} 采样");

    private readonly NetSpeedPlugin _plugin;
    private readonly object _drawGate = new();
    private DrawingResources? _drawing;
    private bool _disposed;

    public NetSpeedDetailPage(NetSpeedPlugin plugin) => _plugin = plugin;

    public float MeasureWidth() => LayoutW;
    public float MeasureHeight() => LayoutH;

    public void Draw(SKCanvas canvas, SKRect rect, WidgetFrame frame)
    {
        if (frame.Alpha == 0 || rect.Width < 1f || rect.Height < 1f) return;

        float scale = Math.Min(Math.Min(rect.Width / LayoutW, rect.Height / LayoutH), 1f);
        if (!(scale > 0f)) return;

        lock (_drawGate)
        {
            if (_disposed) return;
            var drawing = _drawing ??= new DrawingResources();
            int saveCount = canvas.Save();
            try
            {
                drawing.Normal.Typeface = PluginFont.Normal() ?? WidgetKit.Typeface();
                drawing.Bold.Typeface = PluginFont.Bold() ?? WidgetKit.Typeface(TextWeight.Bold);
                canvas.Translate(rect.MidX, rect.MidY);
                canvas.Scale(scale);
                canvas.Translate(-LayoutW * 0.5f, -LayoutH * 0.5f);
                DrawContent(canvas, frame, drawing);
            }
            finally
            {
                drawing.Normal.Typeface = null;
                drawing.Bold.Typeface = null;
                canvas.RestoreToCount(saveCount);
            }
        }
    }

    private void DrawContent(SKCanvas canvas, WidgetFrame frame, DrawingResources drawing)
    {
        byte alpha = frame.Alpha;
        var text = frame.Theme.TextColor.WithAlpha(alpha);
        var sub = frame.Theme.SubTextColor.WithAlpha((byte)(alpha * 0.86f));
        var faint = frame.Theme.TextColor.WithAlpha((byte)(alpha * 0.08f));
        var divider = frame.Theme.SubTextColor.WithAlpha((byte)(alpha * 0.16f));
        var chartBaseline = frame.Theme.SubTextColor.WithAlpha((byte)(alpha * 0.18f));
        var downAccent = SpeedPaint.Accent(frame.Theme.TextColor, SpeedPaint.DownHue, alpha);
        var upAccent = SpeedPaint.Accent(frame.Theme.TextColor, SpeedPaint.UpHue, alpha);

        if (_plugin.State.ReadHistory(ref drawing.HistoryVersion, drawing.DownSamples, drawing.UpSamples,
            out int count, out var snapshot))
        {
            SpeedPaint.UpdateSparkline(drawing.DownPath, DownChart, drawing.DownSamples, count);
            SpeedPaint.UpdateSparkline(drawing.UpPath, UpChart, drawing.UpSamples, count);
        }

        int interval = _plugin.IntervalIndex;
        canvas.DrawText("网络速度", PaddingX, 30f, TextPaint(drawing.Bold, 15f, text));
        canvas.DrawText(IntervalCaptions[interval], LayoutW - PaddingX, 30f,
            TextPaint(drawing.Normal, 9.2f, sub, SKTextAlign.Right));

        var mode = _plugin.State.Mode;
        var labels = NetSpeedPlugin.IntervalLabels;
        for (int i = 0; i < labels.Length; i++)
            DrawChip(canvas, drawing, IntervalChips[i], labels[i], interval == i, downAccent, faint, sub, alpha);
        DrawChip(canvas, drawing, SideChip, "并列", mode == SpeedMode.SideBySide, downAccent, faint, sub, alpha);
        DrawChip(canvas, drawing, RowChip, "双行", mode == SpeedMode.DualRow, downAccent, faint, sub, alpha);

        DrawNicRow(canvas, drawing, text, sub, faint);
        DrawFlowRow(canvas, drawing, text, sub, faint);

        drawing.Fill.Color = divider;
        canvas.DrawRect(PaddingX, 136f, LayoutW - PaddingX * 2f, 0.8f, drawing.Fill);

        DrawMetric(canvas, drawing, DownTop, "↓ 下行", drawing.DownText.Format(snapshot.DownBps), downAccent, text, sub);
        DrawSparkline(canvas, drawing, DownChart, drawing.DownPath, downAccent, chartBaseline);
        DrawMetric(canvas, drawing, UpTop, "↑ 上行", drawing.UpText.Format(snapshot.UpBps), upAccent, text, sub);
        DrawSparkline(canvas, drawing, UpChart, drawing.UpPath, upAccent, chartBaseline);

        canvas.DrawText("左键点击岛上组件可快速切换 · 右键展开 / 收起", PaddingX, 252f,
            TextPaint(drawing.Normal, 9f, sub));
    }

    /// <summary>网卡选择行：浅色圆角条，左侧固定「网卡」，中间当前网卡名（超宽截断），右侧切换提示。</summary>
    private void DrawNicRow(SKCanvas canvas, DrawingResources drawing, SKColor text, SKColor sub, SKColor faint)
    {
        drawing.Fill.Color = faint;
        canvas.DrawRoundRect(NicRow, 8f, 8f, drawing.Fill);
        canvas.DrawText("网卡", NicRow.Left + 10f, NicRow.MidY + 3.6f, TextPaint(drawing.Normal, 10.5f, sub));

        canvas.Save();
        canvas.ClipRect(SKRect.Create(NicValueX, NicRow.Top, NicValueMaxRight - NicValueX, NicRow.Height));
        canvas.DrawText(_plugin.NicLabel, NicValueX, NicRow.MidY + 3.6f, TextPaint(drawing.Bold, 10.5f, text));
        canvas.Restore();

        canvas.DrawText("点击切换 ▸", NicRow.Right - 10f, NicRow.MidY + 3.8f,
            TextPaint(drawing.Normal, 9f, sub, SKTextAlign.Right));
    }

    private static void DrawFlowRow(SKCanvas canvas, DrawingResources drawing, SKColor text, SKColor sub, SKColor faint)
    {
        drawing.Fill.Color = faint;
        canvas.DrawRoundRect(FlowRow, 8f, 8f, drawing.Fill);
        canvas.DrawText("火绒", FlowRow.Left + 10f, FlowRow.MidY + 3.6f, TextPaint(drawing.Normal, 10.5f, sub));
        canvas.DrawText("打开流量监控", FlowRow.Left + 52f, FlowRow.MidY + 3.6f, TextPaint(drawing.Bold, 10.5f, text));
        canvas.DrawText("点击打开 ▸", FlowRow.Right - 10f, FlowRow.MidY + 3.8f,
            TextPaint(drawing.Normal, 9f, sub, SKTextAlign.Right));
    }

    private static void DrawMetric(SKCanvas canvas, DrawingResources drawing, float top, string label,
        (string Number, string Unit) reading, SKColor accent, SKColor text, SKColor sub)
    {
        canvas.DrawText(label, PaddingX, top + 14f, TextPaint(drawing.Bold, 10.5f, accent));

        float baseline = top + 45f;
        var numberPaint = TextPaint(drawing.Bold, 22f, text);
        canvas.DrawText(reading.Number, PaddingX, baseline, numberPaint);
        float numberWidth = numberPaint.MeasureText(reading.Number);
        canvas.DrawText(reading.Unit, PaddingX + numberWidth + 5f, baseline - 1.5f,
            TextPaint(drawing.Normal, 10.5f, sub));
    }

    private static SKRect ChartArea(float top) => new(152f, top + 4f, LayoutW - PaddingX, top + 48f);

    private static void DrawSparkline(SKCanvas canvas, DrawingResources drawing, SKRect area,
        SKPath path, SKColor accent, SKColor baseline)
    {
        drawing.Fill.Color = baseline;
        canvas.DrawRect(area.Left, area.Bottom - 0.6f, area.Width, 0.8f, drawing.Fill);
        if (path.IsEmpty) return;
        drawing.Stroke.Color = accent;
        canvas.DrawPath(path, drawing.Stroke);
    }

    private static void DrawChip(SKCanvas canvas, DrawingResources drawing, SKRect rect, string label, bool active,
        SKColor accent, SKColor inactiveBackground, SKColor subText, byte alpha)
    {
        var background = active ? accent : inactiveBackground;
        drawing.Fill.Color = background;
        canvas.DrawRoundRect(rect, 9f, 9f, drawing.Fill);

        var labelColor = active ? SpeedPaint.OnAccent(background).WithAlpha(alpha) : subText;
        var paint = TextPaint(active ? drawing.Bold : drawing.Normal, 10.5f, labelColor, SKTextAlign.Center);
        canvas.DrawText(label, rect.MidX, rect.MidY + 3.6f, paint);
    }

    private static SKPaint TextPaint(SKPaint paint, float size, SKColor color, SKTextAlign align = SKTextAlign.Left)
    {
        paint.TextSize = size;
        paint.Color = color;
        paint.TextAlign = align;
        return paint;
    }

    public void Dispose()
    {
        lock (_drawGate)
        {
            _disposed = true;
            _drawing?.Dispose();
            _drawing = null;
        }
    }

    private sealed class DrawingResources : IDisposable
    {
        internal readonly SKPaint Normal = new() { IsAntialias = true };
        internal readonly SKPaint Bold = new() { IsAntialias = true };
        internal readonly SKPaint Fill = new() { IsAntialias = true };
        internal readonly SKPaint Stroke = new()
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.3f,
            StrokeCap = SKStrokeCap.Round,
        };
        internal readonly SKPath DownPath = new();
        internal readonly SKPath UpPath = new();
        internal readonly float[] DownSamples = new float[NetSpeedState.HistoryCapacity];
        internal readonly float[] UpSamples = new float[NetSpeedState.HistoryCapacity];
        internal readonly SpeedText DownText = new(SpeedFormat.LowestUnitIndex);
        internal readonly SpeedText UpText = new(SpeedFormat.LowestUnitIndex);
        internal long HistoryVersion;

        public void Dispose()
        {
            Normal.Dispose();
            Bold.Dispose();
            Fill.Dispose();
            Stroke.Dispose();
            DownPath.Dispose();
            UpPath.Dispose();
        }
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
