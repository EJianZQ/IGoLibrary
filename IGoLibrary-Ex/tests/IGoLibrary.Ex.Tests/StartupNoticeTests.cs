using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using IGoLibrary.Ex.Desktop.Startup;

namespace IGoLibrary.Ex.Tests;

public sealed class StartupNoticeTests
{
    [Fact]
    public void DuplicateInstance_UsesRequiredMessage()
    {
        var notice = StartupNotice.DuplicateInstance;

        Assert.Equal("提示", notice.Title);
        Assert.Equal("已有一个正在运行的程序，请不要多开", notice.Message);
    }

    [Fact]
    public void CreateStartupFailure_UsesDistinctFailureMessage()
    {
        var notice = StartupNotice.CreateStartupFailure(new IOException("mutex failed"));

        Assert.Equal("启动失败", notice.Title);
        Assert.Contains("无法确认程序是否可以安全启动", notice.Message, StringComparison.Ordinal);
        Assert.Contains("mutex failed", notice.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(StartupNotice.DuplicateInstance.Message, notice.Message, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void DuplicateInstanceWindow_LoadsExactNotice_AndConfirmClosesWindow()
    {
        var app = Assert.IsType<StartupNoticeApp>(Avalonia.Application.Current);
        var icons = TrayIcon.GetIcons(app);
        Assert.True(icons is null || icons.Count == 0);

        var window = new StartupNoticeWindow(StartupNotice.DuplicateInstance);
        var closed = false;
        window.Closed += (_, _) => closed = true;
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.IsNotType<IGoLibrary.Ex.Desktop.MainWindow>(window);
            Assert.True(window.ShowInTaskbar);
            Assert.Equal(WindowStartupLocation.CenterScreen, window.WindowStartupLocation);
            Assert.Equal("提示", window.Title);
            Assert.Equal(
                "提示",
                Assert.IsType<TextBlock>(window.FindControl<TextBlock>("TitleText")).Text);
            Assert.Equal(
                "已有一个正在运行的程序，请不要多开",
                Assert.IsType<TextBlock>(window.FindControl<TextBlock>("MessageText")).Text);

            var confirmButton = Assert.IsType<Button>(window.FindControl<Button>("ConfirmButton"));
            confirmButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.True(closed);
            Assert.False(window.IsVisible);
        }
        finally
        {
            if (window.IsVisible)
            {
                window.Close();
            }
        }
    }

    [AvaloniaFact]
    public void DuplicateInstanceWindow_SystemCloseEndsWindowLifecycle()
    {
        var window = new StartupNoticeWindow(StartupNotice.DuplicateInstance);
        var closed = false;
        window.Closed += (_, _) => closed = true;

        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.True(closed);
        Assert.False(window.IsVisible);
    }
}
