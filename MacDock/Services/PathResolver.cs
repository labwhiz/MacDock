using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MacDock.Models;

namespace MacDock.Services;

/// <summary>把 Dock 项的目标路径解析为可用的完整路径。</summary>
public static class PathResolver
{
    /// <summary>规范化用户输入的路径：去引号、转绝对路径、目录去掉尾部斜杠（保留盘符根如 D:\）。</summary>
    public static string Normalize(string raw)
    {
        var p = (raw ?? string.Empty).Trim();
        if (p.Length >= 2 && p[0] == '"' && p[p.Length - 1] == '"')
            p = p.Substring(1, p.Length - 2).Trim();
        if (p.Length == 0) return string.Empty;
        try { p = Path.GetFullPath(p); }
        catch (Exception) { }
        if (Directory.Exists(p))
        {
            var trimmed = p.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            p = (trimmed.Length == 2 && trimmed[1] == ':') ? trimmed + Path.DirectorySeparatorChar : trimmed;
        }
        return p;
    }

    /// <summary>裸文件名（explorer.exe / notepad.exe）会按系统目录和 PATH 解析；shell: 与完整路径原样返回。</summary>
    public static string Resolve(string targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath)) return targetPath;
        if (targetPath.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) return targetPath;

        string candidate;
        if (Path.IsPathRooted(targetPath))
        {
            candidate = Path.GetFullPath(targetPath);
            return File.Exists(candidate) ? candidate : targetPath;
        }

        var name = targetPath.Trim();
        if (name.Contains('\\') || name.Contains('/'))
        {
            try
            {
                candidate = Path.GetFullPath(name);
                if (File.Exists(candidate)) return candidate;
            }
            catch (Exception) { }
            return name;
        }

        // 依次查找 System32、Windows、SystemX86、当前目录、PATH
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var system = Environment.SystemDirectory;
        var systemX86 = Environment.GetFolderPath(Environment.SpecialFolder.SystemX86);
        foreach (var dir in new[] { system, windows, systemX86, Environment.CurrentDirectory })
        {
            if (string.IsNullOrEmpty(dir)) continue;
            try
            {
                candidate = Path.Combine(dir, name);
                if (File.Exists(candidate)) return candidate;
            }
            catch (Exception) { }
        }

        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                candidate = Path.Combine(dir.Trim(), name);
                if (File.Exists(candidate)) return candidate;
            }
            catch (Exception) { }
        }

        return targetPath;
    }

    /// <summary>将路径规范化、去重后添加到目标列表，返回是否添加成功。</summary>
    public static bool TryAddPath(ICollection<DockItemModel> items, string rawPath)
    {
        var path = Normalize(rawPath);
        if (string.IsNullOrEmpty(path)) return false;
        if (items.Any(i => string.Equals(i.TargetPath, path, StringComparison.OrdinalIgnoreCase))) return false;
        string name = Directory.Exists(path)
            ? Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            : Path.GetFileNameWithoutExtension(path);
        items.Add(new DockItemModel
        {
            Name = string.IsNullOrEmpty(name) ? path : name,
            TargetPath = path,
        });
        return true;
    }
}

