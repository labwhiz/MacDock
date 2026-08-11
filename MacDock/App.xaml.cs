﻿using System;
using System.Threading;
using System.Windows;
using MacDock.Services;

namespace MacDock;

public partial class App : Application
{
    private Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Log("OnStartup begin");

        bool createdNew;
        _mutex = new Mutex(true, "MacDock_SingleInstance_7E3A9B1F", out createdNew);
        if (!createdNew)
        {
            MessageBox.Show("MacDock 已在运行，请查看系统托盘。", "MacDock", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            try { Log("Unhandled EX: " + args.Exception); } catch { }
            MessageBox.Show($"发生未处理的错误：\n{args.Exception.Message}", "MacDock", MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };

        MainWindow main;
        try
        {
            main = new MainWindow();
            Log("MainWindow ctor OK");
        }
        catch (Exception ex)
        {
            Log("MainWindow ctor EX: " + ex);
            Shutdown(1);
            return;
        }
        try
        {
            main.Show();
            Log("Show called OK");
        }
        catch (Exception ex)
        {
            Log("Show EX: " + ex);
        }
    }

    private static void Log(string msg) => CommonUtils.Log("[app] " + msg);

    protected override void OnExit(ExitEventArgs e)
    {
        try { _mutex?.ReleaseMutex(); } catch (Exception) { }
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
