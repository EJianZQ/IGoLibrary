using System.Collections.ObjectModel;
using System.Diagnostics;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using Avalonia.Threading;
using Avalonia.Media;
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
    public bool ShouldHideToTrayOnClose =>
        MinimizeToTrayEnabled &&
        (IsTaskActive(_grabSeatCoordinator.GetStatus()) ||
         IsTaskActive(_globalLeakCoordinator.GetStatus()) ||
         IsTaskActive(_occupySeatCoordinator.GetStatus()) ||
         IsTaskActive(_tomorrowReservationCoordinator.GetStatus()));

    public int SelectedSeatCount => SelectedSeats.Count;

    public bool HasSelectedSeats => SelectedSeatCount > 0;

    public bool HasNoSelectedSeats => !HasSelectedSeats;

    public bool HasGlobalLeakLibraries => GlobalLeakLibraries.Count > 0;

    public bool HasNoGlobalLeakLibraries => !HasGlobalLeakLibraries;

    public bool CanEditGrabConfiguration => !IsGrabTaskActive;

    public bool CanEditGrabScheduledStartTime => CanEditGrabConfiguration && IsGrabScheduledStartEnabled;

    public int SelectedGlobalLeakLibraryCount => SelectedGlobalLeakLibraries.Count;

    public bool HasSelectedGlobalLeakLibraries => SelectedGlobalLeakLibraryCount > 0;

    public bool HasNoSelectedGlobalLeakLibraries => !HasSelectedGlobalLeakLibraries;

    public bool CanEditGlobalLeakConfiguration => !IsGlobalLeakTaskActive;

    public bool CanEditTomorrowConfiguration => !IsTomorrowTaskActive && !HasActiveVenuePreview;

    public bool HasSelectedTomorrowSeat => SelectedTomorrowSeat is not null;

    public bool HasNoSelectedTomorrowSeat => !HasSelectedTomorrowSeat;

    public bool HasTomorrowSeatLayout => _tomorrowSeats.Count > 0;

    public bool HasNoTomorrowSeatLayout => !HasTomorrowSeatLayout;

    public bool HasVisibleTomorrowSeatResults => _tomorrowSeats.Any(static seat => seat.IsFilterVisible);

    public bool ShowTomorrowSeatFilterEmptyState => HasTomorrowSeatLayout && !HasVisibleTomorrowSeatResults;

    public string SelectedTomorrowSeatText => SelectedTomorrowSeat is null
        ? "尚未选择明日预约座位"
        : $"已选择 {SelectedTomorrowSeat.SeatName}";

    public string DraftSelectedTomorrowSeatSummaryText
    {
        get
        {
            var seat = _tomorrowSeats.FirstOrDefault(x =>
                string.Equals(x.SeatKey, _draftTomorrowSeatKey, StringComparison.Ordinal));
            return seat is null
                ? "本次尚未选择明日预约座位"
                : $"本次已选择 {seat.SeatName}";
        }
    }

    public int DraftSelectedSeatCount => _draftSelectedSeatKeys.Count;

    public bool HasVisibleSeatResults => VisibleSeatResultCount > 0;

    public bool HasNoVisibleSeatResults => !HasVisibleSeatResults;

    public bool HasSeatLayout => _allSeats.Count > 0;

    public bool HasNoSeatLayout => !HasSeatLayout;

    public bool ShowSeatFilterEmptyState => HasSeatLayout && HasNoVisibleSeatResults;

    public bool IsEmailNotificationTabActive => SelectedNotificationSettingsTabIndex == 0;

    public bool IsTelegramNotificationTabActive => SelectedNotificationSettingsTabIndex == 1;

    public bool IsBarkNotificationTabActive => SelectedNotificationSettingsTabIndex == 2;

    public bool IsLocalNotificationTabActive => SelectedNotificationSettingsTabIndex == 3;

    public double NotificationSegmentControlWidth => NotificationSegmentControlWidthValue;

    public double NotificationSegmentSliderWidth => NotificationSegmentSliderWidthValue;

    public double NotificationSegmentSliderOffset => Math.Clamp(SelectedNotificationSettingsTabIndex, 0, 3) *
                                                     NotificationSegmentSliderOffsetValue;

    public IBrush EmailNotificationTabBackgroundBrush => IsEmailNotificationTabActive
        ? NotificationSegmentActiveBrush
        : NotificationSegmentInactiveBrush;

    public IBrush LocalNotificationTabBackgroundBrush => IsLocalNotificationTabActive
        ? NotificationSegmentActiveBrush
        : NotificationSegmentInactiveBrush;

    public IBrush BarkNotificationTabBackgroundBrush => IsBarkNotificationTabActive
        ? NotificationSegmentActiveBrush
        : NotificationSegmentInactiveBrush;

    public IBrush TelegramNotificationTabForegroundBrush => IsTelegramNotificationTabActive
        ? NotificationSegmentActiveTextBrush
        : NotificationSegmentInactiveTextBrush;

    public IBrush BarkNotificationTabForegroundBrush => IsBarkNotificationTabActive
        ? NotificationSegmentActiveTextBrush
        : NotificationSegmentInactiveTextBrush;

    public IBrush EmailNotificationTabForegroundBrush => IsEmailNotificationTabActive
        ? NotificationSegmentActiveTextBrush
        : NotificationSegmentInactiveTextBrush;

    public IBrush LocalNotificationTabForegroundBrush => IsLocalNotificationTabActive
        ? NotificationSegmentActiveTextBrush
        : NotificationSegmentInactiveTextBrush;

    public string SelectedSeatSummaryText => HasSelectedSeats
        ? $"已选 {SelectedSeatCount} 个目标座位"
        : "尚未选择目标座位";

    public string SelectedSeatHintText => HasSelectedSeats
        ? "这些座位会被持续监控，任意一个释放后都会立即尝试预约。"
        : "点击上方按钮打开选座工作区，确认后才会同步到主界面。";

    public string DraftSelectedSeatSummaryText => DraftSelectedSeatCount > 0
        ? $"本次已勾选 {DraftSelectedSeatCount} 个目标座位"
        : "本次尚未勾选目标座位";

    public string SelectedGlobalLeakLibrarySummaryText => HasSelectedGlobalLeakLibraries
        ? $"已选 {SelectedGlobalLeakLibraryCount} 个扫描场馆"
        : "尚未选择扫描场馆";

    public int DraftGlobalLeakLibraryCount => _draftGlobalLeakLibraryIds.Count;

    public string DraftGlobalLeakLibrarySummaryText => DraftGlobalLeakLibraryCount > 0
        ? $"本次已勾选 {DraftGlobalLeakLibraryCount} 个场馆"
        : "本次尚未勾选场馆";

    public string GrabDashboardStatusText => _grabTaskState switch
    {
        CoordinatorTaskState.Starting => "启动中",
        CoordinatorTaskState.Running => "运行中",
        CoordinatorTaskState.Stopping => "停止中",
        CoordinatorTaskState.Completed when _grabStatusReason == CoordinatorStatusReason.Stopped => "已停止",
        CoordinatorTaskState.Completed => "已完成",
        CoordinatorTaskState.Failed => "异常",
        _ => "未运行"
    };

    public IBrush GrabDashboardStatusBrush => _grabTaskState switch
    {
        CoordinatorTaskState.Starting => GrabStateWarningBrush,
        CoordinatorTaskState.Running => GrabStateRunningBrush,
        CoordinatorTaskState.Stopping => GrabStateWarningBrush,
        CoordinatorTaskState.Completed when _grabStatusReason == CoordinatorStatusReason.Stopped => GrabStateFailureBrush,
        CoordinatorTaskState.Completed => GrabStateSuccessBrush,
        CoordinatorTaskState.Failed => GrabStateFailureBrush,
        _ => GrabStateIdleBrush
    };

    public string GlobalLeakDashboardStatusText => _globalLeakTaskState switch
    {
        CoordinatorTaskState.Starting => "启动中",
        CoordinatorTaskState.Running => "运行中",
        CoordinatorTaskState.Stopping => "停止中",
        CoordinatorTaskState.Completed when _globalLeakStatusReason == CoordinatorStatusReason.Stopped => "已停止",
        CoordinatorTaskState.Completed => "已完成",
        CoordinatorTaskState.Failed => "异常",
        _ => "未运行"
    };

    public IBrush GlobalLeakDashboardStatusBrush => _globalLeakTaskState switch
    {
        CoordinatorTaskState.Starting => GrabStateWarningBrush,
        CoordinatorTaskState.Running => GrabStateRunningBrush,
        CoordinatorTaskState.Stopping => GrabStateWarningBrush,
        CoordinatorTaskState.Completed when _globalLeakStatusReason == CoordinatorStatusReason.Stopped => GrabStateFailureBrush,
        CoordinatorTaskState.Completed => GrabStateSuccessBrush,
        CoordinatorTaskState.Failed => GrabStateFailureBrush,
        _ => GrabStateIdleBrush
    };

    public string TomorrowDashboardStatusText => _tomorrowTaskState switch
    {
        CoordinatorTaskState.Starting => "启动中",
        CoordinatorTaskState.Running => "运行中",
        CoordinatorTaskState.Stopping => "停止中",
        CoordinatorTaskState.Completed when _tomorrowStatusReason == CoordinatorStatusReason.Stopped => "已停止",
        CoordinatorTaskState.Completed => "已完成",
        CoordinatorTaskState.Failed => "异常",
        _ => "未运行"
    };

    public IBrush TomorrowDashboardStatusBrush => _tomorrowTaskState switch
    {
        CoordinatorTaskState.Starting => GrabStateWarningBrush,
        CoordinatorTaskState.Running => GrabStateRunningBrush,
        CoordinatorTaskState.Stopping => GrabStateWarningBrush,
        CoordinatorTaskState.Completed when _tomorrowStatusReason == CoordinatorStatusReason.Stopped => GrabStateFailureBrush,
        CoordinatorTaskState.Completed => GrabStateSuccessBrush,
        CoordinatorTaskState.Failed => GrabStateFailureBrush,
        _ => GrabStateIdleBrush
    };

    partial void OnIsOccupyRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(IsOccupyStopped));
    }

    partial void OnIsCancellingCurrentReservationChanged(bool value)
    {
        OnPropertyChanged(nameof(CanCancelCurrentReservation));
    }

    partial void OnIsGrabTaskActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEditGrabConfiguration));
        OnPropertyChanged(nameof(CanEditGrabScheduledStartTime));
    }

    partial void OnIsGlobalLeakTaskActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEditGlobalLeakConfiguration));
    }

    partial void OnGlobalLeakScanIntervalSecondsChanged(int value)
    {
        var normalized = Math.Clamp(value, 1, 3600);
        if (normalized != value)
        {
            GlobalLeakScanIntervalSeconds = normalized;
        }
    }

    partial void OnIsGrabScheduledStartEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEditGrabScheduledStartTime));
    }

    partial void OnScheduledStartTimeChanged(TimeSpan? value)
    {
        if (value is null)
        {
            ScheduledStartTime = _grabScheduledStartDefault;
            return;
        }

        if (!IsTimeOfDay(value.Value))
        {
            return;
        }

        _grabScheduledStartDefault = value.Value;
        ScheduleGrabScheduledStartDefaultAutoSave(value.Value);
    }

    partial void OnTomorrowScheduledStartTimeChanged(TimeSpan? value)
    {
        if (value is null)
        {
            TomorrowScheduledStartTime = _tomorrowScheduledStartDefault;
            return;
        }

        if (!IsTimeOfDay(value.Value))
        {
            return;
        }

        _tomorrowScheduledStartDefault = value.Value;
        ScheduleTomorrowScheduledStartDefaultAutoSave(value.Value);
    }

    partial void OnIsTomorrowTaskActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEditTomorrowConfiguration));
        if (!value)
        {
            return;
        }

        RestoreCommittedTomorrowSeatSelection();
        IsTomorrowSeatSelectionOverlayOpen = false;
    }

    partial void OnSelectedTomorrowSeatChanged(SeatReference? value)
    {
        OnPropertyChanged(nameof(HasSelectedTomorrowSeat));
        OnPropertyChanged(nameof(HasNoSelectedTomorrowSeat));
        OnPropertyChanged(nameof(SelectedTomorrowSeatText));
    }

    partial void OnVisibleSeatResultCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasVisibleSeatResults));
        OnPropertyChanged(nameof(HasNoVisibleSeatResults));
        OnPropertyChanged(nameof(ShowSeatFilterEmptyState));
    }

    partial void OnSelectedNotificationSettingsTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsEmailNotificationTabActive));
        OnPropertyChanged(nameof(IsTelegramNotificationTabActive));
        OnPropertyChanged(nameof(IsBarkNotificationTabActive));
        OnPropertyChanged(nameof(IsLocalNotificationTabActive));
        OnPropertyChanged(nameof(NotificationSegmentSliderOffset));
        OnPropertyChanged(nameof(EmailNotificationTabBackgroundBrush));
        OnPropertyChanged(nameof(BarkNotificationTabBackgroundBrush));
        OnPropertyChanged(nameof(LocalNotificationTabBackgroundBrush));
        OnPropertyChanged(nameof(EmailNotificationTabForegroundBrush));
        OnPropertyChanged(nameof(TelegramNotificationTabForegroundBrush));
        OnPropertyChanged(nameof(BarkNotificationTabForegroundBrush));
        OnPropertyChanged(nameof(LocalNotificationTabForegroundBrush));
    }

    partial void OnIsAuthorizedChanged(bool value)
    {
        if (!value && SelectedTabIndex > AccountAndVenueTabIndex)
        {
            SelectedTabIndex = AccountAndVenueTabIndex;
        }

        if (!value)
        {
            _globalLeakSelectionRestoredForCurrentSession = false;
        }

        UpdateSidebarItems();

        OnPropertyChanged(nameof(AuthorizationStatusText));
        OnPropertyChanged(nameof(IsUnauthorized));
        OnPropertyChanged(nameof(ShowVenuePreviewStateTag));
        OnPropertyChanged(nameof(ShowVenueOpenStatusTag));
        OnPropertyChanged(nameof(ShowVenueClosedStatusTag));

        if (_lockedLibrarySummary is null && !HasActiveVenuePreview)
        {
            VenueFloor = GetUnboundVenueFloorText();
            _lockedVenueFloor = GetUnboundVenueFloorText();
        }

        UpdateHomeHeroPresentation(DateTimeOffset.Now);
        UpdateHomeLockedVenuePresentation();
        UpdateHomeSystemInfoPresentation();
        UpdateHomeReservationCardPresentation(DateTimeOffset.Now);
    }

    partial void OnSessionSummaryChanged(string value)
    {
        UpdateHomeHeroPresentation(DateTimeOffset.Now);
    }

    partial void OnIsVenueOpenChanged(bool value)
    {
        OnPropertyChanged(nameof(IsVenueClosed));
        OnPropertyChanged(nameof(ShowVenueOpenStatusTag));
        OnPropertyChanged(nameof(ShowVenueClosedStatusTag));
    }

    partial void OnIsCurrentLockedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsCurrentPreview));
        OnPropertyChanged(nameof(HasLockedVenue));
        OnPropertyChanged(nameof(CanCancelVenuePreview));
        OnPropertyChanged(nameof(ShowVenueChangeButton));
        OnPropertyChanged(nameof(ShowVenueCancelPreviewButton));
        OnPropertyChanged(nameof(ShowVenuePreviewStateTag));
        OnPropertyChanged(nameof(CurrentVenueLockStateText));
        OnPropertyChanged(nameof(LockVenueButtonText));
    }

    partial void OnHasActiveVenuePreviewChanged(bool value)
    {
        OnPropertyChanged(nameof(CanCancelVenuePreview));
        OnPropertyChanged(nameof(ShowVenueCancelPreviewButton));
        OnPropertyChanged(nameof(ShowVenuePreviewStateTag));
        OnPropertyChanged(nameof(CanEditTomorrowConfiguration));
    }

    public async Task InitializeAsync()
    {
        if (!_themePaletteSubscribed)
        {
            _themePaletteSubscribed = true;
            _appThemeService.PaletteChanged += OnThemePaletteChanged;
            ApplyThemePalette(_appThemeService.CurrentPalette);
        }

        _activityLogService.EntryWritten += OnLogEntryWritten;
        _grabSeatCoordinator.StatusChanged += OnGrabStatusChanged;
        _globalLeakCoordinator.StatusChanged += OnGlobalLeakStatusChanged;
        _occupySeatCoordinator.StatusChanged += OnOccupyStatusChanged;
        _tomorrowReservationCoordinator.StatusChanged += OnTomorrowStatusChanged;
        ApplyGrabStatus(_grabSeatCoordinator.GetStatus());
        ApplyGlobalLeakStatus(_globalLeakCoordinator.GetStatus());
        ApplyOccupyStatus(_occupySeatCoordinator.GetStatus());
        ApplyTomorrowStatus(_tomorrowReservationCoordinator.GetStatus());

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
            await LoadProtocolTemplatesAsync();

            try
            {
                var restored = await AccountVenue.RestoreSessionAsync();
                if (restored.Session is not null)
                {
                    IsAuthorized = true;
                    SessionSummary = restored.StatusMessage;
                    ManualCookieText = restored.Cookie ?? restored.Session.Cookie;
                    UpdateSidebarSessionExpiration(restored.CookieExpirationTime, restored.Cookie);
                    await NotifySessionRestoredAsync(restored.Cookie ?? restored.Session.Cookie);
                    if (restored.ShouldLoadLibraries)
                    {
                        await LoadLibrariesAsync(restorePreferredSelection: true);
                        if (SelectedLibrary is not null)
                        {
                            await BindSelectedLibraryAsync();
                        }
                    }
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
            _ = RunStartupUpdateCheckAsync();
        }
    }

    public async Task FlushPendingScheduledStartDefaultsAsync(CancellationToken cancellationToken = default)
    {
        var pendingGrab = _pendingGrabScheduledStartDefault;
        var pendingTomorrow = _pendingTomorrowScheduledStartDefault;

        CancelPendingGrabScheduledStartDefaultAutoSave();
        CancelPendingTomorrowScheduledStartDefaultAutoSave();

        if (pendingGrab is not null)
        {
            try
            {
                await PersistGrabScheduledStartDefaultAsync(pendingGrab.Value, cancellationToken);
                if (_pendingGrabScheduledStartDefault == pendingGrab)
                {
                    _pendingGrabScheduledStartDefault = null;
                }
            }
            catch (Exception ex)
            {
                _activityLogService.Write(LogEntryKind.Warning, "Settings", $"退出前保存抢座定时时间默认值失败：{ex.Message}");
            }
        }

        if (pendingTomorrow is not null)
        {
            try
            {
                await PersistTomorrowScheduledStartDefaultAsync(pendingTomorrow.Value, cancellationToken);
                if (_pendingTomorrowScheduledStartDefault == pendingTomorrow)
                {
                    _pendingTomorrowScheduledStartDefault = null;
                }
            }
            catch (Exception ex)
            {
                _activityLogService.Write(LogEntryKind.Warning, "Settings", $"退出前保存明日预约触发时间默认值失败：{ex.Message}");
            }
        }
    }

    partial void OnSeatFilterTextChanged(string value) => _ = ApplySeatFilterAsync();

    partial void OnShowAvailableOnlyChanged(bool value) => _ = ApplySeatFilterAsync();

    partial void OnTomorrowSeatFilterTextChanged(string value) => ApplyTomorrowSeatFilter();

    partial void OnEmailAlertsEnabledChanged(bool value) => ScheduleNotificationSettingsAutoSave();

    partial void OnEmailAlertSmtpHostChanged(string value) => ScheduleNotificationSettingsAutoSave();

    partial void OnEmailAlertSmtpPortChanged(int value) => ScheduleNotificationSettingsAutoSave();

    partial void OnSelectedEmailAlertSecurityModeIndexChanged(int value) => ScheduleNotificationSettingsAutoSave();

    partial void OnEmailAlertUsernameChanged(string value) => ScheduleNotificationSettingsAutoSave();

    partial void OnEmailAlertPasswordChanged(string value) => ScheduleNotificationSettingsAutoSave();

    partial void OnEmailAlertFromAddressChanged(string value) => ScheduleNotificationSettingsAutoSave();

    partial void OnEmailAlertToAddressChanged(string value) => ScheduleNotificationSettingsAutoSave();

    partial void OnTelegramAlertsEnabledChanged(bool value) => ScheduleNotificationSettingsAutoSave();

    partial void OnTelegramAlertApiBaseUrlChanged(string value) => ScheduleNotificationSettingsAutoSave();

    partial void OnTelegramAlertBotTokenChanged(string value) => ScheduleNotificationSettingsAutoSave();

    partial void OnTelegramAlertChatIdChanged(string value) => ScheduleNotificationSettingsAutoSave();

    partial void OnBarkAlertsEnabledChanged(bool value) => ScheduleNotificationSettingsAutoSave();

    partial void OnBarkAlertServerUrlChanged(string value) => ScheduleNotificationSettingsAutoSave();

    partial void OnBarkAlertDeviceKeyChanged(string value) => ScheduleNotificationSettingsAutoSave();

    partial void OnBarkAlertSoundChanged(string value) => ScheduleNotificationSettingsAutoSave();

    partial void OnBarkAlertGroupChanged(string value) => ScheduleNotificationSettingsAutoSave();

    partial void OnLocalToastAlertsEnabledChanged(bool value) => ScheduleNotificationSettingsAutoSave();

    partial void OnLocalSoundAlertsEnabledChanged(bool value) => ScheduleNotificationSettingsAutoSave();

    partial void OnSelectedLibraryChanged(LibrarySummary? value)
    {
        if (!IsVenuePickerOpen || value is null || !IsAuthorized)
        {
            return;
        }

        _ = PreviewSelectedLibraryAsync(value);
    }

    [RelayCommand]
    private void OpenHome()
    {
        SelectedTabIndex = 0;
    }

    [RelayCommand]
    private void OpenNotificationSettings()
    {
        SelectedTabIndex = NotificationSettingsTabIndex;
    }

    [RelayCommand]
    private void OpenSystemSettings()
    {
        SelectedTabIndex = SystemSettingsTabIndex;
    }

    [RelayCommand]
    private void ShowEmailNotificationSettings()
    {
        SelectedNotificationSettingsTabIndex = 0;
    }

    [RelayCommand]
    private void ShowTelegramNotificationSettings()
    {
        SelectedNotificationSettingsTabIndex = 1;
    }

    [RelayCommand]
    private void ShowLocalNotificationSettings()
    {
        SelectedNotificationSettingsTabIndex = 3;
    }

    [RelayCommand]
    private void ShowBarkNotificationSettings()
    {
        SelectedNotificationSettingsTabIndex = 2;
    }

    [RelayCommand]
    private void ShowWindow()
    {
        _appWindowService.ShowMainWindow();
    }

    [RelayCommand]
    private void QuitApplication()
    {
        _appWindowService.QuitApplication();
    }

    [RelayCommand]
    private void OpenProjectPage()
    {
        Process.Start(new ProcessStartInfo("https://xn--e-5g8az75bbi3a.com/%E9%A1%B9%E7%9B%AE%E5%8F%91%E5%B8%83/14.html")
        {
            UseShellExecute = true
        });
    }

    [RelayCommand]
    private void OpenReleasesPage()
    {
        Process.Start(new ProcessStartInfo("https://github.com/EJianZQ/IGoLibrary/releases")
        {
            UseShellExecute = true
        });
    }

    [RelayCommand]
    private async Task GetCookieFromLinkAsync()
    {
        await ParseCookieFromLinkAsync(QrLinkText, notifyOnInvalidLink: true);
    }

    public async Task<bool> TryAutoParseClipboardLinkAsync(string clipboardText)
    {
        QrLinkText = clipboardText.Trim();
        return await ParseCookieFromLinkAsync(QrLinkText, notifyOnInvalidLink: false);
    }

    private async Task<bool> ParseCookieFromLinkAsync(string? linkText, bool notifyOnInvalidLink)
    {
        string? reservedCode = null;
        var shouldMarkCodeAsProcessed = false;
        try
        {
            if (!CodeLinkParser.TryExtractCode(linkText, out var code))
            {
                if (notifyOnInvalidLink)
                {
                    await _notificationService.ShowWarningAsync("链接无效", "未能从链接中提取 32 位 code");
                }

                return false;
            }

            if (!TryReserveAuthCode(code))
            {
                _activityLogService.Write(LogEntryKind.Info, "Auth", $"授权 code 已处理，跳过重复解析：{code}");
                if (notifyOnInvalidLink)
                {
                    await _notificationService.ShowInfoAsync("链接已处理", "该授权链接已处理过一次，如需重试，请重新从微信获取新的授权链接");
                }

                return false;
            }

            reservedCode = code;
            var result = await AccountVenue.AuthenticateFromCodeAsync(code, RememberSession);
            shouldMarkCodeAsProcessed = true;
            ManualCookieText = result.Cookie ?? string.Empty;
            SessionSummary = result.StatusMessage;
            SelectedTabIndex = 1;
            await _notificationService.ShowSuccessAsync("已成功获取 Cookie", BuildCookieFetchedMessage(result.Cookie ?? string.Empty));

            if (result.Session is not null)
            {
                IsAuthorized = true;
                SessionSummary = result.StatusMessage;
                UpdateSidebarSessionExpiration(result.CookieExpirationTime, result.Session.Cookie);
                if (result.ShouldLoadLibraries)
                {
                    _globalLeakSelectionRestoredForCurrentSession = false;
                    await LoadLibrariesAsync(restorePreferredSelection: false);
                }
            }
            else if (!string.IsNullOrWhiteSpace(result.AuthenticationFailureMessage))
            {
                _activityLogService.Write(LogEntryKind.Warning, "Auth", $"Cookie 已获取，但自动验证失败：{result.AuthenticationFailureMessage}");
                await _notificationService.ShowInfoAsync(
                    "已获取 Cookie",
                    $"Cookie 已填入文本框，但自动验证失败：{result.AuthenticationFailureMessage}");
            }

            return true;
        }
        catch (Exception ex)
        {
            _activityLogService.Write(LogEntryKind.Error, "Auth", $"通过链接获取 Cookie 失败：{ex.Message}");
            await _notificationService.ShowWarningAsync("获取 Cookie 失败", ex.Message);
            return false;
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(reservedCode))
            {
                CompleteAuthCodeReservation(reservedCode, shouldMarkCodeAsProcessed);
            }
        }
    }

    [RelayCommand]
    private async Task ValidateManualCookieAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(ManualCookieText))
            {
                await _notificationService.ShowWarningAsync("Cookie 为空", "请先输入 Cookie");
                return;
            }

            var result = await AccountVenue.AuthenticateFromCookieAsync(ManualCookieText, RememberSession);
            var session = result.Session ?? throw new InvalidOperationException("Cookie 验证成功但未返回会话。");
            IsAuthorized = true;
            SessionSummary = result.StatusMessage;
            UpdateSidebarSessionExpiration(result.CookieExpirationTime, session.Cookie);
            if (result.ShouldLoadLibraries)
            {
                _globalLeakSelectionRestoredForCurrentSession = false;
                await LoadLibrariesAsync(restorePreferredSelection: false);
            }
            SelectedTabIndex = 1;
        }
        catch (Exception ex)
        {
            _activityLogService.Write(LogEntryKind.Error, "Auth", $"手动验证 Cookie 失败：{ex.Message}");
            await _notificationService.ShowWarningAsync("验证 Cookie 失败", ex.Message);
        }
    }

    [RelayCommand]
    private async Task RestoreSessionAsync()
    {
        try
        {
            var result = await AccountVenue.RestoreSessionAsync();
            if (result.Session is null)
            {
                await _notificationService.ShowInfoAsync("没有会话", "本地没有可恢复的会话");
                return;
            }

            IsAuthorized = true;
            SessionSummary = result.StatusMessage;
            ManualCookieText = result.Cookie ?? result.Session.Cookie;
            UpdateSidebarSessionExpiration(result.CookieExpirationTime, result.Cookie ?? result.Session.Cookie);
            await NotifySessionRestoredAsync(result.Cookie ?? result.Session.Cookie);
            if (result.ShouldLoadLibraries)
            {
                _globalLeakSelectionRestoredForCurrentSession = false;
                await LoadLibrariesAsync(restorePreferredSelection: false);
            }
        }
        catch (Exception ex)
        {
            _activityLogService.Write(LogEntryKind.Error, "Auth", $"恢复会话失败：{ex.Message}");
            await _notificationService.ShowWarningAsync("恢复会话失败", ex.Message);
        }
    }

    [RelayCommand]
    private async Task SignOutAsync()
    {
        await AccountVenue.SignOutAsync();
        await ClearStoredLibrarySelectionAsync();
        CancelFiltering();
        IsGrabSeatSelectionOverlayOpen = false;
        IsTomorrowSeatSelectionOverlayOpen = false;
        IsGlobalLeakLibraryPickerOpen = false;
        _draftSelectedSeatKeys.Clear();
        _draftTomorrowSeatKey = null;
        _draftGlobalLeakLibraryIds.Clear();
        _committedSelectedSeatKeys.Clear();
        _committedGlobalLeakLibraryIds.Clear();
        AvailableLibraries.Clear();
        ClearGlobalLeakLibraries();
        RefreshSelectedGlobalLeakLibrariesPresentation();
        _allSeats.Clear();
        ClearTomorrowSeats();
        VisibleSeats.Clear();
        OnPropertyChanged(nameof(HasSeatLayout));
        OnPropertyChanged(nameof(HasNoSeatLayout));
        OnPropertyChanged(nameof(ShowSeatFilterEmptyState));
        OnPropertyChanged(nameof(HasTomorrowSeatLayout));
        OnPropertyChanged(nameof(HasNoTomorrowSeatLayout));
        OnPropertyChanged(nameof(HasVisibleTomorrowSeatResults));
        OnPropertyChanged(nameof(ShowTomorrowSeatFilterEmptyState));
        OnPropertyChanged(nameof(DraftSelectedTomorrowSeatSummaryText));
        OnPropertyChanged(nameof(DraftGlobalLeakLibrarySummaryText));
        RefreshSelectedSeatsPresentation();
        UpdateDraftSelectionPresentation();
        VisibleSeatResultCount = 0;
        SelectedTomorrowSeat = null;
        SelectedLibrary = null;
        IsAuthorized = false;
        _globalLeakSelectionRestoredForCurrentSession = false;
        SessionSummary = "未登录";
        ClearSidebarSessionExpiration();
        LibrarySummary = "未绑定场馆";
        BoundLibraryTitle = "当前绑定：未锁定目标场馆";
        BoundAvailableSeatsText = "--";
        VenueStatusText = "未绑定";
        IsVenueOpen = false;
        VenueName = "未锁定场馆";
        VenueFloor = GetUnboundVenueFloorText();
        VenueAvailableSeatsText = "--";
        VenueOpenTimeText = "--";
        VenueCloseTimeText = "--";
        _lockedLibrarySummary = null;
        _lockedVenueStatusText = "未绑定";
        _lockedVenueOpen = false;
        _lockedVenueName = "未锁定场馆";
        _lockedVenueFloor = GetUnboundVenueFloorText();
        _lockedVenueAvailableSeatsText = "--";
        _lockedVenueOpenTimeText = "--";
        _lockedVenueCloseTimeText = "--";
        IsCurrentLocked = false;
        HasActiveVenuePreview = false;
        OnPropertyChanged(nameof(HasLockedVenue));
        OnPropertyChanged(nameof(ShowVenueChangeButton));
        OnPropertyChanged(nameof(ShowVenueCancelPreviewButton));
        OnPropertyChanged(nameof(CanCancelVenuePreview));
        UpdateHomeLockedVenuePresentation();
        UpdateHomeHeroPresentation(DateTimeOffset.Now);
        UpdateHomeSystemInfoPresentation();
        UpdateReservationPresentation(null);
        ApplyGrabStatus(CoordinatorStatus.Idle("抢座"));
        ApplyGlobalLeakStatus(CoordinatorStatus.Idle("全域捡漏"));
    }

    [RelayCommand]
    private async Task LoadLibrariesAsync()
    {
        await LoadLibrariesAsync(restorePreferredSelection: true);
    }

    private async Task LoadLibrariesAsync(bool restorePreferredSelection, int? preferredLibraryId = null)
    {
        try
        {
            var result = await AccountVenue.LoadLibrariesAsync(
                restorePreferredSelection,
                preferredLibraryId);
            AvailableLibraries.Clear();
            foreach (var library in result.Libraries)
            {
                AvailableLibraries.Add(library);
            }

            PopulateGlobalLeakLibraries(result.Libraries);
            if (!IsGlobalLeakLibraryPickerOpen)
            {
                await RestoreGlobalLeakLibrarySelectionAsync();
            }

            SelectedLibrary = result.SelectedLibrary;
        }
        catch (Exception ex)
        {
            _activityLogService.Write(LogEntryKind.Error, "Library", $"加载场馆列表失败：{ex.Message}");
            await _notificationService.ShowWarningAsync("加载场馆失败", ex.Message);
        }
    }

    [RelayCommand]
    private async Task BindSelectedLibraryAsync()
    {
        try
        {
            if (SelectedLibrary is null)
            {
                await _notificationService.ShowWarningAsync("未选择场馆", "请先选择一个场馆");
                return;
            }

            var result = await AccountVenue.BindLibraryAsync(SelectedLibrary.LibraryId);
            var preserveSelection = _lockedLibrarySummary?.LibraryId == SelectedLibrary.LibraryId;
            UpdateBoundLibraryPresentation(result.Layout);
            ApplyVenueRuleResult(result.Rule, result.RuleFailureMessage, persistLockedSnapshot: true);
            await PopulateSeatsAsync(result.Layout, preserveSelection);
            PopulateTomorrowSeats(result.Layout);
            ApplyFavoriteStates(result.Favorites.Select(x => x.SeatKey), syncSelection: false);
            if (result.Favorites.Count > 0)
            {
                await _notificationService.ShowInfoAsync("收藏已加载", $"已加载 {result.Favorites.Count} 个收藏座位");
            }

            await RefreshReservationAsync(showNotificationOnError: false);
        }
        catch (Exception ex)
        {
            _activityLogService.Write(LogEntryKind.Error, "Library", $"绑定场馆失败：{ex.Message}");
            await _notificationService.ShowWarningAsync("绑定场馆失败", ex.Message);
        }
    }

    [RelayCommand]
    private async Task RefreshSeatsAsync()
    {
        try
        {
            var result = await AccountVenue.RefreshBoundLibraryAsync();
            UpdateBoundLibraryPresentation(result.Layout);
            if (result.Rule is not null || !string.IsNullOrWhiteSpace(result.RuleFailureMessage))
            {
                ApplyVenueRuleResult(result.Rule, result.RuleFailureMessage, persistLockedSnapshot: true);
            }
            await PopulateSeatsAsync(result.Layout, preserveSelection: true);
            PopulateTomorrowSeats(result.Layout);
            ApplyFavoriteStates(result.Favorites.Select(x => x.SeatKey), syncSelection: false);
            if (result.Favorites.Count > 0)
            {
                await _notificationService.ShowInfoAsync("收藏已加载", $"已加载 {result.Favorites.Count} 个收藏座位");
            }
        }
        catch (Exception ex)
        {
            _activityLogService.Write(LogEntryKind.Error, "Library", $"刷新座位失败：{ex.Message}");
            await _notificationService.ShowWarningAsync("刷新座位失败", ex.Message);
        }
    }

    [RelayCommand]
    private async Task OpenVenuePickerAsync()
    {
        await LoadLibrariesAsync(
            restorePreferredSelection: false,
            preferredLibraryId: _lockedLibrarySummary?.LibraryId);

        IsVenuePickerOpen = true;
    }

    [RelayCommand]
    private void CloseVenuePicker()
    {
        IsVenuePickerOpen = false;
    }

    [RelayCommand]
    private async Task OpenGrabSeatSelectionOverlayAsync()
    {
        if (!CanEditGrabConfiguration)
        {
            return;
        }

        if (SelectedLibrary is null)
        {
            await _notificationService.ShowWarningAsync("未绑定场馆", "请先绑定场馆后再选择目标座位");
            return;
        }

        if (_allSeats.Count == 0)
        {
            await RefreshSeatsAsync();
        }

        if (_allSeats.Count == 0)
        {
            await _notificationService.ShowInfoAsync("暂无座位数据", "当前场馆还没有可供编辑的座位布局");
            return;
        }

        BeginGrabSeatSelectionDraft();
        IsGrabSeatSelectionOverlayOpen = true;
    }

    [RelayCommand]
    private void ConfirmGrabSeatSelection()
    {
        CommitGrabSeatSelection();
        IsGrabSeatSelectionOverlayOpen = false;
    }

    [RelayCommand]
    private void CancelGrabSeatSelection()
    {
        RestoreCommittedSeatSelection();
        IsGrabSeatSelectionOverlayOpen = false;
    }

    [RelayCommand]
    private void RemoveSelectedSeat(SeatReference? seat)
    {
        if (seat is null || !CanEditGrabConfiguration)
        {
            return;
        }

        if (!_committedSelectedSeatKeys.Remove(seat.SeatKey))
        {
            return;
        }

        RefreshSelectedSeatsPresentation();
        if (!IsGrabSeatSelectionOverlayOpen)
        {
            ApplySelectionToSeatItems(_committedSelectedSeatKeys);
        }
    }

    [RelayCommand]
    private void CancelVenuePreview()
    {
        if (_lockedLibrarySummary is null)
        {
            return;
        }

        SelectedLibrary = _lockedLibrarySummary;
        VenueStatusText = _lockedVenueStatusText;
        IsVenueOpen = _lockedVenueOpen;
        VenueName = _lockedVenueName;
        VenueFloor = _lockedVenueFloor;
        VenueAvailableSeatsText = _lockedVenueAvailableSeatsText;
        VenueOpenTimeText = _lockedVenueOpenTimeText;
        VenueCloseTimeText = _lockedVenueCloseTimeText;
        IsCurrentLocked = true;
        HasActiveVenuePreview = false;
        IsVenuePickerOpen = false;
    }

    [RelayCommand]
    private async Task SaveFavoritesAsync()
    {
        if (SelectedLibrary is null)
        {
            return;
        }

        try
        {
            var selected = _allSeats
                .Where(x => x.IsSelected)
                .Select(x => new SeatReference(x.SeatKey, x.SeatName))
                .ToList();
            await AccountVenue.SaveFavoritesAsync(SelectedLibrary.LibraryId, selected);
            ApplyFavoriteStates(selected.Select(x => x.SeatKey), syncSelection: false);
        }
        catch (Exception ex)
        {
            _activityLogService.Write(LogEntryKind.Error, "Favorite", $"保存收藏失败：{ex.Message}");
            await _notificationService.ShowWarningAsync("保存收藏失败", ex.Message);
        }
    }

    [RelayCommand]
    private async Task LoadFavoritesAsync()
    {
        if (SelectedLibrary is null)
        {
            return;
        }

        try
        {
            var favorites = await AccountVenue.GetFavoritesAsync(SelectedLibrary.LibraryId);
            ApplyFavoriteStates(favorites.Select(x => x.SeatKey), syncSelection: false);
            if (favorites.Count > 0)
            {
                await _notificationService.ShowInfoAsync("收藏已加载", $"已加载 {favorites.Count} 个收藏座位");
            }
        }
        catch (Exception ex)
        {
            _activityLogService.Write(LogEntryKind.Error, "Favorite", $"读取收藏失败：{ex.Message}");
            await _notificationService.ShowWarningAsync("读取收藏失败", ex.Message);
        }
    }

    [RelayCommand]
    private void ClearSelectedSeats()
    {
        if (!CanEditGrabConfiguration)
        {
            return;
        }

        _committedSelectedSeatKeys.Clear();
        _draftSelectedSeatKeys.Clear();
        RefreshSelectedSeatsPresentation();
        UpdateDraftSelectionPresentation();
        ApplySelectionToSeatItems(Array.Empty<string>());
    }

    [RelayCommand]
    private async Task StartGrabAsync()
    {
        if (SelectedLibrary is null)
        {
            await _notificationService.ShowWarningAsync("未绑定场馆", "请先绑定场馆");
            return;
        }

        var selectedSeats = SelectedSeats.ToList();
        if (selectedSeats.Count == 0)
        {
            await _notificationService.ShowWarningAsync("未选择座位", "请至少选中一个目标座位");
            return;
        }

        try
        {
            var mode = (GrabPollingMode)SelectedGrabPollingModeIndex;
            var scheduledStart = ParseScheduledTime();
            await PersistGrabReservationStrategyAsync();
            var plan = new GrabSeatPlan(
                SelectedLibrary.LibraryId,
                SelectedLibrary.Name,
                selectedSeats,
                mode,
                GrabPollingStrategyFactory.FromMode(mode),
                scheduledStart);
            await GrabPage.StartAsync(plan);
        }
        catch (Exception ex)
        {
            _activityLogService.Write(LogEntryKind.Error, "Grab", $"启动抢座失败：{ex.Message}");
            await _notificationService.ShowWarningAsync("启动抢座失败", ex.Message);
        }
    }

    [RelayCommand]
    private async Task StopGrabAsync()
    {
        try
        {
            await GrabPage.StopAsync();
        }
        catch (Exception ex)
        {
            _activityLogService.Write(LogEntryKind.Error, "Grab", $"停止抢座失败：{ex.Message}");
            await _notificationService.ShowWarningAsync("停止抢座失败", ex.Message);
        }
    }

    [RelayCommand]
    private async Task OpenGlobalLeakLibraryPickerAsync()
    {
        if (!CanEditGlobalLeakConfiguration)
        {
            return;
        }

        if (!IsAuthorized)
        {
            await _notificationService.ShowWarningAsync("未登录", "请先授权后再选择扫描场馆");
            return;
        }

        await LoadLibrariesAsync(restorePreferredSelection: false);

        if (GlobalLeakLibraries.Count == 0)
        {
            await _notificationService.ShowInfoAsync("暂无场馆数据", "当前账号还没有可用场馆列表");
            return;
        }

        BeginGlobalLeakLibrarySelectionDraft();
        IsGlobalLeakLibraryPickerOpen = true;
    }

    [RelayCommand]
    private async Task RefreshGlobalLeakLibrariesAsync()
    {
        if (!CanEditGlobalLeakConfiguration)
        {
            return;
        }

        await LoadLibrariesAsync(restorePreferredSelection: false);
        if (IsGlobalLeakLibraryPickerOpen)
        {
            ApplyGlobalLeakLibrarySelectionToItems(_draftGlobalLeakLibraryIds);
            UpdateDraftGlobalLeakLibrarySelectionPresentation();
        }
    }

    [RelayCommand]
    private async Task ConfirmGlobalLeakLibrariesAsync()
    {
        if (!CanEditGlobalLeakConfiguration)
        {
            return;
        }

        var selectedLibraries = CreateGlobalLeakLibrarySelectionSnapshotFromItems();
        if (!await TryPersistGlobalLeakLibrarySelectionAsync(selectedLibraries))
        {
            return;
        }

        CommitGlobalLeakLibrarySelection();
        IsGlobalLeakLibraryPickerOpen = false;
    }

    [RelayCommand]
    private void CancelGlobalLeakLibraries()
    {
        RestoreCommittedGlobalLeakLibrarySelection();
        IsGlobalLeakLibraryPickerOpen = false;
    }

    [RelayCommand]
    private async Task SelectAllGlobalLeakLibrariesAsync()
    {
        if (!CanEditGlobalLeakConfiguration)
        {
            return;
        }

        if (!IsGlobalLeakLibraryPickerOpen)
        {
            var selectedLibraries = GlobalLeakLibraries
                .Select(static library => new GlobalLeakLibraryTarget(
                    library.LibraryId,
                    library.LibraryName,
                    library.Floor))
                .ToArray();
            if (!await TryPersistGlobalLeakLibrarySelectionAsync(selectedLibraries))
            {
                return;
            }
        }

        _isSynchronizingGlobalLeakLibrarySelection = true;
        try
        {
            foreach (var library in GlobalLeakLibraries)
            {
                library.IsSelected = true;
            }
        }
        finally
        {
            _isSynchronizingGlobalLeakLibrarySelection = false;
        }

        if (IsGlobalLeakLibraryPickerOpen)
        {
            RefreshDraftGlobalLeakLibrarySelectionFromItems();
            return;
        }

        RefreshCommittedGlobalLeakLibrarySelectionFromItems();
    }

    [RelayCommand]
    private async Task ClearGlobalLeakLibrarySelectionAsync()
    {
        if (!CanEditGlobalLeakConfiguration)
        {
            return;
        }

        if (!await TryPersistGlobalLeakLibrarySelectionAsync(Array.Empty<GlobalLeakLibraryTarget>()))
        {
            return;
        }

        _draftGlobalLeakLibraryIds.Clear();
        _committedGlobalLeakLibraryIds.Clear();
        ApplyGlobalLeakLibrarySelectionToItems(Array.Empty<int>());
        RefreshSelectedGlobalLeakLibrariesPresentation();
        UpdateDraftGlobalLeakLibrarySelectionPresentation();
    }

    [RelayCommand]
    private async Task ClearDraftGlobalLeakLibrariesAsync()
    {
        if (!CanEditGlobalLeakConfiguration)
        {
            return;
        }

        if (!IsGlobalLeakLibraryPickerOpen)
        {
            await ClearGlobalLeakLibrarySelectionAsync();
            return;
        }

        _draftGlobalLeakLibraryIds.Clear();
        ApplyGlobalLeakLibrarySelectionToItems(_draftGlobalLeakLibraryIds);
        UpdateDraftGlobalLeakLibrarySelectionPresentation();
    }

    [RelayCommand]
    private async Task RemoveSelectedGlobalLeakLibraryAsync(GlobalLeakLibraryTarget? target)
    {
        if (target is null || !CanEditGlobalLeakConfiguration)
        {
            return;
        }

        if (!_committedGlobalLeakLibraryIds.Remove(target.LibraryId))
        {
            return;
        }

        var nextLibraries = CreateGlobalLeakLibrarySelectionSnapshot(_committedGlobalLeakLibraryIds);
        if (!await TryPersistGlobalLeakLibrarySelectionAsync(nextLibraries))
        {
            _committedGlobalLeakLibraryIds.Add(target.LibraryId);
            return;
        }

        RefreshSelectedGlobalLeakLibrariesPresentation();
        if (!IsGlobalLeakLibraryPickerOpen)
        {
            ApplyGlobalLeakLibrarySelectionToItems(_committedGlobalLeakLibraryIds);
        }
    }

    [RelayCommand]
    private async Task StartGlobalLeakAsync()
    {
        if (IsGlobalLeakTaskActive)
        {
            return;
        }

        var selectedLibraries = SelectedGlobalLeakLibraries.ToList();
        if (selectedLibraries.Count == 0)
        {
            await _notificationService.ShowWarningAsync("未选择场馆", "请至少选择一个全域捡漏扫描场馆");
            return;
        }

        try
        {
            var intervalSeconds = Math.Clamp(GlobalLeakScanIntervalSeconds, 1, 3600);
            GlobalLeakScanIntervalSeconds = intervalSeconds;
            var plan = new GlobalLeakPlan(
                selectedLibraries,
                TimeSpan.FromSeconds(intervalSeconds));
            await GlobalLeakPage.StartAsync(plan);
        }
        catch (Exception ex)
        {
            _activityLogService.Write(LogEntryKind.Error, "GlobalLeak", $"启动全域捡漏失败：{ex.Message}");
            await _notificationService.ShowWarningAsync("启动全域捡漏失败", ex.Message);
        }
    }

    [RelayCommand]
    private async Task StopGlobalLeakAsync()
    {
        try
        {
            await GlobalLeakPage.StopAsync();
        }
        catch (Exception ex)
        {
            _activityLogService.Write(LogEntryKind.Error, "GlobalLeak", $"停止全域捡漏失败：{ex.Message}");
            await _notificationService.ShowWarningAsync("停止全域捡漏失败", ex.Message);
        }
    }

    [RelayCommand]
    private async Task RefreshTomorrowSeatsAsync()
    {
        await RefreshSeatsAsync();
    }

    [RelayCommand]
    private async Task OpenTomorrowSeatSelectionOverlayAsync()
    {
        if (IsTomorrowTaskActive)
        {
            return;
        }

        if (HasActiveVenuePreview)
        {
            await _notificationService.ShowWarningAsync("正在预览场馆", "请先锁定当前预览场馆后再进行明日预约");
            return;
        }

        if (_lockedLibrarySummary is null)
        {
            await _notificationService.ShowWarningAsync("未绑定场馆", "请先绑定场馆后再选择明日预约目标座位");
            return;
        }

        if (_tomorrowSeats.Count == 0)
        {
            await RefreshSeatsAsync();
        }

        if (_tomorrowSeats.Count == 0)
        {
            await _notificationService.ShowInfoAsync("暂无座位数据", "当前场馆还没有可供选择的座位布局");
            return;
        }

        BeginTomorrowSeatSelectionDraft();
        IsTomorrowSeatSelectionOverlayOpen = true;
    }

    [RelayCommand]
    private void ConfirmTomorrowSeatSelection()
    {
        CommitTomorrowSeatSelection();
        IsTomorrowSeatSelectionOverlayOpen = false;
    }

    [RelayCommand]
    private void CancelTomorrowSeatSelection()
    {
        RestoreCommittedTomorrowSeatSelection();
        IsTomorrowSeatSelectionOverlayOpen = false;
    }

    [RelayCommand]
    private void ClearTomorrowSeat()
    {
        if (!CanEditTomorrowConfiguration)
        {
            return;
        }

        if (IsTomorrowSeatSelectionOverlayOpen)
        {
            _draftTomorrowSeatKey = null;
            ApplyTomorrowSeatSelection(null);
            UpdateDraftTomorrowSeatSelectionPresentation();
            return;
        }

        SelectedTomorrowSeat = null;
        ApplyTomorrowSeatSelection(null);
    }

    [RelayCommand]
    private async Task StartTomorrowReservationAsync()
    {
        await StartTomorrowReservationCoreAsync(executeImmediately: false);
    }

    [RelayCommand]
    private async Task RunTomorrowReservationNowAsync()
    {
        await StartTomorrowReservationCoreAsync(executeImmediately: true);
    }

    private async Task StartTomorrowReservationCoreAsync(bool executeImmediately)
    {
        if (IsTomorrowTaskActive)
        {
            return;
        }

        if (HasActiveVenuePreview)
        {
            await _notificationService.ShowWarningAsync("正在预览场馆", "请先锁定当前预览场馆后再进行明日预约");
            return;
        }

        var lockedLibrary = _lockedLibrarySummary;
        if (lockedLibrary is null)
        {
            await _notificationService.ShowWarningAsync("未绑定场馆", "请先绑定场馆");
            return;
        }

        var selectedSeat = SelectedTomorrowSeat;
        if (selectedSeat is null)
        {
            await _notificationService.ShowWarningAsync("未选择座位", "请先选择一个明日预约目标座位");
            return;
        }

        var selectedSeatInLayout = _tomorrowSeats.FirstOrDefault(seat =>
            string.Equals(seat.SeatKey, selectedSeat.SeatKey, StringComparison.Ordinal));
        if (selectedSeatInLayout is null)
        {
            SelectedTomorrowSeat = null;
            ApplyTomorrowSeatSelection(null);
            await _notificationService.ShowWarningAsync("座位已失效", "请重新选择明日预约目标座位");
            return;
        }

        try
        {
            var scheduledStart = ParseTomorrowScheduledTime();
            TomorrowVerificationText = executeImmediately
                ? "明日预约任务已启动，等待结果"
                : $"等待触发：{scheduledStart:HH\\:mm\\:ss}";
            var plan = new TomorrowReservationPlan(
                lockedLibrary.LibraryId,
                lockedLibrary.Name,
                new SeatReference(selectedSeatInLayout.SeatKey, selectedSeatInLayout.SeatName),
                scheduledStart,
                executeImmediately);
            await TomorrowReservationPage.StartAsync(plan);
        }
        catch (Exception ex)
        {
            _activityLogService.Write(LogEntryKind.Error, "Tomorrow", $"启动明日预约失败：{ex.Message}");
            await _notificationService.ShowWarningAsync("启动明日预约失败", ex.Message);
        }
    }

    [RelayCommand]
    private async Task StopTomorrowReservationAsync()
    {
        try
        {
            await TomorrowReservationPage.StopAsync();
        }
        catch (Exception ex)
        {
            _activityLogService.Write(LogEntryKind.Error, "Tomorrow", $"停止明日预约失败：{ex.Message}");
            await _notificationService.ShowWarningAsync("停止明日预约失败", ex.Message);
        }
    }

    [RelayCommand]
    private async Task RefreshReservationAsync()
    {
        await RefreshReservationAsync(showNotificationOnError: true);
    }

    private async Task RefreshReservationAsync(bool showNotificationOnError)
    {
        try
        {
            var result = await OccupyPage.RefreshReservationAsync();
            if (!result.HasSession)
            {
                UpdateReservationPresentation(null);
                return;
            }

            if (result.Succeeded)
            {
                UpdateReservationPresentation(result.Reservation);
            }
            else if (showNotificationOnError)
            {
                await _notificationService.ShowWarningAsync("刷新预约状态失败", result.FailureMessage ?? "接口未返回预约状态");
            }
        }
        catch (Exception ex)
        {
            _activityLogService.Write(LogEntryKind.Error, "Occupy", $"刷新预约状态失败：{ex.Message}");
            if (showNotificationOnError)
            {
                await _notificationService.ShowWarningAsync("刷新预约状态失败", ex.Message);
            }
        }
    }

    [RelayCommand]
    private async Task CancelCurrentReservationAsync()
    {
        if (_currentReservation is null || IsCancellingCurrentReservation)
        {
            return;
        }

        var reservation = _currentReservation;
        IsCancellingCurrentReservation = true;

        try
        {
            var result = await OccupyPage.CancelCurrentReservationAsync(
                reservation,
                stopOccupyFirst: IsOccupyRunning);
            if (!result.HasSession)
            {
                await _notificationService.ShowWarningAsync("未登录", "当前会话已失效，请重新授权后再操作");
                return;
            }

            if (!result.RemoteSucceeded)
            {
                _activityLogService.Write(LogEntryKind.Warning, "Occupy", $"{reservation.SeatName} 取消预约失败，接口未返回成功结果。");
                await _notificationService.ShowWarningAsync("取消预约失败", "接口未返回成功结果，请稍后重试");
                return;
            }

            _activityLogService.Write(LogEntryKind.Success, "Occupy", $"{reservation.SeatName} 已手动取消预约。");
            UpdateReservationPresentation(result.Reservation);
            await _notificationService.ShowSuccessAsync("已取消预约", $"{reservation.SeatName} 已取消预约");
        }
        catch (Exception ex)
        {
            _activityLogService.Write(LogEntryKind.Error, "Occupy", $"取消预约失败：{ex.Message}");
            await _notificationService.ShowWarningAsync("取消预约失败", ex.Message);
        }
        finally
        {
            IsCancellingCurrentReservation = false;
        }
    }

    [RelayCommand]
    private async Task StartOccupyAsync()
    {
        try
        {
            var plan = new OccupySeatPlan(
                TimeSpan.FromSeconds(Math.Max(1, ReReserveDelaySeconds)),
                (OccupyCheckIntervalMode)SelectedOccupyCheckIntervalModeIndex);
            await OccupyPage.StartAsync(plan);
        }
        catch (Exception ex)
        {
            _activityLogService.Write(LogEntryKind.Error, "Occupy", $"启动占座失败：{ex.Message}");
            await _notificationService.ShowWarningAsync("启动占座失败", ex.Message);
        }
    }

    [RelayCommand]
    private async Task StopOccupyAsync()
    {
        try
        {
            await OccupyPage.StopAsync();
        }
        catch (Exception ex)
        {
            _activityLogService.Write(LogEntryKind.Error, "Occupy", $"停止占座失败：{ex.Message}");
            await _notificationService.ShowWarningAsync("停止占座失败", ex.Message);
        }
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        CancelPendingNotificationSettingsAutoSave();
        var grabReservationStrategy = (GrabReservationStrategy)Math.Clamp(
            SelectedGrabReservationStrategyIndex,
            0,
            GrabReservationStrategies.Length - 1);
        var theme = new ThemePreferences(
            (AppThemeMode)Math.Clamp(SelectedAppThemeModeIndex, 0, ThemeModes.Length - 1),
            UseSystemAccent);
        await SystemSettings.SaveSystemSettingsAsync(new SystemSettingsSnapshot(
            NotificationsEnabled,
            MinimizeToTrayEnabled,
            TraceIntGraphQlOverridesEnabled,
            CheckUpdatesOnStartup,
            RequestTimeoutSeconds,
            NetworkMaxRetries,
            theme,
            grabReservationStrategy,
            BuildTaskEventAlertSettingsSnapshot()));
        await _appThemeService.ApplyThemeAsync(theme);
        await _notificationService.ShowSuccessAsync("设置已保存", "应用设置已写入本地数据库");
    }

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        await RunUpdateCheckAsync(UpdateCheckMode.Manual, notifyWhenNoUpdate: true);
    }

    [RelayCommand]
    private async Task TestToastAsync()
    {
        if (_notificationService is ToastNotificationService toastNotificationService)
        {
            await toastNotificationService.ShowPreviewAsync("测试通知", "这是一条用于测试界面动效与停留时间的 Toast 通知");
            return;
        }

        await _notificationService.ShowInfoAsync("测试通知", "这是一条用于测试界面动效与停留时间的 Toast 通知");
    }

    [RelayCommand]
    private async Task SendTestEmailAlertAsync()
    {
        try
        {
            CancelPendingNotificationSettingsAutoSave();
            await PersistNotificationSettingsSnapshotAsync();
            await NotificationSettings.SendTestEmailAsync(BuildTaskEventAlertSettingsSnapshot().Email);
            NotificationSettingsStatusText = $"测试邮件已发送于 {DateTime.Now:HH:mm:ss}。";
            await _notificationService.ShowSuccessAsync("测试邮件已发送", "请检查收件箱，确认当前 SMTP 配置可用");
        }
        catch (Exception ex)
        {
            NotificationSettingsStatusText = $"测试邮件发送失败：{ex.Message}";
            _activityLogService.Write(LogEntryKind.Warning, "Alert", $"发送测试邮件失败：{ex.Message}");
            await _errorDialogService.ShowErrorAsync("测试邮件发送失败", ex.GetType().Name, BuildExceptionDetails(ex));
        }
    }

    [RelayCommand]
    private async Task SendTestTelegramAlertAsync()
    {
        try
        {
            CancelPendingNotificationSettingsAutoSave();
            await PersistNotificationSettingsSnapshotAsync();
            await NotificationSettings.SendTestTelegramAsync(BuildTaskEventAlertSettingsSnapshot().Telegram);
            NotificationSettingsStatusText = $"测试 Telegram 已发送于 {DateTime.Now:HH:mm:ss}。";
            await _notificationService.ShowSuccessAsync("测试 Telegram 已发送", "请检查 Telegram，确认当前 Bot 配置可用");
        }
        catch (Exception ex)
        {
            NotificationSettingsStatusText = $"测试 Telegram 发送失败：{ex.Message}";
            _activityLogService.Write(LogEntryKind.Warning, "Alert", $"发送测试 Telegram 失败：{ex.Message}");
            await _errorDialogService.ShowErrorAsync("测试 Telegram 发送失败", ex.GetType().Name, BuildExceptionDetails(ex));
        }
    }

    [RelayCommand]
    private async Task SendTestLocalAlertAsync()
    {
        try
        {
            CancelPendingNotificationSettingsAutoSave();
            await PersistNotificationSettingsSnapshotAsync();
            await NotificationSettings.SendTestLocalAlertAsync(BuildTaskEventAlertSettingsSnapshot().Local);
            NotificationSettingsStatusText = $"测试通知已触发于 {DateTime.Now:HH:mm:ss}。";
        }
        catch (Exception ex)
        {
            NotificationSettingsStatusText = $"测试通知发送失败：{ex.Message}";
            _activityLogService.Write(LogEntryKind.Warning, "Alert", $"发送测试通知失败：{ex.Message}");
            await _notificationService.ShowWarningAsync("测试通知发送失败", ex.Message);
        }
    }

    [RelayCommand]
    private async Task SendTestBarkAlertAsync()
    {
        try
        {
            CancelPendingNotificationSettingsAutoSave();
            await PersistNotificationSettingsSnapshotAsync();
            await NotificationSettings.SendTestBarkAsync(BuildTaskEventAlertSettingsSnapshot().Bark);
            NotificationSettingsStatusText = $"测试 Bark 已发送于 {DateTime.Now:HH:mm:ss}。";
            await _notificationService.ShowSuccessAsync("测试 Bark 已发送", "请检查 Bark 客户端，确认当前 Device Key 可用");
        }
        catch (Exception ex)
        {
            NotificationSettingsStatusText = $"测试 Bark 发送失败：{ex.Message}";
            _activityLogService.Write(LogEntryKind.Warning, "Alert", $"发送测试 Bark 失败：{ex.Message}");
            await _errorDialogService.ShowErrorAsync("测试 Bark 发送失败", ex.GetType().Name, BuildExceptionDetails(ex));
        }
    }

    [RelayCommand]
    private async Task SaveProtocolOverridesAsync()
    {
        var overrides = new TraceIntGraphQlTemplateOverrides(
            GetCookieTemplateText,
            QueryLibrariesTemplateText,
            QueryLibraryLayoutTemplateText,
            QueryLibraryRuleTemplateText,
            QueryReservationInfoTemplateText,
            ReserveSeatTemplateText,
            CancelReservationTemplateText,
            TomorrowReservationQueueUrlTemplateText,
            TomorrowReservationWarmUpTemplateText,
            TomorrowReservationSaveTemplateText,
            TomorrowReservationInfoTemplateText);
        await SystemSettings.SaveProtocolOverridesAsync(overrides);
        await _notificationService.ShowSuccessAsync("协议模板已保存", "高级协议覆盖已写入数据库");
    }

    [RelayCommand]
    private async Task ResetProtocolOverridesAsync()
    {
        await SystemSettings.ResetProtocolOverridesAsync();
        await LoadProtocolTemplatesAsync();
        await _notificationService.ShowSuccessAsync("协议模板已重置", "已恢复内置默认模板");
    }

    private async Task RunStartupUpdateCheckAsync()
    {
        await RunUpdateCheckAsync(UpdateCheckMode.Automatic, notifyWhenNoUpdate: false);
    }

    private async Task RunUpdateCheckAsync(
        UpdateCheckMode mode,
        bool notifyWhenNoUpdate)
    {
        if (IsCheckingForUpdates || !(await _updateCheckGate.WaitAsync(0)))
        {
            return;
        }

        IsCheckingForUpdates = true;
        try
        {
            var result = await _updateCheckService.CheckAsync(mode);
            await HandleUpdateCheckResultAsync(result, notifyWhenNoUpdate);
        }
        catch (Exception ex)
        {
            _activityLogService.Write(LogEntryKind.Warning, "Update", $"检查更新失败：{ex.Message}");
            if (notifyWhenNoUpdate)
            {
                await _notificationService.ShowWarningAsync("检查更新失败", ex.Message);
            }
        }
        finally
        {
            IsCheckingForUpdates = false;
            _updateCheckGate.Release();
        }
    }

    private async Task HandleUpdateCheckResultAsync(
        UpdateCheckResult result,
        bool notifyWhenNoUpdate)
    {
        if (result.HasUpdate && result.Release is { } release)
        {
            _activityLogService.Write(LogEntryKind.Info, "Update", $"发现新版本：{release.TagName}");
            var dialogResult = await _updateDialogService.ShowUpdateAsync(release);
            if (dialogResult == UpdateDialogResult.OpenReleasePage)
            {
                await OpenUpdateReleasePageAsync(release.HtmlUrl);
            }
            else if (dialogResult == UpdateDialogResult.SkipVersion)
            {
                await _updateCheckService.SkipVersionAsync(release.Version);
                await _notificationService.ShowSuccessAsync(
                    "已跳过此版本",
                    $"{release.TagName} 将不再提示");
            }

            return;
        }

        if (result.Status == UpdateCheckStatus.Failed)
        {
            _activityLogService.Write(LogEntryKind.Warning, "Update", result.Message);
            if (notifyWhenNoUpdate)
            {
                await _notificationService.ShowWarningAsync("检查更新失败", result.Message);
            }

            return;
        }

        if (notifyWhenNoUpdate)
        {
            await _notificationService.ShowSuccessAsync("检查更新完成", result.Message);
        }
    }

    private async Task OpenUpdateReleasePageAsync(Uri releaseUrl)
    {
        try
        {
            await _externalLinkService.OpenAsync(releaseUrl);
        }
        catch (Exception ex)
        {
            _activityLogService.Write(LogEntryKind.Warning, "Update", $"打开 Release 页面失败：{ex.Message}");
            await _notificationService.ShowWarningAsync("打开 Release 页面失败", ex.Message);
        }
    }

    private async Task PersistGrabReservationStrategyAsync()
    {
        var strategy = (GrabReservationStrategy)Math.Clamp(
            SelectedGrabReservationStrategyIndex,
            0,
            GrabReservationStrategies.Length - 1);

        await GrabPage.SaveReservationStrategyAsync(strategy);
    }

    private async Task LoadSettingsAsync()
    {
        _isLoadingSettings = true;
        var settings = await SystemSettings.LoadSettingsAsync();
        try
        {
            var notifications = settings.Notifications;
            var ui = settings.Ui;
            var theme = ui.Theme ?? ThemePreferences.Default;
            var alertSettings = notifications.TaskEventAlerts ?? TaskEventAlertSettings.Default;

            NotificationsEnabled = notifications.AppBannerNotificationsEnabled;
            MinimizeToTrayEnabled = ui.MinimizeToTray;
            TraceIntGraphQlOverridesEnabled = settings.TraceIntProtocol.GraphQlOverridesEnabled;
            CheckUpdatesOnStartup = settings.Updates.CheckOnStartup;
            RequestTimeoutSeconds = settings.Network.TimeoutSeconds;
            NetworkMaxRetries = settings.Network.MaxRetries;
            SelectedAppThemeModeIndex = (int)theme.Mode;
            UseSystemAccent = theme.UseSystemAccent;
            SelectedGrabReservationStrategyIndex = (int)settings.Tasks.Grab.ReservationStrategy;
            _grabScheduledStartDefault = NormalizeTimeOfDay(
                settings.Tasks.Grab.DefaultScheduledStartTime,
                DefaultGrabScheduledStartTime);
            _tomorrowScheduledStartDefault = NormalizeTimeOfDay(
                settings.Tasks.TomorrowReservation.DefaultScheduledStartTime,
                DefaultTomorrowScheduledStartTime);
            ScheduledStartTime = _grabScheduledStartDefault;
            TomorrowScheduledStartTime = _tomorrowScheduledStartDefault;

            EmailAlertsEnabled = alertSettings.Email.Enabled;
            EmailAlertSmtpHost = alertSettings.Email.SmtpHost;
            EmailAlertSmtpPort = alertSettings.Email.Port;
            SelectedEmailAlertSecurityModeIndex = alertSettings.Email.SecurityMode == EmailSecurityMode.Tls ? 1 : 0;
            EmailAlertUsername = alertSettings.Email.Username;
            EmailAlertPassword = alertSettings.Email.Password;
            EmailAlertFromAddress = alertSettings.Email.FromAddress;
            EmailAlertToAddress = alertSettings.Email.ToAddress;
            TelegramAlertsEnabled = alertSettings.Telegram.Enabled;
            TelegramAlertApiBaseUrl = string.IsNullOrWhiteSpace(alertSettings.Telegram.ApiBaseUrl)
                ? TelegramAlertChannelSettings.DefaultApiBaseUrl
                : alertSettings.Telegram.ApiBaseUrl;
            TelegramAlertBotToken = alertSettings.Telegram.BotToken ?? string.Empty;
            TelegramAlertChatId = alertSettings.Telegram.ChatId ?? string.Empty;
            BarkAlertsEnabled = alertSettings.Bark.Enabled;
            BarkAlertServerUrl = string.IsNullOrWhiteSpace(alertSettings.Bark.ServerUrl)
                ? BarkAlertChannelSettings.DefaultServerUrl
                : alertSettings.Bark.ServerUrl;
            BarkAlertDeviceKey = alertSettings.Bark.DeviceKey ?? string.Empty;
            BarkAlertSound = alertSettings.Bark.Sound ?? string.Empty;
            BarkAlertGroup = string.IsNullOrWhiteSpace(alertSettings.Bark.Group)
                ? BarkAlertChannelSettings.Default.Group
                : alertSettings.Bark.Group;
            LocalToastAlertsEnabled = alertSettings.Local.PopupEnabled;
            LocalSoundAlertsEnabled = alertSettings.Local.SoundEnabled;
            NotificationSettingsStatusText = "更改后会自动保存。";

            _historicalSuccessCount = Math.Max(0, settings.Dashboard.SuccessfulReservationCount);
            _totalGuardSeconds = Math.Max(0, settings.Dashboard.TotalGuardSeconds);
            HomeHistoricalSuccessCount = _historicalSuccessCount;
            UpdateHomeDashboardPresentation();
        }
        finally
        {
            _isLoadingSettings = false;
            _notificationSettingsLoaded = true;
        }
    }

    private void PreviewThemePreferences()
    {
        if (_isLoadingSettings)
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

    private async Task LoadProtocolTemplatesAsync()
    {
        var templates = await SystemSettings.LoadProtocolTemplatesAsync();
        GetCookieTemplateText = templates.GetCookieUrlTemplate;
        QueryLibrariesTemplateText = templates.QueryLibrariesTemplate;
        QueryLibraryLayoutTemplateText = templates.QueryLibraryLayoutTemplate;
        QueryLibraryRuleTemplateText = templates.QueryLibraryRuleTemplate;
        QueryReservationInfoTemplateText = templates.QueryReservationInfoTemplate;
        ReserveSeatTemplateText = templates.ReserveSeatTemplate;
        CancelReservationTemplateText = templates.CancelReservationTemplate;
        TomorrowReservationQueueUrlTemplateText = templates.TomorrowReservationQueueUrlTemplate;
        TomorrowReservationWarmUpTemplateText = templates.TomorrowReservationWarmUpTemplate;
        TomorrowReservationSaveTemplateText = templates.TomorrowReservationSaveTemplate;
        TomorrowReservationInfoTemplateText = templates.TomorrowReservationInfoTemplate;
    }

    private async Task ClearStoredLibrarySelectionAsync()
    {
        try
        {
            await SystemSettings.ClearStoredLibrarySelectionAsync();
        }
        catch (Exception ex)
        {
            _activityLogService.Write(LogEntryKind.Warning, "Auth", $"清理上次场馆选择失败：{ex.Message}");
        }
    }

    private async Task PreviewSelectedLibraryAsync(LibrarySummary library)
    {
        try
        {
            var result = await AccountVenue.PreviewLibraryAsync(library);
            var layout = result.Layout;
            VenueStatusText = layout.IsOpen ? "开放中" : "未开放";
            IsVenueOpen = layout.IsOpen;
            VenueName = layout.Name;
            VenueFloor = layout.Floor;
            VenueAvailableSeatsText = layout.AvailableSeats.ToString();
            ApplyVenueRuleResult(result.Rule, result.RuleFailureMessage, persistLockedSnapshot: false);
            IsCurrentLocked = _lockedLibrarySummary?.LibraryId == library.LibraryId;
            HasActiveVenuePreview = !IsCurrentLocked;
            IsVenuePickerOpen = false;
        }
        catch (Exception ex)
        {
            _activityLogService.Write(LogEntryKind.Error, "Library", $"预览场馆失败：{ex.Message}");
            await _notificationService.ShowWarningAsync("预览场馆失败", ex.Message);
        }
    }

    public async Task HandleVenuePickerLibraryClickAsync(LibrarySummary library)
    {
        if (!IsAuthorized)
        {
            return;
        }

        if (SelectedLibrary?.LibraryId != library.LibraryId)
        {
            SelectedLibrary = library;
            return;
        }

        if (IsVenuePickerOpen)
        {
            await PreviewSelectedLibraryAsync(library);
        }
    }

    private void UpdateBoundLibraryPresentation(LibraryLayout? layout)
    {
        if (layout is null)
        {
            LibrarySummary = "未绑定场馆";
            BoundLibraryTitle = "当前绑定：未锁定目标场馆";
            BoundAvailableSeatsText = "--";
            SelectedTomorrowSeat = null;
            ClearTomorrowSeats();
            VenueStatusText = "未绑定";
            IsVenueOpen = false;
            VenueName = "未锁定场馆";
            VenueFloor = GetUnboundVenueFloorText();
            VenueAvailableSeatsText = "--";
            VenueOpenTimeText = "--";
            VenueCloseTimeText = "--";
            _lockedLibrarySummary = null;
            _lockedVenueStatusText = "未绑定";
            _lockedVenueOpen = false;
            _lockedVenueName = "未锁定场馆";
            _lockedVenueFloor = GetUnboundVenueFloorText();
            _lockedVenueAvailableSeatsText = "--";
            _lockedVenueOpenTimeText = "--";
            _lockedVenueCloseTimeText = "--";
            IsCurrentLocked = false;
            HasActiveVenuePreview = false;
            OnPropertyChanged(nameof(HasLockedVenue));
            OnPropertyChanged(nameof(ShowVenueChangeButton));
            OnPropertyChanged(nameof(ShowVenueCancelPreviewButton));
            OnPropertyChanged(nameof(CanCancelVenuePreview));
            UpdateHomeLockedVenuePresentation();
            UpdateHomeHeroPresentation(DateTimeOffset.Now);
            UpdateHomeSystemInfoPresentation();
            return;
        }

        LibrarySummary = $"{layout.Name} / {layout.Floor} / 余座 {layout.AvailableSeats}";
        BoundLibraryTitle = $"当前绑定：{layout.Name} / {layout.Floor}";
        BoundAvailableSeatsText = layout.AvailableSeats.ToString();
        VenueStatusText = layout.IsOpen ? "开放中" : "未开放";
        IsVenueOpen = layout.IsOpen;
        VenueName = layout.Name;
        VenueFloor = layout.Floor;
        VenueAvailableSeatsText = layout.AvailableSeats.ToString();
        IsCurrentLocked = true;
        HasActiveVenuePreview = false;
        PersistLockedVenueSnapshot();
        OnPropertyChanged(nameof(HasLockedVenue));
        OnPropertyChanged(nameof(ShowVenueChangeButton));
        OnPropertyChanged(nameof(ShowVenueCancelPreviewButton));
        OnPropertyChanged(nameof(CanCancelVenuePreview));
    }

    private async Task LoadVenueRulePresentationAsync(int libraryId, bool persistLockedSnapshot)
    {
        try
        {
            var rule = await AccountVenue.LoadLibraryRuleAsync(libraryId);
            ApplyVenueRulePresentation(rule, persistLockedSnapshot);
        }
        catch (Exception ex)
        {
            VenueOpenTimeText = "--";
            VenueCloseTimeText = "--";
            _activityLogService.Write(LogEntryKind.Warning, "Library", $"加载场馆开放时间失败：{ex.Message}");
            if (persistLockedSnapshot && IsCurrentLocked)
            {
                PersistLockedVenueSnapshot();
            }
        }
    }

    private void ApplyVenueRuleResult(
        LibraryRule? rule,
        string? failureMessage,
        bool persistLockedSnapshot)
    {
        if (!string.IsNullOrWhiteSpace(failureMessage))
        {
            _activityLogService.Write(LogEntryKind.Warning, "Library", $"加载场馆开放时间失败：{failureMessage}");
        }

        ApplyVenueRulePresentation(rule, persistLockedSnapshot);
    }

    private void ApplyVenueRulePresentation(LibraryRule? rule, bool persistLockedSnapshot)
    {
        VenueOpenTimeText = string.IsNullOrWhiteSpace(rule?.OpenTimeText) ? "--" : rule.OpenTimeText;
        VenueCloseTimeText = string.IsNullOrWhiteSpace(rule?.CloseTimeText) ? "--" : rule.CloseTimeText;

        if (persistLockedSnapshot && IsCurrentLocked)
        {
            PersistLockedVenueSnapshot();
        }
    }

    private void PersistLockedVenueSnapshot()
    {
        _lockedLibrarySummary = SelectedLibrary;
        _lockedVenueStatusText = VenueStatusText;
        _lockedVenueOpen = IsVenueOpen;
        _lockedVenueName = VenueName;
        _lockedVenueFloor = VenueFloor;
        _lockedVenueAvailableSeatsText = VenueAvailableSeatsText;
        _lockedVenueOpenTimeText = VenueOpenTimeText;
        _lockedVenueCloseTimeText = VenueCloseTimeText;
        UpdateHomeLockedVenuePresentation();
        UpdateHomeHeroPresentation(DateTimeOffset.Now);
        UpdateHomeSystemInfoPresentation();
    }

    private string GetUnboundVenueFloorText()
    {
        return IsAuthorized ? "等待绑定场馆后获取" : "等待授权并绑定场馆";
    }

    private bool TryReserveAuthCode(string code)
    {
        lock (_processedAuthCodesGate)
        {
            if (_processedAuthCodes.Contains(code) || _inFlightAuthCodes.Contains(code))
            {
                return false;
            }

            _inFlightAuthCodes.Add(code);
            return true;
        }
    }

    private void CompleteAuthCodeReservation(string code, bool markAsProcessed)
    {
        lock (_processedAuthCodesGate)
        {
            _inFlightAuthCodes.Remove(code);
            if (markAsProcessed)
            {
                _processedAuthCodes.Add(code);
            }
        }
    }

    private async Task PopulateSeatsAsync(LibraryLayout layout, bool preserveSelection)
    {
        CancelFiltering();
        var selectedKeysToRestore = preserveSelection
            ? IsGrabSeatSelectionOverlayOpen
                ? _draftSelectedSeatKeys.ToArray()
                : _committedSelectedSeatKeys.ToArray()
            : Array.Empty<string>();

        if (!preserveSelection)
        {
            _draftSelectedSeatKeys.Clear();
            _committedSelectedSeatKeys.Clear();
        }

        foreach (var seat in _allSeats)
        {
            seat.PropertyChanged -= OnSeatItemPropertyChanged;
        }

        _allSeats.Clear();
        VisibleSeats.Clear();
        _isSynchronizingSeatSelection = true;
        foreach (var seat in layout.Seats)
        {
            var item = new SeatItemViewModel(seat.SeatKey, seat.SeatName, seat.IsOccupied);
            item.PropertyChanged += OnSeatItemPropertyChanged;
            item.IsSelected = selectedKeysToRestore.Contains(item.SeatKey, StringComparer.Ordinal);
            _allSeats.Add(item);
            VisibleSeats.Add(item);
        }
        _isSynchronizingSeatSelection = false;

        await ApplySeatFilterAsync();
        OnPropertyChanged(nameof(HasSeatLayout));
        OnPropertyChanged(nameof(HasNoSeatLayout));
        OnPropertyChanged(nameof(ShowSeatFilterEmptyState));

        if (IsGrabSeatSelectionOverlayOpen)
        {
            RefreshDraftSelectionFromCurrentItems();
        }
        else if (preserveSelection)
        {
            RefreshSelectedSeatsPresentation();
        }
        else
        {
            RefreshSelectedSeatsPresentation();
            UpdateDraftSelectionPresentation();
        }
    }

    private void PopulateTomorrowSeats(LibraryLayout layout)
    {
        foreach (var seat in _tomorrowSeats)
        {
            seat.PropertyChanged -= OnTomorrowSeatItemPropertyChanged;
        }

        _tomorrowSeats.Clear();
        TomorrowVisibleSeats.Clear();

        var selectedKey = IsTomorrowSeatSelectionOverlayOpen
            ? _draftTomorrowSeatKey
            : SelectedTomorrowSeat?.SeatKey;
        _isSynchronizingTomorrowSeatSelection = true;
        try
        {
            foreach (var seat in layout.Seats.Where(seat => !string.IsNullOrWhiteSpace(seat.SeatName)))
            {
                var item = new SeatItemViewModel(seat.SeatKey, seat.SeatName, seat.IsOccupied)
                {
                    IsSelected = string.Equals(seat.SeatKey, selectedKey, StringComparison.Ordinal)
                };
                item.PropertyChanged += OnTomorrowSeatItemPropertyChanged;
                _tomorrowSeats.Add(item);
                TomorrowVisibleSeats.Add(item);
            }
        }
        finally
        {
            _isSynchronizingTomorrowSeatSelection = false;
        }

        if (IsTomorrowSeatSelectionOverlayOpen)
        {
            if (!string.IsNullOrWhiteSpace(_draftTomorrowSeatKey) &&
                _tomorrowSeats.All(seat => !string.Equals(seat.SeatKey, _draftTomorrowSeatKey, StringComparison.Ordinal)))
            {
                _draftTomorrowSeatKey = null;
            }
        }
        else if (SelectedTomorrowSeat is not null &&
            _tomorrowSeats.All(seat => !string.Equals(seat.SeatKey, SelectedTomorrowSeat.SeatKey, StringComparison.Ordinal)))
        {
            SelectedTomorrowSeat = null;
        }

        ApplyTomorrowSeatFilter();
        OnPropertyChanged(nameof(HasTomorrowSeatLayout));
        OnPropertyChanged(nameof(HasNoTomorrowSeatLayout));
        OnPropertyChanged(nameof(DraftSelectedTomorrowSeatSummaryText));
    }

    private void ClearTomorrowSeats()
    {
        foreach (var seat in _tomorrowSeats)
        {
            seat.PropertyChanged -= OnTomorrowSeatItemPropertyChanged;
        }

        _tomorrowSeats.Clear();
        TomorrowVisibleSeats.Clear();
        _draftTomorrowSeatKey = null;
        OnPropertyChanged(nameof(HasTomorrowSeatLayout));
        OnPropertyChanged(nameof(HasNoTomorrowSeatLayout));
        OnPropertyChanged(nameof(HasVisibleTomorrowSeatResults));
        OnPropertyChanged(nameof(ShowTomorrowSeatFilterEmptyState));
        OnPropertyChanged(nameof(DraftSelectedTomorrowSeatSummaryText));
    }

    private void PopulateGlobalLeakLibraries(IEnumerable<LibrarySummary> libraries)
    {
        var selectedIdsToRestore = IsGlobalLeakLibraryPickerOpen
            ? _draftGlobalLeakLibraryIds.ToArray()
            : _committedGlobalLeakLibraryIds.ToArray();

        ClearGlobalLeakLibraries();
        _isSynchronizingGlobalLeakLibrarySelection = true;
        try
        {
            foreach (var library in libraries)
            {
                var item = new GlobalLeakLibraryItemViewModel(library)
                {
                    IsSelected = selectedIdsToRestore.Contains(library.LibraryId)
                };
                item.PropertyChanged += OnGlobalLeakLibraryItemPropertyChanged;
                GlobalLeakLibraries.Add(item);
            }
        }
        finally
        {
            _isSynchronizingGlobalLeakLibrarySelection = false;
        }

        if (IsGlobalLeakLibraryPickerOpen)
        {
            RefreshDraftGlobalLeakLibrarySelectionFromItems();
        }
        else
        {
            RefreshSelectedGlobalLeakLibrariesPresentation();
        }

        OnPropertyChanged(nameof(HasGlobalLeakLibraries));
        OnPropertyChanged(nameof(HasNoGlobalLeakLibraries));
    }

    private void ClearGlobalLeakLibraries()
    {
        foreach (var library in GlobalLeakLibraries)
        {
            library.PropertyChanged -= OnGlobalLeakLibraryItemPropertyChanged;
        }

        GlobalLeakLibraries.Clear();
        OnPropertyChanged(nameof(HasGlobalLeakLibraries));
        OnPropertyChanged(nameof(HasNoGlobalLeakLibraries));
    }

    private async Task ApplySeatFilterAsync()
    {
        CancellationTokenSource cts;
        CancellationTokenSource? previousCts;
        lock (_filterGate)
        {
            previousCts = _filteringCts;
            _filteringCts = new CancellationTokenSource();
            cts = _filteringCts;
        }

        previousCts?.Cancel();
        previousCts?.Dispose();

        var filterText = SeatFilterText;
        var showAvailableOnly = ShowAvailableOnly;
        var snapshot = _allSeats
            .Select(seat => new SeatFilterSnapshot(seat, seat.SeatName, seat.IsOccupied))
            .ToArray();

        try
        {
            IsApplyingSeatFilter = true;
            await Task.Yield();

            var filtered = await Task.Run(() =>
            {
                cts.Token.ThrowIfCancellationRequested();

                return snapshot
                    .Select(seat => new SeatFilterResult(
                        seat.ViewModel,
                        ShouldSeatBeVisible(seat.SeatName, seat.IsOccupied, filterText, showAvailableOnly)))
                    .ToArray();
            }, cts.Token);

            if (cts.IsCancellationRequested)
            {
                return;
            }

            VisibleSeatResultCount = filtered.Count(result => result.IsVisible);
            const int batchSize = 48;
            for (var start = 0; start < filtered.Length; start += batchSize)
            {
                cts.Token.ThrowIfCancellationRequested();

                var count = Math.Min(batchSize, filtered.Length - start);
                for (var offset = 0; offset < count; offset++)
                {
                    var result = filtered[start + offset];
                    result.ViewModel.IsFilterVisible = result.IsVisible;
                }

                if (start + count < filtered.Length)
                {
                    await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _activityLogService.Write(LogEntryKind.Error, "Library", $"筛选座位失败：{ex.Message}");
        }
        finally
        {
            lock (_filterGate)
            {
                if (ReferenceEquals(_filteringCts, cts))
                {
                    _filteringCts = null;
                }
            }

            IsApplyingSeatFilter = false;
            cts.Dispose();
        }
    }

    private void OnSeatItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isSynchronizingSeatSelection || e.PropertyName != nameof(SeatItemViewModel.IsSelected))
        {
            return;
        }

        if (IsGrabSeatSelectionOverlayOpen)
        {
            RefreshDraftSelectionFromCurrentItems();
            return;
        }

        RefreshCommittedSelectionFromCurrentItems();
    }

    private void OnTomorrowSeatItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isSynchronizingTomorrowSeatSelection ||
            e.PropertyName != nameof(SeatItemViewModel.IsSelected) ||
            sender is not SeatItemViewModel changedSeat)
        {
            return;
        }

        if (!changedSeat.IsSelected)
        {
            if (IsTomorrowSeatSelectionOverlayOpen)
            {
                if (string.Equals(_draftTomorrowSeatKey, changedSeat.SeatKey, StringComparison.Ordinal))
                {
                    _draftTomorrowSeatKey = null;
                    UpdateDraftTomorrowSeatSelectionPresentation();
                }

                return;
            }

            if (SelectedTomorrowSeat?.SeatKey == changedSeat.SeatKey)
            {
                SelectedTomorrowSeat = null;
            }

            return;
        }

        if (IsTomorrowSeatSelectionOverlayOpen)
        {
            _draftTomorrowSeatKey = changedSeat.SeatKey;
            ApplyTomorrowSeatSelection(changedSeat.SeatKey);
            UpdateDraftTomorrowSeatSelectionPresentation();
            return;
        }

        SelectedTomorrowSeat = new SeatReference(changedSeat.SeatKey, changedSeat.SeatName);
        ApplyTomorrowSeatSelection(changedSeat.SeatKey);
    }

    private void OnGlobalLeakLibraryItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isSynchronizingGlobalLeakLibrarySelection ||
            e.PropertyName != nameof(GlobalLeakLibraryItemViewModel.IsSelected))
        {
            return;
        }

        if (IsGlobalLeakLibraryPickerOpen)
        {
            RefreshDraftGlobalLeakLibrarySelectionFromItems();
            return;
        }

        RefreshCommittedGlobalLeakLibrarySelectionFromItems();
        _ = PersistGlobalLeakLibrarySelectionSafelyAsync();
    }

    private void BeginTomorrowSeatSelectionDraft()
    {
        _draftTomorrowSeatKey = SelectedTomorrowSeat?.SeatKey;
        ApplyTomorrowSeatSelection(_draftTomorrowSeatKey);
        UpdateDraftTomorrowSeatSelectionPresentation();
    }

    private void CommitTomorrowSeatSelection()
    {
        var selectedSeat = _tomorrowSeats.FirstOrDefault(x =>
            string.Equals(x.SeatKey, _draftTomorrowSeatKey, StringComparison.Ordinal));
        SelectedTomorrowSeat = selectedSeat is null
            ? null
            : new SeatReference(selectedSeat.SeatKey, selectedSeat.SeatName);
        _draftTomorrowSeatKey = null;
        ApplyTomorrowSeatSelection(SelectedTomorrowSeat?.SeatKey);
        UpdateDraftTomorrowSeatSelectionPresentation();
    }

    private void RestoreCommittedTomorrowSeatSelection()
    {
        _draftTomorrowSeatKey = null;
        ApplyTomorrowSeatSelection(SelectedTomorrowSeat?.SeatKey);
        UpdateDraftTomorrowSeatSelectionPresentation();
    }

    private void ApplyTomorrowSeatSelection(string? selectedSeatKey)
    {
        _isSynchronizingTomorrowSeatSelection = true;
        try
        {
            foreach (var seat in _tomorrowSeats)
            {
                seat.IsSelected = !string.IsNullOrWhiteSpace(selectedSeatKey) &&
                                  string.Equals(seat.SeatKey, selectedSeatKey, StringComparison.Ordinal);
            }
        }
        finally
        {
            _isSynchronizingTomorrowSeatSelection = false;
        }
    }

    private void ApplyTomorrowSeatFilter()
    {
        var filterText = TomorrowSeatFilterText;
        foreach (var seat in _tomorrowSeats)
        {
            seat.IsFilterVisible = string.IsNullOrWhiteSpace(filterText) ||
                                   seat.SeatName.Contains(filterText, StringComparison.OrdinalIgnoreCase);
        }

        OnPropertyChanged(nameof(HasVisibleTomorrowSeatResults));
        OnPropertyChanged(nameof(ShowTomorrowSeatFilterEmptyState));
    }

    private void UpdateDraftTomorrowSeatSelectionPresentation()
    {
        OnPropertyChanged(nameof(DraftSelectedTomorrowSeatSummaryText));
    }

    private async Task RestoreGlobalLeakLibrarySelectionAsync()
    {
        if (_globalLeakSelectionRestoredForCurrentSession)
        {
            return;
        }

        try
        {
            var settings = await SystemSettings.LoadSettingsAsync();
            var storedLibraries = settings.Tasks.GlobalLeak.SelectedLibraries;
            if (storedLibraries.Count == 0)
            {
                _committedGlobalLeakLibraryIds.Clear();
                ApplyGlobalLeakLibrarySelectionToItems(Array.Empty<int>());
                RefreshSelectedGlobalLeakLibrariesPresentation();
                _globalLeakSelectionRestoredForCurrentSession = true;
                return;
            }

            var availableLibraryIds = GlobalLeakLibraries
                .Select(static library => library.LibraryId)
                .ToHashSet();
            var restoredIds = storedLibraries
                .Select(static library => library.LibraryId)
                .Where(availableLibraryIds.Contains)
                .Distinct()
                .ToArray();
            var skippedCount = storedLibraries
                .Select(static library => library.LibraryId)
                .Distinct()
                .Count(libraryId => !availableLibraryIds.Contains(libraryId));

            _committedGlobalLeakLibraryIds.Clear();
            foreach (var libraryId in restoredIds)
            {
                _committedGlobalLeakLibraryIds.Add(libraryId);
            }

            ApplyGlobalLeakLibrarySelectionToItems(_committedGlobalLeakLibraryIds);
            RefreshSelectedGlobalLeakLibrariesPresentation();
            UpdateDraftGlobalLeakLibrarySelectionPresentation();

            if (restoredIds.Length > 0)
            {
                _activityLogService.Write(LogEntryKind.Info, "GlobalLeak", $"已恢复 {restoredIds.Length} 个全域捡漏扫描场馆。");
            }

            if (skippedCount > 0)
            {
                _activityLogService.Write(LogEntryKind.Info, "GlobalLeak", $"有 {skippedCount} 个历史全域捡漏场馆不在当前账号场馆列表中，已跳过。");
            }

            _globalLeakSelectionRestoredForCurrentSession = true;
        }
        catch (Exception ex)
        {
            _globalLeakSelectionRestoredForCurrentSession = false;
            _activityLogService.Write(LogEntryKind.Warning, "GlobalLeak", $"恢复全域捡漏扫描场馆失败：{ex.Message}");
        }
    }

    private async Task PersistGlobalLeakLibrarySelectionAsync(CancellationToken cancellationToken = default)
    {
        await PersistGlobalLeakLibrarySelectionAsync(
            SelectedGlobalLeakLibraries.ToArray(),
            cancellationToken);
    }

    private async Task PersistGlobalLeakLibrarySelectionAsync(
        IReadOnlyList<GlobalLeakLibraryTarget> selectedLibraries,
        CancellationToken cancellationToken = default)
    {
        await SystemSettings.SaveGlobalLeakSelectedLibrariesAsync(selectedLibraries, cancellationToken);
    }

    private async Task<bool> TryPersistGlobalLeakLibrarySelectionAsync(
        IReadOnlyList<GlobalLeakLibraryTarget> selectedLibraries,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await PersistGlobalLeakLibrarySelectionAsync(selectedLibraries, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _activityLogService.Write(LogEntryKind.Warning, "GlobalLeak", $"保存全域捡漏扫描场馆失败：{ex.Message}");
            await _notificationService.ShowWarningAsync("保存扫描场馆失败", ex.Message, cancellationToken);
            return false;
        }
    }

    private async Task PersistGlobalLeakLibrarySelectionSafelyAsync()
    {
        try
        {
            await PersistGlobalLeakLibrarySelectionAsync();
        }
        catch (Exception ex)
        {
            _activityLogService.Write(LogEntryKind.Warning, "GlobalLeak", $"保存全域捡漏扫描场馆失败：{ex.Message}");
        }
    }

    private void BeginGlobalLeakLibrarySelectionDraft()
    {
        _draftGlobalLeakLibraryIds.Clear();
        foreach (var libraryId in _committedGlobalLeakLibraryIds)
        {
            _draftGlobalLeakLibraryIds.Add(libraryId);
        }

        ApplyGlobalLeakLibrarySelectionToItems(_draftGlobalLeakLibraryIds);
        UpdateDraftGlobalLeakLibrarySelectionPresentation();
    }

    private void CommitGlobalLeakLibrarySelection()
    {
        RefreshCommittedGlobalLeakLibrarySelectionFromItems();
        _draftGlobalLeakLibraryIds.Clear();
        UpdateDraftGlobalLeakLibrarySelectionPresentation();
    }

    private void RestoreCommittedGlobalLeakLibrarySelection()
    {
        _draftGlobalLeakLibraryIds.Clear();
        ApplyGlobalLeakLibrarySelectionToItems(_committedGlobalLeakLibraryIds);
        UpdateDraftGlobalLeakLibrarySelectionPresentation();
    }

    private void RefreshDraftGlobalLeakLibrarySelectionFromItems()
    {
        _draftGlobalLeakLibraryIds.Clear();
        foreach (var libraryId in GlobalLeakLibraries.Where(static library => library.IsSelected).Select(static library => library.LibraryId))
        {
            _draftGlobalLeakLibraryIds.Add(libraryId);
        }

        UpdateDraftGlobalLeakLibrarySelectionPresentation();
    }

    private void RefreshCommittedGlobalLeakLibrarySelectionFromItems()
    {
        _committedGlobalLeakLibraryIds.Clear();
        foreach (var libraryId in GlobalLeakLibraries.Where(static library => library.IsSelected).Select(static library => library.LibraryId))
        {
            _committedGlobalLeakLibraryIds.Add(libraryId);
        }

        RefreshSelectedGlobalLeakLibrariesPresentation();
    }

    private void RefreshSelectedGlobalLeakLibrariesPresentation()
    {
        SelectedGlobalLeakLibraries.Clear();
        foreach (var library in EnumerateSelectedGlobalLeakLibraries(_committedGlobalLeakLibraryIds))
        {
            SelectedGlobalLeakLibraries.Add(library);
        }

        OnPropertyChanged(nameof(SelectedGlobalLeakLibraryCount));
        OnPropertyChanged(nameof(HasSelectedGlobalLeakLibraries));
        OnPropertyChanged(nameof(HasNoSelectedGlobalLeakLibraries));
        OnPropertyChanged(nameof(SelectedGlobalLeakLibrarySummaryText));
    }

    private void UpdateDraftGlobalLeakLibrarySelectionPresentation()
    {
        OnPropertyChanged(nameof(DraftGlobalLeakLibraryCount));
        OnPropertyChanged(nameof(DraftGlobalLeakLibrarySummaryText));
    }

    private IEnumerable<GlobalLeakLibraryTarget> EnumerateSelectedGlobalLeakLibraries(IReadOnlySet<int> selectedLibraryIds)
    {
        return GlobalLeakLibraries
            .Where(library => selectedLibraryIds.Contains(library.LibraryId))
            .Select(static library => new GlobalLeakLibraryTarget(
                library.LibraryId,
                library.LibraryName,
                library.Floor));
    }

    private GlobalLeakLibraryTarget[] CreateGlobalLeakLibrarySelectionSnapshot(IEnumerable<int> selectedLibraryIds)
    {
        var selectedIds = selectedLibraryIds as IReadOnlySet<int> ?? new HashSet<int>(selectedLibraryIds);
        return EnumerateSelectedGlobalLeakLibraries(selectedIds).ToArray();
    }

    private GlobalLeakLibraryTarget[] CreateGlobalLeakLibrarySelectionSnapshotFromItems()
    {
        return GlobalLeakLibraries
            .Where(static library => library.IsSelected)
            .Select(static library => new GlobalLeakLibraryTarget(
                library.LibraryId,
                library.LibraryName,
                library.Floor))
            .ToArray();
    }

    private void ApplyGlobalLeakLibrarySelectionToItems(IEnumerable<int> selectedLibraryIds)
    {
        var selectedIds = selectedLibraryIds as IReadOnlySet<int> ?? new HashSet<int>(selectedLibraryIds);
        _isSynchronizingGlobalLeakLibrarySelection = true;
        try
        {
            foreach (var library in GlobalLeakLibraries)
            {
                library.IsSelected = selectedIds.Contains(library.LibraryId);
            }
        }
        finally
        {
            _isSynchronizingGlobalLeakLibrarySelection = false;
        }
    }

    private void BeginGrabSeatSelectionDraft()
    {
        _draftSelectedSeatKeys.Clear();
        foreach (var seatKey in _committedSelectedSeatKeys)
        {
            _draftSelectedSeatKeys.Add(seatKey);
        }

        ApplySelectionToSeatItems(_draftSelectedSeatKeys);
        UpdateDraftSelectionPresentation();
    }

    private void CommitGrabSeatSelection()
    {
        RefreshCommittedSelectionFromCurrentItems();
        _draftSelectedSeatKeys.Clear();
        UpdateDraftSelectionPresentation();
    }

    private void RestoreCommittedSeatSelection()
    {
        ApplySelectionToSeatItems(_committedSelectedSeatKeys);
        _draftSelectedSeatKeys.Clear();
        UpdateDraftSelectionPresentation();
    }

    private void RefreshDraftSelectionFromCurrentItems()
    {
        _draftSelectedSeatKeys.Clear();
        foreach (var seatKey in EnumerateSelectedSeats().Select(seat => seat.SeatKey))
        {
            _draftSelectedSeatKeys.Add(seatKey);
        }

        UpdateDraftSelectionPresentation();
    }

    private void RefreshCommittedSelectionFromCurrentItems()
    {
        _committedSelectedSeatKeys.Clear();
        foreach (var seatKey in EnumerateSelectedSeats().Select(seat => seat.SeatKey))
        {
            _committedSelectedSeatKeys.Add(seatKey);
        }

        RefreshSelectedSeatsPresentation();
    }

    private void RefreshSelectedSeatsPresentation()
    {
        SelectedSeats.Clear();
        foreach (var seat in EnumerateSelectedSeats(_committedSelectedSeatKeys))
        {
            SelectedSeats.Add(seat);
        }

        OnPropertyChanged(nameof(SelectedSeatCount));
        OnPropertyChanged(nameof(HasSelectedSeats));
        OnPropertyChanged(nameof(HasNoSelectedSeats));
        OnPropertyChanged(nameof(SelectedSeatSummaryText));
        OnPropertyChanged(nameof(SelectedSeatHintText));
    }

    private void UpdateDraftSelectionPresentation()
    {
        OnPropertyChanged(nameof(DraftSelectedSeatCount));
        OnPropertyChanged(nameof(DraftSelectedSeatSummaryText));
    }

    private void ApplySelectionToSeatItems(IEnumerable<string> selectedSeatKeys)
    {
        var seatKeySet = selectedSeatKeys.ToHashSet(StringComparer.Ordinal);
        _isSynchronizingSeatSelection = true;
        try
        {
            foreach (var seat in _allSeats)
            {
                seat.IsSelected = seatKeySet.Contains(seat.SeatKey);
            }
        }
        finally
        {
            _isSynchronizingSeatSelection = false;
        }
    }

    private IEnumerable<SeatReference> EnumerateSelectedSeats()
    {
        return EnumerateSelectedSeats(_allSeats.Where(x => x.IsSelected).Select(x => x.SeatKey));
    }

    private IEnumerable<SeatReference> EnumerateSelectedSeats(IEnumerable<string> selectedSeatKeys)
    {
        var selectedKeySet = selectedSeatKeys.ToHashSet(StringComparer.Ordinal);

        return _allSeats
            .Where(seat => selectedKeySet.Contains(seat.SeatKey))
            .OrderBy(seat => int.TryParse(seat.SeatName, out var number) ? number : int.MaxValue)
            .ThenBy(seat => seat.SeatName, StringComparer.OrdinalIgnoreCase)
            .Select(seat => new SeatReference(seat.SeatKey, seat.SeatName));
    }

    private void ApplyFavoriteStates(IEnumerable<string> favoriteSeatKeys, bool syncSelection)
    {
        var favoriteKeys = favoriteSeatKeys.ToHashSet(StringComparer.Ordinal);

        foreach (var seat in _allSeats)
        {
            var isFavorite = favoriteKeys.Contains(seat.SeatKey);
            seat.IsFavorite = isFavorite;

            if (syncSelection)
            {
                seat.IsSelected = isFavorite;
            }
        }

        if (syncSelection)
        {
            if (IsGrabSeatSelectionOverlayOpen)
            {
                RefreshDraftSelectionFromCurrentItems();
            }
            else
            {
                RefreshCommittedSelectionFromCurrentItems();
            }
        }
    }

    private TimeOnly? ParseScheduledTime()
    {
        if (!IsGrabScheduledStartEnabled)
        {
            return null;
        }

        var scheduledStart = ScheduledStartTime
            ?? throw new InvalidOperationException("抢座定时启动时间不能为空");

        return ToTimeOnly(scheduledStart, "抢座定时启动时间");
    }

    private TimeOnly ParseTomorrowScheduledTime()
    {
        var scheduledStart = TomorrowScheduledStartTime
            ?? throw new InvalidOperationException("明日预约触发时间不能为空");

        return ToTimeOnly(scheduledStart, "明日预约触发时间");
    }

    private static TimeOnly ToTimeOnly(TimeSpan value, string fieldName)
    {
        if (!IsTimeOfDay(value))
        {
            throw new InvalidOperationException($"{fieldName}必须介于 00:00:00 和 23:59:59 之间");
        }

        return TimeOnly.FromTimeSpan(value);
    }

    private static TimeSpan NormalizeTimeOfDay(TimeSpan value, TimeSpan fallback)
    {
        return IsTimeOfDay(value) ? value : fallback;
    }

    private static bool IsTimeOfDay(TimeSpan value)
    {
        return value >= TimeSpan.Zero && value < TimeSpan.FromDays(1);
    }

    private void OnLogEntryWritten(object? sender, AppLogEntry entry)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var displayMessage = TrimSentenceEnding(entry.Message);
            var line = FormatLogLine(entry, displayMessage);
            AllLogsText = AppendLine(AllLogsText, line);
            if (entry.Category is "Grab" or "Library" or "Favorite" or "Auth")
            {
                GrabLogsText = AppendLine(GrabLogsText, line);
            }

            if (entry.Category is "GlobalLeak" or "Auth")
            {
                GlobalLeakLogsText = AppendLine(GlobalLeakLogsText, line);
            }

            if (entry.Category is "Tomorrow" or "Library" or "Auth")
            {
                TomorrowLogsText = AppendLine(TomorrowLogsText, line);
            }

            if (entry.Category is "Occupy" or "Auth")
            {
                OccupyLogsText = AppendLine(OccupyLogsText, line);
                if (OccupyLogLines.Count > 0)
                {
                    OccupyLogLines[^1].IsLatest = false;
                }

                var hasSuccessSemantic = entry.Kind == LogEntryKind.Success ||
                                         entry.Message.Contains("成功", StringComparison.OrdinalIgnoreCase);
                var hasFailureSemantic = entry.Kind == LogEntryKind.Error ||
                                         entry.Message.Contains("失败", StringComparison.OrdinalIgnoreCase);

                OccupyLogLines.Add(new LogLineViewModel(
                    $"[{entry.Timestamp:HH:mm:ss}]",
                    $"{entry.Category}: {displayMessage}",
                    entry.Kind,
                    true,
                    hasSuccessSemantic,
                    hasFailureSemantic,
                    _appThemeService));

                if (entry.Category == "Occupy" &&
                    displayMessage.EndsWith("已重新预约成功", StringComparison.Ordinal))
                {
                    TryRecordOccupySuccess(entry.Timestamp);
                }

                if (entry.Category == "Occupy" && hasSuccessSemantic)
                {
                    _ = Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        try
                        {
                            await Task.Delay(TimeSpan.FromMilliseconds(350));
                            await RefreshReservationAsync(showNotificationOnError: false);
                        }
                        catch (Exception ex)
                        {
                            _activityLogService.Write(LogEntryKind.Warning, "Occupy", $"占座成功后刷新预约状态失败：{ex.Message}");
                        }
                    });
                }
            }
        });
    }

    private void OnGrabStatusChanged(object? sender, CoordinatorStatus status)
    {
        Dispatcher.UIThread.Post(() =>
        {
            ApplyGrabStatus(status);
            TryRecordGrabSuccess(status);
        });

        if (status.State == CoordinatorTaskState.Completed &&
            status.Reason == CoordinatorStatusReason.GrabSucceeded)
        {
            _ = Dispatcher.UIThread.InvokeAsync(async () =>
            {
                try
                {
                    await RefreshReservationAsync(showNotificationOnError: false);
                }
                catch (Exception ex)
                {
                    _activityLogService.Write(LogEntryKind.Warning, "Grab", $"抢座成功后刷新预约状态失败：{ex.Message}");
                }
            });
        }
    }

    private void OnGlobalLeakStatusChanged(object? sender, CoordinatorStatus status)
    {
        Dispatcher.UIThread.Post(() =>
        {
            ApplyGlobalLeakStatus(status);
            TryRecordGlobalLeakSuccess(status);
            if (status.State == CoordinatorTaskState.Completed &&
                status.Reason == CoordinatorStatusReason.GlobalLeakSucceeded)
            {
                _ = RefreshGlobalLeakSuccessReservationAsync();
            }
        });
    }

    private async Task RefreshGlobalLeakSuccessReservationAsync()
    {
        try
        {
            await RefreshReservationAsync(showNotificationOnError: false);
        }
        catch (Exception ex)
        {
            _activityLogService.Write(LogEntryKind.Warning, "GlobalLeak", $"全域捡漏成功后刷新预约状态失败：{ex.Message}");
        }
    }

    private void OnOccupyStatusChanged(object? sender, CoordinatorStatus status)
    {
        Dispatcher.UIThread.Post(() => ApplyOccupyStatus(status));
    }

    private void OnTomorrowStatusChanged(object? sender, CoordinatorStatus status)
    {
        Dispatcher.UIThread.Post(() =>
        {
            ApplyTomorrowStatus(status);
            TryRecordTomorrowSuccess(status);
        });
    }

    private void OnReservationCountdownTick(object? sender, EventArgs e)
    {
        UpdateReservationCountdown();
        UpdateGrabLastRequestText();
        UpdateGlobalLeakLastRequestText();
        UpdateTomorrowLastRequestText();
        UpdateGrabRuntimeClock();
        UpdateGlobalLeakRuntimeClock();
        RefreshSidebarSessionExpirationPresentation(DateTimeOffset.Now);
        UpdateHomeDashboardClock();
    }

    private void ApplyGrabStatus(CoordinatorStatus status)
    {
        GrabStatusText = status.Message;
        IsGrabTaskActive = IsTaskActive(status);
        GrabPollCount = status.PollCount;
        GrabRequestCount = status.RequestCount;
        _grabLastRequestAt = status.LastRequestAt;
        _grabTaskState = status.State;
        _grabStatusReason = status.Reason;
        UpdateGrabLastRequestText();
        ApplyGrabRuntime(status);
        UpdateGuardTracking(status.LastUpdatedAt ?? DateTimeOffset.Now);
        UpdateHomeHeroPresentation(DateTimeOffset.Now);
        UpdateHomeSystemInfoPresentation();
        OnPropertyChanged(nameof(GrabDashboardStatusText));
        OnPropertyChanged(nameof(GrabDashboardStatusBrush));
    }

    private void ApplyGlobalLeakStatus(CoordinatorStatus status)
    {
        GlobalLeakStatusText = status.Message;
        IsGlobalLeakTaskActive = IsTaskActive(status);
        GlobalLeakScanRoundCount = status.PollCount;
        GlobalLeakRequestCount = status.RequestCount;
        _globalLeakLastRequestAt = status.LastRequestAt;
        _globalLeakTaskState = status.State;
        _globalLeakStatusReason = status.Reason;
        UpdateGlobalLeakLastRequestText();
        ApplyGlobalLeakRuntime(status);
        UpdateGuardTracking(status.LastUpdatedAt ?? DateTimeOffset.Now);
        UpdateHomeHeroPresentation(DateTimeOffset.Now);
        UpdateHomeSystemInfoPresentation();
        OnPropertyChanged(nameof(GlobalLeakDashboardStatusText));
        OnPropertyChanged(nameof(GlobalLeakDashboardStatusBrush));
    }

    private void ApplyOccupyStatus(CoordinatorStatus status)
    {
        OccupyStatusText = status.Message;
        IsOccupyRunning = IsTaskActive(status);
        UpdateGuardTracking(status.LastUpdatedAt ?? DateTimeOffset.Now);
        UpdateHomeHeroPresentation(DateTimeOffset.Now);
        UpdateHomeSystemInfoPresentation();
    }

    private void ApplyTomorrowStatus(CoordinatorStatus status)
    {
        var message = TrimSentenceEnding(status.Message);
        TomorrowStatusText = message;
        IsTomorrowTaskActive = IsTaskActive(status);
        TomorrowRequestCount = status.RequestCount;
        _tomorrowLastRequestAt = status.LastRequestAt;
        _tomorrowTaskState = status.State;
        _tomorrowStatusReason = status.Reason;
        TomorrowVerificationText = status.State switch
        {
            CoordinatorTaskState.Idle => "尚未执行明日预约",
            CoordinatorTaskState.Starting or
                CoordinatorTaskState.Running or
                CoordinatorTaskState.Stopping or
                CoordinatorTaskState.Completed or
                CoordinatorTaskState.Failed => message,
            _ => TomorrowVerificationText
        };

        UpdateTomorrowLastRequestText();
        UpdateGuardTracking(status.LastUpdatedAt ?? DateTimeOffset.Now);
        UpdateHomeHeroPresentation(DateTimeOffset.Now);
        UpdateHomeSystemInfoPresentation();
        OnPropertyChanged(nameof(TomorrowDashboardStatusText));
        OnPropertyChanged(nameof(TomorrowDashboardStatusBrush));
    }

    private void UpdateGrabLastRequestText()
    {
        if (_grabLastRequestAt is null)
        {
            GrabLastRequestText = "无";
            return;
        }

        var elapsed = DateTimeOffset.Now - _grabLastRequestAt.Value;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        GrabLastRequestText = elapsed < TimeSpan.FromSeconds(1)
            ? "刚刚"
            : $"{Math.Max(1, (int)Math.Floor(elapsed.TotalSeconds))} 秒前";
    }

    private void UpdateGlobalLeakLastRequestText()
    {
        if (_globalLeakLastRequestAt is null)
        {
            GlobalLeakLastRequestText = "无";
            return;
        }

        var elapsed = DateTimeOffset.Now - _globalLeakLastRequestAt.Value;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        GlobalLeakLastRequestText = elapsed < TimeSpan.FromSeconds(1)
            ? "刚刚"
            : $"{Math.Max(1, (int)Math.Floor(elapsed.TotalSeconds))} 秒前";
    }

    private void UpdateTomorrowLastRequestText()
    {
        if (_tomorrowLastRequestAt is null)
        {
            TomorrowLastRequestText = "无";
            return;
        }

        var elapsed = DateTimeOffset.Now - _tomorrowLastRequestAt.Value;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        TomorrowLastRequestText = elapsed < TimeSpan.FromSeconds(1)
            ? "刚刚"
            : $"{Math.Max(1, (int)Math.Floor(elapsed.TotalSeconds))} 秒前";
    }

    private void ApplyGrabRuntime(CoordinatorStatus status)
    {
        switch (status.State)
        {
            case CoordinatorTaskState.Idle:
            case CoordinatorTaskState.Starting:
                ResetGrabRuntime();
                return;
            case CoordinatorTaskState.Running:
                _grabRuntimeStartedAt ??= status.LastUpdatedAt ?? DateTimeOffset.Now;
                UpdateGrabRuntimeText(DateTimeOffset.Now);
                return;
            case CoordinatorTaskState.Stopping:
            case CoordinatorTaskState.Completed:
            case CoordinatorTaskState.Failed:
                FreezeGrabRuntime(status.LastUpdatedAt);
                return;
        }
    }

    private void UpdateGrabRuntimeClock()
    {
        if (_grabRuntimeStartedAt is null)
        {
            return;
        }

        UpdateGrabRuntimeText(DateTimeOffset.Now);
    }

    private void FreezeGrabRuntime(DateTimeOffset? stoppedAt)
    {
        if (_grabRuntimeStartedAt is null)
        {
            return;
        }

        UpdateGrabRuntimeText(stoppedAt ?? DateTimeOffset.Now);
        _grabRuntimeStartedAt = null;
    }

    private void ResetGrabRuntime()
    {
        _grabRuntimeStartedAt = null;
        GrabRuntimeText = "00:00:00";
    }

    private void UpdateGrabRuntimeText(DateTimeOffset timestamp)
    {
        if (_grabRuntimeStartedAt is null)
        {
            GrabRuntimeText = "00:00:00";
            return;
        }

        GrabRuntimeText = FormatElapsedClock(timestamp - _grabRuntimeStartedAt.Value);
    }

    private void ApplyGlobalLeakRuntime(CoordinatorStatus status)
    {
        switch (status.State)
        {
            case CoordinatorTaskState.Idle:
            case CoordinatorTaskState.Starting:
                ResetGlobalLeakRuntime();
                return;
            case CoordinatorTaskState.Running:
                _globalLeakRuntimeStartedAt ??= status.LastUpdatedAt ?? DateTimeOffset.Now;
                UpdateGlobalLeakRuntimeText(DateTimeOffset.Now);
                return;
            case CoordinatorTaskState.Stopping:
            case CoordinatorTaskState.Completed:
            case CoordinatorTaskState.Failed:
                FreezeGlobalLeakRuntime(status.LastUpdatedAt);
                return;
        }
    }

    private void UpdateGlobalLeakRuntimeClock()
    {
        if (_globalLeakRuntimeStartedAt is null)
        {
            return;
        }

        UpdateGlobalLeakRuntimeText(DateTimeOffset.Now);
    }

    private void FreezeGlobalLeakRuntime(DateTimeOffset? stoppedAt)
    {
        if (_globalLeakRuntimeStartedAt is null)
        {
            return;
        }

        UpdateGlobalLeakRuntimeText(stoppedAt ?? DateTimeOffset.Now);
        _globalLeakRuntimeStartedAt = null;
    }

    private void ResetGlobalLeakRuntime()
    {
        _globalLeakRuntimeStartedAt = null;
        GlobalLeakRuntimeText = "00:00:00";
    }

    private void UpdateGlobalLeakRuntimeText(DateTimeOffset timestamp)
    {
        if (_globalLeakRuntimeStartedAt is null)
        {
            GlobalLeakRuntimeText = "00:00:00";
            return;
        }

        GlobalLeakRuntimeText = FormatElapsedClock(timestamp - _globalLeakRuntimeStartedAt.Value);
    }

    private void UpdateHomeDashboardPresentation()
    {
        var now = DateTimeOffset.Now;
        UpdateHomeHeroPresentation(now);
        UpdateHomeLockedVenuePresentation();
        UpdateHomeReservationCardPresentation(now);
        UpdateHomeSystemInfoPresentation();
        UpdateHomeGuardDurationPresentation(now);
    }

    private void UpdateHomeDashboardClock()
    {
        var now = DateTimeOffset.Now;
        UpdateHomeHeroPresentation(now);
        UpdateHomeReservationCardPresentation(now);
        UpdateHomeSystemInfoPresentation();
        UpdateHomeGuardDurationPresentation(now);
    }

    private void UpdateHomeHeroPresentation(DateTimeOffset now)
    {
        var localNow = now.ToLocalTime();
        HomeGreetingTitleText = BuildGreetingTitleText(localNow.Hour);
        HomeGreetingMessageText = BuildGreetingMessageText(localNow.Hour);
        HomeDateText = localNow.ToString("yyyy 年 MM 月 dd 日 dddd", DashboardCulture);
        HomeTimeText = localNow.ToString("HH:mm:ss", DashboardCulture);

        var (statusText, detailText, brush, backgroundBrush) = ResolveHomeHeroStatusPresentation();
        HomeHeroStatusText = statusText;
        HomeHeroStatusDetailText = detailText;
        HomeHeroStatusBrush = brush;
        HomeHeroStatusBackgroundBrush = backgroundBrush;
    }

    private (string StatusText, string DetailText, IBrush Brush, IBrush BackgroundBrush) ResolveHomeHeroStatusPresentation()
    {
        if (!IsAuthorized)
        {
            return ("等待授权", "完成登录与场馆绑定后即可启用全部引擎。", GrabStateWarningBrush, DashboardWarningSoftBrush);
        }

        var activeTaskCount = new[] { IsGrabTaskActive, IsGlobalLeakTaskActive, IsTomorrowTaskActive, IsOccupyRunning }.Count(static active => active);
        if (activeTaskCount >= 2)
        {
            return ("多任务协同中", "后台任务正在稳定运行，请保持程序常驻。", GrabStateRunningBrush, DashboardRunningSoftBrush);
        }

        if (IsGrabTaskActive)
        {
            return ("抢座任务运行中", "已进入实时监控阶段，请保持程序常驻。", GrabStateRunningBrush, DashboardRunningSoftBrush);
        }

        if (IsGlobalLeakTaskActive)
        {
            return ("全域捡漏运行中", "正在按轮扫描多个场馆，请保持程序常驻。", GrabStateRunningBrush, DashboardRunningSoftBrush);
        }

        if (IsTomorrowTaskActive)
        {
            return ("明日预约运行中", "已进入预约等待或提交阶段，请保持程序常驻", GrabStateRunningBrush, DashboardRunningSoftBrush);
        }

        if (IsOccupyRunning)
        {
            return ("占座守护运行中", "预约过期前会自动续占，请安心保持后台运行。", GrabStateSuccessBrush, DashboardSuccessSoftBrush);
        }

        if (HasLockedVenue)
        {
            return ("核心引擎就绪", "授权、场馆与本地配置均已准备完成。", GrabStateSuccessBrush, DashboardSuccessSoftBrush);
        }

        return ("等待绑定场馆", "当前已授权，下一步锁定一个常用场馆即可开始执行。", GrabStateWarningBrush, DashboardWarningSoftBrush);
    }

    private void UpdateHomeLockedVenuePresentation()
    {
        if (!IsAuthorized)
        {
            HomeLockedVenueTitle = "尚未锁定场馆";
            HomeLockedVenueStateText = "待授权";
            HomeLockedVenueStateBrush = GrabStateWarningBrush;
            HomeLockedVenueStateBackgroundBrush = DashboardWarningSoftBrush;
            return;
        }

        HomeLockedVenueStateText = "已授权";
        HomeLockedVenueStateBrush = GrabStateSuccessBrush;
        HomeLockedVenueStateBackgroundBrush = DashboardSuccessSoftBrush;

        if (!HasLockedVenue)
        {
            HomeLockedVenueTitle = "尚未锁定场馆";
            return;
        }

        HomeLockedVenueTitle = string.IsNullOrWhiteSpace(_lockedVenueFloor)
            ? _lockedVenueName
            : $"{_lockedVenueName} · {_lockedVenueFloor}";
    }

    private void UpdateHomeReservationCardPresentation(DateTimeOffset now)
    {
        if (_currentReservation is null)
        {
            HomeReservationSeatNumberText = "--";
            HomeReservationVenueText = "当前暂无预约记录";
            HomeReservationExpirationTimeText = "--:--:--";
            HomeReservationBadgeText = "空闲中";
            HomeReservationBadgeBrush = GrabStateIdleBrush;
            HomeReservationBadgeBackgroundBrush = DashboardNeutralSoftBrush;
            HomeReservationRemainingText = "--";
            return;
        }

        var remaining = _currentReservation.ExpirationTime - now;
        HomeReservationSeatNumberText = ExtractSeatNumberText(_currentReservation.SeatName);
        HomeReservationVenueText = _currentReservation.LibraryName;
        HomeReservationExpirationTimeText = _currentReservation.ExpirationTime.ToString("HH:mm:ss", DashboardCulture);

        if (remaining <= TimeSpan.Zero)
        {
            HomeReservationBadgeText = "待刷新";
            HomeReservationBadgeBrush = GrabStateWarningBrush;
            HomeReservationBadgeBackgroundBrush = DashboardWarningSoftBrush;
            HomeReservationRemainingText = "已到期";
            return;
        }

        HomeReservationBadgeText = "生效中";
        HomeReservationBadgeBrush = GrabStateSuccessBrush;
        HomeReservationBadgeBackgroundBrush = DashboardSuccessSoftBrush;
        HomeReservationRemainingText = FormatReservationRemaining(remaining);
    }

    private void UpdateHomeSystemInfoPresentation()
    {
        HomeEngineSummaryText = BuildHomeEngineSummaryText();
        HomeMemoryUsageText = MeasureMemoryUsageText();
    }

    private string BuildHomeEngineSummaryText()
    {
        if (!IsAuthorized)
        {
            return "等待授权";
        }

        var activeTasks = new List<string>(4);
        if (IsGrabTaskActive)
        {
            activeTasks.Add("抢座运行中");
        }

        if (IsGlobalLeakTaskActive)
        {
            activeTasks.Add("全域捡漏运行中");
        }

        if (IsTomorrowTaskActive)
        {
            activeTasks.Add("明日预约运行中");
        }

        if (IsOccupyRunning)
        {
            activeTasks.Add("占座守护运行中");
        }

        if (activeTasks.Count > 0)
        {
            return string.Join(" · ", activeTasks);
        }

        return HasLockedVenue ? "所有核心模块已就绪" : "等待绑定场馆";
    }

    private void UpdateGuardTracking(DateTimeOffset timestamp)
    {
        if (IsGrabTaskActive || IsGlobalLeakTaskActive || IsTomorrowTaskActive || IsOccupyRunning)
        {
            _guardTrackingStartedAt ??= timestamp;
            UpdateHomeGuardDurationPresentation(timestamp);
            return;
        }

        if (_guardTrackingStartedAt is null)
        {
            UpdateHomeGuardDurationPresentation(timestamp);
            return;
        }

        _totalGuardSeconds = GetCurrentTotalGuardSeconds(timestamp);
        _guardTrackingStartedAt = null;
        UpdateHomeGuardDurationPresentation(timestamp);
        _ = PersistDashboardMetricsAsync();
    }

    private void UpdateHomeGuardDurationPresentation(DateTimeOffset timestamp)
    {
        HomeTotalGuardDurationText = FormatGuardDuration(GetCurrentTotalGuardSeconds(timestamp));
    }

    private long GetCurrentTotalGuardSeconds(DateTimeOffset timestamp)
    {
        var total = _totalGuardSeconds;
        if (_guardTrackingStartedAt is null)
        {
            return Math.Max(0, total);
        }

        var elapsed = timestamp - _guardTrackingStartedAt.Value;
        if (elapsed <= TimeSpan.Zero)
        {
            return Math.Max(0, total);
        }

        return Math.Max(0, total + (long)Math.Floor(elapsed.TotalSeconds));
    }

    private void TryRecordGrabSuccess(CoordinatorStatus status)
    {
        if (status.State != CoordinatorTaskState.Completed ||
            status.Reason != CoordinatorStatusReason.GrabSucceeded)
        {
            return;
        }

        var recordedAt = status.LastUpdatedAt ?? DateTimeOffset.Now;
        if (_lastRecordedGrabSuccessAt == recordedAt)
        {
            return;
        }

        _lastRecordedGrabSuccessAt = recordedAt;
        _ = RecordSuccessfulReservationAsync();
    }

    private void TryRecordGlobalLeakSuccess(CoordinatorStatus status)
    {
        if (status.State != CoordinatorTaskState.Completed ||
            status.Reason != CoordinatorStatusReason.GlobalLeakSucceeded)
        {
            return;
        }

        var recordedAt = status.LastUpdatedAt ?? DateTimeOffset.Now;
        if (_lastRecordedGlobalLeakSuccessAt == recordedAt)
        {
            return;
        }

        _lastRecordedGlobalLeakSuccessAt = recordedAt;
        _ = RecordSuccessfulReservationAsync();
    }

    private void TryRecordOccupySuccess(DateTimeOffset timestamp)
    {
        if (_lastRecordedOccupySuccessAt == timestamp)
        {
            return;
        }

        _lastRecordedOccupySuccessAt = timestamp;
        _ = RecordSuccessfulReservationAsync();
    }

    private void TryRecordTomorrowSuccess(CoordinatorStatus status)
    {
        if (status.State != CoordinatorTaskState.Completed ||
            status.Reason != CoordinatorStatusReason.TomorrowReservationSucceeded)
        {
            return;
        }

        var recordedAt = status.LastUpdatedAt ?? DateTimeOffset.Now;
        if (_lastRecordedTomorrowSuccessAt == recordedAt)
        {
            return;
        }

        _lastRecordedTomorrowSuccessAt = recordedAt;
        _ = RecordSuccessfulReservationAsync();
    }

    private async Task RecordSuccessfulReservationAsync()
    {
        _historicalSuccessCount++;
        HomeHistoricalSuccessCount = _historicalSuccessCount;
        await PersistDashboardMetricsAsync();
    }

    private async Task PersistDashboardMetricsAsync()
    {
        try
        {
            var totalGuardSeconds = GetCurrentTotalGuardSeconds(DateTimeOffset.Now);
            await SystemSettings.SaveDashboardMetricsAsync(new DashboardMetrics(
                _historicalSuccessCount,
                totalGuardSeconds));
        }
        catch (Exception ex)
        {
            _activityLogService.Write(LogEntryKind.Warning, "Dashboard", $"保存首页统计信息失败：{ex.Message}");
        }
    }

    private static string FormatElapsedClock(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        return $"{Math.Max(0, (int)elapsed.TotalHours):D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
    }

    private static string BuildGreetingTitleText(int hour)
    {
        return hour switch
        {
            < 5 => $"夜深了，{GetSystemUserDisplayName()}",
            < 11 => $"早安，{GetSystemUserDisplayName()}",
            < 14 => $"中午好，{GetSystemUserDisplayName()}",
            < 18 => $"下午好，{GetSystemUserDisplayName()}",
            < 23 => $"晚上好，{GetSystemUserDisplayName()}",
            _ => $"夜深了，{GetSystemUserDisplayName()}"
        };
    }

    private static string BuildGreetingMessageText(int hour)
    {
        return hour switch
        {
            < 5 => "也别忘了给自己留一点休息时间。",
            < 11 => "准备好开始今天的学习了吗？",
            < 14 => "给今天的计划加把劲吧。",
            < 18 => "专注状态已经准备就绪。",
            < 23 => "把今天最后一段时间好好度过吧。",
            _ => "也别忘了给自己留一点休息时间。"
        };
    }

    private static string GetSystemUserDisplayName()
    {
        return SystemUserDisplayNameResolver.GetCurrentDisplayName();
    }

    private static string FormatGuardDuration(long totalSeconds)
    {
        if (totalSeconds <= 0)
        {
            return "0 分钟";
        }

        var duration = TimeSpan.FromSeconds(totalSeconds);
        if (duration.TotalHours >= 24)
        {
            return $"{Math.Max(1, (int)Math.Floor(duration.TotalHours))} 小时";
        }

        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours} 小时 {duration.Minutes:D2} 分";
        }

        return $"{Math.Max(1, duration.Minutes)} 分钟";
    }

    private static string FormatReservationRemaining(TimeSpan remaining)
    {
        if (remaining.TotalHours >= 1)
        {
            return remaining.ToString(@"hh\:mm\:ss", DashboardCulture);
        }

        return remaining.ToString(@"mm\:ss", DashboardCulture);
    }

    private static string ExtractSeatNumberText(string seatName)
    {
        if (string.IsNullOrWhiteSpace(seatName))
        {
            return "--";
        }

        var digits = new string(seatName.Where(char.IsDigit).ToArray());
        return string.IsNullOrWhiteSpace(digits) ? seatName : digits;
    }

    private static string BuildCookieFetchedMessage(string cookie)
    {
        if (!SessionAuthFailureDetector.TryGetCookieExpirationTime(cookie, out var expirationTime))
        {
            return "授权链接解析成功，Cookie 已填入";
        }

        return $"授权链接解析成功，Cookie 已填入{Environment.NewLine}Cookie 到期时间：{expirationTime:M月d日 HH:mm}";
    }

    private async Task NotifySessionRestoredAsync(string cookie)
    {
        if (!SessionAuthFailureDetector.TryGetCookieExpirationTime(cookie, out var expirationTime))
        {
            await _notificationService.ShowSuccessAsync("已成功恢复上次的 Cookie", "本地会话已恢复");
            return;
        }

        var message = $"Cookie 到期时间：{expirationTime:M月d日 HH:mm}";
        if (expirationTime - DateTimeOffset.Now < TimeSpan.FromMinutes(30))
        {
            await _notificationService.ShowWarningAsync("已成功恢复上次的 Cookie，注意到期时间", message);
            return;
        }

        await _notificationService.ShowSuccessAsync("已成功恢复上次的 Cookie", message);
    }

    private void UpdateSidebarSessionExpiration(string cookie)
    {
        if (!SessionAuthFailureDetector.TryGetCookieExpirationTime(cookie, out var expirationTime))
        {
            ClearSidebarSessionExpiration();
            return;
        }

        _sidebarSessionExpirationTime = expirationTime;
        HasSidebarSessionExpiration = true;
        RefreshSidebarSessionExpirationPresentation(DateTimeOffset.Now);
    }

    private void UpdateSidebarSessionExpiration(DateTimeOffset? expirationTime, string? fallbackCookie)
    {
        if (expirationTime is null)
        {
            if (string.IsNullOrWhiteSpace(fallbackCookie))
            {
                ClearSidebarSessionExpiration();
                return;
            }

            UpdateSidebarSessionExpiration(fallbackCookie);
            return;
        }

        _sidebarSessionExpirationTime = expirationTime;
        HasSidebarSessionExpiration = true;
        RefreshSidebarSessionExpirationPresentation(DateTimeOffset.Now);
    }

    private void RefreshSidebarSessionExpirationPresentation(DateTimeOffset timestamp)
    {
        if (_sidebarSessionExpirationTime is null || !HasSidebarSessionExpiration)
        {
            return;
        }

        var expirationTime = _sidebarSessionExpirationTime.Value;
        SidebarSessionExpirationText = expirationTime.ToString("M月d日 HH:mm", DashboardCulture);

        var remaining = expirationTime - timestamp;
        SidebarSessionExpirationBrush = remaining <= TimeSpan.FromMinutes(10)
            ? GrabStateFailureBrush
            : remaining <= TimeSpan.FromMinutes(30)
                ? GrabStateWarningBrush
                : _appThemeService.CurrentPalette.LogDefaultBrush;
    }

    private void ClearSidebarSessionExpiration()
    {
        _sidebarSessionExpirationTime = null;
        SidebarSessionExpirationText = string.Empty;
        SidebarSessionExpirationBrush = _appThemeService.CurrentPalette.LogDefaultBrush;
        HasSidebarSessionExpiration = false;
    }

    private static string MeasureMemoryUsageText()
    {
        using var process = Process.GetCurrentProcess();
        var memory = process.WorkingSet64 / 1024d / 1024d;
        return $"{memory:0.#} MB";
    }

    private void UpdateReservationPresentation(ReservationInfo? info)
    {
        _currentReservation = info;
        OnPropertyChanged(nameof(HasCurrentReservation));
        OnPropertyChanged(nameof(HasNoCurrentReservation));
        OnPropertyChanged(nameof(CanCancelCurrentReservation));

        if (info is null)
        {
            ReservationSummary = "暂无预约";
            ReservationHeroTitle = "暂无预约";
            ReservationExpiryText = "到期：--:--:--";
            ReservationCountdownText = "等待建立预约状态";
            UpdateHomeReservationCardPresentation(DateTimeOffset.Now);
            UpdateHomeSystemInfoPresentation();
            return;
        }

        ReservationSummary = $"{info.LibraryName} / {info.SeatName} / 到期 {info.ExpirationTime:HH:mm:ss}";
        ReservationHeroTitle = $"{info.LibraryName} · {info.SeatName}";
        UpdateReservationCountdown();
        UpdateHomeReservationCardPresentation(DateTimeOffset.Now);
        UpdateHomeSystemInfoPresentation();
    }

    private void UpdateReservationCountdown()
    {
        if (_currentReservation is null)
        {
            ReservationExpiryText = "到期：--:--:--";
            ReservationCountdownText = "等待建立预约状态";
            return;
        }

        ReservationExpiryText = $"到期：{_currentReservation.ExpirationTime:HH:mm:ss}";

        var remaining = _currentReservation.ExpirationTime - DateTimeOffset.Now;
        if (remaining <= TimeSpan.Zero)
        {
            ReservationCountdownText = "倒计时：已到期，等待刷新";
            return;
        }

        var countdown = remaining >= TimeSpan.FromHours(1)
            ? remaining.ToString(@"hh\:mm\:ss")
            : remaining.ToString(@"mm\:ss");
        ReservationCountdownText = $"倒计时：{countdown}";
    }

    private static string AppendLine(string current, string line)
    {
        var builder = new StringBuilder(current);
        builder.AppendLine(line);
        return builder.ToString();
    }

    private static string FormatLogLine(AppLogEntry entry, string message)
    {
        return $"[{entry.Timestamp:HH:mm:ss}] {entry.Category}: {message}";
    }

    private static string TrimSentenceEnding(string message)
    {
        return string.IsNullOrEmpty(message)
            ? message
            : message.TrimEnd('。', '.');
    }

    private static bool IsTaskActive(CoordinatorStatus status)
    {
        return status.State is CoordinatorTaskState.Starting
            or CoordinatorTaskState.Running
            or CoordinatorTaskState.Stopping;
    }

    private void UpdateSidebarItems()
    {
        var desiredItems = IsAuthorized ? AuthorizedSidebarItems : UnauthorizedSidebarItems;
        if (SidebarItems.Count == desiredItems.Length &&
            SidebarItems.Select(item => item.PageIndex).SequenceEqual(desiredItems.Select(item => item.PageIndex)))
        {
            SyncSelectedSidebarItem();
            return;
        }

        SidebarItems.Clear();
        foreach (var item in desiredItems)
        {
            SidebarItems.Add(item);
        }

        SyncSelectedSidebarItem();
    }

    private void SyncSelectedSidebarItem()
    {
        var target = SidebarItems.FirstOrDefault(item => item.PageIndex == SelectedTabIndex)
            ?? SidebarItems.FirstOrDefault();

        _isSynchronizingSidebarSelection = true;
        try
        {
            SelectedSidebarItem = target;
        }
        finally
        {
            _isSynchronizingSidebarSelection = false;
        }
    }

    private void CancelFiltering()
    {
        lock (_filterGate)
        {
            if (_filteringCts is null)
            {
                return;
            }

            _filteringCts.Cancel();
            _filteringCts.Dispose();
            _filteringCts = null;
        }
    }

    private static bool ShouldSeatBeVisible(
        string seatName,
        bool isOccupied,
        string filterText,
        bool showAvailableOnly)
    {
        if (showAvailableOnly && isOccupied)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(filterText))
        {
            return true;
        }

        return seatName.Contains(filterText, StringComparison.OrdinalIgnoreCase);
    }

    private TaskEventAlertSettings BuildTaskEventAlertSettingsSnapshot()
    {
        return new TaskEventAlertSettings(
            new EmailAlertChannelSettings(
                EmailAlertsEnabled,
                EmailAlertSmtpHost.Trim(),
                Math.Clamp(EmailAlertSmtpPort, 1, 65535),
                SelectedEmailAlertSecurityModeIndex == 1 ? EmailSecurityMode.Tls : EmailSecurityMode.None,
                EmailAlertUsername.Trim(),
                EmailAlertPassword,
                EmailAlertFromAddress.Trim(),
                EmailAlertToAddress.Trim()),
            new LocalDesktopAlertSettings(
                LocalToastAlertsEnabled,
                LocalSoundAlertsEnabled),
            new TelegramAlertChannelSettings(
                TelegramAlertsEnabled,
                NormalizeTelegramApiBaseUrlForSnapshot(TelegramAlertApiBaseUrl),
                (TelegramAlertBotToken ?? string.Empty).Trim(),
                (TelegramAlertChatId ?? string.Empty).Trim()),
            new BarkAlertChannelSettings(
                BarkAlertsEnabled,
                NormalizeBarkServerUrlForSnapshot(BarkAlertServerUrl),
                (BarkAlertDeviceKey ?? string.Empty).Trim(),
                (BarkAlertSound ?? string.Empty).Trim(),
                NormalizeBarkGroupForSnapshot(BarkAlertGroup)));
    }

    private static string NormalizeTelegramApiBaseUrlForSnapshot(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim().TrimEnd('/');
        return string.IsNullOrWhiteSpace(trimmed)
            ? TelegramAlertChannelSettings.DefaultApiBaseUrl
            : trimmed;
    }

    private static string NormalizeBarkServerUrlForSnapshot(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim().TrimEnd('/');
        return string.IsNullOrWhiteSpace(trimmed)
            ? BarkAlertChannelSettings.DefaultServerUrl
            : trimmed;
    }

    private static string NormalizeBarkGroupForSnapshot(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(trimmed)
            ? BarkAlertChannelSettings.Default.Group
            : trimmed;
    }

    private static string BuildExceptionDetails(Exception exception)
    {
        var builder = new StringBuilder();
        var current = exception;
        var depth = 0;

        while (current is not null)
        {
            if (depth == 0)
            {
                builder.Append(current.Message);
            }
            else
            {
                builder.AppendLine();
                builder.AppendLine();
                builder.Append($"内部异常 {depth}：{current.GetType().Name}: {current.Message}");
            }

            current = current.InnerException;
            depth++;
        }

        return builder.ToString();
    }

    private void ScheduleNotificationSettingsAutoSave()
    {
        if (_isLoadingSettings || !_notificationSettingsLoaded || !IsInitializationComplete)
        {
            return;
        }

        CancelPendingNotificationSettingsAutoSave();
        NotificationSettingsStatusText = "正在自动保存...";
        _notificationSettingsAutoSaveCts = new CancellationTokenSource();
        _ = AutoSaveNotificationSettingsAsync(_notificationSettingsAutoSaveCts.Token);
    }

    private async Task AutoSaveNotificationSettingsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(450), cancellationToken);
            await PersistNotificationSettingsSnapshotAsync(cancellationToken);
            NotificationSettingsStatusText = $"已自动保存于 {DateTime.Now:HH:mm:ss}。";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            NotificationSettingsStatusText = $"自动保存失败：{ex.Message}";
            _activityLogService.Write(LogEntryKind.Warning, "Alert", $"自动保存通知设置失败：{ex.Message}");
        }
    }

    private async Task PersistNotificationSettingsSnapshotAsync(CancellationToken cancellationToken = default)
    {
        await NotificationSettings.SaveNotificationSettingsAsync(
            BuildTaskEventAlertSettingsSnapshot(),
            cancellationToken);
    }

    private void ScheduleGrabScheduledStartDefaultAutoSave(TimeSpan value)
    {
        if (_isLoadingSettings || !IsInitializationComplete || !IsTimeOfDay(value))
        {
            return;
        }

        CancelPendingGrabScheduledStartDefaultAutoSave();
        _pendingGrabScheduledStartDefault = value;
        var cts = new CancellationTokenSource();
        _grabScheduledStartDefaultAutoSaveCts = cts;
        _ = AutoSaveGrabScheduledStartDefaultAsync(value, cts, cts.Token);
    }

    private async Task AutoSaveGrabScheduledStartDefaultAsync(
        TimeSpan value,
        CancellationTokenSource cancellationTokenSource,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(450), cancellationToken);
            await PersistGrabScheduledStartDefaultAsync(value, cancellationToken);
            ClearCompletedGrabScheduledStartDefaultAutoSave(cancellationTokenSource, value);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _activityLogService.Write(LogEntryKind.Warning, "Settings", $"自动保存抢座定时时间默认值失败：{ex.Message}");
        }
    }

    private Task PersistGrabScheduledStartDefaultAsync(
        TimeSpan value,
        CancellationToken cancellationToken = default)
    {
        return SystemSettings.SaveGrabScheduledStartDefaultAsync(value, cancellationToken);
    }

    private void ScheduleTomorrowScheduledStartDefaultAutoSave(TimeSpan value)
    {
        if (_isLoadingSettings || !IsInitializationComplete || !IsTimeOfDay(value))
        {
            return;
        }

        CancelPendingTomorrowScheduledStartDefaultAutoSave();
        _pendingTomorrowScheduledStartDefault = value;
        var cts = new CancellationTokenSource();
        _tomorrowScheduledStartDefaultAutoSaveCts = cts;
        _ = AutoSaveTomorrowScheduledStartDefaultAsync(value, cts, cts.Token);
    }

    private async Task AutoSaveTomorrowScheduledStartDefaultAsync(
        TimeSpan value,
        CancellationTokenSource cancellationTokenSource,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(450), cancellationToken);
            await PersistTomorrowScheduledStartDefaultAsync(value, cancellationToken);
            ClearCompletedTomorrowScheduledStartDefaultAutoSave(cancellationTokenSource, value);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _activityLogService.Write(LogEntryKind.Warning, "Settings", $"自动保存明日预约触发时间默认值失败：{ex.Message}");
        }
    }

    private Task PersistTomorrowScheduledStartDefaultAsync(
        TimeSpan value,
        CancellationToken cancellationToken = default)
    {
        return SystemSettings.SaveTomorrowScheduledStartDefaultAsync(value, cancellationToken);
    }

    private void CancelPendingNotificationSettingsAutoSave()
    {
        if (_notificationSettingsAutoSaveCts is null)
        {
            return;
        }

        _notificationSettingsAutoSaveCts.Cancel();
        _notificationSettingsAutoSaveCts.Dispose();
        _notificationSettingsAutoSaveCts = null;
    }

    private void CancelPendingGrabScheduledStartDefaultAutoSave()
    {
        if (_grabScheduledStartDefaultAutoSaveCts is null)
        {
            return;
        }

        _grabScheduledStartDefaultAutoSaveCts.Cancel();
        _grabScheduledStartDefaultAutoSaveCts.Dispose();
        _grabScheduledStartDefaultAutoSaveCts = null;
    }

    private void ClearCompletedGrabScheduledStartDefaultAutoSave(
        CancellationTokenSource cancellationTokenSource,
        TimeSpan value)
    {
        if (!ReferenceEquals(_grabScheduledStartDefaultAutoSaveCts, cancellationTokenSource))
        {
            return;
        }

        _grabScheduledStartDefaultAutoSaveCts.Dispose();
        _grabScheduledStartDefaultAutoSaveCts = null;
        if (_pendingGrabScheduledStartDefault == value)
        {
            _pendingGrabScheduledStartDefault = null;
        }
    }

    private void CancelPendingTomorrowScheduledStartDefaultAutoSave()
    {
        if (_tomorrowScheduledStartDefaultAutoSaveCts is null)
        {
            return;
        }

        _tomorrowScheduledStartDefaultAutoSaveCts.Cancel();
        _tomorrowScheduledStartDefaultAutoSaveCts.Dispose();
        _tomorrowScheduledStartDefaultAutoSaveCts = null;
    }

    private void ClearCompletedTomorrowScheduledStartDefaultAutoSave(
        CancellationTokenSource cancellationTokenSource,
        TimeSpan value)
    {
        if (!ReferenceEquals(_tomorrowScheduledStartDefaultAutoSaveCts, cancellationTokenSource))
        {
            return;
        }

        _tomorrowScheduledStartDefaultAutoSaveCts.Dispose();
        _tomorrowScheduledStartDefaultAutoSaveCts = null;
        if (_pendingTomorrowScheduledStartDefault == value)
        {
            _pendingTomorrowScheduledStartDefault = null;
        }
    }

    private void OnThemePaletteChanged(object? sender, AppThemePalette palette)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            ApplyThemePalette(palette);
            return;
        }

        Dispatcher.UIThread.Post(() => ApplyThemePalette(palette));
    }

    private void ApplyThemePalette(AppThemePalette palette)
    {
        GrabStateIdleBrush = palette.IdleBrush;
        GrabStateRunningBrush = palette.RunningBrush;
        GrabStateSuccessBrush = palette.SuccessBrush;
        GrabStateWarningBrush = palette.WarningBrush;
        GrabStateFailureBrush = palette.FailureBrush;
        DashboardRunningSoftBrush = palette.RunningSoftBrush;
        DashboardSuccessSoftBrush = palette.SuccessSoftBrush;
        DashboardWarningSoftBrush = palette.WarningSoftBrush;
        DashboardNeutralSoftBrush = palette.NeutralSoftBrush;
        NotificationSegmentActiveTextBrush = palette.NotificationSegmentActiveTextBrush;
        NotificationSegmentInactiveTextBrush = palette.NotificationSegmentInactiveTextBrush;

        foreach (var logLine in OccupyLogLines)
        {
            logLine.RefreshTheme();
        }

        OnPropertyChanged(nameof(EmailNotificationTabForegroundBrush));
        OnPropertyChanged(nameof(TelegramNotificationTabForegroundBrush));
        OnPropertyChanged(nameof(BarkNotificationTabForegroundBrush));
        OnPropertyChanged(nameof(LocalNotificationTabForegroundBrush));
        OnPropertyChanged(nameof(GrabDashboardStatusBrush));
        OnPropertyChanged(nameof(TomorrowDashboardStatusBrush));
        RefreshSidebarSessionExpirationPresentation(DateTimeOffset.Now);
        UpdateHomeDashboardPresentation();
    }

    private sealed record SeatFilterSnapshot(
        SeatItemViewModel ViewModel,
        string SeatName,
        bool IsOccupied);

    private sealed record SeatFilterResult(
        SeatItemViewModel ViewModel,
        bool IsVisible);
}
