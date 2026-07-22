using IGoLibrary.Ex.Application.Backup;

namespace IGoLibrary.Ex.Desktop.Services;

public sealed class BackupDialogService(AppWindowService appWindowService) : IBackupDialogService
{
    public async Task<string?> RequestPasswordAsync(
        string title,
        string message,
        bool requireConfirmation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var owner = appWindowService.MainWindow;
        if (owner is null)
        {
            return null;
        }

        return await new BackupPasswordWindow(title, message, requireConfirmation)
            .ShowDialog<string?>(owner);
    }

    public async Task<bool> ConfirmRestoreAsync(
        PreparedBackup backup,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var owner = appWindowService.MainWindow;
        return owner is not null && await new BackupComparisonWindow(backup).ShowDialog<bool>(owner);
    }

    public async Task<bool> ConfirmInsecureHttpAsync(CancellationToken cancellationToken = default)
    {
        var choice = await ShowChoiceAsync(
            "确认使用 HTTP WebDAV",
            "HTTP 会以明文传输 WebDAV 账号和加密备份文件，网络中的其他人可能截获账号。应用仍会加密备份正文，但这不能保护 WebDAV 登录凭据。",
            "了解风险并启用",
            secondaryText: null,
            cancellationToken);
        return choice == StorageDialogChoice.Primary;
    }

    public async Task<bool> ConfirmSkipTlsVerificationAsync(
        CancellationToken cancellationToken = default)
    {
        var choice = await ShowChoiceAsync(
            "确认跳过 TLS 证书校验",
            "Skip 会跳过证书链、有效期和主机名校验，攻击者可能冒充 WebDAV 服务器并截获登录凭据。仅应在你完全信任当前网络和服务器时使用。",
            "了解风险并跳过",
            secondaryText: null,
            cancellationToken);
        return choice == StorageDialogChoice.Primary;
    }

    public async Task<bool> ConfirmRemoteOverwriteAsync(
        WebDavRemoteMetadata? metadata,
        CancellationToken cancellationToken = default)
    {
        var remote = metadata is null
            ? "远端版本信息不可用"
            : $"ETag：{metadata.ETag ?? "无"}\n修改时间：{metadata.LastModified?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "未知"}\n大小：{FormatBytes(metadata.ContentLength)}";
        var choice = await ShowChoiceAsync(
            "远端备份存在冲突",
            $"远端文件不是本设备上次成功上传的版本。继续会用当前本地全部数据覆盖远端单一备份文件。\n\n{remote}",
            "确认覆盖远端",
            "取消",
            cancellationToken);
        return choice == StorageDialogChoice.Primary;
    }

    public async Task<BackupPasswordChangeDecision> ConfirmPasswordChangeAsync(
        bool webDavConfigured,
        CancellationToken cancellationToken = default)
    {
        var message = webDavConfigured
            ? "更改密码不会修改已有的本地备份。WebDAV 已配置；继续后会立即用新密码重新加密并覆盖远端备份。若不希望现在覆盖，请取消本次改密。"
            : "更改密码不会修改已有备份；以后导出和上传会使用新密码。请妥善保存旧密码。";
        var choice = await ShowChoiceAsync(
            "更改备份密码",
            message,
            webDavConfigured ? "保存并立即上传" : "保存新密码",
            secondaryText: null,
            cancellationToken);
        return choice switch
        {
            StorageDialogChoice.Primary when webDavConfigured => BackupPasswordChangeDecision.SaveAndUpload,
            StorageDialogChoice.Primary => BackupPasswordChangeDecision.SaveOnly,
            _ => BackupPasswordChangeDecision.Cancel
        };
    }

    private async Task<StorageDialogChoice> ShowChoiceAsync(
        string title,
        string message,
        string primaryText,
        string? secondaryText,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var owner = appWindowService.MainWindow;
        return owner is null
            ? StorageDialogChoice.Cancel
            : await new StorageChoiceWindow(title, message, primaryText, secondaryText)
                .ShowDialog<StorageDialogChoice>(owner);
    }

    private static string FormatBytes(long? value)
    {
        if (value is null)
        {
            return "未知";
        }

        var bytes = value.Value;
        return bytes >= 1024 * 1024
            ? $"{bytes / 1024d / 1024d:F2} MiB"
            : bytes >= 1024 ? $"{bytes / 1024d:F1} KiB" : $"{bytes} B";
    }
}
