using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using MacDock.Models;
using MacDock.Services;
namespace MacDock;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _work;
    private readonly SettingsService _settingsService;
    private bool _loading = true;

    private static readonly string[] PresetColors =
    {
        "#26262E", "#3A3A44", "#141419", "#F2F2F7",
        "#1E2A4A", "#2B2142", "#1F3B2F", "#4A2226",
        "#7A5C2E", "#234A4E",
    };

    private static readonly string[] BorderColors =
    {
        "#55FFFFFF", "#FFFFFFFF", "#FF000000", "#99FFFFFF",
        "#FF4A90D9", "#FFFFB84A", "#99D9E8FF",
    };

    private static readonly string[] FolderLabelColors =
    {
        "#E8E8EE", "#FFFFFF", "#C8C8D0", "#FFFFB84A",
        "#FF4A90D9", "#FF8A8A", "#FF8AC1", "#FFB48A",
    };

    private static readonly string[] HotkeyKeys = BuildHotkeyKeys();

    private static string[] BuildHotkeyKeys()
    {
        var list = new List<string>();
        for (int f = 1; f <= 12; f++) list.Add("F" + f);
        for (int d = 0; d <= 9; d++) list.Add(d.ToString());
        for (char c = 'A'; c <= 'Z'; c++) list.Add(c.ToString());
        list.Add("Space");
        list.Add("Tab");
        list.Add("Home");
        list.Add("End");
        list.Add("Enter");
        list.Add("ESC");
        list.Add("Delete");
        list.Add("PgUp");
        list.Add("PgDn");
        list.Add("Left");
        list.Add("Right");
        list.Add("Up");
        list.Add("Down");
        return list.ToArray();
    }

    public event Action<AppSettings>? SettingsChanged;   // 外观类改动（不重建图标）
    public event Action<AppSettings>? ItemsChanged;      // 图标列表改动（需要重建）

    public IntPtr Hwnd => new WindowInteropHelper(this).Handle;

    public SettingsWindow(AppSettings current, SettingsService settingsService)
    {
        InitializeComponent();
        _settingsService = settingsService;
        _work = current.Clone();

        CmbModifier.Items.Add("None");
        CmbModifier.Items.Add("Ctrl");
        CmbModifier.Items.Add("Alt");
        CmbModifier.Items.Add("Shift");
        CmbModifier.Items.Add("Win");

        CmbBlockModifier.Items.Add("None");
        CmbBlockModifier.Items.Add("Ctrl");
        CmbBlockModifier.Items.Add("Alt");
        CmbBlockModifier.Items.Add("Shift");
        CmbBlockModifier.Items.Add("Win");

        CmbTaskbarLockModifier.Items.Add("None");
        CmbTaskbarLockModifier.Items.Add("Ctrl");
        CmbTaskbarLockModifier.Items.Add("Alt");
        CmbTaskbarLockModifier.Items.Add("Shift");
        CmbTaskbarLockModifier.Items.Add("Win");

        foreach (var k in HotkeyKeys)
        {
            CmbHotkeyKey.Items.Add(k);
            CmbBlockKey.Items.Add(k);
            CmbTaskbarLockKey.Items.Add(k);
        }

        PopulatePositionCombo();
        PopulateStyleCombo();
        BuildColorSwatches();
        BuildBorderSwatches();
        BuildFolderLabelSwatches();
        BuildPresetIcons();

        LoadFromWork();
        HookEvents();
        _loading = false;
    }

    private void PopulatePositionCombo()
    {
        CmbPosition.Items.Add(new ComboBoxItem { Content = "底部居中", Tag = "BottomCenter" });
        CmbPosition.Items.Add(new ComboBoxItem { Content = "底部靠左", Tag = "BottomLeft" });
        CmbPosition.Items.Add(new ComboBoxItem { Content = "底部靠右", Tag = "BottomRight" });
        CmbPosition.Items.Add(new ComboBoxItem { Content = "顶部居中", Tag = "TopCenter" });
    }

    private void PopulateStyleCombo()
    {
        CmbBgStyle.Items.Add(new ComboBoxItem { Content = "毛玻璃（亚克力）", Tag = "Acrylic" });
        CmbBgStyle.Items.Add(new ComboBoxItem { Content = "纯色", Tag = "Solid" });
        CmbBgStyle.Items.Add(new ComboBoxItem { Content = "透明", Tag = "Transparent" });
    }

    private void BuildColorSwatches()
    {
        foreach (var hex in PresetColors)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var btn = new Button
            {
                Width = 30,
                Height = 30,
                Margin = new Thickness(0, 0, 8, 0),
                Padding = new Thickness(0),
                Background = new SolidColorBrush(color),
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(2),
                Tag = hex,
                ToolTip = hex,
            };
            btn.Click += (_, _) =>
            {
                _work.BackgroundColor = (string)btn.Tag;
                RefreshSwatchSelection();
                UpdateLabels();
                Save();
                SettingsChanged?.Invoke(_work);
            };
            ColorSwatches.Children.Add(btn);
        }
    }

    private void RefreshSwatchSelection()
    {
        foreach (Button b in ColorSwatches.Children)
            b.BorderBrush = string.Equals((string)b.Tag, _work.BackgroundColor, StringComparison.OrdinalIgnoreCase) ? Brushes.Gold : Brushes.White;
    }

    private void BuildBorderSwatches()
    {
        foreach (var hex in BorderColors)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var btn = new Button
            {
                Width = 30,
                Height = 30,
                Margin = new Thickness(0, 0, 8, 0),
                Padding = new Thickness(0),
                Background = new SolidColorBrush(color),
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(2),
                Tag = hex,
                ToolTip = hex,
            };
            btn.Click += (_, _) =>
            {
                _work.BorderColor = (string)btn.Tag;
                RefreshBorderSwatchSelection();
                UpdateLabels();
                Save();
                SettingsChanged?.Invoke(_work);
            };
            BorderColorSwatches.Children.Add(btn);
        }
    }

    private void RefreshBorderSwatchSelection()
    {
        foreach (Button b in BorderColorSwatches.Children)
            b.BorderBrush = string.Equals((string)b.Tag, _work.BorderColor, StringComparison.OrdinalIgnoreCase) ? Brushes.Gold : Brushes.White;
    }

    private void BuildFolderLabelSwatches()
    {
        foreach (var hex in FolderLabelColors)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var btn = new Button
            {
                Width = 30,
                Height = 30,
                Margin = new Thickness(0, 0, 8, 0),
                Padding = new Thickness(0),
                Background = new SolidColorBrush(color),
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(2),
                Tag = hex,
                ToolTip = hex,
            };
            btn.Click += (_, _) =>
            {
                _work.FolderLabelColor = (string)btn.Tag;
                RefreshFolderLabelSwatchSelection();
                UpdateLabels();
                Save();
                SettingsChanged?.Invoke(_work);
            };
            FolderLabelColorSwatches.Children.Add(btn);
        }
    }

    private void RefreshFolderLabelSwatchSelection()
    {
        foreach (Button b in FolderLabelColorSwatches.Children)
            b.BorderBrush = string.Equals((string)b.Tag, _work.FolderLabelColor, StringComparison.OrdinalIgnoreCase) ? Brushes.Gold : Brushes.White;
    }

    /// <summary>在“内置文件夹图标样式”区域生成预设图标按钮。</summary>
    private void BuildPresetIcons()
    {
        PresetIconPanel.Children.Clear();
        foreach (var (key, label) in IconPresets.Items)
        {
            var img = new Image
            {
                Source = IconPresets.Draw(key, 56),
                Width = 26,
                Height = 26,
                Stretch = Stretch.Uniform,
            };
            var txt = new TextBlock
            {
                Text = label,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 0, 0),
            };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(img);
            sp.Children.Add(txt);
            var btn = new Button
            {
                Content = sp,
                Margin = new Thickness(0, 0, 8, 4),
                Padding = new Thickness(6, 3, 6, 3),
                Tag = key,
                ToolTip = "预设：" + label,
            };
            btn.Click += (_, _) => ApplyIconOverride("preset:" + (string)btn.Tag);
            PresetIconPanel.Children.Add(btn);
        }
    }

    /// <summary>把自定义图标值应用到当前选中的内置文件夹。</summary>
    private void ApplyIconOverride(string? overrideValue)
    {
        int i = ItemList.SelectedIndex;
        if (i < 0 || i >= _work.Items.Count) return;
        var item = _work.Items[i];
        if (item.FolderItems == null) return;
        item.IconOverride = overrideValue;
        ReloadItemList();
        ItemList.SelectedIndex = i;
        UpdateButtons();
        Save();
        ItemsChanged?.Invoke(_work);
    }

    private void OnPickSoftwareIconClick(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择软件（提取其图标）",
            Filter = "程序与库 (*.exe;*.dll;*.lnk;*.ico)|*.exe;*.dll;*.lnk;*.ico|所有文件 (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog(this) != true) return;
        ApplyIconOverride(dlg.FileName);
    }

    private void OnUploadIconClick(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择图标图片",
            Filter = "图片 (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico;*.webp)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico;*.webp|所有文件 (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MacDock", "icons");
            Directory.CreateDirectory(dir);
            var ext = Path.GetExtension(dlg.FileName);
            if (string.IsNullOrEmpty(ext)) ext = ".png";
            var dest = Path.Combine(dir, Guid.NewGuid().ToString("N") + ext);
            File.Copy(dlg.FileName, dest, true);
            ApplyIconOverride(dest);
        }
        catch (Exception)
        {
            // 复制失败时直接引用原文件
            ApplyIconOverride(dlg.FileName);
        }
    }

    private void OnResetIconClick(object sender, RoutedEventArgs e) => ApplyIconOverride(null);
    private void HookEvents()
    {
        SldIconSize.ValueChanged += (_, _) => { if (_loading) return; _work.IconSize = (int)SldIconSize.Value; UpdateLabels(); SettingsChanged?.Invoke(_work); };
        SldBoost.ValueChanged += (_, _) => { if (_loading) return; _work.MagnifyBoost = SldBoost.Value; UpdateLabels(); SettingsChanged?.Invoke(_work); };
        SldSpacing.ValueChanged += (_, _) => { if (_loading) return; _work.IconSpacing = (int)SldSpacing.Value; UpdateLabels(); SettingsChanged?.Invoke(_work); };
        SldMinW.ValueChanged += (_, _) => { if (_loading) return; _work.BarMinWidth = SldMinW.Value; UpdateLabels(); SettingsChanged?.Invoke(_work); };
        SldMinH.ValueChanged += (_, _) => { if (_loading) return; _work.BarMinHeight = SldMinH.Value; UpdateLabels(); SettingsChanged?.Invoke(_work); };
        SldBgOpacity.ValueChanged += (_, _) => { if (_loading) return; _work.BackgroundOpacity = Math.Round(SldBgOpacity.Value, 2); UpdateLabels(); SettingsChanged?.Invoke(_work); };
        SldOffsetY.ValueChanged += (_, _) => { if (_loading) return; _work.DockOffsetY = (int)SldOffsetY.Value; UpdateLabels(); SettingsChanged?.Invoke(_work); };
        SldOffsetX.ValueChanged += (_, _) => { if (_loading) return; _work.DockOffsetX = (int)SldOffsetX.Value; UpdateLabels(); SettingsChanged?.Invoke(_work); };
        SldCorner.ValueChanged += (_, _) => { if (_loading) return; _work.CornerRadius = (int)SldCorner.Value; UpdateLabels(); SettingsChanged?.Invoke(_work); };
        SldHotzone.ValueChanged += (_, _) => { if (_loading) return; _work.EdgeHotzoneSize = (int)SldHotzone.Value; UpdateLabels(); SettingsChanged?.Invoke(_work); };
        SldFolderGap.ValueChanged += (_, _) => { if (_loading) return; _work.FolderPanelGap = (int)SldFolderGap.Value; UpdateLabels(); SettingsChanged?.Invoke(_work); };
        SldAnimDuration.ValueChanged += (_, _) => { if (_loading) return; _work.AnimationDuration = Math.Round(SldAnimDuration.Value, 2); UpdateLabels(); SettingsChanged?.Invoke(_work); };
        CmbPosition.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            if (CmbPosition.SelectedItem is ComboBoxItem cbi && cbi.Tag is string tag && !string.Equals(tag, _work.DockPosition, StringComparison.Ordinal))
            {
                _work.DockPosition = tag;
                Save();
                SettingsChanged?.Invoke(_work);
            }
        };
        CmbBgStyle.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            if (CmbBgStyle.SelectedItem is ComboBoxItem cbi && cbi.Tag is string tag && !string.Equals(tag, _work.BackgroundStyle, StringComparison.Ordinal))
            {
                _work.BackgroundStyle = tag;
                Save();
                SettingsChanged?.Invoke(_work);
            }
        };
        ChkBlockShow.Checked += (_, _) => { if (_loading) return; _work.BlockShowWhenCovered = true; Save(); SettingsChanged?.Invoke(_work); };
        ChkBlockShow.Unchecked += (_, _) => { if (_loading) return; _work.BlockShowWhenCovered = false; Save(); SettingsChanged?.Invoke(_work); };
        ChkEdgeShow.Checked += (_, _) => { if (_loading) return; _work.ShowOnEdge = true; Save(); SettingsChanged?.Invoke(_work); };
        ChkEdgeShow.Unchecked += (_, _) => { if (_loading) return; _work.ShowOnEdge = false; Save(); SettingsChanged?.Invoke(_work); };
        ChkBorder.Checked += (_, _) => { if (_loading) return; _work.ShowBorder = true; Save(); SettingsChanged?.Invoke(_work); };
        ChkBorder.Unchecked += (_, _) => { if (_loading) return; _work.ShowBorder = false; Save(); SettingsChanged?.Invoke(_work); };
        ChkStartup.Checked += (_, _) => { if (_loading) return; _work.RunOnStartup = true; Save(); SettingsChanged?.Invoke(_work); };
        ChkStartup.Unchecked += (_, _) => { if (_loading) return; _work.RunOnStartup = false; Save(); SettingsChanged?.Invoke(_work); };
        ChkHotkey.Checked += (_, _) => { if (_loading) return; _work.HotkeyEnabled = true; Save(); SettingsChanged?.Invoke(_work); };
        ChkBlockHotkey.Checked += (_, _) => { if (_loading) return; _work.BlockHotkeyEnabled = true; Save(); SettingsChanged?.Invoke(_work); };
        ChkBlockHotkey.Unchecked += (_, _) => { if (_loading) return; _work.BlockHotkeyEnabled = false; Save(); SettingsChanged?.Invoke(_work); };
        ChkHotkey.Unchecked += (_, _) => { if (_loading) return; _work.HotkeyEnabled = false; Save(); SettingsChanged?.Invoke(_work); };
        CmbModifier.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            if (CmbModifier.SelectedItem is string s) { _work.HotkeyModifier = s; Save(); SettingsChanged?.Invoke(_work); }
        };
        CmbBlockModifier.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            if (CmbBlockModifier.SelectedItem is string s) { _work.BlockHotkeyModifier = s; Save(); SettingsChanged?.Invoke(_work); }
        };
        CmbHotkeyKey.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            if (CmbHotkeyKey.SelectedItem is string s) { _work.HotkeyKey = s; Save(); SettingsChanged?.Invoke(_work); }
        };
        CmbBlockKey.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            if (CmbBlockKey.SelectedItem is string s) { _work.BlockHotkeyKey = s; Save(); SettingsChanged?.Invoke(_work); }
        };
        ChkShowFolderLabels.Checked += (_, _) => { if (_loading) return; _work.ShowFolderLabels = true; Save(); SettingsChanged?.Invoke(_work); };
        ChkShowFolderLabels.Unchecked += (_, _) => { if (_loading) return; _work.ShowFolderLabels = false; Save(); SettingsChanged?.Invoke(_work); };
        ChkTaskbarLock.Checked += (_, _) => { if (_loading) return; _work.TaskbarLockEnabled = true; Save(); SettingsChanged?.Invoke(_work); };
        ChkTaskbarLock.Unchecked += (_, _) => { if (_loading) return; _work.TaskbarLockEnabled = false; Save(); SettingsChanged?.Invoke(_work); };
        ChkTaskbarLockHotkey.Checked += (_, _) => { if (_loading) return; _work.TaskbarLockHotkeyEnabled = true; Save(); SettingsChanged?.Invoke(_work); };
        ChkTaskbarLockHotkey.Unchecked += (_, _) => { if (_loading) return; _work.TaskbarLockHotkeyEnabled = false; Save(); SettingsChanged?.Invoke(_work); };
        CmbTaskbarLockModifier.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            if (CmbTaskbarLockModifier.SelectedItem is string s) { _work.TaskbarLockHotkeyModifier = s; Save(); SettingsChanged?.Invoke(_work); }
        };
        CmbTaskbarLockKey.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            if (CmbTaskbarLockKey.SelectedItem is string s) { _work.TaskbarLockHotkeyKey = s; Save(); SettingsChanged?.Invoke(_work); }
        };
        ItemList.SelectionChanged += (_, _) => UpdateButtons();
    }

    private void LoadFromWork()
    {
        SldIconSize.Value = _work.IconSize;
        SldBoost.Value = _work.MagnifyBoost;
        SldSpacing.Value = _work.IconSpacing;
        SldMinW.Value = _work.BarMinWidth;
        SldMinH.Value = _work.BarMinHeight;
        SldBgOpacity.Value = _work.BackgroundOpacity;
        SldOffsetY.Value = _work.DockOffsetY;
        SldOffsetX.Value = _work.DockOffsetX;
        SldCorner.Value = _work.CornerRadius;
        SldHotzone.Value = _work.EdgeHotzoneSize;
        SldFolderGap.Value = _work.FolderPanelGap;
        SldAnimDuration.Value = _work.AnimationDuration;
        SelectComboByTag(CmbPosition, _work.DockPosition);
        SelectComboByTag(CmbBgStyle, _work.BackgroundStyle);
        ChkBlockShow.IsChecked = _work.BlockShowWhenCovered;
        ChkBorder.IsChecked = _work.ShowBorder;
        RefreshBorderSwatchSelection();
        ChkEdgeShow.IsChecked = _work.ShowOnEdge;
        ChkStartup.IsChecked = _work.RunOnStartup;
        ChkHotkey.IsChecked = _work.HotkeyEnabled;
        ChkBlockHotkey.IsChecked = _work.BlockHotkeyEnabled;
        CmbModifier.SelectedItem = _work.HotkeyModifier;
        CmbBlockModifier.SelectedItem = _work.BlockHotkeyModifier;
        SelectKeyCombo(CmbHotkeyKey, _work.HotkeyKey);
        SelectKeyCombo(CmbBlockKey, _work.BlockHotkeyKey);
        ChkShowFolderLabels.IsChecked = _work.ShowFolderLabels;
        ChkTaskbarLock.IsChecked = _work.TaskbarLockEnabled;
        ChkTaskbarLockHotkey.IsChecked = _work.TaskbarLockHotkeyEnabled;
        CmbTaskbarLockModifier.SelectedItem = _work.TaskbarLockHotkeyModifier;
        SelectKeyCombo(CmbTaskbarLockKey, _work.TaskbarLockHotkeyKey);
        RefreshFolderLabelSwatchSelection();
        RefreshSwatchSelection();
        ReloadItemList();
        UpdateLabels();
        UpdateButtons();
    }

    private static void SelectComboByTag(ComboBox combo, string tag)
    {
        foreach (object item in combo.Items)
        {
            if (item is ComboBoxItem cbi && string.Equals(cbi.Tag as string, tag, StringComparison.Ordinal))
            {
                combo.SelectedItem = cbi;
                return;
            }
        }
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
    }

    private static void SelectKeyCombo(ComboBox combo, string key)
    {
        if (!string.IsNullOrEmpty(key))
        {
            foreach (object item in combo.Items)
            {
                if (item is string s && string.Equals(s, key, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedItem = s;
                    return;
                }
            }
            // 保存过但不在列表中的键值：动态加入以便继续显示
            combo.Items.Add(key);
            combo.SelectedItem = key;
            return;
        }
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
    }

    private void UpdateLabels()
    {
        LblIconSize.Text = $"{_work.IconSize} px";
        LblBoost.Text = $"放大 {_work.MagnifyBoost:0.0}（悬停图标约放大至 {1 + _work.MagnifyBoost:0.0} 倍）";
        LblSpacing.Text = $"{_work.IconSpacing} px";
        LblMinW.Text = _work.BarMinWidth > 0 ? $"{_work.BarMinWidth:0} px" : "自动";
        LblMinH.Text = _work.BarMinHeight > 0 ? $"{_work.BarMinHeight:0} px" : "自动";
        LblBgOpacity.Text = $"{_work.BackgroundOpacity:0.00}";
        LblBgColor.Text = _work.BackgroundColor;
        LblOffsetY.Text = $"{_work.DockOffsetY} px";
        LblOffsetX.Text = $"{_work.DockOffsetX} px";
        LblCorner.Text = $"{_work.CornerRadius} px";
        LblHotzone.Text = $"{_work.EdgeHotzoneSize} px";
        LblFolderGap.Text = $"{_work.FolderPanelGap} px";
        LblAnimDuration.Text = $"{_work.AnimationDuration:0.0} 秒";
        LblBorderColor.Text = _work.BorderColor;
        LblFolderLabelColor.Text = _work.FolderLabelColor;
    }

    private void ReloadItemList()
    {
        // ObservableCollection 自动通知增删变化，无需 null/re-set 强刷
        ItemList.ItemsSource = _work.Items;
    }

    /// <summary>外部（如 F3 快捷键）切换“被覆盖禁止唤出”后同步 _work 与勾选框状态。</summary>
    public void RefreshBlockMode(bool newValue)
    {
        _work.BlockShowWhenCovered = newValue;
        if (ChkBlockShow.IsChecked != newValue)
            ChkBlockShow.IsChecked = newValue;
    }

    /// <summary>兼容无参调用，读取 _work 当前值同步勾选框。</summary>
    public void RefreshBlockMode()
    {
        if (ChkBlockShow.IsChecked != _work.BlockShowWhenCovered)
            ChkBlockShow.IsChecked = _work.BlockShowWhenCovered;
    }

    /// <summary>外部（如任务栏锁定快捷键）切换后同步 _work 与勾选框状态。</summary>
    public void RefreshTaskbarLock(bool newValue)
    {
        _work.TaskbarLockEnabled = newValue;
        if (ChkTaskbarLock.IsChecked != newValue)
            ChkTaskbarLock.IsChecked = newValue;
    }

    private void UpdateButtons()
    {
        int i = ItemList.SelectedIndex;
        BtnUp.IsEnabled = i > 0;
        BtnDown.IsEnabled = i >= 0 && i < _work.Items.Count - 1;
        BtnRemove.IsEnabled = i >= 0;
        bool isFolder = i >= 0 && i < _work.Items.Count && _work.Items[i].FolderItems != null;
        BtnPickSoftwareIcon.IsEnabled = isFolder;
        BtnUploadIcon.IsEnabled = isFolder;
        BtnResetIcon.IsEnabled = isFolder;
        foreach (Button b in PresetIconPanel.Children) b.IsEnabled = isFolder;
    }

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择要添加到 Dock 的程序",
            Filter = "应用程序与常用文件 (*.exe;*.lnk;*.bat;*.cmd;*.url;*.ps1)|*.exe;*.lnk;*.bat;*.cmd;*.url;*.ps1|所有文件 (*.*)|*.*",
            Multiselect = true,
        };
        if (dlg.ShowDialog(this) != true) return;
        foreach (var file in dlg.FileNames)
        {
            if (string.IsNullOrEmpty(file)) continue;
            _work.Items.Add(new DockItemModel
            {
                Name = Path.GetFileNameWithoutExtension(file),
                TargetPath = file,
            });
        }
        UpdateButtons();
        Save();
        ItemsChanged?.Invoke(_work);
    }

    private void OnAddFolderClick(object sender, RoutedEventArgs e)
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "选择要添加到 Dock 的文件夹（创建 MacDock 内置文件夹）",
            ShowNewFolderButton = false,
        };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        var folder = dlg.SelectedPath.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        if (string.IsNullOrEmpty(folder)) return;
        if (_work.Items.Any(i => string.Equals(i.TargetPath, folder, StringComparison.OrdinalIgnoreCase))) return;
        string dirName = System.IO.Path.GetFileName(folder);
        _work.Items.Add(new DockItemModel
        {
            Name = string.IsNullOrEmpty(dirName) ? folder : dirName,
            TargetPath = folder,
            FolderItems = BuiltinFolderScanner.Scan(folder),
        });
        UpdateButtons();
        Save();
        ItemsChanged?.Invoke(_work);
    }

    private void OnAddPathClick(object sender, RoutedEventArgs e)
    {
        var full = PathResolver.Normalize(TxtPath.Text);
        if (full.Length == 0) return;
        if (!Directory.Exists(full) && !File.Exists(full))
        {
            MessageBox.Show(this, "路径不存在，请检查后重试。" + System.Environment.NewLine + full, "MacDock", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (_work.Items.Any(i => string.Equals(i.TargetPath, full, StringComparison.OrdinalIgnoreCase))) return;

        if (Directory.Exists(full))
        {
            // 文件夹一律创建为 MacDock 内置文件夹
            string dirName = System.IO.Path.GetFileName(full.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
            _work.Items.Add(new DockItemModel
            {
                Name = string.IsNullOrEmpty(dirName) ? full : dirName,
                TargetPath = full,
                FolderItems = BuiltinFolderScanner.Scan(full),
            });
        }
        else
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(full);
            _work.Items.Add(new DockItemModel
            {
                Name = string.IsNullOrEmpty(name) ? full : name,
                TargetPath = full,
            });
        }
        TxtPath.Clear();
        UpdateButtons();
        Save();
        ItemsChanged?.Invoke(_work);
    }

    private void OnUpClick(object sender, RoutedEventArgs e)
    {
        int i = ItemList.SelectedIndex;
        if (i <= 0) return;
        _work.Items.Move(i, i - 1);
        ItemList.SelectedIndex = i - 1;
        Save();
        ItemsChanged?.Invoke(_work);
    }

    private void OnDownClick(object sender, RoutedEventArgs e)
    {
        int i = ItemList.SelectedIndex;
        if (i < 0 || i >= _work.Items.Count - 1) return;
        _work.Items.Move(i, i + 1);
        ItemList.SelectedIndex = i + 1;
        Save();
        ItemsChanged?.Invoke(_work);
    }

    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        int i = ItemList.SelectedIndex;
        if (i < 0) return;
        _work.Items.RemoveAt(i);
        UpdateButtons();
        Save();
        ItemsChanged?.Invoke(_work);
    }

    private void OnDefaultClick(object sender, RoutedEventArgs e)
    {
        var def = SettingsService.Defaults();
        _work.IconSize = def.IconSize;
        _work.MagnifyBoost = def.MagnifyBoost;
        _work.IconSpacing = def.IconSpacing;
        _work.BarMinWidth = 0;
        _work.BarMinHeight = 0;
        _work.DockPosition = def.DockPosition;
        _work.DockOffsetY = def.DockOffsetY;
        _work.DockOffsetX = def.DockOffsetX;
        _work.CornerRadius = def.CornerRadius;
        _work.EdgeHotzoneSize = def.EdgeHotzoneSize;
        _work.BackgroundStyle = def.BackgroundStyle;
        _work.BackgroundColor = def.BackgroundColor;
        _work.BackgroundOpacity = def.BackgroundOpacity;
        _work.ShowBorder = def.ShowBorder;
        _work.BorderColor = def.BorderColor;
        _work.RunOnStartup = false;
        _work.ShowOnEdge = def.ShowOnEdge;
        _work.HotkeyEnabled = def.HotkeyEnabled;
        _work.HotkeyModifier = def.HotkeyModifier;
        _work.HotkeyKey = def.HotkeyKey;
        _work.BlockHotkeyEnabled = def.BlockHotkeyEnabled;
        _work.BlockHotkeyModifier = def.BlockHotkeyModifier;
        _work.BlockHotkeyKey = def.BlockHotkeyKey;
        _work.BlockShowWhenCovered = def.BlockShowWhenCovered;
        _work.AnimationDuration = def.AnimationDuration;
        _work.ShowFolderLabels = def.ShowFolderLabels;
        _work.FolderLabelColor = def.FolderLabelColor;
        _work.TaskbarLockEnabled = def.TaskbarLockEnabled;
        _work.TaskbarLockHotkeyEnabled = def.TaskbarLockHotkeyEnabled;
        _work.TaskbarLockHotkeyModifier = def.TaskbarLockHotkeyModifier;
        _work.TaskbarLockHotkeyKey = def.TaskbarLockHotkeyKey;
        _work.Items = def.Items;
        LoadFromWork();
        Save();
        SettingsChanged?.Invoke(_work);
        ItemsChanged?.Invoke(_work);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void Save() => _settingsService.Save(_work);
}
