namespace IGoLibrary.Ex.Desktop.Services;

internal interface IClashMihomoCompatibilityService
{
    Task<IAsyncDisposable?> AcquireAsync(
        ClashMihomoCompatibilityOptions options,
        CancellationToken cancellationToken = default);
}

internal sealed record ClashMihomoCompatibilityOptions(
    bool Enabled,
    string ConfigPath,
    string RoutePolicy)
{
    public static ClashMihomoCompatibilityOptions Disabled { get; } =
        new(false, string.Empty, "DIRECT");
}

internal sealed record MihomoConfiguration(
    string ClientName,
    string WorkingDirectory,
    string SourcePath,
    MihomoControllerEndpoint Controller,
    string Secret);

internal abstract record MihomoControllerEndpoint
{
    internal sealed record Http(Uri BaseUri) : MihomoControllerEndpoint;

    internal sealed record WindowsNamedPipe(string PipeName) : MihomoControllerEndpoint;
}

internal interface IClashMihomoConfigurationLocator
{
    Task<IReadOnlyList<MihomoConfiguration>> FindAsync(
        string configPath,
        CancellationToken cancellationToken = default);
}

internal interface IMihomoControllerClient
{
    Task ReloadAsync(
        MihomoConfiguration configuration,
        string configurationPath,
        CancellationToken cancellationToken = default);
}
