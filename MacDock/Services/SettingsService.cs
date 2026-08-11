using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using MacDock.Models;

namespace MacDock.Services;

public class SettingsService
{
    private const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _dir;
    private readonly string _file;

    public SettingsService()
    {
        _dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MacDock");
        _file = Path.Combine(_dir, "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_file))
            {
                var json = File.ReadAllText(_file);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (settings != null)
                {
                    Sanitize(settings);
                    if (settings.Items.Count == 0)
                        settings.Items = DefaultItems();
                    // 配置版本迁移：未来版本号变更时在此补充迁移逻辑
                    if (settings.SchemaVersion < CurrentSchemaVersion)
                    {
                        settings.SchemaVersion = CurrentSchemaVersion;
                    }
                    return settings;
                }
            }
        }
        catch (Exception)
        {
            // 配置损坏时回退默认
        }
        var fresh = new AppSettings { Items = DefaultItems() };
        return fresh;
    }

    /// <summary>清理配置中的危险字段：控制字符/引号路径、控制字符参数、异常超大列表。</summary>
    private static void Sanitize(AppSettings settings)
    {
        if (settings.Items == null)
        {
            settings.Items = DefaultItems();
            return;
        }
        SanitizeItems(settings.Items);
        if (settings.Items.Count > 200)
        {
            for (int i = settings.Items.Count - 1; i >= 200; i--)
                settings.Items.RemoveAt(i);
        }
    }

    private static void SanitizeItems(ObservableCollection<DockItemModel> items)
    {
        for (int i = items.Count - 1; i >= 0; i--)
        {
            var item = items[i];
            if (item == null || !IsSafePath(item.TargetPath) || !IsSafeArguments(item.Arguments))
            {
                items.RemoveAt(i);
                continue;
            }
            if (item.FolderItems != null) SanitizeItems(item.FolderItems);
        }
    }

    private static void SanitizeItems(List<DockItemModel> items)
    {
        items.RemoveAll(i => i == null || !IsSafePath(i.TargetPath) || !IsSafeArguments(i.Arguments));
        foreach (var item in items)
        {
            if (item.FolderItems != null) SanitizeItems(item.FolderItems);
        }
    }

    private static bool IsSafePath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return true; // 内置文件夹占位允许空路径
        foreach (var ch in path)
        {
            if (char.IsControl(ch) || ch == '"') return false;
        }
        return true;
    }

    private static bool IsSafeArguments(string arguments)
    {
        if (string.IsNullOrEmpty(arguments)) return true;
        if (arguments.Length > 1024) return false;
        foreach (var ch in arguments)
        {
            if (char.IsControl(ch)) return false;
        }
        return true;
    }

    public void Save(AppSettings settings)
    {
        try
        {
            settings.SchemaVersion = CurrentSchemaVersion;
            Directory.CreateDirectory(_dir);
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(_file, json);
        }
        catch (Exception)
        {
            // 忽略写配置失败
        }
    }

    public static AppSettings Defaults() => new() { Items = DefaultItems() };

    public static ObservableCollection<DockItemModel> DefaultItems()
    {
        var items = new ObservableCollection<DockItemModel>();
        items.Add(new DockItemModel { Name = "访达", TargetPath = "explorer.exe" });
        items.Add(new DockItemModel { Name = "浏览器", TargetPath = FindBrowser() });
        items.Add(new DockItemModel { Name = "设置", TargetPath = @"C:\Windows\ImmersiveControlPanel\SystemSettings.exe" });

        var terminal = FindTerminal();
        if (terminal != null)
            items.Add(new DockItemModel { Name = "终端", TargetPath = terminal });

        items.Add(new DockItemModel { Name = "记事本", TargetPath = "notepad.exe" });
        items.Add(new DockItemModel { Name = "回收站", TargetPath = "shell:RecycleBinFolder" });
        return items;
    }

    private static string FindBrowser()
    {
        string[] candidates =
        {
            @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
            @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
            @"C:\Program Files\Google\Chrome\Application\chrome.exe",
            @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
            @"C:\Program Files\Mozilla Firefox\firefox.exe",
        };
        foreach (var c in candidates)
            if (File.Exists(c)) return c;
        return "msedge.exe";
    }

    private static string? FindTerminal()
    {
        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WindowsApps\WindowsTerminal.exe");
        if (File.Exists(local)) return local;
        var packages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WindowsApps\wt.exe");
        if (File.Exists(packages)) return packages;
        return null;
    }
}
