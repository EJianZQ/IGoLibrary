using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using IGoLibrary.Ex.Application.Backup;

namespace IGoLibrary.Ex.Desktop;

internal sealed class BackupPasswordWindow : Window
{
    private readonly TextBox _passwordBox;
    private readonly TextBox? _confirmationBox;
    private readonly TextBlock _validationText;

    internal TextBox PasswordBox => _passwordBox;

    internal TextBox? ConfirmationBox => _confirmationBox;

    public BackupPasswordWindow(string title, string message, bool requireConfirmation)
    {
        Title = title;
        Width = 540;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _passwordBox = CreatePasswordBox("备份密码（12～256 位）");
        _confirmationBox = requireConfirmation ? CreatePasswordBox("再次输入备份密码") : null;
        _validationText = new TextBlock
        {
            Foreground = Brushes.IndianRed,
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false
        };

        var cancel = CreateButton("取消", 92);
        cancel.Click += (_, _) => Close(null);
        var confirm = CreateButton(requireConfirmation ? "保存密码" : "继续", 112);
        confirm.Click += (_, _) => Confirm();
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
            Children = { cancel, confirm }
        };
        var content = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeight.Bold },
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                _passwordBox
            }
        };
        if (_confirmationBox is not null)
        {
            content.Children.Add(_confirmationBox);
        }

        content.Children.Add(_validationText);
        content.Children.Add(buttons);
        Content = new Border { Padding = new Thickness(24), Child = content };
        Opened += (_, _) => _passwordBox.Focus();
    }

    private void Confirm()
    {
        var password = _passwordBox.Text ?? string.Empty;
        try
        {
            BackupPasswordRules.Validate(password);
            if (_confirmationBox is not null && !string.Equals(password, _confirmationBox.Text, StringComparison.Ordinal))
            {
                throw new ArgumentException("两次输入的备份密码不一致");
            }

            Close(password);
        }
        catch (ArgumentException ex)
        {
            _validationText.Text = ex.Message;
            _validationText.IsVisible = true;
        }
    }

    private static TextBox CreatePasswordBox(string watermark)
        => new()
        {
            Watermark = watermark,
            PasswordChar = '●',
            RevealPassword = false,
            MinHeight = 40
        };

    private static Button CreateButton(string text, double minWidth)
        => new() { Content = text, MinWidth = minWidth };
}
