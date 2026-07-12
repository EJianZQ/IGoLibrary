using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Abstractions;

public interface ISeatLabelService
{
    Task<IReadOnlyList<SeatLabel>> GetLabelsAsync(
        int libraryId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SeatLabel>> SetLabelsAsync(
        int libraryId,
        IReadOnlyList<SeatReference> seats,
        string text,
        CancellationToken cancellationToken = default);

    Task DeleteLabelsAsync(
        int libraryId,
        IReadOnlyList<string> seatKeys,
        CancellationToken cancellationToken = default);
}
