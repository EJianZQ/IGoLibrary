namespace IGoLibrary.Ex.Application.Logging;

/// <summary>Invalidates in-flight captures whenever detailed logging is disabled.</summary>
public sealed class NetworkLogState
{
    private readonly object _gate = new();
    private long _generation;
    private long _activeGeneration;

    public long CaptureGeneration() => Volatile.Read(ref _activeGeneration);

    public bool IsCurrent(long generation) => generation != 0 && CaptureGeneration() == generation;

    public void Apply(bool enabled)
    {
        lock (_gate)
        {
            if (enabled && _activeGeneration == 0)
                Volatile.Write(ref _activeGeneration, ++_generation);
            else if (!enabled)
                Volatile.Write(ref _activeGeneration, 0);
        }
    }
}
