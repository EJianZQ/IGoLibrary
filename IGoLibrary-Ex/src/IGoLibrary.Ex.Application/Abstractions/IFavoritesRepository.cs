using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Abstractions;

public interface IFavoritesRepository
{
    Task<IReadOnlyList<SeatReference>> GetFavoritesAsync(int libraryId, CancellationToken cancellationToken = default);

    Task SaveFavoritesAsync(int libraryId, IReadOnlyList<SeatReference> seats, CancellationToken cancellationToken = default);
}
