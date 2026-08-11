﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MacDockSetup
{
    internal static class AppInfo
    {
        public const string Name = "MacDock";
        public const string DisplayName = "MacDock 桌面 Dock";
        public const string Version = "1.0.0";
        public const string Publisher = "MacDock";
        public const string UninstallSubKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\MacDock";
        public const string RunValueName = "MacDock";
        public const string UninstallerFileName = "MacDock-Uninstall.exe";
    }

    internal static class Installer
    {
        public static string DefaultInstallDir()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(local, "Programs", AppInfo.Name);
        }

        public static string NormalizePath(string target, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(target)) { error = "安装位置不能为空。"; return null; }
            target = target.Trim();
            if (target.IndexOfAny(Path.GetInvalidPathChars()) >= 0) { error = "安装位置包含非法字符。"; return null; }
            string full;
            try { full = Path.GetFullPath(target); }
            catch (Exception ex) { error = "安装位置无效：" + ex.Message; return null; }
            if (full.Length < 3 || full[1] != ':' || full[2] != '\\') { error = "请填写完整的磁盘路径（如 D:\\MacDock）。"; return null; }
            if (string.Equals(full, Path.GetPathRoot(full), StringComparison.OrdinalIgnoreCase)) { error = "不能直接安装到磁盘根目录。"; return null; }
            return full;
        }

        public static bool Install(string targetDir, bool createShortcut, out string error, Action<string, int> progress, bool writeRegistry)
        {
            error = null;
            try
            {
                string dir = NormalizePath(targetDir, out error);
                if (dir == null) return false;

                if (progress != null) progress("正在准备目录…", 0);
                Directory.CreateDirectory(dir);
                StopMacDock(dir);

                long total = 0;
                foreach (byte[] b in PayloadFiles.Data) total += b.Length;
                long done = 0;
                for (int i = 0; i < PayloadFiles.Names.Length; i++)
                {
                    string path = Path.Combine(dir, PayloadFiles.Names[i]);
                    WriteFileWithRetry(path, PayloadFiles.Data[i]);
                    done += PayloadFiles.Data[i].Length;
                    if (progress != null) progress("正在写入 " + PayloadFiles.Names[i] + " …", (int)(done * 100 / Math.Max(1, total)));
                }

                try
                {
                    string self = Application.ExecutablePath;
                    string dest = Path.Combine(dir, AppInfo.UninstallerFileName);
                    if (!string.Equals(Path.GetFullPath(self), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
                    {
                        File.Copy(self, dest, true);
                    }
                }
                catch { }

                if (createShortcut)
                {
                    if (progress != null) progress("正在创建桌面快捷方式…", 90);
                    CreateDesktopShortcut(dir);
                }
                if (writeRegistry)
                {
                    if (progress != null) progress("正在写入卸载信息…", 95);
                    WriteUninstallRegistry(dir);
                }
                if (progress != null) progress("安装完成。", 100);
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                error = "没有权限写入该目录，请选择有写权限的位置（如 D:\\MacDock）。";
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static bool Uninstall(string targetDir, out string error, bool cleanRegistry)
        {
            error = null;
            try
            {
                string dir = targetDir;
                if (string.IsNullOrWhiteSpace(dir))
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(AppInfo.UninstallSubKey))
                    {
                        if (key != null) dir = key.GetValue("InstallLocation") as string;
                    }
                }
                if (string.IsNullOrWhiteSpace(dir)) { error = "未找到已安装的 MacDock。"; return false; }
                dir = Path.GetFullPath(dir);

                StopMacDock(dir);

                try
                {
                    string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                    string lnk = Path.Combine(desktop, "MacDock.lnk");
                    if (File.Exists(lnk)) File.Delete(lnk);
                }
                catch { }

                try
                {
                    using (RegistryKey run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                    {
                        if (run != null) run.DeleteValue(AppInfo.RunValueName, false);
                    }
                    Registry.CurrentUser.DeleteSubKeyTree(AppInfo.UninstallSubKey, false);
                }
                catch { }

                string[] known = new string[]
                {
                    "MacDock.exe", "MacDock.dll", "MacDock.deps.json", "MacDock.runtimeconfig.json",
                    "MacDock.pdb", AppInfo.UninstallerFileName
                };
                foreach (string f in known)
                {
                    string p = Path.Combine(dir, f);
                    try { if (File.Exists(p)) File.Delete(p); } catch { }
                }

                try
                {
                    if (Directory.Exists(dir) && Directory.GetFileSystemEntries(dir).Length == 0)
                        Directory.Delete(dir, false);
                }
                catch { }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static bool IsInstalled(out string installDir)
        {
            installDir = null;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(AppInfo.UninstallSubKey))
                {
                    if (key == null) return false;
                    installDir = key.GetValue("InstallLocation") as string;
                    return !string.IsNullOrWhiteSpace(installDir);
                }
            }
            catch { return false; }
        }

        public static bool DotNet8DesktopRuntimePresent()
        {
            try
            {
                foreach (string arch in new string[] { "x64", "x86" })
                {
                    string keyPath = @"SOFTWARE\dotnet\Setup\InstalledVersions\" + arch + @"\sharedfx\Microsoft.WindowsDesktop.App";
                    using (RegistryKey key = Registry.LocalMachine.OpenSubKey(keyPath))
                    {
                        if (key == null) continue;
                        foreach (string name in key.GetValueNames())
                        {
                            string v = (key.GetValue(name) ?? "").ToString();
                            if (v.StartsWith("8.")) return true;
                        }
                    }
                }
                // 32-bit registry view fallback
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App"))
                {
                    if (key != null)
                    {
                        foreach (string name in key.GetValueNames())
                        {
                            string v = (key.GetValue(name) ?? "").ToString();
                            if (v.StartsWith("8.")) return true;
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        private static void StopMacDock(string installDir)
        {
            try
            {
                Process[] procs = Process.GetProcessesByName("MacDock");
                try
                {
                    foreach (Process p in procs)
                    {
                        try
                        {
                            string exePath = p.MainModule != null ? p.MainModule.FileName : null;
                            if (!string.IsNullOrEmpty(exePath) &&
                                string.Equals(Path.GetDirectoryName(exePath), installDir, StringComparison.OrdinalIgnoreCase))
                            {
                                try { p.Kill(); } catch { }
                                p.WaitForExit(3000);
                            }
                        }
                        catch { }
                    }
                }
                finally
                {
                    foreach (Process p in procs) p.Dispose();
                }
            }
            catch { }
        }

        private static void WriteFileWithRetry(string path, byte[] data)
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try { File.WriteAllBytes(path, data); return; }
                catch (IOException) { Thread.Sleep(300); }
                catch (UnauthorizedAccessException) { Thread.Sleep(300); }
            }
            File.WriteAllBytes(path, data);
        }

        private static void CreateDesktopShortcut(string installDir)
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrEmpty(desktop)) desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string lnk = Path.Combine(desktop, "MacDock.lnk");
            string exe = Path.Combine(installDir, "MacDock.exe");
            CreateLnk(lnk, exe, installDir, exe, AppInfo.DisplayName);
        }

        private static void CreateLnk(string lnkPath, string targetPath, string workingDir, string iconPath, string description)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) throw new InvalidOperationException("无法创建快捷方式：缺少 WScript.Shell。");
            object shell = Activator.CreateInstance(shellType);
            try
            {
                object lnk = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
                try
                {
                    Type t = lnk.GetType();
                    t.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { targetPath });
                    t.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { workingDir });
                    t.InvokeMember("IconLocation", BindingFlags.SetProperty, null, lnk, new object[] { iconPath + ",0" });
                    t.InvokeMember("Description", BindingFlags.SetProperty, null, lnk, new object[] { description });
                    t.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
                }
                finally
                {
                    Marshal.FinalReleaseComObject(lnk);
                }
            }
            finally
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }

        private static void WriteUninstallRegistry(string installDir)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(AppInfo.UninstallSubKey))
            {
                if (key == null) throw new InvalidOperationException("无法写入注册表卸载信息。");
                key.SetValue("DisplayName", AppInfo.DisplayName);
                key.SetValue("DisplayVersion", AppInfo.Version);
                key.SetValue("Publisher", AppInfo.Publisher);
                key.SetValue("InstallLocation", installDir);
                string exe = Path.Combine(installDir, "MacDock.exe");
                key.SetValue("DisplayIcon", exe);
                string uninstaller = Path.Combine(installDir, AppInfo.UninstallerFileName);
                key.SetValue("UninstallString", "\"" + uninstaller + "\" /uninstall");
                key.SetValue("QuietUninstallString", "\"" + uninstaller + "\" /uninstall");
                key.SetValue("NoModify", 1);
                key.SetValue("NoRepair", 1);
                long size = 0;
                foreach (byte[] b in PayloadFiles.Data) size += b.Length;
                key.SetValue("EstimatedSize", (int)(size / 1024));
            }
        }
    }

    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            string mode = null;
            string headlessDir = null;
            foreach (string a in args)
            {
                string low = a.ToLowerInvariant();
                if (low == "/install" || low == "-install" || low == "--install") mode = "install";
                else if (low == "/uninstall" || low == "-uninstall" || low == "--uninstall") mode = "uninstall";
                else if (mode != null && headlessDir == null) headlessDir = a;
            }

            if (mode == "install")
            {
                string dir = string.IsNullOrWhiteSpace(headlessDir) ? Installer.DefaultInstallDir() : headlessDir;
                string error;
                bool noReg = Environment.GetEnvironmentVariable("MACDOCK_SETUP_NOREG") == "1";
                bool ok = Installer.Install(dir, false, out error, null, !noReg);
                Report("install " + dir + " -> " + (ok ? "OK" : "FAIL: " + error));
                return ok ? 0 : 1;
            }
            if (mode == "uninstall")
            {
                string error2;
                bool noReg2 = Environment.GetEnvironmentVariable("MACDOCK_SETUP_NOREG") == "1";
                bool ok = Installer.Uninstall(string.IsNullOrWhiteSpace(headlessDir) ? null : headlessDir, out error2, !noReg2);
                Report("uninstall -> " + (ok ? "OK" : "FAIL: " + error2));
                return ok ? 0 : 1;
            }

            bool createdNew;
            using (Mutex mutex = new Mutex(true, "MacDockSetup_SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show("安装程序已在运行。", AppInfo.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new SetupForm());
            }
            return 0;
        }

        private static string LogPath()
        {
            string log = Environment.GetEnvironmentVariable("MACDOCK_SETUP_LOG");
            if (string.IsNullOrEmpty(log)) log = Path.Combine(Path.GetTempPath(), "MacDockSetup.log");
            return log;
        }

        private static void Report(string text)
        {
            try { Console.WriteLine(text); } catch { }
            try
            {
                File.AppendAllText(
                    LogPath(),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + text + Environment.NewLine);
            }
            catch { }
        }
    }
}
