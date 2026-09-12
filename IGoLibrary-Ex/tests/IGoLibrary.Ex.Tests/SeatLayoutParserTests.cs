using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Tests;

public sealed class SeatLayoutParserTests
{
    [Fact]
    public void Sample_PreservesAllElementsAndBusinessSeatSemantics()
    {
        var layout = SeatLayoutTestData.Sample();
        Assert.Equal(117580, layout.LibraryId);
        Assert.Equal(42, layout.MaxX);
        Assert.Equal(113, layout.MaxY);
        Assert.Equal(816, layout.LayoutItems.Count);
        Assert.Equal(356, layout.Seats.Count);
        Assert.Equal(0, layout.InvalidLayoutItemCount);
        var counts = layout.LayoutItems.GroupBy(item => item.Type).ToDictionary(group => group.Key, group => group.Count());
        Assert.Equal(new[] { 356, 178, 4, 83, 92, 103 }, new[] { 1, 2, 3, 6, 7, 8 }.Select(type => counts[type]));
        Assert.Equal(189, layout.Seats.Count(seat => seat.IsAvailable));
        Assert.Equal(6, layout.Seats.Count(seat => seat.SeatStatus == 2));
        Assert.Equal(156, layout.Seats.Count(seat => seat.SeatStatus == 3));
        Assert.Equal(5, layout.Seats.Count(seat => seat.SeatStatus == 4));
        Assert.Equal("1", layout.Seats[0].SeatName);
        Assert.All(layout.Seats, seat => Assert.Equal(!seat.IsOccupied, seat.IsAvailable));
    }

    [Fact]
    public void LegacyValues_UnknownTypesAndNames_ArePreservedWithoutChangingAvailability()
    {
        var layout = SeatLayoutTestData.ParseItems("""
            [
              {"x":"0","y":"1","key":"legacy","name":"研修间1","status":"true"},
              {"x":1,"y":1,"key":"blank","type":1,"name":"","seat_status":3,"status":false},
              {"x":2,"y":1,"key":"weird","type":1,"name":"A12","seat_status":99,"status":1},
              {"x":3,"y":1,"key":"","type":98,"name":null},
              {"x":4,"y":1,"type":8,"name":"西"}
            ]
            """, node => { node.Remove("max_x"); node.Remove("max_y"); });
        Assert.Null(layout.MaxX);
        Assert.Null(layout.MaxY);
        Assert.Equal(3, layout.Seats.Count);
        Assert.Equal(5, layout.LayoutItems.Count);
        Assert.True(layout.Seats.Single(seat => seat.SeatKey == "blank").IsAvailable);
        Assert.Null(layout.Seats.Single(seat => seat.SeatKey == "legacy").SeatStatus);
        Assert.Equal(99, layout.Seats.Single(seat => seat.SeatKey == "weird").SeatStatus);
        Assert.Equal("blank", layout.Seats.Single(seat => seat.SeatKey == "blank").SeatName);
        Assert.Contains(layout.LayoutItems, item => item.Type == 98 && item.Name == "");
    }

    [Fact]
    public void InvalidElements_AreCounted_WithoutLosingValidSeatsOrUnnamedDecorations()
    {
        var layout = SeatLayoutTestData.ParseItems("""
            [null, {"x":0,"y":"bad","type":8}, {"x":0,"y":2,"type":"bad"},
             {"x":0,"y":0,"type":1,"key":"","status":false},
             {"x":1,"y":0,"type":1,"key":"invalid-status","status":{}},
             {"x":2,"y":0,"type":1,"key":"ok","name":"001","status":false},
             {"x":3,"y":0,"type":2,"name":null}]
            """);
        Assert.Equal(5, layout.InvalidLayoutItemCount);
        Assert.Equal("ok", Assert.Single(layout.Seats).SeatKey);
        Assert.Equal(4, layout.LayoutItems.Count);
        Assert.Contains(layout.LayoutItems, item => item.Type == 2 && item.Name.Length == 0);
    }
}
