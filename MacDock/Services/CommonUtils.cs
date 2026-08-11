using System;
using System.IO;
using System.Windows.Media;

namespace MacDock.Services;

/// <summary>共享工具方法：日志（含轮转）、颜色解析、图标尺寸计算。</summary>
public static class CommonUtils
{
    private const int MaxLogSize = 1 * 1024 * 1024; // 1MB

    /// <summary>写入日志行到 %AppData%\MacDock\debug.log，超过 1MB 时自动轮转为 .log.old。</summary>
    public static void Log(string msg)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} {msg}\r\n";
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MacDock");
        var logPath = Path.Combine(dir, "debug.log");
        var oldPath = Path.Combine(dir, "debug.log.old");
        Directory.CreateDirectory(dir);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                // 日志轮转：超过大小限制时备份旧日志
                if (File.Exists(logPath))
                {
                    var fi = new FileInfo(logPath);
                    if (fi.Length > MaxLogSize)
                    {
                        try { if (File.Exists(oldPath)) File.Delete(oldPath); } catch { }
                        try { File.Move(logPath, oldPath); } catch { }
                    }
                }
                File.AppendAllText(logPath, line);
                return;
            }
            catch
            {
                System.Threading.Thread.Sleep(30);
            }
        }
    }

    /// <summary>解析十六进制颜色字符串（#RRGGBB / #AARRGGBB），失败返回 fallback。</summary>
    public static Color ParseHexColor(string? hex, Color fallback)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(hex))
                return (Color)ColorConverter.ConvertFromString(hex);
        }
        catch { }
        return fallback;
    }

    /// <summary>图标提取尺寸按 16px 取整分桶，拖动尺寸滑块时复用缓存图标，避免 UI 卡顿。</summary>
    public static int IconExtractSize(double displaySize) =>
        Math.Max(32, (int)(Math.Round(displaySize * 2 / 16.0) * 16));
}
