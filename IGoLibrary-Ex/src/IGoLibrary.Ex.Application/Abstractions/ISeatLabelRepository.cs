using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Abstractions;

public interface ISeatLabelRepository
{
    Task<IReadOnlyList<SeatLabel>> GetLabelsAsync(
        int libraryId,
        CancellationToken cancellationToken = default);

    Task SetLabelsAsync(
        int libraryId,
        IReadOnlyList<SeatLabel> labels,
        CancellationToken cancellationToken = default);

    Task DeleteLabelsAsync(
        int libraryId,
        IReadOnlyList<string> seatKeys,
        CancellationToken cancellationToken = default);
}
