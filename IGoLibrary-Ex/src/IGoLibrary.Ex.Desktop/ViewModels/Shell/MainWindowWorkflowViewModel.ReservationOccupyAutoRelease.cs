using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using System.ComponentModel;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Desktop.ViewModels;

public partial class MainWindowWorkflowViewModel
{
    private readonly DispatcherTimer _reservationCountdownTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    private bool _reservationCountdownTimerInitialized;

    private bool _occupyPageConfigured;

    private void OnWorkflowStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ShellWorkflowState.CurrentReservation))
        {
            return;
        }

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(SynchronizeReservationPresentationFromWorkflowState);
            return;
        }

        SynchronizeReservationPresentationFromWorkflowState();
    }

    private void SynchronizeReservationPresentationFromWorkflowState()
    {
        EnsureOccupyPageConfigured();
        var reservation = WorkflowState.CurrentReservation;
        if (EqualityComparer<ReservationInfo?>.Default.Equals(OccupyPage.CurrentReservation, reservation))
        {
            return;
        }

        OccupyPage.UpdateReservationPresentation(reservation);
    }

    public string[] OccupyCheckIntervalModes => OccupyPage.OccupyCheckIntervalModes;

    public bool HasCurrentReservation => OccupyPage.HasCurrentReservation;

    public bool HasNoCurrentReservation => OccupyPage.HasNoCurrentReservation;

    public bool CanCancelCurrentReservation => OccupyPage.CanCancelCurrentReservation;

    public bool IsCancellingCurrentReservation
    {
        get => OccupyPage.IsCancellingCurrentReservation;
        set => OccupyPage.IsCancellingCurrentReservation = value;
    }

    public string ReservationSummary
    {
        get => OccupyPage.ReservationSummary;
        set => OccupyPage.ReservationSummary = value;
    }

    public string ReservationHeroTitle
    {
        get => OccupyPage.ReservationHeroTitle;
        set => OccupyPage.ReservationHeroTitle = value;
    }

    public string ReservationExpiryText
    {
        get => OccupyPage.ReservationExpiryText;
        set => OccupyPage.ReservationExpiryText = value;
    }

    public string ReservationCountdownText
    {
        get => OccupyPage.ReservationCountdownText;
        set => OccupyPage.ReservationCountdownText = value;
    }

    public string OccupyStatusText
    {
        get => OccupyPage.OccupyStatusText;
        set => OccupyPage.OccupyStatusText = value;
    }

    public bool IsOccupyRunning
    {
        get => OccupyPage.IsOccupyRunning;
        set => OccupyPage.IsOccupyRunning = value;
    }

    public bool IsOccupyStopped => OccupyPage.IsOccupyStopped;

    public int ReReserveDelaySeconds
    {
        get => OccupyPage.ReReserveDelaySeconds;
        set => OccupyPage.ReReserveDelaySeconds = value;
    }

    public int SelectedOccupyCheckIntervalModeIndex
    {
        get => OccupyPage.SelectedOccupyCheckIntervalModeIndex;
        set => OccupyPage.SelectedOccupyCheckIntervalModeIndex = value;
    }

    public bool AutoReleaseReservationEnabled
    {
        get => OccupyPage.AutoReleaseReservationEnabled;
        set => OccupyPage.AutoReleaseReservationEnabled = value;
    }

    public int AutoReleaseLeadSeconds
    {
        get => OccupyPage.AutoReleaseLeadSeconds;
        set => OccupyPage.AutoReleaseLeadSeconds = value;
    }

    public bool IsAutoReleaseSuppressedByOccupy => OccupyPage.IsAutoReleaseSuppressedByOccupy;

    public string AutoReleaseStatusText => OccupyPage.AutoReleaseStatusText;

    public IAsyncRelayCommand RefreshReservationCommand
    {
        get
        {
            EnsureOccupyPageConfigured();
            return OccupyPage.RefreshReservationCommand;
        }
    }

    public IAsyncRelayCommand CancelCurrentReservationCommand
    {
        get
        {
            EnsureOccupyPageConfigured();
            return OccupyPage.CancelCurrentReservationCommand;
        }
    }

    public IAsyncRelayCommand StartOccupyCommand
    {
        get
        {
            EnsureOccupyPageConfigured();
            return OccupyPage.StartOccupyCommand;
        }
    }

    public IAsyncRelayCommand StopOccupyCommand
    {
        get
        {
            EnsureOccupyPageConfigured();
            return OccupyPage.StopOccupyCommand;
        }
    }

    private void EnsureOccupyPageConfigured()
    {
        if (_occupyPageConfigured)
        {
            return;
        }

        _occupyPageConfigured = true;
        OccupyPage.ConfigureOrchestration(
            () => IsInitializationComplete,
            () => IsLoadingSettings,
            ScheduleSystemSettingsAutoSave,
            info =>
            {
                WorkflowState.CurrentReservation = info;
                if (info is null)
                {
                    ClearHomeReservationProgressTracking();
                }
                else
                {
                    EnsureHomeReservationProgressTracking(info, GetCurrentTime());
                }

                UpdateHomeReservationCardPresentation(GetCurrentTime());
                UpdateHomeSystemInfoPresentation();
            },
            RecordSuccessfulReservationAsync,
            status =>
            {
                UpdateGuardTracking(status.LastUpdatedAt ?? GetCurrentTime());
                UpdateHomeHeroPresentation(GetCurrentTime());
                UpdateHomeSystemInfoPresentation();
            });
        OccupyPage.InitializeStatus();
    }

    private void ConfigureOccupyPropertyBridge(ViewModelPropertyBridge propertyBridge)
    {
        propertyBridge.ForwardSame(
            OccupyPage,
            nameof(OccupyPage.HasCurrentReservation),
            nameof(OccupyPage.HasNoCurrentReservation),
            nameof(OccupyPage.CanCancelCurrentReservation),
            nameof(OccupyPage.IsCancellingCurrentReservation),
            nameof(OccupyPage.ReservationSummary),
            nameof(OccupyPage.ReservationHeroTitle),
            nameof(OccupyPage.ReservationExpiryText),
            nameof(OccupyPage.ReservationCountdownText),
            nameof(OccupyPage.OccupyStatusText),
            nameof(OccupyPage.IsOccupyRunning),
            nameof(OccupyPage.IsOccupyStopped),
            nameof(OccupyPage.ReReserveDelaySeconds),
            nameof(OccupyPage.SelectedOccupyCheckIntervalModeIndex),
            nameof(OccupyPage.AutoReleaseReservationEnabled),
            nameof(OccupyPage.AutoReleaseLeadSeconds),
            nameof(OccupyPage.IsAutoReleaseSuppressedByOccupy),
            nameof(OccupyPage.AutoReleaseStatusText));
    }

    private Task RefreshReservationAsync(bool showNotificationOnError)
    {
        EnsureOccupyPageConfigured();
        return OccupyPage.RefreshReservationAsync(showNotificationOnError);
    }

    private void QueueAutoReleaseReservationRefresh()
    {
        EnsureOccupyPageConfigured();
        OccupyPage.QueueAutoReleaseReservationRefresh();
    }

    private void QueueAutoReleaseCheck()
    {
        EnsureOccupyPageConfigured();
        OccupyPage.QueueAutoReleaseCheck();
    }

    private void UpdateReservationPresentation(ReservationInfo? info)
    {
        EnsureOccupyPageConfigured();
        OccupyPage.UpdateReservationPresentation(info);
    }

    private void ApplyOccupyStatus(CoordinatorStatus status)
    {
        EnsureOccupyPageConfigured();
        OccupyPage.ApplyStatus(status);
    }

    private void OnReservationCountdownTick(object? sender, EventArgs e)
    {
        EnsureOccupyPageConfigured();
        OccupyPage.OnCountdownTick();
        GrabPage.UpdateLastRequestText();
        GlobalLeakPage.UpdateLastRequestText();
        TomorrowReservationPage.UpdateLastRequestText();
        GrabPage.UpdateRuntimeClock();
        GlobalLeakPage.UpdateRuntimeClock();
        RefreshSidebarSessionExpirationPresentation(GetCurrentTime());
        UpdateHomeDashboardClock();
    }
}
