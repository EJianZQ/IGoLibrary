using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IGoLibrary.Ex.Application.Backup;
using IGoLibrary.Ex.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace IGoLibrary.Ex.Infrastructure.DataTransfer;

internal sealed record BackupInventory(
    BackupDataSummary Summary,
    IReadOnlyDictionary<string, BackupInventoryCategory> Categories,
    string Fingerprint);

internal sealed record BackupInventoryCategory(
    string Fingerprint,
    int Count,
    bool IsSensitive,
    string? DisplaySummary,
    IReadOnlyDictionary<string, BackupInventoryEntry> Entries);

internal sealed record BackupInventoryEntry(
    string Fingerprint,
    string? DisplaySummary = null);

internal static class BackupInventoryReader
{
    private static readonly (string Key, string DisplayName, bool Sensitive)[] SettingsCategories =
    [
        ("ui", "系统与界面", false),
        ("network", "网络与接口", false),
        ("tasks", "任务配置", false),
        ("notifications", "自动通知", true),
        ("dashboard", "数据统计", false),
        ("venue", "场馆配置", false),
        ("traceIntProtocol", "接口开关", false),
        ("updates", "更新设置", false),
        ("mobileControl", "手机控制", true),
        ("remoteCheckIn", "远程签到配置", false),
        ("logging", "日志设置", false),
        ("backupSync", "WebDAV 配置", false)
    ];

    public static async Task<BackupInventory> ReadAsync(
        string databasePath,
        BackupSecrets secrets,
        CancellationToken cancellationToken = default)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        };
        await using var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync(cancellationToken);

        var categories = new Dictionary<string, BackupInventoryCategory>(StringComparer.Ordinal);
        var settingsJson = await ReadSettingsJsonAsync(connection, cancellationToken);
        AddSettingsCategories(categories, settingsJson);

        var favorite = await ReadEntriesAsync(
            connection,
            "SELECT LibraryId, SeatKey, SeatName FROM Favorites ORDER BY LibraryId, SeatKey;",
            static reader => $"{reader.GetString(0)}\u001f{reader.GetString(1)}",
            static reader => $"{reader.GetString(0)} / {reader.GetString(2)}（{reader.GetString(1)}）",
            cancellationToken);
        categories.Add("收藏座位", CreateCategory(favorite, isSensitive: false));

        var labels = await ReadEntriesAsync(
            connection,
            "SELECT LibraryId, SeatKey, SeatName, LabelText FROM SeatLabels ORDER BY LibraryId, SeatKey;",
            static reader => $"{reader.GetString(0)}\u001f{reader.GetString(1)}",
            static reader => $"{reader.GetString(0)} / {reader.GetString(2)}：{reader.GetString(3)}",
            cancellationToken);
        categories.Add("座位标签", CreateCategory(labels, isSensitive: false));

        var protocol = await ReadEntriesAsync(
            connection,
            "SELECT Key, Value FROM ProtocolOverrides ORDER BY Key;",
            static reader => reader.GetString(0),
            static reader =>
            {
                var value = reader.GetString(1);
                return $"{reader.GetString(0)}（内容长度 {value.Length}，SHA-256 {HashString(value)[..12]}）";
            },
            cancellationToken);
        categories.Add("协议覆盖", CreateCategory(protocol, isSensitive: false));

        var history = await ReadEntriesAsync(
            connection,
            "SELECT RecordId, TaskKind, Fingerprint, RecordedAtUtc, PayloadJson FROM MobileTaskLaunchHistory ORDER BY TaskKind, RecordId;",
            static reader => reader.GetString(0),
            static reader => $"{reader.GetString(1)} / {reader.GetString(3)}",
            cancellationToken);
        categories.Add("任务历史", CreateCategory(history, isSensitive: false));

        AddSecretCategory(categories, "登录会话", secrets.Session);
        AddSecretCategory(categories, "远程签到授权", secrets.RemoteCheckInSession);
        AddSecretCategory<string>(
            categories,
            "WebDAV 密码",
            string.IsNullOrEmpty(secrets.WebDavPassword) ? null : secrets.WebDavPassword);

        var settingsCount = string.IsNullOrEmpty(settingsJson) ? 0 : 1;
        var summary = new BackupDataSummary(
            settingsCount,
            favorite.Count,
            labels.Count,
            protocol.Count,
            history.Count,
            secrets.Session is not null,
            secrets.RemoteCheckInSession is not null,
            !string.IsNullOrEmpty(secrets.WebDavPassword));
        var fingerprint = HashCategories(categories);
        return new BackupInventory(summary, categories, fingerprint);
    }

    public static BackupComparison Compare(BackupInventory local, BackupInventory backup)
    {
        var items = new List<BackupDifferenceItem>();
        foreach (var name in local.Categories.Keys
                     .Concat(backup.Categories.Keys)
                     .Distinct(StringComparer.Ordinal)
                     .OrderBy(static name => name, StringComparer.Ordinal))
        {
            local.Categories.TryGetValue(name, out var localCategory);
            backup.Categories.TryGetValue(name, out var backupCategory);
            var sensitive = localCategory?.IsSensitive == true || backupCategory?.IsSensitive == true;
            var details = CompareEntries(localCategory, backupCategory, sensitive);
            var added = details.Count(detail => detail.Kind == BackupDifferenceKind.Added);
            var removed = details.Count(detail => detail.Kind == BackupDifferenceKind.Removed);
            var changed = details.Count(detail => detail.Kind == BackupDifferenceKind.Changed);
            var unchanged = details.Count(detail => detail.Kind == BackupDifferenceKind.Unchanged);
            var kind = changed > 0 || added > 0 && removed > 0
                ? BackupDifferenceKind.Changed
                : added > 0
                    ? BackupDifferenceKind.Added
                    : removed > 0
                        ? BackupDifferenceKind.Removed
                        : BackupDifferenceKind.Unchanged;
            items.Add(new BackupDifferenceItem(
                name,
                kind,
                Describe(localCategory, sensitive),
                Describe(backupCategory, sensitive),
                sensitive,
                added,
                removed,
                changed,
                unchanged,
                details));
        }

        return new BackupComparison(
            items.Sum(item => item.AddedCount),
            items.Sum(item => item.RemovedCount),
            items.Sum(item => item.ChangedCount),
            items.Sum(item => item.UnchangedCount),
            items);
    }

    private static async Task<string?> ReadSettingsJsonAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM Settings WHERE Key = 'app-settings';";
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    private static void AddSettingsCategories(
        IDictionary<string, BackupInventoryCategory> categories,
        string? settingsJson)
    {
        if (string.IsNullOrWhiteSpace(settingsJson))
        {
            foreach (var (_, displayName, sensitive) in SettingsCategories)
            {
                categories.Add(
                    displayName,
                    CreateCategory(new Dictionary<string, BackupInventoryEntry>(), sensitive));
            }

            categories.Add(
                "其他或未来版本设置",
                CreateCategory(new Dictionary<string, BackupInventoryEntry>(), isSensitive: true));
            return;
        }

        using var document = JsonDocument.Parse(settingsJson);
        foreach (var (key, displayName, sensitive) in SettingsCategories)
        {
            var exists = document.RootElement.TryGetProperty(key, out var value);
            var entries = new Dictionary<string, BackupInventoryEntry>(StringComparer.Ordinal);
            if (exists)
            {
                entries.Add(
                    key,
                    new BackupInventoryEntry(
                        HashString(value.GetRawText()),
                        sensitive ? null : BuildSafeSettingsSummary(key, value)));
            }
            categories.Add(
                displayName,
                CreateCategory(entries, sensitive, entries.Values.FirstOrDefault()?.DisplaySummary));
        }

        var knownKeys = SettingsCategories
            .Select(static category => category.Key)
            .ToHashSet(StringComparer.Ordinal);
        var unknown = document.RootElement
            .EnumerateObject()
            .Where(property => !knownKeys.Contains(property.Name))
            .OrderBy(static property => property.Name, StringComparer.Ordinal)
            .ToArray();
        var unknownEntries = new Dictionary<string, BackupInventoryEntry>(StringComparer.Ordinal);
        foreach (var property in unknown)
        {
            unknownEntries.Add(
                property.Name,
                new BackupInventoryEntry(HashString(property.Value.GetRawText())));
        }

        categories.Add("其他或未来版本设置", CreateCategory(unknownEntries, isSensitive: true));
    }

    private static void AddSecretCategory<T>(
        IDictionary<string, BackupInventoryCategory> categories,
        string name,
        T? value)
    {
        IReadOnlyDictionary<string, BackupInventoryEntry> entries;
        if (value is null)
        {
            entries = new Dictionary<string, BackupInventoryEntry>(StringComparer.Ordinal);
        }
        else
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, AppJson.Default);
            try
            {
                entries = new Dictionary<string, BackupInventoryEntry>(StringComparer.Ordinal)
                {
                    ["state"] = new(Convert.ToHexString(SHA256.HashData(bytes)))
                };
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }

        categories.Add(name, CreateCategory(entries, isSensitive: true));
    }

    private static async Task<Dictionary<string, BackupInventoryEntry>> ReadEntriesAsync(
        SqliteConnection connection,
        string sql,
        Func<SqliteDataReader, string> keySelector,
        Func<SqliteDataReader, string?> displaySelector,
        CancellationToken cancellationToken)
    {
        var entries = new Dictionary<string, BackupInventoryEntry>(StringComparer.Ordinal);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            for (var index = 0; index < reader.FieldCount; index++)
            {
                if (reader.IsDBNull(index))
                {
                    hash.AppendData([0]);
                    continue;
                }

                var bytes = Encoding.UTF8.GetBytes(Convert.ToString(reader.GetValue(index), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
                hash.AppendData(BitConverter.GetBytes(bytes.Length));
                hash.AppendData(bytes);
            }

            if (!entries.TryAdd(
                    keySelector(reader),
                    new BackupInventoryEntry(
                        Convert.ToHexString(hash.GetHashAndReset()),
                        displaySelector(reader))))
            {
                throw new InvalidDataException("备份数据包含重复业务键");
            }
        }

        return entries;
    }

    private static string HashCategories(IReadOnlyDictionary<string, BackupInventoryCategory> categories)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var pair in categories.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(pair.Key));
            hash.AppendData(Convert.FromHexString(pair.Value.Fingerprint));
            hash.AppendData(BitConverter.GetBytes(pair.Value.Count));
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static BackupInventoryCategory CreateCategory(
        IReadOnlyDictionary<string, BackupInventoryEntry> entries,
        bool isSensitive,
        string? displaySummary = null)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var pair in entries.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            var keyBytes = Encoding.UTF8.GetBytes(pair.Key);
            hash.AppendData(BitConverter.GetBytes(keyBytes.Length));
            hash.AppendData(keyBytes);
            hash.AppendData(Convert.FromHexString(pair.Value.Fingerprint));
        }

        return new BackupInventoryCategory(
            Convert.ToHexString(hash.GetHashAndReset()),
            entries.Count,
            isSensitive,
            displaySummary,
            entries);
    }

    private static IReadOnlyList<BackupDifferenceDetail> CompareEntries(
        BackupInventoryCategory? local,
        BackupInventoryCategory? backup,
        bool sensitive)
    {
        var localEntries = local?.Entries ?? new Dictionary<string, BackupInventoryEntry>();
        var backupEntries = backup?.Entries ?? new Dictionary<string, BackupInventoryEntry>();
        var details = new List<BackupDifferenceDetail>();
        foreach (var key in localEntries.Keys
                     .Concat(backupEntries.Keys)
                     .Distinct(StringComparer.Ordinal)
                     .OrderBy(static key => key, StringComparer.Ordinal))
        {
            localEntries.TryGetValue(key, out var localEntry);
            backupEntries.TryGetValue(key, out var backupEntry);
            var kind = localEntry is null
                ? BackupDifferenceKind.Added
                : backupEntry is null
                    ? BackupDifferenceKind.Removed
                    : string.Equals(localEntry.Fingerprint, backupEntry.Fingerprint, StringComparison.Ordinal)
                        ? BackupDifferenceKind.Unchanged
                        : BackupDifferenceKind.Changed;
            details.Add(new BackupDifferenceDetail(
                kind,
                sensitive ? "受保护数据" : key,
                DescribeEntry(localEntry, sensitive),
                DescribeEntry(backupEntry, sensitive),
                sensitive));
        }

        return details;
    }

    private static string DescribeEntry(BackupInventoryEntry? entry, bool sensitive)
        => entry is null
            ? "未配置"
            : sensitive
                ? "已配置（内容已隐藏）"
                : entry.DisplaySummary ?? entry.Fingerprint[..12];

    private static string Describe(BackupInventoryCategory? category, bool sensitive)
    {
        if (category is null or { Count: 0 })
        {
            return "未配置";
        }

        if (sensitive)
        {
            return "已配置（内容已隐藏）";
        }

        if (!string.IsNullOrWhiteSpace(category.DisplaySummary))
        {
            return category.DisplaySummary;
        }

        return category.Count == 1 ? "已配置" : $"{category.Count} 项";
    }

    private static string? BuildSafeSettingsSummary(string category, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var allowed = category switch
        {
            "ui" => new[] { "minimizeToTray", "launchOnStartup" },
            "network" => new[] { "timeoutSeconds", "maxRetries" },
            "dashboard" => new[] { "successfulReservationCount", "totalGuardDurationSeconds" },
            "venue" => new[] { "lastLibraryId", "lastLibraryName" },
            "traceIntProtocol" => new[] { "graphQlOverridesEnabled" },
            "updates" => new[] { "checkOnStartup" },
            "logging" => new[] { "enabled", "retainedFileCount" },
            "backupSync" => new[] { "endpoint", "remoteDirectory", "username", "tlsVerifyMode", "allowInsecureHttp", "autoUploadEnabled" },
            _ => []
        };
        var parts = new List<string>();
        foreach (var name in allowed)
        {
            if (!value.TryGetProperty(name, out var property) ||
                property.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
            {
                continue;
            }

            var rendered = property.ValueKind == JsonValueKind.String
                ? property.GetString() ?? string.Empty
                : property.GetRawText();
            if (category == "backupSync" && name == "endpoint" &&
                Uri.TryCreate(rendered, UriKind.Absolute, out var uri))
            {
                rendered = $"{uri.Scheme}://{uri.Host}:{uri.Port}";
            }
            else if (category == "backupSync" && name == "tlsVerifyMode" &&
                     property.TryGetInt32(out var tlsVerifyMode))
            {
                rendered = tlsVerifyMode == 1 ? "Skip" : "Verify";
            }

            if (rendered.Length > 80)
            {
                rendered = rendered[..77] + "…";
            }

            parts.Add($"{name}={rendered}");
        }

        return parts.Count == 0 ? null : string.Join("，", parts);
    }

    private static string HashString(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
