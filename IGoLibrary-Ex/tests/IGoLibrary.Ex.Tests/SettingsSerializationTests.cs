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
        Assert.Equal(TelegramAlertSettings.Default, settings.CookieExpiryAlerts?.Telegram);
    }
}
