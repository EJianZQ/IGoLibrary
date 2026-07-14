using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using IGoLibrary.Ex.Desktop;
using IGoLibrary.Ex.Desktop.Services;

namespace IGoLibrary.Ex.Tests;

public sealed class WindowsUpdateProgressWindowTests
{
    [AvaloniaFact]
    public async Task ProgressStatesDrivePauseResumeAndCancelButtons()
    {
        var owner = new Window();
        owner.Show();
        try
        {
            var operation = new FakeWindowsPortableUpdateOperation();
            var window = new WindowsUpdateProgressWindow(operation);
            var dialogTask = window.ShowDialog<WindowsPortableUpdateResult>(owner);
            Dispatcher.UIThread.RunJobs();
            await operation.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

            operation.Report(new WindowsUpdateProgress(
                WindowsUpdateStage.Downloading,
                25,
                100,
                "正在下载",
                WindowsUpdateTransferState.Downloading,
                WindowsUpdateAvailableActions.Pause | WindowsUpdateAvailableActions.Cancel));
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.PauseButton.IsVisible);
            Assert.True(window.PauseButton.IsEnabled);
            Assert.Equal("暂停", window.PauseButton.Content);
            Assert.True(window.CancelButton.IsEnabled);
            Assert.Equal(25, window.ProgressValue);

            window.PauseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, operation.PauseCount);
            operation.Report(new WindowsUpdateProgress(
                WindowsUpdateStage.Downloading,
                25,
                100,
                "下载已暂停",
                WindowsUpdateTransferState.Paused,
                WindowsUpdateAvailableActions.Resume | WindowsUpdateAvailableActions.Cancel));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("继续下载", window.PauseButton.Content);

            window.PauseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, operation.ResumeCount);
            operation.Complete(new WindowsPortableUpdateResult(
                WindowsPortableUpdateOutcome.Canceled,
                "测试完成"));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(WindowsPortableUpdateOutcome.Canceled, (await dialogTask).Outcome);
        }
        finally
        {
            owner.Close();
        }
    }

    [AvaloniaFact]
    public async Task RetryManualResumeVerificationAndHandoffStatesDriveAvailableActions()
    {
        var owner = new Window();
        owner.Show();
        try
        {
            var operation = new FakeWindowsPortableUpdateOperation();
            var window = new WindowsUpdateProgressWindow(operation);
            var dialogTask = window.ShowDialog<WindowsPortableUpdateResult>(owner);
            Dispatcher.UIThread.RunJobs();
            await operation.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

            operation.Report(new WindowsUpdateProgress(
                WindowsUpdateStage.Downloading,
                40,
                100,
                "下载中断，2 秒后自动续传（2/3）…",
                WindowsUpdateTransferState.Retrying,
                WindowsUpdateAvailableActions.Pause | WindowsUpdateAvailableActions.Cancel));
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("2/3", window.StatusText);
            Assert.Equal("暂停", window.PauseButton.Content);
            Assert.True(window.CancelButton.IsEnabled);

            operation.Report(new WindowsUpdateProgress(
                WindowsUpdateStage.Downloading,
                40,
                100,
                "自动续传 3 次仍失败，已保留下载进度；请点击继续下载",
                WindowsUpdateTransferState.AwaitingManualResume,
                WindowsUpdateAvailableActions.Resume | WindowsUpdateAvailableActions.Cancel));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("继续下载", window.PauseButton.Content);
            Assert.Equal(40, window.ProgressValue);

            operation.Report(new WindowsUpdateProgress(
                WindowsUpdateStage.Verifying,
                100,
                100,
                "下载完成，正在校验更新包…",
                WindowsUpdateTransferState.Verifying,
                WindowsUpdateAvailableActions.Cancel));
            Dispatcher.UIThread.RunJobs();
            Assert.False(window.PauseButton.IsVisible);
            Assert.True(window.CancelButton.IsEnabled);

            operation.Report(new WindowsUpdateProgress(
                WindowsUpdateStage.Installing,
                0,
                0,
                "更新组件已就绪，正在安全退出应用…",
                WindowsUpdateTransferState.None,
                WindowsUpdateAvailableActions.None));
            Dispatcher.UIThread.RunJobs();
            Assert.False(window.PauseButton.IsVisible);
            Assert.False(window.CancelButton.IsEnabled);
            Assert.Equal("正在安装…", window.CancelButton.Content);

            operation.Complete(new WindowsPortableUpdateResult(
                WindowsPortableUpdateOutcome.Canceled,
                "测试完成"));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(WindowsPortableUpdateOutcome.Canceled, (await dialogTask).Outcome);
        }
        finally
        {
            owner.Close();
        }
    }

    [AvaloniaFact]
    public async Task WindowCloseDeclinedKeepsOperationRunning()
    {
        var owner = new Window();
        owner.Show();
        try
        {
            var operation = new FakeWindowsPortableUpdateOperation();
            var confirmationCount = 0;
            var window = new WindowsUpdateProgressWindow(
                operation,
                _ =>
                {
                    confirmationCount++;
                    return Task.FromResult(false);
                });
            var dialogTask = window.ShowDialog<WindowsPortableUpdateResult>(owner);
            Dispatcher.UIThread.RunJobs();
            await operation.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            operation.Report(new WindowsUpdateProgress(
                WindowsUpdateStage.Downloading,
                10,
                100,
                "正在下载",
                WindowsUpdateTransferState.Downloading,
                WindowsUpdateAvailableActions.Pause | WindowsUpdateAvailableActions.Cancel));
            Dispatcher.UIThread.RunJobs();

            window.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.IsVisible);
            Assert.False(operation.CancellationObserved.Task.IsCompleted);
            Assert.Equal(1, confirmationCount);

            operation.Complete(new WindowsPortableUpdateResult(
                WindowsPortableUpdateOutcome.Canceled,
                "测试完成"));
            Dispatcher.UIThread.RunJobs();
            var result = await dialogTask.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(WindowsPortableUpdateOutcome.Canceled, result.Outcome);
        }
        finally
        {
            owner.Close();
        }
    }

    [AvaloniaFact]
    public async Task WindowCloseConfirmedCancelsOperation()
    {
        var owner = new Window();
        owner.Show();
        try
        {
            var operation = new FakeWindowsPortableUpdateOperation();
            var confirmationCount = 0;
            var window = new WindowsUpdateProgressWindow(
                operation,
                _ =>
                {
                    confirmationCount++;
                    return Task.FromResult(true);
                });
            var dialogTask = window.ShowDialog<WindowsPortableUpdateResult>(owner);
            Dispatcher.UIThread.RunJobs();
            await operation.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            operation.Report(new WindowsUpdateProgress(
                WindowsUpdateStage.Downloading,
                10,
                100,
                "正在下载",
                WindowsUpdateTransferState.Downloading,
                WindowsUpdateAvailableActions.Pause | WindowsUpdateAvailableActions.Cancel));
            Dispatcher.UIThread.RunJobs();

            window.Close();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, confirmationCount);
            Assert.True(operation.RunToken.IsCancellationRequested);
            Dispatcher.UIThread.RunJobs();
            var result = await dialogTask.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(WindowsPortableUpdateOutcome.Canceled, result.Outcome);
        }
        finally
        {
            owner.Close();
        }
    }

    private sealed class FakeWindowsPortableUpdateOperation : IWindowsPortableUpdateOperation
    {
        private readonly TaskCompletionSource<WindowsPortableUpdateResult> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private IProgress<WindowsUpdateProgress>? _progress;

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource CancellationObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int PauseCount { get; private set; }

        public int ResumeCount { get; private set; }

        public CancellationToken RunToken { get; private set; }

        public async Task<WindowsPortableUpdateResult> RunAsync(
            IProgress<WindowsUpdateProgress> progress,
            CancellationToken cancellationToken = default)
        {
            _progress = progress;
            RunToken = cancellationToken;
            using var registration = cancellationToken.Register(() =>
                CancellationObserved.TrySetResult());
            Started.TrySetResult();
            return await _completion.Task.WaitAsync(cancellationToken);
        }

        public bool TryPause()
        {
            PauseCount++;
            return true;
        }

        public bool TryResume()
        {
            ResumeCount++;
            return true;
        }

        public void Report(WindowsUpdateProgress progress)
        {
            _progress?.Report(progress);
        }

        public void Complete(WindowsPortableUpdateResult result)
        {
            _completion.TrySetResult(result);
        }

        public void Dispose()
        {
        }
    }
}
