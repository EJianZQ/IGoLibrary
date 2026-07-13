using System.Collections.Specialized;
using IGoLibrary.Ex.Desktop.ViewModels;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Tests;

public sealed class GlobalLeakLibrarySelectionViewModelTests
{
    [Fact]
    public void RestoreCommittedLibraries_PreservesStoredPriorityAndUsesLatestLibraryMetadata()
    {
        var viewModel = CreateSelection();

        var result = viewModel.RestoreCommittedLibraries(
        [
            new GlobalLeakLibraryTarget(2, "旧场馆B", "旧楼层"),
            new GlobalLeakLibraryTarget(1, "旧场馆A", "旧楼层"),
            new GlobalLeakLibraryTarget(2, "重复场馆B", "重复楼层"),
            new GlobalLeakLibraryTarget(99, "已下线场馆", "旧楼层")
        ]);

        Assert.Equal(new GlobalLeakLibraryRestoreResult(2, 1), result);
        Assert.Equal([2, 1], viewModel.SelectedLibraries.Select(item => item.LibraryId).ToArray());
        Assert.Equal(["场馆B", "场馆A"], viewModel.SelectedLibraries.Select(item => item.LibraryName).ToArray());
        Assert.Equal([1, 2], viewModel.SelectedPriorities.Select(item => item.Priority).ToArray());
    }

    [Fact]
    public void DraftSelection_AppendsNewAndReselectedLibrariesAtLowestPriority()
    {
        var viewModel = CreateSelection();
        viewModel.BeginDraft();

        viewModel.Libraries[0].IsSelected = true;
        viewModel.Libraries[1].IsSelected = true;
        viewModel.Libraries[0].IsSelected = false;
        viewModel.Libraries[0].IsSelected = true;

        Assert.Equal([2, 1], DraftIds(viewModel));
        Assert.False(viewModel.DraftPriorities[0].CanMoveUp);
        Assert.True(viewModel.DraftPriorities[0].CanMoveDown);
        Assert.True(viewModel.DraftPriorities[1].CanMoveUp);
        Assert.False(viewModel.DraftPriorities[1].CanMoveDown);
    }

    [Fact]
    public void SelectAllDraft_PreservesExistingPriorityAndAppendsRemainingLibraryOrder()
    {
        var viewModel = CreateSelection();
        viewModel.BeginDraft();
        viewModel.Libraries[1].IsSelected = true;

        viewModel.SelectAllDraft();

        Assert.Equal([2, 1, 3], DraftIds(viewModel));
        Assert.All(viewModel.Libraries, library => Assert.True(library.IsSelected));
    }

    [Fact]
    public void MoveOperations_ShareOneOrderedDraftAndRejectInvalidBoundaries()
    {
        var viewModel = CreateSelection();
        viewModel.BeginDraft();
        viewModel.SelectAllDraft();

        Assert.True(viewModel.MoveDraftLibrary(3, 1, insertAfter: false));
        Assert.Equal([3, 1, 2], DraftIds(viewModel));

        Assert.True(viewModel.MoveDraftLibraryByOffset(3, 1));
        Assert.Equal([1, 3, 2], DraftIds(viewModel));

        Assert.True(viewModel.MoveDraftLibrary(1, 2, insertAfter: true));
        Assert.Equal([3, 2, 1], DraftIds(viewModel));
        Assert.False(viewModel.MoveDraftLibraryByOffset(3, -1));
        Assert.False(viewModel.MoveDraftLibrary(99, 1, insertAfter: false));
        Assert.False(viewModel.MoveDraftLibrary(2, 2, insertAfter: true));
    }

    [Fact]
    public void MoveDraftLibrary_RaisesOneMoveEvent_AndDoesNotMutateSamePositionDrop()
    {
        var viewModel = CreateSelection();
        viewModel.BeginDraft();
        viewModel.SelectAllDraft();
        var changes = new List<NotifyCollectionChangedEventArgs>();
        viewModel.DraftPriorities.CollectionChanged += (_, args) => changes.Add(args);

        Assert.True(viewModel.MoveDraftLibrary(3, 1, insertAfter: false));

        var change = Assert.Single(changes);
        Assert.Equal(NotifyCollectionChangedAction.Move, change.Action);
        Assert.Equal(2, change.OldStartingIndex);
        Assert.Equal(0, change.NewStartingIndex);
        Assert.Equal([3, 1, 2], DraftIds(viewModel));

        changes.Clear();
        Assert.False(viewModel.MoveDraftLibrary(3, 1, insertAfter: false));
        Assert.Empty(changes);
        Assert.Equal([3, 1, 2], DraftIds(viewModel));
    }

    [Fact]
    public void PopulateLibraries_ReconcilesDraftWithoutChangingItsRelativePriority()
    {
        var viewModel = CreateSelection();
        viewModel.RestoreCommittedLibraries(
        [
            new GlobalLeakLibraryTarget(2, "旧B", "旧"),
            new GlobalLeakLibraryTarget(1, "旧A", "旧"),
            new GlobalLeakLibraryTarget(3, "旧C", "旧")
        ]);
        viewModel.BeginDraft();

        viewModel.PopulateLibraries(
        [
            new LibrarySummary(1, "刷新后A", "4层", true, 100, 0, 0),
            new LibrarySummary(2, "刷新后B", "6层", true, 100, 0, 0)
        ]);

        Assert.Equal([2, 1], DraftIds(viewModel));
        Assert.Equal(["刷新后B", "刷新后A"], viewModel.DraftPriorities.Select(item => item.LibraryName).ToArray());

        viewModel.CancelDraft();

        Assert.Equal([2, 1], viewModel.SelectedLibraries.Select(item => item.LibraryId).ToArray());
    }

    [Fact]
    public void CommitAndCancelDraft_KeepCommittedOrderTransactional()
    {
        var viewModel = CreateSelection();
        viewModel.RestoreCommittedLibraries([new GlobalLeakLibraryTarget(1, "A", "3层")]);
        viewModel.BeginDraft();
        viewModel.Libraries[1].IsSelected = true;
        viewModel.MoveDraftLibrary(2, 1, insertAfter: false);

        viewModel.CancelDraft();
        Assert.Equal([1], viewModel.SelectedLibraries.Select(item => item.LibraryId).ToArray());

        viewModel.BeginDraft();
        viewModel.Libraries[1].IsSelected = true;
        viewModel.MoveDraftLibrary(2, 1, insertAfter: false);
        viewModel.CommitDraft();

        Assert.Equal([2, 1], viewModel.SelectedLibraries.Select(item => item.LibraryId).ToArray());
    }

    private static GlobalLeakLibrarySelectionViewModel CreateSelection()
    {
        var viewModel = new GlobalLeakLibrarySelectionViewModel();
        viewModel.PopulateLibraries(
        [
            new LibrarySummary(1, "场馆A", "3层", true, 120, 20, 10),
            new LibrarySummary(2, "场馆B", "5层", true, 80, 10, 5),
            new LibrarySummary(3, "场馆C", "7层", true, 60, 5, 3)
        ]);
        return viewModel;
    }

    private static int[] DraftIds(GlobalLeakLibrarySelectionViewModel viewModel)
    {
        return viewModel.DraftPriorities.Select(item => item.LibraryId).ToArray();
    }
}
