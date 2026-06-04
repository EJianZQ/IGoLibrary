using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Domain.Helpers;

public static class GrabPollingStrategyFactory
{
    public static GrabSeatPollingStrategy FromMode(GrabPollingMode mode)
    {
        return mode switch
        {
            GrabPollingMode.Aggressive => new GrabSeatPollingStrategy(
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(1),
                50,
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(10)),
            GrabPollingMode.Randomized => new GrabSeatPollingStrategy(
                TimeSpan.FromSeconds(4),
                TimeSpan.FromSeconds(8),
                50,
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(10)),
            _ => new GrabSeatPollingStrategy(
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(5),
                50,
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(10))
        };
    }
}
