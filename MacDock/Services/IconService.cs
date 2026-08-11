using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MacDock.Native;

namespace MacDock.Services;

/// <summary>图标提取：优先高分辨率 IShellItemImageFactory，回退 SHGetFileInfo。</summary>
public class IconService
{
    private static readonly Dictionary<string, BitmapSource> Cache = new();

    /// <summary>缓存上限，超过时清空重建（BitmapSource 已 Freeze，已显示的图标不受影响）。</summary>
    private const int MaxCacheSize = 256;

    /// <summary>向缓存中添加条目，超过上限时先清空。</summary>
    private static void AddToCache(string key, BitmapSource value)
    {
        if (Cache.Count >= MaxCacheSize)
            Cache.Clear();
        Cache[key] = value;
    }

    private static readonly BitmapSource Fallback = CreateFallback();

    /// <summary>允许加载的图片扩展名，防止任意文件类型进入图片解码器。</summary>
    private static readonly HashSet<string> SupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".ico", ".webp", ".tiff", ".wdp",
    };

    public static BitmapSource GetIcon(string targetPath, int size)
    {
        var resolved = PathResolver.Resolve(targetPath);
        var key = resolved + "@" + size;
        if (Cache.TryGetValue(key, out var cached)) return cached;

        // 文件夹：直接返回自绘的圆角文件夹图标（无描边、透明背景），
        // 系统文件夹图标在部分尺寸下会带描边/外框，不符合要求。
        if (Directory.Exists(resolved))
        {
            var folderIcon = GetFolderIcon(size);
            AddToCache(key, folderIcon);
            return folderIcon;
        }

        BitmapSource? icon = null;
        try
        {
            icon = LoadHighRes(resolved, size);
            if (icon == null)
                icon = LoadLegacy(resolved);
        }
        catch (Exception)
        {
            icon = null;
        }

        icon ??= Fallback;
        AddToCache(key, icon);
        return icon;
    }

    /// <summary>获取 Dock 项图标：内置文件夹优先使用自定义图标（预设 / 图片 / 软件图标），否则用默认文件夹图标。</summary>
    public static BitmapSource GetItemIcon(MacDock.Models.DockItemModel item, int size)
    {
        if (item.FolderItems != null)
        {
            if (!string.IsNullOrWhiteSpace(item.IconOverride))
            {
                var ov = item.IconOverride.Trim();
                if (ov.StartsWith("preset:", StringComparison.OrdinalIgnoreCase))
                {
                    var key = ov.Substring(7).Trim();
                    var cacheKey = "preset:" + key.ToLowerInvariant() + "@" + size;
                    if (Cache.TryGetValue(cacheKey, out var cachedPreset)) return cachedPreset;
                    var bmp = IconPresets.Draw(key, size);
                    AddToCache(cacheKey, bmp);
                    return bmp;
                }
                var path = PathResolver.Resolve(ov);
                var img = LoadImageFile(path, size);
                if (img != null)
                {
                    var cacheKey = "custom:" + path + "@" + size;
                    AddToCache(cacheKey, img);
                    return img;
                }
                if (File.Exists(path) || Directory.Exists(path))
                    return GetIcon(path, size);
            }
            return GetFolderIcon(size);
        }
        return GetIcon(item.TargetPath, size);
    }

    /// <summary>加载本地图片文件（png/jpg/jpeg/bmp/ico/webp 等），失败返回 null。</summary>
    public static BitmapSource? LoadImageFile(string path, int size)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        if (!SupportedImageExtensions.Contains(Path.GetExtension(path))) return null;
        try
        {
            var uri = new Uri(path, UriKind.Absolute);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = uri;
            // 限制解码尺寸，防止超大图片导致内存占用过高
            if (size > 0) bmp.DecodePixelWidth = Math.Max(16, Math.Min(size * 2, 1024));
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>自绘圆角文件夹图标：无边框、透明背景，避免系统图标带描边。内置文件夹也使用该图标。</summary>
    public static BitmapSource GetFolderIcon(int size)
    {
        size = Math.Max(16, size);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            double w = size;
            double h = size;
            var body = new LinearGradientBrush(
                Color.FromRgb(0xFF, 0xD8, 0x6A),
                Color.FromRgb(0xF4, 0xB3, 0x38),
                new Point(0, 0), new Point(0, 1));
            body.Freeze();
            var tab = new SolidColorBrush(Color.FromRgb(0xEF, 0xB1, 0x24));
            tab.Freeze();
            // 顶部标签页
            var tabRect = new Rect(w * 0.10, h * 0.26, w * 0.38, h * 0.18);
            dc.DrawRoundedRectangle(tab, null, tabRect, h * 0.06, h * 0.06);
            // 主体（圆角矩形，无描边）
            var bodyRect = new Rect(w * 0.10, h * 0.38, w * 0.80, h * 0.56);
            dc.DrawRoundedRectangle(body, null, bodyRect, h * 0.09, h * 0.09);
        }
        var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }

    private static BitmapSource? LoadHighRes(string path, int size)
    {
        var hbm = ShellItemInterop.GetIconHBitmap(path, size);
        if (hbm == null || hbm.Value == IntPtr.Zero) return null;
        try
        {
            var src = Imaging.CreateBitmapSourceFromHBitmap(hbm.Value, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        finally
        {
            DeleteObject(hbm.Value);
        }
    }

    private static BitmapSource? LoadLegacy(string path)
    {
        var info = new Win32.SHFILEINFO();
        var hIcon = Win32.SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<Win32.SHFILEINFO>(), Win32.SHGFI_ICON | Win32.SHGFI_LARGEICON);
        if (hIcon == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;
        try
        {
            GetIconInfo(info.hIcon, out var iconInfo);
            if (iconInfo.hbmColor != IntPtr.Zero)
            {
                var src = Imaging.CreateBitmapSourceFromHBitmap(iconInfo.hbmColor, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                DeleteObject(iconInfo.hbmColor);
                if (iconInfo.hbmMask != IntPtr.Zero) DeleteObject(iconInfo.hbmMask);
                src.Freeze();
                return src;
            }
        }
        finally
        {
            DestroyIcon(info.hIcon);
        }
        return null;
    }

    private static BitmapSource CreateFallback()
    {
        var bmp = new WriteableBitmap(32, 32, 96, 96, PixelFormats.Bgra32, null);
        var px = new byte[32 * 32 * 4];
        for (int i = 0; i < px.Length; i += 4) { px[i] = 200; px[i + 1] = 200; px[i + 2] = 200; px[i + 3] = 255; }
        bmp.WritePixels(new Int32Rect(0, 0, 32, 32), px, 128, 0);
        bmp.Freeze();
        return bmp;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [DllImport("user32.dll")]
    private static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);
}

