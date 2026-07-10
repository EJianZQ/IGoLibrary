namespace IGoLibrary.Ex.Application.Configuration;

public sealed record RemoteCheckInSettings
{
    public IReadOnlyList<RemoteCheckInVenueProfileSettings> VenueProfiles { get; init; } = [];

    public static RemoteCheckInSettings Default { get; } = new();
}

public sealed record RemoteCheckInVenueProfileSettings
{
    public int LibraryId { get; init; }

    public string LibraryName { get; init; } = string.Empty;

    public string BeaconUuid { get; init; } = string.Empty;

    public int? Major { get; init; }

    public int? Minor { get; init; }

    public decimal? Latitude { get; init; }

    public decimal? Longitude { get; init; }
}
