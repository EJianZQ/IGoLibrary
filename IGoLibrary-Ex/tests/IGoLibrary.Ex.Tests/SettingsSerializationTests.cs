using System.Reflection;
using System.Text.Json;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;
using IGoLibrary.Ex.Infrastructure.Persistence;

namespace IGoLibrary.Ex.Tests;

public sealed class SettingsSerializationTests
{
    [Fact]
    public void LegacyFlatJson_MigratesToNestedAppSettings()
    {
        var settings = MigrateAndDeserialize(
            """
            {
              "notificationsEnabled": false,
              "minimizeToTray": false,
              "customApiOverridesEnabled": true,
              "apiTimeoutSeconds": 9,
              "retryCount": 4,
              "themeMode": 2,
              "useSystemAccent": false,
              "grabReservationStrategy": 1,
              "lastLibraryId": 12,
              "lastLibraryName": "自科阅览区一",
              "successfulReservationCount": 6,
              "totalGuardSeconds": 3600
            }
            """);

        Assert.False(settings.Notifications.AppBannerNotificationsEnabled);
        Assert.False(settings.Ui.MinimizeToTray);
        Assert.Equal(AppThemeMode.Dark, settings.Ui.Theme?.Mode);
        Assert.False(settings.Ui.Theme?.UseSystemAccent);
        Assert.True(settings.Protocol.TemplateOverridesEnabled);
        Assert.Equal(9, settings.RequestPolicy.TimeoutSeconds);
        Assert.Equal(4, settings.RequestPolicy.RetryCount);
        Assert.Equal(GrabReservationStrategy.ReserveDirectly, settings.Tasks.GrabReservationStrategy);
        Assert.Equal(12, settings.Venue.LastLibraryId);
        Assert.Equal("自科阅览区一", settings.Venue.LastLibraryName);
        Assert.Equal(6, settings.Dashboard.SuccessfulReservationCount);
        Assert.Equal(3600, settings.Dashboard.TotalGuardSeconds);
    }

    [Fact]
    public void LegacyAdvancedModeMigration_MigratesToProtocolSettings()
    {
        const string json = """{"advancedMode":true}""";

        var migratedJson = MigrateLegacyAppSettingsJson(json);
        using var document = JsonDocument.Parse(migratedJson);

        Assert.True(document.RootElement.GetProperty("protocol").GetProperty("templateOverridesEnabled").GetBoolean());
        Assert.False(document.RootElement.TryGetProperty("customApiOverridesEnabled", out _));
        Assert.False(document.RootElement.TryGetProperty("advancedMode", out _));
    }

    [Fact]
    public void LegacyCookieExpiryAlertsMigration_MigratesToTaskEventAlerts()
    {
        var settings = MigrateAndDeserialize(
            """
            {
              "cookieExpiryAlerts": {
                "email": {
                  "enabled": true,
                  "smtpHost": "smtp.example.com",
                  "port": 465,
                  "securityMode": 2,
                  "username": "tester",
                  "password": "secret",
                  "fromAddress": "from@example.com",
                  "toAddress": "to@example.com"
                },
                "local": {
                  "toastEnabled": true,
                  "soundEnabled": false
                }
              }
            }
            """);

        var alerts = Assert.IsType<TaskEventAlertSettings>(settings.Notifications.TaskEventAlerts);
        Assert.True(alerts.Email.Enabled);
        Assert.Equal("smtp.example.com", alerts.Email.SmtpHost);
        Assert.Equal(465, alerts.Email.Port);
        Assert.True(alerts.Local.ToastEnabled);
        Assert.False(alerts.Local.SoundEnabled);
        Assert.Equal(TelegramAlertChannelSettings.Default, alerts.Telegram);
    }

    [Fact]
    public void AppSettingsDeserialization_UsesDefaults_WhenNestedJsonOmitsProperties()
    {
        var settings = Assert.IsType<AppSettings>(JsonSerializer.Deserialize<AppSettings>(
            """
            {
              "notifications": {},
              "ui": {
                "theme": {}
              },
              "requestPolicy": {},
              "tasks": {}
            }
            """,
            AppJson.Default));

        Assert.True(settings.Notifications.AppBannerNotificationsEnabled);
        Assert.Equal(TaskEventAlertSettings.Default, settings.Notifications.TaskEventAlerts);
        Assert.True(settings.Ui.MinimizeToTray);
        Assert.Equal(AppThemeMode.FollowSystem, settings.Ui.Theme?.Mode);
        Assert.Equal(ThemeSettings.Default.UseSystemAccent, settings.Ui.Theme?.UseSystemAccent);
        Assert.Equal(5, settings.RequestPolicy.TimeoutSeconds);
        Assert.Equal(3, settings.RequestPolicy.RetryCount);
        Assert.Equal(GrabReservationStrategy.QueryThenReserve, settings.Tasks.GrabReservationStrategy);
    }

    [Fact]
    public void AppSettingsSerialization_WritesNestedSettingsBlocks()
    {
        var json = JsonSerializer.Serialize(AppSettings.Default with
        {
            Notifications = AppSettings.Default.Notifications with
            {
                AppBannerNotificationsEnabled = false,
                TaskEventAlerts = TaskEventAlertSettings.Default
            },
            Protocol = new ProtocolSettings(true)
        }, AppJson.Default);

        Assert.Contains("\"notifications\":", json);
        Assert.Contains("\"ui\":", json);
        Assert.Contains("\"protocol\":", json);
        Assert.Contains("\"requestPolicy\":", json);
        Assert.Contains("\"tasks\":", json);
        Assert.Contains("\"venue\":", json);
        Assert.Contains("\"dashboard\":", json);
        Assert.Contains("\"taskEventAlerts\":", json);
        Assert.Contains("\"templateOverridesEnabled\": true", json);
    }

    [Fact]
    public void AppSettingsSerialization_DoesNotWriteLegacyRootFields()
    {
        var json = JsonSerializer.Serialize(AppSettings.Default with
        {
            Protocol = new ProtocolSettings(true),
            RequestPolicy = new RequestPolicySettings(7, 2),
            Notifications = AppSettings.Default.Notifications with
            {
                TaskEventAlerts = TaskEventAlertSettings.Default
            }
        }, AppJson.Default);

        Assert.DoesNotContain("customApiOverridesEnabled", json);
        Assert.DoesNotContain("advancedMode", json);
        Assert.DoesNotContain("cookieExpiryAlerts", json);
        Assert.DoesNotContain("notificationsEnabled", json);
        Assert.DoesNotContain("apiTimeoutSeconds", json);
        Assert.DoesNotContain("themeMode", json);
    }

    private static AppSettings MigrateAndDeserialize(string json)
    {
        var migratedJson = MigrateLegacyAppSettingsJson(json);
        return Assert.IsType<AppSettings>(JsonSerializer.Deserialize<AppSettings>(migratedJson, AppJson.Default));
    }

    private static string MigrateLegacyAppSettingsJson(string json)
    {
        var method = typeof(SqliteSettingsRepository).GetMethod(
            "MigrateLegacyAppSettingsJson",
            BindingFlags.Static | BindingFlags.NonPublic);

        return Assert.IsType<string>(method?.Invoke(null, [json]));
    }
}
