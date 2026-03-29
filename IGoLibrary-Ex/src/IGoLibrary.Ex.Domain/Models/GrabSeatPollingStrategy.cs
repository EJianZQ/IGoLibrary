namespace IGoLibrary.Ex.Domain.Models;

public sealed record GrabSeatPollingStrategy(
    TimeSpan MinimumDelay,
    TimeSpan MaximumDelay,
    int CooldownEveryCycles,
    TimeSpan CooldownMinimum,
    TimeSpan CooldownMaximum);
