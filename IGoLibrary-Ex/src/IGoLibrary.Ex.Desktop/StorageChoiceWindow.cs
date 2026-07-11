using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace IGoLibrary.Ex.Desktop;

internal enum StorageDialogChoice
{
    Cancel,
    Primary,
    Secondary
}

internal sealed class StorageChoiceWindow : Window
{
    public StorageChoiceWindow(
        string title,
        string message,
        string primaryText,
        string? secondaryText)
    {
        Title = title;
        Width = 620;
        SizeToContent = SizeToContent.Height;
        MinWidth = 460;
        MaxHeight = 680;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var cancelButton = CreateButton("取消", 92);
        cancelButton.Click += (_, _) => Close(StorageDialogChoice.Cancel);

        var primaryButton = CreateButton(primaryText, 132);
        primaryButton.Click += (_, _) => Close(StorageDialogChoice.Primary);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
            Children = { cancelButton }
        };
        if (!string.IsNullOrWhiteSpace(secondaryText))
        {
            var secondaryButton = CreateButton(secondaryText, 132);
            secondaryButton.Click += (_, _) => Close(StorageDialogChoice.Secondary);
            buttons.Children.Add(secondaryButton);
        }

        buttons.Children.Add(primaryButton);
        Content = new Border
        {
            Padding = new Thickness(24),
            Child = new StackPanel
            {
                Spacing = 18,
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        FontSize = 22,
                        FontWeight = FontWeight.Bold
                    },
                    new SelectableTextBlock
                    {
                        Text = message,
                        FontSize = 13,
                        TextWrapping = TextWrapping.Wrap
                    },
                    buttons
                }
            }
        };
    }

    private static Button CreateButton(string text, double minWidth)
        => new()
        {
            Content = text,
            MinWidth = minWidth,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
}
