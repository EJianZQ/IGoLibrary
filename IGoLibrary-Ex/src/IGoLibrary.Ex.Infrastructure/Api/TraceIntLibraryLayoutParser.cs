using System.Text.Json;
using IGoLibrary.Ex.Domain.Models;
using static IGoLibrary.Ex.Infrastructure.Api.TraceIntGraphQlResponseMapper;

namespace IGoLibrary.Ex.Infrastructure.Api;

internal static class TraceIntLibraryLayoutParser
{
    public static LibraryLayout Parse(JsonElement library, JsonElement layout)
    {
        var seats = new List<SeatSnapshot>();
        var items = new List<LibraryLayoutItem>();
        var invalidCount = 0;
        foreach (var element in layout.GetProperty("seats").EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object ||
                !TryReadRequiredIntProperty(element, "x", out var x) ||
                !TryReadRequiredIntProperty(element, "y", out var y))
            {
                invalidCount++;
                continue;
            }

            // Older custom protocol responses omitted type; preserve their seat interpretation.
            var type = 1;
            if (element.TryGetProperty("type", out _) && !TryReadRequiredIntProperty(element, "type", out type))
            {
                invalidCount++;
                continue;
            }
            var key = ReadOptionalStringProperty(element, "key").Trim();
            var name = ReadOptionalStringProperty(element, "name").Trim();
            int? state = TryReadRequiredIntProperty(element, "seat_status", out var stateValue) ? stateValue : null;
            bool? occupied = TryReadBooleanLikeProperty(element, "status", out var occupiedValue) ? occupiedValue : null;
            items.Add(new(type, x, y, key, name, state, occupied));
            if (type != 1) continue;
            if (key.Length == 0 || occupied is null)
            {
                invalidCount++;
                continue;
            }
            seats.Add(new(key, name.Length == 0 ? key : name, occupied.Value, x, y) { SeatStatus = state });
        }

        return new LibraryLayout(
            library.GetProperty("lib_id").GetInt32(),
            library.GetProperty("lib_name").GetString() ?? "Unknown",
            library.GetProperty("lib_floor").GetString() ?? string.Empty,
            library.GetProperty("is_open").GetBoolean(),
            layout.GetProperty("seats_total").GetInt32(),
            layout.GetProperty("seats_booking").GetInt32(),
            layout.GetProperty("seats_used").GetInt32(),
            seats.OrderBy(seat => int.TryParse(seat.SeatName, out var number) ? number : int.MaxValue).ToList())
        {
            MaxX = TryReadRequiredIntProperty(layout, "max_x", out var maxX) ? maxX : null,
            MaxY = TryReadRequiredIntProperty(layout, "max_y", out var maxY) ? maxY : null,
            LayoutItems = items,
            InvalidLayoutItemCount = invalidCount
        };
    }
}
