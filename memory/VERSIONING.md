# 版本号约定

## 已发布：v1.3（2026-08-12）

v1.3 已由用户发布至 GitHub（未含安全补丁）；v1.3.1 为本轮安全补丁版本，版本号同步修改的两处：

1. `MacDock/MacDock.csproj` → `<Version>1.3.1</Version>`
2. `installer/Installer.cs` → `AppInfo.Version = "1.3.1"`

打包后回滚自动生成的 `installer/Payload.cs`，安装包输出：
- `installer/MacDock-Setup.exe`
- 同步副本 `publish/MacDock-Setup.exe`

## 下次新版本：待定

下次制作新版本安装包时，在以上两处修改版本号（沿用 v1.3 的打包流程）。

## 历史备注

- 2026-08-12：v1.3 发布（基础版本）；v1.3.1 安全补丁版本——依据代码审查报告修复 42 项问题（详见 CHANGELOG.md）。
- 2026-08-11：曾尝试升到 1.1.0 并给安装器加版本资源，用户取消；指定下次直接使用 v1.3。
- csc（.NET Framework 4.0）不支持 `/win32version` 选项，无法直接设置安装器 exe 的文件版本资源。
