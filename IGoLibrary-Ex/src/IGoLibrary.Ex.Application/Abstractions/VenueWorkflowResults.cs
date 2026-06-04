using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Abstractions;

public sealed record VenueLibraryLoadResult(
    IReadOnlyList<LibrarySummary> Libraries,
    LibrarySummary? SelectedLibrary);

public sealed record VenueBindingResult(
    LibraryLayout Layout,
    LibraryRule? Rule,
    IReadOnlyList<SeatReference> Favorites,
    string? RuleFailureMessage = null);

public sealed record VenuePreviewResult(
    LibraryLayout Layout,
    LibraryRule? Rule,
    string? RuleFailureMessage = null);
