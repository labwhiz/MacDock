# MacDock 项目对抗式审查报告

> 审查日期：2026-08-11
> 审查范围：全项目代码（MacDock 主体 + installer 安装器）
> 审查方式：第一性原理对抗式审查，不修改任何代码

---

## 一、汇总概览

| 严重级别 | 数量 | 说明 |
|---------|------|------|
| 🔴 高危 BUG | 4 | 可能导致崩溃、资源泄漏或功能失效 |
| 🟡 中危 BUG | 6 | 边界条件、状态不一致、逻辑遗漏 |
| 🟠 低危/健壮性 | 5 | 防御性编程缺失 |
| 💀 死代码 | 3 个方法 + 1 个类 + ~20 个 Win32 常量/导入 | 完全未被调用 |
| 🔧 优化建议 | 12 | 性能、架构、可维护性改进 |

---

## 二、🔴 高危 BUG

### B1. `Process` 对象未释放 — 句柄泄漏 ✅ 已修复

**文件**: `MacDock/Services/ProcessService.cs`（第 21、93 行）
**文件**: `installer/Installer.cs`（第 221 行）

`Process.GetProcessesByName()` 返回的 `Process` 对象实现了 `IDisposable`，内部持有操作系统进程句柄。当前代码从不释放这些对象：

```csharp
// ProcessService.IsRunning — 每次调用泄漏一个 Process[] 的全部句柄
return Process.GetProcessesByName(exeName).Length > 0;

// ProcessService.ActivateOrLaunch — procs 数组从未 Dispose
var procs = Process.GetProcessesByName(exeName);
```

**影响**：`OnRunningTick` 每 1.8 秒对所有 Dock 项调用 `IsRunning`，假设有 8 个图标，每秒泄漏约 4.4 个进程句柄。长时间运行后可能导致系统资源耗尽。

> **修复状态**：已在分支 `fix/process-handle-leak` 修复。三处 `Process.GetProcessesByName()` 调用均添加 `try/finally` 确保 `Process` 对象被 `Dispose()` 释放。

---

### B2. `IconService.Cache` 无限增长 — 内存泄漏 ✅ 已修复

**文件**: `MacDock/Services/IconService.cs`（第 16 行）

```csharp
private static readonly Dictionary<string, BitmapSource> Cache = new();
```

缓存键为 `路径 + "@" + 尺寸`，无上限、无淘汰策略。每次拖动尺寸滑块（`UpdateItemVisuals` 中 `sizeChanged` 为 true 时重新提取图标）都会产生新的缓存条目。`GetItemIcon` 中预设图标、自定义图标图片也各自有独立缓存键。长时间使用且频繁调整尺寸会导致内存持续增长。

**影响**：长时间运行（尤其是用户反复调整设置）后内存占用不断攀升，无回收机制。

> **修复状态**：已在分支 `fix/icon-cache-memory-leak` 修复。添加 `MaxCacheSize = 256` 上限，新增 `AddToCache` 方法在超限时自动清空缓存重建，所有 4 处直接赋值替换为调用 `AddToCache`。`BitmapSource` 已 `Freeze`，清空不影响已显示图标。

---

### B3. `NuGet.Config` 硬编码开发者本机绝对路径 ✅ 已修复

**文件**: `NuGet.Config`（第 8 行）

```xml
<add key="globalPackagesFolder" value="D:\AI\mcp-workspace\.nuget-packages" />
```

此路径是开发机器特定的。其他开发者克隆仓库后，`dotnet restore` / `dotnet publish` 会将包放到一个可能不存在或无权限的路径，导致构建失败。`publish/MacDock-fd` 发布产物目录也引用了这个路径的输出。

**影响**：项目无法在其他机器上正常构建。

> **修复状态**：已在分支 `fix/nuget-config-hardcoded-path` 修复。删除 `config` 节点中 `globalPackagesFolder` 的硬编码路径设置，让 NuGet 使用默认全局包目录（`%USERPROFILE%\.nuget\packages`）。

---

### B4. `SettingsWindow.OnDefaultClick` 恢复默认时遗漏 `BlockShowWhenCovered` ✅ 已修复

**文件**: `MacDock/SettingsWindow.xaml.cs`（第 548–579 行）

"恢复默认"按钮处理了几乎所有设置项，但唯独没有恢复 `BlockShowWhenCovered`：

```csharp
private void OnDefaultClick(object sender, RoutedEventArgs e)
{
    var def = SettingsService.Defaults();
    _work.IconSize = def.IconSize;
    // ... 恢复了 IconSize, MagnifyBoost, IconSpacing, ... 等约 20 个属性
    _work.ShowOnEdge = def.ShowOnEdge;
    _work.HotkeyEnabled = def.HotkeyEnabled;
    // ❌ 缺少: _work.BlockShowWhenCovered = def.BlockShowWhenCovered;
    _work.Items = def.Items;
    LoadFromWork();
    // ...
}
```

**影响**：用户点击"恢复默认"后，"被覆盖时禁止唤出 Dock"的状态不会重置，与用户预期不符。

> **修复状态**：已在分支 `fix/ondefault-missing-block-show` 修复。在 `OnDefaultClick` 方法中 `BlockHotkeyKey` 与 `Items` 之间添加 `_work.BlockShowWhenCovered = def.BlockShowWhenCovered`。

---

## 三、🟡 中危 BUG

### B5. `DesktopIconsService` 句柄缓存永不刷新 ✅ 已修复

**文件**: `MacDock/Services/DesktopIconsService.cs`（第 13、54–62 行）

```csharp
private bool _hooked;
// ...
private void EnsureHooked()
{
    if (_hooked) return;  // 只枚举一次
    _listViews.Clear();
    EnumWindows(_enumWindowsProc, IntPtr.Zero);
    _hooked = true;
}
```

`_hooked` 设为 true 后，即使 Explorer 重启（`explorer.exe` 崩溃后自动重启），缓存的 `SysListView32` 窗口句柄已失效，但 `AreIconsVisible()` / `SetIconsVisible()` 仍使用旧句柄，`ShowWindow` 对已销毁窗口调用将静默失败。

**影响**：Explorer 重启后 F2 隐藏/显示桌面图标功能失效，需重启 MacDock。

> **修复状态**：已在分支 `fix/desktop-icons-stale-handles` 修复。在 `Win32.cs` 中添加 `IsWindow` P/Invoke，`EnsureHooked()` 中验证所有缓存句柄是否仍为有效窗口，任一失效时清空缓存并重新枚举。

---

### B6. `SettingsWindow` 初始化时 Slider `ValueChanged` 触发多余事件 ✅ 已修复

**文件**: `MacDock/SettingsWindow.xaml.cs`（第 51–83 行）

构造函数先调用 `HookEvents()` 订阅所有 `ValueChanged`，再调用 `LoadFromWork()` 设置 Slider 的 `Value`。设置 Value 会触发 `ValueChanged` 事件，从而执行 `Save()` 和 `SettingsChanged?.Invoke(_work)`。

```csharp
public SettingsWindow(AppSettings current, SettingsService settingsService)
{
    InitializeComponent();
    _work = current.Clone();
    // ...
    HookEvents();   // ← 先订阅
    LoadFromWork(); // ← 再设置 Value，触发已订阅的事件
}
```

**影响**：设置窗口打开时会向磁盘写入一次设置文件，并触发一次 Dock 重建/刷新。虽然不会崩溃，但属于不必要的副作用，且可能与并发的 `ApplyRuntimeSettings` 产生竞态。

> **修复状态**：已在分支 `fix/settings-init-spurious-events` 修复。添加 `_loading` 标志（默认 `true`），构造函数完成 `LoadFromWork` 和 `HookEvents` 后才设为 `false`。所有 25 个事件处理 lambda 开头添加 `if (_loading) return;` 守卫。

---

### B7. `HotkeyService.Register` 注册失败时 `_hwnd` 状态不一致 ✅ 已修复

**文件**: `MacDock/Services/HotkeyService.cs`（第 21–34 行）

```csharp
public void Register(IntPtr hwnd, string modifier, string key)
{
    Unregister();       // ← 此时 _hwnd 被清零
    _hwnd = hwnd;       // ← 设置新 hwnd
    uint flags = ModifierToFlags(modifier) | Win32.MOD_NOREPEAT;
    if (!Win32.RegisterHotKey(hwnd, _hotkeyId, flags, KeyToVk(key)))
    {
        IsRegistered = false;  // ← 失败但 _hwnd 仍保留新值
    }
    else
    {
        IsRegistered = true;
    }
}
```

注册失败时 `_hwnd` 已被设置，但 `IsRegistered` 为 false。后续调用 `Unregister()` 时，由于 `IsRegistered == false`，会跳过 `UnregisterHotKey` 调用。如果此时另一个程序释放了该热键，用户再次调用 `Register` 时 `Unregister()` 仍不会真正注销（因为 `IsRegistered` 仍为 false），但 `RegisterHotKey` 可能成功。状态机不完整。

**影响**：热键注册失败后的恢复逻辑不健壮，可能导致热键注册到错误的窗口句柄。

> **修复状态**：已在分支 `fix/hotkey-register-state` 修复。调整 `Register` 方法逻辑：先尝试 `RegisterHotKey`，成功后才设置 `_hwnd = hwnd` 和 `IsRegistered = true`；失败时确保 `_hwnd = IntPtr.Zero` 和 `IsRegistered = false`，保持状态一致。

---

### B8. `OnBlockHotkeyPressed` 直接修改 `_settings`，设置窗口的 `_work` 不同步 ✅ 已修复

**文件**: `MacDock/MainWindow.xaml.cs`（第 275–281 行）

```csharp
private void OnBlockHotkeyPressed()
{
    _settings.BlockShowWhenCovered = !_settings.BlockShowWhenCovered;
    SaveSettings();
    _settingsWindow?.RefreshBlockMode();
    // ...
}
```

`_settingsWindow` 中的 `_work` 是 `_settings.Clone()` 的结果，是独立副本。F3 快捷键修改的是 `_settings`，然后调用 `RefreshBlockMode()` 来同步勾选框。但 `RefreshBlockMode` 只更新 UI 勾选状态：

```csharp
public void RefreshBlockMode()
{
    if (ChkBlockShow.IsChecked != _work.BlockShowWhenCovered)  // ← 比较 _work（旧副本）
        ChkBlockShow.IsChecked = _work.BlockShowWhenCovered;  // ← 设置为 _work 的值
}
```

`_work.BlockShowWhenCovered` 从未被更新，所以 `RefreshBlockMode` 实际上会用**旧值**覆盖 UI。

**影响**：F3 切换后打开设置窗口，勾选框显示旧状态；或在设置窗口打开时按 F3，勾选框可能被重置回旧值。

> **修复状态**：已在分支 `fix/block-hotkey-work-sync` 修复。新增 `RefreshBlockMode(bool newValue)` 重载，先更新 `_work.BlockShowWhenCovered = newValue` 再同步勾选框；`OnBlockHotkeyPressed` 中传入 `_settings.BlockShowWhenCovered` 新值。保留无参重载兼容其他调用场景。

---

### B9. `MainWindow.RefreshLayoutIfMonitorChanged` 用鼠标位置判断显示器变化 ✅ 已修复

**文件**: `MacDock/MainWindow.xaml.cs`（第 1135–1146 行）

```csharp
private void RefreshLayoutIfMonitorChanged()
{
    var pt = GetCursorScreenPoint();       // ← 鼠标当前所在屏幕
    var mon = GetMonitorInfoOf(pt);
    // ...
    if (Math.Abs(expected - _dockEdge) > 30)
        Dispatcher.BeginInvoke(new Action(RefreshLayout));
}
```

如果鼠标不在 Dock 所在的显示器上，而 Dock 所在显示器分辨率/DPI 变了，这个检测不会触发。

**影响**：多显示器场景下，Dock 可能不会在正确的显示器分辨率变化后重新定位。

> **修复状态**：已在分支 `fix/monitor-change-detection` 修复。改用 Dock 锚点位置（`_anchorLeft`、`_dockEdge`）乘以 `_dpiScale` 构造物理像素坐标的 `Win32.POINT`，确保始终检测 Dock 所在显示器的变化。

---

### B10. `FolderPanelWindow.AddPaths` 与 `MainWindow.AddPathsToBuiltinFolder` 代码完全重复 ✅ 已修复

**文件**: `MacDock/FolderPanelWindow.cs`（第 237–261 行）
**文件**: `MacDock/MainWindow.xaml.cs`（第 1409–1433 行）

两个方法包含完全相同的路径规范化、去重、名称提取、添加逻辑。如果修改其中一处（如增加参数支持），另一处容易被遗漏。

**影响**：维护风险，行为不一致风险。

> **修复状态**：已在分支 `fix/duplicate-addpaths` 修复。在 `PathResolver` 中新增 `TryAddPath(ICollection<DockItemModel>, string)` 公共静态方法封装路径规范化、去重检查、名称提取和 `DockItemModel` 创建；两处调用统一替换为 `PathResolver.TryAddPath`。

---

## 四、🟠 低危/健壮性问题

### B11. `MainWindow.OnItemMouseMove` 拖拽排序缺少索引越界保护 ✅ 已修复

**文件**: `MacDock/MainWindow.xaml.cs`（第 830–841 行）

```csharp
if (_dragging && _settings.Items.Count > 1)
{
    int target = IndexFromX(pos.X);
    if (target >= 0 && target < ItemsHost.Children.Count && target != _dragIndex)
    {
        var el = ItemsHost.Children[_dragIndex];  // ← _dragIndex 可能在异步刷新后越界
        ItemsHost.Children.RemoveAt(_dragIndex);
        // ...
    }
}
```

`_dragIndex` 在 `OnItemMouseDown` 中设置后，如果期间有其他操作（如 `RebuildItems` 清空了 `ItemsHost.Children`），`_dragIndex` 可能指向不存在的索引。虽然都在 UI 线程上，但 `Dispatcher.BeginInvoke` 的延迟执行可能导致时序问题。

> **修复状态**：在 `OnItemMouseMove` 的拖拽条件中添加 `_dragIndex >= 0 && _dragIndex < ItemsHost.Children.Count` 边界检查，防止异步刷新后索引越界。

---

### B12. `Installer.StopMacDock` 中 `p.MainModule` 访问可能抛异常 ✅ 已修复

**文件**: `installer/Installer.cs`（第 225 行）

```csharp
string exePath = p.MainModule != null ? p.MainModule.FileName : null;
```

访问 64/32 位不匹配进程的 `MainModule` 会抛 `Win32Exception`。虽然外层有 try-catch，但 `p.MainModule != null` 这个判断本身就会先抛异常再被 catch。应先 try-get 再判断。

> **修复状态**：将 `p.MainModule != null ? p.MainModule.FileName : null` 改为独立 `try { exePath = p.MainModule?.FileName; } catch { }`，避免 null 检查时直接抛出 `Win32Exception`。

---

### B13. `Win32.POINT` 与 WPF `Point` 命名混淆 ✅ 已修复

`Win32.POINT` 的字段名为 `X`/`Y`（大写），而 WPF 的 `System.Windows.Point` 字段名为 `X`/`Y`（也是大写）。在代码中混用时（如 `cursor.X`），编译器能区分类型，但可读性差，容易混淆哪个是物理像素、哪个是 DIP。

> **修复状态**：将 `Win32.POINT` 的字段从大写 `X`/`Y` 改为小写 `x`/`y`（匹配 Win32 C 惯例），添加 XML 文档注释标明物理像素语义，与 WPF `Point.X/Y`（DIP）视觉区分。更新 `MainWindow.xaml.cs` 和 `FolderPanelWindow.cs` 中所有字段引用。

---

### B14. `SettingsWindow.HotkeyKeys` 列表与 `HotkeyService.KeyToVk` 支持范围不匹配 ✅ 已修复

**文件**: `MacDock/SettingsWindow.xaml.cs`（第 33–44 行）

设置窗口的可选按键列表只包含 F1-F12、0-9、A-Z、Space、Tab、Home、End。但 `HotkeyService.KeyToVk` 还支持 Enter、ESC、Delete、PgUp、PgDn、方向键等。用户在配置文件中手动设置这些键后，设置窗口虽然会动态加入选项，但用户无法从 UI 选择这些常用键。

> **修复状态**：在 `BuildHotkeyKeys` 中补充 8 个缺失按键：`Enter`、`ESC`、`Delete`、`PgUp`、`PgDn`、`Left`、`Right`、`Up`、`Down`，与 `KeyToVk` 支持范围完全匹配。

---

### B15. `MainWindow.ComputeWorkBottomPx` 中任务栏高度硬编码阈值 ✅ 已修复

**文件**: `MacDock/MainWindow.xaml.cs`（第 364、375 行）

```csharp
if (trayH <= 0 || trayH > 120)  // ← 120px 硬编码
    // ...
if (trayH <= 0 || trayH > 120) trayH = 48;  // ← 48px 硬编码
```

在超高 DPI（如 200%+ 缩放下任务栏可能超过 120px）或自定义任务栏高度的场景下，这个阈值会导致任务栏检测失败，Dock 可能与任务栏重叠。

> **修复状态**：将硬编码 `120` 替换为 `(int)(120 * _dpiScale)`，将回退值 `48` 替换为 `(int)(48 * _dpiScale)`，按 DPI 缩放阈值，确保高 DPI 场景下任务栏检测正常。

---

## 五、💀 死代码

### D1. `GlassHelper` 整个类完全未使用

**文件**: `MacDock/Native/Win32.cs`（第 272–354 行）

`GlassHelper` 类包含 `ApplyAcrylic` 和 `ApplyHostBackdrop` 两个公开方法，以及 `ACCENT_POLICY`、`WINDOWCOMPOSITIONATTRIBDATA` 等私有结构和 P/Invoke。`MainWindow.ApplyBackground` 的注释明确说明"不再调用 DWM 特效 / SetWindowRgn"，全部改由 WPF 分层渲染。这个类是历史遗留，约 80 行代码完全无用。

---

### D2. `MainWindow` 中的三个未调用方法

| 方法 | 位置 | 说明 |
|------|------|------|
| `LogTickState()` | 第 1042 行 | 定义了状态日志写入逻辑，但无任何调用点 |
| `ToggleDockVisibility()` | 第 1224 行 | 定义了显隐切换，但从未被调用（显示/隐藏走的是 `OnPoll` → `HideDock`/`ShowDock`） |
| `IsCoveredByVisibleWindows()`（无参数版） | 第 1150 行 | 重载版本，实际只调用了带 `dockRect` 参数的版本 |

---

### D3. `Win32` 中大量未使用的常量和 P/Invoke 声明

| 声明 | 位置 | 说明 |
|------|------|------|
| `GW_HWNDNEXT`, `GW_HWNDPREV` | 第 14–15 行 | 无 `GetWindow` 调用使用它们 |
| `WS_EX_APPWINDOW` | 第 17 行 | 从未被引用 |
| `ABM_GETSTATE`, `ABS_AUTOHIDE` | 第 57, 59 行 | 任务栏自动隐藏检测常量，从未使用 |
| `SHGFI_SMALLICON` | 第 65 行 | 只用了 `SHGFI_LARGEICON` |
| `SHGFI_TYPENAME` | 第 66 行 | 从未使用 |
| `SIIGBF_MEMORYONLY` | 第 72 行 | 从未使用 |
| `GetClientRect()` | 第 160 行 | P/Invoke 声明但无调用 |
| `GetWindowLong()` | 第 196 行 | 只用了 `GetWindowLongPtr` |
| `GetWindow()` | 第 202 行 | P/Invoke 声明但无调用 |
| `CreateRoundRectRgn()` | 第 237 行 | 注释说明已弃用 |
| `SetWindowRgn()` | 第 234 行 | 注释说明已弃用 |
| `SHGetPathFromIDListW()` | 第 260 行 | 从未调用 |
| `SendMessage()` | 第 231 行 | 从未调用（`SendMessageTimeout` 也未使用） |
| `SendMessageTimeout()` | 第 246 行 | 从未调用 |
| `DwmSetWindowAttribute()` | 第 264 行 | 从未调用 |
| `DWMWA_WINDOW_CORNER_PREFERENCE`, `DWMWCP_ROUND` | 第 268–269 行 | 从未使用 |

---

### D4. 其他死代码

| 声明 | 文件 | 说明 |
|------|------|------|
| `SlideSeconds` 常量 | `MainWindow.xaml.cs` 第 44 行 | 定义为 0.18，从未使用 |
| `AppSettings.TaskbarAutoHide` | `AppSettings.cs` 第 85 行 | 标注 `[JsonIgnore]`，从未被读写 |
| `DockItemModel.Id` | `DockItemModel.cs` 第 9 行 | 每次构造生成 GUID，但从未用于查找/比较 |
| `IconConverter` 类 | `IconConverter.cs` 第 9–24 行 | XAML 中声明了资源 `IconConverter`，但绑定中只用了 `DockItemIconConverter`，`IconConverter` 类本身和 XAML 资源声明均多余 |

---

## 六、🔧 优化建议

### O1. 重复的 `MONITORINFO` 结构体和 P/Invoke 声明

**文件**: `MainWindow.xaml.cs`（第 24–43 行） vs `Native/Win32.cs`（第 76–83, 184–187 行）

`MainWindow` 中定义了私有 `MONITORINFO` 结构体和 `MonitorFromPoint`、`GetMonitorInfo` 的 P/Invoke 声明，与 `Win32` 中的定义完全重复。应统一使用 `Win32` 中的声明。

---

### O2. 重复的工具方法应提取为公共方法

| 方法 | 位置 1 | 位置 2 |
|------|--------|--------|
| `Log(string)` | `App.xaml.cs` 第 54 行 | `MainWindow.xaml.cs` 第 101 行 |
| `ParseHexColor/ParseHex` | `MainWindow.xaml.cs` 第 225 行 | `FolderPanelWindow.cs` 第 523 行 |
| `IconExtractSize(double)` | `MainWindow.xaml.cs` 第 458 行 | `FolderPanelWindow.cs` 第 458 行 |

---

### O3. `ProcessService` 性能优化 — 批量获取进程

`OnRunningTick` 每 1.8 秒对每个 Dock 项单独调用 `Process.GetProcessesByName`，应改为一次性获取所有进程名集合，然后在内存中查找匹配：

```csharp
// 当前：O(n) 次系统调用
foreach (var item in items)
    ProcessService.IsRunning(item.TargetPath);

// 建议：1 次系统调用
var allProcs = Process.GetProcesses();
foreach (var item in items)
    allProcs.Any(p => p.ProcessName == exeName);
```

---

### O4. `UpdateMagnification` 性能优化 — 短路返回

`CompositionTarget.Rendering` 每帧（约 60fps）调用 `UpdateMagnification`，即使 Dock 不可见或鼠标未移动也全量计算。建议在 `_dockVisible == false` 或 `_itemRoots.Count == 0` 时提前返回。

---

### O5. `SettingsService` 缺少配置版本管理

`settings.json` 没有 schema 版本号。未来如果配置结构变更（字段重命名、类型变更），旧配置反序列化时会静默丢失数据。建议增加 `SchemaVersion` 字段并实现迁移逻辑。

---

### O6. 设置窗口应使用 `ObservableCollection`

`SettingsWindow` 中 `_work.Items` 是 `List<DockItemModel>`，每次增删后需手动调用 `ReloadItemList()` 刷新 ListBox。改用 `ObservableCollection<DockItemModel>` 可自动响应变化，减少手动刷新代码和潜在遗漏。

---

### O7. 日志文件无大小限制

`debug.log` 使用 `File.AppendAllText` 持续追加，`OnRendering` 中高频日志（虽然做了 10 秒节流）和 `PositionWindow` 中的每次定位日志会持续增长文件。建议增加日志轮转（按大小或日期切割）或日志级别控制。

---

### O8. `MainWindow.WndProc` 中 `WM_DISPLAYCHANGE` 隐式委托转换

```csharp
if (msg == Win32.WM_DISPLAYCHANGE) Dispatcher.BeginInvoke(RefreshLayout);
```

此处 `RefreshLayout` 作为方法组隐式转换为 `Action`，但 `BeginInvoke` 的重载参数类型是 `Delegate`，编译器需要推断。建议显式写为 `Dispatcher.BeginInvoke(new Action(RefreshLayout))` 以提高可读性和编译确定性。

---

### O9. `MainWindow.XAML` 中 `DockShell.VerticalAlignment` 被 C# 代码覆盖

XAML 中设置 `VerticalAlignment="Bottom"`，但 `ApplyMagnifyAnchor()` 中设为 `VerticalAlignment.Stretch`，XAML 中的初始值无意义，会误导阅读者。

---

### O10. `Installer.WriteFileWithRetry` 最后一次写入未在 try-catch 内

```csharp
private static void WriteFileWithRetry(string path, byte[] data)
{
    for (int attempt = 0; attempt < 5; attempt++)
    {
        try { File.WriteAllBytes(path, data); return; }
        catch (IOException) { Thread.Sleep(300); }
        catch (UnauthorizedAccessException) { Thread.Sleep(300); }
    }
    File.WriteAllBytes(path, data);  // ← 第 6 次若失败，异常向上抛
}
```

第 6 次（循环外）的 `WriteAllBytes` 不在 try-catch 内，会直接抛 `IOException`/`UnauthorizedAccessException`。虽然被 `Install` 方法的外层 catch 捕获，但异常类型和消息可能与预期不同。

---

### O11. 安装器静默安装模式不创建快捷方式

`Installer.Install` 的 `createShortcut` 参数在静默安装模式下固定传 `false`（`Program.Main` 中 `Installer.Install(dir, false, out error, null, !noReg)`）。命令行无法指定是否创建桌面快捷方式。

---

### O12. `promo/` 目录包含大量测试脚本和抓取数据

`promo/` 目录下有约 15 个 PowerShell 脚本（`baidu-round.ps1`, `baiduimg-debug.ps1` 等）和竞品分析 HTML 页面，属于营销调研产物而非项目代码。建议移出代码仓库或加入 `.gitignore`，避免增大仓库体积。

---

## 七、文件清单与审查覆盖

| 文件 | 行数 | 状态 |
|------|------|------|
| `MacDock/App.xaml.cs` | 79 | ✅ 已审查 |
| `MacDock/App.xaml` | 7 | ✅ 已审查 |
| `MacDock/AssemblyInfo.cs` | 10 | ✅ 已审查 |
| `MacDock/MacDock.csproj` | 18 | ✅ 已审查 |
| `MacDock/MainWindow.xaml.cs` | 1451 | ✅ 已审查 |
| `MacDock/MainWindow.xaml` | 45 | ✅ 已审查 |
| `MacDock/FolderPanelWindow.cs` | 533 | ✅ 已审查 |
| `MacDock/SettingsWindow.xaml.cs` | 584 | ✅ 已审查 |
| `MacDock/SettingsWindow.xaml` | 189 | ✅ 已审查 |
| `MacDock/TextInputDialog.cs` | 48 | ✅ 已审查 |
| `MacDock/Models/AppSettings.cs` | 95 | ✅ 已审查 |
| `MacDock/Models/DockItemModel.cs` | 21 | ✅ 已审查 |
| `MacDock/Native/Win32.cs` | 409 | ✅ 已审查 |
| `MacDock/Services/BuiltinFolderScanner.cs` | 44 | ✅ 已审查 |
| `MacDock/Services/DesktopIconsService.cs` | 89 | ✅ 已审查 |
| `MacDock/Services/HotkeyService.cs` | 95 | ✅ 已审查 |
| `MacDock/Services/IconConverter.cs` | 45 | ✅ 已审查 |
| `MacDock/Services/IconPresets.cs` | 179 | ✅ 已审查 |
| `MacDock/Services/IconService.cs` | 204 | ✅ 已审查 |
| `MacDock/Services/PathResolver.cs` | 79 | ✅ 已审查 |
| `MacDock/Services/ProcessService.cs` | 118 | ✅ 已审查 |
| `MacDock/Services/SettingsService.cs` | 104 | ✅ 已审查 |
| `installer/Installer.cs` | 377 | ✅ 已审查 |
| `installer/SetupForm.cs` | 286 | ✅ 已审查 |
| `installer/Payload.cs` | ~极大 | ⚠️ 自动生成，未详审 |
| `installer/build-installer.ps1` | 67 | ✅ 已审查 |
| `NuGet.Config` | 10 | ✅ 已审查 |

---

## 八、优先级建议

### 立即修复（影响功能正确性）
1. **B1** — Process 对象释放（句柄泄漏，长时间运行必现）
2. **B4** — OnDefaultClick 遗漏 BlockShowWhenCovered（功能缺陷）
3. **B8** — F3 快捷键与设置窗口 `_work` 不同步（状态不一致）
4. **B5** — DesktopIconsService 句柄缓存（Explorer 重启后失效）

### 短期修复（影响构建和健壮性）
5. **B3** — NuGet.Config 硬编码路径（其他机器无法构建）
6. **B2** — IconService 缓存无上限（内存泄漏）
7. **B6** — SettingsWindow 初始化副作用

### 中期清理（代码质量）
8. **D1–D4** — 清理死代码（~200+ 行无用代码）
9. **O1–O2** — 消除重复代码
10. **O3–O4** — 性能优化

### 长期改进
11. **O5** — 配置版本管理
12. **O7** — 日志轮转
13. **O12** — promo 目录整理

---

*本报告仅做检查，未修改任何代码。*
