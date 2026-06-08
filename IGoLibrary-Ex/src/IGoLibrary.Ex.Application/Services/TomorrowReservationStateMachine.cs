using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Services;

internal static class TomorrowReservationStateMachine
{
    internal static DateTimeOffset ResolveNextScheduledStart(TimeOnly scheduledStart, DateTimeOffset now)
    {
        var todayScheduledStart = new DateTimeOffset(
            now.Date.Add(scheduledStart.ToTimeSpan()),
            now.Offset);

        return todayScheduledStart <= now
            ? todayScheduledStart.AddDays(1)
            : todayScheduledStart;
    }

    internal static TimeSpan ResolveWaitDelay(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.FromMilliseconds(10))
        {
            return TimeSpan.Zero;
        }

        if (remaining > TimeSpan.FromSeconds(30))
        {
            return TimeSpan.FromSeconds(1);
        }

        if (remaining > TimeSpan.FromSeconds(10))
        {
            return TimeSpan.FromMilliseconds(500);
        }

        return remaining > TimeSpan.FromMilliseconds(50)
            ? TimeSpan.FromMilliseconds(50)
            : remaining;
    }

    internal static string BuildVerificationText(TomorrowReservationPlan plan, TomorrowReservationInfo? verification)
    {
        if (verification is null)
        {
            return $"{plan.LibraryName} · {plan.Seat.SeatName} 明日预约已提交成功，验证记录暂未返回";
        }

        if (!IsVerificationRecordForPlan(plan, verification))
        {
            return $"{plan.LibraryName} · {plan.Seat.SeatName} 明日预约已提交成功，验证记录与本次目标不一致";
        }

        return $"{verification.Day} / {plan.LibraryName} · {verification.SeatName} 明日预约已确认";
    }

    internal static bool IsVerificationRecordForPlan(
        TomorrowReservationPlan plan,
        TomorrowReservationInfo verification)
    {
        return verification.LibraryId == plan.LibraryId &&
               string.Equals(
                   NormalizeSeatKey(verification.SeatKey),
                   NormalizeSeatKey(plan.Seat.SeatKey),
                   StringComparison.Ordinal);
    }

    private static string NormalizeSeatKey(string seatKey)
    {
        return seatKey.Trim().TrimEnd('.');
    }
}
