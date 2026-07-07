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
        Assert.Equal(HomeReservationProgressTimingMode.FixedReservationDuration, settings.Ui.HomeReservationProgress?.Mode);
        Assert.Equal(30, settings.Ui.HomeReservationProgress?.FixedDurationMinutes);
        Assert.Equal(HomeCookieProgressTimingMode.FixedCookieDuration, settings.Ui.HomeCookieProgress?.Mode);
        Assert.Equal(120, settings.Ui.HomeCookieProgress?.FixedDurationMinutes);
        Assert.True(settings.TraceIntProtocol.GraphQlOverridesEnabled);
        Assert.Equal(9, settings.Network.TimeoutSeconds);
        Assert.Equal(4, settings.Network.MaxRetries);
        Assert.Equal(GrabReservationStrategy.ReserveDirectly, settings.Tasks.Grab.ReservationStrategy);
        Assert.Equal(TimeSpan.Zero, settings.Tasks.Grab.DefaultScheduledStartTime);
        Assert.Equal(5, settings.Tasks.Occupy.ReReservationMaxAttempts);
        Assert.False(settings.Tasks.AutoRelease.Enabled);
        Assert.Equal(AutoReleaseTaskSettings.DefaultLeadSeconds, settings.Tasks.AutoRelease.LeadSeconds);
        Assert.Equal(new TimeSpan(20, 0, 0), settings.Tasks.TomorrowReservation.DefaultScheduledStartTime);
        Assert.Equal(12, settings.Venue.LastLibraryId);
        Assert.Equal("自科阅览区一", settings.Venue.LastLibraryName);
        Assert.Equal(6, settings.Dashboard.SuccessfulReservationCount);
        Assert.Equal(3600, settings.Dashboard.TotalGuardSeconds);
        Assert.True(settings.Updates.CheckOnStartup);
        Assert.Equal(0, settings.MobileControl.Port);
        Assert.Equal(string.Empty, settings.MobileControl.AccessToken);
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
        Assert.Equal(BarkAlertChannelSettings.Default, alerts.Bark);
        Assert.Equal(TaskEventAlertEventSettings.Default, alerts.Events);
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
        Assert.Equal(TaskEventAlertEventSettings.Default, alerts.Events);
        Assert.DoesNotContain("toastEnabled", migratedJson);
        Assert.DoesNotContain("appBannerNotificationsEnabled", migratedJson);
        Assert.Contains("\"popupEnabled\": true", migratedJson);
        Assert.Contains("\"events\":", migratedJson);
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
        Assert.Equal(HomeReservationProgressTimingMode.FixedReservationDuration, settings.Ui.HomeReservationProgress?.Mode);
        Assert.Equal(30, settings.Ui.HomeReservationProgress?.FixedDurationMinutes);
        Assert.Equal(HomeCookieProgressTimingMode.FixedCookieDuration, settings.Ui.HomeCookieProgress?.Mode);
        Assert.Equal(120, settings.Ui.HomeCookieProgress?.FixedDurationMinutes);
        Assert.Equal(5, settings.Network.TimeoutSeconds);
        Assert.Equal(3, settings.Network.MaxRetries);
        Assert.Equal(GrabReservationStrategy.QueryThenReserve, settings.Tasks.Grab.ReservationStrategy);
        Assert.Equal(TimeSpan.Zero, settings.Tasks.Grab.DefaultScheduledStartTime);
        Assert.Equal(4, settings.Tasks.Occupy.ReReservationMaxAttempts);
        Assert.False(settings.Tasks.AutoRelease.Enabled);
        Assert.Equal(AutoReleaseTaskSettings.DefaultLeadSeconds, settings.Tasks.AutoRelease.LeadSeconds);
        Assert.Equal(new TimeSpan(20, 0, 0), settings.Tasks.TomorrowReservation.DefaultScheduledStartTime);
        Assert.Empty(settings.Tasks.GlobalLeak.SelectedLibraries);
        Assert.True(settings.Updates.CheckOnStartup);
        Assert.Equal(0, settings.MobileControl.Port);
        Assert.Equal(string.Empty, settings.MobileControl.AccessToken);
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
        Assert.Contains("\"homeReservationProgress\":", json);
        Assert.Contains("\"fixedDurationMinutes\": 30", json);
        Assert.Contains("\"homeCookieProgress\":", json);
        Assert.Contains("\"fixedDurationMinutes\": 120", json);
        Assert.Contains("\"traceIntProtocol\":", json);
        Assert.Contains("\"network\":", json);
        Assert.Contains("\"tasks\":", json);
        Assert.Contains("\"grab\":", json);
        Assert.Contains("\"occupy\":", json);
        Assert.Contains("\"autoRelease\":", json);
        Assert.Contains("\"leadSeconds\": 60", json);
        Assert.Contains("\"tomorrowReservation\":", json);
        Assert.Contains("\"globalLeak\":", json);
        Assert.Contains("\"selectedLibraries\":", json);
        Assert.Contains("\"defaultScheduledStartTime\":", json);
        Assert.Contains("\"venue\":", json);
        Assert.Contains("\"dashboard\":", json);
        Assert.Contains("\"updates\":", json);
        Assert.Contains("\"mobileControl\":", json);
        Assert.Contains("\"taskEventAlerts\":", json);
        Assert.Contains("\"bark\":", json);
        Assert.Contains("\"apiBaseUrl\": \"https://api.day.app\"", json);
        Assert.Contains("\"deviceKey\": \"\"", json);
        Assert.Contains("\"events\":", json);
        Assert.Contains("\"grabSucceeded\": true", json);
        Assert.Contains("\"occupyReReserveSucceeded\": true", json);
        Assert.Contains("\"tomorrowReservationSucceeded\": true", json);
        Assert.Contains("\"globalLeakSucceeded\": true", json);
        Assert.Contains("\"sessionInvalid\": true", json);
        Assert.Contains("\"taskFailed\": true", json);
        Assert.DoesNotContain("appBannerNotificationsEnabled", json);
        Assert.Contains("\"graphQlOverridesEnabled\": true", json);
    }

    [Fact]
    public void CanonicalJsonWithoutEventSettings_RewritesWithDefaultEventSettings()
    {
        var migratedJson = MigrateLegacyAppSettingsJson(
            """
            {
              "notifications": {
                "taskEventAlerts": {
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
                    "popupEnabled": true,
                    "soundEnabled": false
                  },
                  "telegram": {
                    "enabled": false,
                    "apiBaseUrl": "https://api.telegram.org",
                    "botToken": "",
                    "chatId": ""
                  }
                }
              },
              "ui": {
                "minimizeToTray": true,
                "theme": {
                  "mode": 0,
                  "useSystemAccent": true
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
                  "reservationStrategy": 0,
                  "defaultScheduledStartTime": "00:00:00"
                },
                "occupy": {
                  "reReservationMaxAttempts": 4
                },
                "autoRelease": {
                  "enabled": false,
                  "leadSeconds": 60
                },
                "tomorrowReservation": {
                  "defaultScheduledStartTime": "20:00:00"
                },
                "globalLeak": {
                  "selectedLibraries": []
                }
              },
              "venue": {},
              "dashboard": {},
              "updates": {
                "checkOnStartup": true
              }
            }
            """);

        var settings = Assert.IsType<AppSettings>(JsonSerializer.Deserialize<AppSettings>(migratedJson, AppJson.Default));
        var alerts = Assert.IsType<TaskEventAlertSettings>(settings.Notifications.TaskEventAlerts);
        Assert.Equal(TaskEventAlertEventSettings.Default, alerts.Events);
        Assert.Equal(BarkAlertChannelSettings.Default, alerts.Bark);
        Assert.Contains("\"events\":", migratedJson);
        Assert.Contains("\"bark\":", migratedJson);
        Assert.Contains("\"apiBaseUrl\": \"https://api.day.app\"", migratedJson);
        Assert.Contains("\"taskFailed\": true", migratedJson);
    }

    [Fact]
    public void AppSettingsSerialization_PreservesExplicitEventSettings()
    {
        var json = JsonSerializer.Serialize(AppSettings.Default with
        {
            Notifications = AppSettings.Default.Notifications with
            {
                TaskEventAlerts = new TaskEventAlertSettings(
                    EmailAlertChannelSettings.Default,
                    LocalDesktopAlertSettings.Default,
                    TelegramAlertChannelSettings.Default,
                    TaskEventAlertEventSettings.Default with
                    {
                        GrabSucceeded = false,
                        SessionInvalid = false
                    })
            }
        }, AppJson.Default);

        var settings = Assert.IsType<AppSettings>(JsonSerializer.Deserialize<AppSettings>(json, AppJson.Default));
        var events = Assert.IsType<TaskEventAlertEventSettings>(settings.Notifications.TaskEventAlerts?.Events);
        Assert.False(events.GrabSucceeded);
        Assert.True(events.OccupyReReserveSucceeded);
        Assert.True(events.TomorrowReservationSucceeded);
        Assert.True(events.GlobalLeakSucceeded);
        Assert.False(events.SessionInvalid);
        Assert.True(events.TaskFailed);
        Assert.Contains("\"grabSucceeded\": false", json);
        Assert.Contains("\"sessionInvalid\": false", json);
    }

    [Fact]
    public void AppSettingsSerialization_PreservesExplicitBarkSettings()
    {
        var json = JsonSerializer.Serialize(AppSettings.Default with
        {
            Notifications = AppSettings.Default.Notifications with
            {
                TaskEventAlerts = new TaskEventAlertSettings(
                    EmailAlertChannelSettings.Default,
                    LocalDesktopAlertSettings.Default,
                    TelegramAlertChannelSettings.Default,
                    TaskEventAlertEventSettings.Default,
                    new BarkAlertChannelSettings(
                        true,
                        "https://bark.example.com",
                        "key-1",
                        "IGoLibrary-Ex",
                        "alarm",
                        "timeSensitive"))
            }
        }, AppJson.Default);

        var settings = Assert.IsType<AppSettings>(JsonSerializer.Deserialize<AppSettings>(json, AppJson.Default));
        var bark = Assert.IsType<BarkAlertChannelSettings>(settings.Notifications.TaskEventAlerts?.Bark);
        Assert.True(bark.Enabled);
        Assert.Equal("https://bark.example.com", bark.ApiBaseUrl);
        Assert.Equal("key-1", bark.DeviceKey);
        Assert.Equal("IGoLibrary-Ex", bark.Group);
        Assert.Equal("alarm", bark.Sound);
        Assert.Equal("timeSensitive", bark.Level);
        Assert.Contains("\"bark\":", json);
        Assert.Contains("\"deviceKey\": \"key-1\"", json);
        Assert.Contains("\"level\": \"timeSensitive\"", json);
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
        Assert.Contains("\"mobileControl\":", migratedJson);
        Assert.Contains("\"autoRelease\":", migratedJson);
        Assert.Contains("\"leadSeconds\": 60", migratedJson);
        Assert.Contains("\"globalLeak\":", migratedJson);
        Assert.Contains("\"selectedLibraries\": []", migratedJson);
    }

    [Theory]
    [InlineData(0, AutoReleaseTaskSettings.MinLeadSeconds)]
    [InlineData(4000, AutoReleaseTaskSettings.MaxLeadSeconds)]
    public void AppSettingsNormalization_ClampsAutoReleaseLeadSeconds(
        int leadSeconds,
        int expectedLeadSeconds)
    {
        var settings = Normalize(AppSettings.Default with
        {
            Tasks = AppSettings.Default.Tasks with
            {
                AutoRelease = AutoReleaseTaskSettings.Default with
                {
                    Enabled = true,
                    LeadSeconds = leadSeconds
                }
            }
        });

        Assert.True(settings.Tasks.AutoRelease.Enabled);
        Assert.Equal(expectedLeadSeconds, settings.Tasks.AutoRelease.LeadSeconds);
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

    private static AppSettings Normalize(AppSettings settings)
    {
        var method = typeof(SqliteSettingsRepository).GetMethod(
            "Normalize",
            BindingFlags.Static | BindingFlags.NonPublic);

        return Assert.IsType<AppSettings>(method?.Invoke(null, [settings]));
    }
}
