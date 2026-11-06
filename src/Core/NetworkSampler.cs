using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;

namespace NPS.NetSpeed.Core;

/// <summary>
/// 纯 BCL 的网卡累计字节采样器：把所有「已启用、非环回、非隧道」网卡的
/// IP 统计（IPv4 + IPv6 合计）相加，再用 1 秒滑动窗口算速率。
///
/// 只在插件的定时器线程使用（宿主 ScheduleRefresh 保证同一时刻只有一次回调在跑）。
/// </summary>
internal sealed class NetworkSampler
{
    private const long WindowMs = 1000;          // 速率窗口
    private const long MinSpanMs = 400;          // 窗口内至少要有这么长的跨度才出数
    private const long InterfaceRefreshMs = 3000; // 网卡列表刷新周期（插拔网卡 / VPN 启停）

    private readonly record struct Sample(long Ms, long Rx, long Tx);

    private readonly Queue<Sample> _window = new();
    private readonly HashSet<string> _interfaceKeys = new();
    private NetworkInterface[] _interfaces = Array.Empty<NetworkInterface>();
    private long _interfacesReadAt = -1;
    private long _lastRx;
    private long _lastTx;
    private bool _hasLast;

    public void Reset()
    {
        _window.Clear();
        _interfaceKeys.Clear();
        _interfaces = Array.Empty<NetworkInterface>();
        _interfacesReadAt = -1;
        _lastRx = 0;
        _lastTx = 0;
        _hasLast = false;
    }

    /// <summary>取一次速率。返回值 false 表示窗口还没攒够跨度（调用方按 idle 处理）。</summary>
    public bool TrySample(long nowMs, out double downBps, out double upBps)
    {
        downBps = 0;
        upBps = 0;

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
        // 网卡列表变化（插拔 / VPN 启停）同理：新网卡的累计字节是从开机算起的，
        // 不清基线会被误算成一次巨大的瞬时速度。
        if (interfacesChanged || (_hasLast && (rx < _lastRx || tx < _lastTx))) _window.Clear();
        _window.Enqueue(new Sample(nowMs, rx, tx));
        _lastRx = rx;
        _lastTx = tx;
        _hasLast = true;

        while (_window.Count > 2 && nowMs - _window.Peek().Ms > WindowMs) _window.Dequeue();

        Sample? oldest = null;
        foreach (var sample in _window)
        {
            if (nowMs - sample.Ms >= MinSpanMs)
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

    /// <summary>重新枚举网卡。返回值表示网卡集合是否发生变化（对应流量基线需要重设）。</summary>
    private bool RefreshInterfaces(long nowMs)
    {
        _interfacesReadAt = nowMs;
        try
        {
            var all = NetworkInterface.GetAllNetworkInterfaces();
            var list = new List<NetworkInterface>(all.Length);
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var nic in all)
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                list.Add(nic);
                keys.Add(nic.Id);
            }
            _interfaces = list.ToArray();

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
