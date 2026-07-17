using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using IGoLibrary.Ex.Desktop.Services;

namespace IGoLibrary.Ex.Desktop;

internal sealed class CloudflaredDownloadProgressWindow : Window
{
    private readonly ICloudflaredInstallService _installService;
    private readonly CancellationTokenSource _cancellation;
    private readonly TextBlock _statusText;
    private readonly TextBlock _detailText;
    private readonly ProgressBar _progressBar;
    private bool _operationFinished;
    private bool _canCancel = true;
    private bool _canPause;
    private bool _canResume;
    private CloudflaredInstallDialogResult? _terminalResult;

    public CloudflaredDownloadProgressWindow(
        ICloudflaredInstallService installService,
        CancellationToken cancellationToken = default)
    {
        _installService = installService;
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Title = "下载并安装 cloudflared";
        Width = 520;
        Height = 260;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _statusText = new TextBlock
        {
            Text = "正在准备下载…",
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };
        _detailText = new TextBlock
        {
            Text = $"版本 {_installService.Asset.Version} · {_installService.Asset.RuntimeIdentifier}",
            Foreground = Brushes.Gray,
            TextWrapping = TextWrapping.Wrap
        };
        _progressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Height = 12,
            IsIndeterminate = true
        };
        CancelButton = new Button
        {
            Content = "取消下载",
            MinWidth = 104,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        CancelButton.Click += (_, _) => HandleButtonClick();
        PauseResumeButton = new Button
        {
            Content = "暂停下载",
            MinWidth = 104,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            IsVisible = false
        };
        PauseResumeButton.Click += (_, _) => HandlePauseResumeButtonClick();

        Content = new Border
        {
            Padding = new Thickness(24),
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto"),
                RowSpacing = 16,
                Children =
                {
                    _statusText,
                    new Border { Child = _progressBar, [Grid.RowProperty] = 1 },
                    new Border { Child = _detailText, [Grid.RowProperty] = 2 },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 10,
                        Children = { PauseResumeButton, CancelButton },
                        [Grid.RowProperty] = 3
                    }
                }
            }
        };

        Opened += OnOpened;
        Closing += OnClosing;
        Closed += (_, _) => _cancellation.Dispose();
    }

    internal Button CancelButton { get; }

    internal Button PauseResumeButton { get; }

    internal string StatusText => _statusText.Text ?? string.Empty;

    internal string DetailText => _detailText.Text ?? string.Empty;

    internal double ProgressValue => _progressBar.Value;

    internal bool IsProgressIndeterminate => _progressBar.IsIndeterminate;

    private async void OnOpened(object? sender, EventArgs eventArgs)
    {
        try
        {
            var progress = new Progress<CloudflaredInstallProgress>(UpdateProgress);
            await _installService.InstallAsync(progress, _cancellation.Token);
            _operationFinished = true;
            Close(new CloudflaredInstallDialogResult(
                CloudflaredInstallDialogOutcome.Installed,
                "cloudflared 下载并安装完成"));
        }
        catch (OperationCanceledException)
        {
            _operationFinished = true;
            Close(new CloudflaredInstallDialogResult(
                CloudflaredInstallDialogOutcome.Canceled,
                "已取消下载；本次运行期间再次下载将尝试续传"));
        }
        catch (Exception exception)
        {
            _operationFinished = true;
            _terminalResult = new CloudflaredInstallDialogResult(
                CloudflaredInstallDialogOutcome.Failed,
                DescribeFailure(exception));
            _statusText.Text = "cloudflared 下载或安装失败";
            _detailText.Text = $"{DescribeFailure(exception)}\n请检查网络、磁盘空间或目录权限，详情请查看日志。";
            _progressBar.IsIndeterminate = false;
            _progressBar.Value = 0;
            PauseResumeButton.IsVisible = false;
            PauseResumeButton.IsEnabled = false;
            CancelButton.Content = "关闭";
            CancelButton.IsEnabled = true;
        }
    }

    private void UpdateProgress(CloudflaredInstallProgress progress)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => UpdateProgress(progress));
            return;
        }

        _canCancel = progress.CanCancel;
        _canPause = progress.CanPause;
        _canResume = progress.CanResume;
        _statusText.Text = progress.Status;
        _detailText.Text = progress.TotalBytes > 0
            ? $"{FormatBytes(progress.CompletedBytes)} / {FormatBytes(progress.TotalBytes)}  ·  {progress.Percentage:F1}%"
            : $"版本 {_installService.Asset.Version} · {_installService.Asset.RuntimeIdentifier}";
        _progressBar.IsIndeterminate = progress.TotalBytes <= 0;
        if (!_progressBar.IsIndeterminate)
        {
            _progressBar.Value = progress.Percentage;
        }

        CancelButton.IsEnabled = progress.CanCancel && !_cancellation.IsCancellationRequested;
        CancelButton.Content = progress.CanCancel ? "取消下载" : "正在安装…";
        PauseResumeButton.IsVisible = progress.CanPause || progress.CanResume;
        PauseResumeButton.IsEnabled = PauseResumeButton.IsVisible &&
                                      !_cancellation.IsCancellationRequested;
        PauseResumeButton.Content = progress.CanResume ? "继续下载" : "暂停下载";
    }

    private void HandlePauseResumeButtonClick()
    {
        if (_operationFinished || _cancellation.IsCancellationRequested)
        {
            return;
        }

        if (_canPause && _installService.TryPause())
        {
            PauseResumeButton.IsEnabled = false;
            PauseResumeButton.Content = "正在暂停…";
            return;
        }

        if (_canResume && _installService.TryResume())
        {
            PauseResumeButton.IsEnabled = false;
            PauseResumeButton.Content = "正在继续…";
        }
    }

    private void HandleButtonClick()
    {
        if (_operationFinished)
        {
            Close(_terminalResult ?? new CloudflaredInstallDialogResult(
                CloudflaredInstallDialogOutcome.Failed,
                "cloudflared 安装未完成"));
            return;
        }

        if (!_canCancel || _cancellation.IsCancellationRequested)
        {
            return;
        }

        CancelButton.IsEnabled = false;
        CancelButton.Content = "正在取消…";
        PauseResumeButton.IsEnabled = false;
        _cancellation.Cancel();
    }

    private void OnClosing(object? sender, WindowClosingEventArgs eventArgs)
    {
        if (_operationFinished)
        {
            return;
        }

        eventArgs.Cancel = true;
        HandleButtonClick();
    }

    private static string FormatBytes(long value)
    {
        var units = new[] { "B", "KiB", "MiB", "GiB" };
        var size = (double)Math.Max(0, value);
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size:F1} {units[unit]}";
    }

    private static string DescribeFailure(Exception exception)
    {
        var message = string.IsNullOrWhiteSpace(exception.Message)
            ? "发生未知错误"
            : exception.Message.Trim();
        return message.Length <= 280 ? message : message[..280] + "…";
    }
}
