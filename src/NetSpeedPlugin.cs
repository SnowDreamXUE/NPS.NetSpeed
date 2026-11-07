using NotchPeninsula.Plugins;
using NPS.NetSpeed.Core;
using NPS.NetSpeed.UI;

namespace NPS.NetSpeed;

/// <summary>
/// 入口类。只做四件事：注册组件、按可调周期采样网络、维护网卡过滤、把设置读写到宿主注册表。
/// Id 用反域名风格，发布后不要改。
/// </summary>
public sealed class NetSpeedPlugin : INotchPlugin, IDisposable
{
    /// <summary>采样间隔档位显示名（与 <see cref="IntervalMsValues"/> 一一对应）。</summary>
    internal static readonly string[] IntervalLabels = { "100ms", "500ms", "1秒", "5秒", "10秒" };
    private static readonly long[] IntervalMsValues = { 100, 500, 1000, 5000, 10000 };
    internal const string AllNicLabel = "全部网卡";

    /// <summary>GetSetting 哨兵值：nic 键从未落盘时返回它，用于区分「用户显式选了全部」。</summary>
    private const string NicUnsetSentinel = "\u0000unset";

    internal readonly NetSpeedState State = new();

    private readonly NetworkSampler _sampler = new();
    private IPluginHost? _host;
    private IDisposable? _timer;
    private long _scheduledIntervalMs = -1;
    private int _intervalIndex;
    private string _nicId = "";     // 选中的网卡 Id；空串 = 全部
    private string _nicName = "";   // 选中网卡的显示名缓存（网卡拔出后仍可显示）
    // 首次运行且用户从未选择过网卡时，自动选中的 WLAN 网卡（与火绒等监控的统计口径对齐）。
    // _nicId / _nicName / _nicAutoPending 会在定时器线程（自动选择）与 UI 线程（点击切换）间
    // 交叉读写：string / bool 赋值在 .NET 中原子且竞态后果只是晚一帧显示，无需加锁。
    private bool _nicAutoPending;

    public string Id => "com.snow.netspeed";
    public string DisplayName => "网络速度";
    public string Version => typeof(NetSpeedPlugin).Assembly.GetName().Version!.ToString(3);
    public string Author => "Snow";

    /// <summary>当前采样间隔档位索引（0=100ms / 1=500ms / 2=1秒 / 3=5秒 / 4=10秒）。</summary>
    public int IntervalIndex => _intervalIndex;

    /// <summary>详情页网卡行显示名：全部 / 当前网卡名 / 已拔出提示。</summary>
    public string NicLabel
    {
        get
        {
            if (string.IsNullOrEmpty(_nicId)) return AllNicLabel;
            if (_sampler.TryGetNicName(_nicId, out string name)) return name;
            return string.IsNullOrEmpty(_nicName) ? "已拔出的网卡" : _nicName;
        }
    }

    public void Initialize(IPluginHost host)
    {
        _host = host;
        ReadSettings(host);
        _sampler.Reset();
        State.Reset();

        host.RegisterWidget(new NetSpeedWidget(this));

        // ReadSettings 里已按设置启动采样定时器；100ms 是宿主允许的最小间隔，
        // 回调在后台线程，上一次没跑完时本次直接跳过
        host.PostReminder(new ReminderData
        {
            Title = "网络速度已加载",
            Body = "左键点击组件在「并列」与「双行」之间切换，右键展开详情",
        });
    }

    /// <summary>定时器回调：采样 → 平滑写入状态。宿主按渲染帧持续重绘，无需主动请求刷新。</summary>
    private void Tick()
    {
        TryAutoSelectWlan();

        long now = Environment.TickCount64;
        if (_sampler.TrySample(now, out double downBps, out double upBps))
            State.Push(now, downBps, upBps);
        else
            State.PushIdle(now);
    }

    /// <summary>
    /// 首次运行（nic 键从未落盘）时自动选中候选网卡里的 WLAN / Wi-Fi / 无线网卡，
    /// 与火绒等安全软件默认监控的网卡对齐；没等到就一直等，用户手动切换即取消。
    /// </summary>
    private void TryAutoSelectWlan()
    {
        if (!_nicAutoPending) return;
        if (!_sampler.TryFindWlanNic(out string id, out string name)) return;

        _nicAutoPending = false;
        _nicId = id;
        _nicName = name;
        try { _host?.SetSetting("nic", id); } catch { }
        ApplySamplerConfig();
    }

    /// <summary>
    /// 从宿主注册表读回设置并应用（键会自动加 Plugin.com.snow.netspeed. 前缀）。
    /// 组件重新激活时也会调用；采样间隔与当前一致时不会重复调度定时器。
    /// </summary>
    internal void ReadSettings(IPluginHost host)
    {
        _host ??= host;

        int interval = ParseIndex(host.GetSetting("interval", "0"), 0);

        // 哨兵值区分「nic 键从未落盘」与「用户显式选了全部」：前者允许自动选 WLAN，后者尊重用户
        string rawNic = host.GetSetting("nic", NicUnsetSentinel);
        bool nicNeverSet = rawNic == NicUnsetSentinel;
        _nicId = nicNeverSet ? "" : rawNic;
        _nicAutoPending = nicNeverSet;

        State.Mode = host.GetSetting("mode", "0") == "1" ? SpeedMode.DualRow : SpeedMode.SideBySide;

        _intervalIndex = interval;
        ApplySamplerConfig();
        if (_scheduledIntervalMs != IntervalMsValues[_intervalIndex])
            RescheduleTimer();
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

    /// <summary>切换采样间隔档位：落盘 + 应用到采样器 + 重调度定时器 + 立即重绘。</summary>
    internal void SetSampleInterval(int index)
    {
        if (index < 0 || index >= IntervalMsValues.Length || index == _intervalIndex)
        {
            try { _host?.RequestRedraw(); } catch { }
            return;
        }

        _intervalIndex = index;
        try { _host?.SetSetting("interval", index.ToString()); } catch { }
        _sampler.SetInterval(IntervalMsValues[index]);
        RescheduleTimer();
        try { _host?.RequestRedraw(); } catch { }
    }

    /// <summary>循环切换统计网卡：全部 → 依次每个候选网卡 → 全部；落盘并立即重绘。</summary>
    internal void CycleNic()
    {
        _nicAutoPending = false; // 用户手动选择即取消 WLAN 自动选择
        var list = _sampler.GetNicList();
        if (list.Count == 0) return;

        int current = -1;
        for (int i = 0; i < list.Count; i++)
        {
            if (string.Equals(list[i].Id, _nicId, StringComparison.Ordinal))
            {
                current = i;
                break;
            }
        }

        if (current < 0)
        {
            (_nicId, _nicName) = (list[0].Id, list[0].Name);
        }
        else if (current + 1 < list.Count)
        {
            (_nicId, _nicName) = (list[current + 1].Id, list[current + 1].Name);
        }
        else
        {
            _nicId = "";
            _nicName = "";
        }

        try { _host?.SetSetting("nic", _nicId); } catch { }
        ApplySamplerConfig();
        try { _host?.RequestRedraw(); } catch { }
    }

    /// <summary>打开火绒流量监控：优先 NetFlow.exe 直达，回退主界面 wsctrl.exe；都找不到则提示。</summary>
    internal void OpenHuorongMonitor()
    {
        HuorongTarget? target = null;
        try { target = HuorongLauncher.Resolve(); } catch { }

        bool started = target is { } t && HuorongLauncher.TryStart(t.ExePath);
        if (!started)
        {
            try
            {
                _host?.PostReminder(new ReminderData
                {
                    Title = "无法打开火绒流量监控",
                    Body = "未检测到火绒安全软件（NetFlow.exe / wsctrl.exe），请确认火绒已安装",
                });
            }
            catch { }
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
        _host = null;
    }

    private void ApplySamplerConfig()
    {
        _sampler.SetInterval(IntervalMsValues[_intervalIndex]);
        _sampler.SetNicFilter(string.IsNullOrEmpty(_nicId) ? null : new[] { _nicId });
    }

    /// <summary>按当前档位重启采样定时器（Dispose 旧句柄 + 重新 ScheduleRefresh）。</summary>
    private void RescheduleTimer()
    {
        _timer?.Dispose();
        _timer = _host?.ScheduleRefresh(TimeSpan.FromMilliseconds(IntervalMsValues[_intervalIndex]), Tick);
        _scheduledIntervalMs = IntervalMsValues[_intervalIndex];
    }

    private static int ParseIndex(string? raw, int fallback)
    {
        return int.TryParse(raw, out int value) && value >= 0 && value < IntervalMsValues.Length
            ? value
            : fallback;
    }
}
