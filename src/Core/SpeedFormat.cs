using System;
using System.Globalization;

namespace NPS.NetSpeed.Core;

/// <summary>
/// 数值 → 「数字 + 单位」的拆分格式化。
/// 单位带 15% 回差：速度在 1 KB/s 附近抖动时不会每帧在 “B/s / KB/s” 之间来回跳。
/// </summary>
internal static class SpeedFormat
{
    public const int MaxUnitIndex = 3;

    private static readonly string[] UnitNames = { "B/s", "KB/s", "MB/s", "GB/s" };
    private const double Base = 1024.0;
    private const double UpThreshold = Base * 1.15;
    private const double DownThreshold = Base * 0.85;

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
