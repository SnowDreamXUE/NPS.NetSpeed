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
    public const int HistoryCapacity = 60;   // 100ms × 60 = 6 秒

    private readonly object _gate = new();
    private readonly float[] _downHistory = new float[HistoryCapacity];
    private readonly float[] _upHistory = new float[HistoryCapacity];
    private double _down;
    private double _up;
    private int _count;
    private int _mode;

    public SpeedMode Mode
    {
        get => (SpeedMode)Volatile.Read(ref _mode);
        set => Volatile.Write(ref _mode, (int)value);
    }

    /// <summary>写入一次采样结果。上行衰减比上升慢，避免数值在 0 附近跳。</summary>
    public void Push(long nowMs, double downRaw, double upRaw)
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
        }
    }

    /// <summary>采样还没准备好（启动首秒 / 网卡瞬时失效）时按 0 推进，让数值平滑落回零。</summary>
    public void PushIdle(long nowMs) => Push(nowMs, 0, 0);

    private static double Ease(double current, double target)
    {
        if (target <= 0) return current * 0.72;            // 零流量：每 100ms 衰减，约 1 秒回落
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

    /// <summary>把最近 n 个历史样本按时间顺序拷进调用方的缓冲区，返回 n。</summary>
    public int CopyHistory(float[] downTarget, float[] upTarget)
    {
        lock (_gate)
        {
            int n = Math.Min(_count, Math.Min(downTarget.Length, upTarget.Length));
            int start = HistoryCapacity - n;
            if (n > 0)
            {
                Array.Copy(_downHistory, start, downTarget, 0, n);
                Array.Copy(_upHistory, start, upTarget, 0, n);
            }
            return n;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            Array.Clear(_downHistory, 0, HistoryCapacity);
            Array.Clear(_upHistory, 0, HistoryCapacity);
            _down = 0;
            _up = 0;
            _count = 0;
        }
    }
}
