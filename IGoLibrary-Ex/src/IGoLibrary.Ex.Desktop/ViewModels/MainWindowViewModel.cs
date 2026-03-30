using System.Collections.ObjectModel;
using System.Diagnostics;
using System.ComponentModel;
using System.Text;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IGoLibrary.Ex.Application.Abstractions;
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
    IActivityLogService activityLogService,
    INotificationService notificationService,
    AppWindowService appWindowService) : ViewModelBase
{
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

    public ObservableCollection<LibrarySummary> AvailableLibraries { get; } = [];

    public ObservableCollection<SeatItemViewModel> VisibleSeats { get; } = [];

    public ObservableCollection<string> SelectedSeatNames { get; } = [];

    public ObservableCollection<LogLineViewModel> OccupyLogLines { get; } = [];

    public string[] GrabModes { get; } = ["极限速度", "随机延迟", "延迟 5 秒"];

    public string[] RefreshModes { get; } = ["固定间隔 10 秒", "随机 10~20 秒"];

    public string[] GrabReservationStrategies { get; } = ["先查列表再预约", "直接预约看返回值"];

    public const int AccountAndVenueTabIndex = 1;

    [ObservableProperty]
    private int selectedTabIndex;

    public bool IsAccountAndVenuePageActive => SelectedTabIndex == AccountAndVenueTabIndex;

    [ObservableProperty]
    private string sessionSummary = "未登录";

    [ObservableProperty]
    private bool isAuthorized;

    public string AuthorizationStatusText => IsAuthorized ? "已授权" : "未授权";

    public bool IsUnauthorized => !IsAuthorized;

    [ObservableProperty]
    private bool isInitializationComplete;

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
    private int selectedGrabModeIndex = 2;

    [ObservableProperty]
    private int selectedGrabReservationStrategyIndex;

    [ObservableProperty]
    private string scheduledTimeText = "00:00:00";

    [ObservableProperty]
    private string grabStatusText = "未运行";

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
    private bool notificationsEnabled = true;

    [ObservableProperty]
    private bool minimizeToTrayEnabled = true;

    [ObservableProperty]
    private bool advancedMode;

    [ObservableProperty]
    private int apiTimeoutSeconds = 5;

    [ObservableProperty]
    private int retryCount = 3;

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

    public int SelectedSeatCount => SelectedSeatNames.Count;

    public bool HasSelectedSeats => SelectedSeatCount > 0;

    public bool HasNoSelectedSeats => !HasSelectedSeats;

    public string SelectedSeatSummaryText => HasSelectedSeats
        ? $"已选 {SelectedSeatCount} 个目标座位"
        : "尚未选择监控目标";

    public string SelectedSeatHintText => HasSelectedSeats
        ? "这些座位会被持续监控，任意一个释放后都会立即尝试预约。"
        : "在左侧勾选要监控的座位，右侧会始终显示当前选择。";

    partial void OnIsOccupyRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(IsOccupyStopped));
    }

    partial void OnIsAuthorizedChanged(bool value)
    {
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
        activityLogService.EntryWritten += OnLogEntryWritten;
        grabSeatCoordinator.StatusChanged += OnGrabStatusChanged;
        occupySeatCoordinator.StatusChanged += OnOccupyStatusChanged;

        if (!_reservationCountdownTimerInitialized)
        {
            _reservationCountdownTimerInitialized = true;
            _reservationCountdownTimer.Tick += OnReservationCountdownTick;
            _reservationCountdownTimer.Start();
        }

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
        }
    }

    partial void OnSeatFilterTextChanged(string value) => _ = ApplySeatFilterAsync();

    partial void OnShowAvailableOnlyChanged(bool value) => _ = ApplySeatFilterAsync();

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

            var cookie = await apiClient.GetCookieFromCodeAsync(code);
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
        AvailableLibraries.Clear();
        _allSeats.Clear();
        VisibleSeats.Clear();
        UpdateSelectedSeatSummary();
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
        UpdateReservationPresentation(null);
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
            UpdateBoundLibraryPresentation(layout);
            await LoadVenueRulePresentationAsync(SelectedLibrary.LibraryId, persistLockedSnapshot: true);
            await PopulateSeatsAsync(layout);
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
            await PopulateSeatsAsync(layout);
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
        foreach (var seat in _allSeats.Where(x => x.IsSelected))
        {
            seat.IsSelected = false;
        }

        UpdateSelectedSeatSummary();
    }

    [RelayCommand]
    private async Task StartGrabAsync()
    {
        if (SelectedLibrary is null)
        {
            await notificationService.ShowWarningAsync("未绑定场馆", "请先绑定场馆。");
            return;
        }

        var selectedSeats = _allSeats
            .Where(x => x.IsSelected)
            .Select(x => new TrackedSeat(x.SeatKey, x.SeatName))
            .ToList();
        if (selectedSeats.Count == 0)
        {
            await notificationService.ShowWarningAsync("未选择座位", "请至少选中一个目标座位。");
            return;
        }

        try
        {
            var mode = (GrabMode)SelectedGrabModeIndex;
            var scheduledStart = ParseScheduledTime();
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
        var settings = new AppSettings(
            NotificationsEnabled,
            MinimizeToTrayEnabled,
            AdvancedMode,
            Math.Max(3, ApiTimeoutSeconds),
            Math.Max(1, RetryCount),
            (GrabReservationStrategy)Math.Clamp(SelectedGrabReservationStrategyIndex, 0, GrabReservationStrategies.Length - 1),
            SelectedLibrary?.LibraryId,
            SelectedLibrary?.Name);
        await settingsService.SaveAsync(settings);
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

    private async Task LoadSettingsAsync()
    {
        var settings = await settingsService.LoadAsync();
        NotificationsEnabled = settings.NotificationsEnabled;
        MinimizeToTrayEnabled = settings.MinimizeToTray;
        AdvancedMode = settings.AdvancedMode;
        ApiTimeoutSeconds = settings.ApiTimeoutSeconds;
        RetryCount = settings.RetryCount;
        SelectedGrabReservationStrategyIndex = (int)settings.GrabReservationStrategy;
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
    }

    private string GetUnboundVenueFloorText()
    {
        return IsAuthorized ? "等待绑定场馆后获取" : "等待授权并绑定场馆";
    }

    private async Task PopulateSeatsAsync(LibraryLayout layout)
    {
        CancelFiltering();

        foreach (var seat in _allSeats)
        {
            seat.PropertyChanged -= OnSeatItemPropertyChanged;
        }

        _allSeats.Clear();
        VisibleSeats.Clear();
        foreach (var seat in layout.Seats)
        {
            var item = new SeatItemViewModel(seat.SeatKey, seat.SeatName, seat.IsOccupied);
            item.PropertyChanged += OnSeatItemPropertyChanged;
            _allSeats.Add(item);
            VisibleSeats.Add(item);
        }

        await ApplySeatFilterAsync();
        UpdateSelectedSeatSummary();
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

            cts.Dispose();
        }
    }

    private void OnSeatItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SeatItemViewModel.IsSelected))
        {
            UpdateSelectedSeatSummary();
        }
    }

    private void UpdateSelectedSeatSummary()
    {
        SelectedSeatNames.Clear();

        foreach (var seat in _allSeats
                     .Where(x => x.IsSelected)
                     .OrderBy(x => int.TryParse(x.SeatName, out var number) ? number : int.MaxValue)
                     .ThenBy(x => x.SeatName, StringComparer.OrdinalIgnoreCase))
        {
            SelectedSeatNames.Add(seat.SeatName);
        }

        OnPropertyChanged(nameof(SelectedSeatCount));
        OnPropertyChanged(nameof(HasSelectedSeats));
        OnPropertyChanged(nameof(HasNoSelectedSeats));
        OnPropertyChanged(nameof(SelectedSeatSummaryText));
        OnPropertyChanged(nameof(SelectedSeatHintText));
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
            UpdateSelectedSeatSummary();
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
                    hasFailureSemantic));

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
        Dispatcher.UIThread.Post(() => GrabStatusText = status.Message);

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
        Dispatcher.UIThread.Post(() =>
        {
            OccupyStatusText = status.Message;
            IsOccupyRunning = IsTaskActive(status);
        });
    }

    private void OnReservationCountdownTick(object? sender, EventArgs e)
    {
        UpdateReservationCountdown();
    }

    private void UpdateReservationPresentation(ReservationInfo? info)
    {
        _currentReservation = info;

        if (info is null)
        {
            ReservationSummary = "暂无预约";
            ReservationHeroTitle = "暂无预约";
            ReservationExpiryText = "到期：--:--:--";
            ReservationCountdownText = "等待建立预约状态";
            return;
        }

        ReservationSummary = $"{info.LibraryName} / {info.SeatName} / 到期 {info.ExpirationTime:HH:mm:ss}";
        ReservationHeroTitle = $"{info.LibraryName} · {info.SeatName}";
        UpdateReservationCountdown();
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

    private sealed record SeatFilterSnapshot(
        SeatItemViewModel ViewModel,
        string SeatName,
        bool IsOccupied);

    private sealed record SeatFilterResult(
        SeatItemViewModel ViewModel,
        bool IsVisible);
}
