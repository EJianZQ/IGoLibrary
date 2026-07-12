using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Abstractions;

public interface IVenueWorkflowService
{
    Task<VenueLibraryLoadResult> LoadLibrariesAsync(
        bool restorePreferredSelection,
        int? preferredLibraryId = null,
        CancellationToken cancellationToken = default);

    Task<VenueBindingResult> BindLibraryAsync(
        int libraryId,
        CancellationToken cancellationToken = default);

    Task<VenueBindingResult> RefreshBoundLibraryAsync(CancellationToken cancellationToken = default);

    Task<VenuePreviewResult> PreviewLibraryAsync(
        LibrarySummary library,
        CancellationToken cancellationToken = default);

    Task<LibraryRule?> LoadLibraryRuleAsync(
        int libraryId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SeatReference>> GetFavoritesAsync(
        int libraryId,
        CancellationToken cancellationToken = default);

    Task SaveFavoritesAsync(
        int libraryId,
        IReadOnlyList<SeatReference> seats,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SeatLabel>> SetSeatLabelsAsync(
        int libraryId,
        IReadOnlyList<SeatReference> seats,
        string text,
        CancellationToken cancellationToken = default);

    Task DeleteSeatLabelsAsync(
        int libraryId,
        IReadOnlyList<string> seatKeys,
        CancellationToken cancellationToken = default);
}
