using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using MacDock.Models;
using MacDock.Native;

namespace MacDock.Services;

public static class ProcessService
{
    /// <summary>获取当前所有运行中的进程名集合（忽略大小写），供批量判断使用，避免逐个调用 GetProcessesByName。</summary>
    public static HashSet<string> GetRunningExeNames()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var procs = Process.GetProcesses();
            try
            {
                foreach (var p in procs)
                {
                    try { set.Add(p.ProcessName); } catch { }
                }
            }
            finally
            {
                foreach (var p in procs) p.Dispose();
            }
        }
        catch { }
        return set;
    }

    /// <summary>解析目标可执行文件名（shell: URI 或空路径返回 null）。</summary>
    private static string? ResolveExeName(string targetPath)
    {
        if (targetPath.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) return null;
        var exeName = Path.GetFileNameWithoutExtension(PathResolver.Resolve(targetPath));
        return string.IsNullOrEmpty(exeName) ? null : exeName;
    }

    /// <summary>判断某个 exe 名对应的进程是否在运行。</summary>
    public static bool IsRunning(string targetPath)
    {
        var exeName = ResolveExeName(targetPath);
        if (exeName == null) return false;
        if (exeName.Equals("explorer", StringComparison.OrdinalIgnoreCase)) return true; // 资源管理器常驻
        try
        {
            var procs = Process.GetProcessesByName(exeName);
            try
            {
                return procs.Length > 0;
            }
            finally
            {
                foreach (var p in procs) p.Dispose();
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>批量场景：用已枚举的进程名集合判断，与 OnRunningTick 共用同一判定逻辑，避免重复实现（2.4）。</summary>
    public static bool IsRunning(string targetPath, HashSet<string> runningExeNames)
    {
        var exeName = ResolveExeName(targetPath);
        if (exeName == null) return false;
        if (exeName.Equals("explorer", StringComparison.OrdinalIgnoreCase)) return true; // 资源管理器常驻
        return runningExeNames.Contains(exeName);
    }

    /// <summary>启动应用。</summary>
    public static void Launch(DockItemModel item)
    {
        try
        {
            if (item.TargetPath.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            {
                var uri = SanitizeShellUri(item.TargetPath);
                if (uri == null) return;
                Process.Start(new ProcessStartInfo("explorer.exe", uri) { UseShellExecute = true });
                return;
            }

            var target = PathResolver.Resolve(item.TargetPath);
            var exeName = Path.GetFileNameWithoutExtension(target);

            // 访达：explorer 常驻，不带参数不会开新窗口，这里固定打开"此电脑"
            if (exeName.Equals("explorer", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(item.Arguments))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "shell:MyComputerFolder") { UseShellExecute = true });
                return;
            }

            // 设置：SystemSettings.exe 是 UWP 存根，直接启动不可靠，改用 ms-settings: URI
            if (exeName.Equals("SystemSettings", StringComparison.OrdinalIgnoreCase))
            {
                Process.Start(new ProcessStartInfo("ms-settings:") { UseShellExecute = true });
                return;
            }

            var psi = new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true,
            };
            if (!string.IsNullOrWhiteSpace(item.Arguments))
            {
                if (!IsSafeArguments(item.Arguments)) return;
                // 目标是命令解释器时，额外拒绝可被解释为管道/重定向/命令连接的元字符（7.1）
                if (IsCommandInterpreter(exeName) && !IsSafeInterpreterArguments(item.Arguments)) return;
                psi.Arguments = item.Arguments;
            }
            if (Path.IsPathRooted(target))
            {
                var dir = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(dir)) psi.WorkingDirectory = dir;
            }
            Process.Start(psi);
        }
        catch (Exception)
        {
            // 启动失败静默
        }
    }

    /// <summary>校验 shell: URI，仅允许安全字符，防止参数注入。</summary>
    private static string? SanitizeShellUri(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!value.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) return null;
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch) || ch is ':' or '_' or '-' or '.' or ' ' or '{' or '}' or '/') continue;
            return null;
        }
        return value;
    }

    /// <summary>启动参数仅允许可打印字符，禁止控制字符。</summary>
    private static bool IsSafeArguments(string arguments)
    {
        if (arguments.Length > 1024) return false;
        foreach (var ch in arguments)
        {
            if (char.IsControl(ch)) return false;
        }
        return true;
    }

    /// <summary>命令解释器（cmd/powershell 等）目标：额外过滤可能被解释为管道/重定向/命令连接的元字符（7.1）。</summary>
    private static readonly HashSet<string> CommandInterpreters = new(StringComparer.OrdinalIgnoreCase)
    {
        "cmd", "powershell", "pwsh", "wscript", "cscript",
    };

    private static bool IsCommandInterpreter(string exeName) => CommandInterpreters.Contains(exeName);

    private static bool IsSafeInterpreterArguments(string arguments)
    {
        foreach (var ch in arguments)
        {
            if (ch is '&' or '|' or '<' or '>' or '^' or '%' or '`' or '(' or ')') return false;
        }
        return true;
    }

    /// <summary>若已运行则激活其主窗口，否则启动。</summary>
    public static void ActivateOrLaunch(DockItemModel item)
    {
        var target = PathResolver.Resolve(item.TargetPath);
        var exeName = Path.GetFileNameWithoutExtension(target);

        // 访达：总是开新窗口（资源管理器常驻，激活现有窗口没有意义）
        if (exeName.Equals("explorer", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(item.Arguments))
        {
            Launch(item);
            return;
        }

        if (!item.TargetPath.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
        {
            Process[] procs = null;
            try
            {
                procs = Process.GetProcessesByName(exeName);
                // 优先找有主窗口的
                foreach (var p in procs.OrderByDescending(p => p.MainWindowHandle != IntPtr.Zero))
                {
                    if (p.MainWindowHandle != IntPtr.Zero)
                    {
                        Win32.ShowWindowAsync(p.MainWindowHandle, Win32.SW_RESTORE);
                        Win32.SetForegroundWindow(p.MainWindowHandle);
                        return;
                    }
                }
                if (procs.Length > 0)
                {
                    // 有进程但无窗口（如后台），再启动一个新实例
                    Launch(item);
                    return;
                }
            }
            catch
            {
                // fall through to launch
            }
            finally
            {
                if (procs != null)
                {
                    foreach (var p in procs) p.Dispose();
                }
            }
        }
        Launch(item);
    }
}
