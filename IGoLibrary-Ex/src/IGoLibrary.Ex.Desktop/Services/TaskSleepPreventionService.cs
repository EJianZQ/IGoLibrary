using System.Threading.Channels;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Desktop.Platform.Power;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Desktop.Services;

internal sealed class TaskSleepPreventionService(
    ISettingsService settingsService,
    IGrabSeatCoordinator grabSeatCoordinator,
    IGlobalLeakCoordinator globalLeakCoordinator,
    IOccupySeatCoordinator occupySeatCoordinator,
    ITomorrowReservationCoordinator tomorrowReservationCoordinator,
    ISystemIdleSleepInhibitor sleepInhibitor,
    IActivityLogService activityLogService,
    TimeProvider timeProvider,
    ILogger<TaskSleepPreventionService> logger) : BackgroundService, ITaskSleepPreventionService
{
    internal const string PowerRequestReason = "IGoLibrary-Ex 正在执行图书馆任务";

    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(20),
        TimeSpan.FromSeconds(40),
        TimeSpan.FromSeconds(60)
    ];

    private readonly object _settingsGate = new();
    private readonly Channel<bool> _signals = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        SingleReader = true,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.DropOldest
    });

    private bool _enabled = true;
    private bool _lastProcessedEnabled = true;
    private bool _subscriptionsAttached;
    private bool _hadReconciliationFailure;
    private long _requestedReconciliationVersion;
    private long _processedReconciliationVersion;

    internal event Action<TimeSpan>? RetryDelayScheduled;

    internal long RequestedReconciliationVersion => Interlocked.Read(ref _requestedReconciliationVersion);

    internal long ProcessedReconciliationVersion => Interlocked.Read(ref _processedReconciliationVersion);

    public void SetEnabled(bool enabled)
    {
        lock (_settingsGate)
        {
            if (_enabled == enabled)
            {
                return;
            }

            _enabled = enabled;
        }

        RequestReconciliation();
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        sleepInhibitor.CleanupFailed += OnSleepInhibitorCleanupFailed;
        AttachCoordinatorSubscriptions();

        try
        {
            var settings = await settingsService.LoadAsync(cancellationToken);
            lock (_settingsGate)
            {
                _enabled = settings.Ui.PreventSystemSleepWhileTasksActive;
                _lastProcessedEnabled = _enabled;
            }

            logger.LogInformation(
                "任务期间阻止系统自动休眠服务已启动。平台={Platform}；是否启用={Enabled}。",
                sleepInhibitor.PlatformName,
                _enabled);
            WriteActivity(
                LogEntryKind.Info,
                $"任务防休眠服务已启动，平台：{sleepInhibitor.PlatformName}，设置：{FormatEnabled(_enabled)}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            DetachCoordinatorSubscriptions();
            sleepInhibitor.CleanupFailed -= OnSleepInhibitorCleanupFailed;
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "加载任务期间阻止系统自动休眠设置失败；将使用默认启用状态。平台={Platform}。",
                sleepInhibitor.PlatformName);
            WriteActivity(
                LogEntryKind.Warning,
                $"读取任务防休眠设置失败，将按默认开启状态运行：{ex.Message}");
        }

        if (!sleepInhibitor.IsSupported)
        {
            logger.LogWarning(
                "平台 {Platform} 不支持任务期间阻止系统自动休眠；任务将继续正常运行。",
                sleepInhibitor.PlatformName);
            WriteActivity(
                LogEntryKind.Warning,
                $"当前平台不支持任务防休眠（{sleepInhibitor.PlatformName}），任务将继续正常运行");
        }

        RequestReconciliation();
        try
        {
            await base.StartAsync(cancellationToken);
        }
        catch
        {
            DetachCoordinatorSubscriptions();
            sleepInhibitor.CleanupFailed -= OnSleepInhibitorCleanupFailed;
            throw;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        DetachCoordinatorSubscriptions();
        try
        {
            await base.StopAsync(cancellationToken);
        }
        finally
        {
            _signals.Writer.TryComplete();
            sleepInhibitor.CleanupFailed -= OnSleepInhibitorCleanupFailed;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan? retryDelay = null;
        var retryIndex = 0;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (!await WaitForSignalOrRetryAsync(retryDelay, stoppingToken))
                {
                    break;
                }

                while (_signals.Reader.TryRead(out _))
                {
                }

                LogSettingChangeIfNeeded();
                var requestedVersion = Volatile.Read(ref _requestedReconciliationVersion);
                var result = Reconcile();
                MarkReconciliationProcessed(requestedVersion);
                if (result.Succeeded)
                {
                    retryDelay = null;
                    retryIndex = 0;
                    if (_hadReconciliationFailure)
                    {
                        _hadReconciliationFailure = false;
                        logger.LogInformation(
                            "任务期间阻止系统自动休眠功能已从先前的原生电源管理故障中恢复。平台={Platform}。",
                            sleepInhibitor.PlatformName);
                        WriteActivity(LogEntryKind.Success, "任务防休眠已从系统调用失败中恢复");
                    }

                    continue;
                }

                _hadReconciliationFailure = true;
                retryDelay = RetryDelays[Math.Min(retryIndex, RetryDelays.Length - 1)];
                retryIndex++;
                LogReconciliationFailure(result, retryDelay.Value, retryIndex);
                try
                {
                    RetryDelayScheduled?.Invoke(retryDelay.Value);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "通知任务休眠阻止重试观察器失败。");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            ReleaseForShutdown();
        }
    }

    private ReconciliationResult Reconcile()
    {
        var enabled = GetEnabled();
        IReadOnlyList<string> activeTaskNames = [];
        var operation = "读取活动任务状态";

        try
        {
            activeTaskNames = GetActiveTaskNames();
            var shouldPreventSleep = enabled && activeTaskNames.Count > 0;
            if (!sleepInhibitor.IsSupported)
            {
                return ReconciliationResult.Success();
            }

            if (shouldPreventSleep && !sleepInhibitor.IsActive)
            {
                operation = "申请阻止系统自动休眠";
                sleepInhibitor.Activate(PowerRequestReason);
                var names = string.Join("、", activeTaskNames);
                logger.LogInformation(
                    "已阻止系统因空闲自动休眠。平台={Platform}；活动任务={ActiveTasks}。",
                    sleepInhibitor.PlatformName,
                    names);
                WriteActivity(
                    LogEntryKind.Success,
                    $"已阻止系统自动休眠，活动任务：{names}");
            }
            else if (!shouldPreventSleep && sleepInhibitor.IsActive)
            {
                operation = "释放系统自动休眠阻止";
                sleepInhibitor.Deactivate();
                var releaseReason = enabled ? "所有任务均已结束" : "用户已关闭任务防休眠";
                logger.LogInformation(
                    "已释放系统空闲休眠阻止。平台={Platform}；原因={Reason}。",
                    sleepInhibitor.PlatformName,
                    releaseReason);
                WriteActivity(
                    LogEntryKind.Info,
                    $"{releaseReason}，已允许系统按电源设置自动休眠");
            }

            return ReconciliationResult.Success();
        }
        catch (Exception ex)
        {
            return ReconciliationResult.Failure(
                operation,
                activeTaskNames,
                ex);
        }
    }

    private async Task<bool> WaitForSignalOrRetryAsync(
        TimeSpan? retryDelay,
        CancellationToken cancellationToken)
    {
        if (retryDelay is null)
        {
            return await _signals.Reader.WaitToReadAsync(cancellationToken);
        }

        using var race = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var signalTask = _signals.Reader.WaitToReadAsync(race.Token).AsTask();
        var delayTask = Task.Delay(retryDelay.Value, timeProvider, race.Token);
        var completed = await Task.WhenAny(signalTask, delayTask);
        if (completed == signalTask)
        {
            var hasSignal = await signalTask;
            race.Cancel();
            await IgnoreRaceCancellationAsync(delayTask, race.Token);
            return hasSignal;
        }

        await delayTask;
        race.Cancel();
        await IgnoreRaceCancellationAsync(signalTask, race.Token);
        return true;
    }

    private void LogSettingChangeIfNeeded()
    {
        var enabled = GetEnabled();
        if (_lastProcessedEnabled == enabled)
        {
            return;
        }

        _lastProcessedEnabled = enabled;
        logger.LogInformation("任务期间阻止系统自动休眠设置已变更。是否启用={Enabled}。", enabled);
        WriteActivity(
            LogEntryKind.Info,
            enabled ? "已开启任务进行时阻止系统自动休眠" : "已关闭任务进行时阻止系统自动休眠");
    }

    private void LogReconciliationFailure(
        ReconciliationResult result,
        TimeSpan retryDelay,
        int attempt)
    {
        var nativeException = result.Exception as SystemSleepInhibitorException;
        logger.LogError(
            result.Exception,
            "任务休眠阻止的原生操作失败。平台={Platform}；操作={Operation}；原生操作={NativeOperation}；原生错误码={NativeErrorCode}；活动任务={ActiveTasks}；重试次数={RetryAttempt}；重试延迟秒数={RetryDelaySeconds}。",
            sleepInhibitor.PlatformName,
            result.Operation,
            nativeException?.Operation ?? "unknown",
            nativeException?.NativeErrorCode,
            string.Join("、", result.ActiveTaskNames),
            attempt,
            retryDelay.TotalSeconds);
        WriteActivity(
            LogEntryKind.Warning,
            $"{result.Operation}失败，将在 {retryDelay.TotalSeconds:0} 秒后重试：{result.Exception?.Message}");
    }

    private IReadOnlyList<string> GetActiveTaskNames()
    {
        var activeTasks = new List<string>(4);
        AddIfActive(activeTasks, "抢座", grabSeatCoordinator.GetStatus());
        AddIfActive(activeTasks, "全域捡漏", globalLeakCoordinator.GetStatus());
        AddIfActive(activeTasks, "占座", occupySeatCoordinator.GetStatus());
        AddIfActive(activeTasks, "明日预约", tomorrowReservationCoordinator.GetStatus());
        return activeTasks;
    }

    private static void AddIfActive(ICollection<string> names, string name, CoordinatorStatus status)
    {
        if (status.IsActive)
        {
            names.Add(name);
        }
    }

    private bool GetEnabled()
    {
        lock (_settingsGate)
        {
            return _enabled;
        }
    }

    private void AttachCoordinatorSubscriptions()
    {
        if (_subscriptionsAttached)
        {
            return;
        }

        grabSeatCoordinator.StatusChanged += OnCoordinatorStatusChanged;
        globalLeakCoordinator.StatusChanged += OnCoordinatorStatusChanged;
        occupySeatCoordinator.StatusChanged += OnCoordinatorStatusChanged;
        tomorrowReservationCoordinator.StatusChanged += OnCoordinatorStatusChanged;
        _subscriptionsAttached = true;
    }

    private void DetachCoordinatorSubscriptions()
    {
        if (!_subscriptionsAttached)
        {
            return;
        }

        grabSeatCoordinator.StatusChanged -= OnCoordinatorStatusChanged;
        globalLeakCoordinator.StatusChanged -= OnCoordinatorStatusChanged;
        occupySeatCoordinator.StatusChanged -= OnCoordinatorStatusChanged;
        tomorrowReservationCoordinator.StatusChanged -= OnCoordinatorStatusChanged;
        _subscriptionsAttached = false;
    }

    private void OnCoordinatorStatusChanged(object? sender, CoordinatorStatus status)
        => RequestReconciliation();

    private void RequestReconciliation()
    {
        Interlocked.Increment(ref _requestedReconciliationVersion);
        _signals.Writer.TryWrite(true);
    }

    private void ReleaseForShutdown()
    {
        try
        {
            if (sleepInhibitor.IsActive)
            {
                sleepInhibitor.Deactivate();
                logger.LogInformation(
                    "应用退出期间已释放系统空闲休眠阻止。平台={Platform}。",
                    sleepInhibitor.PlatformName);
                WriteActivity(
                    LogEntryKind.Info,
                    "应用正在退出，已释放系统自动休眠阻止");
            }
        }
        catch (Exception ex)
        {
            var nativeException = ex as SystemSleepInhibitorException;
            logger.LogError(
                ex,
                "应用退出期间释放任务休眠阻止失败。平台={Platform}；原生操作={NativeOperation}；原生错误码={NativeErrorCode}。",
                sleepInhibitor.PlatformName,
                nativeException?.Operation ?? "unknown",
                nativeException?.NativeErrorCode);
            WriteActivity(
                LogEntryKind.Error,
                $"应用退出时释放系统自动休眠阻止失败：{ex.Message}");
        }
        finally
        {
            try
            {
                sleepInhibitor.Dispose();
            }
            catch (Exception ex)
            {
                var nativeException = ex as SystemSleepInhibitorException;
                logger.LogError(
                    ex,
                    "应用退出期间释放任务休眠阻止资源失败。平台={Platform}；原生操作={NativeOperation}；原生错误码={NativeErrorCode}。",
                    sleepInhibitor.PlatformName,
                    nativeException?.Operation ?? "unknown",
                    nativeException?.NativeErrorCode);
                WriteActivity(
                    LogEntryKind.Error,
                    $"应用退出时清理系统自动休眠阻止资源失败：{ex.Message}");
            }
        }
    }

    private void OnSleepInhibitorCleanupFailed(
        object? sender,
        SystemSleepInhibitorException exception)
    {
        logger.LogError(
            exception,
            "清理任务休眠阻止的原生资源失败。平台={Platform}；原生操作={NativeOperation}；原生错误码={NativeErrorCode}。",
            exception.PlatformName,
            exception.Operation,
            exception.NativeErrorCode);
        WriteActivity(
            LogEntryKind.Error,
            $"清理系统自动休眠阻止资源失败（{exception.Operation}，错误码 {exception.NativeErrorCode}）：{exception.Message}");
    }

    private void MarkReconciliationProcessed(long processedVersion)
        => Volatile.Write(ref _processedReconciliationVersion, processedVersion);

    private void WriteActivity(LogEntryKind kind, string message)
    {
        try
        {
            activityLogService.Write(kind, "Power", message);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "写入任务休眠阻止活动记录失败。");
        }
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

    private static string FormatEnabled(bool enabled) => enabled ? "开启" : "关闭";

    private sealed record ReconciliationResult(
        bool Succeeded,
        string Operation,
        IReadOnlyList<string> ActiveTaskNames,
        Exception? Exception)
    {
        public static ReconciliationResult Success() => new(true, string.Empty, [], null);

        public static ReconciliationResult Failure(
            string operation,
            IReadOnlyList<string> activeTaskNames,
            Exception exception) => new(false, operation, activeTaskNames, exception);
    }
}
