using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using IGoLibrary.Ex.Desktop.ViewModels;

namespace IGoLibrary.Ex.Desktop;

internal sealed partial class SeatLabelEditorWindow : Window
{
    private readonly SeatLabelEditorViewModel _viewModel;

    public SeatLabelEditorWindow(SeatLabelEditorViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        Opened += (_, _) => LabelTextBox.Focus();
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e) => Confirm();

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && _viewModel.CanConfirm)
        {
            Confirm();
            e.Handled = true;
        }
    }

    private void Confirm()
    {
        if (!_viewModel.CanConfirm)
        {
            return;
        }

        Close(_viewModel.GetNormalizedText());
    }
}
