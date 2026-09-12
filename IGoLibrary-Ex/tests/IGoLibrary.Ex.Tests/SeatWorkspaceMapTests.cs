using IGoLibrary.Ex.Application.Services;
using Avalonia.Headless.XUnit;
using IGoLibrary.Ex.Desktop.ViewModels;
using IGoLibrary.Ex.Domain.Models;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Tests;

[Collection(NonParallelTestCollection.Name)]
public sealed class SeatWorkspaceMapTests
{
    [AvaloniaFact]
    public async Task FilterAndViewSwitch_KeepCoordinatesAndSelectedOccupiedSeats()
    {
        using var workspace = CreateWorkspace();
        var occupied = workspace.Seats[1];
        occupied.IsSelected = true;
        occupied.LabelText = "靠窗";
        var projection = workspace.Projection;
        workspace.SeatFilterText = "靠窗";
        await workspace.RefreshAsync();
        Assert.Equal(1, workspace.VisibleSeatResultCount);
        Assert.True(occupied.IsFilterVisible);
        workspace.ShowAvailableOnly = true;
        workspace.SelectedViewIndex = 1;
        await workspace.RefreshAsync();
        Assert.False(occupied.IsFilterVisible);
        Assert.True(occupied.IsSelected);
        Assert.True(workspace.ShowSeatFilterEmptyState);
        workspace.IsListMode = true;
        workspace.IsListMode = false;
        await workspace.RefreshAsync();
        Assert.True(occupied.IsFilterVisible);
        Assert.Same(projection, workspace.Projection);
        Assert.Same(occupied, workspace.Seats[1]);
        Assert.True(workspace.IsMapMode);
    }

    [AvaloniaFact]
    public async Task Locate_UsesExactMatchBeforeSubstring_AndDoesNotSelectOrClearFilter()
    {
        using var workspace = CreateWorkspace();
        workspace.ShowAvailableOnly = true;
        workspace.SeatFilterText = "1";
        await workspace.RefreshAsync();
        workspace.LocateText = "2";
        workspace.LocateCommand.Execute(null);
        Assert.Equal("b", Assert.Single(workspace.LocateResults).SeatKey);
        Assert.Equal("b", workspace.LocatedSeat!.SeatKey);
        Assert.Contains("不符合", workspace.LocateHint);
        Assert.All(workspace.Seats, seat => Assert.False(seat.IsSelected));
        Assert.True(workspace.ShowAvailableOnly);
        workspace.LocateText = "missing";
        workspace.LocateCommand.Execute(null);
        Assert.Null(workspace.LocatedSeat);
        Assert.Empty(workspace.LocateResults);
        Assert.All(workspace.Seats, seat => Assert.False(seat.IsLocated));
    }

    [AvaloniaFact]
    public void Locate_DuplicateNamesRequireExplicitChoice()
    {
        var layout = SeatLayoutTestData.Small() with { Seats = [new("a", "同名", false, 0, 0), new("b", "同名", true, 1, 0)] };
        using var workspace = CreateWorkspace(layout);
        workspace.LocateText = "同名";
        workspace.LocateCommand.Execute(null);
        Assert.Equal(2, workspace.LocateResults.Count);
        Assert.Null(workspace.LocatedSeat);
        workspace.LocatedSeat = workspace.LocateResults[1];
        Assert.True(workspace.Seats[1].IsLocated);
        Assert.False(workspace.Seats[0].IsLocated);
    }

    [AvaloniaFact]
    public async Task OldFilterCannotOverwriteNewLayoutOrKeepLoadingAfterClear()
    {
        using var workspace = CreateWorkspace();
        workspace.SeatFilterText = "2";
        var old = workspace.RefreshAsync();
        workspace.Clear();
        Populate(workspace, SeatLayoutTestData.Small(9) with { Seats = [new("new", "2", false, 1, 0)] });
        await workspace.RefreshAsync();
        await old;
        Assert.Equal(9, workspace.LibraryId);
        Assert.True(Assert.Single(workspace.Seats).IsFilterVisible);
        Assert.Equal(1, workspace.VisibleSeatResultCount);
        Assert.False(workspace.IsApplyingSeatFilter);
    }

    [AvaloniaFact]
    public void DuplicateKeys_FallBackAndSynchronizeSelection_WithoutLeakingOldSubscriptions()
    {
        var layout = SeatLayoutTestData.Small() with { Seats = [new("a", "1", false, 0, 0), new("a", "2", false, 1, 0)] };
        using var workspace = CreateWorkspace(layout);
        Assert.True(workspace.IsListMode);
        workspace.IsListMode = false;
        Assert.True(workspace.IsListMode);
        workspace.Seats[0].IsSelected = true;
        Assert.True(workspace.Seats[1].IsSelected);
        var old = workspace.Seats[0];
        Populate(workspace, SeatLayoutTestData.Small());
        old.IsSelected = false;
        old.IsSelected = true;
        Assert.False(workspace.Seats[0].IsSelected);
    }

    [AvaloniaFact]
    public void Diagnostics_AreStructuredAndDeduplicated_NoResponseOrLabelText()
    {
        var logger = new LayoutLogger();
        using var workspace = new SeatWorkspaceViewModel(new ActivityLogService(), logger);
        var layout = SeatLayoutTestData.Small() with { MaxX = 1, MaxY = 1, InvalidLayoutItemCount = 2,
            LayoutItems = [new(99, 0, 0, "", "private-facility-name", null, null)] };
        Populate(workspace, layout);
        workspace.Seats[0].LabelText = "private-label";
        workspace.ApplyLayout(layout);
        var warning = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Equal(2, warning.Fields["InvalidCount"]);
        Assert.Equal(1, warning.Fields["UnknownCount"]);
        Assert.Equal(true, warning.Fields["BoundaryExpanded"]);
        Assert.All(logger.Entries, entry => Assert.DoesNotContain("private", entry.Message));
        Assert.Equal(2, logger.Entries.Count(entry => entry.Id.Id == 3400));
    }

    [Theory]
    [InlineData(1, "空闲")]
    [InlineData(2, "平台已选")]
    [InlineData(3, "有人")]
    [InlineData(4, "暂离")]
    [InlineData(99, "有人（未知状态 99）")]
    public void DetailedStatusDoesNotChangeAvailability(int status, string expected)
    {
        var seat = new SeatItemViewModel("a", "1", true) { SeatStatus = status };
        Assert.Equal(expected, seat.LayoutStatusText);
        Assert.False(seat.IsAvailable);
    }

    internal static SeatWorkspaceViewModel CreateWorkspace(LibraryLayout? layout = null)
    {
        var workspace = new SeatWorkspaceViewModel(new ActivityLogService());
        Populate(workspace, layout ?? SeatLayoutTestData.Small());
        return workspace;
    }
    internal static void Populate(SeatWorkspaceViewModel workspace, LibraryLayout layout)
    {
        workspace.CancelFiltering();
        workspace.Seats.Clear();
        foreach (var seat in layout.Seats)
            workspace.Seats.Add(new(seat.SeatKey, seat.SeatName, seat.IsOccupied) { SeatStatus = seat.SeatStatus });
        workspace.ApplyLayout(layout);
    }
    private sealed record LogRecord(LogLevel Level, EventId Id, string Message, Dictionary<string, object?> Fields);
    private sealed class LayoutLogger : ILogger<SeatWorkspaceViewModel>
    {
        public List<LogRecord> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add(new(level, id, formatter(state, exception), ((IEnumerable<KeyValuePair<string, object?>>)state!).ToDictionary()));
    }
}
