using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Backup;
using IGoLibrary.Ex.Application.Configuration;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Desktop.ViewModels;

namespace IGoLibrary.Ex.Tests;

public sealed class WebDavSyncViewModelTests
{
    [Fact]
    public async Task Save_WithBlankUsername_ClearsAnExistingPasswordForAnonymousAccess()
    {
        var settings = new FakeSettingsService(ConfiguredSettings());
        var secrets = new FakeBackupSecretStore { WebDavPassword = "stored-password" };
        var viewModel = Create(settings, secrets);
        await viewModel.InitializeAsync(settings.Current.BackupSync);
        viewModel.Username = string.Empty;
        viewModel.Password = string.Empty;

        await viewModel.SaveCoreAsync(CancellationToken.None);

        Assert.Equal(string.Empty, settings.Current.BackupSync.Username);
        Assert.Null(secrets.WebDavPassword);
        Assert.Equal(["clear"], secrets.PasswordOperations);
    }

    [Fact]
    public async Task Save_WhenSettingsPersistenceFails_RestoresThePreviousPassword()
    {
        var settings = new FakeSettingsService(ConfiguredSettings())
        {
            UpdateException = new IOException("settings unavailable")
        };
        var secrets = new FakeBackupSecretStore { WebDavPassword = "old-password" };
        var viewModel = Create(settings, secrets);
        await viewModel.InitializeAsync(settings.Current.BackupSync);
        viewModel.Password = "new-password";

        var error = await Assert.ThrowsAsync<IOException>(() =>
            viewModel.SaveCoreAsync(CancellationToken.None));

        Assert.Equal("settings unavailable", error.Message);
        Assert.Equal("old-password", secrets.WebDavPassword);
        Assert.Equal(["save:new-password", "save:old-password"], secrets.PasswordOperations);
        Assert.Equal("account", settings.Current.BackupSync.Username);
    }

    [Fact]
    public async Task Save_WithTlsSkip_RequiresConfirmationAndPersistsTheChoice()
    {
        var settings = new FakeSettingsService(ConfiguredSettings());
        var secrets = new FakeBackupSecretStore { WebDavPassword = "stored-password" };
        var dialog = new NoOpBackupDialogService { ConfirmTlsSkip = true };
        var viewModel = Create(settings, secrets, dialog);
        await viewModel.InitializeAsync(settings.Current.BackupSync);
        viewModel.RemoteDirectory = "备份/我的数据";
        viewModel.SelectedTlsVerifyModeIndex = (int)WebDavTlsVerifyMode.Skip;

        await viewModel.SaveCoreAsync(CancellationToken.None);

        Assert.Equal("备份/我的数据", settings.Current.BackupSync.RemoteDirectory);
        Assert.Equal(WebDavTlsVerifyMode.Skip, settings.Current.BackupSync.TlsVerifyMode);
        Assert.Equal(1, dialog.TlsSkipConfirmationCalls);
    }

    [Fact]
    public async Task Save_WithTlsSkipDeclined_DoesNotPersistTheChoice()
    {
        var settings = new FakeSettingsService(ConfiguredSettings());
        var secrets = new FakeBackupSecretStore { WebDavPassword = "stored-password" };
        var dialog = new NoOpBackupDialogService { ConfirmTlsSkip = false };
        var viewModel = Create(settings, secrets, dialog);
        await viewModel.InitializeAsync(settings.Current.BackupSync);
        viewModel.SelectedTlsVerifyModeIndex = (int)WebDavTlsVerifyMode.Skip;

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            viewModel.SaveCoreAsync(CancellationToken.None));

        Assert.Equal(WebDavTlsVerifyMode.Verify, settings.Current.BackupSync.TlsVerifyMode);
        Assert.Equal(1, dialog.TlsSkipConfirmationCalls);
    }

    private static WebDavSyncViewModel Create(
        FakeSettingsService settings,
        FakeBackupSecretStore secrets,
        NoOpBackupDialogService? dialog = null)
        => new(
            settings,
            secrets,
            new NoOpWebDavSyncService(),
            new NoOpBackupWorkflowService(),
            dialog ?? new NoOpBackupDialogService(),
            new ActivityLogService(),
            new FakeNotificationService());

    private static AppSettings ConfiguredSettings()
        => AppSettings.Default with
        {
            BackupSync = new BackupSyncSettings(
                Endpoint: "https://dav.example.com/root/",
                Username: "account")
        };

    private sealed class FakeSettingsService(AppSettings settings) : ISettingsService
    {
        public AppSettings Current { get; private set; } = settings;

        public Exception? UpdateException { get; init; }

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Current);

        public Task SaveAsync(AppSettings value, CancellationToken cancellationToken = default)
        {
            Current = value;
            return Task.CompletedTask;
        }

        public Task<AppSettings> UpdateAsync(
            Func<AppSettings, AppSettings> update,
            CancellationToken cancellationToken = default)
        {
            if (UpdateException is not null)
            {
                throw UpdateException;
            }

            Current = update(Current);
            return Task.FromResult(Current);
        }
    }

    private sealed class FakeBackupSecretStore : IBackupSecretStore
    {
        public bool IsPersistent => true;

        public string? WebDavPassword { get; set; }

        public List<string> PasswordOperations { get; } = [];

        public Task<string?> LoadBackupPasswordAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<string?>("backup-password");

        public Task SaveBackupPasswordAsync(string password, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ClearBackupPasswordAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string?> LoadPreviousBackupPasswordAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task SavePreviousBackupPasswordAsync(string password, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ClearPreviousBackupPasswordAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string?> LoadWebDavPasswordAsync(CancellationToken cancellationToken = default) => Task.FromResult(WebDavPassword);

        public Task SaveWebDavPasswordAsync(string password, CancellationToken cancellationToken = default)
        {
            WebDavPassword = password;
            PasswordOperations.Add("save:" + password);
            return Task.CompletedTask;
        }

        public Task ClearWebDavPasswordAsync(CancellationToken cancellationToken = default)
        {
            WebDavPassword = null;
            PasswordOperations.Add("clear");
            return Task.CompletedTask;
        }

        public Task<string?> LoadRestoreSecretAsync(string transactionId, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task SaveRestoreSecretAsync(string transactionId, string value, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ClearRestoreSecretAsync(string transactionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class NoOpWebDavSyncService : IWebDavSyncService
    {
        public BackupSyncRuntimeStatus Status => BackupSyncRuntimeStatus.Idle;
        public event EventHandler<BackupSyncRuntimeStatus>? StatusChanged { add { } remove { } }
        public Task ReconcileLocalStateAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RecordRestoredBaselineAsync(string semanticFingerprint, WebDavRemoteMetadata metadata, string expectedEndpointFingerprint, string remoteFileSha256, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<WebDavRemoteMetadata> TestConnectionAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WebDavUploadResult> UploadAsync(bool allowOverwrite, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WebDavDownloadResult> DownloadAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DiscardDownloadAsync(string localFilePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoOpBackupWorkflowService : IBackupWorkflowService
    {
        public Task<bool> ExportLocalAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ImportLocalAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> DownloadAndRestoreAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> UploadAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ChangeBackupPasswordAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class NoOpBackupDialogService : IBackupDialogService
    {
        public bool ConfirmTlsSkip { get; init; } = true;
        public int TlsSkipConfirmationCalls { get; private set; }

        public Task<string?> RequestPasswordAsync(string title, string message, bool requireConfirmation, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ConfirmRestoreAsync(PreparedBackup backup, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ConfirmInsecureHttpAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> ConfirmSkipTlsVerificationAsync(CancellationToken cancellationToken = default)
        {
            TlsSkipConfirmationCalls++;
            return Task.FromResult(ConfirmTlsSkip);
        }
        public Task<bool> ConfirmRemoteOverwriteAsync(WebDavRemoteMetadata? metadata, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<BackupPasswordChangeDecision> ConfirmPasswordChangeAsync(bool webDavConfigured, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
