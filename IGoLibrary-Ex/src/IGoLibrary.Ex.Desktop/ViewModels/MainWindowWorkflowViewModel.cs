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
    IGlobalLeakCoordinator globalLeakCoordinator,
    IOccupySeatCoordinator occupySeatCoordinator,
    ITomorrowReservationCoordinator tomorrowReservationCoordinator,
    IActivityLogService activityLogService,
    INotificationService notificationService,
    IErrorDialogService errorDialogService,
    IUpdateCheckService updateCheckService,
    IUpdateDialogService updateDialogService,
    IExternalLinkService externalLinkService,
    IAppVersionProvider appVersionProvider,
    IAppThemeService appThemeService,
    TimeProvider timeProvider,
    AppWindowService appWindowService,
    IStartupEntryService startupEntryService) : ViewModelBase
{
    private readonly IAppThemeService _appThemeService = appThemeService;
    private readonly IActivityLogService _activityLogService = activityLogService;
    private readonly INotificationService _notificationService = notificationService;
    private readonly IErrorDialogService _errorDialogService = errorDialogService;
    private readonly IUpdateCheckService _updateCheckService = updateCheckService;
    private readonly IUpdateDialogService _updateDialogService = updateDialogService;
    private readonly IExternalLinkService _externalLinkService = externalLinkService;
    private readonly AppWindowService _appWindowService = appWindowService;
    private readonly IStartupEntryService _startupEntryService = startupEntryService;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly IGrabSeatCoordinator _grabSeatCoordinator = grabSeatCoordinator;
    private readonly IGlobalLeakCoordinator _globalLeakCoordinator = globalLeakCoordinator;
    private readonly IOccupySeatCoordinator _occupySeatCoordinator = occupySeatCoordinator;
    private readonly ITomorrowReservationCoordinator _tomorrowReservationCoordinator = tomorrowReservationCoordinator;
    private readonly ObservableCollection<SeatItemViewModel> _allSeats = [];
    private readonly ObservableCollection<SeatItemViewModel> _tomorrowSeats = [];
    private readonly object _filterGate = new();
    private readonly SemaphoreSlim _updateCheckGate = new(1, 1);
    private readonly DispatcherTimer _reservationCountdownTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private CancellationTokenSource? _filteringCts;
    private ReservationInfo? _currentReservation;
    private bool _isAutoReleaseRefreshingReservation;
    private string? _lastAutoReleaseFailedReservationToken;
    private DateTimeOffset? _lastAutoReleaseFailedAt;
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
    private const int GlobalLeakTabIndex = 3;
    private const int TomorrowReservationTabIndex = 4;
    private const int OccupyTabIndex = 5;
    private const int NotificationSettingsTabIndex = 6;
    private const int SystemSettingsTabIndex = 7;
    private static readonly TimeSpan DefaultGrabScheduledStartTime = GrabTaskSettings.Default.DefaultScheduledStartTime;
    private static readonly TimeSpan DefaultTomorrowScheduledStartTime =
        TomorrowReservationTaskSettings.Default.DefaultScheduledStartTime;
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
    private static readonly SidebarNavigationItem GlobalLeakSidebarItem = new(
        GlobalLeakTabIndex,
        "全域捡漏",
        "M9.5 3a6.5 6.5 0 0 1 5.17 10.43l4.45 4.45-1.41 1.41-4.45-4.45A6.5 6.5 0 1 1 9.5 3zm0 2a4.5 4.5 0 1 0 0 9 4.5 4.5 0 0 0 0-9zm9.5-1h2v5h-2V4zm0 7h2v2h-2v-2z");
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
        "自动通知",
        "M12 22a2.5 2.5 0 0 0 2.45-2h-4.9A2.5 2.5 0 0 0 12 22zm6-6V11a6 6 0 1 0-12 0v5l-2 2v1h16v-1l-2-2z");
    private static readonly SidebarNavigationItem SettingsSidebarItem = new(
        SystemSettingsTabIndex,
        "系统设置",
        "M19.14,12.94c0.04-0.3,0.06-0.61,0.06-0.94c0-0.32-0.02-0.64-0.06-0.94l2.03-1.58c0.18-0.14,0.23-0.41,0.12-0.61 l-1.92-3.32c-0.12-0.22-0.37-0.29-0.59-0.22l-2.39,0.96c-0.5-0.38-1.03-0.7-1.62-0.94L14.4,2.81c-0.04-0.24-0.24-0.41-0.48-0.41 h-3.84c-0.24,0-0.43,0.17-0.47,0.41L9.25,5.35C8.66,5.59,8.12,5.92,7.63,6.29L5.24,5.33c-0.22-0.08-0.47,0-0.59,0.22L2.73,8.87 C2.62,9.08,2.66,9.34,2.86,9.48l2.03,1.58C4.84,11.36,4.8,11.69,4.8,12s0.02,0.64,0.06,0.94l-2.03,1.58 c-0.18,0.14-0.23,0.41-0.12,0.61l1.92,3.32c0.12,0.22,0.37,0.29,0.59,0.22l2.39-0.96c0.5,0.38,1.03,0.7,1.62,0.94l0.36,2.54 c0.05,0.24,0.24,0.41,0.48,0.41h3.84c0.24,0,0.43-0.17,0.47-0.41l0.36-2.54c0.59-0.24,1.13-0.56,1.62-0.94l2.39,0.96 c0.22,0.08,0.47,0,0.59-0.22l1.92-3.32c0.12-0.22,0.07-0.49-0.12-0.61L19.14,12.94z M12,15.6c-1.98,0-3.6-1.62-3.6-3.6 s1.62-3.6,3.6-3.6s3.6,1.62,3.6,3.6S13.98,15.6,12,15.6z");
    private static readonly SidebarNavigationItem[] UnauthorizedSidebarItems =
    [
        HomeSidebarItem,
        AccountAndVenueSidebarItem,
        SettingsSidebarItem
    ];
    private static readonly SidebarNavigationItem[] AuthorizedSidebarItems =
    [
        HomeSidebarItem,
        AccountAndVenueSidebarItem,
        GrabSidebarItem,
        GlobalLeakSidebarItem,
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
    private readonly HashSet<string> _committedSelectedSeatKeys = new(StringComparer.Ordinal);
    private readonly HashSet<string> _draftSelectedSeatKeys = new(StringComparer.Ordinal);
    private bool _isSynchronizingSeatSelection;
    private readonly HashSet<int> _committedGlobalLeakLibraryIds = [];
    private readonly HashSet<int> _draftGlobalLeakLibraryIds = [];
    private bool _isSynchronizingGlobalLeakLibrarySelection;
    private bool _globalLeakSelectionRestoredForCurrentSession;
    private CoordinatorTaskState _grabTaskState = CoordinatorTaskState.Idle;
    private CoordinatorStatusReason _grabStatusReason = CoordinatorStatusReason.None;
    private CoordinatorTaskState _globalLeakTaskState = CoordinatorTaskState.Idle;
    private CoordinatorStatusReason _globalLeakStatusReason = CoordinatorStatusReason.None;
    private CoordinatorTaskState _tomorrowTaskState = CoordinatorTaskState.Idle;
    private CoordinatorStatusReason _tomorrowStatusReason = CoordinatorStatusReason.None;
    private DateTimeOffset? _grabLastRequestAt;
    private DateTimeOffset? _globalLeakLastRequestAt;
    private DateTimeOffset? _tomorrowLastRequestAt;
    private DateTimeOffset? _grabRuntimeStartedAt;
    private DateTimeOffset? _globalLeakRuntimeStartedAt;
    private int _historicalSuccessCount;
    private long _totalGuardSeconds;
    private DateTimeOffset? _guardTrackingStartedAt;
    private DateTimeOffset? _lastRecordedGrabSuccessAt;
    private DateTimeOffset? _lastRecordedGlobalLeakSuccessAt;
    private DateTimeOffset? _lastRecordedOccupySuccessAt;
    private DateTimeOffset? _lastRecordedTomorrowSuccessAt;
    private string? _homeReservationProgressReservationIdentity;
    private DateTimeOffset? _homeReservationProgressExpirationTime;
    private DateTimeOffset? _homeReservationProgressStartedAt;
    private string? _homeCookieIdentity;
    private DateTimeOffset? _homeCookieExpirationTime;
    private string? _homeCookieProgressCookieIdentity;
    private DateTimeOffset? _homeCookieProgressExpirationTime;
    private DateTimeOffset? _homeCookieProgressStartedAt;
    private bool _isSynchronizingSidebarSelection;
    private bool _isLoadingSettings;
    private bool _isRollingBackStartupEntry;
    private TimeSpan _grabScheduledStartDefault = DefaultGrabScheduledStartTime;
    private TimeSpan _tomorrowScheduledStartDefault = DefaultTomorrowScheduledStartTime;
    private TimeSpan? _pendingGrabScheduledStartDefault;
    private TimeSpan? _pendingTomorrowScheduledStartDefault;
    private CancellationTokenSource? _grabScheduledStartDefaultAutoSaveCts;
    private CancellationTokenSource? _tomorrowScheduledStartDefaultAutoSaveCts;
    private bool _isSynchronizingTomorrowSeatSelection;
    private string? _draftTomorrowSeatKey;
    private bool _notificationSettingsLoaded;
    private CancellationTokenSource? _notificationSettingsAutoSaveCts;
    private CancellationTokenSource? _systemSettingsAutoSaveCts;
    private CancellationTokenSource? _protocolTemplateAutoSaveCts;
    private bool _isLoadingProtocolTemplates;
    private bool _themePaletteSubscribed;
    private readonly object _processedAuthCodesGate = new();
    private readonly HashSet<string> _processedAuthCodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _inFlightAuthCodes = new(StringComparer.OrdinalIgnoreCase);

    public HomeDashboardViewModel HomeDashboard { get; } = new();

    public AccountVenueViewModel AccountVenue { get; } = new(sessionWorkflowService, venueWorkflowService);

    public GrabPageViewModel GrabPage { get; } = new(grabSeatCoordinator, settingsWorkflowService);

    public GlobalLeakPageViewModel GlobalLeakPage { get; } = new(globalLeakCoordinator);

    public OccupyPageViewModel OccupyPage { get; } = new(occupySeatCoordinator, reservationWorkflowService);

    public TomorrowReservationPageViewModel TomorrowReservationPage { get; } = new(tomorrowReservationCoordinator);

    public NotificationSettingsViewModel NotificationSettings { get; } = new(settingsWorkflowService, notificationTestService);

    public SystemSettingsViewModel SystemSettings { get; } = new(settingsWorkflowService, protocolTemplateEditorService);

    public ObservableCollection<LibrarySummary> AvailableLibraries { get; } = [];

    public ObservableCollection<SidebarNavigationItem> SidebarItems { get; } =
    [
        HomeSidebarItem,
        AccountAndVenueSidebarItem,
        SettingsSidebarItem
    ];

    public ObservableCollection<SeatItemViewModel> VisibleSeats { get; } = [];

    public ObservableCollection<SeatItemViewModel> TomorrowVisibleSeats { get; } = [];

    public ObservableCollection<SeatReference> SelectedSeats { get; } = [];

    public ObservableCollection<GlobalLeakLibraryItemViewModel> GlobalLeakLibraries { get; } = [];

    public ObservableCollection<GlobalLeakLibraryTarget> SelectedGlobalLeakLibraries { get; } = [];

    public ObservableCollection<LogLineViewModel> OccupyLogLines { get; } = [];

    public string[] GrabPollingModes { get; } = ["极限速度", "随机延迟", "延迟 5 秒"];

    public string[] OccupyCheckIntervalModes { get; } = ["固定间隔 10 秒", "随机 10~20 秒"];

    public string[] GrabReservationStrategies { get; } = ["先获取列表判断状态", "直接发送预约请求"];

    public string[] EmailSecurityModes { get; } = ["无", "TLS"];

    public string[] ThemeModes { get; } = ["跟随系统", "浅色", "深色"];

    public string[] HomeReservationProgressTimingModes { get; } = ["固定预约到期时长", "软件运行时计算时长"];

    public string[] HomeCookieProgressTimingModes { get; } = ["固定 Cookie 有效时长", "软件运行时计算时长"];

    public string[] SystemSettingsCategories { get; } = ["常规", "外观", "网络与接口", "存储与更新"];

    public string[] NotificationSettingsCategories { get; } = ["通知事件开关", "邮件提醒配置", "Telegram Bot 配置", "弹窗提醒配置"];

    private DateTimeOffset GetCurrentTime()
    {
        return _timeProvider.GetUtcNow().ToLocalTime();
    }

    public string CurrentAppVersionText { get; } = $"v{appVersionProvider.CurrentVersionText}";

    public const string ProjectGitHubUrl = "https://github.com/EJianZQ/IGoLibrary";

    public const string AuthorSponsorUrl = "https://latiao.vip/%E9%A1%B9%E7%9B%AE%E5%8F%91%E5%B8%83/14.html";

    public const string ProjectAuthorName = "EJianZQ";

    public const string ProjectAuthorAvatarUrl = "https://avatars.githubusercontent.com/u/52780714";

    public bool HasProjectAuthorAvatar => ProjectAuthorAvatar is not null;

    public bool HasNoProjectAuthorAvatar => !HasProjectAuthorAvatar;

    public const int AccountAndVenueTabIndex = 1;

    [ObservableProperty]
    private int selectedTabIndex;

    [ObservableProperty]
    private SidebarNavigationItem? selectedSidebarItem = HomeSidebarItem;

    public bool IsAccountAndVenuePageActive => SelectedTabIndex == AccountAndVenueTabIndex;

    partial void OnSelectedTabIndexChanged(int value)
    {
        if (!IsAuthorized && !IsTabAvailableWithoutAuthorization(value))
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

    private static bool IsTabAvailableWithoutAuthorization(int tabIndex)
    {
        return tabIndex <= AccountAndVenueTabIndex || tabIndex == SystemSettingsTabIndex;
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
    private double homeReservationProgressValue;

    [ObservableProperty]
    private IBrush homeReservationProgressBrush = appThemeService.CurrentPalette.IdleBrush;

    [ObservableProperty]
    private bool hasCurrentCookie;

    public bool HasNoCurrentCookie => !HasCurrentCookie;

    [ObservableProperty]
    private string homeCookieExpirationTimeText = "--:--:--";

    [ObservableProperty]
    private string homeCookieRemainingText = "--";

    [ObservableProperty]
    private string homeCookieBadgeText = "未登录";

    [ObservableProperty]
    private IBrush homeCookieBadgeBrush = appThemeService.CurrentPalette.IdleBrush;

    [ObservableProperty]
    private IBrush homeCookieBadgeBackgroundBrush = appThemeService.CurrentPalette.NeutralSoftBrush;

    [ObservableProperty]
    private double homeCookieProgressValue;

    [ObservableProperty]
    private IBrush homeCookieProgressBrush = appThemeService.CurrentPalette.IdleBrush;

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
    private bool isGlobalLeakLibraryPickerOpen;

    [ObservableProperty]
    private bool isApplyingSeatFilter;

    [ObservableProperty]
    private int visibleSeatResultCount;

    [ObservableProperty]
    private int selectedGrabPollingModeIndex = 2;

    [ObservableProperty]
    private int selectedGrabReservationStrategyIndex;

    [ObservableProperty]
    private bool isGrabScheduledStartEnabled;

    [ObservableProperty]
    private TimeSpan? scheduledStartTime = DefaultGrabScheduledStartTime;

    [ObservableProperty]
    private TimeSpan? tomorrowScheduledStartTime = DefaultTomorrowScheduledStartTime;

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
    private string globalLeakStatusText = "未运行";

    [ObservableProperty]
    private bool isGlobalLeakTaskActive;

    [ObservableProperty]
    private int globalLeakScanRoundCount;

    [ObservableProperty]
    private int globalLeakRequestCount;

    [ObservableProperty]
    private string globalLeakLastRequestText = "无";

    [ObservableProperty]
    private string globalLeakRuntimeText = "00:00:00";

    [ObservableProperty]
    private int globalLeakScanIntervalSeconds = 10;

    [ObservableProperty]
    private string globalLeakLogsText = string.Empty;

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
    private int selectedSystemSettingsCategoryIndex;

    public bool IsSystemSettingsGeneralActive => SelectedSystemSettingsCategoryIndex == 0;

    public bool IsSystemSettingsAppearanceActive => SelectedSystemSettingsCategoryIndex == 1;

    public bool IsSystemSettingsNetworkActive => SelectedSystemSettingsCategoryIndex == 2;

    public bool IsSystemSettingsStorageUpdateActive => SelectedSystemSettingsCategoryIndex == 3;

    public bool LaunchOnStartupSupported => _startupEntryService.IsSupported;

    partial void OnSelectedSystemSettingsCategoryIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsSystemSettingsGeneralActive));
        OnPropertyChanged(nameof(IsSystemSettingsAppearanceActive));
        OnPropertyChanged(nameof(IsSystemSettingsNetworkActive));
        OnPropertyChanged(nameof(IsSystemSettingsStorageUpdateActive));
    }

    [ObservableProperty]
    private bool minimizeToTrayEnabled = true;

    partial void OnMinimizeToTrayEnabledChanged(bool value) => ScheduleSystemSettingsAutoSave();

    [ObservableProperty]
    private bool launchOnStartupEnabled;

    partial void OnLaunchOnStartupEnabledChanged(bool value)
    {
        // During rollback (OS operation failed), don't re-attempt the OS operation
        // to prevent infinite toggle loops.
        if (_isRollingBackStartupEntry)
        {
            ScheduleSystemSettingsAutoSave();
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

            if (!_isLoadingSettings && IsInitializationComplete)
            {
                _ = NotifyLaunchOnStartupUnsupportedAsync();
            }

            return;
        }

        ScheduleSystemSettingsAutoSave();
        _ = ApplyLaunchOnStartupEntryAsync(value);
    }

    [ObservableProperty]
    private bool autoReleaseReservationEnabled;

    partial void OnAutoReleaseReservationEnabledChanged(bool value)
    {
        ScheduleSystemSettingsAutoSave();
        OnPropertyChanged(nameof(AutoReleaseStatusText));
        QueueAutoReleaseReservationRefresh();
        QueueAutoReleaseCheck();
    }

    [ObservableProperty]
    private int autoReleaseLeadSeconds = AutoReleaseTaskSettings.DefaultLeadSeconds;

    partial void OnAutoReleaseLeadSecondsChanged(int value)
    {
        var normalized = AutoReleaseTaskSettings.NormalizeLeadSeconds(value);
        if (normalized != value)
        {
            AutoReleaseLeadSeconds = normalized;
            return;
        }

        ScheduleSystemSettingsAutoSave();
        OnPropertyChanged(nameof(AutoReleaseStatusText));
        QueueAutoReleaseReservationRefresh();
        QueueAutoReleaseCheck();
    }

    [ObservableProperty]
    private bool traceIntGraphQlOverridesEnabled;

    partial void OnTraceIntGraphQlOverridesEnabledChanged(bool value) => ScheduleSystemSettingsAutoSave();

    [ObservableProperty]
    private bool checkUpdatesOnStartup = true;

    partial void OnCheckUpdatesOnStartupChanged(bool value) => ScheduleSystemSettingsAutoSave();

    [ObservableProperty]
    private int requestTimeoutSeconds = 5;

    partial void OnRequestTimeoutSecondsChanged(int value)
    {
        var normalized = Math.Clamp(value, 3, 60);
        if (normalized != value)
        {
            RequestTimeoutSeconds = normalized;
            return;
        }

        ScheduleSystemSettingsAutoSave();
    }

    [ObservableProperty]
    private int networkMaxRetries = 3;

    partial void OnNetworkMaxRetriesChanged(int value)
    {
        var normalized = Math.Clamp(value, 0, 10);
        if (normalized != value)
        {
            NetworkMaxRetries = normalized;
            return;
        }

        ScheduleSystemSettingsAutoSave();
    }

    [ObservableProperty]
    private int selectedAppThemeModeIndex;

    [ObservableProperty]
    private int selectedHomeReservationProgressTimingModeIndex;

    public bool IsHomeReservationFixedProgressMode =>
        CurrentHomeReservationProgressTimingMode == HomeReservationProgressTimingMode.FixedReservationDuration;

    partial void OnSelectedHomeReservationProgressTimingModeIndexChanged(int value)
    {
        var normalized = Math.Clamp(value, 0, HomeReservationProgressTimingModes.Length - 1);
        if (normalized != value)
        {
            SelectedHomeReservationProgressTimingModeIndex = normalized;
            return;
        }

        OnPropertyChanged(nameof(IsHomeReservationFixedProgressMode));
        ScheduleSystemSettingsAutoSave();
        UpdateHomeReservationCardPresentation(GetCurrentTime());
    }

    [ObservableProperty]
    private int homeReservationFixedDurationMinutes =
        HomeReservationProgressSettings.DefaultFixedDurationMinutes;

    partial void OnHomeReservationFixedDurationMinutesChanged(int value)
    {
        var normalized = HomeReservationProgressSettings.NormalizeFixedDurationMinutes(value);
        if (normalized != value)
        {
            HomeReservationFixedDurationMinutes = normalized;
            return;
        }

        ScheduleSystemSettingsAutoSave();
        UpdateHomeReservationCardPresentation(GetCurrentTime());
    }

    [ObservableProperty]
    private int selectedHomeCookieProgressTimingModeIndex;

    public bool IsHomeCookieFixedProgressMode =>
        CurrentHomeCookieProgressTimingMode == HomeCookieProgressTimingMode.FixedCookieDuration;

    partial void OnSelectedHomeCookieProgressTimingModeIndexChanged(int value)
    {
        var normalized = Math.Clamp(value, 0, HomeCookieProgressTimingModes.Length - 1);
        if (normalized != value)
        {
            SelectedHomeCookieProgressTimingModeIndex = normalized;
            return;
        }

        OnPropertyChanged(nameof(IsHomeCookieFixedProgressMode));
        ScheduleSystemSettingsAutoSave();
        UpdateHomeCookieCardPresentation(GetCurrentTime());
    }

    [ObservableProperty]
    private int homeCookieFixedDurationMinutes =
        HomeCookieProgressSettings.DefaultFixedDurationMinutes;

    partial void OnHomeCookieFixedDurationMinutesChanged(int value)
    {
        var normalized = HomeCookieProgressSettings.NormalizeFixedDurationMinutes(value);
        if (normalized != value)
        {
            HomeCookieFixedDurationMinutes = normalized;
            return;
        }

        ScheduleSystemSettingsAutoSave();
        UpdateHomeCookieCardPresentation(GetCurrentTime());
    }

    [ObservableProperty]
    private bool isCheckingForUpdates;

    [ObservableProperty]
    private IImage? projectAuthorAvatar;

    partial void OnProjectAuthorAvatarChanged(IImage? value)
    {
        OnPropertyChanged(nameof(HasProjectAuthorAvatar));
        OnPropertyChanged(nameof(HasNoProjectAuthorAvatar));
    }

    public bool CanCheckForUpdates => !IsCheckingForUpdates;

    public string CheckForUpdatesButtonText => IsCheckingForUpdates ? "正在检查..." : "立即检查更新";

    partial void OnIsCheckingForUpdatesChanged(bool value)
    {
        OnPropertyChanged(nameof(CanCheckForUpdates));
        OnPropertyChanged(nameof(CheckForUpdatesButtonText));
    }

    partial void OnSelectedAppThemeModeIndexChanged(int value)
    {
        PreviewThemePreferences();
        ScheduleSystemSettingsAutoSave();
    }

    [ObservableProperty]
    private bool useSystemAccent = OperatingSystem.IsWindows();

    partial void OnUseSystemAccentChanged(bool value)
    {
        PreviewThemePreferences();
        ScheduleSystemSettingsAutoSave();
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
    private bool grabSucceededAlertsEnabled = true;

    [ObservableProperty]
    private bool occupyReReserveSucceededAlertsEnabled = true;

    [ObservableProperty]
    private bool tomorrowReservationSucceededAlertsEnabled = true;

    [ObservableProperty]
    private bool globalLeakSucceededAlertsEnabled = true;

    [ObservableProperty]
    private bool sessionInvalidAlertsEnabled = true;

    [ObservableProperty]
    private bool taskFailedAlertsEnabled = true;

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
