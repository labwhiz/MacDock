using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace MacDock.Services;

/// <summary>把目标路径转换为图标，用于列表/绑定场景。</summary>
public class IconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string path)
        {
            int size = 56;
            if (parameter is string s && int.TryParse(s, out var n)) size = n;
            return IconService.GetIcon(path, size);
        }
        return Binding.DoNothing;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}


/// <summary>Dock 项图标转换：内置文件夹显示文件夹图标，普通项显示目标图标。用于设置列表绑定。</summary>
public class DockItemIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is MacDock.Models.DockItemModel item)
        {
            int size = 56;
            if (parameter is string s && int.TryParse(s, out var n)) size = n;
            if (item.FolderItems != null)
                return IconService.GetItemIcon(item, Math.Max(32, (int)(Math.Round(size * 2 / 16.0) * 16)));
            return IconService.GetIcon(item.TargetPath, size);
        }
        return Binding.DoNothing;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
