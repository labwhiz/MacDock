using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MacDock.Models;

/// <summary>Dock 中的单个应用项。</summary>
public class DockItemModel
{
    public string Id { get; set; } = System.Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    /// <summary>exe / lnk / shell: 路径。</summary>
    public string TargetPath { get; set; } = "";
    public string Arguments { get; set; } = "";
    /// <summary>内置文件夹（MacDock 虚拟文件夹）的子项；非 null 表示这是一个内置文件夹图标。</summary>
    public List<DockItemModel>? FolderItems { get; set; }
    /// <summary>自定义图标：preset:xxx（预设）或文件/软件路径（图标文件、exe、dll、lnk）；null 表示默认图标。</summary>
    public string? IconOverride { get; set; }
    /// <summary>仅用于运行时状态，不持久化。</summary>
    [JsonIgnore]
    public bool IsRunning { get; set; }
}
