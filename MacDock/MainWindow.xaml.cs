using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using MacDock.Models;
using MacDock.Native;
using MacDock.Services;
using Microsoft.Win32;
using Wf = System.Windows.Forms;
namespace MacDock;

public partial class MainWindow : Window
{
    // ---------- 原生互操作 ----------
    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public Win32.RECT rcMonitor;
        public Win32.RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(Win32.POINT pt, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    private const int DWMWA_CLOAKED = 14;
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const double SlideSeconds = 0.18;

    private readonly SettingsService _settingsService = new();
    private readonly HotkeyService _hotkey = new();                    // F2：隐藏桌面图标
    private readonly HotkeyService _blockHotkey = new(0x4D44); // "MD"：F3 开关“被覆盖禁止唤出”
    private readonly DesktopIconsService _desktopIcons = new();

    private AppSettings _settings = null!;
    private readonly List<Grid> _itemRoots = new();
    private readonly List<ScaleTransform> _scales = new();
    private readonly List<double> _scaleCurrent = new();
    private readonly List<double> _scaleTarget = new();

    private IntPtr _hwnd;
    private HwndSource? _hwndSource;
    private Wf.NotifyIcon? _tray;
    private SettingsWindow? _settingsWindow;

    private readonly DispatcherTimer _pollTimer = new() { Interval = TimeSpan.FromMilliseconds(380) };
    private readonly DispatcherTimer _runningTimer = new() { Interval = TimeSpan.FromMilliseconds(1800) };

    private bool _renderHooked;
    private bool _dockVisible = true;
    private bool _mouseOverDock;
    private double _slideY;
    private double _slideTargetY;
    private double _opacity = 1;
    private double _opacityTarget = 1;

    private double _dpiScale = 1.0;
    private double _baseSize = 48;
    private double _lastIconSize = -1;
    private double _spacing = 8;
    private double _barHeight;
    private double _barWidth;
    private double _winHeight;  // 窗口总高度 = 背景栏高度
    private double _dockEdge;   // 屏幕坐标（DIP）：底部=Dock 底边；顶部=Dock 顶边
    private double _anchorLeft;

    // 拖拽排序
    private int _dragIndex = -1;
    private Point _dragStart;
    private bool _dragging;
    private bool _clickMoved;

    // 文件夹弹出面板
    private FolderPanelWindow? _folderPanel;
    private DockItemModel? _folderPanelItem;
    private IntPtr _folderPanelHwnd;
    private IntPtr _mouseHook;
    private Win32.LowLevelMouseProc? _mouseHookProc;

    private bool _quitting;
    private DateTime _startupGraceUntil = DateTime.UtcNow;
    private DateTime _lastShowUtc = DateTime.MinValue;
    private DateTime _nextCoverLogUtc = DateTime.MinValue;
    private DateTime _nextRenderErrLogUtc = DateTime.MinValue;
    private static void Log(string msg)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} {msg}\r\n";
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MacDock");
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "debug.log"), line);
                return;
            }
            catch
            {
                System.Threading.Thread.Sleep(30);
            }
        }
    }

    public MainWindow()
    {
        InitializeComponent();
        InstallHitOverlay();
        Log("ctor begin");
        _settings = _settingsService.Load();
        ApplyRuntimeSettings(_settings, rebuild: true);
        Log($"ctor after settings, items={_settings.Items.Count}");
        BuildTray();
        Log("ctor after tray");
        CompositionTarget.Rendering += OnRendering;
        _renderHooked = true;

        _pollTimer.Tick += OnPoll;
        _pollTimer.Start();
        _runningTimer.Tick += OnRunningTick;
        _runningTimer.Start();
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        Loaded += OnLoaded;
        Closing += OnClosing;
        MouseLeftButtonDown += OnWindowMouseLeftButtonDown;
    }

    // 分层窗口的透明像素在系统层可能被当作穿透；在 Dock 表面铺一层几乎不可见
    // 命中层，保证空白处右键/左键也能命中 Dock 本身（视觉上无影响）。
    private void InstallHitOverlay()
    {
        var overlay = new Grid
        {
            Background = new SolidColorBrush(Color.FromArgb(2, 0, 0, 0)),
            IsHitTestVisible = true,
        };
        var host = new Grid();
        host.Children.Add(overlay);
        if (DockShell.Child is UIElement oldChild)
        {
            DockShell.Child = null;
            host.Children.Add(oldChild);
        }
        DockShell.Child = host;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e) => RefreshLayout();

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _quitting = true;
        try { CloseFolderPanel(); } catch (Exception) { }
        _pollTimer.Stop();
        _runningTimer.Stop();
        if (_renderHooked)
        {
            CompositionTarget.Rendering -= OnRendering;
            _renderHooked = false;
        }
        _hotkey.Dispose();
        _blockHotkey.Dispose();
        _tray?.Dispose();
        try { SaveSettings(); } catch (Exception) { }
    }

    // ================= 窗口初始化 =================

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        try
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            _hwndSource = HwndSource.FromHwnd(_hwnd);
            _hwndSource?.AddHook(WndProc);

            var src = PresentationSource.FromVisual(this);
            _dpiScale = src?.CompositionTarget.TransformToDevice.M11 ?? 1.0;
            Log($"OnSourceInitialized hwnd={_hwnd} dpi={_dpiScale}");
            _startupGraceUntil = DateTime.UtcNow.AddSeconds(2.5);

            ApplyBackground();
            RefreshLayout();
            RegisterHotkey();
            Log("OnSourceInitialized done");
        }
        catch (Exception ex)
        {
            Log("OnSourceInitialized EX: " + ex);
        }
    }

    private void ApplyBackground()
    {
        var color = ParseHexColor(_settings.BackgroundColor, Color.FromRgb(0x26, 0x26, 0x2E));
        byte alpha = _settings.BackgroundStyle switch
        {
            "Solid" => (byte)255,
            "Transparent" => (byte)0,
            _ => (byte)Math.Round(255 * Math.Clamp(_settings.BackgroundOpacity, 0, 1)),
        };
        DockShell.Background = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
        DockShell.BorderThickness = _settings.ShowBorder ? new Thickness(1) : new Thickness(0);
        DockShell.BorderBrush = new SolidColorBrush(ParseHexColor(_settings.BorderColor, Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)));
        DockShell.CornerRadius = new CornerRadius(Math.Max(0, _settings.CornerRadius));
        // 全部改由 WPF 分层渲染完成：不再调用 DWM 特效 / SetWindowRgn。
        // 避免纯色与毛玻璃出现残留方框不跟随隐藏的问题。
    }

    private static Color ParseHexColor(string? hex, Color fallback)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(hex))
                return (Color)ColorConverter.ConvertFromString(hex);
        }
        catch (Exception) { }
        return fallback;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_hotkey.OnWndProc(hwnd, msg, wParam, lParam)) handled = true;
        if (_blockHotkey.OnWndProc(hwnd, msg, wParam, lParam)) handled = true;
        if (msg == Win32.WM_DISPLAYCHANGE) Dispatcher.BeginInvoke(RefreshLayout);
        return IntPtr.Zero;
    }

    private void RegisterHotkey()
    {
        // 先解绑再绑定，避免多次调用（构造函数 / 窗口初始化 / 每次设置变更）导致事件重复订阅；
        // 重复订阅会让一次按键触发多次切换（隐藏→显示），快捷键看起来完全无效。
        _hotkey.HotkeyPressed -= OnHotkeyPressed;
        _blockHotkey.HotkeyPressed -= OnBlockHotkeyPressed;

        // 窗口句柄未就绪时不注册，避免把热键注册到线程消息队列而收不到 WM_HOTKEY。
        if (_hwnd == IntPtr.Zero) return;

        if (_settings.HotkeyEnabled)
        {
            _hotkey.Register(_hwnd, _settings.HotkeyModifier, _settings.HotkeyKey);
            _hotkey.HotkeyPressed += OnHotkeyPressed;
        }
        else
        {
            _hotkey.Unregister();
        }

        if (_settings.BlockHotkeyEnabled)
        {
            _blockHotkey.Register(_hwnd, _settings.BlockHotkeyModifier, _settings.BlockHotkeyKey);
            _blockHotkey.HotkeyPressed += OnBlockHotkeyPressed;
        }
        else
        {
            _blockHotkey.Unregister();
        }
    }

    private void OnBlockHotkeyPressed()
    {
        _settings.BlockShowWhenCovered = !_settings.BlockShowWhenCovered;
        SaveSettings();
        _settingsWindow?.RefreshBlockMode(_settings.BlockShowWhenCovered);
        ShowToast(_settings.BlockShowWhenCovered ? "已开启：被覆盖时无法唤出 Dock" : "已关闭：被覆盖时仍可从边缘唤出 Dock");
    }

    private void OnHotkeyPressed()
    {
        var nowVisible = _desktopIcons.ToggleDesktopIcons();
        ShowToast(nowVisible ? "桌面图标已显示" : "桌面图标已隐藏");
    }

    // ================= 布局 =================

    private void RefreshLayout()
    {
        if (_hwnd == IntPtr.Zero) return;
        ComputeMetrics();
        PositionWindow(animate: false);
    }

    private void ComputeMetrics()
    {
        _baseSize = _settings.IconSize;
        _spacing = _settings.IconSpacing;
        int count = Math.Max(1, _settings.Items.Count);
        double pad = 10;
        double borderT = _settings.ShowBorder ? 1 : 0;
        // 背景栏固定尺寸：按“放大后的图标”计算，悬停放大时图标始终在背景内，背景本身不动
        double boost = Math.Clamp(_settings.MagnifyBoost, 0.0, 2.0);
        double magSize = _baseSize * (1 + boost);
        double magContentW = count * magSize + Math.Max(0, count - 1) * _spacing;
        _barWidth = Math.Max(magContentW + 2 * (pad + borderT), _settings.BarMinWidth);
        _barHeight = Math.Max(magSize + 2 * (pad + borderT), _settings.BarMinHeight);
        _winHeight = _barHeight;
        DockShell.Width = _barWidth;
        DockShell.Height = _barHeight;
        var pt = new Win32.POINT();
        Win32.GetCursorPos(out pt);
        var mon = GetMonitorInfoOf(pt);
        double scale = _dpiScale;
        bool dockTop = _settings.DockPosition == "TopCenter";
        double margin = 10;

        // Taskbar avoidance: keep the dock above the bottom taskbar popup area (仅底部位置)
        double workLeft = mon.rcWork.Left / scale;
        double workRight = mon.rcWork.Right / scale;
        double workTop = mon.rcWork.Top / scale;
        double workBottom = ComputeWorkBottomPx(mon, dockTop) / scale;
        switch (_settings.DockPosition)
        {
            case "TopCenter":
                _dockEdge = workTop + margin + _settings.DockOffsetY;
                _anchorLeft = (workLeft + workRight) / 2.0 + _settings.DockOffsetX;
                break;
            case "BottomLeft":
                _dockEdge = workBottom - 6 - _settings.DockOffsetY;
                _anchorLeft = workLeft + _barWidth / 2.0 + margin + _settings.DockOffsetX;
                break;
            case "BottomRight":
                _dockEdge = workBottom - 6 - _settings.DockOffsetY;
                _anchorLeft = workRight - _barWidth / 2.0 - margin + _settings.DockOffsetX;
                break;
            default:
                _dockEdge = workBottom - 6 - _settings.DockOffsetY;
                _anchorLeft = (workLeft + workRight) / 2.0 + _settings.DockOffsetX;
                break;
        }
        Log($"metrics barW={_barWidth} barH={_barHeight} winH={_winHeight} dockEdge={_dockEdge} anchorLeft={_anchorLeft} scale={_dpiScale} items={_settings.Items.Count}");
    }

    // 与 ComputeMetrics 共用同一套“工作区底边（含任务栏规避）”计算，避免两者偏差被误判为显示器变化
    private double ComputeWorkBottomPx(MONITORINFO mon, bool dockTop)
    {
        if (dockTop) return mon.rcWork.Bottom;
        double monitorBottomPx = mon.rcMonitor.Bottom;
        double bottomPx = mon.rcWork.Bottom;
        try
        {
            IntPtr tray = Win32.FindWindow("Shell_TrayWnd", null);
            if (tray != IntPtr.Zero && Win32.GetWindowRect(tray, out var trayRect))
            {
                bool onThisMonitor = trayRect.Bottom > mon.rcMonitor.Top && trayRect.Top < mon.rcMonitor.Bottom;
                bool atBottom = trayRect.Bottom >= monitorBottomPx - 8 && trayRect.Top <= monitorBottomPx;
                if (onThisMonitor && atBottom)
                {
                    int trayH = mon.rcMonitor.Bottom - mon.rcWork.Bottom;
                    int maxTrayH = (int)(120 * _dpiScale);
                    if (trayH <= 0 || trayH > maxTrayH)
                    {
                        var barData = new Win32.APPBARDATA
                        {
                            cbSize = Marshal.SizeOf<Win32.APPBARDATA>(),
                            hWnd = tray,
                            uEdge = Win32.ABE_BOTTOM,
                        };
                        Win32.SHAppBarMessage(Win32.ABM_GETTASKBARPOS, ref barData);
                        trayH = barData.rc.Height;
                    }
                    if (trayH <= 0 || trayH > maxTrayH) trayH = (int)(48 * _dpiScale);
                    bottomPx = monitorBottomPx - trayH;
                }
            }
        }
        catch (Exception)
        {
            // Skip this frame until the window is ready.
        }
        return bottomPx;
    }

    private MONITORINFO GetMonitorInfoOf(Win32.POINT pt)
    {
        var mon = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(mon, ref info);
        return info;
    }

    private void PositionWindow(bool animate)
    {
        double w = Math.Round(_barWidth);
        double h = Math.Round(_winHeight);
        double left = Math.Round(_anchorLeft - w / 2.0);
        bool dockTop = _settings.DockPosition == "TopCenter";
        double top = Math.Round(dockTop ? _dockEdge : _dockEdge - h);
        if (!_dockVisible) top = top + _slideTargetY;

        // 仅在变化超过阈值时应用，避免高频重建 SetWindowPos 导致窗口抖动
        if (double.IsNaN(Width) || Math.Abs(Width - w) >= 0.5) Width = w;
        if (double.IsNaN(Height) || Math.Abs(Height - h) >= 0.5) Height = h;
        if (double.IsNaN(Left) || Math.Abs(Left - left) >= 0.5) Left = left;
        if (double.IsNaN(Top) || Math.Abs(Top - top) >= 0.5) Top = top;

        // 仅可见时重置动画状态；隐藏时保持 HideDock 设置的滑出目标，避免反复刷新导致 Dock 闪烁
        if (_dockVisible)
        {
            _slideY = 0;
            _slideTargetY = 0;
            Opacity = 1;
            _opacity = 1;
            _opacityTarget = 1;
        }
        Log($"position w={w} h={h} left={left} top={top} visible={_dockVisible}");
    }
    // ================= Dock 栏 =================

    private void ApplyRuntimeSettings(AppSettings settings, bool rebuild)
    {
        _settings = settings;
        CloseFolderPanel();
        ApplyBackground();
        if (rebuild) RebuildItems(); else UpdateItemVisuals();
        RefreshLayout();
        ApplyMagnifyAnchor();
        UpdateStartupEntry();
        RegisterHotkey();
    }

    private void RebuildItems()
    {
        _itemRoots.Clear();
        _scales.Clear();
        _scaleCurrent.Clear();
        _scaleTarget.Clear();
        ItemsHost.Children.Clear();

        foreach (var item in _settings.Items)
        {
            var root = CreateItemVisual(item);
            _itemRoots.Add(root);
            _scales.Add((ScaleTransform)root.RenderTransform);
            _scaleCurrent.Add(1);
            _scaleTarget.Add(1);
            ItemsHost.Children.Add(root);
        }
        _lastIconSize = _settings.IconSize;
        ApplyMagnifyAnchor();
        RefreshLayout();
    }

    /// <summary>图标提取尺寸按 16px 取整分桶，拖动尺寸滑块时复用缓存图标，避免 UI 卡顿。</summary>
    private static int IconExtractSize(double displaySize) =>
        Math.Max(32, (int)(Math.Round(displaySize * 2 / 16.0) * 16));

    private Grid CreateItemVisual(DockItemModel item)
    {
        double size = _settings.IconSize;
        var root = new Grid
        {
            Width = size,
            Height = size,
            Margin = new Thickness(_settings.IconSpacing / 2.0, 0, _settings.IconSpacing / 2.0, 0),
            Tag = item,
            Cursor = Cursors.Hand,
            ToolTip = item.Name,
            RenderTransformOrigin = new Point(0.5, 1.0),
            Focusable = false,
            // Transparent background is required for hit testing; children are IsHitTestVisible=false.
            Background = Brushes.Transparent,
        };
        root.RenderTransform = new ScaleTransform();
        root.MouseEnter += (_, _) => _mouseOverDock = true;
        root.MouseLeave += (_, _) => _mouseOverDock = false;
        root.MouseLeftButtonDown += OnItemMouseDown;
        root.MouseMove += OnItemMouseMove;
        root.MouseLeftButtonUp += OnItemMouseUp;
        root.ContextMenu = BuildItemMenu(item, root);

        // 内置文件夹图标支持拖入文件/快捷方式直接加入
        if (item.FolderItems != null)
        {
            root.AllowDrop = true;
            root.DragOver += OnFolderIconDragOver;
            root.Drop += OnFolderIconDrop;
        }

        var img = new Image
        {
            Source = IconService.GetItemIcon(item, IconExtractSize(size)),
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            
            IsHitTestVisible = false,
        };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        root.Children.Add(img);

        var dot = new Ellipse
        {
            Width = 5,
            Height = 5,
            Fill = new SolidColorBrush(Color.FromArgb(0xE8, 0xEA, 0xEA, 0xF2)),
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, -5),
            IsHitTestVisible = false,
        };
        root.Children.Add(dot);
        item.IsRunning = ProcessService.IsRunning(item.TargetPath);
        dot.Visibility = item.IsRunning ? Visibility.Visible : Visibility.Collapsed;
        return root;
    }

    /// <summary>设置变化时原位更新图标尺寸/间距，无需重建整个列表。</summary>
    private void UpdateItemVisuals()
    {
        double size = _settings.IconSize;
        double spacing = _settings.IconSpacing;
        bool sizeChanged = Math.Abs(size - _lastIconSize) >= 1;
        for (int i = 0; i < _itemRoots.Count; i++)
        {
            var root = _itemRoots[i];
            root.Width = size;
            root.Height = size;
            root.Margin = new Thickness(spacing / 2.0, 0, spacing / 2.0, 0);
            if (root.Tag is DockItemModel item && root.Children.OfType<Image>().FirstOrDefault() is Image img)
            {
                img.Width = size;
                img.Height = size;
                if (sizeChanged)
                    img.Source = IconService.GetItemIcon(item, IconExtractSize(size));
            }
        }
        if (sizeChanged)
        {
            // 图标尺寸变化时复位放大状态，避免残留缩放
            for (int i = 0; i < _scaleCurrent.Count; i++)
            {
                _scaleCurrent[i] = 1;
                _scaleTarget[i] = 1;
                _scales[i].ScaleX = 1;
                _scales[i].ScaleY = 1;
            }
        }
        _lastIconSize = size;
        ApplyMagnifyAnchor();
    }

    private ContextMenu BuildItemMenu(DockItemModel item, Grid root)
    {
        var menu = new ContextMenu();
        if (item.FolderItems != null)
        {
            var openPanel = new MenuItem { Header = "打开文件夹" };
            openPanel.Click += (_, _) => ToggleFolderPanel(item, root);
            menu.Items.Add(openPanel);

            var addFile = new MenuItem { Header = "添加文件…" };
            addFile.Click += (_, _) => AddToBuiltinFolder(item, file: true);
            menu.Items.Add(addFile);

            var addFolder = new MenuItem { Header = "添加文件夹…" };
            addFolder.Click += (_, _) => AddToBuiltinFolder(item, file: false);
            menu.Items.Add(addFolder);

            var rename = new MenuItem { Header = "重命名" };
            rename.Click += (_, _) => RenameBuiltinFolder(item);
            menu.Items.Add(rename);
            menu.Items.Add(new Separator());
        }
        else
        {
            var open = new MenuItem { Header = "打开" };
            open.Click += (_, _) =>
            {
                if (Directory.Exists(item.TargetPath)) OpenSystemFolder(item.TargetPath);
                else ProcessService.ActivateOrLaunch(item);
            };
            menu.Items.Add(open);

            var showInFolder = new MenuItem { Header = "在文件夹中显示" };
            showInFolder.Click += (_, _) =>
            {
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo("explorer.exe") { UseShellExecute = false };
                    psi.ArgumentList.Add("/select," + item.TargetPath);
                    System.Diagnostics.Process.Start(psi);
                }
                catch (Exception) { }
            };
            menu.Items.Add(showInFolder);
        }

        menu.Items.Add(new Separator());
        var remove = new MenuItem { Header = "从 Dock 移除" };
        remove.Click += (_, _) => RemoveItem(item);
        menu.Items.Add(remove);
        return menu;
    }
    private void RemoveItem(DockItemModel item)
    {
        CloseFolderPanel();
        _settings.Items.Remove(item);
        RebuildItems();
        SaveSettings();
    }

    private void OnDockMenuOpened(object sender, RoutedEventArgs e) => CloseFolderPanel();

    private void OnDockMenuAddFolder(object sender, RoutedEventArgs e)
    {
        var folder = new DockItemModel
        {
            Name = "新建文件夹",
            TargetPath = "",
            FolderItems = new List<DockItemModel>(),
        };
        _settings.Items.Add(folder);
        RebuildItems();
        SaveSettings();
        if (_itemRoots.Count > 0)
            ToggleFolderPanel(folder, _itemRoots[_itemRoots.Count - 1]);
    }

    private void OnDockMenuAddItem(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "选择要添加到 Dock 的文件",
            Filter = "应用程序与常用文件 (*.exe;*.lnk;*.bat;*.cmd;*.url;*.ps1)|*.exe;*.lnk;*.bat;*.cmd;*.url;*.ps1|所有文件 (*.*)|*.*",
            Multiselect = true,
        };
        if (dlg.ShowDialog(this) == true)
            AddDockItems(dlg.FileNames);
    }

    private void OnDockMenuSettings(object sender, RoutedEventArgs e) => OpenSettings();

    private void OnDockMenuToggleDesktop(object sender, RoutedEventArgs e) => OnHotkeyPressed();

    private void OnDockMenuExit(object sender, RoutedEventArgs e) => Close();

    private void AddDockItems(IEnumerable<string> paths)
    {
        bool changed = false;
        foreach (var raw in paths)
        {
            var path = PathResolver.Normalize(raw);
            if (string.IsNullOrEmpty(path)) continue;
            if (_settings.Items.Any(i => string.Equals(i.TargetPath, path, StringComparison.OrdinalIgnoreCase))) continue;
            if (Directory.Exists(path))
            {
                // 拖入真实文件夹 -> 系统文件夹图标，点击打开系统窗口；内置文件夹需通过右键/设置添加
                string dirName = System.IO.Path.GetFileName(path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
                _settings.Items.Add(new DockItemModel
                {
                    Name = string.IsNullOrEmpty(dirName) ? path : dirName,
                    TargetPath = path,
                });
            }
            else
            {
                string name = System.IO.Path.GetFileNameWithoutExtension(path);
                _settings.Items.Add(new DockItemModel
                {
                    Name = string.IsNullOrEmpty(name) ? path : name,
                    TargetPath = path,
                });
            }
            changed = true;
        }
        if (changed)
        {
            RebuildItems();
            SaveSettings();
        }
    }

    // ---------- 文件夹弹出面板 ----------

    private void ToggleFolderPanel(DockItemModel item, Grid root)
    {
        if (_quitting) return;
        if (_folderPanel != null)
        {
            if (_folderPanelItem == item)
            {
                CloseFolderPanel();
                return;
            }
            CloseFolderPanel();
        }
        if (root == null) return;

        // PointToScreen 返回物理像素；WPF 窗口 Left/Top 使用 DIP，需按当前 DPI 缩放换算
        var src = PresentationSource.FromVisual(this);
        double scale = src?.CompositionTarget.TransformToDevice.M11 ?? _dpiScale;
        var p = root.PointToScreen(new Point(0, 0));
        double w = Math.Max(1, root.ActualWidth);
        double anchorX = (p.X + w / 2.0) / scale;
        double iconTopY = p.Y / scale;
        double iconBottomY = (p.Y + Math.Max(1, root.ActualHeight)) / scale;
        bool dockTop = _settings.DockPosition == "TopCenter";
        // 内置文件夹（FolderItems != null）传子项列表；系统文件夹传路径；其余不弹面板
        List<DockItemModel>? folderItems = item.FolderItems;
        string? folderPath = folderItems == null && Directory.Exists(item.TargetPath) ? item.TargetPath : null;
        if (folderItems == null && folderPath == null) return;

        _folderPanel = new FolderPanelWindow(folderPath, folderItems, _settings, anchorX, iconTopY, iconBottomY, dockTop, _dpiScale, CloseFolderPanel, SaveSettings);
        _folderPanelItem = item;
        Log("FOLDER panel open item=" + item.Name);
        try
        {
            _folderPanel.Show();
            try { _folderPanelHwnd = new WindowInteropHelper(_folderPanel).Handle; } catch (Exception) { }
            InstallPanelMouseHook();
        }
        catch (Exception ex) { Log("FOLDER show EX: " + ex.Message); _folderPanel = null; _folderPanelItem = null; UninstallPanelMouseHook(); }
    }

    private void CloseFolderPanel()
    {
        var panel = _folderPanel;
        if (panel == null) return;
        _folderPanel = null;
        _folderPanelItem = null;
        _folderPanelHwnd = IntPtr.Zero;
        UninstallPanelMouseHook();
        Log("FOLDER panel close");
        try { panel.SafeClose(); } catch (Exception) { }
    }

    /// <summary>面板打开时安装低级鼠标钩子：面板外右键 / 双击 关闭面板，单击不关闭（方便拖放）。</summary>
    private void InstallPanelMouseHook()
    {
        if (_mouseHook != IntPtr.Zero) return;
        try
        {
            _mouseHookProc = OnLowLevelMouse;
            using var curProc = System.Diagnostics.Process.GetCurrentProcess();
            using var curModule = curProc.MainModule;
            var hMod = Win32.GetModuleHandle(curModule.ModuleName);
            _mouseHook = Win32.SetWindowsHookEx(Win32.WH_MOUSE_LL, _mouseHookProc, hMod, 0);
        }
        catch (Exception) { }
    }

    private void UninstallPanelMouseHook()
    {
        if (_mouseHook != IntPtr.Zero)
        {
            try { Win32.UnhookWindowsHookEx(_mouseHook); } catch (Exception) { }
            _mouseHook = IntPtr.Zero;
        }
        _mouseHookProc = null;
    }

    private IntPtr OnLowLevelMouse(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0 && _folderPanel != null && _folderPanelHwnd != IntPtr.Zero)
            {
                int msg = wParam.ToInt32();
                if (msg == Win32.WM_RBUTTONDOWN || msg == Win32.WM_LBUTTONDBLCLK)
                {
                    var data = (Win32.MSLLHOOKSTRUCT)System.Runtime.InteropServices.Marshal.PtrToStructure(lParam, typeof(Win32.MSLLHOOKSTRUCT));
                    if (Win32.GetWindowRect(_folderPanelHwnd, out var r) && !r.Contains(data.pt))
                    {
                        Dispatcher.BeginInvoke(new Action(CloseFolderPanel));
                    }
                }
            }
        }
        catch (Exception) { }
        return Win32.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private bool IsInsideFolderIcon(DependencyObject dep)
    {
        DependencyObject? cur = dep;
        while (cur != null)
        {
            if (cur is Grid grid && grid.Tag is DockItemModel item && item == _folderPanelItem)
                return true;
            cur = VisualTreeHelper.GetParent(cur);
        }
        return false;
    }

    private void OnWindowMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_folderPanel == null) return;
        if (e.OriginalSource is DependencyObject dep && IsInsideFolderIcon(dep)) return;
        // 单击不关闭（方便拖放）；双击 Dock 空白处关闭面板
        if (e.ClickCount >= 2) CloseFolderPanel();
    }

    // ---------- 图标点击 / 拖拽排序 ----------

    private void OnItemMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        var root = (Grid)sender;
        Log($"DOWN index={ItemsHost.Children.IndexOf(root)}");
        _dragIndex = ItemsHost.Children.IndexOf(root);
        _dragStart = e.GetPosition(this);
        _dragging = false;
        _clickMoved = false;
        root.CaptureMouse();
        e.Handled = true;
    }

    private void OnItemMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragIndex < 0) return;
        var pos = e.GetPosition(this);
        var delta = pos - _dragStart;
        if (!_dragging && delta.Length > 8)
        {
            _dragging = true;
            _clickMoved = true;
        }
        if (_dragging && _settings.Items.Count > 1)
        {
            int target = IndexFromX(pos.X);
            if (target >= 0 && target < ItemsHost.Children.Count && target != _dragIndex
                && _dragIndex >= 0 && _dragIndex < ItemsHost.Children.Count)
            {
                var el = ItemsHost.Children[_dragIndex];
                ItemsHost.Children.RemoveAt(_dragIndex);
                ItemsHost.Children.Insert(target, el);
                _dragIndex = target;
                SyncItemLists();
            }
        }
        e.Handled = true;
    }

    private void OnItemMouseUp(object sender, MouseButtonEventArgs e)
    {
        var root = (Grid)sender;
        root.ReleaseMouseCapture();
        if (_dragging)
        {
            var order = ItemsHost.Children.OfType<Grid>().Select(g => g.Tag).OfType<DockItemModel>().ToList();
            _settings.Items = order;
            SaveSettings();
        }
        else if (!_clickMoved && _dragIndex >= 0 && _dragIndex < _settings.Items.Count)
        {
            var item = _settings.Items[_dragIndex];
            Log($"CLICK drag={_dragging} moved={_clickMoved} idx={_dragIndex} item={item.Name} path={item.TargetPath}");
            if (item.FolderItems != null)
            {
                ToggleFolderPanel(item, root);
            }
            else
            {
                // 点击其他图标时关闭已打开的面板（单击外部不关，只有双击/右键才关）
                if (_folderPanel != null) CloseFolderPanel();
                if (Directory.Exists(item.TargetPath))
                    OpenSystemFolder(item.TargetPath);
                else
                    ProcessService.ActivateOrLaunch(item);
            }
        }
        Log($"UP drag={_dragging} moved={_clickMoved} idx={_dragIndex}");
        _dragIndex = -1;
        _dragging = false;
        e.Handled = true;
    }

    /// <summary>拖拽排序后同步内部列表顺序，保证放大/动画按新顺序定位。</summary>
    private void SyncItemLists()
    {
        _itemRoots.Clear();
        _scales.Clear();
        _scaleCurrent.Clear();
        _scaleTarget.Clear();
        foreach (var child in ItemsHost.Children)
        {
            if (child is Grid root)
            {
                _itemRoots.Add(root);
                _scales.Add((ScaleTransform)root.RenderTransform);
                _scaleCurrent.Add(1);
                _scaleTarget.Add(1);
            }
        }
    }

    /// <summary>用资源管理器打开系统文件夹（系统文件夹图标点击行为）。</summary>
    private static void OpenSystemFolder(string path)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("explorer.exe") { UseShellExecute = false };
            psi.ArgumentList.Add(path);
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception) { }
    }

    private int IndexFromX(double x)
    {
        double pad = 10;
        double spacing = _spacing;
        double borderT = _settings.ShowBorder ? 1 : 0;
        int count = _itemRoots.Count;
        if (count == 0) return 0;
        double baseSize = _baseSize;
        double contentW = _barWidth - 2 * (pad + borderT);
        double total = count * (baseSize + spacing);
        double slot = (pad + borderT) + (contentW - total) / 2.0 + spacing / 2.0;
        for (int i = 0; i < count; i++)
        {
            if (x < slot + baseSize) return i;
            slot += baseSize + spacing;
        }
        return Math.Max(0, count - 1);
    }

    // ---------- 渲染与动画 ----------

    private void ApplyMagnifyAnchor()
    {
        DockShell.VerticalAlignment = VerticalAlignment.Stretch;
        ItemsHost.VerticalAlignment = VerticalAlignment.Center;
        foreach (var root in _itemRoots)
            root.RenderTransformOrigin = new Point(0.5, 0.5);
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (PresentationSource.FromVisual(this) == null) return;
        try
        {
            UpdateMagnification();
            UpdateSlide();
        }
        catch (Exception ex)
        {
            if (DateTime.UtcNow >= _nextRenderErrLogUtc)
            {
                _nextRenderErrLogUtc = DateTime.UtcNow.AddSeconds(10);
                Log("Rendering EX: " + ex);
            }
        }
    }

    private void UpdateMagnification()
    {
        int count = _itemRoots.Count;
        if (count == 0) return;
        var cursor = GetCursorScreenPoint();
        var local = PointFromScreen(new Point(cursor.X, cursor.Y));
        double boost = Math.Clamp(_settings.MagnifyBoost, 0, 2);
        double pad = 10;
        double borderT = _settings.ShowBorder ? 1 : 0;
        double spacing = _spacing;
        double baseSize = _baseSize;

        double contentW = _barWidth - 2 * (pad + borderT);
        double total = count * (baseSize + spacing);
        double x0 = (pad + borderT) + (contentW - total) / 2.0;
        double y0 = (pad + borderT) + (_barHeight - 2 * (pad + borderT) - baseSize) / 2.0;

        var curW = new double[count];
        var curX = new double[count];
        var curY = new double[count];
        for (int i = 0; i < count; i++)
        {
            curW[i] = baseSize * _scaleCurrent[i];
            curX[i] = x0 + spacing / 2.0 + i * (baseSize + spacing) + baseSize / 2.0;
            curY[i] = y0 + baseSize / 2.0;
        }

        int hover = -1;
        for (int i = 0; i < count; i++)
        {
            double half = curW[i] / 2.0;
            if (local.X >= curX[i] - half - 1 && local.X <= curX[i] + half + 1 &&
                local.Y >= curY[i] - half - 1 && local.Y <= curY[i] + half + 1)
            {
                hover = i;
                break;
            }
        }

        double magnified = 1 + boost;
        for (int i = 0; i < count; i++)
        {
            double target = i == hover ? magnified : 1;
            _scaleTarget[i] = target;
            double cur = _scaleCurrent[i];
            double next = cur + (target - cur) * 0.32;
            if (Math.Abs(next - cur) < 0.0005) next = target;
            _scaleCurrent[i] = next;
            _scales[i].ScaleX = next;
            _scales[i].ScaleY = next;
        }
    }

    private void UpdateSlide()
    {
        if (!_dockVisible) _slideTargetY = HideSlideOffset();
        if (Math.Abs(_slideY - _slideTargetY) > 0.05)
            _slideY += (_slideTargetY - _slideY) * 0.25;
        else
            _slideY = _slideTargetY;

        double targetOpacity = _dockVisible ? 1 : 0;
        _opacity += (targetOpacity - _opacity) * 0.25;
        if (Math.Abs(_opacity - targetOpacity) < 0.01) _opacity = targetOpacity;

        if (_slideY != 0 || _opacity != Opacity)
        {
            bool dockTop = _settings.DockPosition == "TopCenter";
            Top = dockTop ? _dockEdge + _slideY : _dockEdge - _winHeight + _slideY;
            Opacity = _opacity;
        }
    }

    private void OnRunningTick(object? sender, EventArgs e)
    {
        foreach (var root in _itemRoots)
        {
            if (root.Tag is not DockItemModel item) continue;
            bool running = ProcessService.IsRunning(item.TargetPath);
            if (running == item.IsRunning) continue;
            item.IsRunning = running;
            var dot = root.Children.OfType<Ellipse>().FirstOrDefault();
            if (dot != null)
                dot.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void LogTickState()
    {
        string msg;
        if (_hwnd != IntPtr.Zero && Win32.GetWindowRect(_hwnd, out var r))
            msg = $"state dockVisible={_dockVisible} slideY={_slideY:F1} opacity={_opacity:F2} winVis={Win32.IsWindowVisible(_hwnd)} rect={r.Left},{r.Top}-{r.Right},{r.Bottom}";
        else
            msg = $"state hwnd={_hwnd} no-rect";
        Log(msg);
        try
        {
            var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MacDock");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "state.log"), msg + "\r\n");
        }
        catch (Exception ex)
        {
            Log("state EX: " + ex.Message);
        }
    }

    private static Win32.POINT GetCursorScreenPoint()
    {
        Win32.GetCursorPos(out var pt);
        return pt;
    }

    private static string GetClassOf(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        Win32.GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    // ---------- 自动隐藏 / 被覆盖检测 ----------

    private void OnPoll(object? sender, EventArgs e)
    {
        if (_quitting) return;
        RefreshLayoutIfMonitorChanged();
        // 被窗口遮挡时自动隐藏固定开启（不再提供关闭选项）。
        var cursor = GetCursorScreenPoint();
        bool overDock = _mouseOverDock || CursorInDockArea(cursor);
        bool edge = InEdgeHotZone(cursor);
        bool grace = DateTime.UtcNow < _startupGraceUntil;
        bool justShown = (DateTime.UtcNow - _lastShowUtc).TotalMilliseconds < 600;

        if (_dockVisible)
        {
            bool covered = IsCovered();
            if (covered && !overDock && !edge && !_dragging && _folderPanel == null &&
                (_settingsWindow == null || !_settingsWindow.IsActive) && !grace && !justShown)
            {
                Log($"POLL hide covered={covered} over={overDock} edge={edge}");
                HideDock();
            }
        }
        else
        {
            if (!IsCovered())
            {
                Log("POLL show not-covered");
                ShowDock();
                return;
            }
            if (_settings.ShowOnEdge && edge)
            {
                if (_settings.BlockShowWhenCovered)
                {
                    Log("POLL edge blocked");
                    return;
                }
                Log("POLL show edge");
                ShowDock();
            }
        }
    }

    private bool InEdgeHotZone(Win32.POINT cursor)
    {
        var mon = GetMonitorInfoOf(cursor);
        double zone = Math.Max(4, _settings.EdgeHotzoneSize) * _dpiScale;
        return _settings.DockPosition == "TopCenter"
            ? cursor.Y <= mon.rcWork.Top + zone
            : cursor.Y >= mon.rcWork.Bottom - zone;
    }

    private bool CursorInDockArea(Win32.POINT cursor)
    {
        var r = IntendedDockRect();
        if (cursor.X < r.Left - 36 || cursor.X > r.Right + 36 || cursor.Y < r.Top - 36) return false;
        return cursor.Y <= r.Bottom + 36;
    }

    private void RefreshLayoutIfMonitorChanged()
    {
        // 使用 Dock 锚点位置而非鼠标位置来判断显示器变化，
        // 确保鼠标不在 Dock 所在屏幕时也能正确检测分辨率/DPI 变化
        var pt = new Win32.POINT { X = (int)(_anchorLeft * _dpiScale), Y = (int)(_dockEdge * _dpiScale) };
        var mon = GetMonitorInfoOf(pt);
        double scale = _dpiScale;
        bool dockTop = _settings.DockPosition == "TopCenter";
        double expected = dockTop
            ? mon.rcWork.Top / scale + 10 + _settings.DockOffsetY
            : ComputeWorkBottomPx(mon, dockTop) / scale - 6 - _settings.DockOffsetY;
        if (Math.Abs(expected - _dockEdge) > 30)
            Dispatcher.BeginInvoke(new Action(RefreshLayout));
    }

    private bool IsCovered() => IsCoveredByVisibleWindows(IntendedDockRect());

    private bool IsCoveredByVisibleWindows() => IsCoveredByVisibleWindows(IntendedDockRect());

    private bool IsCoveredByVisibleWindows(Win32.RECT dockRect)
    {
        var list = new List<IntPtr>();
        Win32.EnumWindows((hwnd, _) => { list.Add(hwnd); return true; }, IntPtr.Zero);
        foreach (var hwnd in list)
        {
            if (hwnd == _hwnd) continue;
            if (_settingsWindow != null && hwnd == _settingsWindow.Hwnd) continue;
            if (!Win32.IsWindowVisible(hwnd)) continue;
            if (Win32.IsIconic(hwnd)) continue;
            if (!Win32.GetWindowRect(hwnd, out var r)) continue;
            if (!r.IntersectsWith(dockRect)) continue;
            if (r.Width <= 0 || r.Height <= 0) continue;
            long ex = Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE).ToInt64();
            if ((ex & 0x80) != 0) continue; // WS_EX_TOOLWINDOW
            string cls = GetClassOf(hwnd);
            if (cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd" || cls == "Progman" || cls == "WorkerW") continue;
            if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out var cloaked, 4) == 0 && cloaked != 0) continue;
            int dockArea = dockRect.Width * dockRect.Height;
            if (dockArea <= 0) continue;
            if (r.IntersectArea(dockRect) < dockArea * 0.35) continue;
            if (DateTime.UtcNow >= _nextCoverLogUtc)
            {
                _nextCoverLogUtc = DateTime.UtcNow.AddSeconds(10);
                var sbCls = new StringBuilder(256);
                var sbTitle = new StringBuilder(256);
                Win32.GetClassName(hwnd, sbCls, sbCls.Capacity);
                Win32.GetWindowText(hwnd, sbTitle, sbTitle.Capacity);
                Log($"COVER hwnd={hwnd} cls={sbCls} title={sbTitle} rect={r}");
            }
            return true;
        }
        return false;
    }

    private Win32.RECT IntendedDockRect()
    {
        double scale = _dpiScale;
        bool dockTop = _settings.DockPosition == "TopCenter";
        double topY = dockTop ? _dockEdge : _dockEdge - _barHeight;
        var r = new Win32.RECT();
        r.Left = (int)((_anchorLeft - _barWidth / 2.0) * scale);
        r.Top = (int)(topY * scale);
        r.Right = (int)((_anchorLeft + _barWidth / 2.0) * scale);
        r.Bottom = (int)((dockTop ? _dockEdge + _barHeight : _dockEdge) * scale);
        return r;
    }

    // ---------- 显示 / 隐藏 ----------

    private void HideDock()
    {
        _dockVisible = false;
        CloseFolderPanel();
        _slideTargetY = HideSlideOffset();
        SetClickThrough(true);
        Log("HIDE dock");
    }

    private void ShowDock()
    {
        _dockVisible = true;
        _slideTargetY = 0;
        Topmost = true;
        _lastShowUtc = DateTime.UtcNow;
        SetClickThrough(false);
        Log("SHOW dock");
    }

    private double HideSlideOffset() =>
        _settings.DockPosition == "TopCenter" ? -(_winHeight + 40) : (_winHeight + 40);

    private void ToggleDockVisibility()
    {
        if (_dockVisible) HideDock();
        else ShowDock();
    }

    private void SetClickThrough(bool clickThrough)
    {
        if (_hwnd == IntPtr.Zero) return;
        long ex = Win32.GetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE).ToInt64();
        long next = clickThrough ? (ex | Win32.WS_EX_TRANSPARENT) : (ex & ~(long)Win32.WS_EX_TRANSPARENT);
        if (next == ex) return;
        Win32.SetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE, new IntPtr(next));
        Win32.SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0,
            Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE | Win32.SWP_FRAMECHANGED);
    }

    // ---------- 托盘 / 设置 ----------

    private void BuildTray()
    {
        _tray = new Wf.NotifyIcon { Text = "MacDock - 桌面 Dock", Visible = true };
        try
        {
            var res = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
            var stream = res?.Stream;
            if (stream != null)
            {
                _tray.Icon = new System.Drawing.Icon(stream);
            }
        }
        catch (Exception) { }

        var menu = new Wf.ContextMenuStrip();
        menu.Items.Add("显示 / 隐藏桌面图标 (F2)", null, (_, _) => OnHotkeyPressed());
        menu.Items.Add("设置...", null, (_, _) => OpenSettings());
        menu.Items.Add(new Wf.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Close());
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => OpenSettings();
    }

    private void ShowToast(string text)
    {
        try { _tray?.ShowBalloonTip(1200, "MacDock", text, Wf.ToolTipIcon.Info); } catch (Exception) { }
    }

    private void OpenSettings()
    {
        if (_quitting) return;
        CloseFolderPanel();
        if (_settingsWindow != null && _settingsWindow.IsLoaded)
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(_settings, _settingsService);
        _settingsWindow.Owner = this;
        _settingsWindow.SettingsChanged += s => { ApplyRuntimeSettings(s, rebuild: false); SaveSettings(); };
        _settingsWindow.ItemsChanged += s => { ApplyRuntimeSettings(s, rebuild: true); SaveSettings(); };
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    // ---------- 拖放添加 ----------

    private void HighlightDrop(bool on)
    {
        if (on)
        {
            DockShell.BorderThickness = new Thickness(2);
            DockShell.BorderBrush = Brushes.Gold;
        }
        else
        {
            ApplyBackground();
        }
    }

    private void OnDockDragEnter(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        if (e.Effects == DragDropEffects.Copy) HighlightDrop(true);
        e.Handled = true;
    }

    private void OnDockDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDockDragLeave(object sender, DragEventArgs e) => HighlightDrop(false);

    private void OnDockDrop(object sender, DragEventArgs e)
    {
        HighlightDrop(false);
        if (e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            AddDockItems(files);
            ShowToast($"已添加 {files.Length} 个图标到 Dock");
        }
        e.Handled = true;
    }

    // ---------- 持久化 ----------

    private void SaveSettings()
    {
        _settingsService.Save(_settings);
        UpdateStartupEntry();
    }

    private void UpdateStartupEntry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key == null) return;
            if (_settings.RunOnStartup)
            {
                var exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe))
                    key.SetValue("MacDock", "\"" + exe + "\"");
            }
            else
            {
                key.DeleteValue("MacDock", false);
            }
        }
        catch (Exception ex)
        {
            Log("startup EX: " + ex.Message);
        }
    }

    private void OnDisplayChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(new Action(RefreshLayout));



    private void AddToBuiltinFolder(DockItemModel folder, bool file)
    {
        if (folder.FolderItems == null) return;
        if (file)
        {
            using var dlg = new Wf.OpenFileDialog
            {
                Title = "选择要加入文件夹的应用或文件",
                Multiselect = true,
                CheckFileExists = true,
            };
            if (dlg.ShowDialog() != Wf.DialogResult.OK) return;
            AddPathsToBuiltinFolder(folder, dlg.FileNames);
        }
        else
        {
            using var dlg = new Wf.FolderBrowserDialog
            {
                Description = "选择要加入文件夹的文件夹",
                ShowNewFolderButton = false,
            };
            if (dlg.ShowDialog() != Wf.DialogResult.OK) return;
            AddPathsToBuiltinFolder(folder, new[] { dlg.SelectedPath });
        }
    }


    private void OnFolderIconDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnFolderIconDrop(object sender, DragEventArgs e)
    {
        HighlightDrop(false);
        if (e.Data.GetDataPresent(DataFormats.FileDrop) &&
            e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0 &&
            sender is Grid root && root.Tag is DockItemModel item && item.FolderItems != null)
        {
            AddPathsToBuiltinFolder(item, files);
            ShowToast($"已加入 {files.Length} 个图标到「{item.Name}」");
        }
        e.Handled = true;
    }
    private void AddPathsToBuiltinFolder(DockItemModel folder, IEnumerable<string> paths)
    {
        if (folder.FolderItems == null) return;
        bool changed = false;
        foreach (var raw in paths)
        {
            if (PathResolver.TryAddPath(folder.FolderItems, raw))
                changed = true;
        }
        if (changed)
        {
            SaveSettings();
            if (_folderPanel != null) _folderPanel.RefreshContent();
        }
    }

    private void RenameBuiltinFolder(DockItemModel folder)
    {
        var dlg = new TextInputDialog("重命名文件夹", "文件夹名称：", folder.Name);
        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.Value))
        {
            folder.Name = dlg.Value.Trim();
            SaveSettings();
            RebuildItems();
        }
    }
}





