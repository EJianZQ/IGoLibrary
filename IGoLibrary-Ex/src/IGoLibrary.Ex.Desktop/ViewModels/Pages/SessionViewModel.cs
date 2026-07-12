using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Helpers;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Desktop.ViewModels;

public sealed partial class SessionViewModel : ViewModelBase
{
    private static readonly CultureInfo DashboardCulture = CultureInfo.GetCultureInfo("zh-CN");

    private readonly IActivityLogService _activityLogService;
    private readonly INotificationService _notificationService;
    private readonly IAppThemeService _appThemeService;
    private readonly TimeProvider _timeProvider;
    private readonly OAuthCodeConsumptionRegistry _oauthCodeConsumptionRegistry;

    private Func<string, bool, Task<SessionWorkflowResult>>? _authenticateFromCodeAsync;
    private Func<string, bool, Task<SessionWorkflowResult>>? _authenticateFromCookieAsync;
    private Func<Task<SessionWorkflowResult>>? _restoreSessionAsync;
    private Func<Task>? _signOutAsync;
    private Func<bool, int?, Task>? _loadLibrariesAsync;
    private Func<bool, Task>? _stopLanCookieRelaySessionAsync;
    private Func<Task>? _clearStoredLibrarySelectionAsync;
    private Func<Task>? _clearSignedOutPagesAsync;
    private Func<bool>? _canShowVenueConfiguration;
    private Action<int>? _selectTabIndex;
    private Action? _resetSessionScopedSelections;
    private Action? _queueAutoReleaseReservationRefresh;
    private Action? _queueAutoReleaseCheck;
    private Action<bool>? _authorizationChanged;
    private Action? _cookieStateChanged;
    private Action? _scheduleSettingsAutoSave;

    private DateTimeOffset? _sidebarSessionExpirationTime;
    private string? _homeCookieIdentity;
    private string _currentCookie = string.Empty;
    private DateTimeOffset? _homeCookieExpirationTime;
    private string? _homeCookieProgressCookieIdentity;
    private DateTimeOffset? _homeCookieProgressExpirationTime;
    private DateTimeOffset? _homeCookieProgressStartedAt;
    private IBrush _stateIdleBrush;
    private IBrush _stateSuccessBrush;
    private IBrush _stateWarningBrush;
    private IBrush _stateFailureBrush;
    private IBrush _neutralSoftBrush;
    private IBrush _successSoftBrush;
    private IBrush _logDefaultBrush;

    public SessionViewModel(
        IActivityLogService activityLogService,
        INotificationService notificationService,
        IAppThemeService appThemeService,
        TimeProvider timeProvider,
        OAuthCodeConsumptionRegistry oauthCodeConsumptionRegistry)
    {
        _activityLogService = activityLogService;
        _notificationService = notificationService;
        _appThemeService = appThemeService;
        _timeProvider = timeProvider;
        _oauthCodeConsumptionRegistry = oauthCodeConsumptionRegistry;

        var palette = _appThemeService.CurrentPalette;
        _stateIdleBrush = palette.IdleBrush;
        _stateSuccessBrush = palette.SuccessBrush;
        _stateWarningBrush = palette.WarningBrush;
        _stateFailureBrush = palette.FailureBrush;
        _neutralSoftBrush = palette.NeutralSoftBrush;
        _successSoftBrush = palette.SuccessSoftBrush;
        _logDefaultBrush = palette.LogDefaultBrush;

        SidebarSessionExpirationBrush = _logDefaultBrush;
        HomeCookieBadgeBrush = _stateIdleBrush;
        HomeCookieBadgeBackgroundBrush = _neutralSoftBrush;
        HomeCookieProgressBrush = _stateIdleBrush;
    }

    public string[] HomeCookieProgressTimingModes { get; } = ["固定 Cookie 有效时长", "软件运行时计算时长"];

    [ObservableProperty]
    private string sessionSummary = "未登录";

    [ObservableProperty]
    private bool isAuthorized;

    [ObservableProperty]
    private bool hasSidebarSessionExpiration;

    [ObservableProperty]
    private string sidebarSessionExpirationText = string.Empty;

    [ObservableProperty]
    private IBrush sidebarSessionExpirationBrush;

    public string AuthorizationStatusText => CanShowVenueConfiguration() ? "已授权" : "未授权";

    public bool IsUnauthorized => !CanShowVenueConfiguration();

    public bool ShouldShowAuthorizationInput => !CanShowVenueConfiguration();

    public bool ShouldShowAuthorizedSummary => CanShowVenueConfiguration();

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
    private IBrush homeCookieBadgeBrush;

    [ObservableProperty]
    private IBrush homeCookieBadgeBackgroundBrush;

    [ObservableProperty]
    private double homeCookieProgressValue;

    [ObservableProperty]
    private IBrush homeCookieProgressBrush;

    [ObservableProperty]
    private string qrLinkText = string.Empty;

    [ObservableProperty]
    private string manualCookieText = string.Empty;

    [ObservableProperty]
    private bool rememberSession = true;

    [ObservableProperty]
    private int selectedHomeCookieProgressTimingModeIndex;

    public bool IsHomeCookieFixedProgressMode =>
        CurrentHomeCookieProgressTimingMode == HomeCookieProgressTimingMode.FixedCookieDuration;

    [ObservableProperty]
    private int homeCookieFixedDurationMinutes =
        HomeCookieProgressSettings.DefaultFixedDurationMinutes;

    public string CurrentCookie => _currentCookie;

    public DateTimeOffset? HomeCookieExpirationTime => _homeCookieExpirationTime;

    public HomeCookieProgressTimingMode CurrentHomeCookieProgressTimingMode =>
        HomeCookieProgressSettings.NormalizeMode(
            (HomeCookieProgressTimingMode)Math.Clamp(
                SelectedHomeCookieProgressTimingModeIndex,
                0,
                HomeCookieProgressTimingModes.Length - 1));

    public void ConfigureOrchestration(
        Func<string, bool, Task<SessionWorkflowResult>> authenticateFromCodeAsync,
        Func<string, bool, Task<SessionWorkflowResult>> authenticateFromCookieAsync,
        Func<Task<SessionWorkflowResult>> restoreSessionAsync,
        Func<Task> signOutAsync,
        Func<bool, int?, Task> loadLibrariesAsync,
        Func<bool, Task> stopLanCookieRelaySessionAsync,
        Func<Task> clearStoredLibrarySelectionAsync,
        Func<Task> clearSignedOutPagesAsync,
        Func<bool> canShowVenueConfiguration,
        Action<int> selectTabIndex,
        Action resetSessionScopedSelections,
        Action queueAutoReleaseReservationRefresh,
        Action queueAutoReleaseCheck,
        Action<bool> authorizationChanged,
        Action cookieStateChanged,
        Action scheduleSettingsAutoSave)
    {
        _authenticateFromCodeAsync = authenticateFromCodeAsync;
        _authenticateFromCookieAsync = authenticateFromCookieAsync;
        _restoreSessionAsync = restoreSessionAsync;
        _signOutAsync = signOutAsync;
        _loadLibrariesAsync = loadLibrariesAsync;
        _stopLanCookieRelaySessionAsync = stopLanCookieRelaySessionAsync;
        _clearStoredLibrarySelectionAsync = clearStoredLibrarySelectionAsync;
        _clearSignedOutPagesAsync = clearSignedOutPagesAsync;
        _canShowVenueConfiguration = canShowVenueConfiguration;
        _selectTabIndex = selectTabIndex;
        _resetSessionScopedSelections = resetSessionScopedSelections;
        _queueAutoReleaseReservationRefresh = queueAutoReleaseReservationRefresh;
        _queueAutoReleaseCheck = queueAutoReleaseCheck;
        _authorizationChanged = authorizationChanged;
        _cookieStateChanged = cookieStateChanged;
        _scheduleSettingsAutoSave = scheduleSettingsAutoSave;
    }

    public void ApplySettings(AppSettings settings)
    {
        var homeCookieProgress = HomeCookieProgressSettings.Normalize(settings.Ui.HomeCookieProgress);
        SelectedHomeCookieProgressTimingModeIndex = (int)homeCookieProgress.Mode;
        HomeCookieFixedDurationMinutes = homeCookieProgress.FixedDurationMinutes;
    }

    public void ApplyThemePalette(AppThemePalette palette)
    {
        _stateIdleBrush = palette.IdleBrush;
        _stateSuccessBrush = palette.SuccessBrush;
        _stateWarningBrush = palette.WarningBrush;
        _stateFailureBrush = palette.FailureBrush;
        _neutralSoftBrush = palette.NeutralSoftBrush;
        _successSoftBrush = palette.SuccessSoftBrush;
        _logDefaultBrush = palette.LogDefaultBrush;
        RefreshSidebarSessionExpirationPresentation(GetCurrentTime());
        UpdateHomeCookieCardPresentation(GetCurrentTime());
    }

    public void RefreshAuthorizationPresentationProperties()
    {
        OnPropertyChanged(nameof(AuthorizationStatusText));
        OnPropertyChanged(nameof(IsUnauthorized));
        OnPropertyChanged(nameof(ShouldShowAuthorizationInput));
        OnPropertyChanged(nameof(ShouldShowAuthorizedSummary));
    }

    public async Task<bool> TryAutoParseClipboardLinkAsync(string clipboardText)
    {
        QrLinkText = clipboardText.Trim();
        var result = await ParseCookieFromLinkAsync(QrLinkText, notifyOnInvalidLink: false);
        return result.Processed;
    }

    public async Task<SessionCookieLinkParseResult> ParseCookieFromLinkAsync(
        string? linkText,
        bool notifyOnInvalidLink)
    {
        return await ParseCookieFromLinkAsync(
            linkText,
            new SessionCookieLinkParseOptions(
                NotifyOnInvalidLink: notifyOnInvalidLink,
                ShowDesktopNotifications: true,
                SelectAccountTab: true,
                LogDuplicateCodeValue: true,
                ApplyFetchedCookieWhenUnauthenticated: true,
                MarkCodeProcessedWhenUnauthenticated: true));
    }

    public async Task<SessionCookieLinkParseResult> ParseCookieFromLinkAsync(
        string? linkText,
        SessionCookieLinkParseOptions options)
    {
        string? reservedCode = null;
        var shouldMarkCodeAsProcessed = false;
        try
        {
            if (!CodeLinkParser.TryExtractCode(linkText, out var code))
            {
                const string message = "未能从链接中提取 32 位 code";
                if (options.NotifyOnInvalidLink && options.ShowDesktopNotifications)
                {
                    await _notificationService.ShowWarningAsync("链接无效", message);
                }

                return SessionCookieLinkParseResult.InvalidLink(message);
            }

            if (!TryReserveAuthCode(code))
            {
                const string message = "该授权链接已处理过一次，如需重试，请重新从微信获取新的授权链接";
                _activityLogService.Write(
                    LogEntryKind.Info,
                    "Auth",
                    "授权 code 已处理，跳过重复解析。");
                if (options.NotifyOnInvalidLink && options.ShowDesktopNotifications)
                {
                    await _notificationService.ShowInfoAsync("链接已处理", message);
                }

                return SessionCookieLinkParseResult.DuplicateCode(message);
            }

            reservedCode = code;
            var result = await AuthenticateFromCodeAsync(code, RememberSession);
            shouldMarkCodeAsProcessed = result.Session is not null || options.MarkCodeProcessedWhenUnauthenticated;
            if (result.Session is not null || options.ApplyFetchedCookieWhenUnauthenticated)
            {
                ManualCookieText = result.Cookie ?? string.Empty;
                SessionSummary = result.StatusMessage;
            }

            if (options.SelectAccountTab)
            {
                _selectTabIndex?.Invoke(1);
            }

            if (options.ShowDesktopNotifications)
            {
                await _notificationService.ShowSuccessAsync(
                    "已成功获取 Cookie",
                    BuildCookieFetchedMessage(result.Cookie ?? string.Empty));
            }

            if (result.Session is not null)
            {
                IsAuthorized = true;
                SessionSummary = result.StatusMessage;
                UpdateSidebarSessionExpiration(result.CookieExpirationTime, result.Session.Cookie);
                if (result.ShouldLoadLibraries)
                {
                    _resetSessionScopedSelections?.Invoke();
                    await LoadLibrariesAsync(restorePreferredSelection: false);
                }

                QueueAutoRelease();
                return SessionCookieLinkParseResult.AuthenticatedSession("授权链接解析成功，Cookie 已验证并同步到电脑");
            }
            else if (!string.IsNullOrWhiteSpace(result.AuthenticationFailureMessage))
            {
                _activityLogService.Write(LogEntryKind.Warning, "Auth", $"Cookie 已获取，但自动验证失败：{result.AuthenticationFailureMessage}");
                if (options.ShowDesktopNotifications)
                {
                    await _notificationService.ShowInfoAsync(
                        "已获取 Cookie",
                        $"Cookie 已填入文本框，但自动验证失败：{result.AuthenticationFailureMessage}");
                }

                return SessionCookieLinkParseResult.AuthenticationFailed(
                    $"Cookie 已获取，但自动验证失败：{result.AuthenticationFailureMessage}");
            }

            return SessionCookieLinkParseResult.CookieFetched("Cookie 已获取，但尚未完成自动验证");
        }
        catch (Exception ex)
        {
            _activityLogService.Write(LogEntryKind.Error, "Auth", $"通过链接获取 Cookie 失败：{ex.Message}");
            if (options.ShowDesktopNotifications)
            {
                await _notificationService.ShowWarningAsync("获取 Cookie 失败", ex.Message);
            }

            return SessionCookieLinkParseResult.FetchFailed(ex.Message);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(reservedCode))
            {
                CompleteAuthCodeReservation(reservedCode, shouldMarkCodeAsProcessed);
            }
        }
    }

    public async Task RestoreSessionForStartupAsync()
    {
        var result = await RestoreSessionCoreAsync();
        if (result.Session is null)
        {
            return;
        }

        IsAuthorized = true;
        SessionSummary = result.StatusMessage;
        ManualCookieText = result.Cookie ?? result.Session.Cookie;
        UpdateSidebarSessionExpiration(result.CookieExpirationTime, result.Cookie ?? result.Session.Cookie);
        await NotifySessionRestoredAsync(result.Cookie ?? result.Session.Cookie);
        if (result.ShouldLoadLibraries)
        {
            await LoadLibrariesAsync(restorePreferredSelection: true);
        }
    }

    public void UpdateSidebarSessionExpiration(string cookie)
    {
        if (!SessionAuthFailureDetector.TryGetCookieExpirationTime(cookie, out var expirationTime))
        {
            ClearSidebarSessionExpiration();
            return;
        }

        _sidebarSessionExpirationTime = expirationTime;
        HasSidebarSessionExpiration = true;
        UpdateHomeCookieState(expirationTime, cookie);
        RefreshSidebarSessionExpirationPresentation(GetCurrentTime());
    }

    public void UpdateSidebarSessionExpiration(DateTimeOffset? expirationTime, string? fallbackCookie)
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
        UpdateHomeCookieState(expirationTime.Value, fallbackCookie);
        RefreshSidebarSessionExpirationPresentation(GetCurrentTime());
    }

    public void RefreshSidebarSessionExpirationPresentation(DateTimeOffset timestamp)
    {
        if (_sidebarSessionExpirationTime is null || !HasSidebarSessionExpiration)
        {
            return;
        }

        var expirationTime = _sidebarSessionExpirationTime.Value;
        SidebarSessionExpirationText = expirationTime.ToString("M月d日 HH:mm", DashboardCulture);

        var remaining = expirationTime - timestamp;
        SidebarSessionExpirationBrush = remaining <= TimeSpan.FromMinutes(10)
            ? _stateFailureBrush
            : remaining <= TimeSpan.FromMinutes(30)
                ? _stateWarningBrush
                : _logDefaultBrush;
    }

    public void UpdateHomeCookieCardPresentation(DateTimeOffset now)
    {
        if (!IsAuthorized ||
            _homeCookieExpirationTime is not { } expirationTime ||
            expirationTime <= now)
        {
            ClearHomeCookieProgressTracking();
            HasCurrentCookie = false;
            HomeCookieExpirationTimeText = "--:--:--";
            HomeCookieRemainingText = "--";
            HomeCookieBadgeText = "未登录";
            HomeCookieBadgeBrush = _stateIdleBrush;
            HomeCookieBadgeBackgroundBrush = _neutralSoftBrush;
            HomeCookieProgressValue = 0;
            HomeCookieProgressBrush = _stateIdleBrush;
            return;
        }

        HasCurrentCookie = true;
        EnsureHomeCookieProgressTracking(expirationTime, _homeCookieIdentity, now);
        var remaining = expirationTime - now;
        HomeCookieExpirationTimeText = expirationTime.ToString("M月d日 HH:mm:ss", DashboardCulture);
        HomeCookieRemainingText = FormatCookieRemaining(remaining);
        HomeCookieBadgeText = "有效中";
        HomeCookieBadgeBrush = _stateSuccessBrush;
        HomeCookieBadgeBackgroundBrush = _successSoftBrush;
        HomeCookieProgressValue = CalculateHomeCookieProgressValue(remaining, now);
        HomeCookieProgressBrush = ResolveHomeProgressBrush(HomeCookieProgressValue);
    }

    partial void OnIsAuthorizedChanged(bool value)
    {
        RefreshAuthorizationPresentationProperties();
        UpdateHomeCookieCardPresentation(GetCurrentTime());
        _authorizationChanged?.Invoke(value);
    }

    partial void OnHasCurrentCookieChanged(bool value)
    {
        OnPropertyChanged(nameof(HasNoCurrentCookie));
        RefreshAuthorizationPresentationProperties();
        _cookieStateChanged?.Invoke();
    }

    partial void OnSelectedHomeCookieProgressTimingModeIndexChanged(int value)
    {
        var normalized = Math.Clamp(value, 0, HomeCookieProgressTimingModes.Length - 1);
        if (normalized != value)
        {
            SelectedHomeCookieProgressTimingModeIndex = normalized;
            return;
        }

        OnPropertyChanged(nameof(IsHomeCookieFixedProgressMode));
        _scheduleSettingsAutoSave?.Invoke();
        UpdateHomeCookieCardPresentation(GetCurrentTime());
    }

    partial void OnHomeCookieFixedDurationMinutesChanged(int value)
    {
        var normalized = HomeCookieProgressSettings.NormalizeFixedDurationMinutes(value);
        if (normalized != value)
        {
            HomeCookieFixedDurationMinutes = normalized;
            return;
        }

        _scheduleSettingsAutoSave?.Invoke();
        UpdateHomeCookieCardPresentation(GetCurrentTime());
    }

    partial void OnSessionSummaryChanged(string value)
    {
        RefreshAuthorizationPresentationProperties();
    }

    [RelayCommand]
    private Task GetCookieFromLink()
    {
        return ParseCookieFromLinkAsync(QrLinkText, notifyOnInvalidLink: true);
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

            var result = await AuthenticateFromCookieAsync(ManualCookieText, RememberSession);
            var session = result.Session ?? throw new InvalidOperationException("Cookie 验证成功但未返回会话");
            IsAuthorized = true;
            SessionSummary = result.StatusMessage;
            UpdateSidebarSessionExpiration(result.CookieExpirationTime, session.Cookie);
            if (result.ShouldLoadLibraries)
            {
                _resetSessionScopedSelections?.Invoke();
                await LoadLibrariesAsync(restorePreferredSelection: false);
            }

            QueueAutoRelease();
            _selectTabIndex?.Invoke(1);
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
            var result = await RestoreSessionCoreAsync();
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
                _resetSessionScopedSelections?.Invoke();
                await LoadLibrariesAsync(restorePreferredSelection: false);
            }

            QueueAutoRelease();
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
        Exception? credentialClearFailure = null;
        if (_stopLanCookieRelaySessionAsync is not null)
        {
            await _stopLanCookieRelaySessionAsync(true);
        }

        if (_signOutAsync is not null)
        {
            try
            {
                await _signOutAsync();
            }
            catch (Exception ex)
            {
                credentialClearFailure = ex;
            }
        }

        if (_clearStoredLibrarySelectionAsync is not null)
        {
            await _clearStoredLibrarySelectionAsync();
        }

        if (_clearSignedOutPagesAsync is not null)
        {
            await _clearSignedOutPagesAsync();
        }

        IsAuthorized = false;
        _resetSessionScopedSelections?.Invoke();
        SessionSummary = "未登录";
        ClearSidebarSessionExpiration();

        if (credentialClearFailure is not null)
        {
            _activityLogService.Write(
                LogEntryKind.Warning,
                "Auth",
                $"已退出当前会话，但清理本地安全凭据失败：{credentialClearFailure.Message}");
            await _notificationService.ShowWarningAsync(
                "已退出，但凭据清理失败",
                credentialClearFailure.Message);
        }
    }

    private Task<SessionWorkflowResult> AuthenticateFromCodeAsync(string code, bool remember)
    {
        return _authenticateFromCodeAsync?.Invoke(code, remember)
            ?? throw new InvalidOperationException("Session auth callback is not configured.");
    }

    private Task<SessionWorkflowResult> AuthenticateFromCookieAsync(string cookie, bool remember)
    {
        return _authenticateFromCookieAsync?.Invoke(cookie, remember)
            ?? throw new InvalidOperationException("Session auth callback is not configured.");
    }

    private Task<SessionWorkflowResult> RestoreSessionCoreAsync()
    {
        return _restoreSessionAsync?.Invoke()
            ?? throw new InvalidOperationException("Session restore callback is not configured.");
    }

    private Task LoadLibrariesAsync(bool restorePreferredSelection)
    {
        return _loadLibrariesAsync?.Invoke(restorePreferredSelection, null)
            ?? Task.CompletedTask;
    }

    private void QueueAutoRelease()
    {
        _queueAutoReleaseReservationRefresh?.Invoke();
        _queueAutoReleaseCheck?.Invoke();
    }

    private bool TryReserveAuthCode(string code)
    {
        return _oauthCodeConsumptionRegistry.TryReserve(code);
    }

    private void CompleteAuthCodeReservation(string code, bool markAsProcessed)
    {
        _oauthCodeConsumptionRegistry.Complete(code, markAsProcessed);
    }

    private void UpdateHomeCookieState(DateTimeOffset expirationTime, string? cookie)
    {
        _currentCookie = cookie ?? string.Empty;
        _homeCookieIdentity = BuildHomeCookieProgressIdentity(expirationTime, cookie);
        _homeCookieExpirationTime = expirationTime;
        OnPropertyChanged(nameof(CurrentCookie));
        OnPropertyChanged(nameof(HomeCookieExpirationTime));
        UpdateHomeCookieCardPresentation(GetCurrentTime());
        RefreshAuthorizationPresentationProperties();
        _cookieStateChanged?.Invoke();
    }

    private void ClearSidebarSessionExpiration()
    {
        _sidebarSessionExpirationTime = null;
        SidebarSessionExpirationText = string.Empty;
        SidebarSessionExpirationBrush = _logDefaultBrush;
        HasSidebarSessionExpiration = false;
        ClearHomeCookieState();
    }

    private void ClearHomeCookieState()
    {
        _currentCookie = string.Empty;
        _homeCookieIdentity = null;
        _homeCookieExpirationTime = null;
        OnPropertyChanged(nameof(CurrentCookie));
        OnPropertyChanged(nameof(HomeCookieExpirationTime));
        ClearHomeCookieProgressTracking();
        UpdateHomeCookieCardPresentation(GetCurrentTime());
        RefreshAuthorizationPresentationProperties();
        _cookieStateChanged?.Invoke();
    }

    private double CalculateHomeCookieProgressValue(TimeSpan remaining, DateTimeOffset now)
    {
        var progressWindow = CurrentHomeCookieProgressTimingMode switch
        {
            HomeCookieProgressTimingMode.SoftwareRuntimeDuration =>
                ResolveHomeCookieSoftwareRuntimeProgressWindow(now),
            _ => TimeSpan.FromMinutes(HomeCookieFixedDurationMinutes)
        };

        if (progressWindow <= TimeSpan.Zero)
        {
            return 0;
        }

        return Math.Clamp(remaining.TotalSeconds / progressWindow.TotalSeconds * 100, 0, 100);
    }

    private TimeSpan ResolveHomeCookieSoftwareRuntimeProgressWindow(DateTimeOffset now)
    {
        if (_homeCookieExpirationTime is not { } expirationTime)
        {
            return TimeSpan.Zero;
        }

        EnsureHomeCookieProgressTracking(expirationTime, _homeCookieIdentity, now);
        return expirationTime - (_homeCookieProgressStartedAt ?? now);
    }

    private void EnsureHomeCookieProgressTracking(
        DateTimeOffset expirationTime,
        string? cookieIdentity,
        DateTimeOffset observedAt)
    {
        cookieIdentity = BuildHomeCookieProgressIdentity(expirationTime, cookieIdentity);
        if (string.Equals(_homeCookieProgressCookieIdentity, cookieIdentity, StringComparison.Ordinal) &&
            _homeCookieProgressExpirationTime == expirationTime &&
            _homeCookieProgressStartedAt is not null)
        {
            return;
        }

        _homeCookieProgressCookieIdentity = cookieIdentity;
        _homeCookieProgressExpirationTime = expirationTime;
        _homeCookieProgressStartedAt = observedAt;
    }

    private void ClearHomeCookieProgressTracking()
    {
        _homeCookieProgressCookieIdentity = null;
        _homeCookieProgressExpirationTime = null;
        _homeCookieProgressStartedAt = null;
    }

    private IBrush ResolveHomeProgressBrush(double progressValue)
    {
        if (progressValue < 10)
        {
            return _stateFailureBrush;
        }

        if (progressValue < 30)
        {
            return _stateWarningBrush;
        }

        return _stateSuccessBrush;
    }

    private bool CanShowVenueConfiguration()
    {
        return _canShowVenueConfiguration?.Invoke() == true;
    }

    private DateTimeOffset GetCurrentTime()
    {
        return _timeProvider.GetUtcNow().ToLocalTime();
    }

    private static string BuildHomeCookieProgressIdentity(DateTimeOffset expirationTime, string? cookieIdentity)
    {
        var normalizedCookieIdentity = cookieIdentity?.Trim();
        return string.IsNullOrWhiteSpace(normalizedCookieIdentity)
            ? expirationTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture)
            : normalizedCookieIdentity;
    }

    private static string FormatCookieRemaining(TimeSpan remaining)
    {
        if (remaining.TotalDays >= 1)
        {
            return $"{Math.Max(1, (int)Math.Floor(remaining.TotalDays))}天 {remaining.Hours:D2}:{remaining.Minutes:D2}:{remaining.Seconds:D2}";
        }

        if (remaining.TotalHours >= 1)
        {
            return remaining.ToString(@"hh\:mm\:ss", DashboardCulture);
        }

        return remaining.ToString(@"mm\:ss", DashboardCulture);
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
        if (expirationTime - GetCurrentTime() < TimeSpan.FromMinutes(30))
        {
            await _notificationService.ShowWarningAsync("已成功恢复上次的 Cookie，注意到期时间", message);
            return;
        }

        await _notificationService.ShowSuccessAsync("已成功恢复上次的 Cookie", message);
    }
}

public sealed record SessionCookieLinkParseResult(
    bool Processed,
    bool Authenticated,
    string Message,
    SessionCookieLinkParseStatus Status)
{
    public static SessionCookieLinkParseResult AuthenticatedSession(string message)
    {
        return new SessionCookieLinkParseResult(
            true,
            true,
            message,
            SessionCookieLinkParseStatus.Authenticated);
    }

    public static SessionCookieLinkParseResult CookieFetched(string message)
    {
        return new SessionCookieLinkParseResult(
            true,
            false,
            message,
            SessionCookieLinkParseStatus.CookieFetched);
    }

    public static SessionCookieLinkParseResult InvalidLink(string message)
    {
        return new SessionCookieLinkParseResult(
            false,
            false,
            message,
            SessionCookieLinkParseStatus.InvalidLink);
    }

    public static SessionCookieLinkParseResult DuplicateCode(string message)
    {
        return new SessionCookieLinkParseResult(
            false,
            false,
            message,
            SessionCookieLinkParseStatus.DuplicateCode);
    }

    public static SessionCookieLinkParseResult AuthenticationFailed(string message)
    {
        return new SessionCookieLinkParseResult(
            true,
            false,
            message,
            SessionCookieLinkParseStatus.AuthenticationFailed);
    }

    public static SessionCookieLinkParseResult FetchFailed(string message)
    {
        return new SessionCookieLinkParseResult(
            false,
            false,
            message,
            SessionCookieLinkParseStatus.FetchFailed);
    }
}

public sealed record SessionCookieLinkParseOptions(
    bool NotifyOnInvalidLink,
    bool ShowDesktopNotifications,
    bool SelectAccountTab,
    bool LogDuplicateCodeValue,
    bool ApplyFetchedCookieWhenUnauthenticated,
    bool MarkCodeProcessedWhenUnauthenticated)
{
    public static SessionCookieLinkParseOptions MobileControlRefresh { get; } = new(
        NotifyOnInvalidLink: false,
        ShowDesktopNotifications: false,
        SelectAccountTab: false,
        LogDuplicateCodeValue: false,
        ApplyFetchedCookieWhenUnauthenticated: false,
        MarkCodeProcessedWhenUnauthenticated: false);
}

public enum SessionCookieLinkParseStatus
{
    InvalidLink,
    DuplicateCode,
    FetchFailed,
    CookieFetched,
    AuthenticationFailed,
    Authenticated
}
