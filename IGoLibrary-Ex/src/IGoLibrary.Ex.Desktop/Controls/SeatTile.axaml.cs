using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using IGoLibrary.Ex.Desktop.ViewModels;

namespace IGoLibrary.Ex.Desktop.Controls;

public partial class SeatTile : UserControl
{
    public SeatTile()
    {
        InitializeComponent();
        SetMapZoom(1);
        DataContextChanged += (_, _) =>
        {
            if (DataContext is SeatItemViewModel seat)
                StatusStripe.Background = SeatLayoutGraphics.StatusBrush(seat.SeatStatus, seat.IsOccupied);
        };
    }

    /// <summary>角标在常用缩放下保持屏幕可读尺寸；极小地图使用不越过座位边界的简化标记。</summary>
    public void SetMapZoom(double zoom)
    {
        if (!double.IsFinite(zoom) || zoom <= 0) return;
        // 低于 47% 时，52 DIP 座位无法为两个 12 DIP 角标保留间隔。
        var compact = zoom < 0.47;
        var screenSize = compact ? Math.Min(5, 52 * zoom * 0.22) : Math.Clamp(14 * zoom, 12, 18);
        var size = screenSize / zoom;
        FavoriteBadge.Width = FavoriteBadge.Height = LabelBadge.Width = LabelBadge.Height = size;
        FavoriteBadge.CornerRadius = new CornerRadius(size / 2);
        LabelBadge.CornerRadius = new CornerRadius(compact ? 0 : size / 5);
        FavoriteBadge.BorderThickness = LabelBadge.BorderThickness = new Thickness(Math.Min(1, screenSize / 5) / zoom);
        FavoriteGlyph.IsVisible = LabelGlyph.IsVisible = !compact;
        FavoriteGlyph.Margin = new Thickness(size * 0.16);
        LabelGlyph.Margin = new Thickness(size * 0.23, size * 0.16);
        // 为状态条和角标各保留独立区域；所有座位号保持对齐，点击区域仍为 52 DIP。
        SeatNumberBox.Height = 47 - size;
        SeatNumberBox.RenderTransform = new TranslateTransform(0, (5 - size) / 2);
    }
}
