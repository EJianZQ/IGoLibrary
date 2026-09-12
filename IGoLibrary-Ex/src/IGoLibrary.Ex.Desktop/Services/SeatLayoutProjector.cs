using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Desktop.Services;

public sealed record ProjectedLayoutItem(double Left, double Top, LibraryLayoutItem Item, int? SeatIndex);

public sealed record SeatLayoutProjection(
    double Width, double Height, IReadOnlyList<ProjectedLayoutItem> Items,
    string FallbackReason, int UnknownTypeCount, bool BoundaryExpanded)
{
    public bool CanShowMap => Items.Count > 0 && FallbackReason.Length == 0;
}

/// <summary>稀疏坐标投影；不改变座位顺序、标识或业务可用性。</summary>
public static class SeatLayoutProjector
{
    public const double CellSize = 58;
    public const int MaxElements = 10_000;
    public const int MaxSpan = 4_096;

    public static SeatLayoutProjection Project(LibraryLayout layout)
    {
        var decorations = layout.LayoutItems.Where(item => item.Type != 1).ToArray();
        var unknown = decorations.Count(item => SeatFacilityCatalog.Get(item.Type) is null);
        var raw = decorations.Select(item => (Item: item, Index: (int?)null))
            .Concat(layout.Seats.Select((seat, index) =>
                (Item: new LibraryLayoutItem(1, seat.X, seat.Y, seat.SeatKey, seat.SeatName, seat.SeatStatus, seat.IsOccupied), Index: (int?)index)))
            .ToArray();
        SeatLayoutProjection Fallback(string reason) => new(0, 0, [], reason, unknown, false);
        if (raw.Length == 0) return Fallback(string.Empty);
        if (Math.Max(raw.Length, layout.LayoutItems.Count) > MaxElements)
            return Fallback("布局元素过多，已切换列表选座");
        if (layout.Seats.Select(seat => seat.SeatKey).Distinct(StringComparer.Ordinal).Count() != layout.Seats.Count)
            return Fallback("布局存在重复座位标识，已切换列表；同一标识的选择会同步");
        if (layout.Seats.Select(seat => (seat.X, seat.Y)).Distinct().Count() != layout.Seats.Count)
            return Fallback("多个座位位于同一坐标，已切换列表选座");

        var minX = (long)raw.Min(item => item.Item.X);
        var minY = (long)raw.Min(item => item.Item.Y);
        var maxX = (long)raw.Max(item => item.Item.X);
        var maxY = (long)raw.Max(item => item.Item.Y);
        if (maxX - minX + 1 > MaxSpan || maxY - minY + 1 > MaxSpan)
            return Fallback("布局坐标跨度过大，已切换列表选座");
        var expanded = layout.MaxX is { } declaredX && declaredX < maxX ||
                       layout.MaxY is { } declaredY && declaredY < maxY;
        return new((maxX - minX + 3) * CellSize, (maxY - minY + 3) * CellSize,
            raw.Select(item => new ProjectedLayoutItem(
                (item.Item.X - minX + 1) * CellSize, (item.Item.Y - minY + 1) * CellSize,
                item.Item, item.Index)).ToArray(), string.Empty, unknown, expanded);
    }
}
