using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MacDock.Models;

public class AppSettings
{
    /// <summary>图标基础尺寸（像素）。</summary>
    public int IconSize { get; set; } = 48;

    /// <summary>放大强度（0.2 ~ 2.0）。</summary>
    public double MagnifyBoost { get; set; } = 1.0;

    /// <summary>图标间距（像素）。</summary>
    public int IconSpacing { get; set; } = 8;

    /// <summary>背景最小宽度（像素，0 = 自动跟随图标）。</summary>
    public double BarMinWidth { get; set; } = 0;

    /// <summary>背景最小高度（像素，0 = 自动跟随图标）。</summary>
    public double BarMinHeight { get; set; } = 0;

    /// <summary>Dock 位置：BottomCenter / BottomLeft / BottomRight / TopCenter。</summary>
    public string DockPosition { get; set; } = "BottomCenter";

    /// <summary>距屏幕边缘的额外距离（像素，正数远离边缘）。</summary>
    public int DockOffsetY { get; set; } = 0;

    /// <summary>水平偏移（像素，负值向左）。</summary>
    public int DockOffsetX { get; set; } = 0;

    /// <summary>背景框圆角半径（像素）。</summary>
    public int CornerRadius { get; set; } = 16;

    /// <summary>边缘唤出热区距离（像素，鼠标距屏幕边缘多近时唤出 Dock）。</summary>
    public int EdgeHotzoneSize { get; set; } = 14;

    /// <summary>Distance between built-in folder popup and dock icon (px).</summary>
    public int FolderPanelGap { get; set; } = 8;

    /// <summary>背景样式：Acrylic（毛玻璃）/ Solid（纯色）/ Transparent（透明）。</summary>
    public string BackgroundStyle { get; set; } = "Acrylic";

    /// <summary>背景主色（#RRGGBB）。</summary>
    public string BackgroundColor { get; set; } = "#26262E";

    /// <summary>背景不透明度（0.0 ~ 1.0，仅毛玻璃/透明样式生效）。</summary>
    public double BackgroundOpacity { get; set; } = 0.85;

    /// <summary>是否显示边框。</summary>
    public bool ShowBorder { get; set; } = false;

    /// <summary>边框颜色（#AARRGGBB / #RRGGBB）。</summary>
    public string BorderColor { get; set; } = "#55FFFFFF";

    /// <summary>开机自启。</summary>
    public bool RunOnStartup { get; set; } = false;


    /// <summary>鼠标移到屏幕边缘时唤出。</summary>
    public bool ShowOnEdge { get; set; } = true;

    /// <summary>被窗口覆盖时禁止唤出 Dock（边缘热区不生效）。</summary>
    public bool BlockShowWhenCovered { get; set; } = false;

    /// <summary>是否启用“被覆盖禁止唤出”开关快捷键。</summary>
    public bool BlockHotkeyEnabled { get; set; } = true;

    /// <summary>开关快捷键修饰键：None / Ctrl / Alt / Shift / Win。</summary>
    public string BlockHotkeyModifier { get; set; } = "None";

    /// <summary>开关快捷键主键（默认 F3）。</summary>
    public string BlockHotkeyKey { get; set; } = "F3";
    /// <summary>是否启用隐藏桌面图标快捷键。</summary>
    public bool HotkeyEnabled { get; set; } = true;

    /// <summary>快捷键修饰键：None / Ctrl / Alt / Shift / Win。</summary>
    public string HotkeyModifier { get; set; } = "None";

    /// <summary>快捷键主键（默认 F2）。</summary>
    public string HotkeyKey { get; set; } = "F2";

    public List<DockItemModel> Items { get; set; } = new();

    public AppSettings Clone()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(this);
        return System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
    }
}

