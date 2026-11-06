using System;
using NotchPeninsula;
using SkiaSharp;

namespace NPS.NetSpeed.Rendering;

/// <summary>
/// 字体解析：直接借宿主的字体面，插件不自带字体文件。
/// 两条铁律（见宿主文档 10.1）：每次现取、绝不缓存；绝不 Dispose 借来的字体面。
/// </summary>
internal static class PluginFont
{
    public static SKTypeface? Normal()
    {
        try { return FontConfig.Normal; } catch { }
        try { return FontConfig.Fallback; } catch { return null; }
    }

    public static SKTypeface? Bold()
    {
        try { return FontConfig.Bold; } catch { return Normal(); }
    }
}
