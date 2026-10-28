using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Desktop.ViewModels;

namespace IGoLibrary.Ex.Desktop.Controls;

/// <summary>框选拖拽：只维护临时高亮与矩形，松手时才写入选中状态。</summary>
public sealed partial class SeatMapView
{
    private static readonly Color FallbackAccent = Color.Parse("#1296DB");

    private readonly Canvas _marqueeLayer = new() { IsHitTestVisible = false };
    private readonly Rectangle _marqueeShape = new()
    {
        IsVisible = false,
        StrokeThickness = 1.5,
        StrokeDashArray = new AvaloniaList<double> { 4, 3 },
        IsHitTestVisible = false
    };

    private readonly List<SeatItemViewModel> _marqueePreviewSeats = [];
    private Point? _marqueeOrigin;
    private bool _marqueeArmed;
    private bool _marqueeActive;
    private IPointer? _marqueePointer;

    /// <summary>框选进行中；供无头测试与诊断读取。</summary>
    public bool IsMarqueeActive => _marqueeActive;

    /// <summary>框选预览座位数；松手前始终为临时状态。</summary>
    public int MarqueePreviewCount => _marqueePreviewSeats.Count;

    private void InitializeMarquee()
    {
        _marqueeLayer.RenderTransformOrigin = RelativePoint.TopLeft;
        _marqueeLayer.Children.Add(_marqueeShape);
        _surface.Children.Add(_marqueeLayer);
    }

    private void ApplyMarqueeZoom(double zoom)
    {
        _marqueeLayer.Width = _canvas.Width;
        _marqueeLayer.Height = _canvas.Height;
        _marqueeLayer.RenderTransform = new ScaleTransform(zoom, zoom);
    }

    private void ArmMarquee(Point origin, IPointer pointer)
    {
        CancelMarquee();
        // 取得键盘焦点，Esc 取消与空格平移才能在框选期间生效。
        Focus();
        _marqueeOrigin = origin;
        _marqueePointer = pointer;
        _marqueeArmed = true;
    }

    /// <summary>返回 true 表示本次事件已被框选消费，调用方不应再执行平移。</summary>
    private bool TryUpdateMarquee(PointerEventArgs e)
    {
        if (!_marqueeArmed || _marqueeOrigin is not { } origin) return false;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return false;
        var current = e.GetPosition(_canvas);
        if (!_marqueeActive && !SeatMarqueeGeometry.HasReachedThreshold(origin, current)) return true;
        if (!_marqueeActive) StartMarquee(e.Pointer);
        UpdateMarquee(origin, current);
        e.Handled = true;
        return true;
    }

    private void StartMarquee(IPointer pointer)
    {
        _marqueeActive = true;
        _needsFit = false;
        _marqueePointer = pointer;
        pointer.Capture(this);
        EnsureMarqueeBrushes();
        _marqueeShape.IsVisible = true;
    }

    private void EnsureMarqueeBrushes()
    {
        var accent = FallbackAccent;
        var app = Avalonia.Application.Current;
        if (app?.TryGetResource("SemiAccentBrush", app.ActualThemeVariant, out var value) == true &&
            value is ISolidColorBrush brush)
        {
            accent = brush.Color;
        }

        _marqueeShape.Stroke = new SolidColorBrush(accent);
        _marqueeShape.Fill = new SolidColorBrush(accent, 0.14);
    }

    private void UpdateMarquee(Point origin, Point current)
    {
        var rect = SeatMarqueeGeometry.Normalize(origin, current);
        Canvas.SetLeft(_marqueeShape, rect.X);
        Canvas.SetTop(_marqueeShape, rect.Y);
        _marqueeShape.Width = rect.Width;
        _marqueeShape.Height = rect.Height;
        UpdateMarqueePreview(rect);
    }

    private void UpdateMarqueePreview(Rect marquee)
    {
        foreach (var seat in _marqueePreviewSeats) seat.IsMarqueePreview = false;
        _marqueePreviewSeats.Clear();
        foreach (var child in _canvas.Children)
        {
            if (child is not SeatTile tile || tile.DataContext is not SeatItemViewModel seat) continue;
            if (!seat.IsFilterVisible) continue;
            var left = Canvas.GetLeft(tile);
            var top = Canvas.GetTop(tile);
            if (double.IsNaN(left)) left = 0;
            if (double.IsNaN(top)) top = 0;
            var width = tile.Bounds.Width > 0 ? tile.Bounds.Width : tile.Width;
            var height = tile.Bounds.Height > 0 ? tile.Bounds.Height : tile.Height;
            if (!SeatMarqueeGeometry.Intersects(marquee, new Rect(left, top, width, height))) continue;
            seat.IsMarqueePreview = true;
            _marqueePreviewSeats.Add(seat);
        }
    }

    /// <summary>框选只做并集：已选座位保持不变，不会因为框选被取消。</summary>
    private void CommitMarquee()
    {
        foreach (var seat in _marqueePreviewSeats)
        {
            seat.IsMarqueePreview = false;
            if (seat.IsFilterVisible) seat.IsSelected = true;
        }

        _marqueePreviewSeats.Clear();
    }

    private void CancelMarquee()
    {
        foreach (var seat in _marqueePreviewSeats) seat.IsMarqueePreview = false;
        _marqueePreviewSeats.Clear();
        _marqueeShape.IsVisible = false;
        if (_marqueePointer is { } pointer && pointer.Captured == this) pointer.Capture(null);
        _marqueePointer = null;
        _marqueeActive = false;
        _marqueeArmed = false;
        _marqueeOrigin = null;
    }
}
