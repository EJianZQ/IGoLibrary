namespace IGoLibrary.Ex.Desktop.Services;

public sealed class StorageChangeDialogService(AppWindowService appWindowService) : IStorageChangeDialogService
{
    public async Task<StorageMigrationDecision> ConfirmMigrationAsync(
        StorageLocations current,
        StorageLocations target,
        bool dataDirectoryChanged,
        bool logDirectoryChanged,
        CancellationToken cancellationToken = default)
    {
        var changes = new List<string>();
        if (dataDirectoryChanged)
        {
            changes.Add($"数据：\n{current.DataDirectory}\n→ {target.DataDirectory}");
        }

        if (logDirectoryChanged)
        {
            changes.Add($"日志：\n{current.LogDirectory}\n→ {target.LogDirectory}");
        }

        var choice = await ShowAsync(
            "应用存储位置更改",
            string.Join("\n\n", changes) + "\n\n是否将现有文件迁移到新位置？应用将在确认后立即重启。",
            "迁移并重启",
            "不迁移并重启",
            cancellationToken);
        return choice switch
        {
            StorageDialogChoice.Primary => StorageMigrationDecision.Migrate,
            StorageDialogChoice.Secondary => StorageMigrationDecision.DoNotMigrate,
            _ => StorageMigrationDecision.Cancel
        };
    }

    public async Task<bool> ConfirmOverwriteDatabaseAsync(
        string databasePath,
        CancellationToken cancellationToken = default)
    {
        var choice = await ShowAsync(
            "目标位置已有数据",
            $"目标目录已经存在数据库：\n{databasePath}\n\n继续后将使用当前数据库覆盖目标数据库。迁移失败时会自动恢复目标原文件。",
            "覆盖并继续",
            secondaryText: null,
            cancellationToken);
        return choice == StorageDialogChoice.Primary;
    }

    public async Task<bool> ConfirmUseExistingDatabaseAsync(
        string databasePath,
        CancellationToken cancellationToken = default)
    {
        var choice = await ShowAsync(
            "目标位置已有数据",
            $"目标目录已经存在有效数据库：\n{databasePath}\n\n选择继续后，应用将直接使用其中的设置、收藏和接口配置；当前目录中的数据不会迁移过去。",
            "使用现有数据并重启",
            secondaryText: null,
            cancellationToken);
        return choice == StorageDialogChoice.Primary;
    }

    public async Task<bool> ConfirmStopTasksAsync(
        IReadOnlyList<string> taskNames,
        CancellationToken cancellationToken = default)
    {
        var choice = await ShowAsync(
            "正在运行任务",
            $"重启会停止以下任务：\n{string.Join("、", taskNames)}\n\n停止任务后可能错过预约机会，是否继续？",
            "停止任务并继续",
            secondaryText: null,
            cancellationToken);
        return choice == StorageDialogChoice.Primary;
    }

    private async Task<StorageDialogChoice> ShowAsync(
        string title,
        string message,
        string primaryText,
        string? secondaryText,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var owner = appWindowService.MainWindow;
        if (owner is null)
        {
            return StorageDialogChoice.Cancel;
        }

        var dialog = new StorageChoiceWindow(title, message, primaryText, secondaryText);
        return await dialog.ShowDialog<StorageDialogChoice>(owner);
    }
}
