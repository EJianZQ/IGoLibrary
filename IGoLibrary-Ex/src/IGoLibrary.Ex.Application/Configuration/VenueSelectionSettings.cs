namespace IGoLibrary.Ex.Application.Configuration;

public sealed record VenueSelectionSettings
{
    public int? LastLibraryId { get; init; }

    public string? LastLibraryName { get; init; }

    public VenueSelectionSettings()
    {
    }

    public VenueSelectionSettings(int? lastLibraryId, string? lastLibraryName)
    {
        LastLibraryId = lastLibraryId;
        LastLibraryName = lastLibraryName;
    }

    public static VenueSelectionSettings Default { get; } = new();
}
