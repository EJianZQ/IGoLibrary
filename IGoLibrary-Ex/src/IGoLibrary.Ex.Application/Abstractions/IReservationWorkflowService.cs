using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Abstractions;

public interface IReservationWorkflowService
{
    Task<ReservationOperationResult> RefreshReservationAsync(CancellationToken cancellationToken = default);

    Task<ReservationOperationResult> CancelCurrentReservationAsync(
        ReservationInfo reservation,
        bool stopOccupyFirst,
        CancellationToken cancellationToken = default);
}
