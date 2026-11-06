using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace NPS.NetSpeed.Core;

/// <summary>探测到的火绒可执行入口。</summary>
internal readonly record struct HuorongTarget(string ExePath, bool IsNetFlow);

/// <summary>
/// 定位并启动火绒的流量监控。火绒官方确认其 bin 目录下的 NetFlow.exe 可直接吊起
/// 流量监控窗口（5.0 / 6.0 均支持）；若只有主程序 wsctrl.exe，则回退打开主界面。
///
/// 路径探测按可靠性多级回退：注册表配置键 → 卸载表 InstallLocation → HipsDaemon
/// 服务 ImagePath → 默认安装路径；只在按钮点击时执行（注册表读取为微秒级，无需缓存）。
/// 仅在 Windows 运行时才真正读注册表；此类型不触碰任何网络数据。
/// </summary>
internal static class HuorongLauncher
{
    public static HuorongTarget? Resolve()
    {
        foreach (string root in CandidateRoots())
        {
            string bin = Path.Combine(root, "bin");
            string netflow = Path.Combine(bin, "NetFlow.exe");
            if (File.Exists(netflow)) return new HuorongTarget(netflow, IsNetFlow: true);

            string wsctrl = Path.Combine(bin, "wsctrl.exe");
            if (File.Exists(wsctrl)) return new HuorongTarget(wsctrl, IsNetFlow: false);
        }
        return null;
    }

    /// <summary>以 ShellExecute 启动目标（火绒已运行时由它自行激活既有窗口）。返回是否成功。</summary>
    public static bool TryStart(string exePath)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? string.Empty,
            });
            return process != null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>候选安装根目录（即包含 bin\ 的 Sysdiag 目录），已按来源排序去重。</summary>
    private static IEnumerable<string> CandidateRoots()
    {
        var roots = new List<string>();

        // 1. 火绒配置根键 HKLM\SOFTWARE\Huorong\Sysdiag（含 WOW6432Node 变体）：
        //    值名不固定，枚举所有字符串值，取「目录存在且路径含 Huorong」的作为候选
        CollectFromConfigKey(roots, @"SOFTWARE\Huorong\Sysdiag");
        CollectFromConfigKey(roots, @"SOFTWARE\WOW6432Node\Huorong\Sysdiag");

        // 2. 卸载表：DisplayName 含「火绒 / Huorong」的条目取其 InstallLocation
        CollectFromUninstall(roots, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
        CollectFromUninstall(roots, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall");

        // 3. HipsDaemon 服务的 ImagePath（...\bin\HipsDaemon.exe → 上一级即安装根目录）
        CollectFromService(roots, @"SYSTEM\CurrentControlSet\Services\HipsDaemon");

        // 4. 默认安装路径兜底
        foreach (string? baseDir in new[]
                 {
                     Environment.GetEnvironmentVariable("ProgramFiles"),
                     Environment.GetEnvironmentVariable("ProgramFiles(x86)"),
                 })
        {
            if (!string.IsNullOrEmpty(baseDir)) roots.Add(Path.Combine(baseDir, "Huorong", "Sysdiag"));
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string root in roots)
        {
            if (!string.IsNullOrEmpty(root) && Directory.Exists(root) && seen.Add(root)) yield return root;
        }
    }

    private static void CollectFromConfigKey(ICollection<string> roots, string keyPath)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(keyPath);
            if (key == null) return;
            foreach (string name in key.GetValueNames())
            {
                if (key.GetValue(name) is not string value) continue;
                foreach (string candidate in NormalizeRoot(value)) roots.Add(candidate);
            }
        }
        catch
        {
            // 注册表读取失败（权限 / 键不存在）：走下一级探测
        }
    }

    private static void CollectFromUninstall(ICollection<string> roots, string keyPath)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(keyPath);
            if (key == null) return;
            foreach (string sub in key.GetSubKeyNames())
            {
                using var entry = key.OpenSubKey(sub);
                if (entry == null) continue;
                if (entry.GetValue("DisplayName") is not string display) continue;
                if (!(display.Contains("火绒") || display.Contains("Huorong", StringComparison.OrdinalIgnoreCase))) continue;
                if (entry.GetValue("InstallLocation") is not string location) continue;
                foreach (string candidate in NormalizeRoot(location)) roots.Add(candidate);
            }
        }
        catch
        {
            // 同上：走下一级探测
        }
    }

    private static void CollectFromService(ICollection<string> roots, string keyPath)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(keyPath);
            if (key?.GetValue("ImagePath") is not string imagePath) return;
            foreach (string candidate in NormalizeRoot(imagePath)) roots.Add(candidate);
        }
        catch
        {
            // 同上：走下一级探测
        }
    }

    /// <summary>把各类注册表值规整成「安装根目录」候选（剥离引号 / 参数 / bin 尾巴）。</summary>
    private static IEnumerable<string> NormalizeRoot(string raw)
    {
        string path = raw.Trim().Trim('"');
        int argIndex = path.IndexOf(" -", StringComparison.Ordinal);
        if (argIndex > 0) path = path[..argIndex].Trim().Trim('"');
        if (path.Length == 0) yield break;

        // ...\bin\HipsDaemon.exe → 取 bin 的上一级；...\bin → 直接取上一级；其余原样
        string trimmed = path.TrimEnd('\\', '/');
        string? parent = Path.GetDirectoryName(trimmed);
        if (!string.IsNullOrEmpty(parent)
            && (string.Equals(Path.GetFileName(trimmed), "bin", StringComparison.OrdinalIgnoreCase)
                || trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
        {
            path = parent;
        }

        // 路径必须与火绒相关，避免把注册表里无关字符串当目录
        if (path.Contains("Huorong", StringComparison.OrdinalIgnoreCase) || path.Contains("火绒")) yield return path;
    }
}
