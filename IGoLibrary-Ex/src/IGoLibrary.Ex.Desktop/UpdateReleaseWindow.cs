using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaApplication = Avalonia.Application;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Desktop.Services;

namespace IGoLibrary.Ex.Desktop;

public sealed class UpdateReleaseWindow : Window
{
    public UpdateReleaseWindow(
        ReleaseUpdateInfo release,
        string currentVersionText)
    {
        Title = BuildWindowTitle(currentVersionText);
        Width = 680;
        Height = 520;
        MinWidth = 520;
        MinHeight = 360;
        Background = ResolveBrush("AppErrorWindowBackgroundBrush", "#FFF7F8FA");
        CanResize = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var openButton = new Button
        {
            Content = "前往 GitHub Release 页面",
            MinWidth = 180,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        openButton.Click += (_, _) =>
        {
            Close(UpdateDialogResult.OpenReleasePage);
        };

        var skipButton = new Button
        {
            Content = "跳过此版本",
            MinWidth = 120,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        skipButton.Click += (_, _) => Close(UpdateDialogResult.SkipVersion);

        var laterButton = new Button
        {
            Content = "稍后提醒",
            MinWidth = 110,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        laterButton.Click += (_, _) => Close(UpdateDialogResult.Later);

        var releaseBody = string.IsNullOrWhiteSpace(release.Body)
            ? "此版本没有填写更新说明。"
            : release.Body.Trim();

        Content = new Border
        {
            Margin = new Thickness(12),
            Padding = new Thickness(22),
            CornerRadius = new CornerRadius(18),
            Background = ResolveBrush("AppErrorPanelBackgroundBrush", "#FFFFFFFF"),
            BoxShadow = BoxShadows.Parse("0 10 28 0 #160F172A"),
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
                RowSpacing = 14,
                Children =
                {
                    new StackPanel
                    {
                        Spacing = 8,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = $"发现新版本：{release.TagName}",
                                FontSize = 22,
                                FontWeight = FontWeight.Bold,
                                Foreground = ResolveBrush("AppErrorPrimaryTextBrush", "#FF1F2937")
                            },
                            new TextBlock
                            {
                                Text = BuildReleaseSubtitle(release),
                                TextWrapping = TextWrapping.Wrap,
                                Foreground = ResolveBrush("AppErrorSecondaryTextBrush", "#FF4B5563")
                            }
                        }
                    },
                    new TextBlock
                    {
                        Text = release.Name,
                        FontSize = 16,
                        FontWeight = FontWeight.SemiBold,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = ResolveBrush("AppErrorPrimaryTextBrush", "#FF1F2937"),
                        [Grid.RowProperty] = 1
                    },
                    new Border
                    {
                        Background = ResolveBrush("AppErrorDetailBackgroundBrush", "#FFF8FAFC"),
                        CornerRadius = new CornerRadius(10),
                        Padding = new Thickness(12, 10),
                        Child = new ScrollViewer
                        {
                            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                            Content = new SelectableTextBlock
                            {
                                Text = releaseBody,
                                TextWrapping = TextWrapping.Wrap,
                                FontFamily = FontFamily.Parse("Consolas, Cascadia Mono, Monaco, monospace"),
                                FontSize = 13,
                                Foreground = ResolveBrush("AppErrorPrimaryTextBrush", "#FF1F2937"),
                                SelectionBrush = ResolveBrush("AppErrorSelectionBrush", "#2B2563EB"),
                                SelectionForegroundBrush = ResolveBrush("AppErrorSelectionForegroundBrush", "#FF111827")
                            }
                        },
                        [Grid.RowProperty] = 2
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 10,
                        Children =
                        {
                            laterButton,
                            skipButton,
                            openButton
                        },
                        [Grid.RowProperty] = 3
                    }
                }
            }
        };
    }

    internal static string BuildWindowTitle(string? currentVersionText)
    {
        var normalizedVersion = string.IsNullOrWhiteSpace(currentVersionText)
            ? "0.0.0"
            : currentVersionText.Trim();
        if (normalizedVersion.StartsWith('v') || normalizedVersion.StartsWith('V'))
        {
            normalizedVersion = normalizedVersion[1..];
        }

        return $"发现新版本 - 当前版本号 v{normalizedVersion}";
    }

    private static string BuildReleaseSubtitle(ReleaseUpdateInfo release)
    {
        var channel = release.IsPrerelease ? "预发布版本" : "正式版本";
        return release.PublishedAt is { } publishedAt
            ? $"{channel} · 发布于 {publishedAt.LocalDateTime:yyyy-MM-dd HH:mm}"
            : channel;
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
