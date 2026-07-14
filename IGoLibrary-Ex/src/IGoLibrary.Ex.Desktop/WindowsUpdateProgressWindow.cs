using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using IGoLibrary.Ex.Desktop.Services;

namespace IGoLibrary.Ex.Desktop;

public sealed class WindowsUpdateProgressWindow : Window
{
    private readonly IWindowsPortableUpdateOperation _operation;
    private readonly CancellationTokenSource _cancellation;
    private readonly Func<Window, Task<bool>> _confirmCancellationAsync;
    private readonly TextBlock _statusText;
    private readonly TextBlock _detailText;
    private readonly ProgressBar _progressBar;
    private bool _operationFinished;
    private bool _canCancel = true;
    private bool _canPause;
    private bool _canResume;
    private bool _closeConfirmationOpen;

    public WindowsUpdateProgressWindow(
        IWindowsPortableUpdateOperation operation,
        CancellationToken cancellationToken = default)
        : this(operation, ShowCancelConfirmationAsync, cancellationToken)
    {
    }

    internal WindowsUpdateProgressWindow(
        IWindowsPortableUpdateOperation operation,
        Func<Window, Task<bool>> confirmCancellationAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(confirmCancellationAsync);
        _operation = operation;
        _confirmCancellationAsync = confirmCancellationAsync;
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Title = "下载并安装更新";
        Width = 520;
        Height = 270;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _statusText = new TextBlock
        {
            Text = "正在检查安装环境…",
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };
        _detailText = new TextBlock
        {
            Text = "准备开始",
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
        PauseButton = new Button
        {
            Content = "暂停",
            MinWidth = 96,
            IsVisible = false,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        PauseButton.Click += (_, _) => TogglePause();
        CancelButton = new Button
        {
            Content = "取消更新",
            MinWidth = 96,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        CancelButton.Click += (_, _) => CancelOperation();

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
                    new Border
                    {
                        Child = _progressBar,
                        [Grid.RowProperty] = 1
                    },
                    new Border
                    {
                        Child = _detailText,
                        [Grid.RowProperty] = 2
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 10,
                        Children = { PauseButton, CancelButton },
                        [Grid.RowProperty] = 3
                    }
                }
            }
        };

        Opened += OnOpened;
        Closing += OnClosing;
        Closed += (_, _) => _cancellation.Dispose();
    }

    internal Button PauseButton { get; }

    internal Button CancelButton { get; }

    internal string StatusText => _statusText.Text ?? string.Empty;

    internal string DetailText => _detailText.Text ?? string.Empty;

    internal double ProgressValue => _progressBar.Value;

    internal bool IsProgressIndeterminate => _progressBar.IsIndeterminate;

    private async void OnOpened(object? sender, EventArgs eventArgs)
    {
        try
        {
            var progress = new Progress<WindowsUpdateProgress>(UpdateProgress);
            var result = await _operation.RunAsync(progress, _cancellation.Token);
            _operationFinished = true;
            Close(result);
        }
        catch (OperationCanceledException)
        {
            _operationFinished = true;
            Close(new WindowsPortableUpdateResult(
                WindowsPortableUpdateOutcome.Canceled,
                "已取消下载，未修改程序文件"));
        }
        catch (Exception exception)
        {
            _operationFinished = true;
            Close(new WindowsPortableUpdateResult(
                WindowsPortableUpdateOutcome.Failed,
                exception.Message));
        }
    }

    private void UpdateProgress(WindowsUpdateProgress progress)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => UpdateProgress(progress));
            return;
        }

        _canCancel = progress.CanCancel;
        _canPause = progress.CanPause;
        _canResume = progress.CanResume;
        PauseButton.IsVisible = _canPause || _canResume;
        PauseButton.IsEnabled = _canPause || _canResume;
        PauseButton.Content = _canResume ? "继续下载" : "暂停";
        CancelButton.IsEnabled = _canCancel && !_cancellation.IsCancellationRequested;
        CancelButton.Content = progress.CanCancel ? "取消更新" : "正在安装…";
        _statusText.Text = progress.Status;
        _detailText.Text = BuildDetail(progress);
        _progressBar.IsIndeterminate = progress.TotalBytes <= 0;
        if (!_progressBar.IsIndeterminate)
        {
            _progressBar.Value = progress.Percentage;
        }
    }

    private void TogglePause()
    {
        if (_cancellation.IsCancellationRequested)
        {
            return;
        }

        if (_canResume)
        {
            if (_operation.TryResume())
            {
                PauseButton.IsEnabled = false;
                PauseButton.Content = "正在继续…";
            }

            return;
        }

        if (_canPause && _operation.TryPause())
        {
            PauseButton.IsEnabled = false;
            PauseButton.Content = "正在暂停…";
        }
    }

    private void CancelOperation()
    {
        if (!_canCancel || _cancellation.IsCancellationRequested)
        {
            return;
        }

        PauseButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
        CancelButton.Content = "正在取消…";
        _cancellation.Cancel();
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs eventArgs)
    {
        if (_operationFinished)
        {
            return;
        }

        eventArgs.Cancel = true;
        if (!_canCancel || _cancellation.IsCancellationRequested || _closeConfirmationOpen)
        {
            return;
        }

        _closeConfirmationOpen = true;
        try
        {
            if (await _confirmCancellationAsync(this))
            {
                CancelOperation();
            }
        }
        finally
        {
            _closeConfirmationOpen = false;
        }
    }

    private static Task<bool> ShowCancelConfirmationAsync(Window owner)
    {
        return new WindowsUpdateCancelConfirmationWindow().ShowDialog<bool>(owner);
    }

    private static string BuildDetail(WindowsUpdateProgress progress)
    {
        if (progress.TotalBytes <= 0)
        {
            return progress.Stage switch
            {
                WindowsUpdateStage.Checking => "检查",
                WindowsUpdateStage.Verifying => "校验",
                WindowsUpdateStage.WaitingForExit => "等待退出",
                WindowsUpdateStage.Installing => "安装",
                WindowsUpdateStage.Validating => "验证",
                WindowsUpdateStage.RollingBack => "回滚",
                _ => "处理中"
            };
        }

        return $"{FormatBytes(progress.CompletedBytes)} / {FormatBytes(progress.TotalBytes)}  ·  {progress.Percentage:F1}%";
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
}

internal sealed class WindowsUpdateCancelConfirmationWindow : Window
{
    public WindowsUpdateCancelConfirmationWindow()
    {
        Title = "取消自动更新";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        ContinueButton = CreateButton("继续下载");
        ContinueButton.Click += (_, _) => Close(false);
        ConfirmCancelButton = CreateButton("取消更新");
        ConfirmCancelButton.Click += (_, _) => Close(true);

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
                        Text = "确定要取消自动更新吗？",
                        FontSize = 20,
                        FontWeight = FontWeight.Bold
                    },
                    new TextBlock
                    {
                        Text = "取消后会删除本次尚未完成验签的下载文件；下次需要重新下载。",
                        TextWrapping = TextWrapping.Wrap
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 10,
                        Children = { ContinueButton, ConfirmCancelButton }
                    }
                }
            }
        };
    }

    internal Button ContinueButton { get; }

    internal Button ConfirmCancelButton { get; }

    private static Button CreateButton(string text)
    {
        return new Button
        {
            Content = text,
            MinWidth = 96,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
    }
}
