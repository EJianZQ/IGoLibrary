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
using IGoLibrary.Ex.Desktop.Platform;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Helpers;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Desktop.ViewModels;

public partial class MainWindowViewModel(
    ISessionService sessionService,
    ILibraryService libraryService,
    ITraceIntApiClient apiClient,
    ISettingsService settingsService,
    IProtocolTemplateStore protocolTemplateStore,
    IGrabSeatCoordinator grabSeatCoordinator,
    IOccupySeatCoordinator occupySeatCoordinator,
    ICookieExpiryAlertService cookieExpiryAlertService,
    IActivityLogService activityLogService,
    INotificationService notificationService,
    IErrorDialogService errorDialogService,
    IAppThemeService appThemeService,
    AppWindowService appWindowService) : ViewModelBase
{
    private readonly IAppThemeService _appThemeService = appThemeService;
    private readonly ObservableCollection<SeatItemViewModel> _allSeats = [];
    private readonly object _filterGate = new();
    private readonly DispatcherTimer _reservationCountdownTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private CancellationTokenSource? _filteringCts;
    private ReservationInfo? _currentReservation;
    private bool _reservationCountdownTimerInitialized;
    private LibrarySummary? _lockedLibrarySummary;
    private string _lockedVenueStatusText = "未绑定";
    private bool _lockedVenueOpen;
    private string _lockedVenueName = "未锁定场馆";
    private string _lockedVenueFloor = "等待授权并绑定场馆";
    private string _lockedVenueAvailableSeatsText = "--";
    private string _lockedVenueOpenTimeText = "--";
    private string _lockedVenueCloseTimeText = "--";
    private static readonly CultureInfo DashboardCulture = CultureInfo.GetCultureInfo("zh-CN");
    private const int NotificationSettingsTabIndex = 4;
    private const int SystemSettingsTabIndex = 5;
    private static readonly SidebarNavigationItem HomeSidebarItem = new(
        0,
        "首页",
        "M12 3L2 12h3v8h6v-6h2v6h6v-8h3L12 3z");
    private static readonly SidebarNavigationItem AccountAndVenueSidebarItem = new(
        AccountAndVenueTabIndex,
        "账户与场馆",
        "M12 12c2.21 0 4-1.79 4-4s-1.79-4-4-4-4 1.79-4 4 1.79 4 4 4zm0 2c-2.67 0-8 1.34-8 4v2h16v-2c0-2.66-5.33-4-8-4z");
    private static readonly SidebarNavigationItem GrabSidebarItem = new(
        2,
        "抢座",
        "M7 2v11h3v9l7-12h-4l4-8z");
    private static readonly SidebarNavigationItem OccupySidebarItem = new(
        3,
        "占座",
        "M11.99 2C6.47 2 2 6.48 2 12s4.47 10 9.99 10C17.52 22 22 17.52 22 12S17.52 2 11.99 2zM12 20c-4.42 0-8-3.58-8-8s3.58-8 8-8 8 3.58 8 8-3.58 8-8 8z M12.5 7H11v6l5.25 3.15.75-1.23-4.5-2.67z");
    private static readonly SidebarNavigationItem NotificationSettingsSidebarItem = new(
        NotificationSettingsTabIndex,
        "通知设置",
        "M12 22a2.5 2.5 0 0 0 2.45-2h-4.9A2.5 2.5 0 0 0 12 22zm6-6V11a6 6 0 1 0-12 0v5l-2 2v1h16v-1l-2-2z");
    private static readonly SidebarNavigationItem SettingsSidebarItem = new(
        SystemSettingsTabIndex,
        "系统设置",
        "M19.14,12.94c0.04-0.3,0.06-0.61,0.06-0.94c0-0.32-0.02-0.64-0.06-0.94l2.03-1.58c0.18-0.14,0.23-0.41,0.12-0.61 l-1.92-3.32c-0.12-0.22-0.37-0.29-0.59-0.22l-2.39,0.96c-0.5-0.38-1.03-0.7-1.62-0.94L14.4,2.81c-0.04-0.24-0.24-0.41-0.48-0.41 h-3.84c-0.24,0-0.43,0.17-0.47,0.41L9.25,5.35C8.66,5.59,8.12,5.92,7.63,6.29L5.24,5.33c-0.22-0.08-0.47,0-0.59,0.22L2.73,8.87 C2.62,9.08,2.66,9.34,2.86,9.48l2.03,1.58C4.84,11.36,4.8,11.69,4.8,12s0.02,0.64,0.06,0.94l-2.03,1.58 c-0.18,0.14-0.23,0.41-0.12,0.61l1.92,3.32c0.12,0.22,0.37,0.29,0.59,0.22l2.39-0.96c0.5,0.38,1.03,0.7,1.62,0.94l0.36,2.54 c0.05,0.24,0.24,0.41,0.48,0.41h3.84c0.24,0,0.43-0.17,0.47-0.41l0.36-2.54c0.59-0.24,1.13-0.56,1.62-0.94l2.39,0.96 c0.22,0.08,0.47,0,0.59-0.22l1.92-3.32c0.12-0.22,0.07-0.49-0.12-0.61L19.14,12.94z M12,15.6c-1.98,0-3.6-1.62-3.6-3.6 s1.62-3.6,3.6-3.6s3.6,1.62,3.6,3.6S13.98,15.6,12,15.6z");
    private static readonly SidebarNavigationItem[] UnauthorizedSidebarItems =
    [
        HomeSidebarItem,
        AccountAndVenueSidebarItem
    ];
    private static readonly SidebarNavigationItem[] AuthorizedSidebarItems =
    [
        HomeSidebarItem,
        AccountAndVenueSidebarItem,
        GrabSidebarItem,
        OccupySidebarItem,
        NotificationSettingsSidebarItem,
        SettingsSidebarItem
    ];
    private IBrush GrabStateIdleBrush = appThemeService.CurrentPalette.IdleBrush;
    private IBrush GrabStateRunningBrush = appThemeService.CurrentPalette.RunningBrush;
    private IBrush GrabStateSuccessBrush = appThemeService.CurrentPalette.SuccessBrush;
    private IBrush GrabStateWarningBrush = appThemeService.CurrentPalette.WarningBrush;
    private IBrush GrabStateFailureBrush = appThemeService.CurrentPalette.FailureBrush;
    private IBrush DashboardRunningSoftBrush = appThemeService.CurrentPalette.RunningSoftBrush;
    private IBrush DashboardSuccessSoftBrush = appThemeService.CurrentPalette.SuccessSoftBrush;
    private IBrush DashboardWarningSoftBrush = appThemeService.CurrentPalette.WarningSoftBrush;
    private IBrush DashboardNeutralSoftBrush = appThemeService.CurrentPalette.NeutralSoftBrush;
    private static readonly IBrush NotificationSegmentActiveBrush = Brushes.White;
    private static readonly IBrush NotificationSegmentInactiveBrush = Brushes.Transparent;
    private IBrush NotificationSegmentActiveTextBrush = appThemeService.CurrentPalette.NotificationSegmentActiveTextBrush;
    private IBrush NotificationSegmentInactiveTextBrush = appThemeService.CurrentPalette.NotificationSegmentInactiveTextBrush;
    private const double NotificationSegmentControlWidthValue = 396d;
    private const double NotificationSegmentSliderWidthValue = 190d;
    private const double NotificationSegmentSliderOffsetValue = 196d;
    private readonly HashSet<string> _committedSelectedSeatKeys = new(StringComparer.Ordinal);
    private readonly HashSet<string> _draftSelectedSeatKeys = new(StringComparer.Ordinal);
    private bool _isSynchronizingSeatSelection;
    private CoordinatorTaskState _grabTaskState = CoordinatorTaskState.Idle;
    private DateTimeOffset? _grabLastRequestAt;
    private DateTimeOffset? _grabRuntimeStartedAt;
    private int _historicalSuccessCount;
    private long _totalGuardSeconds;
    private DateTimeOffset? _guardTrackingStartedAt;
    private DateTimeOffset? _lastRecordedGrabSuccessAt;
    private DateTimeOffset? _lastRecordedOccupySuccessAt;
    private bool _isSynchronizingSidebarSelection;
    private bool _isLoadingSettings;
    private bool _notificationSettingsLoaded;
    private CancellationTokenSource? _notificationSettingsAutoSaveCts;
    private bool _themePaletteSubscribed;
    private readonly object _processedAuthCodesGate = new();
    private readonly HashSet<string> _processedAuthCodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _inFlightAuthCodes = new(StringComparer.OrdinalIgnoreCase);

    public ObservableCollection<LibrarySummary> AvailableLibraries { get; } = [];

    public ObservableCollection<SidebarNavigationItem> SidebarItems { get; } =
    [
        HomeSidebarItem,
        AccountAndVenueSidebarItem
    ];

    public ObservableCollection<SeatItemViewModel> VisibleSeats { get; } = [];

    public ObservableCollection<TrackedSeat> SelectedSeats { get; } = [];

    public ObservableCollection<LogLineViewModel> OccupyLogLines { get; } = [];

    public string[] GrabModes { get; } = ["极限速度", "随机延迟", "延迟 5 秒"];

    public string[] RefreshModes { get; } = ["固定间隔 10 秒", "随机 10~20 秒"];

    public string[] GrabReservationStrategies { get; } = ["先获取列表判断状态", "直接发送预约请求"];

    public string[] EmailSecurityModes { get; } = ["无", "TLS"];

    public string[] ThemeModes { get; } = ["跟随系统", "浅色", "深色"];

    public const int AccountAndVenueTabIndex = 1;

    [ObservableProperty]
    private int selectedTabIndex;

    [ObservableProperty]
    private SidebarNavigationItem? selectedSidebarItem = HomeSidebarItem;

    public bool IsAccountAndVenuePageActive => SelectedTabIndex == AccountAndVenueTabIndex;

    partial void OnSelectedTabIndexChanged(int value)
    {
        if (!IsAuthorized && value > AccountAndVenueTabIndex)
        {
            SelectedTabIndex = AccountAndVenueTabIndex;
            return;
        }

        SyncSelectedSidebarItem();
    }

    partial void OnSelectedSidebarItemChanged(SidebarNavigationItem? value)
    {
        if (_isSynchronizingSidebarSelection || value is null)
        {
            return;
        }

        if (SelectedTabIndex != value.PageIndex)
        {
            SelectedTabIndex = value.PageIndex;
        }
    }

    [ObservableProperty]
    private string sessionSummary = "未登录";

    [ObservableProperty]
    private bool isAuthorized;

    public string AuthorizationStatusText => IsAuthorized ? "已授权" : "未授权";

    public bool IsUnauthorized => !IsAuthorized;

    public bool HasCurrentReservation => _currentReservation is not null;

    public bool HasNoCurrentReservation => !HasCurrentReservation;

    public bool CanCancelCurrentReservation => _currentReservation is not null && !IsCancellingCurrentReservation;

    [ObservableProperty]
    private bool isInitializationComplete;

    [ObservableProperty]
    private string homeGreetingTitleText = $"早安，{GetSystemUserDisplayName()}";

    [ObservableProperty]
    private string homeGreetingMessageText = "准备好开始今天的学习了吗？";

    [ObservableProperty]
    private string homeDateText = "--";

    [ObservableProperty]
    private string homeTimeText = "--:--:--";

    [ObservableProperty]
    private string homeHeroStatusText = "等待授权";

    [ObservableProperty]
    private string homeHeroStatusDetailText = "完成登录与场馆绑定后即可启用全部引擎。";

    [ObservableProperty]
    private IBrush homeHeroStatusBrush = appThemeService.CurrentPalette.IdleBrush;

    [ObservableProperty]
    private IBrush homeHeroStatusBackgroundBrush = appThemeService.CurrentPalette.NeutralSoftBrush;

    [ObservableProperty]
    private string homeLockedVenueTitle = "尚未锁定场馆";

    [ObservableProperty]
    private string homeLockedVenueStateText = "待授权";

    [ObservableProperty]
    private IBrush homeLockedVenueStateBrush = appThemeService.CurrentPalette.WarningBrush;

    [ObservableProperty]
    private IBrush homeLockedVenueStateBackgroundBrush = appThemeService.CurrentPalette.WarningSoftBrush;

    [ObservableProperty]
    private int homeHistoricalSuccessCount;

    [ObservableProperty]
    private string homeTotalGuardDurationText = "0 分钟";

    [ObservableProperty]
    private string homeEngineSummaryText = "等待授权";

    [ObservableProperty]
    private string homeMemoryUsageText = "--";

    [ObservableProperty]
    private string homeReservationSeatNumberText = "--";

    [ObservableProperty]
    private string homeReservationVenueText = "当前暂无预约记录";

    [ObservableProperty]
    private string homeReservationExpirationTimeText = "--:--:--";

    [ObservableProperty]
    private string homeReservationBadgeText = "暂无预约";

    [ObservableProperty]
    private IBrush homeReservationBadgeBrush = appThemeService.CurrentPalette.IdleBrush;

    [ObservableProperty]
    private IBrush homeReservationBadgeBackgroundBrush = appThemeService.CurrentPalette.NeutralSoftBrush;

    [ObservableProperty]
    private string homeReservationRemainingText = "--";

    [ObservableProperty]
    private bool isCancellingCurrentReservation;

    [ObservableProperty]
    private string librarySummary = "未绑定场馆";

    [ObservableProperty]
    private string boundLibraryTitle = "当前绑定：未锁定目标场馆";

    [ObservableProperty]
    private string boundAvailableSeatsText = "--";

    [ObservableProperty]
    private string venueStatusText = "未绑定";

    [ObservableProperty]
    private bool isVenueOpen;

    public bool IsVenueClosed => !IsVenueOpen;

    [ObservableProperty]
    private string venueName = "未锁定场馆";

    [ObservableProperty]
    private string venueFloor = "等待授权并绑定场馆";

    [ObservableProperty]
    private string venueAvailableSeatsText = "--";

    [ObservableProperty]
    private string venueOpenTimeText = "--";

    [ObservableProperty]
    private string venueCloseTimeText = "--";

    [ObservableProperty]
    private bool isVenuePickerOpen;

    [ObservableProperty]
    private bool isCurrentLocked;

    [ObservableProperty]
    private bool hasActiveVenuePreview;

    public bool IsCurrentPreview => !IsCurrentLocked;

    public bool HasLockedVenue => _lockedLibrarySummary is not null;

    public bool CanCancelVenuePreview => HasActiveVenuePreview && _lockedLibrarySummary is not null;

    public bool ShowVenueChangeButton => IsCurrentLocked || !HasLockedVenue;

    public bool ShowVenueCancelPreviewButton => HasActiveVenuePreview && HasLockedVenue;

    public bool ShowVenuePreviewStateTag => IsAuthorized && HasActiveVenuePreview;

    public bool ShowVenueOpenStatusTag => IsAuthorized && IsVenueOpen;

    public bool ShowVenueClosedStatusTag =>
        IsAuthorized &&
        IsVenueClosed &&
        !string.Equals(VenueStatusText, "未绑定", StringComparison.Ordinal);

    public string CurrentVenueLockStateText => IsCurrentLocked ? "🔒 当前已锁定" : "👀 预览中 (未锁定)";

    public string LockVenueButtonText => IsCurrentLocked ? "当前场馆已锁定" : "保存并锁定该场馆";

    [ObservableProperty]
    private string reservationSummary = "暂无预约";

    [ObservableProperty]
    private string reservationHeroTitle = "暂无预约";

    [ObservableProperty]
    private string reservationExpiryText = "到期：--:--:--";

    [ObservableProperty]
    private string reservationCountdownText = "等待建立预约状态";

    [ObservableProperty]
    private string qrLinkText = string.Empty;

    [ObservableProperty]
    private string manualCookieText = string.Empty;

    [ObservableProperty]
    private bool rememberSession = true;

    [ObservableProperty]
    private LibrarySummary? selectedLibrary;

    [ObservableProperty]
    private string seatFilterText = string.Empty;

    [ObservableProperty]
    private bool showAvailableOnly;

    [ObservableProperty]
    private bool isGrabSeatSelectionOverlayOpen;

    [ObservableProperty]
    private bool isApplyingSeatFilter;

    [ObservableProperty]
    private int visibleSeatResultCount;

    [ObservableProperty]
    private int selectedGrabModeIndex = 2;

    [ObservableProperty]
    private int selectedGrabReservationStrategyIndex;

    [ObservableProperty]
    private string scheduledTimeText = "00:00:00";

    [ObservableProperty]
    private string grabStatusText = "未运行";

    [ObservableProperty]
    private bool isGrabTaskActive;

    [ObservableProperty]
    private int grabPollCount;

    [ObservableProperty]
    private int grabRequestCount;

    [ObservableProperty]
    private string grabLastRequestText = "无";

    [ObservableProperty]
    private string grabRuntimeText = "00:00:00";

    [ObservableProperty]
    private string occupyStatusText = "未运行";

    [ObservableProperty]
    private bool isOccupyRunning;

    public bool IsOccupyStopped => !IsOccupyRunning;

    [ObservableProperty]
    private int reReserveDelaySeconds = 60;

    [ObservableProperty]
    private int selectedRefreshModeIndex;

    [ObservableProperty]
    private int selectedNotificationSettingsTabIndex;

    [ObservableProperty]
    private bool notificationsEnabled = true;

    [ObservableProperty]
    private bool minimizeToTrayEnabled = true;

    [ObservableProperty]
    private bool customApiOverridesEnabled;

    [ObservableProperty]
    private int apiTimeoutSeconds = 5;

    [ObservableProperty]
    private int retryCount = 3;

    [ObservableProperty]
    private int selectedAppThemeModeIndex;

    partial void OnSelectedAppThemeModeIndexChanged(int value)
    {
        PreviewThemeSettings();
    }

    [ObservableProperty]
    private bool useSystemAccent = OperatingSystem.IsWindows();

    partial void OnUseSystemAccentChanged(bool value)
    {
        PreviewThemeSettings();
    }

    [ObservableProperty]
    private bool cookieEmailAlertsEnabled;

    [ObservableProperty]
    private string cookieAlertSmtpHost = string.Empty;

    [ObservableProperty]
    private int cookieAlertSmtpPort = 587;

    [ObservableProperty]
    private int selectedCookieAlertSecurityModeIndex = 1;

    [ObservableProperty]
    private string cookieAlertUsername = string.Empty;

    [ObservableProperty]
    private string cookieAlertPassword = string.Empty;

    [ObservableProperty]
    private string cookieAlertFromAddress = string.Empty;

    [ObservableProperty]
    private string cookieAlertToAddress = string.Empty;

    [ObservableProperty]
    private bool cookieLocalToastEnabled = true;

    [ObservableProperty]
    private bool cookieLocalSoundEnabled;

    [ObservableProperty]
    private string notificationSettingsStatusText = "更改后会自动保存。";

    [ObservableProperty]
    private string allLogsText = string.Empty;

    [ObservableProperty]
    private string grabLogsText = string.Empty;

    [ObservableProperty]
    private string occupyLogsText = string.Empty;

    [ObservableProperty]
    private string getCookieTemplateText = string.Empty;

    [ObservableProperty]
    private string queryLibrariesTemplateText = string.Empty;

    [ObservableProperty]
    private string queryLibraryLayoutTemplateText = string.Empty;

    [ObservableProperty]
    private string queryLibraryRuleTemplateText = string.Empty;

    [ObservableProperty]
    private string queryReservationInfoTemplateText = string.Empty;

    [ObservableProperty]
    private string reserveSeatTemplateText = string.Empty;

    [ObservableProperty]
    private string cancelReservationTemplateText = string.Empty;

    public bool ShouldHideToTrayOnClose =>
        MinimizeToTrayEnabled &&
        (IsTaskActive(grabSeatCoordinator.GetStatus()) || IsTaskActive(occupySeatCoordinator.GetStatus()));

    public int SelectedSeatCount => SelectedSeats.Count;

    public bool HasSelectedSeats => SelectedSeatCount > 0;

    public bool HasNoSelectedSeats => !HasSelectedSeats;

    public bool CanEditGrabConfiguration => !IsGrabTaskActive;

    public int DraftSelectedSeatCount => _draftSelectedSeatKeys.Count;

    public bool HasVisibleSeatResults => VisibleSeatResultCount > 0;

    public bool HasNoVisibleSeatResults => !HasVisibleSeatResults;

    public bool HasSeatLayout => _allSeats.Count > 0;

    public bool HasNoSeatLayout => !HasSeatLayout;

    public bool ShowSeatFilterEmptyState => HasSeatLayout && HasNoVisibleSeatResults;

    public bool IsEmailNotificationTabActive => SelectedNotificationSettingsTabIndex == 0;

    public bool IsLocalNotificationTabActive => SelectedNotificationSettingsTabIndex == 1;

    public double NotificationSegmentControlWidth => NotificationSegmentControlWidthValue;

    public double NotificationSegmentSliderWidth => NotificationSegmentSliderWidthValue;

    public double NotificationSegmentSliderOffset => SelectedNotificationSettingsTabIndex == 1
        ? NotificationSegmentSliderOffsetValue
        : 0d;

    public IBrush EmailNotificationTabBackgroundBrush => IsEmailNotificationTabActive
        ? NotificationSegmentActiveBrush
        : NotificationSegmentInactiveBrush;

    public IBrush LocalNotificationTabBackgroundBrush => IsLocalNotificationTabActive
        ? NotificationSegmentActiveBrush
        : NotificationSegmentInactiveBrush;

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

    public string GrabDashboardStatusText => _grabTaskState switch
    {
        CoordinatorTaskState.Starting => "启动中",
        CoordinatorTaskState.Running => "运行中",
        CoordinatorTaskState.Stopping => "停止中",
        CoordinatorTaskState.Completed when GrabStatusText.Contains("停止", StringComparison.OrdinalIgnoreCase) => "已停止",
        CoordinatorTaskState.Completed => "已完成",
        CoordinatorTaskState.Failed => "异常",
        _ => "未运行"
    };

    public IBrush GrabDashboardStatusBrush => _grabTaskState switch
    {
        CoordinatorTaskState.Starting => GrabStateWarningBrush,
        CoordinatorTaskState.Running => GrabStateRunningBrush,
        CoordinatorTaskState.Stopping => GrabStateWarningBrush,
        CoordinatorTaskState.Completed when GrabStatusText.Contains("停止", StringComparison.OrdinalIgnoreCase) => GrabStateFailureBrush,
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
        OnPropertyChanged(nameof(IsLocalNotificationTabActive));
        OnPropertyChanged(nameof(NotificationSegmentSliderOffset));
        OnPropertyChanged(nameof(EmailNotificationTabBackgroundBrush));
        OnPropertyChanged(nameof(LocalNotificationTabBackgroundBrush));
        OnPropertyChanged(nameof(EmailNotificationTabForegroundBrush));
        OnPropertyChanged(nameof(LocalNotificationTabForegroundBrush));
    }

    partial void OnIsAuthorizedChanged(bool value)
    {
        if (!value && SelectedTabIndex > AccountAndVenueTabIndex)
        {
            SelectedTabIndex = AccountAndVenueTabIndex;
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
    }

    public async Task InitializeAsync()
    {
        if (!_themePaletteSubscribed)
        {
            _themePaletteSubscribed = true;
            _appThemeService.PaletteChanged += OnThemePaletteChanged;
            ApplyThemePalette(_appThemeService.CurrentPalette);
        }

        activityLogService.EntryWritten += OnLogEntryWritten;
        grabSeatCoordinator.StatusChanged += OnGrabStatusChanged;
        occupySeatCoordinator.StatusChanged += OnOccupyStatusChanged;
        ApplyGrabStatus(grabSeatCoordinator.GetStatus());
        ApplyOccupyStatus(occupySeatCoordinator.GetStatus());

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
                var restored = await sessionService.RestoreAsync();
                if (restored is not null)
                {
                    IsAuthorized = true;
                    SessionSummary = $"已恢复会话：{restored.Source} / {restored.SavedAt:yyyy-MM-dd HH:mm:ss}";
                    ManualCookieText = restored.Cookie;
                    await LoadLibrariesAsync(restorePreferredSelection: true);
                    if (SelectedLibrary is not null)
                    {
                        await BindSelectedLibraryAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                activityLogService.Write(LogEntryKind.Warning, "Bootstrap", $"恢复会话失败：{ex.Message}");
            }
        }
        finally
        {
            IsInitializationComplete = true;
            UpdateHomeDashboardPresentation();
        }
    }

    partial void OnSeatFilterTextChanged(string value) => _ = ApplySeatFilterAsync();

    partial void OnShowAvailableOnlyChanged(bool value) => _ = ApplySeatFilterAsync();

    partial void OnCookieEmailAlertsEnabledChanged(bool value) => ScheduleNotificationSettingsAutoSave();

    partial void OnCookieAlertSmtpHostChanged(string value) => ScheduleNotificationSettingsAutoSave();

    partial void OnCookieAlertSmtpPortChanged(int value) => ScheduleNotificationSettingsAutoSave();

    partial void OnSelectedCookieAlertSecurityModeIndexChanged(int value) => ScheduleNotificationSettingsAutoSave();

    partial void OnCookieAlertUsernameChanged(string value) => ScheduleNotificationSettingsAutoSave();

    partial void OnCookieAlertPasswordChanged(string value) => ScheduleNotificationSettingsAutoSave();

    partial void OnCookieAlertFromAddressChanged(string value) => ScheduleNotificationSettingsAutoSave();

    partial void OnCookieAlertToAddressChanged(string value) => ScheduleNotificationSettingsAutoSave();

    partial void OnCookieLocalToastEnabledChanged(bool value) => ScheduleNotificationSettingsAutoSave();

    partial void OnCookieLocalSoundEnabledChanged(bool value) => ScheduleNotificationSettingsAutoSave();

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
    private void ShowLocalNotificationSettings()
    {
        SelectedNotificationSettingsTabIndex = 1;
    }

    [RelayCommand]
    private void ShowWindow()
    {
        appWindowService.ShowMainWindow();
    }

    [RelayCommand]
    private void QuitApplication()
    {
        appWindowService.QuitApplication();
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
                    await notificationService.ShowWarningAsync("链接无效", "未能从链接中提取 32 位 code。");
                }

                return false;
            }

            if (!TryReserveAuthCode(code))
            {
                activityLogService.Write(LogEntryKind.Info, "Auth", $"授权 code 已处理，跳过重复解析：{code}");
                if (notifyOnInvalidLink)
                {
                    await notificationService.ShowInfoAsync("链接已处理", "该授权链接已处理过一次。如需重试，请重新从微信获取新的授权链接。");
                }

                return false;
            }

            reservedCode = code;
            var cookie = await apiClient.GetCookieFromCodeAsync(code);
            shouldMarkCodeAsProcessed = true;
            ManualCookieText = cookie;
            SessionSummary = "已获取 Cookie，等待验证";
            SelectedTabIndex = 1;
            await notificationService.ShowSuccessAsync("已成功获取 Cookie", "授权链接解析成功，Cookie 已填入。");

            try
            {
                var session = await sessionService.AuthenticateFromCookieAsync(cookie, RememberSession);
                IsAuthorized = true;
                SessionSummary = $"登录成功：{session.Source} / {session.SavedAt:yyyy-MM-dd HH:mm:ss}";
                await LoadLibrariesAsync(restorePreferredSelection: false);
            }
            catch (Exception ex)
            {
                activityLogService.Write(LogEntryKind.Warning, "Auth", $"Cookie 已获取，但自动验证失败：{ex.Message}");
                await notificationService.ShowInfoAsync("已获取 Cookie", $"Cookie 已填入文本框，但自动验证失败：{ex.Message}");
            }

            return true;
        }
        catch (Exception ex)
        {
            activityLogService.Write(LogEntryKind.Error, "Auth", $"通过链接获取 Cookie 失败：{ex.Message}");
            await notificationService.ShowWarningAsync("获取 Cookie 失败", ex.Message);
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
                await notificationService.ShowWarningAsync("Cookie 为空", "请先输入 Cookie。");
                return;
            }

            var session = await sessionService.AuthenticateFromCookieAsync(ManualCookieText, RememberSession);
            IsAuthorized = true;
            SessionSummary = $"登录成功：{session.Source} / {session.SavedAt:yyyy-MM-dd HH:mm:ss}";
            await LoadLibrariesAsync(restorePreferredSelection: false);
            SelectedTabIndex = 1;
        }
        catch (Exception ex)
        {
            activityLogService.Write(LogEntryKind.Error, "Auth", $"手动验证 Cookie 失败：{ex.Message}");
            await notificationService.ShowWarningAsync("验证 Cookie 失败", ex.Message);
        }
    }

    [RelayCommand]
    private async Task RestoreSessionAsync()
    {
        try
        {
            var session = await sessionService.RestoreAsync();
            if (session is null)
            {
                await notificationService.ShowInfoAsync("没有会话", "本地没有可恢复的会话。");
                return;
            }

            IsAuthorized = true;
            SessionSummary = $"已恢复会话：{session.Source} / {session.SavedAt:yyyy-MM-dd HH:mm:ss}";
            ManualCookieText = session.Cookie;
            await LoadLibrariesAsync(restorePreferredSelection: false);
        }
        catch (Exception ex)
        {
            activityLogService.Write(LogEntryKind.Error, "Auth", $"恢复会话失败：{ex.Message}");
            await notificationService.ShowWarningAsync("恢复会话失败", ex.Message);
        }
    }

    [RelayCommand]
    private async Task SignOutAsync()
    {
        await sessionService.SignOutAsync();
        await ClearStoredLibrarySelectionAsync();
        CancelFiltering();
        IsGrabSeatSelectionOverlayOpen = false;
        _draftSelectedSeatKeys.Clear();
        _committedSelectedSeatKeys.Clear();
        AvailableLibraries.Clear();
        _allSeats.Clear();
        VisibleSeats.Clear();
        OnPropertyChanged(nameof(HasSeatLayout));
        OnPropertyChanged(nameof(HasNoSeatLayout));
        OnPropertyChanged(nameof(ShowSeatFilterEmptyState));
        RefreshSelectedSeatsPresentation();
        UpdateDraftSelectionPresentation();
        VisibleSeatResultCount = 0;
        SelectedLibrary = null;
        IsAuthorized = false;
        SessionSummary = "未登录";
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
            var libraries = await libraryService.LoadLibrariesAsync();
            AvailableLibraries.Clear();
            foreach (var library in libraries)
            {
                AvailableLibraries.Add(library);
            }

            if (preferredLibraryId is not null)
            {
                SelectedLibrary = AvailableLibraries.FirstOrDefault(x => x.LibraryId == preferredLibraryId.Value);
                return;
            }

            if (!restorePreferredSelection)
            {
                SelectedLibrary = null;
                return;
            }

            var settings = await settingsService.LoadAsync();
            SelectedLibrary = AvailableLibraries.FirstOrDefault(x => x.LibraryId == settings.LastLibraryId)
                ?? AvailableLibraries.FirstOrDefault();
        }
        catch (Exception ex)
        {
            activityLogService.Write(LogEntryKind.Error, "Library", $"加载场馆列表失败：{ex.Message}");
            await notificationService.ShowWarningAsync("加载场馆失败", ex.Message);
        }
    }

    [RelayCommand]
    private async Task BindSelectedLibraryAsync()
    {
        try
        {
            if (SelectedLibrary is null)
            {
                await notificationService.ShowWarningAsync("未选择场馆", "请先选择一个场馆。");
                return;
            }

            var layout = await libraryService.BindLibraryAsync(SelectedLibrary.LibraryId);
            var preserveSelection = _lockedLibrarySummary?.LibraryId == SelectedLibrary.LibraryId;
            UpdateBoundLibraryPresentation(layout);
            await LoadVenueRulePresentationAsync(SelectedLibrary.LibraryId, persistLockedSnapshot: true);
            await PopulateSeatsAsync(layout, preserveSelection);
            await LoadFavoritesAsync();
            await RefreshReservationAsync(showNotificationOnError: false);
        }
        catch (Exception ex)
        {
            activityLogService.Write(LogEntryKind.Error, "Library", $"绑定场馆失败：{ex.Message}");
            await notificationService.ShowWarningAsync("绑定场馆失败", ex.Message);
        }
    }

    [RelayCommand]
    private async Task RefreshSeatsAsync()
    {
        try
        {
            var layout = await libraryService.RefreshBoundLibraryAsync();
            UpdateBoundLibraryPresentation(layout);
            await PopulateSeatsAsync(layout, preserveSelection: true);
            await LoadFavoritesAsync();
        }
        catch (Exception ex)
        {
            activityLogService.Write(LogEntryKind.Error, "Library", $"刷新座位失败：{ex.Message}");
            await notificationService.ShowWarningAsync("刷新座位失败", ex.Message);
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
            await notificationService.ShowWarningAsync("未绑定场馆", "请先绑定场馆后再选择目标座位。");
            return;
        }

        if (_allSeats.Count == 0)
        {
            await RefreshSeatsAsync();
        }

        if (_allSeats.Count == 0)
        {
            await notificationService.ShowInfoAsync("暂无座位数据", "当前场馆还没有可供编辑的座位布局。");
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
    private void RemoveSelectedSeat(TrackedSeat? seat)
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
                .Select(x => new TrackedSeat(x.SeatKey, x.SeatName))
                .ToList();
            await libraryService.SaveFavoritesAsync(SelectedLibrary.LibraryId, selected);
            ApplyFavoriteStates(selected.Select(x => x.SeatKey), syncSelection: false);
        }
        catch (Exception ex)
        {
            activityLogService.Write(LogEntryKind.Error, "Favorite", $"保存收藏失败：{ex.Message}");
            await notificationService.ShowWarningAsync("保存收藏失败", ex.Message);
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
            var favorites = await libraryService.GetFavoritesAsync(SelectedLibrary.LibraryId);
            ApplyFavoriteStates(favorites.Select(x => x.SeatKey), syncSelection: false);
            await notificationService.ShowInfoAsync("收藏已加载", $"已加载 {favorites.Count} 个收藏座位。");
        }
        catch (Exception ex)
        {
            activityLogService.Write(LogEntryKind.Error, "Favorite", $"读取收藏失败：{ex.Message}");
            await notificationService.ShowWarningAsync("读取收藏失败", ex.Message);
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
            await notificationService.ShowWarningAsync("未绑定场馆", "请先绑定场馆。");
            return;
        }

        var selectedSeats = SelectedSeats.ToList();
        if (selectedSeats.Count == 0)
        {
            await notificationService.ShowWarningAsync("未选择座位", "请至少选中一个目标座位。");
            return;
        }

        try
        {
            var mode = (GrabMode)SelectedGrabModeIndex;
            var scheduledStart = ParseScheduledTime();
            await PersistGrabReservationStrategyAsync();
            var plan = new GrabSeatPlan(
                SelectedLibrary.LibraryId,
                SelectedLibrary.Name,
                selectedSeats,
                mode,
                GrabStrategyFactory.FromMode(mode),
                scheduledStart);
            await grabSeatCoordinator.StartAsync(plan);
        }
        catch (Exception ex)
        {
            activityLogService.Write(LogEntryKind.Error, "Grab", $"启动抢座失败：{ex.Message}");
            await notificationService.ShowWarningAsync("启动抢座失败", ex.Message);
        }
    }

    [RelayCommand]
    private async Task StopGrabAsync()
    {
        try
        {
            await grabSeatCoordinator.StopAsync();
        }
        catch (Exception ex)
        {
            activityLogService.Write(LogEntryKind.Error, "Grab", $"停止抢座失败：{ex.Message}");
            await notificationService.ShowWarningAsync("停止抢座失败", ex.Message);
        }
    }

    [RelayCommand]
    private async Task RefreshReservationAsync()
    {
        await RefreshReservationAsync(showNotificationOnError: true);
    }

    private async Task RefreshReservationAsync(bool showNotificationOnError)
    {
        var session = sessionService.CurrentSession;
        if (session is null)
        {
            UpdateReservationPresentation(null);
            return;
        }

        try
        {
            var info = await apiClient.GetReservationInfoAsync(session.Cookie);
            UpdateReservationPresentation(info);
        }
        catch (Exception ex)
        {
            activityLogService.Write(LogEntryKind.Error, "Occupy", $"刷新预约状态失败：{ex.Message}");
            if (showNotificationOnError)
            {
                await notificationService.ShowWarningAsync("刷新预约状态失败", ex.Message);
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

        var session = sessionService.CurrentSession;
        if (session is null)
        {
            await notificationService.ShowWarningAsync("未登录", "当前会话已失效，请重新授权后再操作。");
            return;
        }

        var reservation = _currentReservation;
        IsCancellingCurrentReservation = true;

        try
        {
            if (IsOccupyRunning)
            {
                try
                {
                    await occupySeatCoordinator.StopAsync();
                }
                catch (Exception ex)
                {
                    activityLogService.Write(LogEntryKind.Warning, "Occupy", $"取消预约前停止占座失败：{ex.Message}");
                }
            }

            var cancelled = await apiClient.CancelReservationAsync(session.Cookie, reservation.ReservationToken);
            if (!cancelled)
            {
                activityLogService.Write(LogEntryKind.Warning, "Occupy", $"{reservation.SeatName} 取消预约失败，接口未返回成功结果。");
                await notificationService.ShowWarningAsync("取消预约失败", "接口未返回成功结果，请稍后重试。");
                return;
            }

            activityLogService.Write(LogEntryKind.Success, "Occupy", $"{reservation.SeatName} 已手动取消预约。");
            UpdateReservationPresentation(null);
            await notificationService.ShowSuccessAsync("已取消预约", $"{reservation.SeatName} 已取消预约。");
        }
        catch (Exception ex)
        {
            activityLogService.Write(LogEntryKind.Error, "Occupy", $"取消预约失败：{ex.Message}");
            await notificationService.ShowWarningAsync("取消预约失败", ex.Message);
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
                (RefreshMode)SelectedRefreshModeIndex);
            await occupySeatCoordinator.StartAsync(plan);
        }
        catch (Exception ex)
        {
            activityLogService.Write(LogEntryKind.Error, "Occupy", $"启动占座失败：{ex.Message}");
            await notificationService.ShowWarningAsync("启动占座失败", ex.Message);
        }
    }

    [RelayCommand]
    private async Task StopOccupyAsync()
    {
        try
        {
            await occupySeatCoordinator.StopAsync();
        }
        catch (Exception ex)
        {
            activityLogService.Write(LogEntryKind.Error, "Occupy", $"停止占座失败：{ex.Message}");
            await notificationService.ShowWarningAsync("停止占座失败", ex.Message);
        }
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        CancelPendingNotificationSettingsAutoSave();
        var current = await settingsService.LoadAsync();
        var settings = current with
        {
            NotificationsEnabled = NotificationsEnabled,
            MinimizeToTray = MinimizeToTrayEnabled,
            CustomApiOverridesEnabled = CustomApiOverridesEnabled,
            ApiTimeoutSeconds = Math.Max(3, ApiTimeoutSeconds),
            RetryCount = Math.Max(1, RetryCount),
            ThemeMode = (AppThemeMode)Math.Clamp(SelectedAppThemeModeIndex, 0, ThemeModes.Length - 1),
            UseSystemAccent = UseSystemAccent,
            GrabReservationStrategy = (GrabReservationStrategy)Math.Clamp(
                SelectedGrabReservationStrategyIndex,
                0,
                GrabReservationStrategies.Length - 1),
            CookieExpiryAlerts = BuildCookieExpiryAlertSettings(),
            LastLibraryId = SelectedLibrary?.LibraryId,
            LastLibraryName = SelectedLibrary?.Name,
            SuccessfulReservationCount = _historicalSuccessCount,
            TotalGuardSeconds = GetCurrentTotalGuardSeconds(DateTimeOffset.Now)
        };
        await settingsService.SaveAsync(settings);
        await _appThemeService.ApplySettingsAsync(settings);
        await notificationService.ShowSuccessAsync("设置已保存", "应用设置已写入本地数据库。");
    }

    [RelayCommand]
    private async Task TestToastAsync()
    {
        if (notificationService is ToastNotificationService toastNotificationService)
        {
            await toastNotificationService.ShowPreviewAsync("测试通知", "这是一条用于测试界面动效与停留时间的 Toast 通知。");
            return;
        }

        await notificationService.ShowInfoAsync("测试通知", "这是一条用于测试界面动效与停留时间的 Toast 通知。");
    }

    [RelayCommand]
    private async Task SendTestEmailAlertAsync()
    {
        try
        {
            CancelPendingNotificationSettingsAutoSave();
            await PersistNotificationSettingsSnapshotAsync();
            await cookieExpiryAlertService.SendTestEmailAsync(BuildCookieExpiryAlertSettings().Email);
            NotificationSettingsStatusText = $"测试邮件已发送于 {DateTime.Now:HH:mm:ss}。";
            await notificationService.ShowSuccessAsync("测试邮件已发送", "请检查收件箱，确认当前 SMTP 配置可用。");
        }
        catch (Exception ex)
        {
            NotificationSettingsStatusText = $"测试邮件发送失败：{ex.Message}";
            activityLogService.Write(LogEntryKind.Warning, "Alert", $"发送测试邮件失败：{ex.Message}");
            await errorDialogService.ShowErrorAsync("测试邮件发送失败", ex.GetType().Name, BuildExceptionDetails(ex));
        }
    }

    [RelayCommand]
    private async Task SendTestLocalAlertAsync()
    {
        try
        {
            CancelPendingNotificationSettingsAutoSave();
            await PersistNotificationSettingsSnapshotAsync();
            await cookieExpiryAlertService.SendTestLocalAlertAsync(BuildCookieExpiryAlertSettings().Local);
            NotificationSettingsStatusText = $"测试通知已触发于 {DateTime.Now:HH:mm:ss}。";
        }
        catch (Exception ex)
        {
            NotificationSettingsStatusText = $"测试通知发送失败：{ex.Message}";
            activityLogService.Write(LogEntryKind.Warning, "Alert", $"发送测试通知失败：{ex.Message}");
            await notificationService.ShowWarningAsync("测试通知发送失败", ex.Message);
        }
    }

    [RelayCommand]
    private async Task SaveProtocolOverridesAsync()
    {
        var overrides = new ProtocolTemplateOverrides(
            GetCookieTemplateText,
            QueryLibrariesTemplateText,
            QueryLibraryLayoutTemplateText,
            QueryLibraryRuleTemplateText,
            QueryReservationInfoTemplateText,
            ReserveSeatTemplateText,
            CancelReservationTemplateText);
        await protocolTemplateStore.SaveOverridesAsync(overrides);
        await notificationService.ShowSuccessAsync("协议模板已保存", "高级协议覆盖已写入数据库。");
    }

    [RelayCommand]
    private async Task ResetProtocolOverridesAsync()
    {
        await protocolTemplateStore.ResetOverridesAsync();
        await LoadProtocolTemplatesAsync();
        await notificationService.ShowSuccessAsync("协议模板已重置", "已恢复内置默认模板。");
    }

    private async Task PersistGrabReservationStrategyAsync()
    {
        var settings = await settingsService.LoadAsync();
        var strategy = (GrabReservationStrategy)Math.Clamp(
            SelectedGrabReservationStrategyIndex,
            0,
            GrabReservationStrategies.Length - 1);

        if (settings.GrabReservationStrategy == strategy)
        {
            return;
        }

        await settingsService.SaveAsync(settings with
        {
            GrabReservationStrategy = strategy
        });
    }

    private async Task LoadSettingsAsync()
    {
        _isLoadingSettings = true;
        var settings = await settingsService.LoadAsync();
        try
        {
            NotificationsEnabled = settings.NotificationsEnabled;
            MinimizeToTrayEnabled = settings.MinimizeToTray;
            CustomApiOverridesEnabled = settings.CustomApiOverridesEnabled;
            ApiTimeoutSeconds = settings.ApiTimeoutSeconds;
            RetryCount = settings.RetryCount;
            SelectedAppThemeModeIndex = (int)settings.ThemeMode;
            UseSystemAccent = settings.UseSystemAccent;
            SelectedGrabReservationStrategyIndex = (int)settings.GrabReservationStrategy;

            var cookieAlerts = settings.CookieExpiryAlerts ?? CookieExpiryAlertSettings.Default;
            CookieEmailAlertsEnabled = cookieAlerts.Email.Enabled;
            CookieAlertSmtpHost = cookieAlerts.Email.SmtpHost;
            CookieAlertSmtpPort = cookieAlerts.Email.Port;
            SelectedCookieAlertSecurityModeIndex = cookieAlerts.Email.SecurityMode == EmailSecurityMode.Tls ? 1 : 0;
            CookieAlertUsername = cookieAlerts.Email.Username;
            CookieAlertPassword = cookieAlerts.Email.Password;
            CookieAlertFromAddress = cookieAlerts.Email.FromAddress;
            CookieAlertToAddress = cookieAlerts.Email.ToAddress;
            CookieLocalToastEnabled = cookieAlerts.Local.ToastEnabled;
            CookieLocalSoundEnabled = cookieAlerts.Local.SoundEnabled;
            NotificationSettingsStatusText = "更改后会自动保存。";

            _historicalSuccessCount = Math.Max(0, settings.SuccessfulReservationCount);
            _totalGuardSeconds = Math.Max(0, settings.TotalGuardSeconds);
            HomeHistoricalSuccessCount = _historicalSuccessCount;
            UpdateHomeDashboardPresentation();
        }
        finally
        {
            _isLoadingSettings = false;
            _notificationSettingsLoaded = true;
        }
    }

    private void PreviewThemeSettings()
    {
        if (_isLoadingSettings)
        {
            return;
        }

        _ = PreviewThemeSettingsAsync();
    }

    private async Task PreviewThemeSettingsAsync()
    {
        try
        {
            await _appThemeService.ApplySettingsAsync(AppSettings.Default with
            {
                ThemeMode = (AppThemeMode)Math.Clamp(SelectedAppThemeModeIndex, 0, ThemeModes.Length - 1),
                UseSystemAccent = UseSystemAccent
            });
        }
        catch
        {
            // Theme preview should never block the rest of the settings workflow.
        }
    }

    private async Task LoadProtocolTemplatesAsync()
    {
        var templates = await protocolTemplateStore.GetEffectiveTemplatesAsync();
        GetCookieTemplateText = templates.GetCookieUrlTemplate;
        QueryLibrariesTemplateText = templates.QueryLibrariesTemplate;
        QueryLibraryLayoutTemplateText = templates.QueryLibraryLayoutTemplate;
        QueryLibraryRuleTemplateText = templates.QueryLibraryRuleTemplate;
        QueryReservationInfoTemplateText = templates.QueryReservationInfoTemplate;
        ReserveSeatTemplateText = templates.ReserveSeatTemplate;
        CancelReservationTemplateText = templates.CancelReservationTemplate;
    }

    private async Task ClearStoredLibrarySelectionAsync()
    {
        try
        {
            var settings = await settingsService.LoadAsync();
            if (settings.LastLibraryId is null && string.IsNullOrWhiteSpace(settings.LastLibraryName))
            {
                return;
            }

            await settingsService.SaveAsync(settings with
            {
                LastLibraryId = null,
                LastLibraryName = null
            });
        }
        catch (Exception ex)
        {
            activityLogService.Write(LogEntryKind.Warning, "Auth", $"清理上次场馆选择失败：{ex.Message}");
        }
    }

    private async Task PreviewSelectedLibraryAsync(LibrarySummary library)
    {
        var session = sessionService.CurrentSession;
        if (session is null)
        {
            return;
        }

        try
        {
            var layout = await apiClient.GetLibraryLayoutAsync(session.Cookie, library.LibraryId);
            VenueStatusText = layout.IsOpen ? "开放中" : "未开放";
            IsVenueOpen = layout.IsOpen;
            VenueName = layout.Name;
            VenueFloor = layout.Floor;
            VenueAvailableSeatsText = layout.AvailableSeats.ToString();
            await LoadVenueRulePresentationAsync(library.LibraryId, persistLockedSnapshot: false);
            IsCurrentLocked = _lockedLibrarySummary?.LibraryId == library.LibraryId;
            HasActiveVenuePreview = !IsCurrentLocked;
            IsVenuePickerOpen = false;
        }
        catch (Exception ex)
        {
            activityLogService.Write(LogEntryKind.Error, "Library", $"预览场馆失败：{ex.Message}");
            await notificationService.ShowWarningAsync("预览场馆失败", ex.Message);
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
        var session = sessionService.CurrentSession;
        if (session is null)
        {
            VenueOpenTimeText = "--";
            VenueCloseTimeText = "--";
            if (persistLockedSnapshot && IsCurrentLocked)
            {
                PersistLockedVenueSnapshot();
            }

            return;
        }

        try
        {
            var rule = await apiClient.GetLibraryRuleAsync(session.Cookie, libraryId);
            VenueOpenTimeText = string.IsNullOrWhiteSpace(rule.OpenTimeText) ? "--" : rule.OpenTimeText;
            VenueCloseTimeText = string.IsNullOrWhiteSpace(rule.CloseTimeText) ? "--" : rule.CloseTimeText;
        }
        catch (Exception ex)
        {
            VenueOpenTimeText = "--";
            VenueCloseTimeText = "--";
            activityLogService.Write(LogEntryKind.Warning, "Library", $"加载场馆开放时间失败：{ex.Message}");
        }

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
            activityLogService.Write(LogEntryKind.Error, "Library", $"筛选座位失败：{ex.Message}");
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

    private IEnumerable<TrackedSeat> EnumerateSelectedSeats()
    {
        return EnumerateSelectedSeats(_allSeats.Where(x => x.IsSelected).Select(x => x.SeatKey));
    }

    private IEnumerable<TrackedSeat> EnumerateSelectedSeats(IEnumerable<string> selectedSeatKeys)
    {
        var selectedKeySet = selectedSeatKeys.ToHashSet(StringComparer.Ordinal);

        return _allSeats
            .Where(seat => selectedKeySet.Contains(seat.SeatKey))
            .OrderBy(seat => int.TryParse(seat.SeatName, out var number) ? number : int.MaxValue)
            .ThenBy(seat => seat.SeatName, StringComparer.OrdinalIgnoreCase)
            .Select(seat => new TrackedSeat(seat.SeatKey, seat.SeatName));
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
        if (string.IsNullOrWhiteSpace(ScheduledTimeText) || ScheduledTimeText == "00:00:00")
        {
            return null;
        }

        return TimeOnly.TryParse(ScheduledTimeText, out var value) ? value : null;
    }

    private void OnLogEntryWritten(object? sender, AppLogEntry entry)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var line = $"[{entry.Timestamp:HH:mm:ss}] {entry.Category}: {entry.Message}";
            AllLogsText = AppendLine(AllLogsText, line);
            if (entry.Category is "Grab" or "Library" or "Favorite" or "Auth")
            {
                GrabLogsText = AppendLine(GrabLogsText, line);
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
                    $"{entry.Category}: {entry.Message}",
                    entry.Kind,
                    true,
                    hasSuccessSemantic,
                    hasFailureSemantic,
                    _appThemeService));

                if (entry.Category == "Occupy" &&
                    entry.Message.EndsWith("已重新预约成功。", StringComparison.Ordinal))
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
                            activityLogService.Write(LogEntryKind.Warning, "Occupy", $"占座成功后刷新预约状态失败：{ex.Message}");
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

        if (status.State == CoordinatorTaskState.Completed && status.Message == "已成功预约到目标座位。")
        {
            _ = Dispatcher.UIThread.InvokeAsync(async () =>
            {
                try
                {
                    await RefreshReservationAsync(showNotificationOnError: false);
                }
                catch (Exception ex)
                {
                    activityLogService.Write(LogEntryKind.Warning, "Grab", $"抢座成功后刷新预约状态失败：{ex.Message}");
                }
            });
        }
    }

    private void OnOccupyStatusChanged(object? sender, CoordinatorStatus status)
    {
        Dispatcher.UIThread.Post(() => ApplyOccupyStatus(status));
    }

    private void OnReservationCountdownTick(object? sender, EventArgs e)
    {
        UpdateReservationCountdown();
        UpdateGrabLastRequestText();
        UpdateGrabRuntimeClock();
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
        UpdateGrabLastRequestText();
        ApplyGrabRuntime(status);
        UpdateGuardTracking(status.LastUpdatedAt ?? DateTimeOffset.Now);
        UpdateHomeHeroPresentation(DateTimeOffset.Now);
        UpdateHomeSystemInfoPresentation();
        OnPropertyChanged(nameof(GrabDashboardStatusText));
        OnPropertyChanged(nameof(GrabDashboardStatusBrush));
    }

    private void ApplyOccupyStatus(CoordinatorStatus status)
    {
        OccupyStatusText = status.Message;
        IsOccupyRunning = IsTaskActive(status);
        UpdateGuardTracking(status.LastUpdatedAt ?? DateTimeOffset.Now);
        UpdateHomeHeroPresentation(DateTimeOffset.Now);
        UpdateHomeSystemInfoPresentation();
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

        if (IsGrabTaskActive && IsOccupyRunning)
        {
            return ("双引擎协同中", "抢座与占座守护都在后台稳定运行。", GrabStateRunningBrush, DashboardRunningSoftBrush);
        }

        if (IsGrabTaskActive)
        {
            return ("抢座任务运行中", "已进入实时监控阶段，请保持程序常驻。", GrabStateRunningBrush, DashboardRunningSoftBrush);
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

        if (IsGrabTaskActive && IsOccupyRunning)
        {
            return "抢座运行中 · 占座守护运行中";
        }

        if (IsGrabTaskActive)
        {
            return "抢座运行中 · 占座守护待命";
        }

        if (IsOccupyRunning)
        {
            return "抢座待命 · 占座守护运行中";
        }

        return HasLockedVenue ? "所有核心模块已就绪" : "等待绑定场馆";
    }

    private void UpdateGuardTracking(DateTimeOffset timestamp)
    {
        if (IsGrabTaskActive || IsOccupyRunning)
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
            status.Message != "已成功预约到目标座位。")
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

    private void TryRecordOccupySuccess(DateTimeOffset timestamp)
    {
        if (_lastRecordedOccupySuccessAt == timestamp)
        {
            return;
        }

        _lastRecordedOccupySuccessAt = timestamp;
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
            var current = await settingsService.LoadAsync();
            var totalGuardSeconds = GetCurrentTotalGuardSeconds(DateTimeOffset.Now);

            if (current.SuccessfulReservationCount == _historicalSuccessCount &&
                current.TotalGuardSeconds == totalGuardSeconds)
            {
                return;
            }

            await settingsService.SaveAsync(current with
            {
                SuccessfulReservationCount = _historicalSuccessCount,
                TotalGuardSeconds = totalGuardSeconds
            });
        }
        catch (Exception ex)
        {
            activityLogService.Write(LogEntryKind.Warning, "Dashboard", $"保存首页统计信息失败：{ex.Message}");
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

    private CookieExpiryAlertSettings BuildCookieExpiryAlertSettings()
    {
        return new CookieExpiryAlertSettings(
            new CookieExpiryEmailAlertSettings(
                CookieEmailAlertsEnabled,
                CookieAlertSmtpHost.Trim(),
                Math.Clamp(CookieAlertSmtpPort, 1, 65535),
                SelectedCookieAlertSecurityModeIndex == 1 ? EmailSecurityMode.Tls : EmailSecurityMode.None,
                CookieAlertUsername.Trim(),
                CookieAlertPassword,
                CookieAlertFromAddress.Trim(),
                CookieAlertToAddress.Trim()),
            new CookieExpiryLocalAlertSettings(
                CookieLocalToastEnabled,
                CookieLocalSoundEnabled));
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
            activityLogService.Write(LogEntryKind.Warning, "Alert", $"自动保存通知设置失败：{ex.Message}");
        }
    }

    private async Task PersistNotificationSettingsSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var current = await settingsService.LoadAsync(cancellationToken);
        await settingsService.SaveAsync(current with
        {
            CookieExpiryAlerts = BuildCookieExpiryAlertSettings()
        }, cancellationToken);
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
        OnPropertyChanged(nameof(LocalNotificationTabForegroundBrush));
        OnPropertyChanged(nameof(GrabDashboardStatusBrush));
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
