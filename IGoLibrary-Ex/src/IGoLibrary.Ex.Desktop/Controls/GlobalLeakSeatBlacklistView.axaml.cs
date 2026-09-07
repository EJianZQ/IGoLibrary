using Avalonia.Controls;
using Avalonia.Threading;
using System.ComponentModel;
using IGoLibrary.Ex.Desktop.ViewModels;

namespace IGoLibrary.Ex.Desktop.Controls;

public partial class GlobalLeakSeatBlacklistView : UserControl
{
    private GlobalLeakSeatBlacklistEditorViewModel? _viewModel;
    private bool _attached;

    public GlobalLeakSeatBlacklistView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => { _attached = true; Subscribe(); };
        DetachedFromVisualTree += (_, _) => { _attached = false; Unsubscribe(); };
        DataContextChanged += (_, _) => Subscribe();
    }

    private void Subscribe()
    {
        Unsubscribe();
        if (!_attached || DataContext is not GlobalLeakSeatBlacklistEditorViewModel viewModel) return;
        _viewModel = viewModel;
        viewModel.PropertyChanged += OnEditorPropertyChanged;
        if (viewModel.IsOpen) UpdateFocus(viewModel, true);
    }

    private void Unsubscribe()
    {
        if (_viewModel is not null) _viewModel.PropertyChanged -= OnEditorPropertyChanged;
        _viewModel = null;
    }

    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GlobalLeakSeatBlacklistEditorViewModel.IsOpen) && _viewModel is { } viewModel)
            UpdateFocus(viewModel, viewModel.IsOpen);
    }

    private void UpdateFocus(GlobalLeakSeatBlacklistEditorViewModel viewModel, bool isOpen)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(_viewModel, viewModel) || viewModel.IsOpen != isOpen) return;
            if (isOpen) CloseButton.Focus();
            else if (TopLevel.GetTopLevel(this) is MainWindow window)
                window.FindControl<Button>("ManageGlobalLeakBlacklistButton")?.Focus();
        });
    }
}
