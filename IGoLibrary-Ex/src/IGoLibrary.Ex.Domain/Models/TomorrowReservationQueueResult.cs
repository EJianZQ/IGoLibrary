namespace IGoLibrary.Ex.Domain.Models;

public sealed record TomorrowReservationQueueResult(
    bool ShouldStop,
    string Message)
{
    public static TomorrowReservationQueueResult Continue(string message = "排队通道未拦截，继续明日预约流程")
        => new(false, message);

    public static TomorrowReservationQueueResult Stop(string message)
        => new(true, message);
}
