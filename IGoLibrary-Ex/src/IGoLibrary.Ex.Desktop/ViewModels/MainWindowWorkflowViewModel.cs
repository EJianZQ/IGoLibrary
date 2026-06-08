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

public partial class MainWindowWorkflowViewModel(
    ISessionWorkflowService sessionWorkflowService,
    IVenueWorkflowService venueWorkflowService,
    IReservationWorkflowService reservationWorkflowService,
    ISettingsWorkflowService settingsWorkflowService,
    IProtocolTemplateEditorService protocolTemplateEditorService,
    INotificationTestService notificationTestService,
    IGrabSeatCoordinator grabSeatCoordinator,
    IOccupySeatCoordinator occupySeatCoordinator,
    ITomorrowReservationCoordinator tomorrowReservationCoordinator,
    IActivityLogService activityLogService,
    INotificationService notificationService,
    IErrorDialogService errorDialogService,
    IAppThemeService appThemeService,
    AppWindowService appWindowService) : ViewModelBase
{
    private readonly IAppThemeService _appThemeService = appThemeService;
    private readonly IActivityLogService _activityLogService = activityLogService;
    private readonly INotificationService _notificationService = notificationService;
    private readonly IErrorDialogService _errorDialogService = errorDialogService;
    private readonly AppWindowService _appWindowService = appWindowService;
    private readonly IGrabSeatCoordinator _grabSeatCoordinator = grabSeatCoordinator;
    private readonly IOccupySeatCoordinator _occupySeatCoordinator = occupySeatCoordinator;
    private readonly ITomorrowReservationCoordinator _tomorrowReservationCoordinator = tomorrowReservationCoordinator;
    private readonly ObservableCollection<SeatItemViewModel> _allSeats = [];
    private readonly ObservableCollection<SeatItemViewModel> _tomorrowSeats = [];
    private readonly object _filterGate = new();
    private readonly DispatcherTimer _reservationCountdownTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private CancellationTokenSource? _filteringCts;
    private ReservationInfo? _currentReservation;
    private DateTimeOffset? _sidebarSessionExpirationTime;
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
    private const int TomorrowReservationTabIndex = 3;
    private const int OccupyTabIndex = 4;
    private const int NotificationSettingsTabIndex = 5;
    private const int SystemSettingsTabIndex = 6;
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
    private static readonly SidebarNavigationItem TomorrowReservationSidebarItem = new(
        TomorrowReservationTabIndex,
        "明日预约",
        "M19 3h-1V1h-2v2H8V1H6v2H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2zm0 16H5V8h14v11zM7 10h5v5H7z");
    private static readonly SidebarNavigationItem OccupySidebarItem = new(
        OccupyTabIndex,
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
        TomorrowReservationSidebarItem,
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
    private const double NotificationSegmentControlWidthValue = 560d;
    private const double NotificationSegmentSliderWidthValue = 174d;
    private const double NotificationSegmentSliderOffsetValue = 180d;
    private readonly HashSet<string> _committedSelectedSeatKeys = new(StringComparer.Ordinal);
    private readonly HashSet<string> _draftSelectedSeatKeys = new(StringComparer.Ordinal);
    private bool _isSynchronizingSeatSelection;
    private CoordinatorTaskState _grabTaskState = CoordinatorTaskState.Idle;
    private CoordinatorStatusReason _grabStatusReason = CoordinatorStatusReason.None;
    private CoordinatorTaskState _tomorrowTaskState = CoordinatorTaskState.Idle;
    private CoordinatorStatusReason _tomorrowStatusReason = CoordinatorStatusReason.None;
    private DateTimeOffset? _grabLastRequestAt;
    private DateTimeOffset? _tomorrowLastRequestAt;
    private DateTimeOffset? _grabRuntimeStartedAt;
    private int _historicalSuccessCount;
    private long _totalGuardSeconds;
    private DateTimeOffset? _guardTrackingStartedAt;
    private DateTimeOffset? _lastRecordedGrabSuccessAt;
    private DateTimeOffset? _lastRecordedOccupySuccessAt;
    private DateTimeOffset? _lastRecordedTomorrowSuccessAt;
    private bool _isSynchronizingSidebarSelection;
    private bool _isLoadingSettings;
    private bool _isSynchronizingTomorrowSeatSelection;
    private string? _draftTomorrowSeatKey;
    private bool _notificationSettingsLoaded;
    private CancellationTokenSource? _notificationSettingsAutoSaveCts;
    private bool _themePaletteSubscribed;
    private readonly object _processedAuthCodesGate = new();
    private readonly HashSet<string> _processedAuthCodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _inFlightAuthCodes = new(StringComparer.OrdinalIgnoreCase);

    public HomeDashboardViewModel HomeDashboard { get; } = new();

    public AccountVenueViewModel AccountVenue { get; } = new(sessionWorkflowService, venueWorkflowService);

    public GrabPageViewModel GrabPage { get; } = new(grabSeatCoordinator, settingsWorkflowService);

    public OccupyPageViewModel OccupyPage { get; } = new(occupySeatCoordinator, reservationWorkflowService);

    public TomorrowReservationPageViewModel TomorrowReservationPage { get; } = new(tomorrowReservationCoordinator);

    public NotificationSettingsViewModel NotificationSettings { get; } = new(settingsWorkflowService, notificationTestService);

    public SystemSettingsViewModel SystemSettings { get; } = new(settingsWorkflowService, protocolTemplateEditorService);

    public ObservableCollection<LibrarySummary> AvailableLibraries { get; } = [];

    public ObservableCollection<SidebarNavigationItem> SidebarItems { get; } =
    [
        HomeSidebarItem,
        AccountAndVenueSidebarItem
    ];

    public ObservableCollection<SeatItemViewModel> VisibleSeats { get; } = [];

    public ObservableCollection<SeatItemViewModel> TomorrowVisibleSeats { get; } = [];

    public ObservableCollection<SeatReference> SelectedSeats { get; } = [];

    public ObservableCollection<LogLineViewModel> OccupyLogLines { get; } = [];

    public string[] GrabPollingModes { get; } = ["极限速度", "随机延迟", "延迟 5 秒"];

    public string[] OccupyCheckIntervalModes { get; } = ["固定间隔 10 秒", "随机 10~20 秒"];

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

    [ObservableProperty]
    private bool hasSidebarSessionExpiration;

    [ObservableProperty]
    private string sidebarSessionExpirationText = string.Empty;

    [ObservableProperty]
    private IBrush sidebarSessionExpirationBrush = appThemeService.CurrentPalette.LogDefaultBrush;

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
    private bool isTomorrowSeatSelectionOverlayOpen;

    [ObservableProperty]
    private bool isApplyingSeatFilter;

    [ObservableProperty]
    private int visibleSeatResultCount;

    [ObservableProperty]
    private int selectedGrabPollingModeIndex = 2;

    [ObservableProperty]
    private int selectedGrabReservationStrategyIndex;

    [ObservableProperty]
    private string scheduledTimeText = "00:00:00";

    [ObservableProperty]
    private string tomorrowScheduledTimeText = "21:48:00";

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
    private string tomorrowStatusText = "未运行";

    [ObservableProperty]
    private bool isTomorrowTaskActive;

    [ObservableProperty]
    private int tomorrowRequestCount;

    [ObservableProperty]
    private string tomorrowLastRequestText = "无";

    [ObservableProperty]
    private string tomorrowVerificationText = "尚未执行明日预约";

    [ObservableProperty]
    private string occupyStatusText = "未运行";

    [ObservableProperty]
    private bool isOccupyRunning;

    public bool IsOccupyStopped => !IsOccupyRunning;

    [ObservableProperty]
    private int reReserveDelaySeconds = 60;

    [ObservableProperty]
    private int selectedOccupyCheckIntervalModeIndex;

    [ObservableProperty]
    private int selectedNotificationSettingsTabIndex;

    [ObservableProperty]
    private bool notificationsEnabled = true;

    [ObservableProperty]
    private bool minimizeToTrayEnabled = true;

    [ObservableProperty]
    private bool traceIntGraphQlOverridesEnabled;

    [ObservableProperty]
    private int requestTimeoutSeconds = 5;

    [ObservableProperty]
    private int networkMaxRetries = 3;

    [ObservableProperty]
    private int selectedAppThemeModeIndex;

    partial void OnSelectedAppThemeModeIndexChanged(int value)
    {
        PreviewThemePreferences();
    }

    [ObservableProperty]
    private bool useSystemAccent = OperatingSystem.IsWindows();

    partial void OnUseSystemAccentChanged(bool value)
    {
        PreviewThemePreferences();
    }

    [ObservableProperty]
    private bool emailAlertsEnabled;

    [ObservableProperty]
    private string emailAlertSmtpHost = string.Empty;

    [ObservableProperty]
    private int emailAlertSmtpPort = 587;

    [ObservableProperty]
    private int selectedEmailAlertSecurityModeIndex = 1;

    [ObservableProperty]
    private string emailAlertUsername = string.Empty;

    [ObservableProperty]
    private string emailAlertPassword = string.Empty;

    [ObservableProperty]
    private string emailAlertFromAddress = string.Empty;

    [ObservableProperty]
    private string emailAlertToAddress = string.Empty;

    [ObservableProperty]
    private bool telegramAlertsEnabled;

    [ObservableProperty]
    private string telegramAlertApiBaseUrl = TelegramAlertChannelSettings.DefaultApiBaseUrl;

    [ObservableProperty]
    private string telegramAlertBotToken = string.Empty;

    [ObservableProperty]
    private string telegramAlertChatId = string.Empty;

    [ObservableProperty]
    private bool localToastAlertsEnabled = true;

    [ObservableProperty]
    private bool localSoundAlertsEnabled;

    [ObservableProperty]
    private string notificationSettingsStatusText = "更改后会自动保存。";

    [ObservableProperty]
    private string allLogsText = string.Empty;

    [ObservableProperty]
    private string grabLogsText = string.Empty;

    [ObservableProperty]
    private string occupyLogsText = string.Empty;

    [ObservableProperty]
    private string tomorrowLogsText = string.Empty;

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

    [ObservableProperty]
    private string tomorrowReservationQueueUrlTemplateText = string.Empty;

    [ObservableProperty]
    private string tomorrowReservationWarmUpTemplateText = string.Empty;

    [ObservableProperty]
    private string tomorrowReservationSaveTemplateText = string.Empty;

    [ObservableProperty]
    private string tomorrowReservationInfoTemplateText = string.Empty;

    [ObservableProperty]
    private string tomorrowSeatFilterText = string.Empty;

    [ObservableProperty]
    private SeatReference? selectedTomorrowSeat;

}
