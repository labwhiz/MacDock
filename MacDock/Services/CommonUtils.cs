using System;
using System.IO;
using System.Windows.Media;

namespace MacDock.Services;

/// <summary>共享工具方法：日志（含轮转）、颜色解析、图标尺寸计算。</summary>
public static class CommonUtils
{
    private const int MaxLogSize = 1 * 1024 * 1024; // 1MB
    private static long? _logSize; // 缓存日志文件大小，避免每次写入都查文件系统（6.3）

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
                RotateIfNeeded(logPath, oldPath);
                File.AppendAllText(logPath, line);
                _logSize = (_logSize ?? 0) + System.Text.Encoding.UTF8.GetByteCount(line);
                return;
            }
            catch
            {
                _logSize = null; // 状态未知，下次重新探测
                System.Threading.Thread.Sleep(30);
            }
        }
    }

    /// <summary>日志轮转：仅在累计大小超限时才检查文件系统（6.3）。</summary>
    private static void RotateIfNeeded(string logPath, string oldPath)
    {
        long size = _logSize ?? (File.Exists(logPath) ? new FileInfo(logPath).Length : 0);
        if (size <= MaxLogSize)
        {
            _logSize = size;
            return;
        }
        try { if (File.Exists(oldPath)) File.Delete(oldPath); } catch { }
        try { File.Move(logPath, oldPath); } catch { }
        _logSize = File.Exists(logPath) ? new FileInfo(logPath).Length : 0;
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
