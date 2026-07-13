namespace IGoLibrary.Ex.Application.Configuration;

public sealed record MainViewSizePreferences
{
    public bool RememberSize { get; init; }

    public double? ClientWidth { get; init; }

    public double? ClientHeight { get; init; }

    public MainViewSizePreferences()
    {
    }

    public MainViewSizePreferences(bool rememberSize, double? clientWidth, double? clientHeight)
    {
        RememberSize = rememberSize;
        ClientWidth = clientWidth;
        ClientHeight = clientHeight;
    }

    public static MainViewSizePreferences Default { get; } = new();

    public static MainViewSizePreferences Normalize(MainViewSizePreferences? preferences)
    {
        var current = preferences ?? Default;
        if (!TryNormalizeSize(
                current.ClientWidth,
                current.ClientHeight,
                out var normalizedWidth,
                out var normalizedHeight))
        {
            return current with
            {
                ClientWidth = null,
                ClientHeight = null
            };
        }

        return current with
        {
            ClientWidth = normalizedWidth,
            ClientHeight = normalizedHeight
        };
    }

    public static bool TryNormalizeSize(
        double? clientWidth,
        double? clientHeight,
        out double normalizedWidth,
        out double normalizedHeight)
    {
        normalizedWidth = default;
        normalizedHeight = default;
        if (clientWidth is not { } width ||
            clientHeight is not { } height ||
            !double.IsFinite(width) ||
            !double.IsFinite(height) ||
            width <= 0 ||
            height <= 0)
        {
            return false;
        }

        normalizedWidth = Math.Round(width, 2, MidpointRounding.AwayFromZero);
        normalizedHeight = Math.Round(height, 2, MidpointRounding.AwayFromZero);
        return normalizedWidth > 0 && normalizedHeight > 0;
    }
}
