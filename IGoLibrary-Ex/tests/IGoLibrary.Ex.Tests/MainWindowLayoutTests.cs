using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using IGoLibrary.Ex.Desktop;

namespace IGoLibrary.Ex.Tests;

public sealed class MainWindowLayoutTests
{
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
