using IGoLibrary.Ex.Domain.Enums;

namespace IGoLibrary.Ex.Domain.Models;

public sealed record PreflightTarget(
    PreflightTaskKind Kind,
    IReadOnlyList<SeatReference> SelectedSeats)
{
    public static PreflightTarget Grab(IReadOnlyList<SeatReference> selectedSeats) =>
        new(PreflightTaskKind.Grab, selectedSeats);

    public static PreflightTarget TomorrowReservation(IReadOnlyList<SeatReference> selectedSeats) =>
        new(PreflightTaskKind.TomorrowReservation, selectedSeats);

    public static PreflightTarget Occupy() =>
        new(PreflightTaskKind.Occupy, []);

    public static PreflightTarget CheckInGuard() =>
        new(PreflightTaskKind.CheckInGuard, []);
}
