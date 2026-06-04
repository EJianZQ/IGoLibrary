using IGoLibrary.Ex.Domain.Enums;

namespace IGoLibrary.Ex.Application.Services;

internal sealed class GrabReservationStrategySelector(IEnumerable<IGrabReservationAttemptStrategy> strategies)
{
    private readonly IReadOnlyDictionary<GrabReservationStrategy, IGrabReservationAttemptStrategy> _strategies =
        strategies.ToDictionary(strategy => strategy.Strategy);

    public IGrabReservationAttemptStrategy Select(GrabReservationStrategy strategy)
    {
        return _strategies.TryGetValue(strategy, out var selected)
            ? selected
            : throw new InvalidOperationException($"不支持的抢座预约策略：{strategy}。");
    }
}
