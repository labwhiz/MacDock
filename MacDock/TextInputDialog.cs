using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MacDock;

/// <summary>简单的单行文本输入对话框（用于文件夹重命名）。</summary>
public class TextInputDialog : Window
{
    private readonly TextBox _input;

    public TextInputDialog(string title, string label, string initial)
    {
        Title = title;
        Width = 320;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStyle = WindowStyle.ToolWindow;
        FontFamily = new FontFamily("Microsoft YaHei UI");

        var root = new StackPanel { Margin = new Thickness(14) };
        root.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 6) });
        _input = new TextBox { Text = initial, Margin = new Thickness(0, 0, 0, 10) };
        _input.SelectAll();
        root.Children.Add(_input);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = "确定", Width = 76, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        ok.Click += (_, _) => { DialogResult = true; };
        var cancel = new Button { Content = "取消", Width = 76, IsCancel = true };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);

        Content = root;
        Loaded += (_, _) => _input.Focus();
        _input.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { DialogResult = true; }
        };
    }

    public string Value => _input.Text.Trim();
}
