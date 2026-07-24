using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Backup;
using IGoLibrary.Ex.Application.Configuration;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Infrastructure;
using IGoLibrary.Ex.Infrastructure.DataTransfer;
using Microsoft.Extensions.Logging.Abstractions;

namespace IGoLibrary.Ex.Tests;

public sealed class WebDavSyncServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "IGoLibrary-Ex-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Reconcile_DoesNotClearAChangeRaisedWhileFingerprintIsComputed()
    {
        var locations = new StorageLocations(
            Path.Combine(_directory, "data"),
            Path.Combine(_directory, "logs"));
        var settings = AppSettings.Default with
        {
            BackupSync = new BackupSyncSettings(
                Endpoint: "https://dav.example.com/root/",
                AutoUploadEnabled: true)
        };
        var tracker = new FakeTracker();
        tracker.MarkChanged();
        var endpointFingerprint = WebDavSyncStateStore.GetEndpointFingerprint(
            new Uri(settings.BackupSync.Endpoint),
            BackupSyncSettings.BuildRemotePath(settings.BackupSync.RemoteDirectory),
            settings.BackupSync.Username);
        await new WebDavSyncStateStore(
                locations,
                NullLogger<WebDavSyncStateStore>.Instance)
            .SaveAsync(
                new WebDavSyncState(
                    endpointFingerprint,
                    "\"etag\"",
                    null,
                    1,
                    new string('A', 64),
                    "same-fingerprint",
                    DateTimeOffset.Parse("2026-07-18T08:00:00Z")),
                CancellationToken.None);
        var service = new WebDavSyncService(
            new FakeSettingsService(settings),
            new UnusedDataBackupService(),
            new MutatingFingerprintProvider(tracker),
            new FakeBackupSecretStore(),
            tracker,
            new WebDavClient(TimeProvider.System, NullLogger<WebDavClient>.Instance),
            locations,
            new ActivityLogService(),
            NullLogger<WebDavSyncService>.Instance,
            NullLogger<WebDavSyncStateStore>.Instance,
            TimeProvider.System);

        await service.ReconcileLocalStateAsync();

        Assert.Equal(2, tracker.Version);
        Assert.Equal(1, tracker.LastSynchronizedVersion);
        Assert.True(tracker.IsDirty);
    }

    [Fact]
    public async Task Reconcile_RestoresPersistedStatusWhenAutomaticUploadIsDisabled()
    {
        var locations = new StorageLocations(
            Path.Combine(_directory, "data"),
            Path.Combine(_directory, "logs"));
        var settings = AppSettings.Default with
        {
            BackupSync = new BackupSyncSettings(
                Endpoint: "https://dav.example.com/root/")
        };
        var lastSuccessfulSync = DateTimeOffset.Parse("2026-07-18T08:00:00Z");
        var lastModified = DateTimeOffset.Parse("2026-07-18T07:59:00Z");
        var endpointFingerprint = WebDavSyncStateStore.GetEndpointFingerprint(
            new Uri(settings.BackupSync.Endpoint),
            BackupSyncSettings.BuildRemotePath(settings.BackupSync.RemoteDirectory),
            settings.BackupSync.Username);
        await new WebDavSyncStateStore(
                locations,
                NullLogger<WebDavSyncStateStore>.Instance)
            .SaveAsync(
                new WebDavSyncState(
                    endpointFingerprint,
                    "\"etag\"",
                    lastModified,
                    2048,
                    new string('A', 64),
                    "fingerprint",
                    lastSuccessfulSync),
                CancellationToken.None);
        var service = new WebDavSyncService(
            new FakeSettingsService(settings),
            new UnusedDataBackupService(),
            new UnusedFingerprintProvider(),
            new FakeBackupSecretStore(),
            new FakeTracker(),
            new WebDavClient(TimeProvider.System, NullLogger<WebDavClient>.Instance),
            locations,
            new ActivityLogService(),
            NullLogger<WebDavSyncService>.Instance,
            NullLogger<WebDavSyncStateStore>.Instance,
            TimeProvider.System);
        BackupSyncRuntimeStatus? observedStatus = null;
        service.StatusChanged += (_, _) => throw new InvalidOperationException("订阅者失败");
        service.StatusChanged += (_, status) => observedStatus = status;

        await service.ReconcileLocalStateAsync();

        Assert.Equal(lastSuccessfulSync, service.Status.LastSuccessfulSync);
        Assert.Equal(lastModified, service.Status.RemoteMetadata?.LastModified);
        Assert.Equal(2048, service.Status.RemoteMetadata?.ContentLength);
        Assert.Equal("\"etag\"", service.Status.RemoteMetadata?.ETag);
        Assert.Equal(lastSuccessfulSync, observedStatus?.LastSuccessfulSync);
    }

    [Fact]
    public async Task Reconcile_DoesNotRestoreStatusFromAnotherEndpoint()
    {
        var locations = new StorageLocations(
            Path.Combine(_directory, "data"),
            Path.Combine(_directory, "logs"));
        var settings = AppSettings.Default with
        {
            BackupSync = new BackupSyncSettings(
                Endpoint: "https://dav.example.com/root/")
        };
        await new WebDavSyncStateStore(
                locations,
                NullLogger<WebDavSyncStateStore>.Instance)
            .SaveAsync(
                new WebDavSyncState(
                    "another-endpoint",
                    "\"etag\"",
                    null,
                    1,
                    new string('A', 64),
                    "fingerprint",
                    DateTimeOffset.Parse("2026-07-18T08:00:00Z")),
                CancellationToken.None);
        var service = new WebDavSyncService(
            new FakeSettingsService(settings),
            new UnusedDataBackupService(),
            new UnusedFingerprintProvider(),
            new FakeBackupSecretStore(),
            new FakeTracker(),
            new WebDavClient(TimeProvider.System, NullLogger<WebDavClient>.Instance),
            locations,
            new ActivityLogService(),
            NullLogger<WebDavSyncService>.Instance,
            NullLogger<WebDavSyncStateStore>.Instance,
            TimeProvider.System);

        await service.ReconcileLocalStateAsync();

        Assert.Null(service.Status.LastSuccessfulSync);
        Assert.Null(service.Status.RemoteMetadata);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class MutatingFingerprintProvider(FakeTracker tracker)
        : IPersistentDataFingerprintProvider
    {
        public Task<string> ComputeAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            tracker.MarkChanged();
            return Task.FromResult("same-fingerprint");
        }
    }

    private sealed class UnusedFingerprintProvider : IPersistentDataFingerprintProvider
    {
        public Task<string> ComputeAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Automatic-upload reconciliation should not run.");
    }

    private sealed class FakeTracker : IPersistentDataChangeTracker
    {
        public event EventHandler? Changed;

        public long Version { get; private set; }

        public long? LastSynchronizedVersion { get; private set; }

        public bool IsDirty { get; private set; }

        public bool IsAutomaticUploadPaused { get; private set; }

        public string? AutomaticUploadPauseReason { get; private set; }

        public void MarkChanged(bool pauseAutomaticUpload = false, string? pauseReason = null)
        {
            Version++;
            IsDirty = true;
            IsAutomaticUploadPaused = pauseAutomaticUpload;
            AutomaticUploadPauseReason = pauseReason;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void MarkSynchronized(long synchronizedVersion)
        {
            LastSynchronizedVersion = synchronizedVersion;
            if (synchronizedVersion == Version)
            {
                IsDirty = false;
                IsAutomaticUploadPaused = false;
                AutomaticUploadPauseReason = null;
            }
        }
    }

    private sealed class FakeSettingsService(AppSettings settings) : ISettingsService
    {
        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(settings);

        public Task SaveAsync(AppSettings value, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<AppSettings> UpdateAsync(
            Func<AppSettings, AppSettings> update,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class UnusedDataBackupService : IDataBackupService
    {
        public Task<BackupExportResult> ExportAsync(string destinationPath, string password, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PreparedBackup> PrepareImportAsync(string sourcePath, string password, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> StageRestoreAsync(BackupRestoreRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DiscardPreparedAsync(string preparationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeBackupSecretStore : IBackupSecretStore
    {
        public bool IsPersistent => true;
        public Task<string?> LoadBackupPasswordAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task SaveBackupPasswordAsync(string password, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ClearBackupPasswordAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string?> LoadPreviousBackupPasswordAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task SavePreviousBackupPasswordAsync(string password, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ClearPreviousBackupPasswordAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string?> LoadWebDavPasswordAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task SaveWebDavPasswordAsync(string password, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ClearWebDavPasswordAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string?> LoadRestoreSecretAsync(string transactionId, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task SaveRestoreSecretAsync(string transactionId, string value, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ClearRestoreSecretAsync(string transactionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
