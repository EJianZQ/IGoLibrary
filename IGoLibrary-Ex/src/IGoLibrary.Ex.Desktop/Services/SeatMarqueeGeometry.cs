using Avalonia;

namespace IGoLibrary.Ex.Desktop.Services;

/// <summary>框选几何：只做坐标归一化与相交判定，不访问控件。</summary>
public static class SeatMarqueeGeometry
{
    /// <summary>超过该位移才进入框选，避免把单击误判为拖拽。</summary>
    public const double DragThreshold = 6;

    public static Rect Normalize(Point start, Point end) => new(
        Math.Min(start.X, end.X),
        Math.Min(start.Y, end.Y),
        Math.Abs(end.X - start.X),
        Math.Abs(end.Y - start.Y));

    public static bool HasReachedThreshold(Point start, Point end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        return dx * dx + dy * dy >= DragThreshold * DragThreshold;
    }

    /// <summary>矩形与目标只要有重叠即命中；水平或垂直拖拽的零宽/零高矩形按线段处理。</summary>
    public static bool Intersects(Rect marquee, Rect item)
    {
        if (marquee.Width <= 0 && marquee.Height <= 0) return false;
        return marquee.Right >= item.Left && marquee.Left <= item.Right &&
               marquee.Bottom >= item.Top && marquee.Top <= item.Bottom;
    }
}
