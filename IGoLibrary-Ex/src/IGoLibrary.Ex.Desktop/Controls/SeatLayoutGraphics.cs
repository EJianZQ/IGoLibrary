using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Desktop.Controls;

/// <summary>嵌入的官方设施图片和原始文字；未知类型保留中性图形。</summary>
internal static class SeatLayoutGraphics
{
    private static readonly IBrush Green = new SolidColorBrush(Color.Parse("#50AF78"));
    private static readonly IBrush Red = new SolidColorBrush(Color.Parse("#F56870"));
    private static readonly IBrush Gray = new SolidColorBrush(Color.Parse("#9EA4AC"));
    private static readonly IBrush Empty = new SolidColorBrush(Color.Parse("#CBD2D9"));
    private static readonly IBrush Blue = new SolidColorBrush(Color.Parse("#69BFE7"));
    // 五种图片在进程内按需加载、共享，不随每个设施控件重复解码或释放。
    private static readonly Dictionary<string, Lazy<Bitmap>> Images = new[] { "desk", "entrance", "pillar", "window", "bookrack" }
        .ToDictionary(name => name, name => new Lazy<Bitmap>(() =>
        {
            using var stream = AssetLoader.Open(new Uri($"avares://IGoLibrary.Ex.Desktop/Assets/SeatLayout/{name}.png"));
            return new Bitmap(stream);
        }));
    public static IBrush StatusBrush(int? status, bool occupied) => status switch
    {
        1 => Empty, 2 => Green, 3 => Red, 4 => Gray, _ => occupied ? Gray : Empty
    };

    public static Control Create(LibraryLayoutItem item)
    {
        Control content;
        var facility = SeatFacilityCatalog.Get(item.Type);
        var isText = item.Type == 8 && !string.IsNullOrWhiteSpace(item.Name);
        if (isText)
        {
            content = new TextBlock { Text = item.Name, FontSize = 17, TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            content.Bind(TextBlock.ForegroundProperty, new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("SemiColorText0"));
        }
        else if (facility is { } known)
        {
            content = new Image { Source = Images[known.AssetName].Value, Width = 52, Height = 52, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapInterpolationMode(content, BitmapInterpolationMode.HighQuality);
        }
        else
        {
            content = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse("M24,4 L44,24 24,44 4,24 Z"), Stroke = Blue, StrokeThickness = 2,
                Width = 36, Height = 36, Stretch = Stretch.Uniform,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        }
        var tile = new Border { Width = 52, Height = 52, Child = content, CornerRadius = new CornerRadius(6) };
        ToolTip.SetTip(tile, isText ? item.Name : facility?.DisplayName ?? $"设施类型 {item.Type}（名称未确认）");
        return tile;
    }
}
