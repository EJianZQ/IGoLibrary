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
    SessionInvalid = 7,
    TaskFailed = 8
}
