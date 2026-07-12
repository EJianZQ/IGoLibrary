using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Desktop.ViewModels;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Tests;

public sealed class SeatLabelViewModelTests
{
    private static readonly LibrarySummary Library = new(7, "自科", "4F", true);

    [Fact]
    public async Task BatchCommand_SetsAllSelectedSeatsIncludingFilteredOutSeat()
    {
        var labelService = new FakeSeatLabelService();
        var dialog = new FakeSeatLabelDialogService();
        dialog.Results.Enqueue("  靠窗  ");
        var viewModel = CreateViewModel(labelService, dialog);
        await PopulateAsync(viewModel);
        viewModel.ApplySeatLabels([new SeatLabel("seat-1", "1", "旧标签")]);
        viewModel.VisibleSeats[0].IsSelected = true;
        viewModel.VisibleSeats[1].IsSelected = true;
        viewModel.VisibleSeats[1].IsFilterVisible = false;

        await viewModel.SetSelectedSeatLabelCommand.ExecuteAsync(null);

        Assert.Equal(1, labelService.SetCalls);
        Assert.Equal("靠窗", viewModel.VisibleSeats[0].LabelText);
        Assert.Equal("靠窗", viewModel.VisibleSeats[1].LabelText);
        Assert.Contains("2 个座位", Assert.Single(dialog.Requests).Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ContextEdit_OnlyChangesClickedSeatAndDoesNotChangeSelection()
    {
        var labelService = new FakeSeatLabelService();
        var dialog = new FakeSeatLabelDialogService();
        dialog.Results.Enqueue("新标签");
        var viewModel = CreateViewModel(labelService, dialog);
        await PopulateAsync(viewModel);
        viewModel.VisibleSeats[0].IsSelected = true;
        var target = viewModel.VisibleSeats[1];

        await target.EditLabelCommand.ExecuteAsync(null);

        Assert.True(viewModel.VisibleSeats[0].IsSelected);
        Assert.False(target.IsSelected);
        Assert.Equal("新标签", target.LabelText);
    }

    [Fact]
    public async Task ContextDelete_RemovesOnlyClickedLabel()
    {
        var labelService = new FakeSeatLabelService();
        var viewModel = CreateViewModel(labelService, new FakeSeatLabelDialogService());
        await PopulateAsync(viewModel);
        viewModel.ApplySeatLabels(
        [
            new SeatLabel("seat-1", "1", "A"),
            new SeatLabel("seat-2", "2", "B")
        ]);

        await viewModel.VisibleSeats[0].DeleteLabelCommand.ExecuteAsync(null);

        Assert.Equal(1, labelService.DeleteCalls);
        Assert.Null(viewModel.VisibleSeats[0].LabelText);
        Assert.Equal("B", viewModel.VisibleSeats[1].LabelText);
    }

    [Fact]
    public async Task CancelledDialog_DoesNotWrite()
    {
        var labelService = new FakeSeatLabelService();
        var viewModel = CreateViewModel(labelService, new FakeSeatLabelDialogService());
        await PopulateAsync(viewModel);
        viewModel.VisibleSeats[0].IsSelected = true;

        await viewModel.SetSelectedSeatLabelCommand.ExecuteAsync(null);

        Assert.Equal(0, labelService.SetCalls);
        Assert.Null(viewModel.VisibleSeats[0].LabelText);
    }

    [Fact]
    public async Task EditingToSameNormalizedText_DoesNotWriteAgain()
    {
        var labelService = new FakeSeatLabelService();
        var dialog = new FakeSeatLabelDialogService();
        dialog.Results.Enqueue("  常用  ");
        var viewModel = CreateViewModel(labelService, dialog);
        await PopulateAsync(viewModel);
        viewModel.ApplySeatLabels([new SeatLabel("seat-1", "1", "常用")]);

        await viewModel.VisibleSeats[0].EditLabelCommand.ExecuteAsync(null);

        Assert.Equal(0, labelService.SetCalls);
        Assert.Equal("常用", viewModel.VisibleSeats[0].LabelText);
    }

    [Fact]
    public async Task RestoringSelectionDraft_DoesNotRevertSavedLabel()
    {
        var labelService = new FakeSeatLabelService();
        var dialog = new FakeSeatLabelDialogService();
        dialog.Results.Enqueue("安静区域");
        var viewModel = CreateViewModel(labelService, dialog);
        await PopulateAsync(viewModel);
        viewModel.BeginDraft();
        viewModel.VisibleSeats[0].IsSelected = true;

        await viewModel.SetSelectedSeatLabelCommand.ExecuteAsync(null);
        viewModel.RestoreCommittedSelection();

        Assert.False(viewModel.VisibleSeats[0].IsSelected);
        Assert.Equal("安静区域", viewModel.VisibleSeats[0].LabelText);
    }

    [Fact]
    public async Task PersistenceFailure_LeavesOriginalLabelAndShowsWarning()
    {
        var labelService = new FakeSeatLabelService { SetException = new InvalidOperationException("database failed") };
        var dialog = new FakeSeatLabelDialogService();
        dialog.Results.Enqueue("新标签");
        var notifications = new FakeNotificationService();
        var viewModel = CreateViewModel(labelService, dialog, notifications);
        await PopulateAsync(viewModel);
        viewModel.ApplySeatLabels([new SeatLabel("seat-1", "1", "原标签")]);
        viewModel.VisibleSeats[0].IsSelected = true;

        await viewModel.SetSelectedSeatLabelCommand.ExecuteAsync(null);

        Assert.Equal("原标签", viewModel.VisibleSeats[0].LabelText);
        Assert.Contains(notifications.Warnings, warning => warning.Title == "保存座位标签失败");
    }

    [Fact]
    public async Task ApplyingLabels_IgnoresUnknownSeatsAndClearsStaleState()
    {
        var viewModel = CreateViewModel(new FakeSeatLabelService(), new FakeSeatLabelDialogService());
        await PopulateAsync(viewModel);
        viewModel.VisibleSeats[0].LabelText = "旧标签";

        viewModel.ApplySeatLabels([new SeatLabel("missing", "99", "不存在")]);

        Assert.All(viewModel.VisibleSeats, seat => Assert.Null(seat.LabelText));
    }

    [Fact]
    public async Task Filter_MatchesSeatNumberOrLabelIgnoringCase()
    {
        var viewModel = CreateViewModel(new FakeSeatLabelService(), new FakeSeatLabelDialogService());
        await PopulateAsync(viewModel);
        viewModel.ApplySeatLabels([new SeatLabel("seat-1", "1", "Window Seat")]);

        viewModel.SeatFilterText = "  window  ";
        await WaitForFilterAsync(viewModel, () => viewModel.VisibleSeatResultCount == 1);

        Assert.True(viewModel.VisibleSeats[0].IsFilterVisible);
        Assert.False(viewModel.VisibleSeats[1].IsFilterVisible);

        viewModel.SeatFilterText = "2";
        await WaitForFilterAsync(
            viewModel,
            () => viewModel.VisibleSeatResultCount == 1 && viewModel.VisibleSeats[1].IsFilterVisible);

        Assert.False(viewModel.VisibleSeats[0].IsFilterVisible);
        Assert.True(viewModel.VisibleSeats[1].IsFilterVisible);
    }

    [Fact]
    public async Task Filter_ReappliesWhenLabelsChange()
    {
        var viewModel = CreateViewModel(new FakeSeatLabelService(), new FakeSeatLabelDialogService());
        await PopulateAsync(viewModel);
        viewModel.SeatFilterText = "安静";
        await WaitForFilterAsync(viewModel, () => viewModel.VisibleSeatResultCount == 0);

        viewModel.ApplySeatLabels([new SeatLabel("seat-2", "2", "安静区域")]);
        await WaitForFilterAsync(
            viewModel,
            () => viewModel.VisibleSeatResultCount == 1 && viewModel.VisibleSeats[1].IsFilterVisible);

        viewModel.ApplySeatLabels([]);
        await WaitForFilterAsync(viewModel, () => viewModel.VisibleSeatResultCount == 0);

        Assert.All(viewModel.VisibleSeats, seat => Assert.False(seat.IsFilterVisible));
    }

    [Fact]
    public void SeatItemWithoutCallbacks_DoesNotExposeLabelEditing()
    {
        var seat = new SeatItemViewModel("seat-1", "1", false);

        Assert.False(seat.SupportsLabelEditing);
        Assert.False(seat.HasLabel);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("靠窗", true)]
    public void EditorViewModel_ValidatesInput(string text, bool expectedCanConfirm)
    {
        var viewModel = new SeatLabelEditorViewModel(new SeatLabelDialogRequest("标题", "说明", text));

        Assert.Equal(expectedCanConfirm, viewModel.CanConfirm);
    }

    private static MultiSeatSelectionViewModel CreateViewModel(
        FakeSeatLabelService labelService,
        FakeSeatLabelDialogService dialog,
        FakeNotificationService? notifications = null)
    {
        var workflow = new VenueWorkflowService(
            new FakeLibraryService(),
            labelService,
            new FakeSessionService(),
            new FakeTraceIntApiClient(),
            new FakeSettingsService(AppSettings.Default));
        var viewModel = new MultiSeatSelectionViewModel(
            workflow,
            new ActivityLogService(),
            notifications ?? new FakeNotificationService(),
            dialog);
        viewModel.Configure(() => Library, () => true, () => true);
        return viewModel;
    }

    private static Task PopulateAsync(MultiSeatSelectionViewModel viewModel)
    {
        return viewModel.PopulateSeatsAsync(
            new LibraryLayout(
                Library.LibraryId,
                Library.Name,
                Library.Floor,
                true,
                2,
                2,
                0,
                [new SeatSnapshot("seat-1", "1", false, 0, 0), new SeatSnapshot("seat-2", "2", false, 1, 0)]),
            preserveSelection: false);
    }

    private static async Task WaitForFilterAsync(
        MultiSeatSelectionViewModel viewModel,
        Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (!viewModel.IsApplyingSeatFilter && condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail("座位筛选未在预期时间内完成。");
    }
}
