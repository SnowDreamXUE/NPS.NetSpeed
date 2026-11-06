using System;
using NotchPeninsula.Plugins;
using NPS.NetSpeed.Core;
using NPS.NetSpeed.UI;

namespace NPS.NetSpeed;

/// <summary>
/// 入口类。只做三件事：注册组件、按 100ms 周期采样网络、把模式设置读写到宿主注册表。
/// Id 用反域名风格，发布后不要改。
/// </summary>
public sealed class NetSpeedPlugin : INotchPlugin, IDisposable
{
    internal readonly NetSpeedState State = new();

    private readonly NetworkSampler _sampler = new();
    private IPluginHost? _host;
    private IDisposable? _timer;

    public string Id => "com.nps.netspeed";
    public string DisplayName => "网络速度";
    public string Version => "1.0.0";
    public string Author => "Snow";

    public void Initialize(IPluginHost host)
    {
        _host = host;
        ReadSettings(host);
        _sampler.Reset();
        State.Reset();

        host.RegisterWidget(new NetSpeedWidget(this));

        // 100ms 是宿主允许的最小间隔；回调在后台线程，上一次没跑完时本次直接跳过
        _timer = host.ScheduleRefresh(TimeSpan.FromMilliseconds(100), Tick);

        host.PostReminder(new ReminderData
        {
            Title = "网络速度已加载",
            Body = "左键点击组件在「并列」与「双行」之间切换，右键展开详情",
        });
    }

    /// <summary>定时器回调：采样 → 平滑写入状态。宿主按渲染帧持续重绘，无需主动请求刷新。</summary>
    private void Tick()
    {
        long now = Environment.TickCount64;
        if (_sampler.TrySample(now, out double downBps, out double upBps))
            State.Push(now, downBps, upBps);
        else
            State.PushIdle(now);
    }

    /// <summary>从宿主注册表读回设置（键会自动加 Plugin.com.nps.netspeed. 前缀）。</summary>
    internal void ReadSettings(IPluginHost host)
    {
        string raw = host.GetSetting("mode", "0");
        State.Mode = raw == "1" ? SpeedMode.DualRow : SpeedMode.SideBySide;
    }

    /// <summary>切换模式：落盘 + 重测宽度（两种布局宽度不同）+ 立即重绘。</summary>
    internal void SetMode(SpeedMode mode)
    {
        if (State.Mode == mode)
        {
            try { _host?.RequestRedraw(); } catch { }
            return;
        }

        State.Mode = mode;
        try { _host?.SetSetting("mode", mode == SpeedMode.DualRow ? "1" : "0"); } catch { }
        try { _host?.InvalidateWidgetLayout(); } catch { }
        try { _host?.RequestRedraw(); } catch { }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
        _host = null;
    }
}
