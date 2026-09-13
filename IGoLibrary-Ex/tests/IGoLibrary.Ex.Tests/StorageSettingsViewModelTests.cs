using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Desktop.ViewModels;

namespace IGoLibrary.Ex.Tests;

public sealed class StorageSettingsViewModelTests
{
    [Fact]
    public async Task SelectDirectories_StagesPathsUntilApplyCommandRuns()
    {
        var storage = new FakeStorageLocationService();
        var picker = new FakeFolderPickerService
        {
            SelectedPath = Path.Combine(Path.GetTempPath(), "selected-data")
        };
        var workflow = new FakeStorageChangeWorkflowService();
        var viewModel = Create(storage, picker, workflow, out _);

        await viewModel.SelectDataDirectoryCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasStorageLocationChanges);
        Assert.Equal(Path.GetFullPath(picker.SelectedPath), viewModel.PendingDataDirectory);
        Assert.Null(workflow.LastTarget);

        await viewModel.ApplyStorageLocationChangesCommand.ExecuteAsync(null);

        Assert.NotNull(workflow.LastTarget);
        Assert.Equal(viewModel.PendingDataDirectory, workflow.LastTarget.DataDirectory);
    }

    [Fact]
    public void RestoreDefaults_UsesConfiguredDefaultDirectories()
    {
        var storage = new FakeStorageLocationService
        {
            Defaults = new StorageLocations(
                Path.Combine(Path.GetTempPath(), "default-data"),
                Path.Combine(Path.GetTempPath(), "default-logs"))
        };
        var viewModel = Create(
            storage,
            new FakeFolderPickerService(),
            new FakeStorageChangeWorkflowService(),
            out _);

        viewModel.RestoreDefaultDataDirectoryCommand.Execute(null);
        viewModel.RestoreDefaultLogDirectoryCommand.Execute(null);

        Assert.Equal(storage.Defaults.DataDirectory, viewModel.PendingDataDirectory);
        Assert.Equal(storage.Defaults.LogDirectory, viewModel.PendingLogDirectory);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InitializeAsync_ShowsStoredStartupResult(bool succeeded)
    {
        var storage = new FakeStorageLocationService
        {
            StartupResult = new StorageLocationStartupResult(succeeded, "result-message")
        };
        var viewModel = Create(
            storage,
            new FakeFolderPickerService(),
            new FakeStorageChangeWorkflowService(),
            out var notification);

        await viewModel.InitializeAsync(LogFileSettings.Default);

        if (succeeded)
        {
            Assert.Contains(notification.Successes, item => item.Message == "result-message");
        }
        else
        {
            Assert.Contains(notification.Warnings, item => item.Message == "result-message");
        }
    }

    [Fact]
    public async Task InitializeAsync_AppliesLoggingSettingsWithoutSaving()
    {
        var loggingWorkflow = new FakeLoggingSettingsWorkflowService();
        var viewModel = Create(
            new FakeStorageLocationService(),
            new FakeFolderPickerService(),
            new FakeStorageChangeWorkflowService(),
            out _,
            loggingWorkflow);

        await viewModel.InitializeAsync(new LogFileSettings(false, 42));

        Assert.False(viewModel.IsFileLoggingEnabled);
        Assert.Equal(42, viewModel.RetainedLogFileCount);
        Assert.Empty(loggingWorkflow.SavedSettings);
    }

    [Fact]
    public async Task LoggingChanges_PersistLatestSnapshotImmediately()
    {
        var loggingWorkflow = new FakeLoggingSettingsWorkflowService();
        var viewModel = Create(
            new FakeStorageLocationService(),
            new FakeFolderPickerService(),
            new FakeStorageChangeWorkflowService(),
            out _,
            loggingWorkflow);
        await viewModel.InitializeAsync(LogFileSettings.Default);

        viewModel.IsFileLoggingEnabled = false;
        viewModel.RetainedLogFileCount = 12;
        await viewModel.FlushPendingLoggingSettingsSaveAsync();

        Assert.NotEmpty(loggingWorkflow.SavedSettings);
        Assert.Equal(new LogFileSettings(false, 12), loggingWorkflow.SavedSettings[^1]);
    }

    [Fact]
    public async Task LoggingSaveFailure_RollsBackToLastPersistedSettings()
    {
        var loggingWorkflow = new FakeLoggingSettingsWorkflowService
        {
            SaveHandler = _ => Task.FromException<LoggingSettingsUpdateResult>(
                new IOException("database unavailable"))
        };
        var viewModel = Create(
            new FakeStorageLocationService(),
            new FakeFolderPickerService(),
            new FakeStorageChangeWorkflowService(),
            out var notification,
            loggingWorkflow);
        await viewModel.InitializeAsync(LogFileSettings.Default);

        viewModel.IsFileLoggingEnabled = false;
        await viewModel.FlushPendingLoggingSettingsSaveAsync();

        Assert.True(viewModel.IsFileLoggingEnabled);
        Assert.Contains(notification.Warnings, item => item.Title == "无法保存日志设置");
    }

    [Fact]
    public async Task NetworkLogging_PersistsWhileMasterIsOff_AndRestoresOnLoad()
    {
        var workflow = new FakeLoggingSettingsWorkflowService();
        var viewModel = Create(new(), new(), new(), out _, workflow);
        await viewModel.InitializeAsync(new LogFileSettings(false, 42));
        viewModel.RecordNetworkRequests = true;
        await viewModel.FlushPendingLoggingSettingsSaveAsync();
        var saved = workflow.SavedSettings[^1];
        Assert.True(saved.RecordNetworkRequests);
        Assert.False(saved.Enabled);
        Assert.Equal(42, saved.RetainedFileCount);
        var restarted = Create(new(), new(), new(), out _, new());
        await restarted.InitializeAsync(saved);
        Assert.True(restarted.RecordNetworkRequests);
    }

    [Fact]
    public async Task NetworkLogging_RuntimeFailureRetainsSavedChoiceAndShowsAccurateWarning()
    {
        var workflow = new FakeLoggingSettingsWorkflowService
        {
            SaveHandler = settings => Task.FromResult(new LoggingSettingsUpdateResult(settings,
                LogRuntimeApplyResult.Success with { ApplicationFailure = "应用失败" }))
        };
        var viewModel = Create(new(), new(), new(), out var notification, workflow);
        await viewModel.InitializeAsync(LogFileSettings.Default);
        viewModel.RecordNetworkRequests = true;
        await viewModel.FlushPendingLoggingSettingsSaveAsync();
        Assert.True(viewModel.RecordNetworkRequests);
        Assert.Contains(notification.Warnings, w => w.Title == "日志设置已保存，但未能应用");
    }

    [Fact]
    public async Task NetworkLogging_SaveFailureRollsBackChoice()
    {
        var workflow = new FakeLoggingSettingsWorkflowService
        {
            SaveHandler = _ => throw new IOException("保存失败")
        };
        var viewModel = Create(new(), new(), new(), out _, workflow);
        await viewModel.InitializeAsync(LogFileSettings.Default);
        viewModel.RecordNetworkRequests = true;
        await viewModel.FlushPendingLoggingSettingsSaveAsync();
        Assert.False(viewModel.RecordNetworkRequests);
    }

    private static StorageSettingsViewModel Create(
        FakeStorageLocationService storage,
        FakeFolderPickerService picker,
        FakeStorageChangeWorkflowService workflow,
        out FakeNotificationService notification,
        FakeLoggingSettingsWorkflowService? loggingWorkflow = null)
    {
        notification = new FakeNotificationService();
        return new StorageSettingsViewModel(
            storage,
            picker,
            workflow,
            loggingWorkflow ?? new FakeLoggingSettingsWorkflowService(),
            new ActivityLogService(),
            notification);
    }
}
