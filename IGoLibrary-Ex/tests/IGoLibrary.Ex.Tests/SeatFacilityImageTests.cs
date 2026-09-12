using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using IGoLibrary.Ex.Desktop.Controls;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Tests;

[Collection(NonParallelTestCollection.Name)]
public sealed class SeatFacilityImageTests
{
    [AvaloniaTheory]
    [InlineData(2, "desk", "桌子")]
    [InlineData(3, "entrance", "入口")]
    [InlineData(6, "pillar", "柱子")]
    [InlineData(7, "window", "窗")]
    [InlineData(8, "bookrack", "书架")]
    public void OfficialImagesAreEmbeddedAndSharedAcrossTiles(int type, string asset, string name)
    {
        using var stream = AssetLoader.Open(new Uri($"avares://IGoLibrary.Ex.Desktop/Assets/SeatLayout/{asset}.png"));
        var signature = new byte[8];
        stream.ReadExactly(signature);
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, signature);
        Assert.Equal(asset, SeatFacilityCatalog.Get(type)!.Value.AssetName);
        var tile = Assert.IsType<Border>(SeatLayoutGraphics.Create(new(type, 0, 0, "", "", null, null)));
        var other = Assert.IsType<Border>(SeatLayoutGraphics.Create(new(type, 2, 3, "", "", null, null)));
        var image = Assert.IsType<Image>(tile.Child);
        Assert.IsType<Bitmap>(image.Source);
        Assert.Same(image.Source, Assert.IsType<Image>(other.Child).Source);
        Assert.Equal(name, ToolTip.GetTip(tile));
        Assert.Equal(52, image.Width);
        Assert.Equal(52, image.Height);
    }

    [AvaloniaFact]
    public void SampleKeepsTextPillarsSeparateFromImagePillarsAndBookrack()
    {
        var layout = SeatLayoutTestData.Sample();
        var tiles = layout.LayoutItems.Where(item => item.Type != 1)
            .Select(item => Assert.IsType<Border>(SeatLayoutGraphics.Create(item))).ToArray();
        Assert.Equal(178, tiles.Count(tile => tile.Child is Image && Equals(ToolTip.GetTip(tile), "桌子")));
        Assert.Equal(83, tiles.Count(tile => tile.Child is Image && Equals(ToolTip.GetTip(tile), "柱子")));
        Assert.Equal(92, tiles.Count(tile => tile.Child is Image && Equals(ToolTip.GetTip(tile), "窗")));
        Assert.Equal(4, tiles.Count(tile => tile.Child is Image && Equals(ToolTip.GetTip(tile), "入口")));
        Assert.Single(tiles, tile => tile.Child is Image && Equals(ToolTip.GetTip(tile), "书架"));
        Assert.Equal(94, tiles.Count(tile => tile.Child is TextBlock { Text: "柱" }));
        Assert.Single(tiles, tile => tile.Child is TextBlock { Text: "柱柱" });
        foreach (var text in new[] { "东", "南", "西", "北", "服", "务", "台" })
            Assert.Single(tiles, tile => tile.Child is TextBlock label && label.Text == text);
        Assert.Equal(102, tiles.Count(tile => tile.Child is TextBlock));
        Assert.Equal(0, SeatLayoutProjector.Project(layout).UnknownTypeCount);
        Assert.Equal(356, layout.Seats.Count);
    }

    [AvaloniaFact]
    public void TypeAndOriginalTextDetermineRendering_UnknownTypesRemainNeutral()
    {
        Border Create(int type, string name) => Assert.IsType<Border>(
            SeatLayoutGraphics.Create(new LibraryLayoutItem(type, 0, 0, "", name, null, null)));
        Assert.Equal("柱", Assert.IsType<TextBlock>(Create(8, "柱").Child).Text);
        Assert.Equal("西", Assert.IsType<TextBlock>(Create(8, "西").Child).Text);
        Assert.IsType<Image>(Create(6, "柱").Child);
        Assert.IsType<Image>(Create(8, " ").Child);
        var unknown = Create(99, "柱");
        Assert.IsType<Avalonia.Controls.Shapes.Path>(unknown.Child);
        Assert.Equal("设施类型 99（名称未确认）", ToolTip.GetTip(unknown));
    }
}
