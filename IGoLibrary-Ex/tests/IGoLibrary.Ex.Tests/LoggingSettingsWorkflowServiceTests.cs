using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Services;

namespace IGoLibrary.Ex.Tests;

public sealed class LoggingSettingsWorkflowServiceTests
{
    [Fact]
    public async Task SaveAsync_PersistsNormalizedSettingsBeforeApplyingRuntimeState()
    {
        var settingsService = new FakeSettingsService(AppSettings.Default);
        var runtime = new RecordingRuntimeController(settingsService);
        var service = new LoggingSettingsWorkflowService(settingsService, runtime);

        var result = await service.SaveAsync(new LogFileSettings(false, 999));

        Assert.Equal(new LogFileSettings(false, 365), settingsService.CurrentSettings.Logging);
        Assert.Equal(settingsService.CurrentSettings.Logging, runtime.AppliedSettings);
        Assert.Equal(settingsService.CurrentSettings.Logging, result.Settings);
    }

    [Fact]
    public async Task SaveAsync_WhenPersistenceFails_DoesNotChangeRuntimeState()
    {
        var settingsService = new FakeSettingsService(AppSettings.Default);
        settingsService.UpdateExceptions.Enqueue(new IOException("database unavailable"));
        var runtime = new RecordingRuntimeController(settingsService);
        var service = new LoggingSettingsWorkflowService(settingsService, runtime);

        await Assert.ThrowsAsync<IOException>(() =>
            service.SaveAsync(new LogFileSettings(false, 5)));

        Assert.Null(runtime.AppliedSettings);
        Assert.Equal(LogFileSettings.Default, settingsService.CurrentSettings.Logging);
    }

    private sealed class RecordingRuntimeController(ISettingsService settingsService)
        : IAppLogRuntimeController
    {
        public LogFileSettings? AppliedSettings { get; private set; }

        public async Task<LogRuntimeApplyResult> ApplyAsync(
            LogFileSettings settings,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(settings, (await settingsService.LoadAsync(cancellationToken)).Logging);
            AppliedSettings = settings;
            return LogRuntimeApplyResult.Success;
        }
    }
}
