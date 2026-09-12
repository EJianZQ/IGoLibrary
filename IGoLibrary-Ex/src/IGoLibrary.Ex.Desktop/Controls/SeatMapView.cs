using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Desktop.ViewModels;

namespace IGoLibrary.Ex.Desktop.Controls;

/// <summary>地图渲染与指针视口，不执行业务选择或网络请求。</summary>
public sealed class SeatMapView : UserControl
{
    private readonly Canvas _canvas = new() { Background = Brushes.Transparent };
    private readonly Canvas _surface = new()
    {
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top
    };
    private readonly ScrollViewer _scroll = new()
    {
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
    };
    private SeatWorkspaceViewModel? _workspace;
    private int? _libraryId;
    private SeatLayoutProjection? _projection;
    private bool _needsFit = true;
    private bool _space;
    private Point? _pressed;
    private Vector _dragOffset;
    private IPointer? _pointer;
    private bool _dragging;
    private int _viewportVersion;
    public double Zoom { get; private set; } = 1;
    public Vector Offset => _scroll.Offset;
    public int ElementCount => _canvas.Children.Count;
    public event EventHandler? ZoomChanged;

    public SeatMapView()
    {
        _canvas.RenderTransformOrigin = RelativePoint.TopLeft;
        _surface.Children.Add(_canvas);
        _scroll.Content = _surface;
        Content = _scroll;
        ClipToBounds = true;
        Focusable = true;
        AddHandler(PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnMoved, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnReleased, RoutingStrategies.Tunnel);
        AddHandler(PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnMapKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnMapKeyUp, RoutingStrategies.Tunnel);
        PointerCaptureLost += (_, _) => EndDrag();
        LostFocus += (_, _) => _space = false;
        DetachedFromVisualTree += (_, _) => { _space = false; EndDrag(); ++_viewportVersion; };
        _scroll.SizeChanged += (_, _) => { if (_needsFit && _scroll.Viewport.Width > 0) Fit(false); };
    }

    public void SetWorkspace(SeatWorkspaceViewModel? workspace)
    {
        _workspace = workspace;
        var projection = workspace?.Projection;
        if (ReferenceEquals(projection, _projection)) return;
        ++_viewportVersion;
        var oldOffset = _scroll.Offset;
        var changedVenue = _libraryId != workspace?.LibraryId;
        var reuse = !changedVenue && projection is not null && _projection is not null &&
            projection.Items.Count == _canvas.Children.Count && projection.Items.Count == _projection.Items.Count &&
            projection.Items.Zip(_projection.Items).All(pair =>
                pair.First.Left == pair.Second.Left && pair.First.Top == pair.Second.Top &&
                pair.First.SeatIndex == pair.Second.SeatIndex && pair.First.Item.Type == pair.Second.Item.Type &&
                pair.First.Item.Key == pair.Second.Item.Key &&
                (pair.First.SeatIndex is not null || pair.First.Item.Name == pair.Second.Item.Name));
        _libraryId = workspace?.LibraryId;
        _projection = projection;
        if (!reuse) _canvas.Children.Clear();
        _canvas.Width = projection?.Width ?? 0;
        _canvas.Height = projection?.Height ?? 0;
        _surface.Width = _canvas.Width * Zoom;
        _surface.Height = _canvas.Height * Zoom;
        if (workspace is not null && projection is { CanShowMap: true })
        {
            for (var i = 0; i < projection.Items.Count; i++)
            {
                var element = projection.Items[i];
                if (reuse)
                {
                    if (element.SeatIndex is { } existingIndex) _canvas.Children[i].DataContext = workspace.Seats[existingIndex];
                    continue;
                }
                Control tile = element.SeatIndex is { } index
                    ? new SeatTile { DataContext = workspace.Seats[index], Name = $"MapSeat_{index}" }
                    : SeatLayoutGraphics.Create(element.Item);
                if (tile is SeatTile seatTile) seatTile.SetMapZoom(Zoom);
                Canvas.SetLeft(tile, element.Left + 3);
                Canvas.SetTop(tile, element.Top + 3);
                _canvas.Children.Add(tile);
            }
        }
        _needsFit = changedVenue || _needsFit;
        QueueViewport(() => { if (_needsFit) Fit(false); else SetOffset(oldOffset); });
    }
    public void Fit(bool wholeMap)
    {
        if (_projection is not { CanShowMap: true } || _scroll.Viewport.Width <= 0) { _needsFit = true; return; }
        _needsFit = false;
        ApplyZoom(SeatMapViewport.Fit(ContentSize, _scroll.Viewport, wholeMap), new Vector());
    }
    public void ZoomBy(double factor) => SetZoom(Zoom * factor);
    public void SetZoom(double zoom, Point? anchor = null)
    {
        if (!double.IsFinite(zoom) || zoom <= 0) return;
        _needsFit = false;
        zoom = SeatMapViewport.ClampZoom(zoom, ContentSize, _scroll.Viewport, Zoom);
        if (zoom == Zoom) return;
        var point = anchor ?? new Point(_scroll.Viewport.Width / 2, _scroll.Viewport.Height / 2);
        ApplyZoom(zoom, SeatMapViewport.ZoomAt(_scroll.Offset, point, Zoom, zoom));
    }
    public void Locate()
    {
        if (_workspace?.LocatedSeat is not { } seat || _projection is null) return;
        var index = _workspace.Seats.IndexOf(seat);
        var target = _projection.Items.FirstOrDefault(item => item.SeatIndex == index);
        if (target is null) return;
        _needsFit = false;
        var zoom = Math.Max(1, Zoom);
        ApplyZoom(zoom, SeatMapViewport.Center(new Point(target.Left + 29, target.Top + 29), _scroll.Viewport, zoom));
    }
    private Size ContentSize => new(_canvas.Width, _canvas.Height);
    private void ApplyZoom(double zoom, Vector offset)
    {
        Zoom = zoom;
        _canvas.RenderTransform = new ScaleTransform(zoom, zoom);
        foreach (var tile in _canvas.Children.OfType<SeatTile>()) tile.SetMapZoom(zoom);
        _surface.Width = _canvas.Width * zoom;
        _surface.Height = _canvas.Height * zoom;
        ZoomChanged?.Invoke(this, EventArgs.Empty);
        QueueViewport(() => SetOffset(offset));
    }
    private void SetOffset(Vector offset) => _scroll.Offset = SeatMapViewport.Clamp(offset, ContentSize, _scroll.Viewport, Zoom);
    private void QueueViewport(Action action)
    {
        var version = ++_viewportVersion;
        Dispatcher.UIThread.Post(() => { if (version == _viewportVersion) action(); }, DispatcherPriority.Loaded);
    }
    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) == 0) return;
        SetZoom(Zoom * Math.Pow(1.15, e.Delta.Y), e.GetPosition(_scroll));
        e.Handled = true;
    }
    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        var properties = e.GetCurrentPoint(this).Properties;
        var source = e.Source as Visual;
        var overSeat = source is SeatTile || source?.GetVisualAncestors().OfType<SeatTile>().Any() == true;
        var overScrollbar = source?.GetVisualAncestors().OfType<Avalonia.Controls.Primitives.ScrollBar>().Any() == true;
        if (overScrollbar || !(properties.IsMiddleButtonPressed || properties.IsLeftButtonPressed && (_space || !overSeat))) return;
        _pressed = e.GetPosition(_scroll);
        _dragOffset = _scroll.Offset;
        _pointer = e.Pointer;
        e.Pointer.Capture(this);
        Focus();
        e.Handled = true;
    }
    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (_pressed is not { } pressed) return;
        var position = e.GetPosition(_scroll);
        var delta = new Vector(position.X - pressed.X, position.Y - pressed.Y);
        if (!_dragging && delta.Length <= 5) return;
        _dragging = true;
        _needsFit = false;
        SetOffset(_dragOffset - delta);
        e.Handled = true;
    }
    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_pressed is null) return;
        EndDrag();
        e.Handled = true;
    }
    private void EndDrag()
    {
        var pointer = _pointer;
        _pointer = null;
        _pressed = null;
        _dragging = false;
        if (pointer?.Captured == this) pointer.Capture(null);
    }
    private void OnMapKeyDown(object? sender, KeyEventArgs e)
    {
        // A focused seat retains Space-to-toggle keyboard access.
        if (e.Key == Key.Space && (e.Source == this || e.Source == _scroll)) { _space = true; e.Handled = true; }
    }
    private void OnMapKeyUp(object? sender, KeyEventArgs e) { if (e.Key == Key.Space) _space = false; }
}
