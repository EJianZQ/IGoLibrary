using IGoLibrary.Ex.Application.Configuration;
using IGoLibrary.Ex.Domain.Enums;

namespace IGoLibrary.Ex.Desktop.ViewModels;

public sealed partial class StorageSettingsViewModel
{
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
                RetainedLogFileCount) { RecordNetworkRequests = RecordNetworkRequests });
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
                    if (result.RuntimeResult.ApplicationFailure is { } failure)
                    {
                        activityLogService.Write(LogEntryKind.Warning, "Logging", failure);
                        await TryShowLoggingWarningAsync("日志设置已保存，但未能应用", failure);
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

                    activityLogService.Write(LogEntryKind.Warning, "Settings", $"保存日志设置失败：{ex.Message}", ex);
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
            RecordNetworkRequests = settings.RecordNetworkRequests;
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
            activityLogService.Write(LogEntryKind.Warning, "Settings", $"显示日志设置提示失败：{ex.Message}", ex);
        }
    }

}
