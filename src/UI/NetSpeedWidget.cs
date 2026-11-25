using NotchPeninsula.Plugins;
using NPS.NetSpeed.Core;
using NPS.NetSpeed.Rendering;
using SkiaSharp;

namespace NPS.NetSpeed.UI;

/// <summary>
/// 岛上的收起态组件：数值优先，只画「箭头 + 数字 + 单位」，不含任何条形 / 进度图形。
/// 两种布局都由固定宽度的 Box 拼成，数值变化不需要重测宽度，只有切换模式时宽度才变。
/// 左键单击 = 切换并列 / 双行，右键保持宿主默认行为（展开详情页）。
/// </summary>
internal sealed class NetSpeedWidget : IAutoWidthWidget
{
    // 单个通道：箭头 / 数值 / 单位。两种模式共用同一组宽度，数值右对齐不会顶到单位。
    private const float ArrowW = 7f;
    private const float ArrowGap = 2f;
    private const float ValueW = 25f;
    private const float ValueGap = 1f;
    private const float UnitW = 23f;
    private const float ChannelW = ArrowW + ArrowGap + ValueW + ValueGap + UnitW;

    private const float ChannelGap = 10f;

    private const float ValueSize = 10f;
    private const float UnitSize = 8f;
    private const float BaselineOffset = 3.4f;
    private static readonly Box SideBySideLayout = WidgetKit.Icon(ChannelW * 2f + ChannelGap);
    private static readonly Box DualRowLayout = WidgetKit.Icon(ChannelW);

    private readonly NetSpeedPlugin _plugin;
    private readonly NetSpeedDetailPage _detail;
    private readonly object _drawGate = new();
    private readonly SpeedText _downText = new(SpeedFormat.MinUnitIndex);
    private readonly SpeedText _upText = new(SpeedFormat.MinUnitIndex);
    private SKPaint? _arrowPaint;
    private SKPaint? _valuePaint;
    private SKPaint? _unitPaint;
    private bool _deactivated;

    public NetSpeedWidget(NetSpeedPlugin plugin)
    {
        _plugin = plugin;
        _detail = new NetSpeedDetailPage(plugin);
    }

    public string Id => "com.snow.netspeed.widget";
    public string DisplayName => "网络速度";
    public IDetailPage? DetailPage => _detail;

    public Box? Layout(float availableHeight)
        => _plugin.State.Mode == SpeedMode.DualRow ? DualRowLayout : SideBySideLayout;

    public void Draw(SKCanvas canvas, SKRect rect, WidgetFrame frame)
    {
        if (frame.Alpha == 0) return;

        var content = WidgetKit.ContentRect(rect);
        if (content.Width < 40f || content.Height < 10f) return;

        lock (_drawGate)
        {
            if (_deactivated) return;
            var arrowPaint = _arrowPaint ??= SpeedPaint.CreateArrowPaint();
            var valuePaint = _valuePaint ??= new SKPaint
            {
                IsAntialias = true, TextSize = ValueSize, TextAlign = SKTextAlign.Right,
            };
            var unitPaint = _unitPaint ??= new SKPaint { IsAntialias = true, TextSize = UnitSize };
            try
            {
                valuePaint.Typeface = WidgetKit.Typeface(TextWeight.SemiBold);
                unitPaint.Typeface = WidgetKit.Typeface();
                DrawContent(canvas, content, frame, arrowPaint, valuePaint, unitPaint);
            }
            finally
            {
                valuePaint.Typeface = null;
                unitPaint.Typeface = null;
            }
        }
    }

    private void DrawContent(SKCanvas canvas, SKRect content, WidgetFrame frame,
        SKPaint arrowPaint, SKPaint valuePaint, SKPaint unitPaint)
    {
        var snapshot = _plugin.State.Read();
        var (downNumber, downUnit) = _downText.Format(snapshot.DownBps);
        var (upNumber, upUnit) = _upText.Format(snapshot.UpBps);

        byte alpha = frame.Alpha;
        valuePaint.Color = frame.Theme.TextColor.WithAlpha(alpha);
        unitPaint.Color = frame.Theme.SubTextColor.WithAlpha((byte)(alpha * 0.85f));
        var downAccent = SpeedPaint.Accent(frame.Theme.TextColor, SpeedPaint.DownHue, alpha);
        var upAccent = SpeedPaint.Accent(frame.Theme.TextColor, SpeedPaint.UpHue, alpha);
        float offsetY = frame.TextOffsetY;

        if (_plugin.State.Mode == SpeedMode.DualRow)
        {
            float half = content.Height * 0.5f;
            DrawChannel(canvas, content.Left, content.Top + half * 0.5f + offsetY,
                downNumber, downUnit, downAccent, false, arrowPaint, valuePaint, unitPaint);
            DrawChannel(canvas, content.Left, content.Top + half * 1.5f + offsetY,
                upNumber, upUnit, upAccent, true, arrowPaint, valuePaint, unitPaint);
        }
        else
        {
            float centerY = content.MidY + offsetY;
            DrawChannel(canvas, content.Left, centerY,
                downNumber, downUnit, downAccent, false, arrowPaint, valuePaint, unitPaint);
            DrawChannel(canvas, content.Left + ChannelW + ChannelGap, centerY,
                upNumber, upUnit, upAccent, true, arrowPaint, valuePaint, unitPaint);
        }
    }

    /// <summary>一个通道：箭头（强调色）+ 右对齐数值（主文字色）+ 单位（次要文字色）。</summary>
    private static void DrawChannel(SKCanvas canvas, float x, float centerY,
        string number, string unit, SKColor accent, bool up,
        SKPaint arrowPaint, SKPaint valuePaint, SKPaint unitPaint)
    {
        arrowPaint.Color = accent;
        SpeedPaint.DrawArrow(canvas, x + ArrowW * 0.5f, centerY, up, arrowPaint);

        float valueRight = x + ArrowW + ArrowGap + ValueW;
        canvas.DrawText(number, valueRight, centerY + BaselineOffset, valuePaint);
        canvas.DrawText(unit, valueRight + ValueGap, centerY + BaselineOffset, unitPaint);
    }

    public WidgetHit HitTest(float x, float y, SKRect rect)
        => x >= 0 && x <= rect.Width && y >= 0 && y <= rect.Height
            ? new WidgetHit("toggle-mode")
            : WidgetHit.None;

    public void OnLeftClick(string? action, float x, float y)
    {
        if (action != "toggle-mode") return;
        var next = _plugin.State.Mode == SpeedMode.SideBySide ? SpeedMode.DualRow : SpeedMode.SideBySide;
        _plugin.SetMode(next);
    }

    public void OnRightClick() { }

    public void OnActivate(IPluginHost host) => _plugin.ReloadSettings();

    public void OnDeactivate()
    {
        lock (_drawGate)
        {
            _deactivated = true;
            _arrowPaint?.Dispose();
            _valuePaint?.Dispose();
            _unitPaint?.Dispose();
            _arrowPaint = _valuePaint = _unitPaint = null;
        }
        _detail.Dispose();
    }
}
