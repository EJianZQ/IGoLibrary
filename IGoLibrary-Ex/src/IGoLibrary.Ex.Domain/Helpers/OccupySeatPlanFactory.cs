using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Domain.Helpers;

public static class OccupySeatPlanFactory
{
    public static OccupySeatPlan Create(int reReserveDelaySeconds, OccupyCheckIntervalMode checkIntervalMode)
    {
        if (!Enum.IsDefined(checkIntervalMode))
        {
            throw new ArgumentOutOfRangeException(nameof(checkIntervalMode), checkIntervalMode, "占座检测间隔模式无效");
        }

        return new OccupySeatPlan(
            TimeSpan.FromSeconds(Math.Max(1, reReserveDelaySeconds)),
            checkIntervalMode);
    }
}
