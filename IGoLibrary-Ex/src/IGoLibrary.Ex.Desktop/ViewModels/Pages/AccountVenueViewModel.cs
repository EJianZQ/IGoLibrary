using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Desktop.ViewModels;

public sealed partial class AccountVenueViewModel : ViewModelBase
{
    private readonly ISessionWorkflowService _sessionWorkflowService;
    private readonly IVenueWorkflowService _venueWorkflowService;
    private readonly ISettingsWorkflowService _settingsWorkflowService;
    private readonly IActivityLogService _activityLogService;
    private readonly INotificationService _notificationService;
    private LibrarySummary? _lockedLibrarySummary;
    private string _lockedVenueStatusText = "未绑定";
    private bool _lockedVenueOpen;
    private string _lockedVenueName = "未锁定场馆";
    private string _lockedVenueFloor = "等待授权并绑定场馆";
    private string _lockedVenueAvailableSeatsText = "--";
    private string _lockedVenueOpenTimeText = "--";
    private string _lockedVenueCloseTimeText = "--";
    private Func<bool>? _isAuthorized;
    private Func<bool>? _hasCurrentCookie;
    private Func<DateTimeOffset?>? _homeCookieExpirationTime;
    private Func<IReadOnlyList<LibrarySummary>, Task>? _librariesLoadedAsync;
    private Func<VenueBindingResult, bool, Task>? _layoutLoadedAsync;
    private Func<Task>? _refreshReservationAsync;
    private Action? _venuePreviewChanged;
    private Action? _venuePresentationChanged;
    private IBrush _successBrush;
    private IBrush _successSoftBrush;
    private IBrush _warningBrush;
    private IBrush _warningSoftBrush;

    public AccountVenueViewModel(
        ISessionWorkflowService sessionWorkflowService,
        IVenueWorkflowService venueWorkflowService,
        ISettingsWorkflowService settingsWorkflowService,
        IActivityLogService activityLogService,
        INotificationService notificationService,
        IAppThemeService appThemeService)
    {
        _sessionWorkflowService = sessionWorkflowService;
        _venueWorkflowService = venueWorkflowService;
        _settingsWorkflowService = settingsWorkflowService;
        _activityLogService = activityLogService;
        _notificationService = notificationService;

        var palette = appThemeService.CurrentPalette;
        _successBrush = palette.SuccessBrush;
        _successSoftBrush = palette.SuccessSoftBrush;
        _warningBrush = palette.WarningBrush;
        _warningSoftBrush = palette.WarningSoftBrush;
        HomeLockedVenueStateBrush = _warningBrush;
        HomeLockedVenueStateBackgroundBrush = _warningSoftBrush;
    }

    public ObservableCollection<LibrarySummary> AvailableLibraries { get; } = [];

    public LibrarySummary? LockedLibrary => _lockedLibrarySummary;

    public bool CanShowVenueConfiguration => IsAuthorized() && (_homeCookieExpirationTime?.Invoke() is null || _hasCurrentCookie?.Invoke() == true);

    [ObservableProperty]
    private string homeLockedVenueTitle = "尚未锁定场馆";

    [ObservableProperty]
    private string homeLockedVenueStateText = "待授权";

    [ObservableProperty]
    private IBrush homeLockedVenueStateBrush;

    [ObservableProperty]
    private IBrush homeLockedVenueStateBackgroundBrush;

    [ObservableProperty]
    private string homeReservationVenueText = "当前暂无预约记录";

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

    [ObservableProperty]
    private LibrarySummary? selectedLibrary;

    public bool IsCurrentPreview => !IsCurrentLocked;

    public bool HasLockedVenue => _lockedLibrarySummary is not null;

    public bool CanCancelVenuePreview => HasActiveVenuePreview && _lockedLibrarySummary is not null;

    public bool ShowVenueChangeButton => IsCurrentLocked || !HasLockedVenue;

    public bool ShowVenueCancelPreviewButton => HasActiveVenuePreview && HasLockedVenue;

    public bool ShowVenuePreviewStateTag => IsAuthorized() && HasActiveVenuePreview;

    public bool ShowVenueOpenStatusTag => IsAuthorized() && IsVenueOpen;

    public bool ShowVenueClosedStatusTag =>
        IsAuthorized() &&
        IsVenueClosed &&
        !string.Equals(VenueStatusText, "未绑定", StringComparison.Ordinal);

    public string CurrentVenueLockStateText => IsCurrentLocked ? "🔒 当前已锁定" : "👀 预览中 (未锁定)";

    public string LockVenueButtonText => IsCurrentLocked ? "当前场馆已锁定" : "保存并锁定该场馆";

    public void ConfigureOrchestration(
        Func<bool> isAuthorized,
        Func<bool> hasCurrentCookie,
        Func<DateTimeOffset?> homeCookieExpirationTime,
        Func<IReadOnlyList<LibrarySummary>, Task> librariesLoadedAsync,
        Func<VenueBindingResult, bool, Task> layoutLoadedAsync,
        Func<Task> refreshReservationAsync,
        Action venuePreviewChanged,
        Action venuePresentationChanged)
    {
        _isAuthorized = isAuthorized;
        _hasCurrentCookie = hasCurrentCookie;
        _homeCookieExpirationTime = homeCookieExpirationTime;
        _librariesLoadedAsync = librariesLoadedAsync;
        _layoutLoadedAsync = layoutLoadedAsync;
        _refreshReservationAsync = refreshReservationAsync;
        _venuePreviewChanged = venuePreviewChanged;
        _venuePresentationChanged = venuePresentationChanged;
    }

    public void ApplyThemePalette(AppThemePalette palette)
    {
        _successBrush = palette.SuccessBrush;
        _successSoftBrush = palette.SuccessSoftBrush;
        _warningBrush = palette.WarningBrush;
        _warningSoftBrush = palette.WarningSoftBrush;
        UpdateHomeLockedVenuePresentation();
    }

    public void NotifyAuthorizationStateChanged()
    {
        OnPropertyChanged(nameof(CanShowVenueConfiguration));
        OnPropertyChanged(nameof(ShowVenuePreviewStateTag));
        OnPropertyChanged(nameof(ShowVenueOpenStatusTag));
        OnPropertyChanged(nameof(ShowVenueClosedStatusTag));
        UpdateHomeLockedVenuePresentation();
    }

    public void RefreshHomeLockedVenuePresentation()
    {
        UpdateHomeLockedVenuePresentation();
    }

    public Task<SessionWorkflowResult> AuthenticateFromCodeAsync(
        string code,
        bool remember,
        CancellationToken cancellationToken = default)
    {
        return _sessionWorkflowService.AuthenticateFromCodeAsync(code, remember, cancellationToken);
    }

    public Task<SessionWorkflowResult> AuthenticateFromCookieAsync(
        string cookie,
        bool remember,
        CancellationToken cancellationToken = default)
    {
        return _sessionWorkflowService.AuthenticateFromCookieAsync(cookie, remember, cancellationToken);
    }

    public Task<SessionWorkflowResult> RestoreSessionAsync(CancellationToken cancellationToken = default)
    {
        return _sessionWorkflowService.RestoreAsync(cancellationToken);
    }

    public Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        return _sessionWorkflowService.SignOutAsync(cancellationToken);
    }

    public Task<LibraryRule?> LoadLibraryRuleAsync(
        int libraryId,
        CancellationToken cancellationToken = default)
    {
        return _venueWorkflowService.LoadLibraryRuleAsync(libraryId, cancellationToken);
    }

    [RelayCommand]
    private async Task LoadLibrariesAsync()
    {
        await LoadLibrariesAsync(restorePreferredSelection: true);
    }

    public async Task LoadLibrariesAsync(bool restorePreferredSelection, int? preferredLibraryId = null)
    {
        try
        {
            var result = await _venueWorkflowService.LoadLibrariesAsync(
                restorePreferredSelection,
                preferredLibraryId);
            AvailableLibraries.Clear();
            foreach (var library in result.Libraries)
            {
                AvailableLibraries.Add(library);
            }

            if (_librariesLoadedAsync is not null)
            {
                await _librariesLoadedAsync(result.Libraries);
            }

            SelectedLibrary = result.SelectedLibrary;
        }
        catch (Exception ex)
        {
            _activityLogService.Write(LogEntryKind.Error, "Library", $"加载场馆列表失败：{ex.Message}", ex);
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

            var result = await _venueWorkflowService.BindLibraryAsync(SelectedLibrary.LibraryId);
            var preserveSelection = _lockedLibrarySummary?.LibraryId == SelectedLibrary.LibraryId;
            UpdateBoundLibraryPresentation(result.Layout);
            ApplyVenueRuleResult(result.Rule, result.RuleFailureMessage, persistLockedSnapshot: true);
            if (_layoutLoadedAsync is not null)
            {
                await _layoutLoadedAsync(result, preserveSelection);
            }

            if (result.Favorites.Count > 0)
            {
                await _notificationService.ShowInfoAsync("收藏已加载", $"已加载 {result.Favorites.Count} 个收藏座位");
            }

            if (_refreshReservationAsync is not null)
            {
                await _refreshReservationAsync();
            }
        }
        catch (Exception ex)
        {
            _activityLogService.Write(LogEntryKind.Error, "Library", $"绑定场馆失败：{ex.Message}", ex);
            await _notificationService.ShowWarningAsync("绑定场馆失败", ex.Message);
        }
    }

    [RelayCommand]
    private async Task RefreshSeatsAsync()
    {
        try
        {
            var result = await _venueWorkflowService.RefreshBoundLibraryAsync();
            UpdateBoundLibraryPresentation(result.Layout);
            if (result.Rule is not null || !string.IsNullOrWhiteSpace(result.RuleFailureMessage))
            {
                ApplyVenueRuleResult(result.Rule, result.RuleFailureMessage, persistLockedSnapshot: true);
            }

            if (_layoutLoadedAsync is not null)
            {
                await _layoutLoadedAsync(result, true);
            }

            if (result.Favorites.Count > 0)
            {
                await _notificationService.ShowInfoAsync("收藏已加载", $"已加载 {result.Favorites.Count} 个收藏座位");
            }
        }
        catch (Exception ex)
        {
            _activityLogService.Write(LogEntryKind.Error, "Library", $"刷新座位失败：{ex.Message}", ex);
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

    public async Task ClearStoredLibrarySelectionAsync()
    {
        try
        {
            await _settingsWorkflowService.ClearStoredLibrarySelectionAsync();
        }
        catch (Exception ex)
        {
            _activityLogService.Write(LogEntryKind.Warning, "Auth", $"清理上次场馆选择失败：{ex.Message}", ex);
        }
    }

    public async Task HandleVenuePickerLibraryClickAsync(LibrarySummary library)
    {
        if (!IsAuthorized())
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

    public void ClearVenueState()
    {
        AvailableLibraries.Clear();
        SelectedLibrary = null;
        UpdateBoundLibraryPresentation(null);
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
        _venuePreviewChanged?.Invoke();
    }

    partial void OnSelectedLibraryChanged(LibrarySummary? value)
    {
        if (!IsVenuePickerOpen || value is null || !IsAuthorized())
        {
            return;
        }

        _ = PreviewSelectedLibraryAsync(value);
    }

    private async Task PreviewSelectedLibraryAsync(LibrarySummary library)
    {
        try
        {
            var result = await _venueWorkflowService.PreviewLibraryAsync(library);
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
            _activityLogService.Write(LogEntryKind.Error, "Library", $"预览场馆失败：{ex.Message}", ex);
            await _notificationService.ShowWarningAsync("预览场馆失败", ex.Message);
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
            OnLockedVenueChanged();
            UpdateHomeLockedVenuePresentation();
            _venuePresentationChanged?.Invoke();
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
        OnLockedVenueChanged();
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
        OnPropertyChanged(nameof(LockedLibrary));
        UpdateHomeLockedVenuePresentation();
        _venuePresentationChanged?.Invoke();
    }

    private void OnLockedVenueChanged()
    {
        OnPropertyChanged(nameof(LockedLibrary));
        OnPropertyChanged(nameof(HasLockedVenue));
        OnPropertyChanged(nameof(ShowVenueChangeButton));
        OnPropertyChanged(nameof(ShowVenueCancelPreviewButton));
        OnPropertyChanged(nameof(CanCancelVenuePreview));
    }

    private string GetUnboundVenueFloorText()
    {
        return IsAuthorized() ? "等待绑定场馆后获取" : "等待授权并绑定场馆";
    }

    private void UpdateHomeLockedVenuePresentation()
    {
        if (!IsAuthorized())
        {
            HomeLockedVenueTitle = "尚未锁定场馆";
            HomeLockedVenueStateText = "待授权";
            HomeLockedVenueStateBrush = _warningBrush;
            HomeLockedVenueStateBackgroundBrush = _warningSoftBrush;
            return;
        }

        HomeLockedVenueStateText = "已授权";
        HomeLockedVenueStateBrush = _successBrush;
        HomeLockedVenueStateBackgroundBrush = _successSoftBrush;

        if (!HasLockedVenue)
        {
            HomeLockedVenueTitle = "尚未锁定场馆";
            return;
        }

        HomeLockedVenueTitle = string.IsNullOrWhiteSpace(_lockedVenueFloor)
            ? _lockedVenueName
            : $"{_lockedVenueName} · {_lockedVenueFloor}";
    }

    private bool IsAuthorized()
    {
        return _isAuthorized?.Invoke() == true;
    }
}
