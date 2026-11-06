using System;
using System.Globalization;

namespace NPS.NetSpeed.Core;

/// <summary>
/// 数值 → 「数字 + 单位」的拆分格式化。
/// 当前单位数值 ≥ 1024 即升单位（与任务管理器等常规习惯一致），< 0.85 才降单位
/// （等价于上一单位数值 < 870.4）：单位边界下方留 15% 回差，速度在边界抖动时不会每帧来回跳。
/// </summary>
internal static class SpeedFormat
{
    public const int MaxUnitIndex = 3;

    private static readonly string[] UnitNames = { "B/s", "KB/s", "MB/s", "GB/s" };
    private const double Base = 1024.0;
    // 阈值与 scaled（当前单位下的数值）同量纲：升单位 1024（到边界即升）、降单位 0.85。
    private const double UpThreshold = Base;
    private const double DownThreshold = 0.85;

    public static void Format(double bps, ref int unitIndex, out string number, out string unit)
    {
        if (double.IsNaN(bps) || double.IsInfinity(bps) || bps < 0) bps = 0;
        if (unitIndex < 0) unitIndex = 0;
        if (unitIndex > MaxUnitIndex) unitIndex = MaxUnitIndex;

        if (bps < 4)
        {
            // 零流量统一回 B/s + “0”，不会出现 “0 MB/s” 这种突变的单位
            unitIndex = 0;
            number = "0";
            unit = UnitNames[0];
            return;
        }

        double scaled = bps / Math.Pow(Base, unitIndex);
        while (unitIndex < MaxUnitIndex && scaled >= UpThreshold)
        {
            unitIndex++;
            scaled = bps / Math.Pow(Base, unitIndex);
        }
        while (unitIndex > 0 && scaled < DownThreshold)
        {
            unitIndex--;
            scaled = bps / Math.Pow(Base, unitIndex);
        }

        string format = unitIndex switch
        {
            0 => "0",
            1 => scaled < 100 ? "0.0" : "0",
            _ => scaled < 10 ? "0.00" : (scaled < 100 ? "0.0" : "0"),
        };
        number = scaled.ToString(format, CultureInfo.InvariantCulture);
        unit = UnitNames[unitIndex];
    }
}
