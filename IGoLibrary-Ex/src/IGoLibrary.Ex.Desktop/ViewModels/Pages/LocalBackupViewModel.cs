using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Domain.Enums;

namespace IGoLibrary.Ex.Desktop.ViewModels;

public sealed partial class LocalBackupViewModel(
    IBackupWorkflowService workflowService,
    IBackupSecretStore secretStore,
    IActivityLogService activityLogService,
    INotificationService notificationService) : ViewModelBase
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartOperation))]
    private bool isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BackupPasswordStatusText))]
    private bool hasBackupPassword;

    [ObservableProperty]
    private string statusText = string.Empty;

    public bool CanStartOperation => !IsBusy;

    public string BackupPasswordStatusText => HasBackupPassword ? "已配置" : "未配置";

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        HasBackupPassword = await secretStore.LoadBackupPasswordAsync(cancellationToken) is not null;
    }

    [RelayCommand(CanExecute = nameof(CanStartOperation))]
    private Task ExportAsync(CancellationToken cancellationToken)
        => RunAsync("正在导出全部数据…", workflowService.ExportLocalAsync, cancellationToken);

    [RelayCommand(CanExecute = nameof(CanStartOperation))]
    private Task ImportAsync(CancellationToken cancellationToken)
        => RunAsync("正在检查备份文件…", workflowService.ImportLocalAsync, cancellationToken);

    [RelayCommand(CanExecute = nameof(CanStartOperation))]
    private Task ChangePasswordAsync(CancellationToken cancellationToken)
        => RunAsync("正在更新备份密码…", workflowService.ChangeBackupPasswordAsync, cancellationToken);

    private async Task RunAsync(
        string busyText,
        Func<CancellationToken, Task<bool>> operation,
        CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        NotifyCommands();
        StatusText = busyText;
        try
        {
            var completed = await operation(cancellationToken);
            StatusText = completed ? "操作已完成" : "操作已取消";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusText = "操作已取消";
        }
        catch (Exception ex)
        {
            StatusText = $"操作失败：{ex.Message}";
            activityLogService.Write(LogEntryKind.Error, "Backup", StatusText);
            await notificationService.ShowWarningAsync("备份操作失败", ex.Message, CancellationToken.None);
        }
        finally
        {
            HasBackupPassword = await secretStore.LoadBackupPasswordAsync(CancellationToken.None) is not null;
            IsBusy = false;
            NotifyCommands();
        }
    }

    private void NotifyCommands()
    {
        ExportCommand.NotifyCanExecuteChanged();
        ImportCommand.NotifyCanExecuteChanged();
        ChangePasswordCommand.NotifyCanExecuteChanged();
    }
}
