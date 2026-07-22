using IGoLibrary.Ex.Application.Backup;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;
using IGoLibrary.Ex.Infrastructure.DataTransfer;
using IGoLibrary.Ex.Infrastructure.Persistence;

namespace IGoLibrary.Ex.Tests;

public sealed class BackupInventoryComparisonTests
{
    [Fact]
    public void Compare_RedactsSensitiveValuesAndClassifiesAllKinds()
    {
        const string secretLiteral = "COOKIE-DO-NOT-LEAK";
        var local = Inventory(new Dictionary<string, BackupInventoryCategory>
        {
            ["自动通知"] = Category(true, ("secret", secretLiteral, secretLiteral)),
            ["收藏座位"] = Category(false, ("seat-1", "A", "本地座位")),
            ["座位标签"] = Category(false, ("label-1", "same-1", "标签 1"), ("label-2", "same-2", "标签 2")),
            ["仅本地"] = Category(false, ("local", "local", "仅本地"))
        });
        var backup = Inventory(new Dictionary<string, BackupInventoryCategory>
        {
            ["自动通知"] = Category(true, ("secret", "different-secret", "不得显示")),
            ["收藏座位"] = Category(false, ("seat-1", "B", "备份座位"), ("seat-2", "C", "新增座位")),
            ["座位标签"] = Category(false, ("label-1", "same-1", "标签 1"), ("label-2", "same-2", "标签 2")),
            ["仅备份"] = Category(false, ("backup", "backup", "仅备份"))
        });

        var comparison = BackupInventoryReader.Compare(local, backup);

        Assert.Equal(2, comparison.AddedCount);
        Assert.Equal(1, comparison.RemovedCount);
        Assert.Equal(2, comparison.ChangedCount);
        Assert.Equal(2, comparison.UnchangedCount);
        Assert.DoesNotContain(
            comparison.Items,
            item => item.LocalSummary.Contains(secretLiteral, StringComparison.Ordinal) ||
                    item.BackupSummary.Contains(secretLiteral, StringComparison.Ordinal));
        var sensitive = Assert.Single(comparison.Items, item => item.Category == "自动通知");
        Assert.True(sensitive.IsSensitive);
        Assert.Equal("已配置（内容已隐藏）", sensitive.LocalSummary);
        var sensitiveDetail = Assert.Single(sensitive.Details!);
        Assert.Equal("受保护数据", sensitiveDetail.Key);
        Assert.DoesNotContain(secretLiteral, sensitiveDetail.LocalValue, StringComparison.Ordinal);
        var favorites = Assert.Single(comparison.Items, item => item.Category == "收藏座位");
        Assert.Equal(1, favorites.AddedCount);
        Assert.Equal(1, favorites.ChangedCount);
    }

    [Fact]
    public async Task ReadAndCompare_DetectsChangedCredentialValuesWithoutExposingThem()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "IGoLibrary-Ex-tests",
            Guid.NewGuid().ToString("N"));
        try
        {
            var locations = new StorageLocations(directory, Path.Combine(directory, "logs"));
            var factory = new SqliteConnectionFactory(locations);
            await new SqliteAppDataInitializer(factory).InitializeAsync();
            var createdAt = DateTimeOffset.Parse("2026-07-18T08:00:00Z");
            var localSecrets = new BackupSecrets(
                new SessionCredentials("COOKIE-LOCAL", SessionSource.ManualCookie, createdAt, true),
                new RemoteCheckInSessionCredentials("TOKEN-LOCAL", createdAt, true),
                "WEBDAV-LOCAL");
            var backupSecrets = new BackupSecrets(
                new SessionCredentials("COOKIE-BACKUP", SessionSource.ManualCookie, createdAt, true),
                new RemoteCheckInSessionCredentials("TOKEN-BACKUP", createdAt, true),
                "WEBDAV-BACKUP");

            var local = await BackupInventoryReader.ReadAsync(factory.DatabasePath, localSecrets);
            var backup = await BackupInventoryReader.ReadAsync(factory.DatabasePath, backupSecrets);
            var comparison = BackupInventoryReader.Compare(local, backup);

            Assert.NotEqual(local.Fingerprint, backup.Fingerprint);
            foreach (var categoryName in new[] { "登录会话", "远程签到授权", "WebDAV 密码" })
            {
                var category = Assert.Single(comparison.Items, item => item.Category == categoryName);
                Assert.Equal(BackupDifferenceKind.Changed, category.Kind);
                Assert.Equal(1, category.ChangedCount);
                Assert.Equal("已配置（内容已隐藏）", category.LocalSummary);
                Assert.Equal("已配置（内容已隐藏）", category.BackupSummary);
            }

            var rendered = string.Join(
                '\n',
                comparison.Items.SelectMany(item =>
                    new[] { item.LocalSummary, item.BackupSummary }
                        .Concat(item.Details?.SelectMany(detail =>
                            new[] { detail.Key, detail.LocalValue, detail.BackupValue }) ?? [])));
            foreach (var secret in new[]
                     {
                         "COOKIE-LOCAL", "COOKIE-BACKUP",
                         "TOKEN-LOCAL", "TOKEN-BACKUP",
                         "WEBDAV-LOCAL", "WEBDAV-BACKUP"
                     })
            {
                Assert.DoesNotContain(secret, rendered, StringComparison.Ordinal);
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static BackupInventory Inventory(
        IReadOnlyDictionary<string, BackupInventoryCategory> categories)
        => new(
            new(0, 0, 0, 0, 0, false, false, false),
            categories,
            "fingerprint");

    private static BackupInventoryCategory Category(
        bool sensitive,
        params (string Key, string Fingerprint, string Display)[] values)
    {
        var entries = values.ToDictionary(
            static value => value.Key,
            static value => new BackupInventoryEntry(value.Fingerprint, value.Display),
            StringComparer.Ordinal);
        return new BackupInventoryCategory("category", entries.Count, sensitive, null, entries);
    }
}
