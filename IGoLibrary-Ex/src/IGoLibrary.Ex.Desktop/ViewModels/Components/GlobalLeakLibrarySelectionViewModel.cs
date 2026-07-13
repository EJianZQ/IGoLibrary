using System.Collections.ObjectModel;
using System.ComponentModel;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Desktop.ViewModels;

public sealed class GlobalLeakLibrarySelectionViewModel : ViewModelBase
{
    private bool _isSynchronizingSelection;

    public ObservableCollection<GlobalLeakLibraryItemViewModel> Libraries { get; } = [];

    public ObservableCollection<GlobalLeakLibraryTarget> SelectedLibraries { get; } = [];

    public ObservableCollection<GlobalLeakLibraryPriorityItemViewModel> SelectedPriorities { get; } = [];

    public ObservableCollection<GlobalLeakLibraryPriorityItemViewModel> DraftPriorities { get; } = [];

    public bool IsDraftActive { get; private set; }

    public bool HasLibraries => Libraries.Count > 0;

    public bool HasNoLibraries => !HasLibraries;

    public int SelectedCount => SelectedLibraries.Count;

    public bool HasSelectedLibraries => SelectedCount > 0;

    public bool HasNoSelectedLibraries => !HasSelectedLibraries;

    public int DraftCount => DraftPriorities.Count;

    public bool HasDraftLibraries => DraftCount > 0;

    public bool HasNoDraftLibraries => !HasDraftLibraries;

    public string SelectedSummaryText => HasSelectedLibraries
        ? $"已选 {SelectedCount} 个扫描场馆"
        : "尚未选择扫描场馆";

    public string DraftSummaryText => HasDraftLibraries
        ? $"本次已勾选 {DraftCount} 个场馆，右侧从上到下依次扫描"
        : "本次尚未勾选场馆";

    public void PopulateLibraries(IEnumerable<LibrarySummary> libraries)
    {
        var committedOrder = GetSelectedSnapshot();
        var draftOrder = GetDraftSnapshot();

        DisconnectLibraryItems();
        Libraries.Clear();

        var seenLibraryIds = new HashSet<int>();
        foreach (var library in libraries)
        {
            if (!seenLibraryIds.Add(library.LibraryId))
            {
                continue;
            }

            var item = new GlobalLeakLibraryItemViewModel(library);
            item.PropertyChanged += OnLibraryItemPropertyChanged;
            Libraries.Add(item);
        }

        ReplaceSelectedLibraries(ReconcileTargets(committedOrder));
        if (IsDraftActive)
        {
            ReplaceDraftPriorities(ReconcileTargets(draftOrder));
            ApplySelectionToItems(DraftPriorities.Select(static item => item.LibraryId));
        }
        else
        {
            ApplySelectionToItems(SelectedLibraries.Select(static item => item.LibraryId));
        }

        NotifyLibraryPresentationChanged();
    }

    public void ClearLibraries()
    {
        DisconnectLibraryItems();
        Libraries.Clear();
        ClearDropIndicators();
        IsDraftActive = false;
        DraftPriorities.Clear();

        SelectedLibraries.Clear();
        SelectedPriorities.Clear();

        NotifyLibraryPresentationChanged();
        NotifySelectedPresentationChanged();
        NotifyDraftPresentationChanged();
    }

    public GlobalLeakLibraryRestoreResult RestoreCommittedLibraries(
        IEnumerable<GlobalLeakLibraryTarget> storedLibraries)
    {
        var stored = DistinctTargets(storedLibraries);
        var availableIds = Libraries.Select(static library => library.LibraryId).ToHashSet();
        var skippedCount = stored.Count(target => !availableIds.Contains(target.LibraryId));
        var restored = ReconcileTargets(stored);

        ReplaceSelectedLibraries(restored);
        if (!IsDraftActive)
        {
            ApplySelectionToItems(SelectedLibraries.Select(static item => item.LibraryId));
        }

        return new GlobalLeakLibraryRestoreResult(SelectedLibraries.Count, skippedCount);
    }

    public void SetCommittedLibraries(IEnumerable<GlobalLeakLibraryTarget> libraries)
    {
        ReplaceSelectedLibraries(ReconcileTargets(libraries));
        IsDraftActive = false;
        DraftPriorities.Clear();
        ClearDropIndicators();
        ApplySelectionToItems(SelectedLibraries.Select(static item => item.LibraryId));
        NotifyDraftPresentationChanged();
    }

    public void BeginDraft()
    {
        IsDraftActive = true;
        ReplaceDraftPriorities(ReconcileTargets(SelectedLibraries));
        ApplySelectionToItems(DraftPriorities.Select(static item => item.LibraryId));
    }

    public void CommitDraft()
    {
        var committed = GetDraftSnapshot();
        ReplaceSelectedLibraries(committed);
        IsDraftActive = false;
        DraftPriorities.Clear();
        ClearDropIndicators();
        ApplySelectionToItems(SelectedLibraries.Select(static item => item.LibraryId));
        NotifyDraftPresentationChanged();
    }

    public void CancelDraft()
    {
        IsDraftActive = false;
        DraftPriorities.Clear();
        ClearDropIndicators();
        ApplySelectionToItems(SelectedLibraries.Select(static item => item.LibraryId));
        NotifyDraftPresentationChanged();
    }

    public void SelectAllDraft()
    {
        if (!IsDraftActive)
        {
            return;
        }

        var selectedIds = DraftPriorities.Select(static item => item.LibraryId).ToHashSet();
        foreach (var library in Libraries)
        {
            if (!selectedIds.Add(library.LibraryId))
            {
                continue;
            }

            DraftPriorities.Add(new GlobalLeakLibraryPriorityItemViewModel(ToTarget(library)));
        }

        ApplySelectionToItems(selectedIds);
        UpdateDraftPositions();
        NotifyDraftPresentationChanged();
    }

    public void ClearDraft()
    {
        if (!IsDraftActive)
        {
            return;
        }

        DraftPriorities.Clear();
        ClearDropIndicators();
        ApplySelectionToItems([]);
        NotifyDraftPresentationChanged();
    }

    public bool MoveDraftLibraryByOffset(int libraryId, int offset)
    {
        if (!IsDraftActive || offset == 0)
        {
            return false;
        }

        var sourceIndex = FindDraftIndex(libraryId);
        var destinationIndex = sourceIndex + offset;
        if (sourceIndex < 0 || destinationIndex < 0 || destinationIndex >= DraftPriorities.Count)
        {
            return false;
        }

        DraftPriorities.Move(sourceIndex, destinationIndex);
        UpdateDraftPositions();
        return true;
    }

    public bool MoveDraftLibrary(int sourceLibraryId, int targetLibraryId, bool insertAfter)
    {
        if (!IsDraftActive || sourceLibraryId == targetLibraryId)
        {
            return false;
        }

        var sourceIndex = FindDraftIndex(sourceLibraryId);
        var targetIndex = FindDraftIndex(targetLibraryId);
        if (sourceIndex < 0 || targetIndex < 0)
        {
            return false;
        }

        var destinationIndex = CalculateDropDestinationIndex(sourceIndex, targetIndex, insertAfter);
        if (destinationIndex == sourceIndex)
        {
            ClearDropIndicators();
            return false;
        }

        DraftPriorities.Move(sourceIndex, destinationIndex);
        ClearDropIndicators();
        UpdateDraftPositions();
        return true;
    }

    public bool SetDropIndicator(int targetLibraryId, bool insertAfter)
    {
        var target = DraftPriorities.FirstOrDefault(item => item.LibraryId == targetLibraryId);
        if (target is null)
        {
            ClearDropIndicators();
            return false;
        }

        foreach (var item in DraftPriorities)
        {
            item.IsDropBefore = ReferenceEquals(item, target) && !insertAfter;
            item.IsDropAfter = ReferenceEquals(item, target) && insertAfter;
        }

        return true;
    }

    public void ClearDropIndicators()
    {
        foreach (var item in DraftPriorities)
        {
            item.IsDropBefore = false;
            item.IsDropAfter = false;
        }
    }

    public GlobalLeakLibraryTarget[] GetSelectedSnapshot()
    {
        return SelectedLibraries.ToArray();
    }

    public GlobalLeakLibraryTarget[] GetDraftSnapshot()
    {
        return DraftPriorities.Select(static item => item.Target).ToArray();
    }

    public GlobalLeakLibraryTarget[] CreateSelectedSnapshotWithout(int libraryId)
    {
        return SelectedLibraries.Where(item => item.LibraryId != libraryId).ToArray();
    }

    private void OnLibraryItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isSynchronizingSelection ||
            e.PropertyName != nameof(GlobalLeakLibraryItemViewModel.IsSelected) ||
            sender is not GlobalLeakLibraryItemViewModel library)
        {
            return;
        }

        if (!IsDraftActive)
        {
            ApplySelectionToItems(SelectedLibraries.Select(static item => item.LibraryId));
            return;
        }

        var existingIndex = FindDraftIndex(library.LibraryId);
        if (library.IsSelected && existingIndex < 0)
        {
            DraftPriorities.Add(new GlobalLeakLibraryPriorityItemViewModel(ToTarget(library)));
        }
        else if (!library.IsSelected && existingIndex >= 0)
        {
            DraftPriorities.RemoveAt(existingIndex);
        }

        ClearDropIndicators();
        UpdateDraftPositions();
        NotifyDraftPresentationChanged();
    }

    private void ReplaceSelectedLibraries(IEnumerable<GlobalLeakLibraryTarget> libraries)
    {
        SelectedLibraries.Clear();
        SelectedPriorities.Clear();
        foreach (var library in DistinctTargets(libraries))
        {
            SelectedLibraries.Add(library);
            SelectedPriorities.Add(new GlobalLeakLibraryPriorityItemViewModel(library));
        }

        for (var index = 0; index < SelectedPriorities.Count; index++)
        {
            SelectedPriorities[index].UpdatePosition(index, SelectedPriorities.Count);
        }

        NotifySelectedPresentationChanged();
    }

    private void ReplaceDraftPriorities(IEnumerable<GlobalLeakLibraryTarget> libraries)
    {
        DraftPriorities.Clear();
        foreach (var library in DistinctTargets(libraries))
        {
            DraftPriorities.Add(new GlobalLeakLibraryPriorityItemViewModel(library));
        }

        UpdateDraftPositions();
        NotifyDraftPresentationChanged();
    }

    private GlobalLeakLibraryTarget[] ReconcileTargets(IEnumerable<GlobalLeakLibraryTarget> orderedTargets)
    {
        var availableById = Libraries.ToDictionary(static library => library.LibraryId);
        return DistinctTargets(orderedTargets)
            .Where(target => availableById.ContainsKey(target.LibraryId))
            .Select(target => ToTarget(availableById[target.LibraryId]))
            .ToArray();
    }

    private void ApplySelectionToItems(IEnumerable<int> selectedLibraryIds)
    {
        var selectedIds = selectedLibraryIds.ToHashSet();
        _isSynchronizingSelection = true;
        try
        {
            foreach (var library in Libraries)
            {
                library.IsSelected = selectedIds.Contains(library.LibraryId);
            }
        }
        finally
        {
            _isSynchronizingSelection = false;
        }
    }

    private void UpdateDraftPositions()
    {
        for (var index = 0; index < DraftPriorities.Count; index++)
        {
            DraftPriorities[index].UpdatePosition(index, DraftPriorities.Count);
        }
    }

    private int FindDraftIndex(int libraryId)
    {
        for (var index = 0; index < DraftPriorities.Count; index++)
        {
            if (DraftPriorities[index].LibraryId == libraryId)
            {
                return index;
            }
        }

        return -1;
    }

    private static int CalculateDropDestinationIndex(int sourceIndex, int targetIndex, bool insertAfter)
    {
        if (insertAfter)
        {
            return sourceIndex < targetIndex ? targetIndex : targetIndex + 1;
        }

        return sourceIndex < targetIndex ? targetIndex - 1 : targetIndex;
    }

    private void DisconnectLibraryItems()
    {
        foreach (var library in Libraries)
        {
            library.PropertyChanged -= OnLibraryItemPropertyChanged;
        }
    }

    private void NotifyLibraryPresentationChanged()
    {
        OnPropertyChanged(nameof(HasLibraries));
        OnPropertyChanged(nameof(HasNoLibraries));
    }

    private void NotifySelectedPresentationChanged()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelectedLibraries));
        OnPropertyChanged(nameof(HasNoSelectedLibraries));
        OnPropertyChanged(nameof(SelectedSummaryText));
    }

    private void NotifyDraftPresentationChanged()
    {
        OnPropertyChanged(nameof(DraftCount));
        OnPropertyChanged(nameof(HasDraftLibraries));
        OnPropertyChanged(nameof(HasNoDraftLibraries));
        OnPropertyChanged(nameof(DraftSummaryText));
    }

    private static GlobalLeakLibraryTarget ToTarget(GlobalLeakLibraryItemViewModel library)
    {
        return new GlobalLeakLibraryTarget(library.LibraryId, library.LibraryName, library.Floor);
    }

    private static GlobalLeakLibraryTarget[] DistinctTargets(IEnumerable<GlobalLeakLibraryTarget> libraries)
    {
        var seenIds = new HashSet<int>();
        return libraries.Where(library => seenIds.Add(library.LibraryId)).ToArray();
    }
}

public sealed record GlobalLeakLibraryRestoreResult(int RestoredCount, int SkippedCount);
