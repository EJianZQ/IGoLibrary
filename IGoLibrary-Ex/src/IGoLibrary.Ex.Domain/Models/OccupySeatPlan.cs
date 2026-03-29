using IGoLibrary.Ex.Domain.Enums;

namespace IGoLibrary.Ex.Domain.Models;

public sealed record OccupySeatPlan(
    TimeSpan ReReserveDelay,
    RefreshMode RefreshMode);
