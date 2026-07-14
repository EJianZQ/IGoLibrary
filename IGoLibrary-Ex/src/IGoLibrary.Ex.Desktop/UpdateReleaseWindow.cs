using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using AvaloniaApplication = Avalonia.Application;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Desktop.Services;
using Markdown.Avalonia.Full;

namespace IGoLibrary.Ex.Desktop;

public sealed class UpdateReleaseWindow : Window
{
    private const double ReleaseBodyScrollBarContentInset = 20;
    private const string MarkdownDocumentClass = "Markdown_Avalonia_MarkdownViewer";

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
            Content = "前往 GitHub",
            MinWidth = 125,
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

        var buttons = new List<Control>
        {
            laterButton,
            skipButton,
            openButton
        };
        if (ShouldShowAutomaticInstall(release, OperatingSystem.IsWindows()))
        {
            var installButton = new Button
            {
                Content = "下载并安装",
                MinWidth = 125,
                HorizontalContentAlignment = HorizontalAlignment.Center
            };
            installButton.Classes.Add("accent");
            installButton.Click += (_, _) => Close(UpdateDialogResult.DownloadAndInstall);
            buttons.Add(installButton);
        }

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
            [Grid.RowProperty] = 3
        };
        foreach (var button in buttons)
        {
            buttonPanel.Children.Add(button);
        }

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
                        Child = CreateReleaseBodyViewer(release.Body),
                        [Grid.RowProperty] = 2
                    },
                    buttonPanel
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

    internal static MarkdownScrollViewer CreateReleaseBodyViewer(string? body)
    {
        var releaseBody = string.IsNullOrWhiteSpace(body)
            ? "此版本没有填写更新说明"
            : body.Trim();

        var viewer = new MarkdownScrollViewer
        {
            Markdown = releaseBody,
            SelectionEnabled = true,
            SelectionBrush = ResolveBrush("AppErrorSelectionBrush", "#2B2563EB")
        };
        viewer.Styles.Add(CreateMarkdownDocumentInsetStyle());

        return viewer;
    }

    internal static Style CreateMarkdownDocumentInsetStyle()
    {
        return new Style(selector => selector.OfType<Control>().Class(MarkdownDocumentClass))
        {
            Setters =
            {
                new Setter(Layoutable.MarginProperty, new Thickness(0, 0, ReleaseBodyScrollBarContentInset, 0))
            }
        };
    }

    internal static bool ShouldShowAutomaticInstall(
        ReleaseUpdateInfo release,
        bool isWindows)
    {
        return isWindows && release.WindowsX64Package is not null;
    }

    private static string BuildReleaseSubtitle(ReleaseUpdateInfo release)
    {
        const string channel = "正式版本";
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
