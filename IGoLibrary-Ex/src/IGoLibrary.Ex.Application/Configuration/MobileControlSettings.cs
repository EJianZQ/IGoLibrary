namespace IGoLibrary.Ex.Application.Configuration;

public enum MobileControlNetworkMode
{
    LocalNetwork = 0,
    CloudflareTunnel = 1
}

public enum CloudflareTunnelProxyMode
{
    Auto = 0,
    SystemProxy = 1,
    ManualHttpProxy = 2,
    Direct = 3
}

public sealed record MobileControlSettings(
    int Port = 0,
    string AccessToken = "",
    bool AutoStart = false,
    MobileControlNetworkMode NetworkMode = MobileControlNetworkMode.LocalNetwork,
    CloudflareTunnelProxyMode TunnelProxyMode = CloudflareTunnelProxyMode.Auto,
    string TunnelManualProxyUrl = "",
    bool ClashMihomoCompatibilityEnabled = false,
    string ClashMihomoConfigPath = "",
    string ClashMihomoRoutePolicy = "DIRECT",
    bool FallbackToLocalNetworkOnTunnelFailure = true)
{
    public const string DefaultClashMihomoRoutePolicy = "DIRECT";

    public const int MinPort = 1024;

    public const int MaxPort = 65535;

    public const int RandomPortMinInclusive = 10000;

    public const int RandomPortMaxExclusive = 61000;

    public static MobileControlSettings Default { get; } = new();

    public static bool IsValidPort(int port)
    {
        return port is >= MinPort and <= MaxPort;
    }

    public static MobileControlNetworkMode NormalizeNetworkMode(MobileControlNetworkMode mode)
    {
        return mode is MobileControlNetworkMode.LocalNetwork or MobileControlNetworkMode.CloudflareTunnel
            ? mode
            : MobileControlNetworkMode.LocalNetwork;
    }

    public static CloudflareTunnelProxyMode NormalizeTunnelProxyMode(CloudflareTunnelProxyMode mode)
    {
        return mode is >= CloudflareTunnelProxyMode.Auto and <= CloudflareTunnelProxyMode.Direct
            ? mode
            : CloudflareTunnelProxyMode.Auto;
    }

    public static bool TryNormalizeManualProxyUrl(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttp ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            uri.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        normalized = uri.GetLeftPart(UriPartial.Authority);
        return true;
    }

    public static bool TryNormalizeClashMihomoConfigPath(string? value, out string normalized)
    {
        normalized = string.Empty;
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return true;
        }

        var extension = Path.GetExtension(trimmed);
        if (!Path.IsPathFullyQualified(trimmed) ||
            !extension.Equals(".yaml", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".yml", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        normalized = Path.GetFullPath(trimmed);
        return true;
    }

    public static bool TryNormalizeClashMihomoRoutePolicy(string? value, out string normalized)
    {
        normalized = value?.Trim() ?? string.Empty;
        return normalized.Length is > 0 and <= 128 &&
               normalized.All(static character =>
                   !char.IsControl(character) && character is not ',' and not '#');
    }
}
