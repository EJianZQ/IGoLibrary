using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Abstractions;

public interface ILibraryService
{
    LibrarySummary? BoundLibrary { get; }

    Task<IReadOnlyList<LibrarySummary>> LoadLibrariesAsync(CancellationToken cancellationToken = default);

    Task<LibraryLayout> BindLibraryAsync(int libraryId, CancellationToken cancellationToken = default);

    Task<LibraryLayout> RefreshBoundLibraryAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TrackedSeat>> GetFavoritesAsync(int libraryId, CancellationToken cancellationToken = default);

    Task SaveFavoritesAsync(int libraryId, IReadOnlyList<TrackedSeat> seats, CancellationToken cancellationToken = default);
}
