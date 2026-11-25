using NotchPeninsula.Plugins;

namespace NPS.NetSpeed.UI;

internal sealed class NetSpeedSettingsPage : ISettingsPage
{
    public string Title => "网络速度";

    public IReadOnlyList<SettingControl> Controls { get; } =
    [
        new ChoiceSetting(NetSpeedPlugin.ModeKey, "显示模式", ["并列", "双行"], 0),
        new ChoiceSetting(NetSpeedPlugin.IntervalKey, "采样间隔", NetSpeedPlugin.IntervalLabels,
            NetSpeedPlugin.DefaultIntervalIndex),
    ];
}
