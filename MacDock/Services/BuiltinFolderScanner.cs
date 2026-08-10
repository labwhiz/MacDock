using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MacDock.Models;

namespace MacDock.Services;

/// <summary>把磁盘目录扫描成 MacDock 内置文件夹的初始子项（可启动项 + 子文件夹）。</summary>
public static class BuiltinFolderScanner
{
    private static readonly string[] LaunchableExtensions =
        { ".exe", ".lnk", ".url", ".bat", ".cmd", ".msi", ".appref-ms", ".ps1" };

    public static List<DockItemModel> Scan(string directory)
    {
        var items = new List<DockItemModel>();
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return items;
        try
        {
            foreach (var dir in Directory.GetDirectories(directory))
            {
                string name;
                try { name = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)); }
                catch { name = dir; }
                if (string.IsNullOrEmpty(name) || name.StartsWith(".")) continue;
                items.Add(new DockItemModel { Name = name, TargetPath = dir });
            }
            foreach (var file in Directory.GetFiles(directory))
            {
                string ext;
                try { ext = Path.GetExtension(file); } catch { continue; }
                if (string.IsNullOrEmpty(ext) || !LaunchableExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase)) continue;
                string name = Path.GetFileNameWithoutExtension(file);
                if (string.IsNullOrEmpty(name)) continue;
                items.Add(new DockItemModel { Name = name, TargetPath = file });
            }
        }
        catch (Exception) { }
        return items
            .OrderByDescending(i => Directory.Exists(i.TargetPath))
            .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}