using System.Diagnostics;
using IGoLibrary.Ex.Application.Abstractions;

namespace IGoLibrary.Ex.Infrastructure.Persistence;

public sealed class StorageLocationManager : IStorageLocationService
{
    private static readonly TimeSpan ParentExitTimeout = TimeSpan.FromSeconds(60);

    private readonly StorageLocatorStore _locatorStore;
    private readonly StorageLocations _recoveryLocations;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private StorageLocationStartupResult? _volatileStartupResult;

    public StorageLocationManager()
        : this(
            StorageLocationDefaults.GetLocatorFilePath(),
            StorageLocationDefaults.GetDefaults(),
            StorageLocationDefaults.GetPlatformDefaults())
    {
    }

    internal StorageLocationManager(
        string locatorFilePath,
        StorageLocations defaults,
        StorageLocations? recoveryLocations = null)
    {
        Defaults = StoragePathRules.Normalize(defaults);
        _recoveryLocations = StoragePathRules.Normalize(recoveryLocations ?? defaults);
        _locatorStore = new StorageLocatorStore(locatorFilePath, Defaults);
        Current = Defaults;
    }

    public StorageLocations Current { get; private set; }

    public StorageLocations Defaults { get; }

    public async Task<StorageLocations> InitializeAsync(
        int? restartParentProcessId = null,
        CancellationToken cancellationToken = default)
    {
        if (restartParentProcessId is > 0 && restartParentProcessId.Value != Environment.ProcessId)
        {
            await WaitForParentExitAsync(restartParentProcessId, cancellationToken);
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var document = _locatorStore.Load();
            StorageLocations active;
            var activeConfigurationChanged = false;
            try
            {
                active = StoragePathRules.Normalize(document.Active ?? Defaults);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                active = Defaults;
                document.Active = Defaults;
                document.Pending = null;
                document.LastResult = new StorageLocationStartupResult(
                    false,
                    $"存储位置配置包含无效路径，已恢复默认目录：{ex.Message}");
                activeConfigurationChanged = true;
            }

            if (document.Pending is null)
            {
                return ActivateWithoutPendingChange(
                    document,
                    active,
                    activeConfigurationChanged,
                    retryPendingCleanup: !activeConfigurationChanged);
            }

            PendingStorageLocationChange? pending = null;
            StorageMigrationTransaction? transaction = null;
            var locatorCommitted = false;
            try
            {
                pending = document.Pending with
                {
                    Source = StoragePathRules.Normalize(document.Pending.Source),
                    Target = StoragePathRules.Normalize(document.Pending.Target)
                };
                StoragePathRules.ValidateWritable(pending.Target);
                StorageLocatorStore.RetryPendingCleanup(
                    document,
                    GetProtectedDirectories(active, pending.Target));

                var dataChanged = !StoragePathRules.DirectoriesReferToSameLocation(
                    pending.Source.DataDirectory,
                    pending.Target.DataDirectory);
                if (dataChanged && !pending.MigrateData)
                {
                    var targetDatabase = Path.Combine(
                        pending.Target.DataDirectory,
                        StorageLocationDefaults.DatabaseFileName);
                    var inspection = StorageDatabaseValidator.Inspect(targetDatabase);
                    if (inspection.Exists && !inspection.IsValid)
                    {
                        throw new InvalidDataException(
                            $"目标目录中的现有数据库无效：{inspection.FailureMessage ?? "未知错误"}");
                    }
                }

                transaction = new StorageMigrationTransaction(pending);
                transaction.Execute();

                document.Active = pending.Target;
                document.Pending = null;
                document.LastResult = new StorageLocationStartupResult(
                    true,
                    pending.MigrateData || pending.MigrateLogs
                        ? "存储位置已更新，文件迁移完成。"
                        : "存储位置已更新，旧目录文件保持不变。");
                _locatorStore.Save(document);
                locatorCommitted = true;

                Current = pending.Target;
                transaction.AcceptCommit();
                var cleanupFailures = transaction.CleanupSourceFiles();
                if (cleanupFailures.Count > 0)
                {
                    MergePendingCleanup(document, cleanupFailures);
                    document.LastResult = new StorageLocationStartupResult(
                        true,
                        $"存储位置已更新，但有 {cleanupFailures.Count} 个旧文件暂未删除，将在下次启动重试。");
                    _locatorStore.Save(document);
                }

                StoragePathRules.EnsureDirectories(Current);
                return Current;
            }
            catch (Exception ex)
            {
                if (locatorCommitted && pending is not null)
                {
                    Current = pending.Target;
                    StoragePathRules.EnsureDirectories(Current);
                    return Current;
                }

                transaction?.Rollback();
                document.Active = active;
                document.Pending = null;
                document.LastResult = new StorageLocationStartupResult(
                    false,
                    $"存储位置迁移失败，已继续使用原目录：{ex.Message}");
                if (TryPrepareLocations(active, out var activeFailure))
                {
                    _locatorStore.Save(document);
                    Current = active;
                    return Current;
                }

                return ActivateRecoveryLocations(document, active, activeFailure!);
            }
            finally
            {
                transaction?.Dispose();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task ValidateAsync(StorageLocations locations, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StoragePathRules.ValidateWritable(StoragePathRules.Normalize(locations));
        return Task.CompletedTask;
    }

    public Task<StorageTargetDatabaseInspection> InspectTargetDatabaseAsync(
        string dataDirectory,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = StoragePathRules.NormalizeDirectory(dataDirectory, nameof(dataDirectory));
        return Task.FromResult(StorageDatabaseValidator.Inspect(
            Path.Combine(normalized, StorageLocationDefaults.DatabaseFileName)));
    }

    public async Task StageChangeAsync(
        StorageLocationChangeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var target = StoragePathRules.Normalize(request.Target);
        StoragePathRules.ValidateWritable(target);
        var dataChanged = !StoragePathRules.DirectoriesReferToSameLocation(
            Current.DataDirectory,
            target.DataDirectory);
        var logsChanged = !StoragePathRules.DirectoriesReferToSameLocation(
            Current.LogDirectory,
            target.LogDirectory);
        if (!dataChanged && !logsChanged)
        {
            throw new InvalidOperationException("存储位置没有变化。");
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var document = _locatorStore.Load();
            document.Active = Current;
            document.Pending = new PendingStorageLocationChange(
                Current,
                target,
                request.MigrateData && dataChanged,
                request.MigrateLogs && logsChanged,
                request.OverwriteTargetDatabase,
                DateTimeOffset.UtcNow);
            document.LastResult = null;
            _locatorStore.Save(document);
        }
        finally
        {
            _gate.Release();
        }
    }

    private StorageLocations ActivateWithoutPendingChange(
        StorageLocatorDocument document,
        StorageLocations active,
        bool stateChanged,
        bool retryPendingCleanup)
    {
        if (!TryPrepareLocations(active, out var activeFailure))
        {
            return ActivateRecoveryLocations(document, active, activeFailure!);
        }

        Current = active;
        var cleanupStateChanged = retryPendingCleanup && StorageLocatorStore.RetryPendingCleanup(
            document,
            GetProtectedDirectories(active));
        _locatorStore.SaveIfNeeded(document, stateChanged || cleanupStateChanged);
        return Current;
    }

    private StorageLocations ActivateRecoveryLocations(
        StorageLocatorDocument document,
        StorageLocations unavailableLocations,
        Exception activeFailure)
    {
        if (!TryPrepareLocations(_recoveryLocations, out var recoveryFailure))
        {
            throw new AggregateException(
                "已配置的存储目录和平台恢复目录均不可用。",
                activeFailure,
                recoveryFailure!);
        }

        Current = _recoveryLocations;
        var result = new StorageLocationStartupResult(
            false,
            $"已配置的存储目录当前不可用，本次运行临时使用平台默认目录；原配置保持不变，下次启动会重新尝试。原因：{activeFailure.Message}");
        document.Active = unavailableLocations;
        document.Pending = null;
        document.LastResult = result;
        try
        {
            _locatorStore.Save(document);
        }
        catch
        {
            _volatileStartupResult = result;
        }

        return Current;
    }

    private static bool TryPrepareLocations(StorageLocations locations, out Exception? failure)
    {
        try
        {
            StoragePathRules.EnsureDirectories(locations);
            StoragePathRules.ValidateWritable(locations);
            failure = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or
                                       System.Security.SecurityException)
        {
            failure = ex;
            return false;
        }
    }

    private static string[] GetProtectedDirectories(params StorageLocations[] locations)
    {
        return locations
            .SelectMany(item => new[] { item.DataDirectory, item.LogDirectory })
            .ToArray();
    }

    private static void MergePendingCleanup(
        StorageLocatorDocument document,
        IEnumerable<PendingStorageCleanup> cleanupFailures)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var knownPaths = new HashSet<string>(comparer);
        foreach (var cleanup in document.PendingCleanup)
        {
            if (cleanup.TryGetFullPath(out var path))
            {
                knownPaths.Add(path);
            }
        }

        foreach (var cleanup in cleanupFailures)
        {
            if (cleanup.TryGetFullPath(out var path) && knownPaths.Add(path))
            {
                document.PendingCleanup.Add(cleanup);
            }
        }
    }

    public async Task CancelPendingChangeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var document = _locatorStore.Load();
            if (document.Pending is null)
            {
                return;
            }

            document.Pending = null;
            _locatorStore.Save(document);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<StorageLocationStartupResult?> ConsumeStartupResultAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_volatileStartupResult is not null)
            {
                var volatileResult = _volatileStartupResult;
                _volatileStartupResult = null;
                return volatileResult;
            }

            var document = _locatorStore.Load();
            var result = document.LastResult;
            if (result is null)
            {
                return null;
            }

            document.LastResult = null;
            _locatorStore.Save(document);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task WaitForParentExitAsync(int? processId, CancellationToken cancellationToken)
    {
        if (processId is not > 0 || processId == Environment.ProcessId)
        {
            return;
        }

        Process process;
        try
        {
            process = Process.GetProcessById(processId.Value);
        }
        catch (ArgumentException)
        {
            return;
        }

        using (process)
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            timeout.CancelAfter(ParentExitTimeout);
            await process.WaitForExitAsync(timeout.Token);
        }
    }
}
