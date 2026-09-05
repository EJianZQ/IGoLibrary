using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using AvaloniaApplication = Avalonia.Application;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Updates;
using IGoLibrary.Ex.Desktop.Services;
using Markdown.Avalonia.Full;

namespace IGoLibrary.Ex.Desktop;

public sealed class UpdateReleaseWindow : Window
{
    private const double ReleaseBodyScrollBarContentInset = 20;
    private const string MarkdownDocumentClass = "Markdown_Avalonia_MarkdownViewer";

    public UpdateReleaseWindow(
        ReleaseUpdateInfo release,
        ReleaseVersion currentVersion)
    {
        Title = BuildWindowTitle(currentVersion.ToString());
        Width = 680;
        Height = 520;
        MinWidth = 520;
        MinHeight = 360;
        Background = ResolveBrush("AppErrorWindowBackgroundBrush", "#FFF7F8FA");
        CanResize = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var isWindows = OperatingSystem.IsWindows();
        var compatibilityWarning = BuildAutomaticUpdateWarning(
            release,
            currentVersion,
            isWindows);
        var openButton = new Button
        {
            Name = "ReleasePageButton",
            Content = compatibilityWarning is null
                ? "前往 GitHub"
                : "前往 GitHub 手动下载",
            MinWidth = 125,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        if (compatibilityWarning is not null)
        {
            openButton.Classes.Add("accent");
        }
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
        if (ShouldShowAutomaticInstall(release, currentVersion, isWindows))
        {
            var installButton = new Button
            {
                Name = "AutomaticInstallButton",
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
        };
        foreach (var button in buttons)
        {
            buttonPanel.Children.Add(button);
        }

        var bodyRow = compatibilityWarning is null ? 2 : 3;
        var buttonRow = compatibilityWarning is null ? 3 : 4;
        Grid.SetRow(buttonPanel, buttonRow);

        var contentGrid = new Grid
        {
            RowDefinitions = new RowDefinitions(
                compatibilityWarning is null
                    ? "Auto,Auto,*,Auto"
                    : "Auto,Auto,Auto,*,Auto"),
            RowSpacing = 14
        };
        contentGrid.Children.Add(new StackPanel
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
        });
        contentGrid.Children.Add(new TextBlock
        {
            Text = release.Name,
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Foreground = ResolveBrush("AppErrorPrimaryTextBrush", "#FF1F2937"),
            [Grid.RowProperty] = 1
        });
        if (compatibilityWarning is not null)
        {
            contentGrid.Children.Add(CreateAutomaticUpdateWarning(compatibilityWarning));
        }

        contentGrid.Children.Add(new Border
        {
            Background = ResolveBrush("AppErrorDetailBackgroundBrush", "#FFF8FAFC"),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 10),
            Child = CreateReleaseBodyViewer(release.Body),
            [Grid.RowProperty] = bodyRow
        });
        contentGrid.Children.Add(buttonPanel);

        Content = new Border
        {
            Margin = new Thickness(12),
            Padding = new Thickness(22),
            CornerRadius = new CornerRadius(18),
            Background = ResolveBrush("AppErrorPanelBackgroundBrush", "#FFFFFFFF"),
            BoxShadow = BoxShadows.Parse("0 10 28 0 #160F172A"),
            Child = contentGrid
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
        ReleaseVersion currentVersion,
        bool isWindows)
    {
        return isWindows &&
               release.WindowsX64Package is not null &&
               release.AutomaticUpdatePolicy.Evaluate(currentVersion) ==
               AutomaticUpdateEligibility.Supported;
    }

    internal static string? BuildAutomaticUpdateWarning(
        ReleaseUpdateInfo release,
        ReleaseVersion currentVersion,
        bool isWindows)
    {
        if (!isWindows ||
            release.AutomaticUpdatePolicy.Evaluate(currentVersion) ==
            AutomaticUpdateEligibility.Supported)
        {
            return null;
        }

        return AutomaticUpdateCompatibilityMessages.BuildBlockedMessage(
            release.AutomaticUpdatePolicy,
            currentVersion);
    }

    private static Border CreateAutomaticUpdateWarning(string message)
    {
        return new Border
        {
            Name = "AutomaticUpdateWarning",
            Background = ResolveBrush("AppWarningBackgroundBrush", "#FFFFF7ED"),
            BorderBrush = ResolveBrush("AppWarningBorderBrush", "#FFF59E0B"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 10),
            [Grid.RowProperty] = 2,
            Child = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new TextBlock
                    {
                        Text = "无法自动更新",
                        FontWeight = FontWeight.SemiBold,
                        Foreground = ResolveBrush("AppWarningTextBrush", "#FF9A3412")
                    },
                    new TextBlock
                    {
                        Name = "AutomaticUpdateWarningText",
                        Text = message,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = ResolveBrush("AppWarningTextBrush", "#FF9A3412")
                    }
                }
            }
        };
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
