using System.Reflection;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Updates;

namespace IGoLibrary.Ex.Application.Services;

public sealed class AssemblyAppVersionProvider : IAppVersionProvider
{
    public AssemblyAppVersionProvider()
        : this(Assembly.GetEntryAssembly() ?? typeof(AssemblyAppVersionProvider).Assembly)
    {
    }

    internal AssemblyAppVersionProvider(Assembly assembly)
    {
        CurrentVersionText = ResolveVersionText(assembly);
        if (!ReleaseVersion.TryParse(CurrentVersionText, out var version))
        {
            version = new ReleaseVersion(0, 0, 0);
            CurrentVersionText = version.ToString();
        }

        CurrentVersion = version;
    }

    public ReleaseVersion CurrentVersion { get; }

    public string CurrentVersionText { get; }

    private static string ResolveVersionText(Assembly assembly)
    {
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            var metadataSeparatorIndex = informationalVersion.IndexOf('+', StringComparison.Ordinal);
            return metadataSeparatorIndex >= 0
                ? informationalVersion[..metadataSeparatorIndex]
                : informationalVersion;
        }

        return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}
