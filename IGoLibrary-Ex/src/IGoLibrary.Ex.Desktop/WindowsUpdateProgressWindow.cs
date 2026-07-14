using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using IGoLibrary.Ex.Desktop.Services;

namespace IGoLibrary.Ex.Desktop;

public sealed class WindowsUpdateProgressWindow : Window
{
    private readonly Func<IProgress<WindowsUpdateProgress>, CancellationToken, Task<WindowsPortableUpdateResult>> _operation;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly TextBlock _statusText;
    private readonly TextBlock _detailText;
    private readonly ProgressBar _progressBar;
    private readonly Button _cancelButton;
    private bool _operationFinished;
    private bool _canCancel = true;

    public WindowsUpdateProgressWindow(
        Func<IProgress<WindowsUpdateProgress>, CancellationToken, Task<WindowsPortableUpdateResult>> operation)
    {
        _operation = operation;
        Title = "下载并安装更新";
        Width = 520;
        Height = 250;
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
        _cancelButton = new Button
        {
            Content = "取消",
            MinWidth = 96,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        _cancelButton.Click += (_, _) => CancelOperation();

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
                    new Border
                    {
                        Child = _cancelButton,
                        [Grid.RowProperty] = 3
                    }
                }
            }
        };

        Opened += OnOpened;
        Closing += OnClosing;
    }

    private async void OnOpened(object? sender, EventArgs eventArgs)
    {
        try
        {
            var progress = new Progress<WindowsUpdateProgress>(UpdateProgress);
            var result = await _operation(progress, _cancellation.Token);
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
        _cancelButton.IsEnabled = progress.CanCancel;
        _cancelButton.Content = progress.CanCancel ? "取消" : "正在安装…";
        _statusText.Text = progress.Status;
        _detailText.Text = BuildDetail(progress);
        _progressBar.IsIndeterminate = progress.TotalBytes <= 0;
        if (!_progressBar.IsIndeterminate)
        {
            _progressBar.Value = progress.Percentage;
        }
    }

    private void CancelOperation()
    {
        if (!_canCancel || _cancellation.IsCancellationRequested)
        {
            return;
        }

        _cancelButton.IsEnabled = false;
        _cancelButton.Content = "正在取消…";
        _cancellation.Cancel();
    }

    private void OnClosing(object? sender, WindowClosingEventArgs eventArgs)
    {
        if (_operationFinished)
        {
            return;
        }

        eventArgs.Cancel = true;
        CancelOperation();
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
