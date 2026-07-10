using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Desktop.ViewModels;

public partial class MainWindowWorkflowViewModel
{
    private bool _accountVenueConfigured;

    public ObservableCollection<LibrarySummary> AvailableLibraries => AccountVenue.AvailableLibraries;

    public bool CanShowVenueConfiguration => AccountVenue.CanShowVenueConfiguration;

    public string HomeLockedVenueTitle
    {
        get => AccountVenue.HomeLockedVenueTitle;
        set => AccountVenue.HomeLockedVenueTitle = value;
    }

    public string HomeLockedVenueStateText
    {
        get => AccountVenue.HomeLockedVenueStateText;
        set => AccountVenue.HomeLockedVenueStateText = value;
    }

    public IBrush HomeLockedVenueStateBrush
    {
        get => AccountVenue.HomeLockedVenueStateBrush;
        set => AccountVenue.HomeLockedVenueStateBrush = value;
    }

    public IBrush HomeLockedVenueStateBackgroundBrush
    {
        get => AccountVenue.HomeLockedVenueStateBackgroundBrush;
        set => AccountVenue.HomeLockedVenueStateBackgroundBrush = value;
    }

    public string HomeReservationVenueText
    {
        get => AccountVenue.HomeReservationVenueText;
        set => AccountVenue.HomeReservationVenueText = value;
    }

    public string LibrarySummary
    {
        get => AccountVenue.LibrarySummary;
        set => AccountVenue.LibrarySummary = value;
    }

    public string BoundLibraryTitle
    {
        get => AccountVenue.BoundLibraryTitle;
        set => AccountVenue.BoundLibraryTitle = value;
    }

    public string BoundAvailableSeatsText
    {
        get => AccountVenue.BoundAvailableSeatsText;
        set => AccountVenue.BoundAvailableSeatsText = value;
    }

    public string VenueStatusText
    {
        get => AccountVenue.VenueStatusText;
        set => AccountVenue.VenueStatusText = value;
    }

    public bool IsVenueOpen
    {
        get => AccountVenue.IsVenueOpen;
        set => AccountVenue.IsVenueOpen = value;
    }

    public bool IsVenueClosed => AccountVenue.IsVenueClosed;

    public string VenueName
    {
        get => AccountVenue.VenueName;
        set => AccountVenue.VenueName = value;
    }

    public string VenueFloor
    {
        get => AccountVenue.VenueFloor;
        set => AccountVenue.VenueFloor = value;
    }

    public string VenueAvailableSeatsText
    {
        get => AccountVenue.VenueAvailableSeatsText;
        set => AccountVenue.VenueAvailableSeatsText = value;
    }

    public string VenueOpenTimeText
    {
        get => AccountVenue.VenueOpenTimeText;
        set => AccountVenue.VenueOpenTimeText = value;
    }

    public string VenueCloseTimeText
    {
        get => AccountVenue.VenueCloseTimeText;
        set => AccountVenue.VenueCloseTimeText = value;
    }

    public bool IsVenuePickerOpen
    {
        get => AccountVenue.IsVenuePickerOpen;
        set => AccountVenue.IsVenuePickerOpen = value;
    }

    public bool IsCurrentLocked
    {
        get => AccountVenue.IsCurrentLocked;
        set => AccountVenue.IsCurrentLocked = value;
    }

    public bool HasActiveVenuePreview
    {
        get => AccountVenue.HasActiveVenuePreview;
        set => AccountVenue.HasActiveVenuePreview = value;
    }

    public bool IsCurrentPreview => AccountVenue.IsCurrentPreview;

    public bool HasLockedVenue => AccountVenue.HasLockedVenue;

    public bool CanCancelVenuePreview => AccountVenue.CanCancelVenuePreview;

    public bool ShowVenueChangeButton => AccountVenue.ShowVenueChangeButton;

    public bool ShowVenueCancelPreviewButton => AccountVenue.ShowVenueCancelPreviewButton;

    public bool ShowVenuePreviewStateTag => AccountVenue.ShowVenuePreviewStateTag;

    public bool ShowVenueOpenStatusTag => AccountVenue.ShowVenueOpenStatusTag;

    public bool ShowVenueClosedStatusTag => AccountVenue.ShowVenueClosedStatusTag;

    public string CurrentVenueLockStateText => AccountVenue.CurrentVenueLockStateText;

    public string LockVenueButtonText => AccountVenue.LockVenueButtonText;

    public LibrarySummary? SelectedLibrary
    {
        get => AccountVenue.SelectedLibrary;
        set => AccountVenue.SelectedLibrary = value;
    }

    public IAsyncRelayCommand LoadLibrariesCommand
    {
        get
        {
            EnsureAccountVenueConfigured();
            return AccountVenue.LoadLibrariesCommand;
        }
    }

    public IAsyncRelayCommand BindSelectedLibraryCommand
    {
        get
        {
            EnsureAccountVenueConfigured();
            return AccountVenue.BindSelectedLibraryCommand;
        }
    }

    public IAsyncRelayCommand RefreshSeatsCommand
    {
        get
        {
            EnsureAccountVenueConfigured();
            return AccountVenue.RefreshSeatsCommand;
        }
    }

    public IAsyncRelayCommand OpenVenuePickerCommand
    {
        get
        {
            EnsureAccountVenueConfigured();
            return AccountVenue.OpenVenuePickerCommand;
        }
    }

    public IRelayCommand CloseVenuePickerCommand
    {
        get
        {
            EnsureAccountVenueConfigured();
            return AccountVenue.CloseVenuePickerCommand;
        }
    }

    public IRelayCommand CancelVenuePreviewCommand
    {
        get
        {
            EnsureAccountVenueConfigured();
            return AccountVenue.CancelVenuePreviewCommand;
        }
    }

    public Task LoadLibrariesAsync(bool restorePreferredSelection, int? preferredLibraryId = null)
    {
        EnsureAccountVenueConfigured();
        return AccountVenue.LoadLibrariesAsync(restorePreferredSelection, preferredLibraryId);
    }

    public Task BindSelectedLibraryAsync()
    {
        EnsureAccountVenueConfigured();
        return AccountVenue.BindSelectedLibraryCommand.ExecuteAsync(null);
    }

    public Task RefreshSeatsAsync()
    {
        EnsureAccountVenueConfigured();
        return AccountVenue.RefreshSeatsCommand.ExecuteAsync(null);
    }

    public Task HandleVenuePickerLibraryClickAsync(LibrarySummary library)
    {
        EnsureAccountVenueConfigured();
        return AccountVenue.HandleVenuePickerLibraryClickAsync(library);
    }

    private void EnsureAccountVenueConfigured()
    {
        if (_accountVenueConfigured)
        {
            return;
        }

        _accountVenueConfigured = true;
        AccountVenue.ConfigureOrchestration(
            () => IsAuthorized,
            () => HasCurrentCookie,
            () => Session.HomeCookieExpirationTime,
            async libraries =>
            {
                GlobalLeakPage.PopulateLibraries(libraries);
                if (!IsGlobalLeakLibraryPickerOpen)
                {
                    await GlobalLeakPage.RestoreLibrarySelectionAsync();
                }
            },
            async (result, preserveSelection) =>
            {
                EnsureMultiSeatSelectionConfigured();
                await MultiSeatSelection.PopulateSeatsAsync(result.Layout, preserveSelection);
                TomorrowReservationPage.PopulateSeats(result.Layout);
                MultiSeatSelection.ApplyFavoriteStates(result.Favorites.Select(x => x.SeatKey), syncSelection: false);
            },
            () => RefreshReservationAsync(showNotificationOnError: false),
            () =>
            {
                TomorrowReservationPage.NotifyVenuePreviewChanged();
                OnPropertyChanged(nameof(CanEditTomorrowConfiguration));
            },
            () =>
            {
                WorkflowState.LockedLibrary = AccountVenue.LockedLibrary;
                UpdateHomeHeroPresentation(GetCurrentTime());
                UpdateHomeSystemInfoPresentation();
            });
    }

    private void ConfigureAccountVenuePropertyBridge(ViewModelPropertyBridge propertyBridge)
    {
        propertyBridge.ForwardSame(
            AccountVenue,
            nameof(AccountVenue.HomeLockedVenueTitle),
            nameof(AccountVenue.HomeLockedVenueStateText),
            nameof(AccountVenue.HomeLockedVenueStateBrush),
            nameof(AccountVenue.HomeLockedVenueStateBackgroundBrush),
            nameof(AccountVenue.HomeReservationVenueText),
            nameof(AccountVenue.LibrarySummary),
            nameof(AccountVenue.BoundLibraryTitle),
            nameof(AccountVenue.BoundAvailableSeatsText),
            nameof(AccountVenue.VenueStatusText),
            nameof(AccountVenue.IsVenueOpen),
            nameof(AccountVenue.IsVenueClosed),
            nameof(AccountVenue.VenueName),
            nameof(AccountVenue.VenueFloor),
            nameof(AccountVenue.VenueAvailableSeatsText),
            nameof(AccountVenue.VenueOpenTimeText),
            nameof(AccountVenue.VenueCloseTimeText),
            nameof(AccountVenue.IsVenuePickerOpen),
            nameof(AccountVenue.IsCurrentLocked),
            nameof(AccountVenue.HasActiveVenuePreview),
            nameof(AccountVenue.IsCurrentPreview),
            nameof(AccountVenue.HasLockedVenue),
            nameof(AccountVenue.CanCancelVenuePreview),
            nameof(AccountVenue.ShowVenueChangeButton),
            nameof(AccountVenue.ShowVenueCancelPreviewButton),
            nameof(AccountVenue.ShowVenuePreviewStateTag),
            nameof(AccountVenue.ShowVenueOpenStatusTag),
            nameof(AccountVenue.ShowVenueClosedStatusTag),
            nameof(AccountVenue.CurrentVenueLockStateText),
            nameof(AccountVenue.LockVenueButtonText),
            nameof(AccountVenue.SelectedLibrary),
            nameof(AccountVenue.CanShowVenueConfiguration));
        propertyBridge.Forward(
            AccountVenue,
            nameof(AccountVenue.HasActiveVenuePreview),
            nameof(CanEditTomorrowConfiguration));
    }
}
