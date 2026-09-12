using Avalonia;

namespace IGoLibrary.Ex.Desktop.Services;

/// <summary>不访问控件的视口计算，便于验证缩放锚点及边界。</summary>
public static class SeatMapViewport
{
    private const double MaximumZoom = 3;

    public static double Fit(Size content, Size viewport, bool wholeMap)
    {
        if (content.Width <= 0 || content.Height <= 0 || viewport.Width <= 0 || viewport.Height <= 0) return 1;
        var width = viewport.Width / content.Width;
        return Math.Min(MaximumZoom, wholeMap ? Math.Min(width, viewport.Height / content.Height) : width);
    }

    public static double ClampZoom(double zoom, Size content, Size viewport, double currentZoom)
    {
        // Long maps may need less than 10% to fit. Retain an already smaller zoom after
        // a resize or layout refresh so that zooming out can never enlarge the map.
        var minimum = Math.Min(currentZoom, Math.Min(0.1, Fit(content, viewport, wholeMap: true)));
        return Math.Clamp(zoom, minimum, MaximumZoom);
    }

    public static Vector Clamp(Vector offset, Size content, Size viewport, double zoom) => new(
        Math.Clamp(offset.X, 0, Math.Max(0, content.Width * zoom - viewport.Width)),
        Math.Clamp(offset.Y, 0, Math.Max(0, content.Height * zoom - viewport.Height)));

    public static Vector ZoomAt(Vector offset, Point anchor, double oldZoom, double newZoom) => new(
        (offset.X + anchor.X) / oldZoom * newZoom - anchor.X,
        (offset.Y + anchor.Y) / oldZoom * newZoom - anchor.Y);

    public static Vector Center(Point contentPoint, Size viewport, double zoom) => new(
        contentPoint.X * zoom - viewport.Width / 2,
        contentPoint.Y * zoom - viewport.Height / 2);
}
