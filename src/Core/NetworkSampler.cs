using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;

namespace NPS.NetSpeed.Core;

/// <summary>给详情页网卡切换用的候选网卡快照（渲染线程只读）。</summary>
internal readonly record struct NicInfo(string Id, string Name);

/// <summary>
/// 纯 BCL 的网卡累计字节采样器：把候选网卡（已启用、非环回、非隧道、有 TCP/IP 单播地址，
/// 且通过网卡过滤）的 IP 统计（IPv4 + IPv6 合计）相加，再用滑动窗口算速率。
///
/// 速率窗口随采样间隔缩放：100ms 快档维持 1s 滑动窗口；慢档位窗口 ≈ 1.2 × 间隔，
/// 恰好覆盖最近两个样本，速率等于「最近一个间隔内的字节增量 ÷ 间隔」。
///
/// 只在插件的定时器线程调用 TrySample（宿主 ScheduleRefresh 保证同一时刻只有一次回调在跑）；
/// SetInterval / SetNicFilter / GetNicList / TryGetNicName 供 UI 线程调用，内部用 _gate 同步。
/// </summary>
internal sealed class NetworkSampler
{
    private const long FastWindowMs = 1000;        // 快档（≤833ms 间隔）的速率窗口
    private const long FastMinSpanMs = 400;        // 快档窗口内至少要有的跨度
    private const long InterfaceRefreshMs = 3000;  // 网卡列表刷新周期（插拔网卡 / VPN 启停）

    private static readonly string[] WlanKeywords = { "WLAN", "Wi-Fi", "Wireless", "无线" };

    private readonly record struct Sample(long Ms, long Rx, long Tx);

    private readonly object _gate = new();
    private readonly Queue<Sample> _window = new();
    private readonly HashSet<string> _interfaceKeys = new(StringComparer.Ordinal);
    private readonly HashSet<string> _nicFilter = new(StringComparer.Ordinal);

    private NetworkInterface[] _interfaces = Array.Empty<NetworkInterface>();
    private List<NicInfo> _nicList = new();
    private long _interfacesReadAt = -1;
    private long _lastRx;
    private long _lastTx;
    private bool _hasLast;
    private long _intervalMs = 100;

    /// <summary>速率窗口跨度：慢档 ≈ 1.2 × 间隔，快档固定 1s。</summary>
    private long WindowMs => Math.Max(FastWindowMs, (long)(_intervalMs * 1.2));

    /// <summary>窗口内出数所需的最小跨度：慢档取间隔的 55%，保证至少跨过两个样本。</summary>
    private long MinSpanMs => Math.Max(FastMinSpanMs, (long)(_intervalMs * 0.55));

    public void Reset()
    {
        lock (_gate)
        {
            _window.Clear();
            _interfaceKeys.Clear();
            _interfaces = Array.Empty<NetworkInterface>();
            _nicList.Clear();
            _interfacesReadAt = -1;
            _lastRx = 0;
            _lastTx = 0;
            _hasLast = false;
        }
    }

    /// <summary>设置采样间隔（ms）。间隔变化会清空速率窗口，攒够两个样本后重新出数。</summary>
    public void SetInterval(long intervalMs)
    {
        lock (_gate)
        {
            if (intervalMs <= 0 || intervalMs == _intervalMs) return;
            _intervalMs = intervalMs;
            _window.Clear();
        }
    }

    /// <summary>设置参与统计的网卡 Id 集合；传 null 或空集合表示统计全部候选网卡。</summary>
    public void SetNicFilter(IReadOnlyCollection<string>? ids)
    {
        lock (_gate)
        {
            _nicFilter.Clear();
            if (ids != null)
                foreach (var id in ids)
                    if (!string.IsNullOrEmpty(id)) _nicFilter.Add(id);
            _interfacesReadAt = -1; // 立即重新枚举并应用过滤
            _window.Clear();        // 统计口径变了，旧窗口作废
        }
    }

    /// <summary>候选网卡快照（与当前过滤无关，供详情页列出可选项）。</summary>
    public IReadOnlyList<NicInfo> GetNicList()
    {
        lock (_gate) return _nicList.ToArray();
    }

    /// <summary>按 Id 查网卡显示名，找不到返回 false。</summary>
    public bool TryGetNicName(string id, out string name)
    {
        lock (_gate)
        {
            foreach (var nic in _nicList)
            {
                if (string.Equals(nic.Id, id, StringComparison.Ordinal))
                {
                    name = nic.Name;
                    return true;
                }
            }
        }
        name = "";
        return false;
    }

    /// <summary>候选网卡里找无线网卡（名称含 WLAN / Wi-Fi / Wireless / 无线），供插件首次运行时默认选中。</summary>
    public bool TryFindWlanNic(out string id, out string name)
    {
        lock (_gate)
        {
            foreach (var nic in _nicList)
            {
                foreach (string keyword in WlanKeywords)
                {
                    if (nic.Name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        id = nic.Id;
                        name = nic.Name;
                        return true;
                    }
                }
            }
        }
        id = "";
        name = "";
        return false;
    }

    /// <summary>取一次速率。返回值 false 表示窗口还没攒够跨度（调用方按 idle 处理）。</summary>
    public bool TrySample(long nowMs, out double downBps, out double upBps)
    {
        downBps = 0;
        upBps = 0;

        lock (_gate)
        {
            bool interfacesChanged = false;
            if (_interfacesReadAt < 0 || nowMs - _interfacesReadAt >= InterfaceRefreshMs)
                interfacesChanged = RefreshInterfaces(nowMs);

            if (!TryReadTotals(out long rx, out long tx))
            {
                _window.Clear();
                _hasLast = false;
                return false;
            }

            // 计数器回绕 / 网卡重连会让总量变小的，直接丢掉旧窗口，避免算出天文数字。
            // 网卡列表变化（插拔 / VPN 启停 / 过滤结果变化）同理：新网卡的累计字节是从
            // 开机算起的，不清基线会被误算成一次巨大的瞬时速度。
            if (interfacesChanged || (_hasLast && (rx < _lastRx || tx < _lastTx))) _window.Clear();
            _window.Enqueue(new Sample(nowMs, rx, tx));
            _lastRx = rx;
            _lastTx = tx;
            _hasLast = true;

            long windowMs = WindowMs;
            while (_window.Count > 2 && nowMs - _window.Peek().Ms > windowMs) _window.Dequeue();

            Sample? oldest = null;
            long minSpanMs = MinSpanMs;
            foreach (var sample in _window)
            {
                if (nowMs - sample.Ms >= minSpanMs)
                {
                    oldest = sample;
                    break;
                }
            }
            if (oldest is null) return false;

            double spanSeconds = (nowMs - oldest.Value.Ms) / 1000.0;
            if (spanSeconds <= 0) return false;

            downBps = Math.Max(0d, (rx - oldest.Value.Rx) / spanSeconds);
            upBps = Math.Max(0d, (tx - oldest.Value.Tx) / spanSeconds);
            return true;
        }
    }

    /// <summary>重新枚举网卡。返回值表示「参与统计的网卡集合」是否发生变化（对应流量基线需要重设）。</summary>
    private bool RefreshInterfaces(long nowMs)
    {
        _interfacesReadAt = nowMs;
        try
        {
            var all = NetworkInterface.GetAllNetworkInterfaces();
            var candidates = new List<NetworkInterface>(all.Length);
            var infos = new List<NicInfo>(all.Length);
            foreach (var nic in all)
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                // 没有任何单播地址的接口（WAN Miniport 之类）没有 TCP/IP 绑定，
                // ipconfig 也不显示，统计不到流量，一并排除
                try
                {
                    if (nic.GetIPProperties().UnicastAddresses.Count == 0) continue;
                }
                catch
                {
                    continue; // 属性查询失败的异常网卡按「无协议绑定」处理
                }
                candidates.Add(nic);
                infos.Add(new NicInfo(nic.Id, nic.Name));
            }
            _nicList = infos;

            // 应用网卡过滤：空集合 = 全部候选网卡
            var selected = new List<NetworkInterface>(candidates.Count);
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var nic in candidates)
            {
                if (_nicFilter.Count > 0 && !_nicFilter.Contains(nic.Id)) continue;
                selected.Add(nic);
                keys.Add(nic.Id);
            }
            _interfaces = selected.ToArray();

            bool changed = !_interfaceKeys.SetEquals(keys);
            _interfaceKeys.Clear();
            foreach (var key in keys) _interfaceKeys.Add(key);
            return changed;
        }
        catch
        {
            if (_interfaces.Length == 0) return false;
            _interfaces = Array.Empty<NetworkInterface>();
            _interfaceKeys.Clear();
            return true;
        }
    }

    private bool TryReadTotals(out long rx, out long tx)
    {
        rx = 0;
        tx = 0;
        bool any = false;
        foreach (var nic in _interfaces)
        {
            try
            {
                // GetIPStatistics 是 IPv4 + IPv6 的合计口径；单取 GetIPv4Statistics 会漏掉 IPv6 流量
                var stats = nic.GetIPStatistics();
                rx += stats.BytesReceived;
                tx += stats.BytesSent;
                any = true;
            }
            catch
            {
                // 网卡在这瞬间被禁用 / 虚拟网卡驱动异常：跳过这一张，不影响其他网卡
            }
        }
        return any;
    }
}
