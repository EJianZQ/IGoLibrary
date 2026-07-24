using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Backup;
using IGoLibrary.Ex.Application.Configuration;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Domain.Enums;

namespace IGoLibrary.Ex.Desktop.ViewModels;

public sealed partial class WebDavSyncViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly IBackupSecretStore _secretStore;
    private readonly IWebDavSyncService _syncService;
    private readonly IBackupWorkflowService _workflowService;
    private readonly IBackupDialogService _dialogService;
    private readonly IActivityLogService _activityLogService;
    private readonly INotificationService _notificationService;
    private bool _isLoading;
    private WebDavTlsVerifyMode _savedTlsVerifyMode = WebDavTlsVerifyMode.Verify;

    public WebDavSyncViewModel(
        ISettingsService settingsService,
        IBackupSecretStore secretStore,
        IWebDavSyncService syncService,
        IBackupWorkflowService workflowService,
        IBackupDialogService dialogService,
        IActivityLogService activityLogService,
        INotificationService notificationService)
    {
        _settingsService = settingsService;
        _secretStore = secretStore;
        _syncService = syncService;
        _workflowService = workflowService;
        _dialogService = dialogService;
        _activityLogService = activityLogService;
        _notificationService = notificationService;
        _syncService.StatusChanged += OnStatusChanged;
    }

    public string[] TlsVerifyModes { get; } = ["Verify（推荐）", "Skip（不安全）"];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTlsVerifyApplicable))]
    [NotifyPropertyChangedFor(nameof(ShowsTlsSkipWarning))]
    private string endpoint = string.Empty;

    [ObservableProperty]
    private string remoteDirectory = BackupSyncSettings.DefaultRemoteDirectory;

    [ObservableProperty]
    private string username = string.Empty;

    [ObservableProperty]
    private string password = string.Empty;

    [ObservableProperty]
    private bool hasStoredPassword;

    [ObservableProperty]
    private bool allowInsecureHttp;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsTlsSkipWarning))]
    private int selectedTlsVerifyModeIndex;

    [ObservableProperty]
    private bool autoUploadEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartOperation))]
    private bool isBusy;

    [ObservableProperty]
    private bool hasConflict;

    [ObservableProperty]
    private string statusText = BackupSyncRuntimeStatus.Idle.Message;

    [ObservableProperty]
    private string lastSuccessfulSyncText = "从未";

    [ObservableProperty]
    private string remoteModifiedText = "未知";

    public bool CanStartOperation => !IsBusy;

    public bool SupportsAutomaticUpload => _secretStore.IsPersistent;

    public bool ShowsInsecureHttpWarning => AllowInsecureHttp;

    public bool IsTlsVerifyApplicable
        => !(Uri.TryCreate(Endpoint.Trim(), UriKind.Absolute, out var uri) &&
             uri.Scheme == Uri.UriSchemeHttp);

    public bool ShowsTlsSkipWarning
        => IsTlsVerifyApplicable && GetSelectedTlsVerifyMode() == WebDavTlsVerifyMode.Skip;

    public async Task InitializeAsync(
        BackupSyncSettings settings,
        CancellationToken cancellationToken = default)
    {
        var normalized = BackupSyncSettings.Normalize(settings);
        _isLoading = true;
        try
        {
            Endpoint = normalized.Endpoint;
            RemoteDirectory = normalized.RemoteDirectory;
            Username = normalized.Username;
            AllowInsecureHttp = normalized.AllowInsecureHttp;
            SelectedTlsVerifyModeIndex = (int)normalized.TlsVerifyMode;
            _savedTlsVerifyMode = normalized.TlsVerifyMode;
            AutoUploadEnabled = normalized.AutoUploadEnabled;
            Password = string.Empty;
            HasStoredPassword = await _secretStore.LoadWebDavPasswordAsync(cancellationToken) is not null;
            ApplyStatus(_syncService.Status);
        }
        finally
        {
            _isLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartOperation))]
    private Task SaveAsync(CancellationToken cancellationToken)
        => RunAsync("正在保存 WebDAV 设置…", SaveCoreAsync, cancellationToken);

    [RelayCommand(CanExecute = nameof(CanStartOperation))]
    private Task TestConnectionAsync(CancellationToken cancellationToken)
        => RunAsync("正在测试 WebDAV 连接…", async token =>
        {
            await SaveCoreAsync(token);
            await _syncService.TestConnectionAsync(token);
            await _notificationService.ShowSuccessAsync("WebDAV 连接正常", "已验证目录读取、创建、写入和删除权限。", token);
        }, cancellationToken);

    [RelayCommand(CanExecute = nameof(CanStartOperation))]
    private Task UploadAsync(CancellationToken cancellationToken)
        => RunAsync("正在上传全部数据…", async token =>
        {
            await SaveCoreAsync(token);
            await _workflowService.UploadAsync(token);
        }, cancellationToken);

    [RelayCommand(CanExecute = nameof(CanStartOperation))]
    private Task DownloadAndRestoreAsync(CancellationToken cancellationToken)
        => RunAsync("正在下载远端备份…", async token =>
        {
            await SaveCoreAsync(token);
            await _workflowService.DownloadAndRestoreAsync(token);
        }, cancellationToken);

    partial void OnAllowInsecureHttpChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowsInsecureHttpWarning));
    }

    partial void OnAutoUploadEnabledChanged(bool value)
    {
        if (!_isLoading && value && !SupportsAutomaticUpload)
        {
            AutoUploadEnabled = false;
            _ = _notificationService.ShowWarningAsync(
                "当前平台无法自动上传",
                "未检测到可持久保存密码的系统安全凭据服务；仍可逐次执行手动操作。");
        }
    }

    internal async Task SaveCoreAsync(CancellationToken cancellationToken)
    {
        var allowHttp = AllowInsecureHttp;
        if (Uri.TryCreate(Endpoint.Trim(), UriKind.Absolute, out var rawUri) &&
            rawUri.Scheme == Uri.UriSchemeHttp)
        {
            if (!allowHttp && !await _dialogService.ConfirmInsecureHttpAsync(cancellationToken))
            {
                throw new OperationCanceledException("用户未授权 HTTP WebDAV", cancellationToken);
            }

            allowHttp = true;
        }
        else
        {
            allowHttp = false;
        }

        if (!BackupSyncSettings.TryValidateEndpoint(
                Endpoint,
                allowHttp,
                out var endpointUri,
                out var endpointError))
        {
            throw new ArgumentException(endpointError);
        }

        if (!BackupSyncSettings.TryValidateRemoteDirectory(
                RemoteDirectory,
                out var remoteDirectory,
                out var directoryError))
        {
            throw new ArgumentException(directoryError);
        }

        var tlsVerifyMode = endpointUri!.Scheme == Uri.UriSchemeHttps
            ? GetSelectedTlsVerifyMode()
            : WebDavTlsVerifyMode.Verify;
        if (tlsVerifyMode == WebDavTlsVerifyMode.Skip &&
            _savedTlsVerifyMode != WebDavTlsVerifyMode.Skip &&
            !await _dialogService.ConfirmSkipTlsVerificationAsync(cancellationToken))
        {
            throw new OperationCanceledException("用户未授权跳过 WebDAV TLS 证书校验", cancellationToken);
        }

        var normalizedUsername = Username.Trim();
        var storedPassword = await _secretStore.LoadWebDavPasswordAsync(cancellationToken);
        if (string.IsNullOrEmpty(storedPassword))
        {
            storedPassword = null;
        }

        var effectivePassword = string.IsNullOrEmpty(normalizedUsername)
            ? null
            : string.IsNullOrEmpty(Password)
                ? storedPassword
                : Password;
        if (string.IsNullOrEmpty(normalizedUsername) != string.IsNullOrEmpty(effectivePassword))
        {
            throw new ArgumentException("WebDAV 用户名和密码必须同时填写，或同时留空使用匿名访问");
        }

        if (AutoUploadEnabled &&
            await _secretStore.LoadBackupPasswordAsync(cancellationToken) is null)
        {
            throw new InvalidOperationException("启用自动上传前，请先设置备份加密密码");
        }

        var passwordChanged = !string.Equals(
            storedPassword,
            effectivePassword,
            StringComparison.Ordinal);
        if (passwordChanged)
        {
            await SetWebDavPasswordAsync(effectivePassword, cancellationToken);
        }

        AppSettings saved;
        try
        {
            saved = await _settingsService.UpdateAsync(current => current with
            {
                BackupSync = new BackupSyncSettings(
                    endpointUri.AbsoluteUri,
                    remoteDirectory,
                    normalizedUsername,
                    tlsVerifyMode,
                    allowHttp,
                    AutoUploadEnabled && SupportsAutomaticUpload)
            }, cancellationToken);
        }
        catch (Exception settingsException) when (passwordChanged)
        {
            try
            {
                await SetWebDavPasswordAsync(storedPassword, CancellationToken.None);
            }
            catch (Exception rollbackException)
            {
                throw new AggregateException(
                    "WebDAV 设置保存失败，并且无法恢复原有 WebDAV 密码；请重新检查并保存同步设置",
                    settingsException,
                    rollbackException);
            }

            throw;
        }

        _isLoading = true;
        try
        {
            var settings = BackupSyncSettings.Normalize(saved.BackupSync);
            Endpoint = settings.Endpoint;
            RemoteDirectory = settings.RemoteDirectory;
            Username = settings.Username;
            AllowInsecureHttp = settings.AllowInsecureHttp;
            SelectedTlsVerifyModeIndex = (int)settings.TlsVerifyMode;
            _savedTlsVerifyMode = settings.TlsVerifyMode;
            AutoUploadEnabled = settings.AutoUploadEnabled;
            Password = string.Empty;
            HasStoredPassword = await _secretStore.LoadWebDavPasswordAsync(cancellationToken) is not null;
        }
        finally
        {
            _isLoading = false;
        }

        _activityLogService.Write(LogEntryKind.Success, "Backup", "WebDAV 同步设置已保存");
    }

    private Task SetWebDavPasswordAsync(string? value, CancellationToken cancellationToken)
        => string.IsNullOrEmpty(value)
            ? _secretStore.ClearWebDavPasswordAsync(cancellationToken)
            : _secretStore.SaveWebDavPasswordAsync(value, cancellationToken);

    private WebDavTlsVerifyMode GetSelectedTlsVerifyMode()
        => SelectedTlsVerifyModeIndex == (int)WebDavTlsVerifyMode.Skip
            ? WebDavTlsVerifyMode.Skip
            : WebDavTlsVerifyMode.Verify;

    private async Task RunAsync(
        string busyText,
        Func<CancellationToken, Task> operation,
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
            await operation(cancellationToken);
            ApplyStatus(_syncService.Status);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusText = "操作已取消";
        }
        catch (OperationCanceledException)
        {
            StatusText = "操作已取消";
        }
        catch (Exception ex)
        {
            StatusText = $"操作失败：{ex.Message}";
            _activityLogService.Write(LogEntryKind.Error, "Backup", StatusText, ex);
            await _notificationService.ShowWarningAsync("WebDAV 操作失败", ex.Message, CancellationToken.None);
        }
        finally
        {
            IsBusy = false;
            NotifyCommands();
        }
    }

    private void OnStatusChanged(object? sender, BackupSyncRuntimeStatus status)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            ApplyStatus(status);
        }
        else
        {
            Dispatcher.UIThread.Post(() => ApplyStatus(status));
        }
    }

    private void ApplyStatus(BackupSyncRuntimeStatus status)
    {
        StatusText = status.Message;
        HasConflict = status.HasConflict;
        LastSuccessfulSyncText = status.LastSuccessfulSync?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "从未";
        RemoteModifiedText = status.RemoteMetadata?.LastModified?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "未知";
    }

    private void NotifyCommands()
    {
        SaveCommand.NotifyCanExecuteChanged();
        TestConnectionCommand.NotifyCanExecuteChanged();
        UploadCommand.NotifyCanExecuteChanged();
        DownloadAndRestoreCommand.NotifyCanExecuteChanged();
    }
}
