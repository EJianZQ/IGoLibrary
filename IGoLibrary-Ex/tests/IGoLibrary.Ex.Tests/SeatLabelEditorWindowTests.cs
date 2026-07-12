using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using IGoLibrary.Ex.Desktop;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Desktop.ViewModels;

namespace IGoLibrary.Ex.Tests;

public sealed class SeatLabelEditorWindowTests
{
    [AvaloniaFact]
    public void Window_DisablesConfirmationUntilInputIsValid()
    {
        var viewModel = new SeatLabelEditorViewModel(new SeatLabelDialogRequest("设置标签", "说明"));
        var window = new SeatLabelEditorWindow(viewModel);
        var input = Assert.IsType<TextBox>(window.FindControl<TextBox>("LabelTextBox"));
        var confirm = Assert.IsType<Button>(window.FindControl<Button>("ConfirmButton"));
        var validation = Assert.IsType<TextBlock>(window.FindControl<TextBlock>("ValidationText"));

        Assert.Equal(32, input.MaxLength);
        Assert.False(confirm.IsEnabled);
        Assert.NotEmpty(validation.Text ?? string.Empty);

        viewModel.LabelText = "  靠窗  ";

        Assert.True(confirm.IsEnabled);
        Assert.Equal(string.Empty, validation.Text);
        Assert.Equal("靠窗", viewModel.GetNormalizedText());
    }

    [AvaloniaFact]
    public async Task EnterConfirmsAndEscapeCancelsDialog()
    {
        var owner = new Window();
        owner.Show();
        try
        {
            var confirmedViewModel = new SeatLabelEditorViewModel(
                new SeatLabelDialogRequest("设置标签", "说明", "  常用  "));
            var confirmedWindow = new SeatLabelEditorWindow(confirmedViewModel);
            var confirmedTask = confirmedWindow.ShowDialog<string?>(owner);
            Dispatcher.UIThread.RunJobs();
            confirmedWindow.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Enter
            });
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("常用", await confirmedTask);

            var cancelledWindow = new SeatLabelEditorWindow(new SeatLabelEditorViewModel(
                new SeatLabelDialogRequest("设置标签", "说明", "常用")));
            var cancelledTask = cancelledWindow.ShowDialog<string?>(owner);
            Dispatcher.UIThread.RunJobs();
            cancelledWindow.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Escape
            });
            Dispatcher.UIThread.RunJobs();
            Assert.Null(await cancelledTask);
        }
        finally
        {
            owner.Close();
        }
    }
}
