using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Desktop.ViewModels;

public partial class MainWindowWorkflowViewModel
{
    private bool _multiSeatSelectionConfigured;

    public ObservableCollection<SeatItemViewModel> VisibleSeats => MultiSeatSelection.VisibleSeats;

    public ObservableCollection<SeatReference> SelectedSeats => MultiSeatSelection.SelectedSeats;

    public string SeatFilterText
    {
        get => MultiSeatSelection.SeatFilterText;
        set => MultiSeatSelection.SeatFilterText = value;
    }

    public bool ShowAvailableOnly
    {
        get => MultiSeatSelection.ShowAvailableOnly;
        set => MultiSeatSelection.ShowAvailableOnly = value;
    }

    public bool IsApplyingSeatFilter
    {
        get => MultiSeatSelection.IsApplyingSeatFilter;
        set => MultiSeatSelection.IsApplyingSeatFilter = value;
    }

    public int VisibleSeatResultCount
    {
        get => MultiSeatSelection.VisibleSeatResultCount;
        set => MultiSeatSelection.VisibleSeatResultCount = value;
    }

    public int SelectedSeatCount => MultiSeatSelection.SelectedSeatCount;

    public bool HasSelectedSeats => MultiSeatSelection.HasSelectedSeats;

    public bool HasNoSelectedSeats => MultiSeatSelection.HasNoSelectedSeats;

    public int DraftSelectedSeatCount => MultiSeatSelection.DraftSelectedSeatCount;

    public bool HasVisibleSeatResults => MultiSeatSelection.HasVisibleSeatResults;

    public bool HasNoVisibleSeatResults => MultiSeatSelection.HasNoVisibleSeatResults;

    public bool HasSeatLayout => MultiSeatSelection.HasSeatLayout;

    public bool HasNoSeatLayout => MultiSeatSelection.HasNoSeatLayout;

    public bool ShowSeatFilterEmptyState => MultiSeatSelection.ShowSeatFilterEmptyState;

    public string SelectedSeatSummaryText => MultiSeatSelection.SelectedSeatSummaryText;

    public string SelectedSeatHintText => MultiSeatSelection.SelectedSeatHintText;

    public string DraftSelectedSeatSummaryText => MultiSeatSelection.DraftSelectedSeatSummaryText;

    public IRelayCommand<SeatReference?> RemoveSelectedSeatCommand
    {
        get
        {
            EnsureMultiSeatSelectionConfigured();
            return MultiSeatSelection.RemoveSelectedSeatCommand;
        }
    }

    public IAsyncRelayCommand SaveFavoritesCommand
    {
        get
        {
            EnsureMultiSeatSelectionConfigured();
            return MultiSeatSelection.SaveFavoritesCommand;
        }
    }

    public IAsyncRelayCommand LoadFavoritesCommand
    {
        get
        {
            EnsureMultiSeatSelectionConfigured();
            return MultiSeatSelection.LoadFavoritesCommand;
        }
    }

    public IRelayCommand ClearSelectedSeatsCommand
    {
        get
        {
            EnsureMultiSeatSelectionConfigured();
            return MultiSeatSelection.ClearSelectedSeatsCommand;
        }
    }

    private void EnsureMultiSeatSelectionConfigured()
    {
        if (_multiSeatSelectionConfigured)
        {
            return;
        }

        _multiSeatSelectionConfigured = true;
        MultiSeatSelection.Configure(
            () => SelectedLibrary,
            () => CanEditGrabConfiguration,
            () => IsGrabSeatSelectionOverlayOpen);
    }

    private void ConfigureMultiSeatSelectionPropertyBridge(ViewModelPropertyBridge propertyBridge)
    {
        propertyBridge.ForwardSame(
            MultiSeatSelection,
            nameof(MultiSeatSelection.SeatFilterText),
            nameof(MultiSeatSelection.ShowAvailableOnly),
            nameof(MultiSeatSelection.IsApplyingSeatFilter),
            nameof(MultiSeatSelection.VisibleSeatResultCount),
            nameof(MultiSeatSelection.SelectedSeatCount),
            nameof(MultiSeatSelection.HasSelectedSeats),
            nameof(MultiSeatSelection.HasNoSelectedSeats),
            nameof(MultiSeatSelection.DraftSelectedSeatCount),
            nameof(MultiSeatSelection.HasVisibleSeatResults),
            nameof(MultiSeatSelection.HasNoVisibleSeatResults),
            nameof(MultiSeatSelection.HasSeatLayout),
            nameof(MultiSeatSelection.HasNoSeatLayout),
            nameof(MultiSeatSelection.ShowSeatFilterEmptyState),
            nameof(MultiSeatSelection.SelectedSeatSummaryText),
            nameof(MultiSeatSelection.SelectedSeatHintText),
            nameof(MultiSeatSelection.DraftSelectedSeatSummaryText));
    }
}
