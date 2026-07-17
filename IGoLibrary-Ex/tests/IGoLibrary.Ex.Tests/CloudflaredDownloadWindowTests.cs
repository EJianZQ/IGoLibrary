using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using IGoLibrary.Ex.Desktop;
using IGoLibrary.Ex.Desktop.Services;

namespace IGoLibrary.Ex.Tests;

[Collection(NonParallelTestCollection.Name)]
public sealed class CloudflaredDownloadWindowTests
{
    [AvaloniaFact]
    public async Task ConfirmationButtonsReturnExplicitChoice()
    {
        var installDirectory = Path.Combine(
            Path.GetTempPath(),
            "IGoLibrary-Ex",
            "tools",
            "cloudflared",
            "2026.7.0",
            "win-x64");
        var owner = new Window();
        owner.Show();
        try
        {
            var declineWindow = new CloudflaredDownloadConfirmationWindow(Asset(), installDirectory);
            Assert.Contains($"组件会安装：{installDirectory}", declineWindow.MessageText, StringComparison.Ordinal);
            Assert.DoesNotContain("不需要管理员权限", declineWindow.MessageText, StringComparison.Ordinal);
            var declined = declineWindow.ShowDialog<bool>(owner);
            Dispatcher.UIThread.RunJobs();
            declineWindow.LaterButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(await declined);

            var acceptWindow = new CloudflaredDownloadConfirmationWindow(Asset(), installDirectory);
            var accepted = acceptWindow.ShowDialog<bool>(owner);
            Dispatcher.UIThread.RunJobs();
            acceptWindow.DownloadButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(await accepted);
        }
        finally
        {
            owner.Close();
        }
    }

    [AvaloniaFact]
    public async Task ProgressWindowShowsTransferAndCancelReturnsCanceled()
    {
        var owner = new Window();
        owner.Show();
        try
        {
            var service = new BlockingInstallService();
            var window = new CloudflaredDownloadProgressWindow(service);
            var resultTask = window.ShowDialog<CloudflaredInstallDialogResult>(owner);
            Dispatcher.UIThread.RunJobs();
            await service.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

            service.Progress!.Report(new CloudflaredInstallProgress(
                CloudflaredInstallStage.Downloading,
                "正在下载 cloudflared…",
                25,
                100,
                CanPause: true));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("正在下载 cloudflared…", window.StatusText);
            Assert.Equal(25, window.ProgressValue);
            Assert.True(window.CancelButton.IsEnabled);
            Assert.True(window.PauseResumeButton.IsVisible);
            Assert.Equal("暂停下载", window.PauseResumeButton.Content);

            window.PauseResumeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, service.PauseCalls);

            service.Progress.Report(new CloudflaredInstallProgress(
                CloudflaredInstallStage.Paused,
                "下载已暂停，进度已保留",
                25,
                100,
                CanResume: true));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("继续下载", window.PauseResumeButton.Content);

            window.PauseResumeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, service.ResumeCalls);

            window.CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var result = await resultTask.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal(CloudflaredInstallDialogOutcome.Canceled, result.Outcome);
        }
        finally
        {
            owner.Close();
        }
    }

    private static CloudflaredAssetDescriptor Asset()
        => new(
            "2026.7.0",
            "win-x64",
            "cloudflared.exe",
            "binary",
            100,
            new string('0', 64),
            "cloudflared.exe",
            100,
            new string('0', 64),
            new Uri("https://github.com/cloudflare/cloudflared/releases/download/2026.7.0/cloudflared.exe"));

    private sealed class BlockingInstallService : ICloudflaredInstallService
    {
        public CloudflaredAssetDescriptor Asset { get; } = CloudflaredDownloadWindowTests.Asset();

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IProgress<CloudflaredInstallProgress>? Progress { get; private set; }

        public int PauseCalls { get; private set; }

        public int ResumeCalls { get; private set; }

        public bool TryPause()
        {
            PauseCalls++;
            return true;
        }

        public bool TryResume()
        {
            ResumeCalls++;
            return true;
        }

        public async Task InstallAsync(
            IProgress<CloudflaredInstallProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Progress = progress;
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }
}
