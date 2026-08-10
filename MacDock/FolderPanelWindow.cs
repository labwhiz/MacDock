using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using MacDock.Models;
using MacDock.Native;
using MacDock.Services;

namespace MacDock;

/// <summary>文件夹弹出面板：点击 Dock 上的文件夹图标时，在图标旁边（背景栏外）弹出规整矩形网格，容纳文件夹内所有图标。</summary>
public class FolderPanelWindow : Window
{
    private readonly string? _folderPath;        // 系统文件夹视图（null 时表示内置文件夹）
    private readonly List<DockItemModel>? _folderItems; // 内置文件夹子项（null 时表示系统文件夹）
    private readonly AppSettings _settings;
    private readonly double _anchorX;      // 图标中心 X（DIP 屏幕坐标）
    private readonly double _iconTopY;     // 图标顶边 Y（DIP 屏幕坐标）
    private readonly double _iconBottomY;  // 图标底边 Y（DIP 屏幕坐标）
    private readonly bool _dockTop;
    private readonly double _dpiScale;
    private readonly Action? _onClosed;
    private readonly Action? _onChanged;
    private Rect _workArea;
    private System.Windows.Controls.WrapPanel? _panelHost;
    private MacDock.Models.DockItemModel? _panelDragModel;
    private Point _panelDragStart;
    private bool _panelDragMoved;
    private double _panelItemW;
    private double _panelItemH;
    private bool _closing;

    public FolderPanelWindow(
        string? folderPath,
        List<DockItemModel>? folderItems,
        AppSettings settings,
        double anchorX,
        double iconTopY,
        double iconBottomY,
        bool dockTop,
        double dpiScale,
        Action? onClosed,
        Action? onChanged)
    {
        _folderPath = folderPath;
        _folderItems = folderItems;
        _settings = settings;
        _anchorX = anchorX;
        _iconTopY = iconTopY;
        _iconBottomY = iconBottomY;
        _dockTop = dockTop;
        _dpiScale = dpiScale;
        _onClosed = onClosed;
        _onChanged = onChanged;
        _workArea = GetWorkAreaDip();

        Title = "FolderPanel";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowActivated = true;
        Focusable = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

        // 先放到屏幕外，SizeChanged 定位后再出现，避免闪烁
        Left = -20000;
        Top = -20000;

        SizeChanged += OnSizeChanged;
        Closed += (_, _) => _onClosed?.Invoke();

        Content = BuildContent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        try
        {
            // 工具窗口：不进任务栏 / Alt+Tab，且不参与 Dock 的“被覆盖”判断
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            long ex = Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE).ToInt64();
            ex |= Win32.WS_EX_TOOLWINDOW;
            Win32.SetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE, new IntPtr(ex));
        }
        catch (Exception) { }
    }

    /// <summary>内容变化后重建面板（内置文件夹添加/移除时由外部调用）。</summary>
    public void RefreshContent()
    {
        if (_closing) return;
        try { Content = BuildContent(); } catch (Exception) { }
    }

    /// <summary>SafeClose: 防止在窗口关闭过程中再次 Close()/Show() 触发无法设置可见性异常。</summary>
    public void SafeClose()
    {
        if (_closing) return;
        _closing = true;
        try { Close(); } catch (Exception) { }
    }

    private bool IsBuiltinFolder => _folderPath == null && _folderItems != null;

    private FrameworkElement BuildContent()
    {
        var color = ParseHex(_settings.BackgroundColor, Color.FromRgb(0x26, 0x26, 0x2E));
        byte alpha = _settings.BackgroundStyle switch
        {
            "Solid" => (byte)255,
            "Transparent" => (byte)0,
            _ => (byte)Math.Round(255 * Math.Clamp(_settings.BackgroundOpacity, 0, 1)),
        };

        var border = new Border
        {
            CornerRadius = new CornerRadius(Math.Max(0, _settings.CornerRadius)),
            Padding = new Thickness(12),
            BorderThickness = _settings.ShowBorder ? new Thickness(1) : new Thickness(0),
            Background = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B)),
            BorderBrush = new SolidColorBrush(ParseHex(_settings.BorderColor, Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF))),
        };

        if (IsBuiltinFolder)
        {
            border.AllowDrop = true;
            border.DragOver += (_, e) =>
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                    e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            };
            border.Drop += OnPanelDrop;
            border.ContextMenu = BuildEmptyMenu();
        }

        var entries = GetEntries();
        if (entries.Count == 0)
        {
            border.Child = new TextBlock
            {
                Text = IsBuiltinFolder ? "（空文件夹）\n可拖入文件/快捷方式添加" : "（空文件夹）",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xB4)),
                Margin = new Thickness(10),
                TextAlignment = TextAlignment.Center,
            };
            return border;
        }

        int iconSize = Math.Max(28, Math.Min(44, (int)Math.Round(_settings.IconSize * 0.65)));
        double itemW = iconSize + 12;
        double itemH = iconSize + 26;

        _panelItemW = itemW;
        _panelItemH = itemH;
        var panel = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            MaxWidth = Math.Max(80, _workArea.Width - 48),
            ItemWidth = itemW,
            ItemHeight = itemH,
        };
        _panelHost = panel;
        foreach (var entry in entries)
        {
            panel.Children.Add(BuildItem(entry, iconSize, itemW, itemH));
        }

        var scroll = new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = Math.Max(60, MaxPanelHeight()),
            Padding = new Thickness(0),
        };
        border.Child = scroll;
        return border;
    }

    private ContextMenu BuildEmptyMenu()
    {
        var menu = new ContextMenu();
        var addFile = new MenuItem { Header = "添加文件…" };
        addFile.Click += (_, _) => AddFilesViaDialog();
        menu.Items.Add(addFile);
        var addFolder = new MenuItem { Header = "添加文件夹…" };
        addFolder.Click += (_, _) => AddFoldersViaDialog();
        menu.Items.Add(addFolder);
        return menu;
    }

    private void AddFilesViaDialog()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择要加入文件夹的应用或文件",
            Multiselect = true,
            CheckFileExists = true,
        };
        if (dlg.ShowDialog(this) == true)
            AddPaths(dlg.FileNames);
    }

    private void AddFoldersViaDialog()
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "选择要加入文件夹的文件夹",
            ShowNewFolderButton = false,
        };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            AddPaths(new[] { dlg.SelectedPath });
    }

    private void OnPanelDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
        if (paths != null && paths.Length > 0)
            AddPaths(paths);
    }

    private void AddPaths(IEnumerable<string> paths)
    {
        if (!IsBuiltinFolder || _folderItems == null) return;
        bool changed = false;
        foreach (var raw in paths)
        {
            var path = PathResolver.Normalize(raw);
            if (string.IsNullOrEmpty(path)) continue;
            if (_folderItems.Any(i => string.Equals(i.TargetPath, path, StringComparison.OrdinalIgnoreCase))) continue;
            string name = Directory.Exists(path)
                ? System.IO.Path.GetFileName(path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar))
                : System.IO.Path.GetFileNameWithoutExtension(path);
            _folderItems.Add(new DockItemModel
            {
                Name = string.IsNullOrEmpty(name) ? path : name,
                TargetPath = path,
            });
            changed = true;
        }
        if (changed)
        {
            Content = BuildContent();
            _onChanged?.Invoke();
        }
    }

    private void RemoveEntry(DockItemModel item)
    {
        if (!IsBuiltinFolder || _folderItems == null) return;
        _folderItems.Remove(item);
        Content = BuildContent();
        _onChanged?.Invoke();
    }

    private List<(string Path, string Name, DockItemModel? Model, bool IsDir)> GetEntries()
    {
        var list = new List<(string, string, DockItemModel?, bool)>();
        if (IsBuiltinFolder && _folderItems != null)
        {
            foreach (var m in _folderItems)
            {
                string name = string.IsNullOrEmpty(m.Name) ? m.TargetPath : m.Name;
                list.Add((m.TargetPath, name, m, Directory.Exists(m.TargetPath)));
            }
            return list; // 内置文件夹保持用户顺序（支持拖拽排序）
        }

        if (!string.IsNullOrEmpty(_folderPath))
        {
            try
            {
                foreach (string p in Directory.GetFileSystemEntries(_folderPath))
                {
                    string name;
                    try { name = System.IO.Path.GetFileName(p.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)); }
                    catch { name = p; }
                    if (string.IsNullOrEmpty(name)) name = p;
                    list.Add((p, name, null, Directory.Exists(p)));
                }
            }
            catch (Exception) { }
        }
        return list
            .OrderByDescending(x => x.Item4)
            .ThenBy(x => x.Item2, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private FrameworkElement BuildItem((string Path, string Name, DockItemModel? Model, bool IsDir) entry, int iconSize, double itemW, double itemH)
    {
        var border = new Border
        {
            Width = itemW,
            Height = itemH,
            CornerRadius = new CornerRadius(8),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Margin = new Thickness(2),
        };

        var grid = new Grid { Margin = new Thickness(0) };
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var img = new Image
        {
            Source = IconService.GetIcon(entry.Path, IconExtractSize(iconSize)),
            Width = iconSize,
            Height = iconSize,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);

        var label = new TextBlock
        {
            Text = entry.Name,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xEE)),
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = itemW - 4,
            Margin = new Thickness(0, 2, 0, 0),
            IsHitTestVisible = false,
        };

        Grid.SetRow(img, 0);
        Grid.SetRow(label, 1);
        grid.Children.Add(img);
        grid.Children.Add(label);
        border.Child = grid;

        border.MouseEnter += (_, _) => border.Background = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
        border.MouseLeave += (_, _) => border.Background = Brushes.Transparent;
        border.MouseLeftButtonDown += (_, e) => OnPanelItemMouseDown(border, entry, e);
        border.MouseMove += (_, e) => OnPanelItemMouseMove(border, entry, e);
        border.MouseLeftButtonUp += (_, e) => OnPanelItemMouseUp(border, entry, e);
        if (entry.Model != null)
        {
            var menu = new ContextMenu();
            var remove = new MenuItem { Header = "从文件夹移除" };
            remove.Click += (_, _) => RemoveEntry(entry.Model);
            menu.Items.Add(remove);
            border.ContextMenu = menu;
        }
        return border;
    }

    /// <summary>内置文件夹面板内拖拽排序：按下时记录拖拽项。</summary>
    private void OnPanelItemMouseDown(Border border, (string Path, string Name, DockItemModel? Model, bool IsDir) entry, MouseButtonEventArgs e)
    {
        if (entry.Model == null || !IsBuiltinFolder) return;
        _panelDragModel = entry.Model;
        _panelDragStart = e.GetPosition(this);
        _panelDragMoved = false;
        border.CaptureMouse();
        e.Handled = true;
    }

    /// <summary>拖拽移动：根据鼠标位置计算目标索引并重排 _folderItems 与面板子项。</summary>
    private void OnPanelItemMouseMove(Border border, (string Path, string Name, DockItemModel? Model, bool IsDir) entry, MouseEventArgs e)
    {
        if (entry.Model == null || _panelDragModel != entry.Model || _folderItems == null) return;
        if (e.LeftButton != MouseButtonState.Pressed) return;
        var pos = e.GetPosition(this);
        if (!_panelDragMoved && (pos - _panelDragStart).Length > 6)
        {
            _panelDragMoved = true;
            border.Opacity = 0.55;
        }
        if (!_panelDragMoved) return;

        int from = _folderItems.IndexOf(entry.Model);
        if (from < 0) return;
        int target = PanelIndexFromPoint(pos);
        if (target < 0 || target >= _folderItems.Count || target == from) return;

        _folderItems.RemoveAt(from);
        _folderItems.Insert(target, entry.Model);
        if (_panelHost != null && from < _panelHost.Children.Count && target < _panelHost.Children.Count)
        {
            var el = _panelHost.Children[from];
            _panelHost.Children.RemoveAt(from);
            _panelHost.Children.Insert(target, el);
        }
        e.Handled = true;
    }

    /// <summary>结束拖拽：恢复透明度并保存顺序；未移动视为单击打开。</summary>
    private void OnPanelItemMouseUp(Border border, (string Path, string Name, DockItemModel? Model, bool IsDir) entry, MouseButtonEventArgs e)
    {
        if (entry.Model == null) { OpenEntry(entry); return; }
        if (_panelDragModel == entry.Model)
        {
            border.ReleaseMouseCapture();
            border.Opacity = 1;
            bool moved = _panelDragMoved;
            _panelDragModel = null;
            _panelDragMoved = false;
            if (moved)
            {
                _onChanged?.Invoke();
                e.Handled = true;
                return;
            }
        }
        OpenEntry(entry);
    }

    /// <summary>根据面板内鼠标位置估算目标索引（WrapPanel 按 ItemWidth/ItemHeight 布局）。</summary>
    private int PanelIndexFromPoint(Point pos)
    {
        if (_panelHost == null || _folderItems == null || _folderItems.Count == 0) return -1;
        double itemW = Math.Max(1, _panelItemW);
        double itemH = Math.Max(1, _panelItemH);
        int cols = Math.Max(1, (int)(_panelHost.ActualWidth / itemW));
        int row = Math.Max(0, (int)(pos.Y / itemH));
        int col = Math.Max(0, (int)(pos.X / itemW));
        int idx = row * cols + col;
        return Math.Min(idx, _folderItems.Count - 1);
    }
    private void OpenEntry((string Path, string Name, DockItemModel? Model, bool IsDir) entry)
    {
        if (entry.Model != null)
        {
            ProcessService.ActivateOrLaunch(entry.Model);
        }
        else
        {
            try
            {
                Process.Start(new ProcessStartInfo(entry.Path) { UseShellExecute = true });
            }
            catch (Exception) { }
        }
        SafeClose();
    }

    private static int IconExtractSize(double displaySize) =>
        Math.Max(32, (int)(Math.Round(displaySize * 2 / 16.0) * 16));

    private double MaxPanelHeight()
    {
        double gap = Math.Max(0, _settings.FolderPanelGap);
        return _dockTop
            ? Math.Max(60, _workArea.Bottom - 8 - _iconBottomY - gap)
            : Math.Max(60, _iconTopY - gap - (_workArea.Top + 8));
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_closing) return;
        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        double gap = Math.Max(0, _settings.FolderPanelGap);
        double x = _anchorX - w / 2.0;
        double y = _dockTop ? _iconBottomY + gap : _iconTopY - gap - h;

        x = Math.Max(_workArea.Left + 8, Math.Min(x, _workArea.Right - 8 - w));

        if (!_dockTop && y < _workArea.Top + 8)
            y = _iconBottomY + gap;      // 上方空间不足：翻转到图标下方
        else if (_dockTop && y + h > _workArea.Bottom - 8)
            y = _iconTopY - gap - h;     // 下方空间不足：翻转到图标上方

        y = Math.Max(_workArea.Top + 8, Math.Min(y, _workArea.Bottom - 8 - h));

        Left = Math.Round(x);
        Top = Math.Round(y);
    }

    protected override void OnClosed(EventArgs e)
    {
        _closing = true;
        base.OnClosed(e);
    }

    private Rect GetWorkAreaDip()
    {
        try
        {
            var pt = new Win32.POINT
            {
                X = (int)(_anchorX * _dpiScale),
                Y = (int)(_iconTopY * _dpiScale),
            };
            IntPtr mon = Win32.MonitorFromPoint(pt, 2); // MONITOR_DEFAULTTONEAREST
            var info = new Win32.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Win32.MONITORINFO>() };
            Win32.GetMonitorInfo(mon, ref info);
            return new Rect(
                info.rcWork.Left / _dpiScale,
                info.rcWork.Top / _dpiScale,
                (info.rcWork.Right - info.rcWork.Left) / _dpiScale,
                (info.rcWork.Bottom - info.rcWork.Top) / _dpiScale);
        }
        catch (Exception ex)
        {
            return new Rect(0, 0, SystemParameters.WorkArea.Width, SystemParameters.WorkArea.Height);
        }
    }

    private static Color ParseHex(string? hex, Color fallback)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(hex))
                return (Color)ColorConverter.ConvertFromString(hex);
        }
        catch (Exception) { }
        return fallback;
    }
}
