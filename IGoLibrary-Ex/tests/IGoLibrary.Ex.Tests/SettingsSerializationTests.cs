using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
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
        Assert.False(settings.MobileControl.AutoStart);
        Assert.Empty(settings.RemoteCheckIn.VenueProfiles);
    }

    [Fact]
    public void RemoteCheckInProfiles_RoundTripAndNormalizeInvalidValues()
    {
        var settings = Normalize(AppSettings.Default with
        {
            RemoteCheckIn = new RemoteCheckInSettings
            {
                VenueProfiles =
                [
                    new RemoteCheckInVenueProfileSettings
                    {
                        LibraryId = 7,
                        LibraryName = " 测试馆 ",
                        BeaconUuid = "e2c56db5-dffb-48d2-b060-d0f5a71096e0",
                        Major = 10001,
                        Minor = 20002,
                        Latitude = 39.908722m,
                        Longitude = 116.397499m
                    },
                    new RemoteCheckInVenueProfileSettings
                    {
                        LibraryId = 8,
                        LibraryName = "坏配置",
                        BeaconUuid = "invalid",
                        Major = 70000,
                        Minor = -1,
                        Latitude = 91m,
                        Longitude = -181m
                    }
                ]
            }
        });

        var valid = Assert.Single(settings.RemoteCheckIn.VenueProfiles, profile => profile.LibraryId == 7);
        Assert.Equal("测试馆", valid.LibraryName);
        Assert.Equal("E2C56DB5-DFFB-48D2-B060-D0F5A71096E0", valid.BeaconUuid);
        var invalid = Assert.Single(settings.RemoteCheckIn.VenueProfiles, profile => profile.LibraryId == 8);
        Assert.Equal(string.Empty, invalid.BeaconUuid);
        Assert.Null(invalid.Major);
        Assert.Null(invalid.Minor);
        Assert.Null(invalid.Latitude);
        Assert.Null(invalid.Longitude);

        var json = JsonSerializer.Serialize(settings, AppJson.Default);
        Assert.Contains("remoteCheckIn", json);
        Assert.DoesNotContain("wechatSESS_ID", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CanonicalSettingsWithoutRemoteCheckIn_MigratesEmptySection()
    {
        var migrated = MigrateLegacyAppSettingsJson(
            """
            {
              "notifications": {},
              "traceIntProtocol": { "graphQlOverridesEnabled": false },
              "network": { "timeoutSeconds": 5, "maxRetries": 3 },
              "tasks": {
                "grab": {}, "occupy": {}, "autoRelease": {},
                "tomorrowReservation": {}, "globalLeak": {}
              },
              "updates": {},
              "mobileControl": {}
            }
            """);
        using var document = JsonDocument.Parse(migrated);

        Assert.Equal(
            JsonValueKind.Array,
            document.RootElement.GetProperty("remoteCheckIn").GetProperty("venueProfiles").ValueKind);
    }

    [Fact]
    public void UpdateEtagVersionMigration_PreservesBinding_AndLeavesLegacyEtagUnbound()
    {
        var bound = MigrateAndDeserialize(
            """
            {
              "updates": {
                "lastReleaseETag": "\"etag\"",
                "lastReleaseETagVersion": "1.0.0"
              }
            }
            """);
        var legacy = MigrateAndDeserialize(
            """
            {
              "updates": {
                "lastReleaseETag": "\"legacy\""
              }
            }
            """);

        Assert.Equal("\"etag\"", bound.Updates.LastReleaseETag);
        Assert.Equal("1.0.0", bound.Updates.LastReleaseETagVersion);
        Assert.Equal("\"legacy\"", legacy.Updates.LastReleaseETag);
        Assert.Null(legacy.Updates.LastReleaseETagVersion);
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
        Assert.Equal(WxPusherAlertChannelSettings.Default, alerts.WxPusher);
        Assert.Equal(ServerChanAlertChannelSettings.Default, alerts.ServerChan);
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
        Assert.Contains("\"cookieExpiring\": true", migratedJson);
    }

    [Fact]
    public void PreviousCanonicalJsonMissingOnlyCookieExpiring_BackfillsEnabledDefault()
    {
        var currentJson = JsonSerializer.Serialize(AppSettings.Default, AppJson.Default);
        var previousJson = currentJson.Replace(
            "\"cookieExpiring\": true,",
            string.Empty,
            StringComparison.Ordinal);
        Assert.DoesNotContain("\"cookieExpiring\"", previousJson, StringComparison.Ordinal);

        var migratedJson = MigrateLegacyAppSettingsJson(previousJson);

        Assert.Contains("\"cookieExpiring\": true", migratedJson, StringComparison.Ordinal);
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
        Assert.True(settings.Tasks.Grab.OptimalStrategyReminderEnabled);
        Assert.Equal(TimeSpan.Zero, settings.Tasks.Grab.DefaultScheduledStartTime);
        Assert.Equal(4, settings.Tasks.Occupy.ReReservationMaxAttempts);
        Assert.False(settings.Tasks.AutoRelease.Enabled);
        Assert.Equal(AutoReleaseTaskSettings.DefaultLeadSeconds, settings.Tasks.AutoRelease.LeadSeconds);
        Assert.Equal(new TimeSpan(20, 0, 0), settings.Tasks.TomorrowReservation.DefaultScheduledStartTime);
        Assert.Empty(settings.Tasks.GlobalLeak.SelectedLibraries);
        Assert.True(settings.Updates.CheckOnStartup);
        Assert.Equal(0, settings.MobileControl.Port);
        Assert.Equal(string.Empty, settings.MobileControl.AccessToken);
        Assert.False(settings.MobileControl.AutoStart);
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
        Assert.Contains("\"windowSize\":", json);
        Assert.Contains("\"rememberSize\": false", json);
        Assert.Contains("\"homeReservationProgress\":", json);
        Assert.Contains("\"fixedDurationMinutes\": 30", json);
        Assert.Contains("\"homeCookieProgress\":", json);
        Assert.Contains("\"fixedDurationMinutes\": 120", json);
        Assert.Contains("\"traceIntProtocol\":", json);
        Assert.Contains("\"network\":", json);
        Assert.Contains("\"tasks\":", json);
        Assert.Contains("\"grab\":", json);
        Assert.Contains("\"optimalStrategyReminderEnabled\": true", json);
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
        Assert.Contains("\"wxPusher\":", json);
        Assert.Contains("\"apiBaseUrl\": \"https://wxpusher.zjiecode.com\"", json);
        Assert.Contains("\"appToken\": \"\"", json);
        Assert.Contains("\"serverChan\":", json);
        Assert.Contains("\"sendKey\": \"\"", json);
        Assert.Contains("\"noIp\": false", json);
        Assert.Contains("\"events\":", json);
        Assert.Contains("\"grabSucceeded\": true", json);
        Assert.Contains("\"occupyReReserveSucceeded\": true", json);
        Assert.Contains("\"tomorrowReservationSucceeded\": true", json);
        Assert.Contains("\"globalLeakSucceeded\": true", json);
        Assert.Contains("\"sessionInvalid\": true", json);
        Assert.Contains("\"taskFailed\": true", json);
        Assert.DoesNotContain("appBannerNotificationsEnabled", json);
        Assert.Contains("\"graphQlOverridesEnabled\": true", json);
        Assert.Contains("\"autoStart\": false", json);
    }

    [Fact]
    public void WindowSizePreferences_DefaultToDisabledWithoutStoredDimensions()
    {
        var windowSize = Assert.IsType<MainViewSizePreferences>(AppSettings.Default.Ui.MainViewSize);

        Assert.False(windowSize.RememberSize);
        Assert.Null(windowSize.ClientWidth);
        Assert.Null(windowSize.ClientHeight);
    }

    [Fact]
    public void SerializedDefaultSettings_AreAlreadyCanonical()
    {
        var json = JsonSerializer.Serialize(AppSettings.Default, AppJson.Default);

        var migratedJson = MigrateLegacyAppSettingsJson(json);

        Assert.Equal(json, migratedJson);
    }

    [Fact]
    public void AppSettingsSerialization_PreservesExplicitWindowSizePreferences()
    {
        var json = JsonSerializer.Serialize(AppSettings.Default with
        {
            Ui = AppSettings.Default.Ui with
            {
                MainViewSize = new MainViewSizePreferences(true, 1280.25, 760.5)
            }
        }, AppJson.Default);

        var settings = Assert.IsType<AppSettings>(JsonSerializer.Deserialize<AppSettings>(json, AppJson.Default));
        var windowSize = Assert.IsType<MainViewSizePreferences>(settings.Ui.MainViewSize);

        Assert.True(windowSize.RememberSize);
        Assert.Equal(1280.25, windowSize.ClientWidth);
        Assert.Equal(760.5, windowSize.ClientHeight);
    }

    [Fact]
    public void CanonicalJsonWithoutWindowSize_RewritesWithDisabledDefaults()
    {
        var root = Assert.IsType<JsonObject>(JsonNode.Parse(
            JsonSerializer.Serialize(AppSettings.Default, AppJson.Default)));
        Assert.True(Assert.IsType<JsonObject>(root["ui"]).Remove("windowSize"));

        var migratedJson = MigrateLegacyAppSettingsJson(root.ToJsonString(AppJson.Default));
        var settings = Assert.IsType<AppSettings>(
            JsonSerializer.Deserialize<AppSettings>(migratedJson, AppJson.Default));
        var windowSize = Assert.IsType<MainViewSizePreferences>(settings.Ui.MainViewSize);

        Assert.False(windowSize.RememberSize);
        Assert.Null(windowSize.ClientWidth);
        Assert.Null(windowSize.ClientHeight);
        Assert.Contains("\"windowSize\":", migratedJson);
    }

    [Fact]
    public void AppSettingsMigration_DropsPartiallyStoredWindowSizePair()
    {
        var root = Assert.IsType<JsonObject>(JsonNode.Parse(
            JsonSerializer.Serialize(AppSettings.Default, AppJson.Default)));
        var windowSize = Assert.IsType<JsonObject>(root["ui"]?["windowSize"]);
        windowSize["rememberSize"] = true;
        windowSize["clientWidth"] = 1200d;

        var migratedJson = MigrateLegacyAppSettingsJson(root.ToJsonString(AppJson.Default));
        var settings = Assert.IsType<AppSettings>(
            JsonSerializer.Deserialize<AppSettings>(migratedJson, AppJson.Default));
        var migratedWindowSize = Assert.IsType<MainViewSizePreferences>(settings.Ui.MainViewSize);

        Assert.True(migratedWindowSize.RememberSize);
        Assert.Null(migratedWindowSize.ClientWidth);
        Assert.Null(migratedWindowSize.ClientHeight);
        Assert.Contains("\"clientWidth\": null", migratedJson);
        Assert.Contains("\"clientHeight\": null", migratedJson);
    }

    [Theory]
    [InlineData(-1d, 800d)]
    [InlineData(0d, 800d)]
    [InlineData(1200d, 0d)]
    [InlineData(double.NaN, 800d)]
    [InlineData(1200d, double.PositiveInfinity)]
    public void AppSettingsNormalization_DropsInvalidWindowSizePairs(double width, double height)
    {
        var settings = Normalize(AppSettings.Default with
        {
            Ui = AppSettings.Default.Ui with
            {
                MainViewSize = new MainViewSizePreferences(true, width, height)
            }
        });
        var windowSize = Assert.IsType<MainViewSizePreferences>(settings.Ui.MainViewSize);

        Assert.True(windowSize.RememberSize);
        Assert.Null(windowSize.ClientWidth);
        Assert.Null(windowSize.ClientHeight);
    }

    [Fact]
    public void AppSettingsNormalization_RoundsValidWindowSizeToTwoDecimals()
    {
        var settings = Normalize(AppSettings.Default with
        {
            Ui = AppSettings.Default.Ui with
            {
                MainViewSize = new MainViewSizePreferences(true, 1200.126, 800.125)
            }
        });
        var windowSize = Assert.IsType<MainViewSizePreferences>(settings.Ui.MainViewSize);

        Assert.Equal(1200.13, windowSize.ClientWidth);
        Assert.Equal(800.13, windowSize.ClientHeight);
    }

    [Fact]
    public void GrabOptimalStrategyReminderMigration_DefaultsToEnabledAndPreservesExplicitDisabledValue()
    {
        var canonical = Assert.IsType<JsonObject>(JsonNode.Parse(
            JsonSerializer.Serialize(AppSettings.Default, AppJson.Default)));
        var grab = Assert.IsType<JsonObject>(canonical["tasks"]?["grab"]);
        Assert.True(grab.Remove("optimalStrategyReminderEnabled"));

        var migratedJson = MigrateLegacyAppSettingsJson(canonical.ToJsonString());
        var missingSetting = Assert.IsType<AppSettings>(
            JsonSerializer.Deserialize<AppSettings>(migratedJson, AppJson.Default));
        var explicitlyDisabled = MigrateAndDeserialize(JsonSerializer.Serialize(
            AppSettings.Default with
            {
                Tasks = AppSettings.Default.Tasks with
                {
                    Grab = AppSettings.Default.Tasks.Grab with
                    {
                        OptimalStrategyReminderEnabled = false
                    }
                }
            },
            AppJson.Default));

        Assert.Contains("\"optimalStrategyReminderEnabled\": true", migratedJson);
        Assert.True(missingSetting.Tasks.Grab.OptimalStrategyReminderEnabled);
        Assert.False(explicitlyDisabled.Tasks.Grab.OptimalStrategyReminderEnabled);
    }

    [Fact]
    public void AppSettingsSerialization_PreservesExplicitMobileControlAutoStart()
    {
        var json = JsonSerializer.Serialize(AppSettings.Default with
        {
            MobileControl = new MobileControlSettings(9527, "token", true)
        }, AppJson.Default);

        var settings = Assert.IsType<AppSettings>(JsonSerializer.Deserialize<AppSettings>(json, AppJson.Default));

        Assert.Equal(9527, settings.MobileControl.Port);
        Assert.Equal("token", settings.MobileControl.AccessToken);
        Assert.True(settings.MobileControl.AutoStart);
        Assert.Contains("\"autoStart\": true", json);
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
        Assert.Equal(WxPusherAlertChannelSettings.Default, alerts.WxPusher);
        Assert.Equal(ServerChanAlertChannelSettings.Default, alerts.ServerChan);
        Assert.Contains("\"events\":", migratedJson);
        Assert.Contains("\"bark\":", migratedJson);
        Assert.Contains("\"apiBaseUrl\": \"https://api.day.app\"", migratedJson);
        Assert.Contains("\"wxPusher\":", migratedJson);
        Assert.Contains("\"apiBaseUrl\": \"https://wxpusher.zjiecode.com\"", migratedJson);
        Assert.Contains("\"serverChan\":", migratedJson);
        Assert.Contains("\"sendKey\": \"\"", migratedJson);
        Assert.Contains("\"cookieExpiring\": true", migratedJson);
        Assert.Contains("\"taskFailed\": true", migratedJson);
    }

    [Fact]
    public void AppSettingsSerialization_PreservesCompleteExplicitNotificationSettings()
    {
        var expectedEvents = TaskEventAlertEventSettings.Default with
        {
            CookieExpiring = false,
            GrabSucceeded = false,
            OccupyReReserveSucceeded = false,
            TomorrowReservationSucceeded = false,
            GlobalLeakSucceeded = false,
            SessionInvalid = false,
            TaskFailed = false
        };
        var expectedBark = new BarkAlertChannelSettings(
            true,
            "https://bark.example.com",
            "key-1",
            "IGoLibrary-Ex",
            "alarm",
            "timeSensitive");
        var expectedWxPusher = new WxPusherAlertChannelSettings(
            true,
            "https://wxpusher.example.com",
            "AT_xxx",
            "UID_1,UID_2",
            "1;2");
        var expectedServerChan = new ServerChanAlertChannelSettings(
            true,
            "SCT_xxx",
            true,
            "9|66",
            "user-1");
        var json = JsonSerializer.Serialize(AppSettings.Default with
        {
            Notifications = AppSettings.Default.Notifications with
            {
                TaskEventAlerts = new TaskEventAlertSettings(
                    EmailAlertChannelSettings.Default,
                    LocalDesktopAlertSettings.Default,
                    TelegramAlertChannelSettings.Default,
                    expectedEvents,
                    expectedBark,
                    expectedWxPusher,
                    expectedServerChan)
            }
        }, AppJson.Default);

        var settings = Assert.IsType<AppSettings>(JsonSerializer.Deserialize<AppSettings>(json, AppJson.Default));
        var alerts = Assert.IsType<TaskEventAlertSettings>(settings.Notifications.TaskEventAlerts);
        Assert.Equal(expectedEvents, alerts.Events);
        Assert.Equal(expectedBark, alerts.Bark);
        Assert.Equal(expectedWxPusher, alerts.WxPusher);
        Assert.Equal(expectedServerChan, alerts.ServerChan);
        Assert.Contains("\"cookieExpiring\": false", json);
        Assert.Contains("\"grabSucceeded\": false", json);
        Assert.Contains("\"occupyReReserveSucceeded\": false", json);
        Assert.Contains("\"tomorrowReservationSucceeded\": false", json);
        Assert.Contains("\"globalLeakSucceeded\": false", json);
        Assert.Contains("\"sessionInvalid\": false", json);
        Assert.Contains("\"taskFailed\": false", json);
        Assert.Contains("\"bark\":", json);
        Assert.Contains("\"deviceKey\": \"key-1\"", json);
        Assert.Contains("\"level\": \"timeSensitive\"", json);
        Assert.Contains("\"wxPusher\":", json);
        Assert.Contains("\"appToken\": \"AT_xxx\"", json);
        Assert.Contains("\"uids\": \"UID_1,UID_2\"", json);
        Assert.Contains("\"topicIds\": \"1;2\"", json);
        Assert.Contains("\"serverChan\":", json);
        Assert.Contains("\"sendKey\": \"SCT_xxx\"", json);
        Assert.Contains("\"noIp\": true", json);
        Assert.Contains("\"channel\": \"9|66\"", json);
        Assert.Contains("\"openId\": \"user-1\"", json);
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
        Assert.Contains("\"autoStart\": false", migratedJson);
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
                    new GlobalLeakLibrarySelectionSettings(2, "场馆B", "5层"),
                    new GlobalLeakLibrarySelectionSettings(1, "场馆A", "3层"),
                    new GlobalLeakLibrarySelectionSettings(3, "场馆C", "7层")
                ])
            }
        }, AppJson.Default);

        var settings = Assert.IsType<AppSettings>(JsonSerializer.Deserialize<AppSettings>(json, AppJson.Default));

        Assert.Equal([2, 1, 3], settings.Tasks.GlobalLeak.SelectedLibraries.Select(x => x.LibraryId).ToArray());
        Assert.Equal(["场馆B", "场馆A", "场馆C"], settings.Tasks.GlobalLeak.SelectedLibraries.Select(x => x.LibraryName).ToArray());
        Assert.Equal(["5层", "3层", "7层"], settings.Tasks.GlobalLeak.SelectedLibraries.Select(x => x.Floor).ToArray());
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

    [Fact]
    public void LoggingSettings_DefaultsToEnabledAndThirtyFiles()
    {
        Assert.True(AppSettings.Default.Logging.Enabled);
        Assert.Equal(30, AppSettings.Default.Logging.RetainedFileCount);
    }

    [Fact]
    public void LegacySettingsWithoutLogging_MigrateWithLoggingDefaults()
    {
        var settings = MigrateAndDeserialize("{}");

        Assert.Equal(LogFileSettings.Default, settings.Logging);
    }

    [Fact]
    public void LoggingSettingsWithMissingCount_PreserveEnabledAndDefaultTheCount()
    {
        var settings = MigrateAndDeserialize("""
            {
              "logging": {
                "enabled": false
              }
            }
            """);

        Assert.Equal(new LogFileSettings(false, 30), settings.Logging);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-10, 1)]
    [InlineData(366, 365)]
    [InlineData(10000, 365)]
    public void LoggingSettings_NormalizeRetainedFileCount(int value, int expected)
    {
        var settings = Normalize(AppSettings.Default with
        {
            Logging = new LogFileSettings(false, value)
        });

        Assert.False(settings.Logging.Enabled);
        Assert.Equal(expected, settings.Logging.RetainedFileCount);
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
