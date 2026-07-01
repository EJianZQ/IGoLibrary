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

        Assert.False(settings.Ui.MinimizeToTray);
        Assert.Equal(AppThemeMode.Dark, settings.Ui.Theme?.Mode);
        Assert.False(settings.Ui.Theme?.UseSystemAccent);
        Assert.True(settings.TraceIntProtocol.GraphQlOverridesEnabled);
        Assert.Equal(9, settings.Network.TimeoutSeconds);
        Assert.Equal(4, settings.Network.MaxRetries);
        Assert.Equal(GrabReservationStrategy.ReserveDirectly, settings.Tasks.Grab.ReservationStrategy);
        Assert.Equal(TimeSpan.Zero, settings.Tasks.Grab.DefaultScheduledStartTime);
        Assert.Equal(5, settings.Tasks.Occupy.ReReservationMaxAttempts);
        Assert.Equal(new TimeSpan(20, 0, 0), settings.Tasks.TomorrowReservation.DefaultScheduledStartTime);
        Assert.Equal(12, settings.Venue.LastLibraryId);
        Assert.Equal("自科阅览区一", settings.Venue.LastLibraryName);
        Assert.Equal(6, settings.Dashboard.SuccessfulReservationCount);
        Assert.Equal(3600, settings.Dashboard.TotalGuardSeconds);
        Assert.True(settings.Updates.CheckOnStartup);
    }

    [Fact]
    public void LegacyAdvancedModeMigration_MigratesToTraceIntProtocolSettings()
    {
        const string json = """{"advancedMode":true}""";

        var migratedJson = MigrateLegacyAppSettingsJson(json);
        using var document = JsonDocument.Parse(migratedJson);

        Assert.True(document.RootElement.GetProperty("traceIntProtocol").GetProperty("graphQlOverridesEnabled").GetBoolean());
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
        Assert.True(alerts.Local.PopupEnabled);
        Assert.False(alerts.Local.SoundEnabled);
        Assert.Equal(TelegramAlertChannelSettings.Default, alerts.Telegram);
    }

    [Fact]
    public void CanonicalJsonWithLegacyToastEnabled_RewritesToPopupEnabled()
    {
        var migratedJson = MigrateLegacyAppSettingsJson(
            """
            {
              "notifications": {
                "appBannerNotificationsEnabled": true,
                "taskEventAlerts": {
                  "local": {
                    "toastEnabled": true,
                    "soundEnabled": false
                  }
                }
              },
              "traceIntProtocol": {
                "graphQlOverridesEnabled": false
              },
              "network": {
                "timeoutSeconds": 5,
                "maxRetries": 3
              },
              "tasks": {
                "grab": {
                  "reservationStrategy": 0
                },
                "occupy": {
                  "reReservationMaxAttempts": 4
                }
              }
            }
            """);

        var settings = Assert.IsType<AppSettings>(JsonSerializer.Deserialize<AppSettings>(migratedJson, AppJson.Default));
        var alerts = Assert.IsType<TaskEventAlertSettings>(settings.Notifications.TaskEventAlerts);
        Assert.True(alerts.Local.PopupEnabled);
        Assert.False(alerts.Local.SoundEnabled);
        Assert.DoesNotContain("toastEnabled", migratedJson);
        Assert.DoesNotContain("appBannerNotificationsEnabled", migratedJson);
        Assert.Contains("\"popupEnabled\": true", migratedJson);
    }

    [Fact]
    public void CanonicalJsonWithLegacyRequestPolicyRetryCount_RewritesNetworkAndOccupyAttempts()
    {
        var migratedJson = MigrateLegacyAppSettingsJson(
            """
            {
              "notifications": {},
              "traceIntProtocol": {
                "graphQlOverridesEnabled": false
              },
              "network": {
                "timeoutSeconds": 5
              },
              "requestPolicy": {
                "retryCount": 2
              },
              "tasks": {
                "grab": {
                  "reservationStrategy": 0
                },
                "occupy": {}
              }
            }
            """);

        var settings = Assert.IsType<AppSettings>(JsonSerializer.Deserialize<AppSettings>(migratedJson, AppJson.Default));
        Assert.Equal(2, settings.Network.MaxRetries);
        Assert.Equal(3, settings.Tasks.Occupy.ReReservationMaxAttempts);
        Assert.DoesNotContain("requestPolicy", migratedJson);
        Assert.DoesNotContain("retryCount", migratedJson);
        Assert.Contains("\"maxRetries\": 2", migratedJson);
        Assert.Contains("\"reReservationMaxAttempts\": 3", migratedJson);
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
              "network": {},
              "tasks": {}
            }
            """,
            AppJson.Default));

        Assert.Equal(TaskEventAlertSettings.Default, settings.Notifications.TaskEventAlerts);
        Assert.True(settings.Ui.MinimizeToTray);
        Assert.Equal(AppThemeMode.FollowSystem, settings.Ui.Theme?.Mode);
        Assert.Equal(ThemePreferences.Default.UseSystemAccent, settings.Ui.Theme?.UseSystemAccent);
        Assert.Equal(5, settings.Network.TimeoutSeconds);
        Assert.Equal(3, settings.Network.MaxRetries);
        Assert.Equal(GrabReservationStrategy.QueryThenReserve, settings.Tasks.Grab.ReservationStrategy);
        Assert.Equal(TimeSpan.Zero, settings.Tasks.Grab.DefaultScheduledStartTime);
        Assert.Equal(4, settings.Tasks.Occupy.ReReservationMaxAttempts);
        Assert.Equal(new TimeSpan(20, 0, 0), settings.Tasks.TomorrowReservation.DefaultScheduledStartTime);
        Assert.Empty(settings.Tasks.GlobalLeak.SelectedLibraries);
        Assert.True(settings.Updates.CheckOnStartup);
    }

    [Fact]
    public void AppSettingsSerialization_WritesNestedSettingsBlocks()
    {
        var json = JsonSerializer.Serialize(AppSettings.Default with
        {
            Notifications = AppSettings.Default.Notifications with
            {
                TaskEventAlerts = TaskEventAlertSettings.Default
            },
            TraceIntProtocol = new TraceIntProtocolSettings(true)
        }, AppJson.Default);

        Assert.Contains("\"notifications\":", json);
        Assert.Contains("\"ui\":", json);
        Assert.Contains("\"traceIntProtocol\":", json);
        Assert.Contains("\"network\":", json);
        Assert.Contains("\"tasks\":", json);
        Assert.Contains("\"grab\":", json);
        Assert.Contains("\"occupy\":", json);
        Assert.Contains("\"tomorrowReservation\":", json);
        Assert.Contains("\"globalLeak\":", json);
        Assert.Contains("\"selectedLibraries\":", json);
        Assert.Contains("\"defaultScheduledStartTime\":", json);
        Assert.Contains("\"venue\":", json);
        Assert.Contains("\"dashboard\":", json);
        Assert.Contains("\"updates\":", json);
        Assert.Contains("\"taskEventAlerts\":", json);
        Assert.DoesNotContain("appBannerNotificationsEnabled", json);
        Assert.Contains("\"graphQlOverridesEnabled\": true", json);
    }

    [Fact]
    public void CanonicalJsonWithoutUpdates_RewritesWithDefaultUpdateSettings()
    {
        var migratedJson = MigrateLegacyAppSettingsJson(
            """
            {
              "notifications": {},
              "ui": {},
              "traceIntProtocol": {
                "graphQlOverridesEnabled": false
              },
              "network": {
                "timeoutSeconds": 5,
                "maxRetries": 3
              },
              "tasks": {
                "grab": {
                  "reservationStrategy": 0,
                  "defaultScheduledStartTime": "00:00:00"
                },
                "occupy": {
                  "reReservationMaxAttempts": 4
                },
                "tomorrowReservation": {
                  "defaultScheduledStartTime": "20:00:00"
                }
              },
              "venue": {},
              "dashboard": {}
            }
            """);

        Assert.Contains("\"updates\":", migratedJson);
        Assert.Contains("\"checkOnStartup\": true", migratedJson);
        Assert.Contains("\"globalLeak\":", migratedJson);
        Assert.Contains("\"selectedLibraries\": []", migratedJson);
    }

    [Fact]
    public void AppSettingsSerialization_PreservesGlobalLeakSelectedLibraries()
    {
        var json = JsonSerializer.Serialize(AppSettings.Default with
        {
            Tasks = AppSettings.Default.Tasks with
            {
                GlobalLeak = new GlobalLeakTaskSettings(
                [
                    new GlobalLeakLibrarySelectionSettings(1, "场馆A", "3层"),
                    new GlobalLeakLibrarySelectionSettings(2, "场馆B", "5层")
                ])
            }
        }, AppJson.Default);

        var settings = Assert.IsType<AppSettings>(JsonSerializer.Deserialize<AppSettings>(json, AppJson.Default));

        Assert.Equal([1, 2], settings.Tasks.GlobalLeak.SelectedLibraries.Select(x => x.LibraryId).ToArray());
        Assert.Equal(["场馆A", "场馆B"], settings.Tasks.GlobalLeak.SelectedLibraries.Select(x => x.LibraryName).ToArray());
        Assert.Equal(["3层", "5层"], settings.Tasks.GlobalLeak.SelectedLibraries.Select(x => x.Floor).ToArray());
    }

    [Fact]
    public void AppSettingsSerialization_DoesNotWriteLegacyRootFields()
    {
        var json = JsonSerializer.Serialize(AppSettings.Default with
        {
            TraceIntProtocol = new TraceIntProtocolSettings(true),
            Network = new NetworkRequestSettings(7, 2),
            Notifications = AppSettings.Default.Notifications with
            {
                TaskEventAlerts = TaskEventAlertSettings.Default
            }
        }, AppJson.Default);

        Assert.DoesNotContain("customApiOverridesEnabled", json);
        Assert.DoesNotContain("advancedMode", json);
        Assert.DoesNotContain("cookieExpiryAlerts", json);
        Assert.DoesNotContain("notificationsEnabled", json);
        Assert.DoesNotContain("appBannerNotificationsEnabled", json);
        Assert.DoesNotContain("apiTimeoutSeconds", json);
        Assert.DoesNotContain("retryCount", json);
        Assert.DoesNotContain("requestPolicy", json);
        Assert.DoesNotContain("protocol\":", json);
        Assert.DoesNotContain("templateOverridesEnabled", json);
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
            "MigrateAppSettingsJson",
            BindingFlags.Static | BindingFlags.NonPublic);

        return Assert.IsType<string>(method?.Invoke(null, [json]));
    }
}
