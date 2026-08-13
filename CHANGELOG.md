# 更新日志（CHANGELOG）

## v1.4.0（2026-08-13）· 不再依赖 .NET 8 运行时

- 主程序目标框架由 .NET 8 改为 .NET Framework 4.8（Windows 10/11 内置），安装与运行不再需要安装 .NET 8 桌面运行时
- 安装器运行时检测由 .NET 8 桌面运行时改为 .NET Framework 4.8，未检测到时才提示
- 打包脚本、安装/卸载逻辑适配 net48 发布产物（无 dll/deps/runtimeconfig，新增 System.Text.Json 依赖）

## v1.3.1（2026-08-12）— 安全补丁版本

> v1.3 已于 2026-08-12 发布；本版为安全补丁版本，含 7.1–7.3 安全加固及《代码审查报告》全部 42 项修复（主程序 Release 构建通过、安装器编译通过）。

本轮依据《代码审查报告》逐条修复 **42 项**问题。

### 功能与 BUG 修复
- 修复「恢复默认」遗漏重置文件夹面板间距（`FolderPanelGap`），并改为整体替换默认值，杜绝逐字段遗漏（2.9 / 5.4）
- 修复窗口退出时序：主窗口关闭时排除自身计数，应用改用 `OnLastWindowClose`，避免托盘模式进程残留（2.2 / 8.3）
- 修复单实例 Mutex 异常路径：未持有锁时直接释放，退出时不再多余 `ReleaseMutex`（2.1 / 3.7）
- 修复 `MainWindow` 构造失败后进程残留：失败即 `Shutdown(1)`（3.6）
- 修复卸载器 `cleanRegistry` 参数未生效：传 `false` 时不再清理注册表（4.9 / 9.1）
- 修复拖拽排序后状态重置时序、桌面图标多显示器可见性误判、工作区获取异常未记录（2.6 / 2.7 / 2.8）
- 运行状态判定统一收敛到 `ProcessService.IsRunning(targetPath, runningSet)`，消除与 `OnRunningTick` 的重复逻辑（2.4）

### 安全加固
- 命令解释器（cmd / powershell / pwsh / wscript / cscript）启动参数额外过滤管道、重定向等元字符（7.1）
- `shell:` URI 白名单不再允许反斜杠（7.2）
- 路径校验改用 Windows 非法字符集（控制字符、`"`、`<`、`>`、`|` 等）（7.3）

### 性能优化
- 窗口遮挡检测：顶级窗口枚举加短 TTL 缓存，不再每次轮询全量 `EnumWindows`（3.3）
- 放大计算仅在鼠标靠近 Dock 时执行逐项命中检测（3.2）
- 图标缓存由"满 256 清空"改为 LRU 淘汰最近最少使用（6.2）
- 日志文件大小改为缓存累计，不再每次写入查询文件系统（6.3）
- 运行进程轮询间隔 1.8s 调至 3s（6.1）
- 启动阶段与显示器变化时的布局刷新去重（6.4）

### 清理与质量
- 清理 8 处死代码：未使用常量/API/方法/属性，以及空文件 `NuGet/Migrations/1`（4.1–4.8 / 4.10）
- 滑块事件绑定样板提取为 `HookSlider`（5.3）；`pad=10` 提取为 `ShellPadding` 常量（2.5 / 5.2）
- `DockItemIconConverter` 移入 `MacDock.Converters` 命名空间（5.7）；`Process` 使用风格统一（5.6）
- 配置损坏时记录日志并备份为 `settings.json.bad`，避免配置无感知丢失（3.5）
- csproj 补发布优化设置（PDB 内嵌、明确关闭裁剪等）（8.1）
- 安装器：.NET 运行时检查兼容 8+；`WriteFileWithRetry` 重试耗尽后统一抛错；`csc` 路径动态查找（9.2 / 9.4 / 9.5）

### 安装包
- 版本号升级至 **v1.3.1**（`MacDock.csproj` 与安装器 `AppInfo.Version`）；安装包由用户自行发布
- 安装包输出：`installer/MacDock-Setup.exe`（同步副本 `publish/MacDock-Setup.exe`）

### 备注（未改动项）
- 3.4（任务栏锁定轮询）、5.1（拆分 MainWindow）、8.2（WPF+WinForms，合理选择）、9.3（可选优化）维持现状，详见审查报告说明。

## v1.3（2026-08-12）

v1.3 基础版本（未含安全补丁），由用户发布至 GitHub。v1.3.1 起作为安全补丁版本承接后续修复。