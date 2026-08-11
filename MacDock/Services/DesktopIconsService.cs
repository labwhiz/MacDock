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
    private bool? _lastVisible; // 最近一次设置/检测到的状态，避免多显示器部分可见时误判（2.6）
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
        // 多显示器下各 ListView 可能状态不一致，以最近一次操作/检测结果为准，保证切换行为符合预期
        if (_lastVisible.HasValue) return _lastVisible.Value;
        return QueryLiveVisible();
    }

    private bool QueryLiveVisible()
    {
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
        _lastVisible = visible;
        int cmd = visible ? Win32.SW_SHOW : Win32.SW_HIDE;
        foreach (var lv in _listViews)
        {
            if (lv != IntPtr.Zero) Win32.ShowWindow(lv, cmd);
        }
    }

    private void EnsureHooked()
    {
        // 验证缓存句柄是否仍然有效（Explorer 重启后旧句柄会失效）
        if (_hooked)
        {
            bool allValid = _listViews.Count > 0;
            foreach (var lv in _listViews)
            {
                if (lv == IntPtr.Zero || !Win32.IsWindow(lv))
                {
                    allValid = false;
                    break;
                }
            }
            if (allValid) return;

            // 句柄已失效（如 Explorer 重启），重新枚举
            _listViews.Clear();
            _lastVisible = null;
            _hooked = false;
        }

        if (_hooked) return;
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
