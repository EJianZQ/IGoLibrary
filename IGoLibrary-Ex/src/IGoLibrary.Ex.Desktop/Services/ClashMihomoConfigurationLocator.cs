using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IGoLibrary.Ex.Desktop.Services;

internal sealed partial class ClashMihomoConfigurationLocator : IClashMihomoConfigurationLocator
{
    private const long MaxConfigurationBytes = 32L * 1024 * 1024;

    public async Task<IReadOnlyList<MihomoConfiguration>> FindAsync(
        string configPath,
        CancellationToken cancellationToken = default)
    {
        var results = new List<MihomoConfiguration>();
        foreach (var candidate in GetCandidatePaths(configPath)
                     .DistinctBy(static candidate => candidate.Path, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(candidate.Path))
            {
                continue;
            }

            var file = new FileInfo(candidate.Path);
            if (file.Length <= 0 || file.Length > MaxConfigurationBytes)
            {
                continue;
            }

            string content;
            try
            {
                content = await File.ReadAllTextAsync(candidate.Path, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (!TryReadController(content, out var controller, out var secret))
            {
                continue;
            }

            results.Add(new MihomoConfiguration(
                candidate.ClientName,
                Path.GetDirectoryName(candidate.Path)!,
                candidate.Path,
                controller,
                secret));
        }

        return results;
    }

    internal static bool TryReadController(
        string content,
        out MihomoControllerEndpoint controller,
        out string secret)
    {
        controller = null!;
        secret = string.Empty;
        var httpValue = ReadScalar(content, "external-controller");
        if (!string.IsNullOrWhiteSpace(httpValue) && TryReadHttpController(httpValue, out var controllerUri))
        {
            controller = new MihomoControllerEndpoint.Http(controllerUri);
            secret = ReadScalar(content, "secret") ?? string.Empty;
            return true;
        }

        var pipeValue = ReadScalar(content, "external-controller-pipe");
        if (OperatingSystem.IsWindows() &&
            TryNormalizeWindowsNamedPipe(pipeValue, out var pipeName))
        {
            controller = new MihomoControllerEndpoint.WindowsNamedPipe(pipeName);
            return true;
        }

        return false;
    }

    private static bool TryReadHttpController(string controller, out Uri controllerUri)
    {
        controllerUri = null!;
        var value = controller.Contains("://", StringComparison.Ordinal)
            ? controller
            : controller.StartsWith(':')
                ? $"http://127.0.0.1{controller}"
                : $"http://{controller}";
        if (!Uri.TryCreate(value, UriKind.Absolute, out var candidate) ||
            candidate.Scheme != Uri.UriSchemeHttp ||
            !candidate.IsDefaultPort && candidate.Port is <= 0 or > 65535 ||
            !string.IsNullOrEmpty(candidate.UserInfo) ||
            candidate.AbsolutePath != "/" ||
            !TryNormalizeControllerHost(candidate.Host, out var controllerHost))
        {
            return false;
        }

        controllerUri = new UriBuilder(Uri.UriSchemeHttp, controllerHost, candidate.Port)
        {
            Path = "/"
        }.Uri;
        return true;
    }

    internal static bool TryNormalizeWindowsNamedPipe(string? value, out string pipeName)
    {
        pipeName = string.Empty;
        const string prefix = @"\\.\pipe\";
        if (string.IsNullOrWhiteSpace(value) ||
            !value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var candidate = value[prefix.Length..].Trim();
        if (candidate.Length is <= 0 or > 128 ||
            candidate.Any(static character =>
                char.IsControl(character) || character is '\\' or '/' or ':'))
        {
            return false;
        }

        pipeName = candidate;
        return true;
    }

    private static IEnumerable<(string ClientName, string Path)> GetCandidatePaths(string configPath)
    {
        if (!string.IsNullOrWhiteSpace(configPath))
        {
            yield return ("Mihomo（手动配置）", configPath);
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(appData))
        {
            yield return (
                "Clash Verge Rev",
                Path.Combine(appData, "io.github.clash-verge-rev.clash-verge-rev", "clash-verge.yaml"));
            yield return ("Mihomo", Path.Combine(appData, "mihomo", "config.yaml"));
            yield return ("Clash Nyanpasu", Path.Combine(appData, "io.github.clash-nyanpasu.clash-nyanpasu", "clash-config.yaml"));
            yield return ("Mihomo Party", Path.Combine(appData, "mihomo-party", "work", "config.yaml"));
        }

        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            yield return ("Mihomo Party", Path.Combine(localAppData, "mihomo-party", "work", "config.yaml"));
        }

        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            yield return ("Mihomo", Path.Combine(userProfile, ".config", "mihomo", "config.yaml"));
            yield return ("Clash Meta", Path.Combine(userProfile, ".config", "clash", "config.yaml"));
        }
    }

    private static string? ReadScalar(string content, string key)
    {
        foreach (var line in content.Split('\n'))
        {
            var match = TopLevelScalarRegex().Match(line.TrimEnd('\r'));
            if (!match.Success || !match.Groups["key"].Value.Equals(key, StringComparison.Ordinal))
            {
                continue;
            }

            var value = match.Groups["value"].Value.Trim();
            if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
            {
                return value[1..^1].Replace("''", "'", StringComparison.Ordinal);
            }

            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            {
                try
                {
                    return JsonSerializer.Deserialize<string>(value);
                }
                catch (JsonException)
                {
                    return null;
                }
            }

            var commentIndex = value.IndexOf(" #", StringComparison.Ordinal);
            return (commentIndex >= 0 ? value[..commentIndex] : value).Trim();
        }

        return null;
    }

    private static bool TryNormalizeControllerHost(string host, out string normalized)
    {
        normalized = host;
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!IPAddress.TryParse(host, out var address))
        {
            return false;
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.Equals(IPAddress.Any))
        {
            normalized = IPAddress.Loopback.ToString();
            return true;
        }

        if (address.Equals(IPAddress.IPv6Any))
        {
            normalized = IPAddress.IPv6Loopback.ToString();
            return true;
        }

        return false;
    }

    [GeneratedRegex(@"^(?<key>[A-Za-z][A-Za-z0-9_-]*)\s*:\s*(?<value>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex TopLevelScalarRegex();
}
