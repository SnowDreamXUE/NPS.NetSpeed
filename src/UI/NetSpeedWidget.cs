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

    private readonly NetSpeedPlugin _plugin;
    private readonly NetSpeedDetailPage _detail;
    private int _downUnit;
    private int _upUnit;

    public NetSpeedWidget(NetSpeedPlugin plugin)
    {
        _plugin = plugin;
        _detail = new NetSpeedDetailPage(plugin);
    }

    public string Id => "com.nps.netspeed.widget";
    public string DisplayName => "网络速度";
    public IDetailPage? DetailPage => _detail;

    public Box? Layout(float availableHeight)
    {
        // 双行：上下两行，每行一个通道，宽度只算一份
        if (_plugin.State.Mode == SpeedMode.DualRow)
            return WidgetKit.Row(
                WidgetKit.Icon(ArrowW), WidgetKit.Gap(ArrowGap),
                WidgetKit.Icon(ValueW), WidgetKit.Gap(ValueGap), WidgetKit.Icon(UnitW));

        // 并列：两个通道左右并排，中间留一个槽间距
        return WidgetKit.Row(
            WidgetKit.Icon(ArrowW), WidgetKit.Gap(ArrowGap),
            WidgetKit.Icon(ValueW), WidgetKit.Gap(ValueGap), WidgetKit.Icon(UnitW),
            WidgetKit.Gap(ChannelGap),
            WidgetKit.Icon(ArrowW), WidgetKit.Gap(ArrowGap),
            WidgetKit.Icon(ValueW), WidgetKit.Gap(ValueGap), WidgetKit.Icon(UnitW));
    }

    public void Draw(SKCanvas canvas, SKRect rect, WidgetFrame frame)
    {
        if (frame.Alpha == 0) return;

        var content = WidgetKit.ContentRect(rect);
        if (content.Width < 40f || content.Height < 10f) return;

        var snapshot = _plugin.State.Read();
        SpeedFormat.Format(snapshot.DownBps, ref _downUnit, out string downNumber, out string downUnitName);
        SpeedFormat.Format(snapshot.UpBps, ref _upUnit, out string upNumber, out string upUnitName);

        byte alpha = frame.Alpha;
        var textColor = frame.Theme.TextColor.WithAlpha(alpha);
        var subColor = frame.Theme.SubTextColor.WithAlpha((byte)(alpha * 0.85f));
        var downAccent = SpeedPaint.Accent(frame.Theme.TextColor, SpeedPaint.DownHue, alpha);
        var upAccent = SpeedPaint.Accent(frame.Theme.TextColor, SpeedPaint.UpHue, alpha);
        float offsetY = frame.TextOffsetY;

        if (_plugin.State.Mode == SpeedMode.DualRow)
        {
            float half = content.Height * 0.5f;
            DrawChannel(canvas, content.Left, content.Top + half * 0.5f + offsetY,
                downNumber, downUnitName, downAccent, textColor, subColor, false);
            DrawChannel(canvas, content.Left, content.Top + half * 1.5f + offsetY,
                upNumber, upUnitName, upAccent, textColor, subColor, true);
        }
        else
        {
            float centerY = content.MidY + offsetY;
            DrawChannel(canvas, content.Left, centerY,
                downNumber, downUnitName, downAccent, textColor, subColor, false);
            DrawChannel(canvas, content.Left + ChannelW + ChannelGap, centerY,
                upNumber, upUnitName, upAccent, textColor, subColor, true);
        }
    }

    /// <summary>一个通道：箭头（强调色）+ 右对齐数值（主文字色）+ 单位（次要文字色）。</summary>
    private static void DrawChannel(SKCanvas canvas, float x, float centerY,
        string number, string unit, SKColor accent, SKColor textColor, SKColor subColor, bool up)
    {
        SpeedPaint.DrawArrow(canvas, x + ArrowW * 0.5f, centerY, up, accent);

        float valueRight = x + ArrowW + ArrowGap + ValueW;
        DrawText(canvas, number, valueRight, centerY + BaselineOffset, ValueSize,
            textColor, SKTextAlign.Right, TextWeight.SemiBold);
        DrawText(canvas, unit, valueRight + ValueGap, centerY + BaselineOffset, UnitSize,
            subColor, SKTextAlign.Left, TextWeight.Normal);
    }

    private static void DrawText(SKCanvas canvas, string text, float x, float baseline,
        float size, SKColor color, SKTextAlign align, TextWeight weight)
    {
        using var paint = new SKPaint
        {
            IsAntialias = true,
            TextSize = size,
            Color = color,
            TextAlign = align,
            Typeface = WidgetKit.Typeface(weight),
        };
        canvas.DrawText(text, x, baseline, paint);
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

    public void OnActivate(IPluginHost host)
    {
        _plugin.ReadSettings(host);
    }

    public void OnDeactivate() { }
}
