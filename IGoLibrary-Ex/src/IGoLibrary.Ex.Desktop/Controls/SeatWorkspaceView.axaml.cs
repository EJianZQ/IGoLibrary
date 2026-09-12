using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using IGoLibrary.Ex.Desktop.ViewModels;

namespace IGoLibrary.Ex.Desktop.Controls;

public partial class SeatWorkspaceView : UserControl
{
    public static readonly StyledProperty<bool> ShowLegendProperty =
        AvaloniaProperty.Register<SeatWorkspaceView, bool>(nameof(ShowLegend), true);

    public bool ShowLegend
    {
        get => GetValue(ShowLegendProperty);
        set => SetValue(ShowLegendProperty, value);
    }

    private readonly SeatMapView _map = new();
    private readonly ItemsControl _list = new()
    {
        ItemsPanel = new FuncTemplate<Panel?>(() => new WrapPanel()),
        ItemTemplate = new FuncDataTemplate<SeatItemViewModel>((seat, _) =>
        {
            var tile = new SeatTile { DataContext = seat, Margin = new Avalonia.Thickness(3) };
            tile.Bind(IsVisibleProperty, new Binding(nameof(SeatItemViewModel.IsFilterVisible)));
            return tile;
        })
    };
    private readonly ScrollViewer _listScroll;
    private SeatWorkspaceViewModel? _workspace;
    private bool _attached;

    public SeatWorkspaceView()
    {
        InitializeComponent();
        _listScroll = new ScrollViewer { Content = _list };
        _map.ZoomChanged += (_, _) => ZoomText.Text = $"{_map.Zoom:P0}";
        DataContextChanged += (_, _) => { if (_attached) Connect(); };
        AttachedToVisualTree += (_, _) => { _attached = true; Connect(); };
        DetachedFromVisualTree += (_, _) => { _attached = false; Disconnect(); };
    }
    private void Connect()
    {
        Disconnect();
        _workspace = DataContext as SeatWorkspaceViewModel;
        if (_workspace is not null)
        {
            _workspace.PropertyChanged += OnWorkspaceChanged;
            _workspace.LocationRequested += OnLocationRequested;
        }
        _list.ItemsSource = _workspace?.Seats;
        RefreshView();
    }
    private void Disconnect()
    {
        if (_workspace is null) return;
        _workspace.PropertyChanged -= OnWorkspaceChanged;
        _workspace.LocationRequested -= OnLocationRequested;
        _workspace = null;
    }
    private void OnWorkspaceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SeatWorkspaceViewModel.Projection) or nameof(SeatWorkspaceViewModel.IsMapMode)) RefreshView();
    }
    private void RefreshView()
    {
        if (_workspace?.IsMapMode == true)
        {
            DisplayHost.Content = _map;
            _map.SetWorkspace(_workspace);
        }
        else DisplayHost.Content = _listScroll;
    }
    private void OnLocationRequested(object? sender, EventArgs e)
    {
        if (_workspace?.IsMapMode == true) _map.Locate();
        else
        {
            var seat = _workspace?.LocatedSeat;
            Dispatcher.UIThread.Post(() =>
            {
                if (_workspace?.LocatedSeat != seat) return;
                _list.GetLogicalDescendants().OfType<SeatTile>().FirstOrDefault(tile => ReferenceEquals(tile.DataContext, seat))?.BringIntoView();
            });
        }
    }
    private void OnZoomOut(object? sender, RoutedEventArgs e) => _map.ZoomBy(1 / 1.2);
    private void OnZoomIn(object? sender, RoutedEventArgs e) => _map.ZoomBy(1.2);
    private void OnActualSize(object? sender, RoutedEventArgs e) => _map.SetZoom(1);
    private void OnFitWidth(object? sender, RoutedEventArgs e) => _map.Fit(false);
    private void OnFitAll(object? sender, RoutedEventArgs e) => _map.Fit(true);
    private void OnLocateKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        _workspace?.LocateCommand.Execute(null);
        e.Handled = true;
    }
}
