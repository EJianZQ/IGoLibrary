using System.Runtime.InteropServices;
using System.Text.Json;
using IGoLibrary.Ex.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Desktop.Services;

internal sealed class CloudflaredAssetCatalog
{
    internal const string ManifestResourceName =
        "IGoLibrary.Ex.Desktop.Resources.Cloudflared.cloudflared-assets.json";
    internal const string LicenseResourceName =
        "IGoLibrary.Ex.Desktop.Resources.Cloudflared.LICENSE.txt";
    internal const string NoticesResourceName =
        "IGoLibrary.Ex.Desktop.Resources.Cloudflared.THIRD-PARTY-NOTICES.txt";

    private readonly CloudflaredAssetManifest _manifest;
    private readonly string _rid;
    private readonly ILogger<CloudflaredAssetCatalog> _logger;

    public CloudflaredAssetCatalog(ILogger<CloudflaredAssetCatalog> logger)
        : this(ReadManifestJson(), ResolveCurrentRid(), logger)
    {
    }

    internal CloudflaredAssetCatalog(
        string manifestJson,
        string rid,
        ILogger<CloudflaredAssetCatalog> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(rid);
        _logger = logger;
        _manifest = JsonSerializer.Deserialize(
                        manifestJson,
                        DesktopUpdateJsonSerializerContext.Default.CloudflaredAssetManifest)
                    ?? throw new InvalidDataException("cloudflared 资产清单为空");
        _rid = rid;
        Current = BuildCurrentAsset();
        LicenseBytes = ReadEmbeddedBytes(LicenseResourceName);
        NoticesBytes = ReadEmbeddedBytes(NoticesResourceName);
        _logger.LogInformation(
            "已加载 cloudflared 资产清单。版本={Version}，运行时={RuntimeIdentifier}，下载大小={DownloadSize}。",
            Current.Version,
            Current.RuntimeIdentifier,
            Current.DownloadSize);
    }

    internal CloudflaredAssetDescriptor Current { get; }

    internal byte[] LicenseBytes { get; }

    internal byte[] NoticesBytes { get; }

    private CloudflaredAssetDescriptor BuildCurrentAsset()
    {
        if (string.IsNullOrWhiteSpace(_manifest.Version) ||
            !_manifest.Assets.TryGetValue(_rid, out var entry) ||
            entry is null)
        {
            throw new PlatformNotSupportedException($"当前系统架构不支持自动下载 cloudflared：{_rid}");
        }

        ValidateEntry(_manifest.Version, _rid, entry);
        var executableName = _rid == "win-x64" ? "cloudflared.exe" : "cloudflared";
        var downloadUri = new Uri(
            $"https://github.com/cloudflare/cloudflared/releases/download/{Uri.EscapeDataString(_manifest.Version)}/{Uri.EscapeDataString(entry.FileName)}");
        return new CloudflaredAssetDescriptor(
            _manifest.Version,
            _rid,
            entry.FileName,
            entry.ArchiveType,
            entry.Size,
            entry.Sha256.ToLowerInvariant(),
            executableName,
            entry.ExecutableSize,
            entry.ExecutableSha256.ToLowerInvariant(),
            downloadUri);
    }

    private static void ValidateEntry(string version, string rid, CloudflaredAssetEntry entry)
    {
        if (!Version.TryParse(version, out _) ||
            string.IsNullOrWhiteSpace(entry.FileName) ||
            entry.FileName != Path.GetFileName(entry.FileName) ||
            entry.Size <= 0 ||
            entry.ExecutableSize <= 0 ||
            !IsSha256(entry.Sha256) ||
            !IsSha256(entry.ExecutableSha256) ||
            entry.ArchiveType is not ("binary" or "tgz") ||
            rid == "win-x64" && entry.ArchiveType != "binary" ||
            rid.StartsWith("osx-", StringComparison.Ordinal) && entry.ArchiveType != "tgz")
        {
            throw new InvalidDataException($"cloudflared 资产清单中的 {rid} 条目无效");
        }
    }

    private static bool IsSha256(string? value)
        => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static string ResolveCurrentRid()
    {
        if (OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64)
        {
            return "win-x64";
        }

        if (OperatingSystem.IsMacOS())
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "osx-x64",
                Architecture.Arm64 => "osx-arm64",
                _ => throw new PlatformNotSupportedException(
                    $"当前 macOS 架构不支持自动下载 cloudflared：{RuntimeInformation.ProcessArchitecture}")
            };
        }

        throw new PlatformNotSupportedException(
            $"当前系统不支持自动下载 cloudflared：{RuntimeInformation.OSDescription}");
    }

    private static string ReadManifestJson()
    {
        using var stream = OpenResource(ManifestResourceName);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static byte[] ReadEmbeddedBytes(string resourceName)
    {
        using var stream = OpenResource(resourceName);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static Stream OpenResource(string resourceName)
        => typeof(CloudflaredAssetCatalog).Assembly.GetManifestResourceStream(resourceName)
           ?? throw new InvalidDataException($"缺少嵌入资源：{resourceName}");
}

internal sealed record CloudflaredAssetDescriptor(
    string Version,
    string RuntimeIdentifier,
    string FileName,
    string ArchiveType,
    long DownloadSize,
    string DownloadSha256,
    string ExecutableName,
    long ExecutableSize,
    string ExecutableSha256,
    Uri DownloadUri)
{
    internal ReleaseAssetInfo ToReleaseAssetInfo()
        => new(
            FileName,
            DownloadUri,
            DownloadSize,
            $"sha256:{DownloadSha256}",
            ArchiveType == "tgz" ? "application/gzip" : "application/octet-stream");
}

internal sealed record CloudflaredAssetManifest(
    string Version,
    Dictionary<string, CloudflaredAssetEntry> Assets);

internal sealed record CloudflaredAssetEntry(
    string FileName,
    long Size,
    string Sha256,
    long ExecutableSize,
    string ExecutableSha256,
    string ArchiveType);
