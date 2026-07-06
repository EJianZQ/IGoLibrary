using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Desktop.ViewModels;

public partial class MainWindowWorkflowViewModel
{
    private bool _tomorrowReservationPageConfigured;

    public ObservableCollection<SeatItemViewModel> TomorrowVisibleSeats => TomorrowReservationPage.TomorrowVisibleSeats;

    public bool IsTomorrowSeatSelectionOverlayOpen
    {
        get => TomorrowReservationPage.IsTomorrowSeatSelectionOverlayOpen;
        set => TomorrowReservationPage.IsTomorrowSeatSelectionOverlayOpen = value;
    }

    public TimeSpan? TomorrowScheduledStartTime
    {
        get => TomorrowReservationPage.TomorrowScheduledStartTime;
        set => TomorrowReservationPage.TomorrowScheduledStartTime = value;
    }

    public string TomorrowStatusText
    {
        get => TomorrowReservationPage.TomorrowStatusText;
        set => TomorrowReservationPage.TomorrowStatusText = value;
    }

    public bool IsTomorrowTaskActive
    {
        get => TomorrowReservationPage.IsTomorrowTaskActive;
        set => TomorrowReservationPage.IsTomorrowTaskActive = value;
    }

    public int TomorrowRequestCount
    {
        get => TomorrowReservationPage.TomorrowRequestCount;
        set => TomorrowReservationPage.TomorrowRequestCount = value;
    }

    public string TomorrowLastRequestText
    {
        get => TomorrowReservationPage.TomorrowLastRequestText;
        set => TomorrowReservationPage.TomorrowLastRequestText = value;
    }

    public string TomorrowVerificationText
    {
        get => TomorrowReservationPage.TomorrowVerificationText;
        set => TomorrowReservationPage.TomorrowVerificationText = value;
    }

    public string TomorrowSeatFilterText
    {
        get => TomorrowReservationPage.TomorrowSeatFilterText;
        set => TomorrowReservationPage.TomorrowSeatFilterText = value;
    }

    public SeatReference? SelectedTomorrowSeat
    {
        get => TomorrowReservationPage.SelectedTomorrowSeat;
        set => TomorrowReservationPage.SelectedTomorrowSeat = value;
    }

    public bool CanEditTomorrowConfiguration => TomorrowReservationPage.CanEditTomorrowConfiguration;

    public bool HasSelectedTomorrowSeat => TomorrowReservationPage.HasSelectedTomorrowSeat;

    public bool HasNoSelectedTomorrowSeat => TomorrowReservationPage.HasNoSelectedTomorrowSeat;

    public bool HasTomorrowSeatLayout => TomorrowReservationPage.HasTomorrowSeatLayout;

    public bool HasNoTomorrowSeatLayout => TomorrowReservationPage.HasNoTomorrowSeatLayout;

    public bool HasVisibleTomorrowSeatResults => TomorrowReservationPage.HasVisibleTomorrowSeatResults;

    public bool ShowTomorrowSeatFilterEmptyState => TomorrowReservationPage.ShowTomorrowSeatFilterEmptyState;

    public string SelectedTomorrowSeatText => TomorrowReservationPage.SelectedTomorrowSeatText;

    public string DraftSelectedTomorrowSeatSummaryText => TomorrowReservationPage.DraftSelectedTomorrowSeatSummaryText;

    public string TomorrowDashboardStatusText => TomorrowReservationPage.TomorrowDashboardStatusText;

    public IBrush TomorrowDashboardStatusBrush => TomorrowReservationPage.TomorrowDashboardStatusBrush;

    public IAsyncRelayCommand RefreshTomorrowSeatsCommand
    {
        get
        {
            EnsureTomorrowReservationPageConfigured();
            return TomorrowReservationPage.RefreshTomorrowSeatsCommand;
        }
    }

    public IAsyncRelayCommand OpenTomorrowSeatSelectionOverlayCommand
    {
        get
        {
            EnsureTomorrowReservationPageConfigured();
            return TomorrowReservationPage.OpenTomorrowSeatSelectionOverlayCommand;
        }
    }

    public IRelayCommand ConfirmTomorrowSeatSelectionCommand
    {
        get
        {
            EnsureTomorrowReservationPageConfigured();
            return TomorrowReservationPage.ConfirmTomorrowSeatSelectionCommand;
        }
    }

    public IRelayCommand CancelTomorrowSeatSelectionCommand
    {
        get
        {
            EnsureTomorrowReservationPageConfigured();
            return TomorrowReservationPage.CancelTomorrowSeatSelectionCommand;
        }
    }

    public IRelayCommand ClearTomorrowSeatCommand
    {
        get
        {
            EnsureTomorrowReservationPageConfigured();
            return TomorrowReservationPage.ClearTomorrowSeatCommand;
        }
    }

    public IAsyncRelayCommand StartTomorrowReservationCommand
    {
        get
        {
            EnsureTomorrowReservationPageConfigured();
            return TomorrowReservationPage.StartTomorrowReservationCommand;
        }
    }

    public IAsyncRelayCommand RunTomorrowReservationNowCommand
    {
        get
        {
            EnsureTomorrowReservationPageConfigured();
            return TomorrowReservationPage.RunTomorrowReservationNowCommand;
        }
    }

    public IAsyncRelayCommand StopTomorrowReservationCommand
    {
        get
        {
            EnsureTomorrowReservationPageConfigured();
            return TomorrowReservationPage.StopTomorrowReservationCommand;
        }
    }

    private void EnsureTomorrowReservationPageConfigured()
    {
        if (_tomorrowReservationPageConfigured)
        {
            return;
        }

        _tomorrowReservationPageConfigured = true;
        TomorrowReservationPage.ConfigureOrchestration(
            () => IsInitializationComplete,
            () => IsLoadingSettings,
            () => HasActiveVenuePreview,
            () => AccountVenue.LockedLibrary,
            RefreshSeatsAsync,
            RecordSuccessfulReservationAsync,
            status =>
            {
                UpdateGuardTracking(status.LastUpdatedAt ?? GetCurrentTime());
                UpdateHomeHeroPresentation(GetCurrentTime());
                UpdateHomeSystemInfoPresentation();
            });
        TomorrowReservationPage.InitializeStatus();
    }

    private void ConfigureTomorrowReservationPropertyBridge(ViewModelPropertyBridge propertyBridge)
    {
        propertyBridge.ForwardSame(
            TomorrowReservationPage,
            nameof(TomorrowReservationPage.IsTomorrowSeatSelectionOverlayOpen),
            nameof(TomorrowReservationPage.TomorrowScheduledStartTime),
            nameof(TomorrowReservationPage.TomorrowStatusText),
            nameof(TomorrowReservationPage.IsTomorrowTaskActive),
            nameof(TomorrowReservationPage.TomorrowRequestCount),
            nameof(TomorrowReservationPage.TomorrowLastRequestText),
            nameof(TomorrowReservationPage.TomorrowVerificationText),
            nameof(TomorrowReservationPage.TomorrowSeatFilterText),
            nameof(TomorrowReservationPage.SelectedTomorrowSeat),
            nameof(TomorrowReservationPage.CanEditTomorrowConfiguration),
            nameof(TomorrowReservationPage.HasSelectedTomorrowSeat),
            nameof(TomorrowReservationPage.HasNoSelectedTomorrowSeat),
            nameof(TomorrowReservationPage.HasTomorrowSeatLayout),
            nameof(TomorrowReservationPage.HasNoTomorrowSeatLayout),
            nameof(TomorrowReservationPage.HasVisibleTomorrowSeatResults),
            nameof(TomorrowReservationPage.ShowTomorrowSeatFilterEmptyState),
            nameof(TomorrowReservationPage.SelectedTomorrowSeatText),
            nameof(TomorrowReservationPage.DraftSelectedTomorrowSeatSummaryText),
            nameof(TomorrowReservationPage.TomorrowDashboardStatusText),
            nameof(TomorrowReservationPage.TomorrowDashboardStatusBrush));
    }

    private void ApplyTomorrowStatus(CoordinatorStatus status)
    {
        TomorrowReservationPage.ApplyStatus(status);
    }
}
