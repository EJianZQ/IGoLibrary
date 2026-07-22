using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IGoLibrary.Ex.Application.Backup;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Infrastructure.DataTransfer;

internal sealed record WebDavSyncState(
    string EndpointFingerprint,
    string? ETag,
    DateTimeOffset? LastModified,
    long? ContentLength,
    string? RemoteFileSha256,
    string? LocalSemanticFingerprint,
    DateTimeOffset? LastSuccessfulSync);

internal sealed class WebDavSyncStateStore(
    StorageLocations locations,
    ILogger<WebDavSyncStateStore> logger)
{
    private readonly string _path = Path.Combine(
        locations.DataDirectory,
        ".backup-sync",
        "webdav-state.json");
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<WebDavSyncState?> LoadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            var json = await File.ReadAllTextAsync(_path, cancellationToken);
            return JsonSerializer.Deserialize<WebDavSyncState>(json, Persistence.AppJson.Default);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogWarning(ex, "WebDAV sync state is invalid; automatic overwrite protection will require manual resolution.");
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(WebDavSyncState state, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var directory = Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(directory);
            var temporary = _path + ".tmp";
            await File.WriteAllTextAsync(
                temporary,
                JsonSerializer.Serialize(state, Persistence.AppJson.Default),
                new UTF8Encoding(false),
                cancellationToken);
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public static string GetEndpointFingerprint(Uri endpointUri, string remotePath, string username)
    {
        var value = $"{endpointUri.GetLeftPart(UriPartial.Path)}|{remotePath}|{username}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }
}
