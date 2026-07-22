using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Backup;
using IGoLibrary.Ex.Application.Configuration;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Infrastructure.Security;

namespace IGoLibrary.Ex.Tests;

public sealed class BackupWorkflowServiceTests
{
    [Fact]
    public async Task PasswordRotation_AmbiguousUploadKeepsNewPasswordAndProtectedPreviousPassword()
    {
        var secretStore = new PlatformBackupSecretStore(new InMemoryBackupSecretBackend());
        await secretStore.SaveBackupPasswordAsync("old-password");
        var webDav = new FakeWebDavSyncService
        {
            UploadException = new IOException("response lost after PUT")
        };
        var service = CreatePasswordWorkflow(secretStore, webDav);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ChangeBackupPasswordAsync());

        Assert.Contains("无法确认", error.Message, StringComparison.Ordinal);
        Assert.Equal("new-password", await secretStore.LoadBackupPasswordAsync());
        Assert.Equal("old-password", await secretStore.LoadPreviousBackupPasswordAsync());
        Assert.Equal(1, webDav.UploadCalls);
    }

    [Fact]
    public async Task SuccessfulManualUpload_ClearsPreviousPasswordRecoveryCopy()
    {
        var secretStore = new PlatformBackupSecretStore(new InMemoryBackupSecretBackend());
        await secretStore.SaveBackupPasswordAsync("new-password");
        await secretStore.SavePreviousBackupPasswordAsync("old-password");
        var webDav = new FakeWebDavSyncService();
        var service = CreatePasswordWorkflow(secretStore, webDav);

        Assert.True(await service.UploadAsync());

        Assert.Null(await secretStore.LoadPreviousBackupPasswordAsync());
        Assert.Equal(1, webDav.UploadCalls);
    }

    [Fact]
    public async Task Import_StopsTasksAndFlushesBeforePreparingComparison()
    {
        var calls = new List<string>();
        var backup = new RecordingBackupService(calls);
        var dialog = new FakeBackupDialogService(calls)
        {
            RestoreConfirmed = false
        };
        var service = new BackupWorkflowService(
            backup,
            new FakeWebDavSyncService(),
            new PlatformBackupSecretStore(new InMemoryBackupSecretBackend()),
            new FixedSettingsService(),
            new FixedFilePickerService(),
            dialog,
            new RecordingFlushService(calls),
            new RecordingActiveTaskService(calls),
            new RecordingStorageDialogService(calls),
            new NoOpRestoreRestartService(),
            new ActivityLogService(),
            new NoOpNotificationService(),
            TimeProvider.System);

        Assert.False(await service.ImportLocalAsync());

        Assert.Equal(
            ["confirm-stop", "stop", "flush", "prepare", "confirm-restore", "discard"],
            calls);
    }

    private static BackupWorkflowService CreatePasswordWorkflow(
        IBackupSecretStore secretStore,
        IWebDavSyncService webDav)
        => new(
            null!,
            webDav,
            secretStore,
            new FixedSettingsService(),
            null!,
            new FakeBackupDialogService
            {
                RequestedPassword = "new-password",
                PasswordChangeDecision = BackupPasswordChangeDecision.SaveAndUpload
            },
            new RecordingFlushService([]),
            null!,
            null!,
            null!,
            new ActivityLogService(),
            new NoOpNotificationService(),
            TimeProvider.System);

    private sealed class FakeWebDavSyncService : IWebDavSyncService
    {
        public Exception? UploadException { get; init; }
        public int UploadCalls { get; private set; }
        public BackupSyncRuntimeStatus Status => BackupSyncRuntimeStatus.Idle;
        public event EventHandler<BackupSyncRuntimeStatus>? StatusChanged { add { } remove { } }

        public Task<WebDavUploadResult> UploadAsync(
            bool allowOverwrite,
            CancellationToken cancellationToken = default)
        {
            UploadCalls++;
            return UploadException is null
                ? Task.FromResult<WebDavUploadResult>(null!)
                : Task.FromException<WebDavUploadResult>(UploadException);
        }

        public Task ReconcileLocalStateAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RecordRestoredBaselineAsync(string semanticFingerprint, WebDavRemoteMetadata metadata, string expectedEndpointFingerprint, string remoteFileSha256, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<WebDavRemoteMetadata> TestConnectionAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WebDavDownloadResult> DownloadAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DiscardDownloadAsync(string localFilePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FixedSettingsService : ISettingsService
    {
        private AppSettings _settings = AppSettings.Default with
        {
            BackupSync = new BackupSyncSettings(Endpoint: "https://dav.example.com/")
        };

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_settings);

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            _settings = settings;
            return Task.CompletedTask;
        }

        public Task<AppSettings> UpdateAsync(Func<AppSettings, AppSettings> update, CancellationToken cancellationToken = default)
        {
            _settings = update(_settings);
            return Task.FromResult(_settings);
        }
    }

    private sealed class FakeBackupDialogService(List<string>? calls = null) : IBackupDialogService
    {
        public string? RequestedPassword { get; init; } = "import-password";
        public BackupPasswordChangeDecision PasswordChangeDecision { get; init; } = BackupPasswordChangeDecision.SaveOnly;
        public bool RestoreConfirmed { get; init; }

        public Task<string?> RequestPasswordAsync(string title, string message, bool requireConfirmation, CancellationToken cancellationToken = default)
            => Task.FromResult(RequestedPassword);

        public Task<bool> ConfirmRestoreAsync(PreparedBackup backup, CancellationToken cancellationToken = default)
        {
            calls?.Add("confirm-restore");
            return Task.FromResult(RestoreConfirmed);
        }

        public Task<bool> ConfirmInsecureHttpAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> ConfirmSkipTlsVerificationAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> ConfirmRemoteOverwriteAsync(WebDavRemoteMetadata? metadata, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<BackupPasswordChangeDecision> ConfirmPasswordChangeAsync(bool webDavConfigured, CancellationToken cancellationToken = default) => Task.FromResult(PasswordChangeDecision);
    }

    private sealed class RecordingBackupService(List<string> calls) : IDataBackupService
    {
        public Task<BackupExportResult> ExportAsync(string destinationPath, string password, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<PreparedBackup> PrepareImportAsync(string sourcePath, string password, CancellationToken cancellationToken = default)
        {
            calls.Add("prepare");
            return Task.FromResult(new PreparedBackup("prep", sourcePath, null!, null!, "operation"));
        }

        public Task<string> StageRestoreAsync(BackupRestoreRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task DiscardPreparedAsync(string preparationId, CancellationToken cancellationToken = default)
        {
            calls.Add("discard");
            return Task.CompletedTask;
        }
    }

    private sealed class FixedFilePickerService : IBackupFilePickerService
    {
        public Task<string?> PickExportPathAsync(string suggestedFileName, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<string?> PickImportPathAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>("source.igobackup");
    }

    private sealed class RecordingFlushService(List<string> calls) : IBackupDataFlushService
    {
        public void Configure(Func<CancellationToken, Task> flushAsync) { }

        public Task FlushAsync(CancellationToken cancellationToken = default)
        {
            calls.Add("flush");
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingActiveTaskService(List<string> calls) : IActiveBackupTaskService
    {
        public IReadOnlyList<string> GetActiveTaskNames() => ["正在运行的任务"];

        public Task StopAllAsync(CancellationToken cancellationToken = default)
        {
            calls.Add("stop");
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingStorageDialogService(List<string> calls) : IStorageChangeDialogService
    {
        public Task<bool> ConfirmStopTasksAsync(IReadOnlyList<string> taskNames, CancellationToken cancellationToken = default)
        {
            calls.Add("confirm-stop");
            return Task.FromResult(true);
        }

        public Task<StorageMigrationDecision> ConfirmMigrationAsync(StorageLocations current, StorageLocations target, bool dataDirectoryChanged, bool logDirectoryChanged, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ConfirmOverwriteDatabaseAsync(string databasePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ConfirmUseExistingDatabaseAsync(string databasePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class NoOpRestoreRestartService : IDataRestoreRestartService
    {
        public Task RestartAsync(string transactionId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoOpNotificationService : INotificationService
    {
        public Task ShowInfoAsync(string title, string message, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ShowWarningAsync(string title, string message, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ShowSuccessAsync(string title, string message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
