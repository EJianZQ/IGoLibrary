using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Configuration;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Domain.Enums;

namespace IGoLibrary.Ex.Desktop.ViewModels;

public sealed partial class StorageSettingsViewModel(
    IStorageLocationService storageLocationService,
    IFolderPickerService folderPickerService,
    IStorageChangeWorkflowService changeWorkflowService,
    ILoggingSettingsWorkflowService loggingSettingsWorkflowService,
    IActivityLogService activityLogService,
    INotificationService notificationService,
    LocalBackupViewModel? localBackupViewModel = null,
    WebDavSyncViewModel? webDavSyncViewModel = null,
    IBackupRestoreStartupService? backupRestoreStartupService = null) : ViewModelBase
{
    private readonly object _loggingSettingsSaveGate = new();
    private LogFileSettings _lastPersistedLoggingSettings = LogFileSettings.Default;
    private LogFileSettings _pendingLoggingSettings = LogFileSettings.Default;
    private Task _loggingSettingsSaveTask = Task.CompletedTask;
    private long _pendingLoggingSettingsVersion;
    private long _processedLoggingSettingsVersion;
    private bool _loggingSettingsSaveLoopRunning;
    private bool _isLoadingLoggingSettings;

    public LocalBackupViewModel? LocalBackup { get; } = localBackupViewModel;

    public WebDavSyncViewModel? WebDavSync { get; } = webDavSyncViewModel;

    [ObservableProperty]
    private string currentDataDirectory = storageLocationService.Current.DataDirectory;

    [ObservableProperty]
    private string currentLogDirectory = storageLocationService.Current.LogDirectory;

    [ObservableProperty]
    private string pendingDataDirectory = storageLocationService.Current.DataDirectory;

    [ObservableProperty]
    private string pendingLogDirectory = storageLocationService.Current.LogDirectory;

    [ObservableProperty]
    private bool isStorageLocationOperationInProgress;

    [ObservableProperty]
    private bool isFileLoggingEnabled = LogFileSettings.Default.Enabled;

    [ObservableProperty]
    private int retainedLogFileCount = LogFileSettings.Default.RetainedFileCount;

    [ObservableProperty]
    private bool isLoggingSettingsSaveInProgress;

    public bool HasStorageLocationChanges =>
        !PathsEqual(CurrentDataDirectory, PendingDataDirectory) ||
        !PathsEqual(CurrentLogDirectory, PendingLogDirectory);

    public bool CanApplyStorageLocationChanges =>
        HasStorageLocationChanges && !IsStorageLocationOperationInProgress;

    public async Task InitializeAsync(
        LogFileSettings loggingSettings,
        BackupSyncSettings? backupSyncSettings = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedLoggingSettings = LogFileSettings.Normalize(loggingSettings);
        _isLoadingLoggingSettings = true;
        try
        {
            IsFileLoggingEnabled = normalizedLoggingSettings.Enabled;
            RetainedLogFileCount = normalizedLoggingSettings.RetainedFileCount;
            _lastPersistedLoggingSettings = normalizedLoggingSettings;
            _pendingLoggingSettings = normalizedLoggingSettings;
        }
        finally
        {
            _isLoadingLoggingSettings = false;
        }

        CurrentDataDirectory = storageLocationService.Current.DataDirectory;
        CurrentLogDirectory = storageLocationService.Current.LogDirectory;
        PendingDataDirectory = CurrentDataDirectory;
        PendingLogDirectory = CurrentLogDirectory;

        if (LocalBackup is not null)
        {
            await LocalBackup.InitializeAsync(cancellationToken);
        }

        if (WebDavSync is not null)
        {
            await WebDavSync.InitializeAsync(
                backupSyncSettings ?? BackupSyncSettings.Default,
                cancellationToken);
        }

        var result = await storageLocationService.ConsumeStartupResultAsync(cancellationToken);
        if (result is not null)
        {
            activityLogService.Write(
                result.Succeeded ? LogEntryKind.Success : LogEntryKind.Error,
                "Storage",
                result.Message);
            try
            {
                if (result.Succeeded)
                {
                    await notificationService.ShowSuccessAsync("存储位置已更新", result.Message, cancellationToken);
                }
                else
                {
                    await notificationService.ShowWarningAsync("存储位置更改失败", result.Message, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                activityLogService.Write(LogEntryKind.Warning, "Storage", $"显示存储位置启动结果失败：{ex.Message}");
            }
        }

        if (backupRestoreStartupService is not null)
        {
            var restoreResult = await backupRestoreStartupService.ConsumeStartupResultAsync(cancellationToken);
            if (restoreResult is not null)
            {
                activityLogService.Write(
                    restoreResult.Succeeded ? LogEntryKind.Success : LogEntryKind.Error,
                    "Backup",
                    restoreResult.Message);
                if (restoreResult.Succeeded)
                {
                    await notificationService.ShowSuccessAsync("数据恢复完成", restoreResult.Message, cancellationToken);
                }
                else
                {
                    await notificationService.ShowWarningAsync("数据恢复失败", restoreResult.Message, cancellationToken);
                }
            }
        }
    }

    [RelayCommand]
    private async Task SelectDataDirectoryAsync(CancellationToken cancellationToken)
    {
        var selected = await folderPickerService.PickFolderAsync("选择数据存储位置", cancellationToken);
        if (!string.IsNullOrWhiteSpace(selected))
        {
            PendingDataDirectory = Path.GetFullPath(selected);
        }
    }

    [RelayCommand]
    private async Task SelectLogDirectoryAsync(CancellationToken cancellationToken)
    {
        var selected = await folderPickerService.PickFolderAsync("选择日志文件存储位置", cancellationToken);
        if (!string.IsNullOrWhiteSpace(selected))
        {
            PendingLogDirectory = Path.GetFullPath(selected);
        }
    }

    [RelayCommand]
    private void RestoreDefaultDataDirectory()
    {
        PendingDataDirectory = storageLocationService.Defaults.DataDirectory;
    }

    [RelayCommand]
    private void RestoreDefaultLogDirectory()
    {
        PendingLogDirectory = storageLocationService.Defaults.LogDirectory;
    }

    [RelayCommand(CanExecute = nameof(CanApplyStorageLocationChanges))]
    private async Task ApplyStorageLocationChangesAsync(CancellationToken cancellationToken)
    {
        IsStorageLocationOperationInProgress = true;
        try
        {
            await changeWorkflowService.ApplyAsync(
                new StorageLocations(PendingDataDirectory, PendingLogDirectory),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            activityLogService.Write(LogEntryKind.Error, "Storage", $"应用存储位置失败：{ex.Message}");
            await notificationService.ShowWarningAsync(
                "无法更改存储位置",
                ex.Message,
                CancellationToken.None);
        }
        finally
        {
            IsStorageLocationOperationInProgress = false;
        }
    }

    partial void OnPendingDataDirectoryChanged(string value) => NotifyLocationChangeState();

    partial void OnPendingLogDirectoryChanged(string value) => NotifyLocationChangeState();

    partial void OnIsStorageLocationOperationInProgressChanged(bool value) => NotifyLocationChangeState();

    partial void OnIsFileLoggingEnabledChanged(bool value) => QueueLoggingSettingsSave();

    partial void OnRetainedLogFileCountChanged(int value)
    {
        var normalized = Math.Clamp(
            value,
            LogFileSettings.MinRetainedFileCount,
            LogFileSettings.MaxRetainedFileCount);
        if (normalized != value)
        {
            RetainedLogFileCount = normalized;
            return;
        }

        QueueLoggingSettingsSave();
    }

    public Task FlushPendingLoggingSettingsSaveAsync()
    {
        lock (_loggingSettingsSaveGate)
        {
            return _loggingSettingsSaveTask;
        }
    }

    private void NotifyLocationChangeState()
    {
        OnPropertyChanged(nameof(HasStorageLocationChanges));
        OnPropertyChanged(nameof(CanApplyStorageLocationChanges));
        ApplyStorageLocationChangesCommand.NotifyCanExecuteChanged();
    }

    private void QueueLoggingSettingsSave()
    {
        if (_isLoadingLoggingSettings)
        {
            return;
        }

        var shouldStartLoop = false;
        lock (_loggingSettingsSaveGate)
        {
            _pendingLoggingSettings = LogFileSettings.Normalize(new LogFileSettings(
                IsFileLoggingEnabled,
                RetainedLogFileCount));
            _pendingLoggingSettingsVersion++;
            if (!_loggingSettingsSaveLoopRunning)
            {
                _loggingSettingsSaveLoopRunning = true;
                shouldStartLoop = true;
            }
        }

        if (!shouldStartLoop)
        {
            return;
        }

        var saveTask = PersistLoggingSettingsLoopAsync();
        lock (_loggingSettingsSaveGate)
        {
            _loggingSettingsSaveTask = saveTask;
        }
    }

    private async Task PersistLoggingSettingsLoopAsync()
    {
        IsLoggingSettingsSaveInProgress = true;
        try
        {
            while (true)
            {
                LogFileSettings pending;
                long version;
                lock (_loggingSettingsSaveGate)
                {
                    if (_processedLoggingSettingsVersion == _pendingLoggingSettingsVersion)
                    {
                        _loggingSettingsSaveLoopRunning = false;
                        return;
                    }

                    pending = _pendingLoggingSettings;
                    version = _pendingLoggingSettingsVersion;
                }

                try
                {
                    var result = await loggingSettingsWorkflowService.SaveAsync(pending);
                    bool hasNewerValue;
                    lock (_loggingSettingsSaveGate)
                    {
                        _lastPersistedLoggingSettings = result.Settings;
                        _processedLoggingSettingsVersion = version;
                        hasNewerValue = _pendingLoggingSettingsVersion != version;
                    }

                    if (!hasNewerValue)
                    {
                        ApplyNormalizedLoggingSettings(result.Settings);
                    }
                    if (result.RuntimeResult.TotalDeleteFailureCount > 0)
                    {
                        var message =
                            $"设置已保存，但有 {result.RuntimeResult.TotalDeleteFailureCount} 个日志文件暂时无法清理，将在后续重试。";
                        activityLogService.Write(LogEntryKind.Warning, "Logging", message);
                        await TryShowLoggingWarningAsync("部分日志暂未清理", message);
                    }
                }
                catch (Exception ex)
                {
                    LogFileSettings persisted;
                    bool hasNewerValue;
                    lock (_loggingSettingsSaveGate)
                    {
                        _processedLoggingSettingsVersion = version;
                        persisted = _lastPersistedLoggingSettings;
                        hasNewerValue = _pendingLoggingSettingsVersion != version;
                    }

                    if (!hasNewerValue)
                    {
                        ApplyNormalizedLoggingSettings(persisted);
                    }

                    activityLogService.Write(LogEntryKind.Warning, "Settings", $"保存日志设置失败：{ex.Message}");
                    await TryShowLoggingWarningAsync("无法保存日志设置", ex.Message);
                }
            }
        }
        finally
        {
            IsLoggingSettingsSaveInProgress = false;
        }
    }

    private void ApplyNormalizedLoggingSettings(LogFileSettings settings)
    {
        _isLoadingLoggingSettings = true;
        try
        {
            IsFileLoggingEnabled = settings.Enabled;
            RetainedLogFileCount = settings.RetainedFileCount;
        }
        finally
        {
            _isLoadingLoggingSettings = false;
        }
    }

    private async Task TryShowLoggingWarningAsync(string title, string message)
    {
        try
        {
            await notificationService.ShowWarningAsync(title, message, CancellationToken.None);
        }
        catch (Exception ex)
        {
            activityLogService.Write(LogEntryKind.Warning, "Settings", $"显示日志设置提示失败：{ex.Message}");
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        try
        {
            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }
}
