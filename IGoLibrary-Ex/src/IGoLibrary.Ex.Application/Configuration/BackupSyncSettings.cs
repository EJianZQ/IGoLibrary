namespace IGoLibrary.Ex.Application.Configuration;

public enum WebDavTlsVerifyMode
{
    Verify = 0,
    Skip = 1
}

public sealed record BackupSyncSettings(
    string Endpoint = "",
    string RemoteDirectory = "IGoLibrary-Ex",
    string Username = "",
    WebDavTlsVerifyMode TlsVerifyMode = WebDavTlsVerifyMode.Verify,
    bool AllowInsecureHttp = false,
    bool AutoUploadEnabled = false)
{
    public const string DefaultRemoteDirectory = "IGoLibrary-Ex";
    public const string RemoteFileName = "IGoLibrary-Ex.igobackup";

    public static BackupSyncSettings Default { get; } = new();

    public static BackupSyncSettings Normalize(BackupSyncSettings? settings)
    {
        settings ??= Default;
        return settings with
        {
            Endpoint = settings.Endpoint?.Trim() ?? string.Empty,
            RemoteDirectory = NormalizeRemoteDirectory(settings.RemoteDirectory),
            Username = settings.Username?.Trim() ?? string.Empty,
            TlsVerifyMode = settings.TlsVerifyMode is WebDavTlsVerifyMode.Verify or WebDavTlsVerifyMode.Skip
                ? settings.TlsVerifyMode
                : WebDavTlsVerifyMode.Verify
        };
    }

    public static bool TryValidateEndpoint(
        string? value,
        bool allowInsecureHttp,
        out Uri? normalized,
        out string? error)
    {
        normalized = null;
        error = null;
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            error = "WebDAV Endpoint 必须是有效的 HTTP 或 HTTPS 绝对地址";
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            error = "WebDAV Endpoint 不能包含账号、查询参数或片段";
            return false;
        }

        if (uri.Scheme == Uri.UriSchemeHttp && !allowInsecureHttp)
        {
            error = "HTTP 连接未获授权，请先确认明文传输风险";
            return false;
        }

        var builder = new UriBuilder(uri)
        {
            Path = uri.AbsolutePath.EndsWith("/", StringComparison.Ordinal)
                ? uri.AbsolutePath
                : uri.AbsolutePath + "/"
        };
        normalized = builder.Uri;
        return true;
    }

    public static bool TryValidateRemoteDirectory(
        string? value,
        out string normalized,
        out string? error)
    {
        normalized = NormalizeRemoteDirectory(value);
        error = null;
        if (string.IsNullOrEmpty(normalized))
        {
            return true;
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => segment is "." or ".." ||
                                    segment.Contains('\\') ||
                                    segment.Any(char.IsControl)))
        {
            error = "WebDAV 同步目录包含无效的路径片段";
            return false;
        }

        return true;
    }

    public static string BuildRemotePath(string? remoteDirectory)
    {
        var normalized = NormalizeRemoteDirectory(remoteDirectory);
        return string.IsNullOrEmpty(normalized)
            ? RemoteFileName
            : $"{normalized}/{RemoteFileName}";
    }

    private static string NormalizeRemoteDirectory(string? value)
        => (value ?? string.Empty).Trim().Replace('\\', '/').Trim('/');
}
