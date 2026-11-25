using System;
using System.Threading;

namespace NPS.NetSpeed.Core;

/// <summary>收起态两种布局：并列（两个通道左右并排）/ 双行（上下各一行）。</summary>
internal enum SpeedMode
{
    SideBySide = 0,
    DualRow = 1,
}

/// <summary>渲染线程读取的只读快照（逻辑单位 B/s）。</summary>
internal readonly record struct SpeedSnapshot(float DownBps, float UpBps);

/// <summary>
/// 插件共享状态。定时器线程写、渲染线程读，全部经过 <c>_gate</c>；
/// Mode 只用一个 int 承载，走 Volatile，避免锁在每帧路径上。
/// </summary>
internal sealed class NetSpeedState
{
    public const int HistoryCapacity = 60;

    private readonly object _gate = new();
    private readonly float[] _downHistory = new float[HistoryCapacity];
    private readonly float[] _upHistory = new float[HistoryCapacity];
    private double _down;
    private double _up;
    private int _count;
    private int _mode;
    private long _historyVersion;

    public SpeedMode Mode
    {
        get => (SpeedMode)Volatile.Read(ref _mode);
        set => Volatile.Write(ref _mode, (int)value);
    }

    /// <summary>写入一次采样结果。上行衰减比上升慢，避免数值在 0 附近跳。</summary>
    public void Push(double downRaw, double upRaw)
    {
        lock (_gate)
        {
            _down = Ease(_down, downRaw);
            _up = Ease(_up, upRaw);
            if (downRaw <= 0 && _down < 4) _down = 0;
            if (upRaw <= 0 && _up < 4) _up = 0;
            ShiftAppend(_downHistory, (float)_down);
            ShiftAppend(_upHistory, (float)_up);
            if (_count < HistoryCapacity) _count++;
            _historyVersion++;
        }
    }

    private static double Ease(double current, double target)
    {
        if (target <= 0) return current * 0.72;            // 零流量：每次采样衰减
        double k = target > current ? 0.50 : 0.26;         // 上升快、下降缓
        return current + (target - current) * k;
    }

    private static void ShiftAppend(float[] buffer, float value)
    {
        Array.Copy(buffer, 1, buffer, 0, buffer.Length - 1);
        buffer[^1] = value;
    }

    public SpeedSnapshot Read()
    {
        lock (_gate) return new SpeedSnapshot((float)_down, (float)_up);
    }

    /// <summary>读取当前速率，只在历史变化时复制样本。</summary>
    public bool ReadHistory(ref long version, float[] downTarget, float[] upTarget,
        out int count, out SpeedSnapshot snapshot)
    {
        lock (_gate)
        {
            snapshot = new SpeedSnapshot((float)_down, (float)_up);
            count = Math.Min(_count, Math.Min(downTarget.Length, upTarget.Length));
            if (version == _historyVersion) return false;
            int start = HistoryCapacity - count;
            Array.Copy(_downHistory, start, downTarget, 0, count);
            Array.Copy(_upHistory, start, upTarget, 0, count);
            version = _historyVersion;
            return true;
        }
    }
}
