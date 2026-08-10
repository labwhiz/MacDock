using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using MacDock.Native;

namespace MacDock.Services;

/// <summary>通过系统 API 显示/隐藏桌面图标。</summary>
public class DesktopIconsService
{
    private readonly List<IntPtr> _listViews = new();
    private bool _hooked;
    private EnumWindowsProc? _enumWindowsProc;
    private EnumChildProc? _enumChildProc;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    private delegate bool EnumChildProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr hWndParent, EnumChildProc lpEnumFunc, IntPtr lParam);

    public bool AreIconsVisible()
    {
        EnsureHooked();
        foreach (var lv in _listViews)
        {
            if (lv != IntPtr.Zero && Win32.IsWindowVisible(lv)) return true;
        }
        return false;
    }

    /// <summary>切换桌面图标可见性，返回新状态（true=可见）。</summary>
    public bool ToggleDesktopIcons()
    {
        bool visible = AreIconsVisible();
        SetIconsVisible(!visible);
        return !visible;
    }

    public void SetIconsVisible(bool visible)
    {
        EnsureHooked();
        int cmd = visible ? Win32.SW_SHOW : Win32.SW_HIDE;
        foreach (var lv in _listViews)
        {
            if (lv != IntPtr.Zero) Win32.ShowWindow(lv, cmd);
        }
    }

    private void EnsureHooked()
    {
        if (_hooked) return;
        _listViews.Clear();
        _enumWindowsProc = OnEnumWindows;
        _enumChildProc = OnEnumChild;
        EnumWindows(_enumWindowsProc, IntPtr.Zero);
        _hooked = true;
    }

    private bool OnEnumWindows(IntPtr hWnd, IntPtr lParam)
    {
        var sb = new StringBuilder(256);
        Win32.GetClassName(hWnd, sb, sb.Capacity);
        var cls = sb.ToString();
        if (cls == "Progman" || cls == "WorkerW")
        {
            var defView = Win32.FindWindowEx(hWnd, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (defView != IntPtr.Zero)
            {
                // 该视图下所有 SysListView32（多显示器时每屏一个）
                EnumChildWindows(defView, _enumChildProc!, IntPtr.Zero);
            }
        }
        return true;
    }

    private bool OnEnumChild(IntPtr hWnd, IntPtr lParam)
    {
        var sb = new StringBuilder(256);
        Win32.GetClassName(hWnd, sb, sb.Capacity);
        if (sb.ToString() == "SysListView32")
            _listViews.Add(hWnd);
        return true;
    }
}
