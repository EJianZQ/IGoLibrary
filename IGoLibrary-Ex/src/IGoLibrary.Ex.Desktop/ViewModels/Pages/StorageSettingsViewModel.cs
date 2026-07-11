using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Domain.Enums;

namespace IGoLibrary.Ex.Desktop.ViewModels;

public sealed partial class StorageSettingsViewModel(
    IStorageLocationService storageLocationService,
    IFolderPickerService folderPickerService,
    IStorageChangeWorkflowService changeWorkflowService,
    IActivityLogService activityLogService,
    INotificationService notificationService) : ViewModelBase
{
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

    public bool HasStorageLocationChanges =>
        !PathsEqual(CurrentDataDirectory, PendingDataDirectory) ||
        !PathsEqual(CurrentLogDirectory, PendingLogDirectory);

    public bool CanApplyStorageLocationChanges =>
        HasStorageLocationChanges && !IsStorageLocationOperationInProgress;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        CurrentDataDirectory = storageLocationService.Current.DataDirectory;
        CurrentLogDirectory = storageLocationService.Current.LogDirectory;
        PendingDataDirectory = CurrentDataDirectory;
        PendingLogDirectory = CurrentLogDirectory;

        var result = await storageLocationService.ConsumeStartupResultAsync(cancellationToken);
        if (result is null)
        {
            return;
        }

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

    private void NotifyLocationChangeState()
    {
        OnPropertyChanged(nameof(HasStorageLocationChanges));
        OnPropertyChanged(nameof(CanApplyStorageLocationChanges));
        ApplyStorageLocationChangesCommand.NotifyCanExecuteChanged();
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
