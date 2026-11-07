using System;
using System.Globalization;

namespace NPS.NetSpeed.Core;

/// <summary>
/// 数值 → 「数字 + 单位」的拆分格式化。
/// 最小单位由 <see cref="Format"/> 的 minUnitIndex 决定：岛上收起态组件用 KB/s（MinUnitIndex，
/// 不显示 B/s，亚 KB 流量以小数示出），详情页大号读数用 B/s（LowestUnitIndex）。
/// 当前单位数值 ≥ 1024 即升单位（与任务管理器等常规习惯一致），< 0.85 才降单位
/// （等价于上一单位数值 < 870.4）：单位边界下方留 15% 回差，速度在边界抖动时不会每帧来回跳。
/// </summary>
internal static class SpeedFormat
{
    public const int MaxUnitIndex = 3;
    /// <summary>岛上收起态组件的最小单位（KB/s）。</summary>
    public const int MinUnitIndex = 1;
    /// <summary>详情页大号读数的最小单位（B/s）。</summary>
    public const int LowestUnitIndex = 0;

    private static readonly string[] UnitNames = { "B/s", "KB/s", "MB/s", "GB/s" };
    private const double Base = 1024.0;
    // 阈值与 scaled（当前单位下的数值）同量纲：升单位 1024（到边界即升）、降单位 0.85。
    private const double UpThreshold = Base;
    private const double DownThreshold = 0.85;

    public static void Format(double bps, ref int unitIndex, out string number, out string unit,
        int minUnitIndex = MinUnitIndex)
    {
        if (double.IsNaN(bps) || double.IsInfinity(bps) || bps < 0) bps = 0;
        if (unitIndex < minUnitIndex) unitIndex = minUnitIndex;
        if (unitIndex > MaxUnitIndex) unitIndex = MaxUnitIndex;

        if (bps < 4)
        {
            // 零流量统一回最小单位 + “0”，不会出现 “0 MB/s” 这种突变的单位
            unitIndex = minUnitIndex;
            number = "0";
            unit = UnitNames[minUnitIndex];
            return;
        }

        double scaled = bps / Math.Pow(Base, unitIndex);
        while (unitIndex < MaxUnitIndex && scaled >= UpThreshold)
        {
            unitIndex++;
            scaled = bps / Math.Pow(Base, unitIndex);
        }
        while (unitIndex > minUnitIndex && scaled < DownThreshold)
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
