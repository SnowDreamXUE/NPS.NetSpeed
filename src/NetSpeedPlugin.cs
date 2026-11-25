using NotchPeninsula.Plugins;
using NPS.NetSpeed.Core;
using NPS.NetSpeed.UI;

namespace NPS.NetSpeed;

/// <summary>
/// 注册组件和设置页，协调采样与宿主设置。
/// </summary>
public sealed class NetSpeedPlugin : INotchPlugin, IDisposable
{
    /// <summary>采样间隔档位显示名（与 <see cref="IntervalMsValues"/> 一一对应）。</summary>
    internal static readonly string[] IntervalLabels = { "100ms", "500ms", "1秒", "5秒", "10秒" };
    internal const int DefaultIntervalIndex = 2;
    internal const string ModeKey = "mode";
    internal const string IntervalKey = "interval";
    private static readonly long[] IntervalMsValues = { 100, 500, 1000, 5000, 10000 };
    private const string NicKey = "nic";
    private const string AllNicLabel = "全部网卡";

    /// <summary>GetSetting 哨兵值：nic 键从未落盘时返回它，用于区分「用户显式选了全部」。</summary>
    private const string NicUnsetSentinel = "\u0000unset";

    internal readonly NetSpeedState State = new();

    private readonly object _gate = new();
    private readonly NetworkSampler _sampler = new(IntervalMsValues[DefaultIntervalIndex]);
    private IPluginHost? _host;
    private Timer? _timer;
    private long _nextSampleAt;
    private int _intervalIndex = DefaultIntervalIndex;
    private string _nicId = "";
    private string _nicName = "";
    private string _nicLabel = AllNicLabel;
    private bool _nicAutoPending;

    public string Id => "com.snow.netspeed";
    public string DisplayName => "网络速度";
    public string Version => typeof(NetSpeedPlugin).Assembly.GetName().Version!.ToString(3);
    public string Author => "Snow";

    /// <summary>当前采样间隔档位索引（0=100ms / 1=500ms / 2=1秒 / 3=5秒 / 4=10秒）。</summary>
    public int IntervalIndex => Volatile.Read(ref _intervalIndex);

    /// <summary>详情页网卡行显示名：全部 / 当前网卡名 / 已拔出提示。</summary>
    public string NicLabel => Volatile.Read(ref _nicLabel);

    public void Initialize(IPluginHost host)
    {
        lock (_gate)
        {
            _host = host;
            host.SettingsChanged += ReloadSettings;
            ReloadSettings();
            _timer = new Timer(Tick);
            ScheduleSample(Environment.TickCount64);
        }

        host.RegisterWidget(new NetSpeedWidget(this));
        host.RegisterSettingsPage(new NetSpeedSettingsPage());

        host.PostReminder(new ReminderData
        {
            Title = "网络速度已加载",
            Body = "左键点击组件在「并列」与「双行」之间切换，右键展开详情",
        });
    }

    private void Tick(object? state)
    {
        lock (_gate)
        {
            if (_timer is null) return;
            long now = Environment.TickCount64;
            if (now < _nextSampleAt)
            {
                // Change 前已经排队的回调，或系统时钟粒度导致的提前唤醒。
                ArmTimer();
                return;
            }

            try
            {
                if (_nicAutoPending && _sampler.TryFindWlanNic(out string id))
                    _host?.SetSetting(NicKey, id);

                if (_sampler.TrySample(now, out double downBps, out double upBps))
                    State.Push(downBps, upBps);
                else
                    State.Push(0, 0);
                UpdateNicLabel();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError("NPS.NetSpeed 采样失败：{0}", ex);
            }
            finally
            {
                ScheduleSample(now);
            }
        }
    }

    internal void ReloadSettings()
    {
        lock (_gate)
        {
            if (_host is not { } host) return;
            var mode = host.GetSetting(ModeKey, "0") == "1" ? SpeedMode.DualRow : SpeedMode.SideBySide;
            if (State.Mode != mode)
            {
                State.Mode = mode;
                host.InvalidateWidgetLayout();
            }

            int interval = ParseIndex(host.GetSetting(IntervalKey, ""));
            if (interval != _intervalIndex)
            {
                _sampler.SetInterval(IntervalMsValues[interval]);
                Volatile.Write(ref _intervalIndex, interval);
                ScheduleSample(Environment.TickCount64);
            }

            // 区分初次运行与用户显式选择「全部网卡」。
            string rawNic = host.GetSetting(NicKey, NicUnsetSentinel);
            _nicAutoPending = rawNic == NicUnsetSentinel;
            string nicId = _nicAutoPending ? "" : rawNic;
            if (!string.Equals(_nicId, nicId, StringComparison.Ordinal))
            {
                _nicId = nicId;
                _nicName = "";
                _sampler.SetNicFilter(nicId);
                UpdateNicLabel();
            }
        }
    }

    /// <summary>切换模式，宿主发出设置通知后重测组件宽度。</summary>
    internal void SetMode(SpeedMode mode)
    {
        lock (_gate)
            if (State.Mode != mode)
                _host?.SetSetting(ModeKey, mode == SpeedMode.DualRow ? "1" : "0");
    }

    /// <summary>切换采样间隔，宿主发出设置通知后调整定时器周期。</summary>
    internal void SetSampleInterval(int index)
    {
        lock (_gate)
            if (index >= 0 && index < IntervalMsValues.Length && index != _intervalIndex)
                _host?.SetSetting(IntervalKey, index.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>循环切换统计网卡：全部 → 依次每个候选网卡 → 全部。</summary>
    internal void CycleNic()
    {
        lock (_gate)
        {
            var list = _sampler.GetNicList();
            if (list.Count == 0) return;

            int next = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (!string.Equals(list[i].Id, _nicId, StringComparison.Ordinal)) continue;
                next = i + 1;
                break;
            }

            _host?.SetSetting(NicKey, next < list.Count ? list[next].Id : "");
        }
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
        lock (_gate)
        {
            if (_host is { } host) host.SettingsChanged -= ReloadSettings;
            _host = null;
            _timer?.Dispose();
            _timer = null;
        }
    }

    private void UpdateNicLabel()
    {
        if (_sampler.TryGetNicName(_nicId, out string name)) _nicName = name;
        string label = _nicId.Length == 0 ? AllNicLabel : _nicName.Length == 0 ? "已拔出的网卡" : _nicName;
        Volatile.Write(ref _nicLabel, label);
    }

    private void ScheduleSample(long now)
    {
        _nextSampleAt = now + IntervalMsValues[_intervalIndex];
        ArmTimer();
    }

    private void ArmTimer()
        => _timer?.Change(Math.Max(1, _nextSampleAt - Environment.TickCount64), Timeout.Infinite);

    private static int ParseIndex(string raw)
    {
        return int.TryParse(raw, out int value) && value >= 0 && value < IntervalMsValues.Length
            ? value
            : DefaultIntervalIndex;
    }
}
