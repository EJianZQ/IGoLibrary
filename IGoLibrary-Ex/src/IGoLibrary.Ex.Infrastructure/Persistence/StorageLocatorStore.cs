using System.Text.Json;
using System.Text.Json.Serialization;

namespace IGoLibrary.Ex.Infrastructure.Persistence;

internal sealed class StorageLocatorStore(string locatorFilePath, StorageLocations defaults)
{
    private const int SchemaVersion = 2;
    private readonly string _locatorFilePath = Path.GetFullPath(locatorFilePath);

    public StorageLocatorDocument Load()
    {
        if (!File.Exists(_locatorFilePath))
        {
            return new StorageLocatorDocument { Active = defaults };
        }

        try
        {
            var json = File.ReadAllText(_locatorFilePath);
            using var parsed = JsonDocument.Parse(json);
            if (!parsed.RootElement.TryGetProperty("schemaVersion", out var schemaVersionElement) ||
                !schemaVersionElement.TryGetInt32(out var schemaVersion))
            {
                throw new JsonException("定位配置缺少有效的版本号");
            }

            return schemaVersion switch
            {
                SchemaVersion => NormalizeDocument(
                    JsonSerializer.Deserialize<StorageLocatorDocument>(json, AppJson.Default)
                    ?? throw new JsonException("定位配置内容为空")),
                1 => UpgradeVersionOne(
                    JsonSerializer.Deserialize<LegacyStorageLocatorDocument>(json, AppJson.Default)
                    ?? throw new JsonException("定位配置内容为空")),
                _ => throw new JsonException($"不支持的存储位置配置版本：{schemaVersion}")
            };
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            var corruptPath = _locatorFilePath + $".corrupt-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
            File.Move(_locatorFilePath, corruptPath, overwrite: false);
            return new StorageLocatorDocument
            {
                Active = defaults,
                LastResult = new StorageLocationStartupResult(
                    false,
                    $"存储位置配置已损坏，已恢复默认目录并备份原文件：{ex.Message}")
            };
        }
    }

    public void SaveIfNeeded(StorageLocatorDocument document, bool stateChanged)
    {
        if (stateChanged || document.NeedsSave || document.LastResult is not null || document.PendingCleanup.Count > 0)
        {
            Save(document);
        }
    }

    public void Save(StorageLocatorDocument document)
    {
        document.SchemaVersion = SchemaVersion;
        var directory = Path.GetDirectoryName(_locatorFilePath)
                        ?? throw new InvalidOperationException("无法确定存储位置配置目录");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{StorageLocationDefaults.LocatorFileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            var json = JsonSerializer.Serialize(document, AppJson.Default);
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false)))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, _locatorFilePath, overwrite: true);
            document.NeedsSave = false;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public static bool RetryPendingCleanup(
        StorageLocatorDocument document,
        IReadOnlyCollection<string> protectedDirectories)
    {
        if (document.PendingCleanup.Count == 0)
        {
            return false;
        }

        var remaining = new List<PendingStorageCleanup>();
        var seenPaths = new HashSet<string>(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var changed = false;
        foreach (var cleanup in document.PendingCleanup)
        {
            if (cleanup is null || !cleanup.TryGetFullPath(out var path) || !seenPaths.Add(path))
            {
                changed = true;
                continue;
            }

            if (protectedDirectories.Any(directory =>
                    StoragePathRules.DirectoriesReferToSameLocation(cleanup.SourceDirectory, directory)))
            {
                changed = true;
                continue;
            }

            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                changed = true;
            }
            catch
            {
                remaining.Add(cleanup);
            }
        }

        document.PendingCleanup = remaining;
        return changed;
    }

    private static StorageLocatorDocument NormalizeDocument(StorageLocatorDocument document)
    {
        document.PendingCleanup ??= [];
        return document;
    }

    private static StorageLocatorDocument UpgradeVersionOne(LegacyStorageLocatorDocument legacy)
    {
        var cleanupItems = new List<PendingStorageCleanup>();
        foreach (var path in legacy.PendingCleanup ?? [])
        {
            if (PendingStorageCleanup.TryCreateFromLegacyPath(path, out var cleanup) && cleanup is not null)
            {
                cleanupItems.Add(cleanup);
            }
        }

        return new StorageLocatorDocument
        {
            Active = legacy.Active,
            Pending = legacy.Pending,
            PendingCleanup = cleanupItems,
            LastResult = legacy.LastResult,
            NeedsSave = true
        };
    }
}

internal sealed class StorageLocatorDocument
{
    public int SchemaVersion { get; set; } = 2;

    public StorageLocations? Active { get; set; }

    public PendingStorageLocationChange? Pending { get; set; }

    public List<PendingStorageCleanup> PendingCleanup { get; set; } = [];

    public StorageLocationStartupResult? LastResult { get; set; }

    [JsonIgnore]
    public bool NeedsSave { get; set; }
}

internal sealed class LegacyStorageLocatorDocument
{
    public int SchemaVersion { get; set; } = 1;

    public StorageLocations? Active { get; set; }

    public PendingStorageLocationChange? Pending { get; set; }

    public List<string>? PendingCleanup { get; set; }

    public StorageLocationStartupResult? LastResult { get; set; }
}
