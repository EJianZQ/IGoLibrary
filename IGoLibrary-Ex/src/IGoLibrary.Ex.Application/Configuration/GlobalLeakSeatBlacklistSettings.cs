namespace IGoLibrary.Ex.Application.Configuration;

public sealed record GlobalLeakBlacklistedSeatSettings(int LibraryId, string SeatKey, string SeatName);

public static class GlobalLeakSeatBlacklistSettings
{
    public static IReadOnlyList<GlobalLeakBlacklistedSeatSettings> Normalize(
        IReadOnlyList<GlobalLeakBlacklistedSeatSettings>? seats)
    {
        return (seats ?? [])
            .Where(static seat => seat is not null && seat.LibraryId > 0 && !string.IsNullOrWhiteSpace(seat.SeatKey))
            .DistinctBy(static seat => (seat.LibraryId, seat.SeatKey))
            .Select(static seat => seat with
            {
                SeatName = string.IsNullOrWhiteSpace(seat.SeatName) ? seat.SeatKey : seat.SeatName
            })
            .OrderBy(static seat => seat.LibraryId)
            .ThenBy(static seat => seat.SeatKey, StringComparer.Ordinal)
            .ToArray();
    }
}
