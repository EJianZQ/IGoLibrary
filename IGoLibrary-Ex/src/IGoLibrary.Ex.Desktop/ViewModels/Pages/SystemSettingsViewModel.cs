using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Desktop.ViewModels;

public sealed partial class SystemSettingsViewModel : ViewModelBase
{
    private readonly ISettingsWorkflowService _settingsWorkflowService;
    private readonly IProtocolTemplateEditorService _protocolTemplateEditorService;
    private readonly IAppThemeService _appThemeService;
    private readonly IActivityLogService _activityLogService;
    private readonly INotificationService _notificationService;
    private readonly IStartupEntryService _startupEntryService;
    private readonly DeferredAutoSaveController _systemSettingsAutoSave;

    private Func<bool> _isInitialized = static () => false;
    private Func<GrabReservationStrategy> _grabReservationStrategy = static () => GrabReservationStrategy.QueryThenReserve;
    private Func<bool> _traceIntGraphQlOverridesEnabled = static () => false;
    private Func<HomeReservationProgressTimingMode> _homeReservationProgressMode = static () => HomeReservationProgressTimingMode.FixedReservationDuration;
    private Func<int> _homeReservationFixedDurationMinutes = static () => HomeReservationProgressSettings.DefaultFixedDurationMinutes;
    private Func<HomeCookieProgressTimingMode> _homeCookieProgressMode = static () => HomeCookieProgressTimingMode.FixedCookieDuration;
    private Func<int> _homeCookieFixedDurationMinutes = static () => HomeCookieProgressSettings.DefaultFixedDurationMinutes;
    private Func<bool> _autoReleaseEnabled = static () => AutoReleaseTaskSettings.Default.Enabled;
    private Func<int> _autoReleaseLeadSeconds = static () => AutoReleaseTaskSettings.DefaultLeadSeconds;
    private Func<TaskEventAlertSettings> _taskEventAlertSettings = static () => TaskEventAlertSettings.Default;
    private Action _cancelPendingNotificationSettingsAutoSave = static () => { };
    private bool _isRollingBackStartupEntry;

    public SystemSettingsViewModel(
        ISettingsWorkflowService settingsWorkflowService,
        IProtocolTemplateEditorService protocolTemplateEditorService,
        IAppThemeService appThemeService,
        IActivityLogService activityLogService,
        INotificationService notificationService,
        IStartupEntryService startupEntryService,
        StorageSettingsViewModel storageSettings)
    {
        _settingsWorkflowService = settingsWorkflowService;
        _protocolTemplateEditorService = protocolTemplateEditorService;
        _appThemeService = appThemeService;
        _activityLogService = activityLogService;
        _notificationService = notificationService;
        _startupEntryService = startupEntryService;
        StorageSettings = storageSettings;
        _systemSettingsAutoSave = new DeferredAutoSaveController(
            TimeSpan.FromMilliseconds(300),
            cancellationToken => PersistSystemSettingsAsync(showNotification: false, cancellationToken));
    }

    public string[] SystemSettingsCategories { get; } = ["常规", "外观", "网络与接口", "存储与日志", "关于"];

    public StorageSettingsViewModel StorageSettings { get; }

    public string[] ThemeModes { get; } = ["跟随系统", "浅色", "深色"];

    [ObservableProperty]
    private int selectedSystemSettingsCategoryIndex;

    public bool IsSystemSettingsGeneralActive => SelectedSystemSettingsCategoryIndex == 0;

    public bool IsSystemSettingsAppearanceActive => SelectedSystemSettingsCategoryIndex == 1;

    public bool IsSystemSettingsNetworkActive => SelectedSystemSettingsCategoryIndex == 2;

    public bool IsSystemSettingsStorageActive => SelectedSystemSettingsCategoryIndex == 3;

    public bool IsSystemSettingsAboutActive => SelectedSystemSettingsCategoryIndex == 4;

    public bool LaunchOnStartupSupported => _startupEntryService.IsSupported;

    [ObservableProperty]
    private bool minimizeToTrayEnabled = true;

    [ObservableProperty]
    private bool launchOnStartupEnabled;

    [ObservableProperty]
    private bool checkUpdatesOnStartup = true;

    [ObservableProperty]
    private int requestTimeoutSeconds = 5;

    [ObservableProperty]
    private int networkMaxRetries = 3;

    [ObservableProperty]
    private int selectedAppThemeModeIndex;

    [ObservableProperty]
    private bool useSystemAccent = OperatingSystem.IsWindows();

    public bool IsLoadingSettings { get; private set; }

    public bool HasPendingAutoSave => _systemSettingsAutoSave.HasPending;

    public void Configure(
        Func<bool> isInitialized,
        Func<GrabReservationStrategy> grabReservationStrategy,
        Func<bool> traceIntGraphQlOverridesEnabled,
        Func<HomeReservationProgressTimingMode> homeReservationProgressMode,
        Func<int> homeReservationFixedDurationMinutes,
        Func<HomeCookieProgressTimingMode> homeCookieProgressMode,
        Func<int> homeCookieFixedDurationMinutes,
        Func<bool> autoReleaseEnabled,
        Func<int> autoReleaseLeadSeconds,
        Func<TaskEventAlertSettings> taskEventAlertSettings,
        Action cancelPendingNotificationSettingsAutoSave)
    {
        _isInitialized = isInitialized;
        _grabReservationStrategy = grabReservationStrategy;
        _traceIntGraphQlOverridesEnabled = traceIntGraphQlOverridesEnabled;
        _homeReservationProgressMode = homeReservationProgressMode;
        _homeReservationFixedDurationMinutes = homeReservationFixedDurationMinutes;
        _homeCookieProgressMode = homeCookieProgressMode;
        _homeCookieFixedDurationMinutes = homeCookieFixedDurationMinutes;
        _autoReleaseEnabled = autoReleaseEnabled;
        _autoReleaseLeadSeconds = autoReleaseLeadSeconds;
        _taskEventAlertSettings = taskEventAlertSettings;
        _cancelPendingNotificationSettingsAutoSave = cancelPendingNotificationSettingsAutoSave;
    }

    public async Task LoadAndApplyAsync(
        Action<AppSettings> applyLoadedSettings,
        CancellationToken cancellationToken = default)
    {
        IsLoadingSettings = true;
        var settings = await LoadSettingsAsync(cancellationToken);
        try
        {
            ApplySettings(settings);
            applyLoadedSettings(settings);
        }
        finally
        {
            IsLoadingSettings = false;
        }

        await SyncStartupEntryAfterLoadAsync();
        await StorageSettings.InitializeAsync(settings.Logging, cancellationToken);
    }

    public void ApplySettings(AppSettings settings)
    {
        var ui = settings.Ui;
        var theme = ui.Theme ?? ThemePreferences.Default;

        MinimizeToTrayEnabled = ui.MinimizeToTray;
        LaunchOnStartupEnabled = ui.LaunchOnStartup;
        CheckUpdatesOnStartup = settings.Updates.CheckOnStartup;
        RequestTimeoutSeconds = settings.Network.TimeoutSeconds;
        NetworkMaxRetries = settings.Network.MaxRetries;
        SelectedAppThemeModeIndex = (int)theme.Mode;
        UseSystemAccent = theme.UseSystemAccent;
    }

    public void ScheduleAutoSave()
    {
        if (IsLoadingSettings || !_isInitialized())
        {
            return;
        }

        _systemSettingsAutoSave.Schedule(ex =>
            _activityLogService.Write(LogEntryKind.Warning, "Settings", $"自动保存系统设置失败：{ex.Message}"));
    }

    public void CancelPendingAutoSave()
    {
        _systemSettingsAutoSave.Cancel();
    }

    public Task FlushPendingAutoSaveAsync(CancellationToken cancellationToken = default)
    {
        return _systemSettingsAutoSave.FlushAsync(cancellationToken);
    }

    public Task<AppSettings> LoadSettingsAsync(CancellationToken cancellationToken = default)
    {
        return _settingsWorkflowService.LoadAsync(cancellationToken);
    }

    public Task<AppSettings> SaveSystemSettingsAsync(
        SystemSettingsSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        return _settingsWorkflowService.SaveSystemSettingsAsync(snapshot, cancellationToken);
    }

    public Task ClearStoredLibrarySelectionAsync(CancellationToken cancellationToken = default)
    {
        return _settingsWorkflowService.ClearStoredLibrarySelectionAsync(cancellationToken);
    }

    public Task SaveGrabScheduledStartDefaultAsync(
        TimeSpan value,
        CancellationToken cancellationToken = default)
    {
        return _settingsWorkflowService.SaveGrabScheduledStartDefaultAsync(value, cancellationToken);
    }

    public Task SaveTomorrowScheduledStartDefaultAsync(
        TimeSpan value,
        CancellationToken cancellationToken = default)
    {
        return _settingsWorkflowService.SaveTomorrowScheduledStartDefaultAsync(value, cancellationToken);
    }

    public Task SaveGlobalLeakSelectedLibrariesAsync(
        IReadOnlyList<GlobalLeakLibraryTarget> libraries,
        CancellationToken cancellationToken = default)
    {
        return _settingsWorkflowService.SaveGlobalLeakSelectedLibrariesAsync(libraries, cancellationToken);
    }

    public Task SaveDashboardMetricsAsync(
        DashboardMetrics metrics,
        CancellationToken cancellationToken = default)
    {
        return _settingsWorkflowService.SaveDashboardMetricsAsync(metrics, cancellationToken);
    }

    public Task<TraceIntProtocolTemplates> LoadProtocolTemplatesAsync(CancellationToken cancellationToken = default)
    {
        return _protocolTemplateEditorService.LoadTemplatesAsync(cancellationToken);
    }

    public Task SaveProtocolOverridesAsync(
        TraceIntProtocolTemplateOverrides overrides,
        CancellationToken cancellationToken = default)
    {
        return _protocolTemplateEditorService.SaveOverridesAsync(overrides, cancellationToken);
    }

    public Task ResetProtocolOverridesAsync(CancellationToken cancellationToken = default)
    {
        return _protocolTemplateEditorService.ResetOverridesAsync(cancellationToken);
    }

    partial void OnSelectedSystemSettingsCategoryIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsSystemSettingsGeneralActive));
        OnPropertyChanged(nameof(IsSystemSettingsAppearanceActive));
        OnPropertyChanged(nameof(IsSystemSettingsNetworkActive));
        OnPropertyChanged(nameof(IsSystemSettingsStorageActive));
        OnPropertyChanged(nameof(IsSystemSettingsAboutActive));
    }

    partial void OnMinimizeToTrayEnabledChanged(bool value) => ScheduleAutoSave();

    partial void OnLaunchOnStartupEnabledChanged(bool value)
    {
        if (_isRollingBackStartupEntry)
        {
            ScheduleAutoSave();
            return;
        }

        if (!_startupEntryService.IsSupported && value)
        {
            _isRollingBackStartupEntry = true;
            try
            {
                LaunchOnStartupEnabled = false;
            }
            finally
            {
                _isRollingBackStartupEntry = false;
            }

            if (!IsLoadingSettings && _isInitialized())
            {
                _ = NotifyLaunchOnStartupUnsupportedAsync();
            }

            return;
        }

        ScheduleAutoSave();
        _ = ApplyLaunchOnStartupEntryAsync(value);
    }

    partial void OnCheckUpdatesOnStartupChanged(bool value) => ScheduleAutoSave();

    partial void OnRequestTimeoutSecondsChanged(int value)
    {
        var normalized = Math.Clamp(value, 3, 60);
        if (normalized != value)
        {
            RequestTimeoutSeconds = normalized;
            return;
        }

        ScheduleAutoSave();
    }

    partial void OnNetworkMaxRetriesChanged(int value)
    {
        var normalized = Math.Clamp(value, 0, 10);
        if (normalized != value)
        {
            NetworkMaxRetries = normalized;
            return;
        }

        ScheduleAutoSave();
    }

    partial void OnSelectedAppThemeModeIndexChanged(int value)
    {
        PreviewThemePreferences();
        ScheduleAutoSave();
    }

    partial void OnUseSystemAccentChanged(bool value)
    {
        PreviewThemePreferences();
        ScheduleAutoSave();
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        CancelPendingAutoSave();
        _cancelPendingNotificationSettingsAutoSave();
        await PersistSystemSettingsAsync(showNotification: true);
    }

    private async Task PersistSystemSettingsAsync(
        bool showNotification,
        CancellationToken cancellationToken = default)
    {
        var theme = new ThemePreferences(
            (AppThemeMode)Math.Clamp(SelectedAppThemeModeIndex, 0, ThemeModes.Length - 1),
            UseSystemAccent);
        var homeReservationProgress = new HomeReservationProgressSettings(
            _homeReservationProgressMode(),
            HomeReservationProgressSettings.NormalizeFixedDurationMinutes(_homeReservationFixedDurationMinutes()));
        var homeCookieProgress = new HomeCookieProgressSettings(
            _homeCookieProgressMode(),
            HomeCookieProgressSettings.NormalizeFixedDurationMinutes(_homeCookieFixedDurationMinutes()));

        await SaveSystemSettingsAsync(new SystemSettingsSnapshot(
            MinimizeToTrayEnabled,
            LaunchOnStartupEnabled,
            _traceIntGraphQlOverridesEnabled(),
            CheckUpdatesOnStartup,
            Math.Clamp(RequestTimeoutSeconds, 3, 60),
            Math.Clamp(NetworkMaxRetries, 0, 10),
            theme,
            homeReservationProgress,
            homeCookieProgress,
            _grabReservationStrategy(),
            _autoReleaseEnabled(),
            AutoReleaseTaskSettings.NormalizeLeadSeconds(_autoReleaseLeadSeconds()),
            _taskEventAlertSettings()),
            cancellationToken);
        await _appThemeService.ApplyThemeAsync(theme, cancellationToken);
        if (showNotification)
        {
            await _notificationService.ShowSuccessAsync("设置已保存", "应用设置已写入本地数据库");
        }
    }

    private void PreviewThemePreferences()
    {
        if (IsLoadingSettings)
        {
            return;
        }

        _ = PreviewThemePreferencesAsync();
    }

    private async Task PreviewThemePreferencesAsync()
    {
        try
        {
            await _appThemeService.ApplyThemeAsync(new ThemePreferences(
                (AppThemeMode)Math.Clamp(SelectedAppThemeModeIndex, 0, ThemeModes.Length - 1),
                UseSystemAccent));
        }
        catch
        {
            // Theme preview should never block the rest of the settings workflow.
        }
    }

    private async Task ApplyLaunchOnStartupEntryAsync(bool enabled)
    {
        if (IsLoadingSettings || !_isInitialized())
        {
            return;
        }

        if (!_startupEntryService.IsSupported)
        {
            return;
        }

        try
        {
            if (enabled)
            {
                await _startupEntryService.EnableAsync();
                _activityLogService.Write(LogEntryKind.Success, "Settings", "已写入开机启动项");
                await _notificationService.ShowSuccessAsync("开机启动项", "已注册开机自启动");
            }
            else
            {
                await _startupEntryService.DisableAsync();
                _activityLogService.Write(LogEntryKind.Info, "Settings", "已移除开机启动项");
                await _notificationService.ShowInfoAsync("开机启动项", "已移除开机自启动");
            }
        }
        catch (Exception ex)
        {
            _activityLogService.Write(LogEntryKind.Warning, "Settings", $"更新开机启动项失败：{ex.Message}");

            _isRollingBackStartupEntry = true;
            try
            {
                LaunchOnStartupEnabled = !enabled;
                var restoredState = enabled ? "关闭" : "开启";
                await _notificationService.ShowWarningAsync(
                    "开机启动项更新失败",
                    $"已恢复开关到{restoredState}状态。原因：{ex.Message}");
            }
            finally
            {
                _isRollingBackStartupEntry = false;
            }
        }
    }

    private async Task SyncStartupEntryAfterLoadAsync()
    {
        if (!_startupEntryService.IsSupported || !LaunchOnStartupEnabled)
        {
            return;
        }

        try
        {
            await _startupEntryService.EnableAsync();
            _activityLogService.Write(LogEntryKind.Info, "Settings", "已同步开机启动项");
        }
        catch (Exception ex)
        {
            _activityLogService.Write(LogEntryKind.Warning, "Settings", $"同步开机启动项失败：{ex.Message}");
        }
    }

    private async Task NotifyLaunchOnStartupUnsupportedAsync()
    {
        try
        {
            await _notificationService.ShowWarningAsync(
                "开机启动项不可用",
                "当前系统暂不支持开机自启动，已恢复开关到关闭状态。");
        }
        catch (Exception ex)
        {
            _activityLogService.Write(LogEntryKind.Warning, "Settings", $"提示开机启动不可用失败：{ex.Message}");
        }
    }
}
