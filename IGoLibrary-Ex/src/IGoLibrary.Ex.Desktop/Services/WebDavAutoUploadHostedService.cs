using System.Threading.Channels;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Exceptions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Desktop.Services;

internal sealed class WebDavAutoUploadHostedService(
    IWebDavSyncService syncService,
    ISettingsService settingsService,
    IBackupSecretStore secretStore,
    IPersistentDataChangeTracker changeTracker,
    TimeProvider timeProvider,
    ILogger<WebDavAutoUploadHostedService> logger) : BackgroundService
{
    internal static readonly TimeSpan QuietPeriod = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan MaximumDelay = TimeSpan.FromMinutes(5);
    internal static readonly TimeSpan ExitUploadBudget = TimeSpan.FromSeconds(20);
    internal static readonly TimeSpan InitialRetryDelay = TimeSpan.FromMinutes(1);
    internal static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromMinutes(15);

    private readonly Channel<bool> _changes = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        SingleReader = true,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.DropOldest
    });

    internal event Action<TimeSpan>? DelayScheduled;

    internal event Action<TimeSpan>? RetryDelayScheduled;

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        changeTracker.Changed += OnDataChanged;
        if (changeTracker.IsDirty && !changeTracker.IsAutomaticUploadPaused)
        {
            _changes.Writer.TryWrite(true);
        }

        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await syncService.ReconcileLocalStateAsync(stoppingToken);
        if (changeTracker.IsDirty && !changeTracker.IsAutomaticUploadPaused)
        {
            _changes.Writer.TryWrite(true);
        }

        while (await _changes.Reader.WaitToReadAsync(stoppingToken))
        {
            while (_changes.Reader.TryRead(out _))
            {
            }

            var firstChangeAt = timeProvider.GetUtcNow();
            var lastChangeAt = firstChangeAt;
            while (!stoppingToken.IsCancellationRequested)
            {
                var quietDue = lastChangeAt + QuietPeriod;
                var forcedDue = firstChangeAt + MaximumDelay;
                var due = quietDue < forcedDue ? quietDue : forcedDue;
                var delay = due - timeProvider.GetUtcNow();
                if (delay <= TimeSpan.Zero)
                {
                    break;
                }

                DelayScheduled?.Invoke(delay);
                var quietWait = await WaitForChangeBeforeDelayAsync(delay, stoppingToken);
                if (quietWait == DelayWaitResult.ChannelCompleted)
                {
                    return;
                }

                if (quietWait == DelayWaitResult.DelayElapsed)
                {
                    break;
                }

                while (_changes.Reader.TryRead(out _))
                {
                }

                lastChangeAt = timeProvider.GetUtcNow();
            }

            var result = await TryUploadAsync(stoppingToken);
            var retryDelay = InitialRetryDelay;
            while (result == AutoUploadAttemptResult.Retry &&
                   changeTracker.IsDirty &&
                   !changeTracker.IsAutomaticUploadPaused &&
                   !stoppingToken.IsCancellationRequested)
            {
                RetryDelayScheduled?.Invoke(retryDelay);
                var retryWait = await WaitForChangeBeforeDelayAsync(retryDelay, stoppingToken);
                if (retryWait == DelayWaitResult.ChannelCompleted)
                {
                    return;
                }

                if (retryWait == DelayWaitResult.ChangeAvailable)
                {
                    break;
                }

                result = await TryUploadAsync(stoppingToken);
                retryDelay = TimeSpan.FromTicks(Math.Min(
                    checked(retryDelay.Ticks * 2),
                    MaximumRetryDelay.Ticks));
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        changeTracker.Changed -= OnDataChanged;
        _changes.Writer.TryComplete();
        await base.StopAsync(cancellationToken);
        if (!changeTracker.IsDirty || changeTracker.IsAutomaticUploadPaused)
        {
            return;
        }

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(ExitUploadBudget);
        try
        {
            _ = await TryUploadAsync(budget.Token);
        }
        catch (OperationCanceledException) when (budget.IsCancellationRequested)
        {
            logger.LogWarning("应用退出等待期限内，WebDAV 自动上传未完成；已保留未同步状态。");
        }
    }

    private async Task<AutoUploadAttemptResult> TryUploadAsync(CancellationToken cancellationToken)
    {
        if (!changeTracker.IsDirty || changeTracker.IsAutomaticUploadPaused)
        {
            return AutoUploadAttemptResult.Completed;
        }

        var settings = await settingsService.LoadAsync(cancellationToken);
        if (!settings.BackupSync.AutoUploadEnabled || !secretStore.IsPersistent)
        {
            return AutoUploadAttemptResult.Completed;
        }

        try
        {
            await syncService.UploadAsync(allowOverwrite: false, cancellationToken);
            return AutoUploadAttemptResult.Completed;
        }
        catch (BackupSyncConflictException ex)
        {
            changeTracker.MarkChanged(
                pauseAutomaticUpload: true,
                pauseReason: ex.Message);
            logger.LogWarning("因远端基线发生变化，WebDAV 自动上传已暂停。");
            return AutoUploadAttemptResult.Completed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "WebDAV 自动上传失败；将保留未同步状态并重试。");
            return AutoUploadAttemptResult.Retry;
        }
    }

    private async Task<DelayWaitResult> WaitForChangeBeforeDelayAsync(
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        using var race = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var delayTask = Task.Delay(delay, timeProvider, race.Token);
        var changedTask = _changes.Reader.WaitToReadAsync(race.Token).AsTask();
        var completed = await Task.WhenAny(delayTask, changedTask);
        if (completed == changedTask)
        {
            var hasChange = await changedTask;
            race.Cancel();
            await IgnoreRaceCancellationAsync(delayTask, race.Token);
            return hasChange
                ? DelayWaitResult.ChangeAvailable
                : DelayWaitResult.ChannelCompleted;
        }

        await delayTask;
        race.Cancel();
        await IgnoreRaceCancellationAsync(changedTask, race.Token);
        return DelayWaitResult.DelayElapsed;
    }

    private static async Task IgnoreRaceCancellationAsync(Task task, CancellationToken raceToken)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException) when (raceToken.IsCancellationRequested)
        {
        }
    }

    private void OnDataChanged(object? sender, EventArgs args)
        => _changes.Writer.TryWrite(true);

    private enum AutoUploadAttemptResult
    {
        Completed,
        Retry
    }

    private enum DelayWaitResult
    {
        DelayElapsed,
        ChangeAvailable,
        ChannelCompleted
    }
}
