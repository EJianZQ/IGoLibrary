namespace IGoLibrary.Ex.Application.Services;

internal sealed class SystemCoordinatorRuntime : ICoordinatorRuntime
{
    public DateTimeOffset Now => DateTimeOffset.Now;

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        return delay <= TimeSpan.Zero
            ? Task.CompletedTask
            : Task.Delay(delay, cancellationToken);
    }

    public TimeSpan RandomBetween(TimeSpan minimum, TimeSpan maximum)
    {
        if (maximum <= minimum)
        {
            return minimum;
        }

        var delta = maximum - minimum;
        var offset = Random.Shared.NextDouble() * delta.TotalMilliseconds;
        return minimum + TimeSpan.FromMilliseconds(offset);
    }

    public int NextInt(int minInclusive, int maxExclusive)
    {
        return Random.Shared.Next(minInclusive, maxExclusive);
    }
}
