using System.Reflection;
using System.Text.Json;
using IGoLibrary.Ex.Domain.Models;
using IGoLibrary.Ex.Infrastructure.Persistence;

namespace IGoLibrary.Ex.Tests;

public sealed class SettingsSerializationTests
{
    [Fact]
    public void AppSettingsDeserialization_DefaultsTelegramAlerts_WhenLegacyAlertJsonOmitsTelegram()
    {
        var json =
            """
            {
              "notificationsEnabled": true,
              "minimizeToTray": true,
              "customApiOverridesEnabled": false,
              "apiTimeoutSeconds": 5,
              "retryCount": 3,
              "themeMode": 0,
              "useSystemAccent": true,
              "grabReservationStrategy": 0,
              "lastLibraryId": null,
              "lastLibraryName": null,
              "cookieExpiryAlerts": {
                "email": {
                  "enabled": false,
                  "smtpHost": "",
                  "port": 587,
                  "securityMode": 1,
                  "username": "",
                  "password": "",
                  "fromAddress": "",
                  "toAddress": ""
                },
                "local": {
                  "toastEnabled": true,
                  "soundEnabled": false
                }
              },
              "successfulReservationCount": 0,
              "totalGuardSeconds": 0
            }
            """;

        var settings = JsonSerializer.Deserialize<AppSettings>(json, AppJson.Default);

        Assert.NotNull(settings);
        Assert.Equal(TelegramAlertChannelSettings.Default, settings.TaskEventAlerts?.Telegram);
    }

    [Fact]
    public void AppSettingsSerialization_UsesLegacyPersistentPropertyNames()
    {
        var json = JsonSerializer.Serialize(AppSettings.Default with
        {
            ProtocolTemplateOverridesEnabled = true,
            TaskEventAlerts = TaskEventAlertSettings.Default
        }, AppJson.Default);

        Assert.Contains("\"customApiOverridesEnabled\": true", json);
        Assert.Contains("\"cookieExpiryAlerts\":", json);
        Assert.DoesNotContain("protocolTemplateOverridesEnabled", json);
        Assert.DoesNotContain("taskEventAlerts", json);
    }

    [Fact]
    public void LegacyAdvancedModeMigration_WritesLegacyProtocolOverridePropertyName()
    {
        const string json = """{"advancedMode":true}""";

        var method = typeof(SqliteSettingsRepository).GetMethod(
            "MigrateLegacyAppSettingsJson",
            BindingFlags.Static | BindingFlags.NonPublic);

        var migratedJson = Assert.IsType<string>(method?.Invoke(null, [json]));
        using var document = JsonDocument.Parse(migratedJson);

        Assert.True(document.RootElement.GetProperty("customApiOverridesEnabled").GetBoolean());
        Assert.False(document.RootElement.TryGetProperty("protocolTemplateOverridesEnabled", out _));
    }
}
