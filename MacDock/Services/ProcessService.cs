using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using MacDock.Models;
using MacDock.Native;

namespace MacDock.Services;

public static class ProcessService
{
    /// <summary>判断某个 exe 名对应的进程是否在运行。</summary>
    public static bool IsRunning(string targetPath)
    {
        if (targetPath.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) return false;
        var exeName = Path.GetFileNameWithoutExtension(PathResolver.Resolve(targetPath));
        if (string.IsNullOrEmpty(exeName)) return false;
        if (exeName.Equals("explorer", StringComparison.OrdinalIgnoreCase)) return true; // 资源管理器常驻
        try
        {
            return Process.GetProcessesByName(exeName).Length > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>启动应用。</summary>
    public static void Launch(DockItemModel item)
    {
        try
        {
            if (item.TargetPath.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{item.TargetPath}\"") { UseShellExecute = true });
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
            if (!string.IsNullOrWhiteSpace(item.Arguments)) psi.Arguments = item.Arguments;
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
            try
            {
                var procs = Process.GetProcessesByName(exeName);
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
        }
        Launch(item);
    }
}
