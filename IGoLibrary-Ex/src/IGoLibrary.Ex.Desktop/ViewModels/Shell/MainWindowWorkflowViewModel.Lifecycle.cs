using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Desktop.Platform;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Helpers;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Desktop.ViewModels;

public partial class MainWindowWorkflowViewModel
{
    public bool IsInitializationComplete
    {
        get => WorkflowState.IsInitializationComplete;
        set
        {
            if (WorkflowState.IsInitializationComplete == value)
            {
                return;
            }

            WorkflowState.IsInitializationComplete = value;
            OnPropertyChanged();
        }
    }

    public async Task InitializeAsync()
    {
        EnsurePropertyBridge();
        EnsureNavigationConfigured();
        EnsureActivityLogsConfigured();
        EnsureSessionConfigured();

        if (!_workflowStateSubscribed)
        {
            _workflowStateSubscribed = true;
            WorkflowState.PropertyChanged += OnWorkflowStatePropertyChanged;
        }

        if (!_themePaletteSubscribed)
        {
            _themePaletteSubscribed = true;
            _appThemeService.PaletteChanged += OnThemePaletteChanged;
            ApplyThemePalette(_appThemeService.CurrentPalette);
        }

        if (!_lanCookieRelayServiceSubscribed)
        {
            _lanCookieRelayServiceSubscribed = true;
            _lanCookieRelayService.Stopped += OnLanCookieRelayStopped;
        }

        _ = LoadProjectAuthorAvatarAsync();

        _activityLogService.EntryWritten += OnLogEntryWritten;
        EnsureOccupyPageConfigured();
        EnsureGrabPageConfigured();
        EnsureGlobalLeakPageConfigured();
        EnsureTomorrowReservationPageConfigured();

        if (!_reservationCountdownTimerInitialized)
        {
            _reservationCountdownTimerInitialized = true;
            _reservationCountdownTimer.Tick += OnReservationCountdownTick;
            _reservationCountdownTimer.Start();
        }

        UpdateHomeDashboardPresentation();

        try
        {
            await LoadSettingsAsync();
            await InitializeMobileControlAsync();
            await LoadProtocolTemplatesAsync();

            try
            {
                await RestoreSessionForStartupAsync();
                if (IsAuthorized && SelectedLibrary is not null)
                {
                    await BindSelectedLibraryAsync();
                }
            }
            catch (Exception ex)
            {
                _activityLogService.Write(LogEntryKind.Warning, "Bootstrap", $"恢复会话失败：{ex.Message}");
            }
        }
        finally
        {
            IsInitializationComplete = true;
            UpdateHomeDashboardPresentation();
            QueueAutoReleaseReservationRefresh();
            QueueAutoReleaseCheck();
            _ = RunStartupUpdateCheckAsync();
        }
    }

    public async Task FlushPendingScheduledStartDefaultsAsync(CancellationToken cancellationToken = default)
    {
        await StopLanCookieRelaySessionAsync(closeDialog: false);
        try
        {
            await _mobileControlService.StopAsync(cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _activityLogService.Write(LogEntryKind.Warning, "MobileControl", $"退出前停止手机控制失败：{ex.Message}");
        }

        var hasPendingProtocolTemplates = HasPendingProtocolTemplateAutoSave;

        CancelPendingProtocolTemplateAutoSave();

        try
        {
            await FlushPendingSystemSettingsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _activityLogService.Write(LogEntryKind.Warning, "Settings", $"退出前保存系统设置失败：{ex.Message}");
        }

        if (hasPendingProtocolTemplates)
        {
            try
            {
                await PersistProtocolOverridesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _activityLogService.Write(LogEntryKind.Warning, "Settings", $"退出前保存接口模板失败：{ex.Message}");
            }
        }

        try
        {
            await GrabPage.FlushPendingScheduledStartDefaultAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _activityLogService.Write(LogEntryKind.Warning, "Settings", $"退出前保存抢座定时时间默认值失败：{ex.Message}");
        }

        try
        {
            await TomorrowReservationPage.FlushPendingScheduledStartDefaultAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _activityLogService.Write(LogEntryKind.Warning, "Settings", $"退出前保存明日预约触发时间默认值失败：{ex.Message}");
        }
    }
}
