using CommunityToolkit.Mvvm.Input;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Desktop.ViewModels;

public partial class MainWindowWorkflowViewModel
{
    private bool _systemSettingsConfigured;

    public string[] SystemSettingsCategories => SystemSettings.SystemSettingsCategories;

    public int SelectedSystemSettingsCategoryIndex
    {
        get => SystemSettings.SelectedSystemSettingsCategoryIndex;
        set
        {
            EnsureSystemSettingsConfigured();
            SystemSettings.SelectedSystemSettingsCategoryIndex = value;
        }
    }

    public bool IsSystemSettingsGeneralActive => SystemSettings.IsSystemSettingsGeneralActive;

    public bool IsSystemSettingsAppearanceActive => SystemSettings.IsSystemSettingsAppearanceActive;

    public bool IsSystemSettingsNetworkActive => SystemSettings.IsSystemSettingsNetworkActive;

    public bool IsSystemSettingsStorageActive => SystemSettings.IsSystemSettingsStorageActive;

    public bool IsSystemSettingsAboutActive => SystemSettings.IsSystemSettingsAboutActive;

    public bool LaunchOnStartupSupported => SystemSettings.LaunchOnStartupSupported;

    public bool MinimizeToTrayEnabled
    {
        get => SystemSettings.MinimizeToTrayEnabled;
        set
        {
            EnsureSystemSettingsConfigured();
            SystemSettings.MinimizeToTrayEnabled = value;
        }
    }

    public bool PreventSystemSleepWhileTasksActive
    {
        get => SystemSettings.PreventSystemSleepWhileTasksActive;
        set
        {
            EnsureSystemSettingsConfigured();
            SystemSettings.PreventSystemSleepWhileTasksActive = value;
        }
    }

    public bool LaunchOnStartupEnabled
    {
        get => SystemSettings.LaunchOnStartupEnabled;
        set
        {
            EnsureSystemSettingsConfigured();
            SystemSettings.LaunchOnStartupEnabled = value;
        }
    }

    public bool CheckUpdatesOnStartup
    {
        get => SystemSettings.CheckUpdatesOnStartup;
        set
        {
            EnsureSystemSettingsConfigured();
            SystemSettings.CheckUpdatesOnStartup = value;
        }
    }

    public int RequestTimeoutSeconds
    {
        get => SystemSettings.RequestTimeoutSeconds;
        set
        {
            EnsureSystemSettingsConfigured();
            SystemSettings.RequestTimeoutSeconds = value;
        }
    }

    public int NetworkMaxRetries
    {
        get => SystemSettings.NetworkMaxRetries;
        set
        {
            EnsureSystemSettingsConfigured();
            SystemSettings.NetworkMaxRetries = value;
        }
    }

    public IAsyncRelayCommand SaveSettingsCommand
    {
        get
        {
            EnsureSystemSettingsConfigured();
            return SystemSettings.SaveSettingsCommand;
        }
    }

    private bool IsLoadingSettings => SystemSettings.IsLoadingSettings;

    private void EnsureSystemSettingsConfigured()
    {
        if (_systemSettingsConfigured)
        {
            return;
        }

        _systemSettingsConfigured = true;
        SystemSettings.Configure(
            () => IsInitializationComplete,
            () => GrabPage.CurrentReservationStrategy,
            () => TraceIntGraphQlOverridesEnabled,
            () => CurrentHomeReservationProgressTimingMode,
            () => HomeReservationFixedDurationMinutes,
            () => CurrentHomeCookieProgressTimingMode,
            () => HomeCookieFixedDurationMinutes,
            () => AutoReleaseReservationEnabled,
            () => AutoReleaseLeadSeconds,
            BuildTaskEventAlertSettingsSnapshot,
            CancelPendingNotificationSettingsAutoSave);
    }

    private void ConfigureSystemSettingsPropertyBridge(ViewModelPropertyBridge propertyBridge)
    {
        propertyBridge.ForwardSame(
            SystemSettings,
            nameof(SystemSettings.SelectedSystemSettingsCategoryIndex),
            nameof(SystemSettings.IsSystemSettingsGeneralActive),
            nameof(SystemSettings.IsSystemSettingsAppearanceActive),
            nameof(SystemSettings.IsSystemSettingsNetworkActive),
            nameof(SystemSettings.IsSystemSettingsStorageActive),
            nameof(SystemSettings.IsSystemSettingsAboutActive),
            nameof(SystemSettings.MinimizeToTrayEnabled),
            nameof(SystemSettings.PreventSystemSleepWhileTasksActive),
            nameof(SystemSettings.LaunchOnStartupEnabled),
            nameof(SystemSettings.CheckUpdatesOnStartup),
            nameof(SystemSettings.RequestTimeoutSeconds),
            nameof(SystemSettings.NetworkMaxRetries),
            nameof(SystemSettings.SelectedAppThemeModeIndex),
            nameof(SystemSettings.UseSystemAccent));
    }

    private Task LoadSettingsAsync()
    {
        EnsureSystemSettingsConfigured();
        return SystemSettings.LoadAndApplyAsync(settings =>
        {
            HomeDashboard.ApplySettings(settings);
            Session.ApplySettings(settings);
            TraceIntGraphQlOverridesEnabled = settings.TraceIntProtocol.GraphQlOverridesEnabled;
            GrabPage.ApplySettings(settings);
            OccupyPage.ApplySettings(settings);
            TomorrowReservationPage.ApplySettings(settings);
            MobileControl.ApplySettings(settings.MobileControl);
            NotificationSettings.ApplySettings(settings);
            UpdateHomeDashboardPresentation();
            EnsureNotificationSettingsConfigured();
            NotificationSettings.MarkSettingsLoaded();
        });
    }

    private void ScheduleSystemSettingsAutoSave()
    {
        EnsureSystemSettingsConfigured();
        SystemSettings.ScheduleAutoSave();
    }

    private void CancelPendingSystemSettingsAutoSave()
    {
        SystemSettings.CancelPendingAutoSave();
    }

    private Task FlushPendingSystemSettingsAsync(CancellationToken cancellationToken = default)
    {
        return SystemSettings.FlushPendingAutoSaveAsync(cancellationToken);
    }
}
