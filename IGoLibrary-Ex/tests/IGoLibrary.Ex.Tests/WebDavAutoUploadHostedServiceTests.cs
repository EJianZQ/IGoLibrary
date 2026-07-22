using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Backup;
using IGoLibrary.Ex.Application.Configuration;
using IGoLibrary.Ex.Desktop.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace IGoLibrary.Ex.Tests;

public sealed class WebDavAutoUploadHostedServiceTests
{
    [Fact]
    public async Task DirtyData_UploadsAfterThirtySecondQuietPeriod()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-07-18T08:00:00Z"));
        var tracker = new FakeTracker { IsDirty = true };
        var sync = new FakeSync(tracker);
        var service = Create(sync, tracker, time);
        var delayScheduled = new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.DelayScheduled += delay => delayScheduled.TrySetResult(delay);
        await service.StartAsync(CancellationToken.None);
        await sync.Reconciled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(
            WebDavAutoUploadHostedService.QuietPeriod,
            await delayScheduled.Task.WaitAsync(TimeSpan.FromSeconds(2)));

        time.Advance(TimeSpan.FromSeconds(29));
        Assert.Equal(0, sync.UploadCount);
        time.Advance(TimeSpan.FromSeconds(1));

        await sync.Uploaded.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, sync.UploadCount);
        Assert.False(sync.LastAllowOverwrite);
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task PausedLocalImport_DoesNotAutomaticallyUpload()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-07-18T08:00:00Z"));
        var tracker = new FakeTracker
        {
            IsDirty = true,
            IsAutomaticUploadPaused = true,
            AutomaticUploadPauseReason = "等待手动确认"
        };
        var sync = new FakeSync(tracker);
        var service = Create(sync, tracker, time);
        await service.StartAsync(CancellationToken.None);
        await sync.Reconciled.Task.WaitAsync(TimeSpan.FromSeconds(2));

        time.Advance(TimeSpan.FromHours(1));
        await Task.Yield();

        Assert.Equal(0, sync.UploadCount);
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ContinuousChanges_CannotPostponeUploadBeyondFiveMinutes()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-07-18T08:00:00Z"));
        var tracker = new FakeTracker { IsDirty = true };
        var sync = new FakeSync(tracker);
        var service = Create(sync, tracker, time);
        var delays = System.Threading.Channels.Channel.CreateUnbounded<TimeSpan>();
        service.DelayScheduled += delay => delays.Writer.TryWrite(delay);
        await service.StartAsync(CancellationToken.None);
        await sync.Reconciled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await delays.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));

        for (var index = 0; index < 10; index++)
        {
            time.Advance(TimeSpan.FromSeconds(29));
            tracker.MarkChanged();
            await delays.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        }

        Assert.Equal(0, sync.UploadCount);
        time.Advance(TimeSpan.FromSeconds(10));
        await sync.Uploaded.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, sync.UploadCount);
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Stop_PerformsFinalUploadWhenDirty()
    {
        var tracker = new FakeTracker { IsDirty = true };
        var sync = new FakeSync(tracker);
        var service = Create(sync, tracker, TimeProvider.System);
        await service.StartAsync(CancellationToken.None);
        await sync.Reconciled.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await service.StopAsync(CancellationToken.None);

        Assert.Equal(1, sync.UploadCount);
    }

    [Fact]
    public async Task TransientFailure_RetriesWithoutAnotherLocalChange()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-07-18T08:00:00Z"));
        var tracker = new FakeTracker { IsDirty = true };
        var sync = new FakeSync(tracker) { FailuresRemaining = 1 };
        var service = Create(sync, tracker, time);
        var retryScheduled = new TaskCompletionSource<TimeSpan>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        service.RetryDelayScheduled += delay => retryScheduled.TrySetResult(delay);
        await service.StartAsync(CancellationToken.None);
        await sync.Reconciled.Task.WaitAsync(TimeSpan.FromSeconds(2));

        time.Advance(WebDavAutoUploadHostedService.QuietPeriod);
        Assert.Equal(
            WebDavAutoUploadHostedService.InitialRetryDelay,
            await retryScheduled.Task.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(1, sync.UploadCount);
        Assert.True(tracker.IsDirty);

        time.Advance(WebDavAutoUploadHostedService.InitialRetryDelay - TimeSpan.FromSeconds(1));
        Assert.Equal(1, sync.UploadCount);
        time.Advance(TimeSpan.FromSeconds(1));

        await sync.Uploaded.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(2, sync.UploadCount);
        Assert.False(tracker.IsDirty);
        await service.StopAsync(CancellationToken.None);
    }

    private static WebDavAutoUploadHostedService Create(
        FakeSync sync,
        FakeTracker tracker,
        TimeProvider timeProvider)
        => new(
            sync,
            new FakeSettingsService(),
            new FakeBackupSecretStore(),
            tracker,
            timeProvider,
            NullLogger<WebDavAutoUploadHostedService>.Instance);

    private sealed class FakeSync(FakeTracker tracker) : IWebDavSyncService
    {
        public TaskCompletionSource Reconciled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Uploaded { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int UploadCount { get; private set; }

        public int FailuresRemaining { get; set; }

        public bool LastAllowOverwrite { get; private set; }

        public BackupSyncRuntimeStatus Status => BackupSyncRuntimeStatus.Idle;

        public event EventHandler<BackupSyncRuntimeStatus>? StatusChanged
        {
            add { }
            remove { }
        }

        public Task ReconcileLocalStateAsync(CancellationToken cancellationToken = default)
        {
            Reconciled.TrySetResult();
            return Task.CompletedTask;
        }

        public Task RecordRestoredBaselineAsync(
            string semanticFingerprint,
            WebDavRemoteMetadata metadata,
            string expectedEndpointFingerprint,
            string remoteFileSha256,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<WebDavRemoteMetadata> TestConnectionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new WebDavRemoteMetadata(false, null, null, null));

        public Task<WebDavUploadResult> UploadAsync(
            bool allowOverwrite,
            CancellationToken cancellationToken = default)
        {
            UploadCount++;
            LastAllowOverwrite = allowOverwrite;
            if (FailuresRemaining > 0)
            {
                FailuresRemaining--;
                throw new HttpRequestException("temporary WebDAV outage");
            }

            tracker.IsDirty = false;
            Uploaded.TrySetResult();
            return Task.FromResult(new WebDavUploadResult(
                new WebDavRemoteMetadata(true, 1, "etag", null),
                Manifest(),
                "operation"));
        }

        public Task<WebDavDownloadResult> DownloadAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DiscardDownloadAsync(string localFilePath, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        private static BackupManifest Manifest()
            => new(
                1,
                "1.0.0",
                1,
                DateTimeOffset.UtcNow,
                "windows",
                1,
                new string('A', 64),
                0,
                new string('B', 64),
                new string('C', 64),
                new BackupDataSummary(0, 0, 0, 0, 0, false, false, false));
    }

    private sealed class FakeTracker : IPersistentDataChangeTracker
    {
        public event EventHandler? Changed;

        public long Version { get; private set; }

        public bool IsDirty { get; set; }

        public bool IsAutomaticUploadPaused { get; set; }

        public string? AutomaticUploadPauseReason { get; set; }

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
            if (synchronizedVersion == Version)
            {
                IsDirty = false;
                IsAutomaticUploadPaused = false;
            }
        }
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        private AppSettings _settings = AppSettings.Default with
        {
            BackupSync = BackupSyncSettings.Default with { AutoUploadEnabled = true }
        };

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_settings);

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            _settings = settings;
            return Task.CompletedTask;
        }

        public Task<AppSettings> UpdateAsync(
            Func<AppSettings, AppSettings> update,
            CancellationToken cancellationToken = default)
        {
            _settings = update(_settings);
            return Task.FromResult(_settings);
        }
    }

    private sealed class FakeBackupSecretStore : IBackupSecretStore
    {
        public bool IsPersistent => true;

        public Task<string?> LoadBackupPasswordAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<string?>("password-1234");

        public Task SaveBackupPasswordAsync(string password, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ClearBackupPasswordAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string?> LoadPreviousBackupPasswordAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task SavePreviousBackupPasswordAsync(string password, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ClearPreviousBackupPasswordAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string?> LoadWebDavPasswordAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task SaveWebDavPasswordAsync(string password, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ClearWebDavPasswordAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string?> LoadRestoreSecretAsync(string transactionId, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task SaveRestoreSecretAsync(string transactionId, string value, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ClearRestoreSecretAsync(string transactionId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
