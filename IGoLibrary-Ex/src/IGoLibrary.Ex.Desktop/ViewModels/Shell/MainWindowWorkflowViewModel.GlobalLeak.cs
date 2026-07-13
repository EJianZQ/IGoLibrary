using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Desktop.ViewModels;

public partial class MainWindowWorkflowViewModel
{
    private bool _globalLeakPageConfigured;

    public ObservableCollection<GlobalLeakLibraryItemViewModel> GlobalLeakLibraries => GlobalLeakPage.GlobalLeakLibraries;

    public ObservableCollection<GlobalLeakLibraryTarget> SelectedGlobalLeakLibraries => GlobalLeakPage.SelectedGlobalLeakLibraries;

    public ObservableCollection<GlobalLeakLibraryPriorityItemViewModel> SelectedGlobalLeakLibraryPriorities =>
        GlobalLeakPage.SelectedGlobalLeakLibraryPriorities;

    public ObservableCollection<GlobalLeakLibraryPriorityItemViewModel> DraftGlobalLeakLibraryPriorities =>
        GlobalLeakPage.DraftGlobalLeakLibraryPriorities;

    public bool IsGlobalLeakLibraryPickerOpen
    {
        get => GlobalLeakPage.IsGlobalLeakLibraryPickerOpen;
        set => GlobalLeakPage.IsGlobalLeakLibraryPickerOpen = value;
    }

    public string GlobalLeakStatusText
    {
        get => GlobalLeakPage.GlobalLeakStatusText;
        set => GlobalLeakPage.GlobalLeakStatusText = value;
    }

    public bool IsGlobalLeakTaskActive
    {
        get => GlobalLeakPage.IsGlobalLeakTaskActive;
        set => GlobalLeakPage.IsGlobalLeakTaskActive = value;
    }

    public int GlobalLeakScanRoundCount
    {
        get => GlobalLeakPage.GlobalLeakScanRoundCount;
        set => GlobalLeakPage.GlobalLeakScanRoundCount = value;
    }

    public int GlobalLeakRequestCount
    {
        get => GlobalLeakPage.GlobalLeakRequestCount;
        set => GlobalLeakPage.GlobalLeakRequestCount = value;
    }

    public string GlobalLeakLastRequestText
    {
        get => GlobalLeakPage.GlobalLeakLastRequestText;
        set => GlobalLeakPage.GlobalLeakLastRequestText = value;
    }

    public string GlobalLeakRuntimeText
    {
        get => GlobalLeakPage.GlobalLeakRuntimeText;
        set => GlobalLeakPage.GlobalLeakRuntimeText = value;
    }

    public int GlobalLeakScanIntervalSeconds
    {
        get => GlobalLeakPage.GlobalLeakScanIntervalSeconds;
        set => GlobalLeakPage.GlobalLeakScanIntervalSeconds = value;
    }

    public bool HasGlobalLeakLibraries => GlobalLeakPage.HasGlobalLeakLibraries;

    public bool HasNoGlobalLeakLibraries => GlobalLeakPage.HasNoGlobalLeakLibraries;

    public int SelectedGlobalLeakLibraryCount => GlobalLeakPage.SelectedGlobalLeakLibraryCount;

    public bool HasSelectedGlobalLeakLibraries => GlobalLeakPage.HasSelectedGlobalLeakLibraries;

    public bool HasNoSelectedGlobalLeakLibraries => GlobalLeakPage.HasNoSelectedGlobalLeakLibraries;

    public bool HasDraftGlobalLeakLibraries => GlobalLeakPage.HasDraftGlobalLeakLibraries;

    public bool HasNoDraftGlobalLeakLibraries => GlobalLeakPage.HasNoDraftGlobalLeakLibraries;

    public bool CanEditGlobalLeakConfiguration => GlobalLeakPage.CanEditGlobalLeakConfiguration;

    public bool CanCancelGlobalLeakLibraryPicker => GlobalLeakPage.CanCancelGlobalLeakLibraryPicker;

    public string SelectedGlobalLeakLibrarySummaryText => GlobalLeakPage.SelectedGlobalLeakLibrarySummaryText;

    public int DraftGlobalLeakLibraryCount => GlobalLeakPage.DraftGlobalLeakLibraryCount;

    public string DraftGlobalLeakLibrarySummaryText => GlobalLeakPage.DraftGlobalLeakLibrarySummaryText;

    public string GlobalLeakDashboardStatusText => GlobalLeakPage.GlobalLeakDashboardStatusText;

    public IBrush GlobalLeakDashboardStatusBrush => GlobalLeakPage.GlobalLeakDashboardStatusBrush;

    public IAsyncRelayCommand OpenGlobalLeakLibraryPickerCommand
    {
        get
        {
            EnsureGlobalLeakPageConfigured();
            return GlobalLeakPage.OpenGlobalLeakLibraryPickerCommand;
        }
    }

    public IAsyncRelayCommand RefreshGlobalLeakLibrariesCommand
    {
        get
        {
            EnsureGlobalLeakPageConfigured();
            return GlobalLeakPage.RefreshGlobalLeakLibrariesCommand;
        }
    }

    public IAsyncRelayCommand ConfirmGlobalLeakLibrariesCommand
    {
        get
        {
            EnsureGlobalLeakPageConfigured();
            return GlobalLeakPage.ConfirmGlobalLeakLibrariesCommand;
        }
    }

    public IRelayCommand CancelGlobalLeakLibrariesCommand
    {
        get
        {
            EnsureGlobalLeakPageConfigured();
            return GlobalLeakPage.CancelGlobalLeakLibrariesCommand;
        }
    }

    public IAsyncRelayCommand SelectAllGlobalLeakLibrariesCommand
    {
        get
        {
            EnsureGlobalLeakPageConfigured();
            return GlobalLeakPage.SelectAllGlobalLeakLibrariesCommand;
        }
    }

    public IAsyncRelayCommand ClearGlobalLeakLibrarySelectionCommand
    {
        get
        {
            EnsureGlobalLeakPageConfigured();
            return GlobalLeakPage.ClearGlobalLeakLibrarySelectionCommand;
        }
    }

    public IAsyncRelayCommand ClearDraftGlobalLeakLibrariesCommand
    {
        get
        {
            EnsureGlobalLeakPageConfigured();
            return GlobalLeakPage.ClearDraftGlobalLeakLibrariesCommand;
        }
    }

    public IAsyncRelayCommand<GlobalLeakLibraryTarget?> RemoveSelectedGlobalLeakLibraryCommand
    {
        get
        {
            EnsureGlobalLeakPageConfigured();
            return GlobalLeakPage.RemoveSelectedGlobalLeakLibraryCommand;
        }
    }

    public IRelayCommand<GlobalLeakLibraryPriorityItemViewModel?> MoveGlobalLeakLibraryUpCommand
    {
        get
        {
            EnsureGlobalLeakPageConfigured();
            return GlobalLeakPage.MoveGlobalLeakLibraryUpCommand;
        }
    }

    public IRelayCommand<GlobalLeakLibraryPriorityItemViewModel?> MoveGlobalLeakLibraryDownCommand
    {
        get
        {
            EnsureGlobalLeakPageConfigured();
            return GlobalLeakPage.MoveGlobalLeakLibraryDownCommand;
        }
    }

    public IAsyncRelayCommand StartGlobalLeakCommand
    {
        get
        {
            EnsureGlobalLeakPageConfigured();
            return GlobalLeakPage.StartGlobalLeakCommand;
        }
    }

    public IAsyncRelayCommand StopGlobalLeakCommand
    {
        get
        {
            EnsureGlobalLeakPageConfigured();
            return GlobalLeakPage.StopGlobalLeakCommand;
        }
    }

    public bool MoveDraftGlobalLeakLibrary(int sourceLibraryId, int targetLibraryId, bool insertAfter)
    {
        EnsureGlobalLeakPageConfigured();
        return GlobalLeakPage.MoveDraftGlobalLeakLibrary(sourceLibraryId, targetLibraryId, insertAfter);
    }

    public bool SetGlobalLeakLibraryDropIndicator(int targetLibraryId, bool insertAfter)
    {
        EnsureGlobalLeakPageConfigured();
        return GlobalLeakPage.SetGlobalLeakLibraryDropIndicator(targetLibraryId, insertAfter);
    }

    public void ClearGlobalLeakLibraryDropIndicators()
    {
        EnsureGlobalLeakPageConfigured();
        GlobalLeakPage.ClearGlobalLeakLibraryDropIndicators();
    }

    private void EnsureGlobalLeakPageConfigured()
    {
        if (_globalLeakPageConfigured)
        {
            return;
        }

        _globalLeakPageConfigured = true;
        GlobalLeakPage.ConfigureOrchestration(
            () => IsAuthorized,
            () => RefreshReservationAsync(showNotificationOnError: false),
            RecordSuccessfulReservationAsync,
            status =>
            {
                UpdateGuardTracking(status.LastUpdatedAt ?? GetCurrentTime());
                UpdateHomeHeroPresentation(GetCurrentTime());
                UpdateHomeSystemInfoPresentation();
            });
        GlobalLeakPage.InitializeStatus();
    }

    private void ConfigureGlobalLeakPropertyBridge(ViewModelPropertyBridge propertyBridge)
    {
        propertyBridge.ForwardSame(
            GlobalLeakPage,
            nameof(GlobalLeakPage.IsGlobalLeakLibraryPickerOpen),
            nameof(GlobalLeakPage.GlobalLeakStatusText),
            nameof(GlobalLeakPage.IsGlobalLeakTaskActive),
            nameof(GlobalLeakPage.GlobalLeakScanRoundCount),
            nameof(GlobalLeakPage.GlobalLeakRequestCount),
            nameof(GlobalLeakPage.GlobalLeakLastRequestText),
            nameof(GlobalLeakPage.GlobalLeakRuntimeText),
            nameof(GlobalLeakPage.GlobalLeakScanIntervalSeconds),
            nameof(GlobalLeakPage.HasGlobalLeakLibraries),
            nameof(GlobalLeakPage.HasNoGlobalLeakLibraries),
            nameof(GlobalLeakPage.SelectedGlobalLeakLibraryCount),
            nameof(GlobalLeakPage.HasSelectedGlobalLeakLibraries),
            nameof(GlobalLeakPage.HasNoSelectedGlobalLeakLibraries),
            nameof(GlobalLeakPage.HasDraftGlobalLeakLibraries),
            nameof(GlobalLeakPage.HasNoDraftGlobalLeakLibraries),
            nameof(GlobalLeakPage.CanEditGlobalLeakConfiguration),
            nameof(GlobalLeakPage.CanCancelGlobalLeakLibraryPicker),
            nameof(GlobalLeakPage.SelectedGlobalLeakLibrarySummaryText),
            nameof(GlobalLeakPage.DraftGlobalLeakLibraryCount),
            nameof(GlobalLeakPage.DraftGlobalLeakLibrarySummaryText),
            nameof(GlobalLeakPage.GlobalLeakDashboardStatusText),
            nameof(GlobalLeakPage.GlobalLeakDashboardStatusBrush));
    }

    private void ApplyGlobalLeakStatus(CoordinatorStatus status)
    {
        GlobalLeakPage.ApplyStatus(status);
    }
}
