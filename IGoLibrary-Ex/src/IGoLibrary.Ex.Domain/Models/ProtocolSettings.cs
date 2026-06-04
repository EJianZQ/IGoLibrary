namespace IGoLibrary.Ex.Domain.Models;

public sealed record ProtocolSettings
{
    public bool TemplateOverridesEnabled { get; init; }

    public ProtocolSettings()
    {
    }

    public ProtocolSettings(bool templateOverridesEnabled)
    {
        TemplateOverridesEnabled = templateOverridesEnabled;
    }

    public static ProtocolSettings Default { get; } = new();
}
