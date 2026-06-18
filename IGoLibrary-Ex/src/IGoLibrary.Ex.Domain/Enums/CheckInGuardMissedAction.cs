namespace IGoLibrary.Ex.Domain.Enums;

public enum CheckInGuardMissedAction
{
    NotifyOnly = 0,
    CancelReservation = 1,
    CancelAndReserveSameSeat = 2,
    CancelAndReserveSameSeatOrRandomInLibrary = 3
}
