namespace IGoLibrary.Ex.Domain.Models;

public sealed record VenueAvailabilityWatchPlan(
    int LibraryId,
    string LibraryName,
    TimeSpan PollingInterval);
