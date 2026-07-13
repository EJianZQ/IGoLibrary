using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using IGoLibrary.Ex.Desktop;
using IGoLibrary.Ex.Desktop.Controls;
using IGoLibrary.Ex.Desktop.ViewModels;

namespace IGoLibrary.Ex.Tests;

public sealed class MainWindowLayoutTests
{
    [AvaloniaFact]
    public void MainWindow_PreservesDefaultSizeAndExposesRememberSizeToggle()
    {
        var window = new MainWindow();

        Assert.Equal(1188, window.Width);
        Assert.Equal(840, window.Height);
        Assert.Equal(1000, window.MinWidth);
        Assert.Equal(680, window.MinHeight);
        Assert.NotNull(window.FindControl<ToggleSwitch>("RememberWindowSizeToggle"));
    }

    [AvaloniaFact]
    public void GrabPage_ProvidesOuterVerticalScrollingForExpandedSeatSelection()
    {
        var window = new MainWindow();
        var scrollViewer = Assert.IsType<ScrollViewer>(
            window.FindControl<ScrollViewer>("GrabPageScrollViewer"));

        Assert.Equal(ScrollBarVisibility.Disabled, scrollViewer.HorizontalScrollBarVisibility);
        Assert.Equal(ScrollBarVisibility.Auto, scrollViewer.VerticalScrollBarVisibility);
    }

    [AvaloniaFact]
    public void GrabSeatSelectionModal_StretchesToAvailableWidth()
    {
        var window = new MainWindow();
        var modal = Assert.IsType<Border>(window.FindControl<Border>("GrabSeatSelectionModal"));

        Assert.Equal(HorizontalAlignment.Stretch, modal.HorizontalAlignment);
        Assert.Equal(1180, modal.MaxWidth);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void SeatTemplate_MapsAvailabilityToATypeSafeForegroundStyle(bool isOccupied)
    {
        var window = new MainWindow();
        var seat = new SeatItemViewModel("seat-1", "1", isOccupied);
        var template = Assert.Single(window.DataTemplates, candidate => candidate.Match(seat));
        var seatControl = Assert.IsAssignableFrom<Control>(template.Build(seat));
        seatControl.DataContext = seat;
        window.Content = seatControl;

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var seatName = Assert.Single(
                seatControl.GetLogicalDescendants().OfType<TextBlock>(),
                candidate => candidate.Name == "SeatNameTextBlock");
            Assert.Equal(isOccupied, seatName.Classes.Contains("unavailable"));
            Assert.IsAssignableFrom<IBrush>(seatName.Foreground);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void InWindowDialogs_ExposeAttentionAnimationTargets()
    {
        var window = new MainWindow();
        string[] targetNames =
        [
            "LanCookieRelayDialogModal",
            "GrabSeatSelectionModal",
            "MobileControlDetailsModal",
            "GlobalLeakLibraryPickerModal",
            "TomorrowSeatSelectionModal",
            "VenuePickerModal"
        ];

        foreach (var targetName in targetNames)
        {
            Assert.IsType<Border>(window.FindControl<Border>(targetName));
        }
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(0.5, 1.014)]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    public void ModalAttentionScale_UsesOneSubtlePulse(double progress, double expected)
    {
        Assert.Equal(expected, MainWindow.CalculateModalAttentionScale(progress), precision: 6);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.125, 3.5)]
    [InlineData(0.375, -2.5)]
    [InlineData(1, 0)]
    public void ModalAttentionOffset_UsesDampedHorizontalFeedback(double progress, double expected)
    {
        Assert.Equal(expected, MainWindow.CalculateModalAttentionOffset(progress), precision: 6);
    }

    [AvaloniaFact]
    public void GlobalLeakLibraryPicker_UsesTwoColumnPriorityLayout()
    {
        var window = new MainWindow();
        var modal = Assert.IsType<Border>(window.FindControl<Border>("GlobalLeakLibraryPickerModal"));
        var columns = Assert.IsType<Grid>(window.FindControl<Grid>("GlobalLeakLibraryPickerColumns"));
        Assert.IsType<Grid>(window.FindControl<Grid>("GlobalLeakLibraryPickerContent"));
        var priorityPanel = Assert.IsType<Border>(window.FindControl<Border>("GlobalLeakPriorityPanel"));
        var priorityScrollViewer = Assert.IsType<ScrollViewer>(
            window.FindControl<ScrollViewer>("GlobalLeakPriorityScrollViewer"));
        var priorityItemsControl = Assert.IsType<AnimatedReorderItemsControl>(
            window.FindControl<AnimatedReorderItemsControl>("GlobalLeakPriorityItemsControl"));
        Assert.IsType<Grid>(window.FindControl<Grid>("GlobalLeakLibraryPickerActions"));
        Assert.IsType<Button>(window.FindControl<Button>("GlobalLeakLibraryPickerCloseButton"));
        Assert.IsType<Button>(window.FindControl<Button>("GlobalLeakLibraryPickerConfirmButton"));
        var dragOverlay = Assert.IsType<Canvas>(
            window.FindControl<Canvas>("GlobalLeakPriorityDragOverlay"));
        var dragGhost = Assert.IsType<Border>(window.FindControl<Border>("GlobalLeakPriorityDragGhost"));
        var emptyState = Assert.IsType<Border>(window.FindControl<Border>("GlobalLeakPriorityEmptyState"));

        Assert.Equal(HorizontalAlignment.Stretch, modal.HorizontalAlignment);
        Assert.Equal(1180, modal.MaxWidth);
        Assert.Equal(2, columns.ColumnDefinitions.Count);
        Assert.Equal(columns.ColumnDefinitions[0].Width, columns.ColumnDefinitions[1].Width);
        Assert.Equal(1, Grid.GetColumn(priorityPanel));
        Assert.True(priorityPanel.IsLogicalAncestorOf(priorityScrollViewer));
        Assert.True(DragDrop.GetAllowDrop(priorityScrollViewer));
        Assert.Equal(TimeSpan.FromMilliseconds(220), priorityItemsControl.ReorderAnimationDuration);
        Assert.Equal(4, Grid.GetRowSpan(dragOverlay));
        Assert.False(dragOverlay.IsHitTestVisible);
        Assert.Equal(0, Canvas.GetLeft(dragGhost));
        Assert.Equal(0, Canvas.GetTop(dragGhost));
        Assert.False(dragGhost.IsHitTestVisible);
        Assert.False(dragGhost.IsVisible);
        Assert.Equal(0.72, dragGhost.Opacity);
        Assert.Equal(Colors.Transparent, Assert.IsAssignableFrom<ISolidColorBrush>(emptyState.Background).Color);
        Assert.DoesNotContain(
            window.GetLogicalDescendants().OfType<TextBlock>(),
            textBlock => textBlock.Text == "勾选范围与原有逻辑一致");
    }

    [Fact]
    public void GlobalLeakPriorityDragHelpers_ApplyThresholdAndRowMidpoint()
    {
        Assert.False(MainWindow.HasExceededGlobalLeakDragThreshold(new Point(10, 10), new Point(15.9, 10)));
        Assert.True(MainWindow.HasExceededGlobalLeakDragThreshold(new Point(10, 10), new Point(16, 10)));
        Assert.False(MainWindow.ShouldInsertGlobalLeakPriorityAfter(19.9, 40));
        Assert.True(MainWindow.ShouldInsertGlobalLeakPriorityAfter(20, 40));
        Assert.False(MainWindow.ShouldInsertGlobalLeakPriorityAfter(20, 0));
        Assert.Equal(-1, MainWindow.GetGlobalLeakPriorityAutoScrollDirection(20, 300));
        Assert.Equal(0, MainWindow.GetGlobalLeakPriorityAutoScrollDirection(150, 300));
        Assert.Equal(1, MainWindow.GetGlobalLeakPriorityAutoScrollDirection(280, 300));
        Assert.Equal(0, MainWindow.GetGlobalLeakPriorityAutoScrollDirection(20, 0));
        Assert.Equal(0, MainWindow.CalculateGlobalLeakPriorityAutoScrollOffset(5, 100, -1));
        Assert.Equal(100, MainWindow.CalculateGlobalLeakPriorityAutoScrollOffset(95, 100, 1));
        Assert.Equal(
            new Point(36, 36),
            MainWindow.CalculateGlobalLeakPriorityDragGhostPosition(
                new Point(20, 20),
                new Size(500, 300),
                new Size(100, 60)));
        Assert.Equal(
            new Point(400, 240),
            MainWindow.CalculateGlobalLeakPriorityDragGhostPosition(
                new Point(490, 290),
                new Size(500, 300),
                new Size(100, 60)));
    }

    [Theory]
    [InlineData(120, 40, 80)]
    [InlineData(40, 120, -80)]
    [InlineData(80, 80, 0)]
    public void AnimatedReorderItemsControl_CalculatesFlipTranslation(
        double previousPosition,
        double currentPosition,
        double expectedOffset)
    {
        Assert.Equal(
            expectedOffset,
            AnimatedReorderItemsControl.CalculateTranslationOffset(previousPosition, currentPosition));
    }

    [Theory]
    [InlineData(80, 0, 80)]
    [InlineData(80, 0.5, 10)]
    [InlineData(-80, 0.5, -10)]
    [InlineData(80, 1, 0)]
    [InlineData(80, 2, 0)]
    public void AnimatedReorderItemsControl_UsesCubicEaseOut(
        double startingOffset,
        double progress,
        double expectedOffset)
    {
        Assert.Equal(
            expectedOffset,
            AnimatedReorderItemsControl.CalculateAnimatedOffset(startingOffset, progress),
            precision: 6);
    }

    [AvaloniaFact]
    public void AnimatedReorderItemsControl_AppliesTranslationAfterCollectionMove()
    {
        var items = new System.Collections.ObjectModel.ObservableCollection<string>
        {
            "first",
            "second",
            "third"
        };
        var itemsControl = new AnimatedReorderItemsControl
        {
            ItemsSource = items,
            ItemTemplate = new FuncDataTemplate<string>((_, _) => new Border { Height = 40 }),
            ReorderAnimationDuration = TimeSpan.FromSeconds(10)
        };
        var window = new Window
        {
            Width = 240,
            Height = 240,
            Content = itemsControl
        };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.Measure(new Size(240, 240));
            window.Arrange(new Rect(0, 0, 240, 240));

            items.Move(0, 2);
            window.Measure(new Size(240, 240));
            window.Arrange(new Rect(0, 0, 240, 240));
            Dispatcher.UIThread.RunJobs();

            var firstContainer = Assert.IsAssignableFrom<Control>(itemsControl.ContainerFromIndex(2));
            var transform = Assert.IsType<TranslateTransform>(firstContainer.RenderTransform);
            Assert.True(Math.Abs(transform.Y) > 0.5, "Moved card should still be sliding from its previous position.");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void AnimatedReorderItemsControl_CancelsExistingAnimation_WhenReverseMoveCancelsOffset()
    {
        var items = new System.Collections.ObjectModel.ObservableCollection<string>
        {
            "first",
            "second",
            "third"
        };
        var itemsControl = new AnimatedReorderItemsControl
        {
            ItemsSource = items,
            ItemTemplate = new FuncDataTemplate<string>((_, _) => new Border { Height = 40 }),
            ReorderAnimationDuration = TimeSpan.FromSeconds(30)
        };
        var window = new Window
        {
            Width = 240,
            Height = 240,
            Content = itemsControl
        };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.Measure(new Size(240, 240));
            window.Arrange(new Rect(0, 0, 240, 240));

            items.Move(0, 2);
            window.Measure(new Size(240, 240));
            window.Arrange(new Rect(0, 0, 240, 240));
            Dispatcher.UIThread.RunJobs();
            var movedContainer = Assert.IsAssignableFrom<Control>(itemsControl.ContainerFromIndex(2));
            Assert.True(Math.Abs(Assert.IsType<TranslateTransform>(movedContainer.RenderTransform).Y) > 0.5);

            items.Move(2, 0);
            window.Measure(new Size(240, 240));
            window.Arrange(new Rect(0, 0, 240, 240));
            Dispatcher.UIThread.RunJobs();

            var restoredContainer = Assert.IsAssignableFrom<Control>(itemsControl.ContainerFromIndex(0));
            Assert.True(
                restoredContainer.RenderTransform is null or TranslateTransform { Y: 0 },
                "Reverse move should leave no residual translation on the restored card.");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void GeneralSettings_ExposeOptimalGrabStrategyReminderToggle()
    {
        var window = new MainWindow();

        var toggle = window.FindControl<ToggleSwitch>("OptimalGrabStrategyReminderToggle");

        Assert.NotNull(toggle);
    }

    [AvaloniaFact]
    public void LanCookieRelayDialog_StatusWrapsBelowHeaderAndCannotOverlapCloseButton()
    {
        var window = new MainWindow
        {
            Width = 1188,
            Height = 840
        };
        var overlay = Assert.IsType<Border>(window.FindControl<Border>("LanCookieRelayDialogOverlay"));
        var header = Assert.IsType<Grid>(window.FindControl<Grid>("LanCookieRelayDialogHeader"));
        var statusPanel = Assert.IsType<Grid>(window.FindControl<Grid>("LanCookieRelayStatusPanel"));
        var statusText = Assert.IsType<TextBlock>(window.FindControl<TextBlock>("LanCookieRelayStatusTextBlock"));
        var closeButton = Assert.IsType<Button>(window.FindControl<Button>("LanCookieRelayCloseButton"));

        Assert.Equal(2, header.RowDefinitions.Count);
        Assert.Equal(0, Grid.GetRow(closeButton));
        Assert.Equal(1, Grid.GetColumn(closeButton));
        Assert.Equal(1, Grid.GetRow(statusPanel));
        Assert.Equal(2, Grid.GetColumnSpan(statusPanel));
        Assert.Equal(TextWrapping.Wrap, statusText.TextWrapping);
        Assert.Equal(HorizontalAlignment.Stretch, statusText.HorizontalAlignment);

        overlay.IsVisible = true;
        statusText.Text =
            "快传启动失败：检测到系统已开启代理，但Clash/Mihomo 路由策略为 DIRECT，明显冲突" +
            "请填写正确的路由策略或把代理方式切换为不使用显式代理";
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.True(statusText.Bounds.Height > 20, "Long status text should occupy multiple lines.");
            Assert.True(
                statusPanel.Bounds.Top >= closeButton.Bounds.Bottom,
                "Status row must be laid out below the close-button row.");
            Assert.True(
                statusText.Bounds.Right <= statusPanel.Bounds.Width + 0.5,
                "Wrapped status text must stay inside the status panel.");
        }
        finally
        {
            window.Close();
        }
    }
}
