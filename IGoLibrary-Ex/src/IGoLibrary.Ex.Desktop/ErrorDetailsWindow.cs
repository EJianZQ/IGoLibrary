using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaApplication = Avalonia.Application;

namespace IGoLibrary.Ex.Desktop;

public sealed class ErrorDetailsWindow : Window
{
    public ErrorDetailsWindow(string title, string errorType, string errorMessage)
    {
        Title = title;
        Width = 560;
        Height = 320;
        MinWidth = 460;
        MinHeight = 260;
        Background = ResolveBrush("AppErrorWindowBackgroundBrush", "#FFF7F8FA");
        CanResize = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var okButton = new Button
        {
            Content = "确定",
            HorizontalAlignment = HorizontalAlignment.Right,
            MinWidth = 96
        };
        okButton.Click += (_, _) => Close();

        var detailText = new SelectableTextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontFamily = FontFamily.Parse("Consolas, Cascadia Mono, Monaco, monospace"),
            FontSize = 13,
            Foreground = ResolveBrush("AppErrorPrimaryTextBrush", "#FF1F2937"),
            Text = $"错误类型：{errorType}{Environment.NewLine}{Environment.NewLine}具体错误：{errorMessage}",
            SelectionBrush = ResolveBrush("AppErrorSelectionBrush", "#2B2563EB"),
            SelectionForegroundBrush = ResolveBrush("AppErrorSelectionForegroundBrush", "#FF111827")
        };

        Content = new Border
        {
            Margin = new Thickness(12),
            Padding = new Thickness(20),
            CornerRadius = new CornerRadius(18),
            Background = ResolveBrush("AppErrorPanelBackgroundBrush", "#FFFFFFFF"),
            BoxShadow = BoxShadows.Parse("0 10 28 0 #160F172A"),
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
                RowSpacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        FontSize = 20,
                        FontWeight = FontWeight.Bold,
                        Foreground = ResolveBrush("AppErrorPrimaryTextBrush", "#FF1F2937")
                    },
                    new TextBlock
                    {
                        Text = "请根据下列信息检查 SMTP 配置、网络连通性和授权码。",
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = ResolveBrush("AppErrorSecondaryTextBrush", "#FF4B5563"),
                        [Grid.RowProperty] = 1
                    },
                    new Border
                    {
                        Background = ResolveBrush("AppErrorDetailBackgroundBrush", "#FFF8F2F2"),
                        CornerRadius = new CornerRadius(10),
                        Padding = new Thickness(12,10),
                        Child = new ScrollViewer
                        {
                            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                            Content = detailText
                        },
                        [Grid.RowProperty] = 2
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children =
                        {
                            okButton
                        },
                        [Grid.RowProperty] = 3
                    }
                }
            }
        };
    }

    private static IBrush ResolveBrush(string resourceKey, string fallbackColor)
    {
        var app = AvaloniaApplication.Current;
        if (app?.TryGetResource(
                resourceKey,
                app.ActualThemeVariant,
                out var resource) == true &&
            resource is IBrush brush)
        {
            return brush;
        }

        return new SolidColorBrush(Color.Parse(fallbackColor));
    }
}
