namespace IGoLibrary.Ex.Application.Configuration;

public sealed record MobileControlSettings(int Port = 0, string AccessToken = "", bool AutoStart = false)
{
    public const int MinPort = 1024;

    public const int MaxPort = 65535;

    public const int RandomPortMinInclusive = 10000;

    public const int RandomPortMaxExclusive = 61000;

    public static MobileControlSettings Default { get; } = new();

    public static bool IsValidPort(int port)
    {
        return port is >= MinPort and <= MaxPort;
    }
}
