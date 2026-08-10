using System;
using MacDock.Native;

namespace MacDock.Services;

/// <summary>全局快捷键注册（默认 F2 隐藏桌面图标）。</summary>
public class HotkeyService : IDisposable
{
    private readonly int _hotkeyId;
    private IntPtr _hwnd = IntPtr.Zero;

    public event Action? HotkeyPressed;

    public HotkeyService(int hotkeyId = 0x4D43) // "MC"
    {
        _hotkeyId = hotkeyId;
    }

    public bool IsRegistered { get; private set; }

    public void Register(IntPtr hwnd, string modifier, string key)
    {
        Unregister();
        _hwnd = hwnd;
        uint flags = ModifierToFlags(modifier) | Win32.MOD_NOREPEAT;
        if (!Win32.RegisterHotKey(hwnd, _hotkeyId, flags, KeyToVk(key)))
        {
            IsRegistered = false;
        }
        else
        {
            IsRegistered = true;
        }
    }

    public void Unregister()
    {
        if (IsRegistered && _hwnd != IntPtr.Zero)
        {
            Win32.UnregisterHotKey(_hwnd, _hotkeyId);
        }
        IsRegistered = false;
        _hwnd = IntPtr.Zero;
    }

    /// <summary>处理 WM_HOTKEY，返回是否消费。</summary>
    public bool OnWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == Win32.WM_HOTKEY && wParam.ToInt32() == _hotkeyId)
        {
            HotkeyPressed?.Invoke();
            return true;
        }
        return false;
    }

    public static uint ModifierToFlags(string modifier) => modifier switch
    {
        "Ctrl" => Win32.MOD_CONTROL,
        "Alt" => Win32.MOD_ALT,
        "Shift" => Win32.MOD_SHIFT,
        "Win" => Win32.MOD_WIN,
        _ => 0,
    };

    public static uint KeyToVk(string key)
    {
        if (string.IsNullOrEmpty(key)) return 0;
        var k = key.Trim().ToUpperInvariant();
        if (k.Length == 1 && k[0] is >= 'A' and <= 'Z') return (uint)k[0];
        if (k.Length == 1 && k[0] is >= '0' and <= '9') return (uint)k[0];
        if (k.StartsWith('F') && k.Length > 1 && int.TryParse(k[1..], out var n) && n is >= 1 and <= 24)
            return (uint)(0x6F + n);
        return k switch
        {
            "SPACE" => 0x20,
            "ENTER" or "RETURN" => 0x0D,
            "ESC" or "ESCAPE" => 0x1B,
            "TAB" => 0x09,
            "DEL" or "DELETE" => 0x2E,
            "HOME" => 0x24,
            "END" => 0x23,
            "PGUP" => 0x21,
            "PGDN" => 0x22,
            "LEFT" => 0x25,
            "RIGHT" => 0x27,
            "UP" => 0x26,
            "DOWN" => 0x28,
            _ => 0,
        };
    }

    public void Dispose() => Unregister();
}

