using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Desktop.ViewModels;

public partial class MainWindowWorkflowViewModel
{
    private bool _grabPageConfigured;

    public string[] GrabPollingModes => GrabPage.GrabPollingModes;

    public string[] GrabReservationStrategies => GrabPage.GrabReservationStrategies;

    public bool IsGrabSeatSelectionOverlayOpen
    {
        get => GrabPage.IsGrabSeatSelectionOverlayOpen;
        set => GrabPage.IsGrabSeatSelectionOverlayOpen = value;
    }

    public int SelectedGrabPollingModeIndex
    {
        get => GrabPage.SelectedGrabPollingModeIndex;
        set => GrabPage.SelectedGrabPollingModeIndex = value;
    }

    public int SelectedGrabReservationStrategyIndex
    {
        get => GrabPage.SelectedGrabReservationStrategyIndex;
        set => GrabPage.SelectedGrabReservationStrategyIndex = value;
    }

    public bool IsGrabScheduledStartEnabled
    {
        get => GrabPage.IsGrabScheduledStartEnabled;
        set => GrabPage.IsGrabScheduledStartEnabled = value;
    }

    public TimeSpan? ScheduledStartTime
    {
        get => GrabPage.ScheduledStartTime;
        set => GrabPage.ScheduledStartTime = value;
    }

    public string GrabStatusText
    {
        get => GrabPage.GrabStatusText;
        set => GrabPage.GrabStatusText = value;
    }

    public bool IsGrabTaskActive
    {
        get => GrabPage.IsGrabTaskActive;
        set => GrabPage.IsGrabTaskActive = value;
    }

    public int GrabPollCount
    {
        get => GrabPage.GrabPollCount;
        set => GrabPage.GrabPollCount = value;
    }

    public int GrabRequestCount
    {
        get => GrabPage.GrabRequestCount;
        set => GrabPage.GrabRequestCount = value;
    }

    public string GrabLastRequestText
    {
        get => GrabPage.GrabLastRequestText;
        set => GrabPage.GrabLastRequestText = value;
    }

    public string GrabRuntimeText
    {
        get => GrabPage.GrabRuntimeText;
        set => GrabPage.GrabRuntimeText = value;
    }

    public bool CanEditGrabConfiguration => GrabPage.CanEditGrabConfiguration;

    public bool CanEditGrabScheduledStartTime => GrabPage.CanEditGrabScheduledStartTime;

    public string GrabDashboardStatusText => GrabPage.GrabDashboardStatusText;

    public IBrush GrabDashboardStatusBrush => GrabPage.GrabDashboardStatusBrush;

    public IAsyncRelayCommand OpenGrabSeatSelectionOverlayCommand
    {
        get
        {
            EnsureGrabPageConfigured();
            return GrabPage.OpenGrabSeatSelectionOverlayCommand;
        }
    }

    public IRelayCommand ConfirmGrabSeatSelectionCommand
    {
        get
        {
            EnsureGrabPageConfigured();
            return GrabPage.ConfirmGrabSeatSelectionCommand;
        }
    }

    public IRelayCommand CancelGrabSeatSelectionCommand
    {
        get
        {
            EnsureGrabPageConfigured();
            return GrabPage.CancelGrabSeatSelectionCommand;
        }
    }

    public IAsyncRelayCommand StartGrabCommand
    {
        get
        {
            EnsureGrabPageConfigured();
            return GrabPage.StartGrabCommand;
        }
    }

    public IAsyncRelayCommand StopGrabCommand
    {
        get
        {
            EnsureGrabPageConfigured();
            return GrabPage.StopGrabCommand;
        }
    }

    private void EnsureGrabPageConfigured()
    {
        if (_grabPageConfigured)
        {
            return;
        }

        _grabPageConfigured = true;
        EnsureMultiSeatSelectionConfigured();
        GrabPage.ConfigureOrchestration(
            () => IsInitializationComplete,
            () => IsLoadingSettings,
            () => AccountVenue.LockedLibrary,
            () => MultiSeatSelection.SeatCount,
            MultiSeatSelection.GetSelectedSeatSnapshot,
            RefreshSeatsAsync,
            MultiSeatSelection.BeginDraft,
            MultiSeatSelection.CommitDraft,
            MultiSeatSelection.RestoreCommittedSelection,
            () => RefreshReservationAsync(showNotificationOnError: false),
            RecordSuccessfulReservationAsync,
            status =>
            {
                UpdateGuardTracking(status.LastUpdatedAt ?? GetCurrentTime());
                UpdateHomeHeroPresentation(GetCurrentTime());
                UpdateHomeSystemInfoPresentation();
            });
        GrabPage.InitializeStatus();
    }

    private void ConfigureGrabPropertyBridge(ViewModelPropertyBridge propertyBridge)
    {
        propertyBridge.ForwardSame(
            GrabPage,
            nameof(GrabPage.IsGrabSeatSelectionOverlayOpen),
            nameof(GrabPage.SelectedGrabPollingModeIndex),
            nameof(GrabPage.SelectedGrabReservationStrategyIndex),
            nameof(GrabPage.IsGrabScheduledStartEnabled),
            nameof(GrabPage.ScheduledStartTime),
            nameof(GrabPage.GrabStatusText),
            nameof(GrabPage.IsGrabTaskActive),
            nameof(GrabPage.GrabPollCount),
            nameof(GrabPage.GrabRequestCount),
            nameof(GrabPage.GrabLastRequestText),
            nameof(GrabPage.GrabRuntimeText),
            nameof(GrabPage.CanEditGrabConfiguration),
            nameof(GrabPage.CanEditGrabScheduledStartTime),
            nameof(GrabPage.GrabDashboardStatusText),
            nameof(GrabPage.GrabDashboardStatusBrush));
    }

    private void ApplyGrabStatus(CoordinatorStatus status)
    {
        GrabPage.ApplyStatus(status);
    }
}
