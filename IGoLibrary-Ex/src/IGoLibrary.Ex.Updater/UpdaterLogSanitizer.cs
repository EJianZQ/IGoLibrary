using System.Text.RegularExpressions;

namespace IGoLibrary.Ex.Updater;

// The Native AOT updater intentionally cannot reference the main Application assembly.
// Keep these rules synchronized with Application/Logging/AppLogSanitizer.cs.
internal static partial class UpdaterLogSanitizer
{
    private const string Redacted = "<redacted>";

    public static string Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value ?? string.Empty;
        }

        var sanitized = UrlQueryRegex().Replace(
            value,
            static match => $"{match.Groups["base"].Value}?<redacted>");
        sanitized = UrlUserInfoRegex().Replace(
            sanitized,
            static match => $"{match.Groups["scheme"].Value}{Redacted}@");
        sanitized = HeaderSecretRegex().Replace(
            sanitized,
            static match => $"{match.Groups["key"].Value}{Redacted}");
        sanitized = NamedSecretRegex().Replace(
            sanitized,
            static match => $"{match.Groups["key"].Value}{Redacted}");
        sanitized = CredentialSchemeRegex().Replace(
            sanitized,
            static match => $"{match.Groups["scheme"].Value}{Redacted}");
        sanitized = JwtRegex().Replace(sanitized, Redacted);
        sanitized = EmailRegex().Replace(
            sanitized,
            static match => $"***@{match.Groups["domain"].Value}");
        sanitized = WindowsUserPathRegex().Replace(sanitized, @"%USERPROFILE%\");
        sanitized = MacUserPathRegex().Replace(sanitized, "/Users/<user>/");
        sanitized = UnixHomePathRegex().Replace(sanitized, "/home/<user>/");
        return sanitized;
    }

    [GeneratedRegex(
        @"(?<base>\b(?:https?|wss?)://[^\s?#]+)\?[^\s#|]*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UrlQueryRegex();

    [GeneratedRegex(
        @"(?<scheme>\b(?:https?|wss?)://)[^/\s:@]+:[^@/\s]+@",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UrlUserInfoRegex();

    [GeneratedRegex(
        @"(?<key>\b(?:Cookie|Set-Cookie|Authorization|Proxy-Authorization)\s*[:=]\s*)[^\r\n|]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HeaderSecretRegex();

    [GeneratedRegex(
        @"(?<key>[""']?\b(?:access[_-]?token|refresh[_-]?token|token|wechatSESS_ID|password|passwd|secret|sendkey|api[_-]?key|authorization|serverid|chat[_-]?id|device[_-]?key)\b[""']?\s*[:=]\s*)(?:""[^""\r\n]*""|'[^'\r\n]*'|[^&,\s;|}\]]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NamedSecretRegex();

    [GeneratedRegex(
        @"(?<scheme>\b(?:Bearer|Basic)\s+)[A-Za-z0-9._~+/=-]{6,}",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CredentialSchemeRegex();

    [GeneratedRegex(
        @"\b[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex JwtRegex();

    [GeneratedRegex(
        @"\b[A-Z0-9._%+-]+@(?<domain>[A-Z0-9.-]+\.[A-Z]{2,})\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(
        @"[A-Z]:\\Users\\[^\\\r\n]+\\",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WindowsUserPathRegex();

    [GeneratedRegex(
        @"/Users/[^/\r\n]+/",
        RegexOptions.CultureInvariant)]
    private static partial Regex MacUserPathRegex();

    [GeneratedRegex(
        @"/home/[^/\s]+/",
        RegexOptions.CultureInvariant)]
    private static partial Regex UnixHomePathRegex();
}
