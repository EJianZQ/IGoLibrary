using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Desktop.ViewModels;

public partial class MainWindowWorkflowViewModel
{
    private bool _sessionConfigured;

    public string[] HomeCookieProgressTimingModes => Session.HomeCookieProgressTimingModes;

    public string SessionSummary
    {
        get => Session.SessionSummary;
        set => Session.SessionSummary = value;
    }

    public bool IsAuthorized
    {
        get => Session.IsAuthorized;
        set
        {
            EnsureSessionConfigured();
            WorkflowState.IsAuthorized = value;
            Session.IsAuthorized = value;
        }
    }

    public bool HasSidebarSessionExpiration
    {
        get => Session.HasSidebarSessionExpiration;
        set => Session.HasSidebarSessionExpiration = value;
    }

    public string SidebarSessionExpirationText
    {
        get => Session.SidebarSessionExpirationText;
        set => Session.SidebarSessionExpirationText = value;
    }

    public IBrush SidebarSessionExpirationBrush
    {
        get => Session.SidebarSessionExpirationBrush;
        set => Session.SidebarSessionExpirationBrush = value;
    }

    public string AuthorizationStatusText => Session.AuthorizationStatusText;

    public bool IsUnauthorized => Session.IsUnauthorized;

    public bool ShouldShowAuthorizationInput => Session.ShouldShowAuthorizationInput;

    public bool ShouldShowAuthorizedSummary => Session.ShouldShowAuthorizedSummary;

    public bool HasCurrentCookie
    {
        get => Session.HasCurrentCookie;
        set => Session.HasCurrentCookie = value;
    }

    public bool HasNoCurrentCookie => Session.HasNoCurrentCookie;

    public string HomeCookieExpirationTimeText
    {
        get => Session.HomeCookieExpirationTimeText;
        set => Session.HomeCookieExpirationTimeText = value;
    }

    public string HomeCookieRemainingText
    {
        get => Session.HomeCookieRemainingText;
        set => Session.HomeCookieRemainingText = value;
    }

    public string HomeCookieBadgeText
    {
        get => Session.HomeCookieBadgeText;
        set => Session.HomeCookieBadgeText = value;
    }

    public IBrush HomeCookieBadgeBrush
    {
        get => Session.HomeCookieBadgeBrush;
        set => Session.HomeCookieBadgeBrush = value;
    }

    public IBrush HomeCookieBadgeBackgroundBrush
    {
        get => Session.HomeCookieBadgeBackgroundBrush;
        set => Session.HomeCookieBadgeBackgroundBrush = value;
    }

    public double HomeCookieProgressValue
    {
        get => Session.HomeCookieProgressValue;
        set => Session.HomeCookieProgressValue = value;
    }

    public IBrush HomeCookieProgressBrush
    {
        get => Session.HomeCookieProgressBrush;
        set => Session.HomeCookieProgressBrush = value;
    }

    public string QrLinkText
    {
        get => Session.QrLinkText;
        set => Session.QrLinkText = value;
    }

    public string ManualCookieText
    {
        get => Session.ManualCookieText;
        set => Session.ManualCookieText = value;
    }

    public bool RememberSession
    {
        get => Session.RememberSession;
        set => Session.RememberSession = value;
    }

    public int SelectedHomeCookieProgressTimingModeIndex
    {
        get => Session.SelectedHomeCookieProgressTimingModeIndex;
        set => Session.SelectedHomeCookieProgressTimingModeIndex = value;
    }

    public bool IsHomeCookieFixedProgressMode => Session.IsHomeCookieFixedProgressMode;

    public int HomeCookieFixedDurationMinutes
    {
        get => Session.HomeCookieFixedDurationMinutes;
        set => Session.HomeCookieFixedDurationMinutes = value;
    }

    private HomeCookieProgressTimingMode CurrentHomeCookieProgressTimingMode =>
        Session.CurrentHomeCookieProgressTimingMode;

    public IAsyncRelayCommand GetCookieFromLinkCommand
    {
        get
        {
            EnsureSessionConfigured();
            return Session.GetCookieFromLinkCommand;
        }
    }

    public IAsyncRelayCommand ValidateManualCookieCommand
    {
        get
        {
            EnsureSessionConfigured();
            return Session.ValidateManualCookieCommand;
        }
    }

    public IAsyncRelayCommand RestoreSessionCommand
    {
        get
        {
            EnsureSessionConfigured();
            return Session.RestoreSessionCommand;
        }
    }

    public IAsyncRelayCommand SignOutCommand
    {
        get
        {
            EnsureSessionConfigured();
            return Session.SignOutCommand;
        }
    }

    private void EnsureSessionConfigured()
    {
        if (_sessionConfigured)
        {
            return;
        }

        _sessionConfigured = true;
        EnsureAccountVenueConfigured();
        Session.ConfigureOrchestration(
            (code, remember) => AccountVenue.AuthenticateFromCodeAsync(code, remember),
            (cookie, remember) => AccountVenue.AuthenticateFromCookieAsync(cookie, remember),
            () => AccountVenue.RestoreSessionAsync(),
            () => AccountVenue.SignOutAsync(),
            (restorePreferredSelection, preferredLibraryId) => LoadLibrariesAsync(restorePreferredSelection, preferredLibraryId),
            StopLanCookieRelaySessionAsync,
            () => AccountVenue.ClearStoredLibrarySelectionAsync(),
            ClearSignedOutPageStateAsync,
            () => CanShowVenueConfiguration,
            tabIndex => SelectedTabIndex = tabIndex,
            () => GlobalLeakPage.ResetRestoredSelectionForCurrentSession(),
            QueueAutoReleaseReservationRefresh,
            QueueAutoReleaseCheck,
            OnSessionAuthorizationChanged,
            OnSessionCookieStateChanged,
            ScheduleSystemSettingsAutoSave);
    }

    private void ConfigureSessionPropertyBridge(ViewModelPropertyBridge propertyBridge)
    {
        propertyBridge.ForwardSame(
            Session,
            nameof(Session.SessionSummary),
            nameof(Session.IsAuthorized),
            nameof(Session.HasSidebarSessionExpiration),
            nameof(Session.SidebarSessionExpirationText),
            nameof(Session.SidebarSessionExpirationBrush),
            nameof(Session.AuthorizationStatusText),
            nameof(Session.IsUnauthorized),
            nameof(Session.ShouldShowAuthorizationInput),
            nameof(Session.ShouldShowAuthorizedSummary),
            nameof(Session.HasCurrentCookie),
            nameof(Session.HasNoCurrentCookie),
            nameof(Session.HomeCookieExpirationTimeText),
            nameof(Session.HomeCookieRemainingText),
            nameof(Session.HomeCookieBadgeText),
            nameof(Session.HomeCookieBadgeBrush),
            nameof(Session.HomeCookieBadgeBackgroundBrush),
            nameof(Session.HomeCookieProgressValue),
            nameof(Session.HomeCookieProgressBrush),
            nameof(Session.QrLinkText),
            nameof(Session.ManualCookieText),
            nameof(Session.RememberSession),
            nameof(Session.SelectedHomeCookieProgressTimingModeIndex),
            nameof(Session.IsHomeCookieFixedProgressMode),
            nameof(Session.HomeCookieFixedDurationMinutes));
    }

    public Task<bool> TryAutoParseClipboardLinkAsync(string clipboardText)
    {
        EnsureSessionConfigured();
        return Session.TryAutoParseClipboardLinkAsync(clipboardText);
    }

    private Task<SessionCookieLinkParseResult> ParseCookieFromLinkAsync(
        string? linkText,
        bool notifyOnInvalidLink)
    {
        EnsureSessionConfigured();
        return Session.ParseCookieFromLinkAsync(linkText, notifyOnInvalidLink);
    }

    private Task RestoreSessionForStartupAsync()
    {
        EnsureSessionConfigured();
        return Session.RestoreSessionForStartupAsync();
    }

    private void UpdateSidebarSessionExpiration(string cookie)
    {
        EnsureSessionConfigured();
        Session.UpdateSidebarSessionExpiration(cookie);
    }

    private void UpdateSidebarSessionExpiration(DateTimeOffset? expirationTime, string? fallbackCookie)
    {
        EnsureSessionConfigured();
        Session.UpdateSidebarSessionExpiration(expirationTime, fallbackCookie);
    }

    private void RefreshSidebarSessionExpirationPresentation(DateTimeOffset timestamp)
    {
        EnsureSessionConfigured();
        Session.RefreshSidebarSessionExpirationPresentation(timestamp);
    }

    private void UpdateHomeCookieCardPresentation(DateTimeOffset now)
    {
        EnsureSessionConfigured();
        Session.UpdateHomeCookieCardPresentation(now);
    }

    private void OnSessionAuthorizationChanged(bool value)
    {
        WorkflowState.IsAuthorized = value;

        if (!value && !IsTabAvailableWithoutAuthorization(SelectedTabIndex))
        {
            SelectedTabIndex = AccountAndVenueTabIndex;
        }

        if (!value)
        {
            GlobalLeakPage.ResetRestoredSelectionForCurrentSession();
        }

        UpdateSidebarItems();
        AccountVenue.NotifyAuthorizationStateChanged();

        OnPropertyChanged(nameof(IsAuthorized));
        OnPropertyChanged(nameof(AuthorizationStatusText));
        OnPropertyChanged(nameof(IsUnauthorized));
        OnPropertyChanged(nameof(ShouldShowAuthorizationInput));
        OnPropertyChanged(nameof(ShouldShowAuthorizedSummary));
        OnPropertyChanged(nameof(CanShowVenueConfiguration));
        OnPropertyChanged(nameof(ShowVenuePreviewStateTag));
        OnPropertyChanged(nameof(ShowVenueOpenStatusTag));
        OnPropertyChanged(nameof(ShowVenueClosedStatusTag));

        UpdateHomeHeroPresentation(GetCurrentTime());
        UpdateHomeSystemInfoPresentation();
        UpdateHomeReservationCardPresentation(GetCurrentTime());
        UpdateHomeCookieCardPresentation(GetCurrentTime());
    }

    private void OnSessionCookieStateChanged()
    {
        WorkflowState.CurrentCookie = Session.CurrentCookie;
        WorkflowState.CurrentCookieExpirationTime = Session.HomeCookieExpirationTime;
        AccountVenue.NotifyAuthorizationStateChanged();
        OnPropertyChanged(nameof(HasCurrentCookie));
        OnPropertyChanged(nameof(HasNoCurrentCookie));
        OnPropertyChanged(nameof(AuthorizationStatusText));
        OnPropertyChanged(nameof(IsUnauthorized));
        OnPropertyChanged(nameof(ShouldShowAuthorizationInput));
        OnPropertyChanged(nameof(ShouldShowAuthorizedSummary));
        OnPropertyChanged(nameof(CanShowVenueConfiguration));
    }

    private Task ClearSignedOutPageStateAsync()
    {
        IsGrabSeatSelectionOverlayOpen = false;
        IsTomorrowSeatSelectionOverlayOpen = false;
        IsGlobalLeakLibraryPickerOpen = false;
        GlobalLeakPage.ClearLibraries();
        MultiSeatSelection.ClearSeats();
        TomorrowReservationPage.ClearSeats();
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
        VisibleSeatResultCount = 0;
        AccountVenue.ClearVenueState();
        UpdateHomeHeroPresentation(GetCurrentTime());
        UpdateHomeSystemInfoPresentation();
        UpdateReservationPresentation(null);
        ApplyGrabStatus(CoordinatorStatus.Idle("抢座"));
        ApplyGlobalLeakStatus(CoordinatorStatus.Idle("全域捡漏"));
        return Task.CompletedTask;
    }
}
