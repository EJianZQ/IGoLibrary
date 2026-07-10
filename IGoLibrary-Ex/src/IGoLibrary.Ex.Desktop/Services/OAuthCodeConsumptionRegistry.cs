namespace IGoLibrary.Ex.Desktop.Services;

public sealed class OAuthCodeConsumptionRegistry
{
    private readonly object _gate = new();
    private readonly HashSet<string> _processedCodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _inFlightCodes = new(StringComparer.OrdinalIgnoreCase);

    public bool TryReserve(string code)
    {
        lock (_gate)
        {
            if (_processedCodes.Contains(code) || _inFlightCodes.Contains(code))
            {
                return false;
            }

            _inFlightCodes.Add(code);
            return true;
        }
    }

    public void Complete(string code, bool markAsProcessed)
    {
        lock (_gate)
        {
            _inFlightCodes.Remove(code);
            if (markAsProcessed)
            {
                _processedCodes.Add(code);
            }
        }
    }
}
