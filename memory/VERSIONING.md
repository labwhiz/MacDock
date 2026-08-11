# 版本号约定

## 下次新版本：v1.3

下次制作新版本安装包时，版本号改为 **v1.3**，需同步修改两处：

1. `MacDock/MacDock.csproj` → `<Version>1.3.0</Version>`
2. `installer/Installer.cs` → `AppInfo.Version = "1.3.0"`

打包后回滚自动生成的 `installer/Payload.cs`，安装包输出：
- `installer/MacDock-Setup.exe`
- 同步副本 `publish/MacDock-Setup.exe`

## 历史备注

- 2026-08-11：曾尝试升到 1.1.0 并给安装器加版本资源，用户取消；指定下次直接使用 v1.3。
- csc（.NET Framework 4.0）不支持 `/win32version` 选项，无法直接设置安装器 exe 的文件版本资源。
