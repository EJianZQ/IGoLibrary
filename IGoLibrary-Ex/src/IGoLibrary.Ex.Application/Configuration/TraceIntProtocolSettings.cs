namespace IGoLibrary.Ex.Application.Configuration;

public sealed record TraceIntProtocolSettings
{
    public bool GraphQlOverridesEnabled { get; init; }

    public TraceIntProtocolSettings()
    {
    }

    public TraceIntProtocolSettings(bool graphQlOverridesEnabled)
    {
        GraphQlOverridesEnabled = graphQlOverridesEnabled;
    }

    public static TraceIntProtocolSettings Default { get; } = new();
}
