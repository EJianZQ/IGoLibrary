namespace IGoLibrary.Ex.Application.Services;

internal interface ICoordinatorRuntime
{
    DateTimeOffset Now { get; }

    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);

    TimeSpan RandomBetween(TimeSpan minimum, TimeSpan maximum);

    int NextInt(int minInclusive, int maxExclusive);
}
