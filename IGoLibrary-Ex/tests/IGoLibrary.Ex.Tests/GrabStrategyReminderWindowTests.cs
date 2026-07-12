using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using IGoLibrary.Ex.Desktop;
using IGoLibrary.Ex.Desktop.Services;

namespace IGoLibrary.Ex.Tests;

public sealed class GrabStrategyReminderWindowTests
{
    [Fact]
    public async Task DialogServiceCancelsWhenMainWindowIsUnavailable()
    {
        var service = new GrabStrategyReminderDialogService(new AppWindowService());

        var result = await service.ShowAsync();

        Assert.Equal(GrabStrategyReminderDecision.Cancel, result.Decision);
        Assert.False(result.DisableReminder);
    }

    [AvaloniaFact]
    public async Task ButtonsReturnChoiceTogetherWithCheckboxState()
    {
        var owner = new Window();
        owner.Show();
        try
        {
            var switchWindow = new GrabStrategyReminderWindow();
            var switchTask = switchWindow.ShowDialog<GrabStrategyReminderResult?>(owner);
            Dispatcher.UIThread.RunJobs();
            switchWindow.SwitchToOptimalButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var switchResult = Assert.IsType<GrabStrategyReminderResult>(await switchTask);
            Assert.Equal(GrabStrategyReminderDecision.SwitchToOptimal, switchResult.Decision);
            Assert.False(switchResult.DisableReminder);

            var keepWindow = new GrabStrategyReminderWindow();
            var keepTask = keepWindow.ShowDialog<GrabStrategyReminderResult?>(owner);
            Dispatcher.UIThread.RunJobs();
            keepWindow.DisableReminderCheckBox.IsChecked = true;
            keepWindow.KeepCurrentButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var keepResult = Assert.IsType<GrabStrategyReminderResult>(await keepTask);
            Assert.Equal(GrabStrategyReminderDecision.KeepCurrent, keepResult.Decision);
            Assert.True(keepResult.DisableReminder);

            var switchAndDisableWindow = new GrabStrategyReminderWindow();
            var switchAndDisableTask = switchAndDisableWindow.ShowDialog<GrabStrategyReminderResult?>(owner);
            Dispatcher.UIThread.RunJobs();
            switchAndDisableWindow.DisableReminderCheckBox.IsChecked = true;
            switchAndDisableWindow.SwitchToOptimalButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var switchAndDisableResult = Assert.IsType<GrabStrategyReminderResult>(await switchAndDisableTask);
            Assert.Equal(GrabStrategyReminderDecision.SwitchToOptimal, switchAndDisableResult.Decision);
            Assert.True(switchAndDisableResult.DisableReminder);
        }
        finally
        {
            owner.Close();
        }
    }

    [AvaloniaFact]
    public async Task EscapeAndWindowCloseCancelStartingTask()
    {
        var owner = new Window();
        owner.Show();
        try
        {
            var escapeWindow = new GrabStrategyReminderWindow();
            var escapeTask = escapeWindow.ShowDialog<GrabStrategyReminderResult?>(owner);
            Dispatcher.UIThread.RunJobs();
            escapeWindow.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Escape
            });
            Dispatcher.UIThread.RunJobs();
            var escapeResult = Assert.IsType<GrabStrategyReminderResult>(await escapeTask);
            Assert.Equal(GrabStrategyReminderDecision.Cancel, escapeResult.Decision);
            Assert.False(escapeResult.DisableReminder);

            var closeWindow = new GrabStrategyReminderWindow();
            var closeTask = closeWindow.ShowDialog<GrabStrategyReminderResult?>(owner);
            Dispatcher.UIThread.RunJobs();
            closeWindow.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.Null(await closeTask);
        }
        finally
        {
            owner.Close();
        }
    }
}
