namespace IGoLibrary.Ex.Domain.Enums;

public enum CoordinatorStatusReason
{
    None = 0,
    Starting = 1,
    Running = 2,
    Stopping = 3,
    Stopped = 4,
    GrabSucceeded = 5,
    OccupyReReserveSucceeded = 6,
    TomorrowReservationSucceeded = 7,
    SessionInvalid = 8,
    TaskFailed = 9,
    GlobalLeakSucceeded = 10
}
