using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Updates;
using IGoLibrary.Ex.Desktop.Services;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Tests;

public sealed class WindowsUpdateProgressDialogServiceTests
{
    [Fact]
    public async Task ShowAsync_IncompatibleRelease_IsBlockedBeforeTaskAndUiChecks()
    {
        var guard = new RecordingInstallGuard();
        var updateService = new RecordingPortableUpdateService();
        var logger = new CapturingLogger<WindowsUpdateProgressDialogService>();
        var service = new WindowsUpdateProgressDialogService(
            new AppWindowService(),
            guard,
            updateService,
            new FakeAppVersionProvider(new ReleaseVersion(1, 0, 4)),
            logger);
        var release = CreateRelease() with
        {
            AutomaticUpdatePolicy = AutomaticUpdatePolicy.RequireMinimumVersion(
                new ReleaseVersion(1, 0, 5))
        };

        var result = await service.ShowAsync(release);

        Assert.Equal(WindowsPortableUpdateOutcome.Blocked, result.Outcome);
        Assert.Contains("v1.0.4", result.Message);
        Assert.Contains("v1.0.5", result.Message);
        Assert.Equal(0, guard.CallCount);
        Assert.Equal(0, updateService.CallCount);
        Assert.Contains(
            logger.Entries,
            entry => entry.Level == LogLevel.Warning &&
                     entry.Message.Contains("CurrentVersionTooOld", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ShowAsync_InvalidReleasePolicy_FailsClosedWithoutInternalDiagnosticText()
    {
        var logger = new CapturingLogger<WindowsUpdateProgressDialogService>();
        var service = new WindowsUpdateProgressDialogService(
            new AppWindowService(),
            new RecordingInstallGuard(),
            new RecordingPortableUpdateService(),
            new FakeAppVersionProvider(new ReleaseVersion(1, 0, 5)),
            logger);
        var release = CreateRelease() with
        {
            AutomaticUpdatePolicy = AutomaticUpdatePolicy.Invalid
        };

        var result = await service.ShowAsync(release);

        Assert.Equal(WindowsPortableUpdateOutcome.Blocked, result.Outcome);
        Assert.Contains("兼容性标记无效", result.Message);
        Assert.DoesNotContain("InvalidReleasePolicy", result.Message);
    }

    private static ReleaseUpdateInfo CreateRelease()
    {
        return new ReleaseUpdateInfo(
            new ReleaseVersion(1, 0, 6),
            "v1.0.6",
            "IGoLibrary-Ex v1.0.6",
            "notes",
            new Uri("https://github.com/EJianZQ/IGoLibrary/releases/tag/v1.0.6"),
            DateTimeOffset.UtcNow);
    }

    private sealed class RecordingInstallGuard : IUpdateInstallGuard
    {
        public int CallCount { get; private set; }

        public IReadOnlyList<string> GetBlockingTaskNames()
        {
            CallCount++;
            return [];
        }
    }

    private sealed class RecordingPortableUpdateService : IWindowsPortableUpdateService
    {
        public int CallCount { get; private set; }

        public IWindowsPortableUpdateOperation CreateOperation(ReleaseUpdateInfo release)
        {
            CallCount++;
            throw new InvalidOperationException("不兼容请求不应创建更新操作");
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
