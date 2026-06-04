using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Services;

internal static class DirectReservationMissClassifier
{
    public static bool TryClassify(Exception exception, out DirectReservationMissKind missKind)
    {
        missKind = DirectReservationMissKind.None;
        if (exception is not InvalidOperationException)
        {
            return false;
        }

        var message = exception.Message;
        if (!message.Contains("GraphQL 错误(code=1)", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (message.Contains("请重新尝试", StringComparison.OrdinalIgnoreCase))
        {
            missKind = DirectReservationMissKind.RetryRequested;
            return true;
        }

        if (message.Contains("座位", StringComparison.OrdinalIgnoreCase) &&
            (message.Contains("预定", StringComparison.OrdinalIgnoreCase) ||
             message.Contains("预约", StringComparison.OrdinalIgnoreCase) ||
             message.Contains("不可预约", StringComparison.OrdinalIgnoreCase)))
        {
            missKind = DirectReservationMissKind.Occupied;
            return true;
        }

        return false;
    }

    public static string GetMessage(DirectReservationMissKind missKind, SeatReference seat)
    {
        return missKind switch
        {
            DirectReservationMissKind.RetryRequested =>
                $"{seat.SeatName} 返回“请重新尝试”，触发短暂退避后继续。",
            DirectReservationMissKind.Occupied =>
                $"{seat.SeatName} 已被占用，继续尝试下一个目标座位。",
            _ => $"{seat.SeatName} 预约未命中。"
        };
    }
}

internal enum DirectReservationMissKind
{
    None = 0,
    Occupied = 1,
    RetryRequested = 2
}
