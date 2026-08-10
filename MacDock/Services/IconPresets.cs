using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MacDock.Services;

/// <summary>内置文件夹预设图标：彩色圆角底 + 简洁白色符号，供“图标样式”设置使用。</summary>
public static class IconPresets
{
    public static readonly (string Key, string Label)[] Items =
    {
        ("game", "游戏"),
        ("tool", "工具"),
        ("ai", "AI"),
        ("design", "设计"),
        ("music", "音乐"),
        ("doc", "文档"),
        ("img", "图片"),
        ("code", "代码"),
    };

    public static string LabelOf(string key)
    {
        foreach (var (k, label) in Items)
            if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) return label;
        return key;
    }

    public static BitmapSource Draw(string key, int size)
    {
        size = Math.Max(16, size);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            double w = size;
            double h = size;
            var (c1, c2) = ColorsOf(key);
            var bg = new LinearGradientBrush(c1, c2, new Point(0, 0), new Point(0, 1));
            bg.Freeze();
            dc.DrawRoundedRectangle(bg, null, new Rect(0, 0, w, h), h * 0.22, h * 0.22);
            var pen = new Pen(Brushes.White, Math.Max(1.5, h * 0.065));
            pen.Freeze();
            DrawSymbol(dc, key, pen, w, h);
        }
        var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }

    private static (Color, Color) ColorsOf(string key)
    {
        return key.ToLowerInvariant() switch
        {
            "game" => (Color.FromRgb(0x6C, 0x5C, 0xE7), Color.FromRgb(0x3E, 0x2F, 0x9E)),
            "tool" => (Color.FromRgb(0xE8, 0x8A, 0x3A), Color.FromRgb(0xB8, 0x5B, 0x1A)),
            "ai" => (Color.FromRgb(0x3A, 0xB8, 0xE0), Color.FromRgb(0x1A, 0x6E, 0x9E)),
            "design" => (Color.FromRgb(0xE0, 0x5C, 0xA8), Color.FromRgb(0x9E, 0x2F, 0x6E)),
            "music" => (Color.FromRgb(0x5C, 0xE0, 0xA0), Color.FromRgb(0x2F, 0x9E, 0x6E)),
            "doc" => (Color.FromRgb(0x5C, 0xA0, 0xE8), Color.FromRgb(0x2F, 0x6E, 0x9E)),
            "img" => (Color.FromRgb(0xE8, 0xC8, 0x4A), Color.FromRgb(0xB8, 0x8A, 0x1A)),
            "code" => (Color.FromRgb(0x50, 0xC8, 0xB0), Color.FromRgb(0x2E, 0x8A, 0x74)),
            _ => (Color.FromRgb(0x8A, 0x8A, 0x9A), Color.FromRgb(0x5A, 0x5A, 0x6A)),
        };
    }

    private static void DrawSymbol(DrawingContext dc, string key, Pen pen, double w, double h)
    {
        double u = Math.Min(w, h);
        switch (key.ToLowerInvariant())
        {
            case "game":
            {
                // 手柄主体
                var body = new Rect(w * 0.16, h * 0.42, w * 0.68, h * 0.30);
                dc.DrawRoundedRectangle(null, pen, body, u * 0.10, u * 0.10);
                // 十字键
                dc.DrawLine(pen, new Point(w * 0.38, h * 0.50), new Point(w * 0.46, h * 0.50));
                dc.DrawLine(pen, new Point(w * 0.42, h * 0.46), new Point(w * 0.42, h * 0.54));
                // 右按钮
                dc.DrawEllipse(null, pen, new Point(w * 0.60, h * 0.50), u * 0.045, u * 0.045);
                dc.DrawEllipse(null, pen, new Point(w * 0.70, h * 0.50), u * 0.045, u * 0.045);
                break;
            }
            case "tool":
            {
                // 扳手：圆环 + 手柄
                dc.DrawEllipse(null, pen, new Point(w * 0.44, h * 0.38), u * 0.13, u * 0.13);
                dc.DrawLine(pen, new Point(w * 0.54, h * 0.48), new Point(w * 0.72, h * 0.66));
                dc.DrawEllipse(null, pen, new Point(w * 0.70, h * 0.68), u * 0.05, u * 0.05);
                break;
            }
            case "ai":
            {
                // 芯片
                var chip = new Rect(w * 0.28, h * 0.28, w * 0.44, h * 0.44);
                dc.DrawRectangle(null, pen, chip);
                dc.DrawLine(pen, new Point(w * 0.36, h * 0.20), new Point(w * 0.36, h * 0.28));
                dc.DrawLine(pen, new Point(w * 0.64, h * 0.20), new Point(w * 0.64, h * 0.28));
                dc.DrawLine(pen, new Point(w * 0.36, h * 0.72), new Point(w * 0.36, h * 0.80));
                dc.DrawLine(pen, new Point(w * 0.64, h * 0.72), new Point(w * 0.64, h * 0.80));
                dc.DrawLine(pen, new Point(w * 0.20, h * 0.36), new Point(w * 0.28, h * 0.36));
                dc.DrawLine(pen, new Point(w * 0.20, h * 0.64), new Point(w * 0.28, h * 0.64));
                dc.DrawLine(pen, new Point(w * 0.72, h * 0.36), new Point(w * 0.80, h * 0.36));
                dc.DrawLine(pen, new Point(w * 0.72, h * 0.64), new Point(w * 0.80, h * 0.64));
                break;
            }
            case "design":
            {
                // 调色板：圆 + 顶部缺口
                dc.DrawEllipse(null, pen, new Point(w * 0.5, h * 0.52), u * 0.28, u * 0.28);
                dc.DrawEllipse(null, pen, new Point(w * 0.5, h * 0.42), u * 0.06, u * 0.06);
                dc.DrawEllipse(null, pen, new Point(w * 0.34, h * 0.52), u * 0.05, u * 0.05);
                dc.DrawEllipse(null, pen, new Point(w * 0.56, h * 0.60), u * 0.05, u * 0.05);
                dc.DrawEllipse(null, pen, new Point(w * 0.62, h * 0.44), u * 0.05, u * 0.05);
                break;
            }
            case "music":
            {
                // 两个八分音符
                dc.DrawEllipse(null, pen, new Point(w * 0.34, h * 0.68), u * 0.10, u * 0.07);
                dc.DrawEllipse(null, pen, new Point(w * 0.64, h * 0.60), u * 0.10, u * 0.07);
                dc.DrawLine(pen, new Point(w * 0.42, h * 0.66), new Point(w * 0.42, h * 0.30));
                dc.DrawLine(pen, new Point(w * 0.72, h * 0.58), new Point(w * 0.72, h * 0.24));
                dc.DrawLine(pen, new Point(w * 0.42, h * 0.30), new Point(w * 0.72, h * 0.24));
                break;
            }
            case "doc":
            {
                var body = new Rect(w * 0.26, h * 0.18, w * 0.48, h * 0.64);
                dc.DrawRectangle(null, pen, body);
                dc.DrawLine(pen, new Point(w * 0.38, h * 0.34), new Point(w * 0.62, h * 0.34));
                dc.DrawLine(pen, new Point(w * 0.38, h * 0.46), new Point(w * 0.62, h * 0.46));
                dc.DrawLine(pen, new Point(w * 0.38, h * 0.58), new Point(w * 0.56, h * 0.58));
                break;
            }
            case "img":
            {
                var body = new Rect(w * 0.20, h * 0.24, w * 0.60, h * 0.52);
                dc.DrawRectangle(null, pen, body);
                // 太阳
                dc.DrawEllipse(null, pen, new Point(w * 0.38, h * 0.40), u * 0.05, u * 0.05);
                // 山
                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    ctx.BeginFigure(new Point(w * 0.28, h * 0.62), true, true);
                    ctx.LineTo(new Point(w * 0.44, h * 0.44), true, false);
                    ctx.LineTo(new Point(w * 0.58, h * 0.62), true, false);
                    ctx.Close();
                }
                geo.Freeze();
                dc.DrawGeometry(null, pen, geo);
                break;
            }
            case "code":
            {
                // 尖括号 </ >
                dc.DrawLine(pen, new Point(w * 0.32, h * 0.30), new Point(w * 0.20, h * 0.50));
                dc.DrawLine(pen, new Point(w * 0.20, h * 0.50), new Point(w * 0.32, h * 0.70));
                dc.DrawLine(pen, new Point(w * 0.68, h * 0.30), new Point(w * 0.80, h * 0.50));
                dc.DrawLine(pen, new Point(w * 0.80, h * 0.50), new Point(w * 0.68, h * 0.70));
                // 斜杠
                dc.DrawLine(pen, new Point(w * 0.56, h * 0.28), new Point(w * 0.44, h * 0.72));
                break;
            }
            default:
            {
                // 问号
                var q = new FormattedText("?", System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, new Typeface("Segoe UI"), h * 0.55, Brushes.White);
                dc.DrawText(q, new Point(w * 0.26, h * 0.14));
                break;
            }
        }
    }
}
