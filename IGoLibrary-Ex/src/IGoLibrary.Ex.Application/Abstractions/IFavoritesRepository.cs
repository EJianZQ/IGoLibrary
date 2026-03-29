using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Abstractions;

public interface IFavoritesRepository
{
    Task<IReadOnlyList<TrackedSeat>> GetFavoritesAsync(int libraryId, CancellationToken cancellationToken = default);

    Task SaveFavoritesAsync(int libraryId, IReadOnlyList<TrackedSeat> seats, CancellationToken cancellationToken = default);
}
