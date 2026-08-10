# MacDock 安装包（MacDock-Setup.exe）

轻量级 Win11 桌面 Dock 栏美化软件的安装程序。

## 使用方法
双击 `MacDock-Setup.exe`，在界面中：
- 选择安装位置（默认 `%LOCALAPPDATA%\Programs\MacDock`，可改为任意有写权限的目录，如 `D:\MacDock`）
- 勾选是否创建桌面快捷方式、是否安装后立即启动
- 点击「安装」完成；安装完成后可通过「设置 → 卸载」或系统「应用 → 已安装的应用」中卸载

## 命令行参数（静默安装/卸载）
```powershell
# 静默安装到指定目录（不创建桌面快捷方式、不写注册表卸载项）
MacDock-Setup.exe /install D:\MacDock

# 静默卸载（显式指定目录；省略目录时从注册表读取）
MacDock-Setup.exe /uninstall D:\MacDock
```

## 环境要求
- Windows 10/11
- .NET 8 桌面运行时（Microsoft.WindowsDesktop.App 8.x）——运行 MacDock 本身所需
- 安装程序为 .NET Framework 4.8 单文件，Windows 10/11 自带，无需额外安装

## 说明
- 按用户级安装，不需要管理员权限，注册表卸载项写入当前用户的 HKCU
- 安装器会把自身复制到安装目录的 `MacDock-Uninstall.exe`，卸载时通过它清理
- 应用数据保存在 `%APPDATA%\MacDock`，卸载时保留（避免误删个人配置）

## 重新打包
MacDock 源码更新后，运行 `build-installer.ps1` 即可自动完成：
发布 → 生成内嵌负载 `Payload.cs` → 用内置 csc 编译新的 `MacDock-Setup.exe`。

## 源码文件
- `Installer.cs`  — 安装/卸载逻辑、注册表、快捷方式、命令行入口
- `SetupForm.cs`  — WinForms 安装界面
- `Payload.cs`    — 内嵌的 MacDock 发布产物（自动生成，勿手改）
- `build-installer.ps1` — 一键打包脚本
