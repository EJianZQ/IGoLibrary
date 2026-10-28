using System.ComponentModel;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using IGoLibrary.Ex.Desktop.Services;
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
    private readonly Canvas _listOverlay = new() { IsHitTestVisible = false };
    private readonly Rectangle _listMarqueeShape = new()
    {
        IsVisible = false,
        StrokeThickness = 1.5,
        StrokeDashArray = new AvaloniaList<double> { 4, 3 },
        IsHitTestVisible = false
    };
    private readonly Grid _listHost = new();
    private readonly List<SeatItemViewModel> _listPreviewSeats = [];
    private Point? _listOrigin;
    private bool _listArmed;
    private bool _listActive;
    private IPointer? _listPointer;
    private SeatWorkspaceViewModel? _workspace;
    private bool _attached;

    public SeatWorkspaceView()
    {
        InitializeComponent();
        _listScroll = new ScrollViewer { Content = _list };
        _listHost.Children.Add(_listScroll);
        _listHost.Children.Add(_listOverlay);
        _listHost.ClipToBounds = true;
        _listOverlay.Children.Add(_listMarqueeShape);
        _list.AddHandler(PointerPressedEvent, OnListPressed, RoutingStrategies.Tunnel);
        _list.AddHandler(PointerMovedEvent, OnListMoved, RoutingStrategies.Tunnel);
        _list.AddHandler(PointerReleasedEvent, OnListReleased, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnWorkspaceKeyDown, RoutingStrategies.Tunnel);
        _map.ZoomChanged += (_, _) => ZoomText.Text = $"{_map.Zoom:P0}";
        DataContextChanged += (_, _) => { if (_attached) Connect(); };
        AttachedToVisualTree += (_, _) => { _attached = true; Connect(); };
        DetachedFromVisualTree += (_, _) => { _attached = false; EndListMarquee(); Disconnect(); };
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
        EndListMarquee();
        if (_workspace?.IsMapMode == true)
        {
            DisplayHost.Content = _map;
            _map.SetWorkspace(_workspace);
        }
        else DisplayHost.Content = _listHost;
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

    /// <summary>框选进行中；供无头测试与诊断读取。</summary>
    public bool IsListMarqueeActive => _listActive;

    /// <summary>框选预览座位数；松手前始终为临时状态。</summary>
    public int ListMarqueePreviewCount => _listPreviewSeats.Count;

    private void OnWorkspaceKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || !_listActive) return;
        EndListMarquee();
        e.Handled = true;
    }

    private void OnListPressed(object? sender, PointerPressedEventArgs e)
    {
        var properties = e.GetCurrentPoint(_list).Properties;
        if (!properties.IsLeftButtonPressed) return;
        var source = e.Source as Visual;
        if (source?.GetVisualAncestors().OfType<ScrollBar>().Any() == true) return;
        EndListMarquee();
        _listOrigin = e.GetPosition(_list);
        _listPointer = e.Pointer;
        _listArmed = true;
    }

    private void OnListMoved(object? sender, PointerEventArgs e)
    {
        if (!_listArmed || _listOrigin is not { } origin) return;
        if (!e.GetCurrentPoint(_list).Properties.IsLeftButtonPressed) return;
        var current = e.GetPosition(_list);
        if (!_listActive && !SeatMarqueeGeometry.HasReachedThreshold(origin, current)) return;
        if (!_listActive) StartListMarquee(e.Pointer);
        UpdateListMarquee(origin, current);
        e.Handled = true;
    }

    private void OnListReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_listActive)
        {
            CommitListMarquee();
            EndListMarquee();
            e.Handled = true;
            return;
        }

        // 未越过阈值：交还给座位自身的单击切换。
        if (_listArmed) EndListMarquee();
    }

    private void StartListMarquee(IPointer pointer)
    {
        _listActive = true;
        _listPointer = pointer;
        // 捕获到列表本身，松手事件才会沿 _list 的路由冒泡到本视图的隧道处理器。
        pointer.Capture(_list);
        var accent = ResolveAccentColor();
        _listMarqueeShape.Stroke = new SolidColorBrush(accent);
        _listMarqueeShape.Fill = new SolidColorBrush(accent, 0.14);
        _listMarqueeShape.IsVisible = true;
    }

    private static Color ResolveAccentColor()
    {
        var app = Avalonia.Application.Current;
        if (app?.TryGetResource("SemiAccentBrush", app.ActualThemeVariant, out var value) == true &&
            value is ISolidColorBrush brush)
        {
            return brush.Color;
        }

        return Color.Parse("#1296DB");
    }

    private void UpdateListMarquee(Point origin, Point current)
    {
        var rect = SeatMarqueeGeometry.Normalize(origin, current);
        var offset = _listScroll.Offset;
        Canvas.SetLeft(_listMarqueeShape, rect.X - offset.X);
        Canvas.SetTop(_listMarqueeShape, rect.Y - offset.Y);
        _listMarqueeShape.Width = rect.Width;
        _listMarqueeShape.Height = rect.Height;
        UpdateListPreview(rect);
    }

    private void UpdateListPreview(Rect marquee)
    {
        foreach (var seat in _listPreviewSeats) seat.IsMarqueePreview = false;
        _listPreviewSeats.Clear();
        foreach (var tile in _list.GetVisualDescendants().OfType<SeatTile>())
        {
            if (tile.DataContext is not SeatItemViewModel seat || !seat.IsFilterVisible) continue;
            if (tile.TranslatePoint(new Point(0, 0), _list) is not { } position) continue;
            var size = tile.Bounds.Size;
            if (size.Width <= 0 || size.Height <= 0) continue;
            if (!SeatMarqueeGeometry.Intersects(marquee, new Rect(position, size))) continue;
            seat.IsMarqueePreview = true;
            _listPreviewSeats.Add(seat);
        }
    }

    /// <summary>框选只做并集：已选座位保持不变，不会因为框选被取消。</summary>
    private void CommitListMarquee()
    {
        foreach (var seat in _listPreviewSeats)
        {
            seat.IsMarqueePreview = false;
            if (seat.IsFilterVisible) seat.IsSelected = true;
        }

        _listPreviewSeats.Clear();
    }

    private void EndListMarquee()
    {
        foreach (var seat in _listPreviewSeats) seat.IsMarqueePreview = false;
        _listPreviewSeats.Clear();
        _listMarqueeShape.IsVisible = false;
        if (_listPointer is { } pointer && pointer.Captured == _list) pointer.Capture(null);
        _listPointer = null;
        _listActive = false;
        _listArmed = false;
        _listOrigin = null;
    }
}
