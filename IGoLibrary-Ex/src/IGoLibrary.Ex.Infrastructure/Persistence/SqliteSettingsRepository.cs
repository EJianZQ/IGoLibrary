using System.Text;
using System.Text.Json;
using IGoLibrary.Ex.Application.Abstractions;

namespace IGoLibrary.Ex.Infrastructure.Persistence;

public sealed class SqliteSettingsRepository(
    SqliteConnectionFactory connectionFactory,
    IAppSettingsDefaults appSettingsDefaults) : ISettingsRepository
{
    private const string SettingsKey = "app-settings";

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM Settings WHERE Key = $key;";
        command.Parameters.AddWithValue("$key", SettingsKey);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is string json && !string.IsNullOrWhiteSpace(json))
        {
            var migratedJson = MigrateAppSettingsJsonCore(json, appSettingsDefaults.CreateDefault());
            var settings = JsonSerializer.Deserialize<AppSettings>(migratedJson, AppJson.Default)
                           ?? appSettingsDefaults.CreateDefault();
            return Normalize(settings);
        }

        return Normalize(appSettingsDefaults.CreateDefault());
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(Normalize(settings), AppJson.Default);

        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Settings(Key, Value)
            VALUES($key, $value)
            ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
            """;
        command.Parameters.AddWithValue("$key", SettingsKey);
        command.Parameters.AddWithValue("$value", json);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    internal static string MigrateAppSettingsJson(string json)
    {
        return MigrateAppSettingsJsonCore(json, AppSettings.Default);
    }

    private static string MigrateAppSettingsJsonCore(string json, AppSettings defaults)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return json;
        }

        if (IsCanonicalAndLegacyFree(root))
        {
            return json;
        }

        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });

        var notifications = ReadObject(root, "notifications");
        var ui = ReadObject(root, "ui");
        var theme = ReadObject(ui, "theme");
        var windowSize = ReadObject(ui, "windowSize");
        var homeReservationProgress = ReadObject(ui, "homeReservationProgress");
        var homeCookieProgress = ReadObject(ui, "homeCookieProgress");
        var legacyProtocol = ReadObject(root, "protocol");
        var protocol = ReadObject(root, "traceIntProtocol");
        var legacyRequestPolicy = ReadObject(root, "requestPolicy");
        var network = ReadObject(root, "network");
        var tasks = ReadObject(root, "tasks");
        var grab = ReadObject(tasks, "grab");
        var occupy = ReadObject(tasks, "occupy");
        var autoRelease = ReadObject(tasks, "autoRelease");
        var tomorrowReservation = ReadObject(tasks, "tomorrowReservation");
        var globalLeak = ReadObject(tasks, "globalLeak");
        var updates = ReadObject(root, "updates");
        var mobileControl = ReadObject(root, "mobileControl");
        var remoteCheckIn = ReadObject(root, "remoteCheckIn");
        var logging = ReadObject(root, "logging");

        var legacyRetryCount = ReadInt(root, "retryCount")
            ?? ReadInt(legacyRequestPolicy, "retryCount");

        writer.WriteStartObject();

        writer.WritePropertyName("notifications");
        writer.WriteStartObject();
        writer.WritePropertyName("taskEventAlerts");
        WriteTaskEventAlerts(writer, root, notifications, defaults.Notifications.TaskEventAlerts ?? TaskEventAlertSettings.Default);
        writer.WriteEndObject();

        writer.WritePropertyName("ui");
        writer.WriteStartObject();
        writer.WriteBoolean(
            "minimizeToTray",
            ReadBool(ui, "minimizeToTray")
            ?? ReadBool(root, "minimizeToTray")
            ?? defaults.Ui.MinimizeToTray);
        writer.WriteBoolean(
            "launchOnStartup",
            ReadBool(ui, "launchOnStartup")
            ?? defaults.Ui.LaunchOnStartup);
        var normalizedWindowSize = MainViewSizePreferences.Normalize(new MainViewSizePreferences(
            ReadBool(windowSize, "rememberSize")
            ?? defaults.Ui.MainViewSize?.RememberSize
            ?? MainViewSizePreferences.Default.RememberSize,
            ReadDouble(windowSize, "clientWidth")
            ?? defaults.Ui.MainViewSize?.ClientWidth,
            ReadDouble(windowSize, "clientHeight")
            ?? defaults.Ui.MainViewSize?.ClientHeight));
        writer.WritePropertyName("windowSize");
        writer.WriteStartObject();
        writer.WriteBoolean("rememberSize", normalizedWindowSize.RememberSize);
        if (normalizedWindowSize.ClientWidth is { } clientWidth)
        {
            writer.WriteNumber("clientWidth", clientWidth);
        }
        else
        {
            writer.WriteNull("clientWidth");
        }

        if (normalizedWindowSize.ClientHeight is { } clientHeight)
        {
            writer.WriteNumber("clientHeight", clientHeight);
        }
        else
        {
            writer.WriteNull("clientHeight");
        }

        writer.WriteEndObject();
        writer.WritePropertyName("theme");
        writer.WriteStartObject();
        writer.WriteNumber(
            "mode",
            ReadInt(theme, "mode")
            ?? ReadInt(root, "themeMode")
            ?? (int)defaults.Ui.Theme!.Mode);
        writer.WriteBoolean(
            "useSystemAccent",
            ReadBool(theme, "useSystemAccent")
            ?? ReadBool(root, "useSystemAccent")
            ?? defaults.Ui.Theme!.UseSystemAccent);
        writer.WriteEndObject();
        writer.WritePropertyName("homeReservationProgress");
        writer.WriteStartObject();
        writer.WriteNumber(
            "mode",
            NormalizeHomeReservationProgressMode(
                ReadInt(homeReservationProgress, "mode"),
                defaults.Ui.HomeReservationProgress?.Mode
                ?? HomeReservationProgressSettings.Default.Mode));
        writer.WriteNumber(
            "fixedDurationMinutes",
            NormalizeHomeReservationFixedDurationMinutes(
                ReadInt(homeReservationProgress, "fixedDurationMinutes"),
                defaults.Ui.HomeReservationProgress?.FixedDurationMinutes
                ?? HomeReservationProgressSettings.Default.FixedDurationMinutes));
        writer.WriteEndObject();
        writer.WritePropertyName("homeCookieProgress");
        writer.WriteStartObject();
        writer.WriteNumber(
            "mode",
            NormalizeHomeCookieProgressMode(
                ReadInt(homeCookieProgress, "mode"),
                defaults.Ui.HomeCookieProgress?.Mode
                ?? HomeCookieProgressSettings.Default.Mode));
        writer.WriteNumber(
            "fixedDurationMinutes",
            NormalizeHomeCookieFixedDurationMinutes(
                ReadInt(homeCookieProgress, "fixedDurationMinutes"),
                defaults.Ui.HomeCookieProgress?.FixedDurationMinutes
                ?? HomeCookieProgressSettings.Default.FixedDurationMinutes));
        writer.WriteEndObject();
        writer.WriteEndObject();

        writer.WritePropertyName("traceIntProtocol");
        writer.WriteStartObject();
        writer.WriteBoolean(
            "graphQlOverridesEnabled",
            ReadBool(protocol, "graphQlOverridesEnabled")
            ?? ReadBool(legacyProtocol, "templateOverridesEnabled")
            ?? ReadBool(root, "customApiOverridesEnabled")
            ?? ReadBool(root, "advancedMode")
            ?? defaults.TraceIntProtocol.GraphQlOverridesEnabled);
        writer.WriteEndObject();

        writer.WritePropertyName("network");
        writer.WriteStartObject();
        writer.WriteNumber(
            "timeoutSeconds",
            ReadInt(network, "timeoutSeconds")
            ?? ReadInt(legacyRequestPolicy, "timeoutSeconds")
            ?? ReadInt(root, "apiTimeoutSeconds")
            ?? defaults.Network.TimeoutSeconds);
        writer.WriteNumber(
            "maxRetries",
            ReadInt(network, "maxRetries")
            ?? legacyRetryCount
            ?? defaults.Network.MaxRetries);
        writer.WriteEndObject();

        writer.WritePropertyName("tasks");
        writer.WriteStartObject();
        writer.WritePropertyName("grab");
        writer.WriteStartObject();
        writer.WriteNumber(
            "reservationStrategy",
            ReadInt(grab, "reservationStrategy")
            ?? ReadInt(tasks, "grabReservationStrategy")
            ?? ReadInt(root, "grabReservationStrategy")
            ?? (int)defaults.Tasks.Grab.ReservationStrategy);
        writer.WriteBoolean(
            "optimalStrategyReminderEnabled",
            ReadBool(grab, "optimalStrategyReminderEnabled")
            ?? defaults.Tasks.Grab.OptimalStrategyReminderEnabled);
        writer.WriteString(
            "defaultScheduledStartTime",
            NormalizeTimeOfDay(
                ReadTimeSpan(grab, "defaultScheduledStartTime"),
                defaults.Tasks.Grab.DefaultScheduledStartTime).ToString("c"));
        writer.WriteEndObject();
        writer.WritePropertyName("occupy");
        writer.WriteStartObject();
        writer.WriteNumber(
            "reReservationMaxAttempts",
            ReadInt(occupy, "reReservationMaxAttempts")
            ?? (legacyRetryCount.HasValue ? legacyRetryCount.Value + 1 : (int?)null)
            ?? defaults.Tasks.Occupy.ReReservationMaxAttempts);
        writer.WriteEndObject();
        writer.WritePropertyName("autoRelease");
        writer.WriteStartObject();
        writer.WriteBoolean(
            "enabled",
            ReadBool(autoRelease, "enabled")
            ?? defaults.Tasks.AutoRelease.Enabled);
        writer.WriteNumber(
            "leadSeconds",
            NormalizeAutoReleaseLeadSeconds(
                ReadInt(autoRelease, "leadSeconds"),
                defaults.Tasks.AutoRelease.LeadSeconds));
        writer.WriteEndObject();
        writer.WritePropertyName("tomorrowReservation");
        writer.WriteStartObject();
        writer.WriteString(
            "defaultScheduledStartTime",
            NormalizeTimeOfDay(
                ReadTimeSpan(tomorrowReservation, "defaultScheduledStartTime"),
                defaults.Tasks.TomorrowReservation.DefaultScheduledStartTime).ToString("c"));
        writer.WriteEndObject();
        writer.WritePropertyName("globalLeak");
        writer.WriteStartObject();
        writer.WritePropertyName("selectedLibraries");
        var selectedLibraries = ReadArray(globalLeak, "selectedLibraries");
        if (selectedLibraries.ValueKind == JsonValueKind.Array)
        {
            selectedLibraries.WriteTo(writer);
        }
        else
        {
            writer.WriteStartArray();
            writer.WriteEndArray();
        }
        writer.WriteEndObject();
        writer.WriteEndObject();

        writer.WritePropertyName("venue");
        writer.WriteStartObject();
        WriteNullableInt(writer, "lastLibraryId", ReadObject(root, "venue"), root, "lastLibraryId");
        WriteNullableString(writer, "lastLibraryName", ReadObject(root, "venue"), root, "lastLibraryName");
        writer.WriteEndObject();

        writer.WritePropertyName("dashboard");
        writer.WriteStartObject();
        var dashboard = ReadObject(root, "dashboard");
        writer.WriteNumber(
            "successfulReservationCount",
            ReadInt(dashboard, "successfulReservationCount")
            ?? ReadInt(root, "successfulReservationCount")
            ?? defaults.Dashboard.SuccessfulReservationCount);
        writer.WriteNumber(
            "totalGuardSeconds",
            ReadLong(dashboard, "totalGuardSeconds")
            ?? ReadLong(root, "totalGuardSeconds")
            ?? defaults.Dashboard.TotalGuardSeconds);
        writer.WriteEndObject();

        writer.WritePropertyName("updates");
        writer.WriteStartObject();
        writer.WriteBoolean(
            "checkOnStartup",
            ReadBool(updates, "checkOnStartup")
            ?? defaults.Updates.CheckOnStartup);
        WriteNullableDateTimeOffset(writer, "lastCheckedAtUtc", updates);
        WriteNullableString(writer, "skippedVersion", updates, default, "skippedVersion");
        WriteNullableString(writer, "lastReleaseETag", updates, default, "lastReleaseETag");
        WriteNullableString(
            writer,
            "lastReleaseETagVersion",
            updates,
            default,
            "lastReleaseETagVersion");
        writer.WriteEndObject();

        writer.WritePropertyName("mobileControl");
        writer.WriteStartObject();
        writer.WriteNumber(
            "port",
            ReadInt(mobileControl, "port") ?? defaults.MobileControl.Port);
        writer.WriteString(
            "accessToken",
            ReadString(mobileControl, "accessToken") ?? defaults.MobileControl.AccessToken);
        writer.WriteBoolean(
            "autoStart",
            ReadBool(mobileControl, "autoStart") ?? defaults.MobileControl.AutoStart);
        writer.WriteNumber(
            "networkMode",
            (int)MobileControlSettings.NormalizeNetworkMode(
                (MobileControlNetworkMode)(ReadInt(mobileControl, "networkMode")
                    ?? (int)defaults.MobileControl.NetworkMode)));
        var proxyMode = MobileControlSettings.NormalizeTunnelProxyMode(
            (CloudflareTunnelProxyMode)(ReadInt(mobileControl, "tunnelProxyMode")
                ?? (int)defaults.MobileControl.TunnelProxyMode));
        var proxyUrlValue = ReadString(mobileControl, "tunnelManualProxyUrl")
            ?? defaults.MobileControl.TunnelManualProxyUrl;
        var hasValidProxyUrl = MobileControlSettings.TryNormalizeManualProxyUrl(
            proxyUrlValue,
            out var normalizedProxyUrl);
        if (proxyMode == CloudflareTunnelProxyMode.ManualHttpProxy && !hasValidProxyUrl)
        {
            proxyMode = CloudflareTunnelProxyMode.Auto;
        }

        writer.WriteNumber("tunnelProxyMode", (int)proxyMode);
        writer.WriteString("tunnelManualProxyUrl", hasValidProxyUrl ? normalizedProxyUrl : string.Empty);
        writer.WriteBoolean(
            "fallbackToLocalNetworkOnTunnelFailure",
            ReadBool(mobileControl, "fallbackToLocalNetworkOnTunnelFailure")
            ?? defaults.MobileControl.FallbackToLocalNetworkOnTunnelFailure);
        writer.WriteBoolean(
            "clashMihomoCompatibilityEnabled",
            ReadBool(mobileControl, "clashMihomoCompatibilityEnabled")
            ?? defaults.MobileControl.ClashMihomoCompatibilityEnabled);
        var clashConfigPath = ReadString(mobileControl, "clashMihomoConfigPath")
            ?? defaults.MobileControl.ClashMihomoConfigPath;
        writer.WriteString(
            "clashMihomoConfigPath",
            MobileControlSettings.TryNormalizeClashMihomoConfigPath(clashConfigPath, out var normalizedClashConfigPath)
                ? normalizedClashConfigPath
                : string.Empty);
        var clashRoutePolicy = ReadString(mobileControl, "clashMihomoRoutePolicy")
            ?? defaults.MobileControl.ClashMihomoRoutePolicy;
        writer.WriteString(
            "clashMihomoRoutePolicy",
            MobileControlSettings.TryNormalizeClashMihomoRoutePolicy(clashRoutePolicy, out var normalizedClashRoutePolicy)
                ? normalizedClashRoutePolicy
                : MobileControlSettings.DefaultClashMihomoRoutePolicy);
        writer.WriteEndObject();

        writer.WritePropertyName("remoteCheckIn");
        writer.WriteStartObject();
        writer.WritePropertyName("venueProfiles");
        var venueProfiles = ReadArray(remoteCheckIn, "venueProfiles");
        if (venueProfiles.ValueKind == JsonValueKind.Array)
        {
            venueProfiles.WriteTo(writer);
        }
        else
        {
            writer.WriteStartArray();
            writer.WriteEndArray();
        }
        writer.WriteEndObject();

        writer.WritePropertyName("logging");
        writer.WriteStartObject();
        writer.WriteBoolean(
            "enabled",
            ReadBool(logging, "enabled") ?? defaults.Logging.Enabled);
        writer.WriteNumber(
            "retainedFileCount",
            LogFileSettings.Normalize(new LogFileSettings(
                ReadBool(logging, "enabled") ?? defaults.Logging.Enabled,
                ReadInt(logging, "retainedFileCount") ?? defaults.Logging.RetainedFileCount))
                .RetainedFileCount);
        writer.WriteEndObject();

        writer.WriteEndObject();
        writer.Flush();
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static AppSettings Normalize(AppSettings settings)
    {
        var notifications = settings.Notifications ?? NotificationSettings.Default;
        var ui = settings.Ui ?? UiPreferences.Default;
        var windowSize = MainViewSizePreferences.Normalize(ui.MainViewSize);
        var alertSettings = notifications.TaskEventAlerts ?? TaskEventAlertSettings.Default;
        var tasks = settings.Tasks ?? TaskExecutionSettings.Default;
        var grab = tasks.Grab ?? GrabTaskSettings.Default;
        var occupy = tasks.Occupy ?? OccupyTaskSettings.Default;
        var autoRelease = tasks.AutoRelease ?? AutoReleaseTaskSettings.Default;
        var tomorrowReservation = tasks.TomorrowReservation ?? TomorrowReservationTaskSettings.Default;
        var globalLeak = tasks.GlobalLeak ?? GlobalLeakTaskSettings.Default;
        var updates = settings.Updates ?? UpdateCheckSettings.Default;
        var mobileControl = settings.MobileControl ?? MobileControlSettings.Default;
        var tunnelProxyMode = MobileControlSettings.NormalizeTunnelProxyMode(mobileControl.TunnelProxyMode);
        var hasValidTunnelProxyUrl = MobileControlSettings.TryNormalizeManualProxyUrl(
            mobileControl.TunnelManualProxyUrl,
            out var normalizedTunnelProxyUrl);
        if (tunnelProxyMode == CloudflareTunnelProxyMode.ManualHttpProxy && !hasValidTunnelProxyUrl)
        {
            tunnelProxyMode = CloudflareTunnelProxyMode.Auto;
        }
        var hasValidClashConfigPath = MobileControlSettings.TryNormalizeClashMihomoConfigPath(
            mobileControl.ClashMihomoConfigPath,
            out var normalizedClashConfigPath);
        var hasValidClashRoutePolicy = MobileControlSettings.TryNormalizeClashMihomoRoutePolicy(
            mobileControl.ClashMihomoRoutePolicy,
            out var normalizedClashRoutePolicy);

        var remoteCheckIn = settings.RemoteCheckIn ?? RemoteCheckInSettings.Default;
        return settings with
        {
            Notifications = notifications with
            {
                TaskEventAlerts = new TaskEventAlertSettings(
                    alertSettings.Email ?? EmailAlertChannelSettings.Default,
                    alertSettings.Local ?? LocalDesktopAlertSettings.Default,
                    alertSettings.Telegram ?? TelegramAlertChannelSettings.Default,
                    alertSettings.Events ?? TaskEventAlertEventSettings.Default,
                    alertSettings.Bark ?? BarkAlertChannelSettings.Default,
                    alertSettings.WxPusher ?? WxPusherAlertChannelSettings.Default,
                    alertSettings.ServerChan ?? ServerChanAlertChannelSettings.Default)
            },
            Ui = ui with
            {
                MainViewSize = windowSize,
                Theme = ui.Theme ?? ThemePreferences.Default,
                HomeReservationProgress = HomeReservationProgressSettings.Normalize(ui.HomeReservationProgress),
                HomeCookieProgress = HomeCookieProgressSettings.Normalize(ui.HomeCookieProgress)
            },
            TraceIntProtocol = settings.TraceIntProtocol ?? TraceIntProtocolSettings.Default,
            Network = settings.Network ?? NetworkRequestSettings.Default,
            Tasks = tasks with
            {
                Grab = grab with
                {
                    DefaultScheduledStartTime = NormalizeTimeOfDay(
                        grab.DefaultScheduledStartTime,
                        GrabTaskSettings.Default.DefaultScheduledStartTime)
                },
                Occupy = occupy,
                AutoRelease = autoRelease with
                {
                    LeadSeconds = AutoReleaseTaskSettings.NormalizeLeadSeconds(autoRelease.LeadSeconds)
                },
                TomorrowReservation = tomorrowReservation with
                {
                    DefaultScheduledStartTime = NormalizeTimeOfDay(
                        tomorrowReservation.DefaultScheduledStartTime,
                        TomorrowReservationTaskSettings.Default.DefaultScheduledStartTime)
                },
                GlobalLeak = globalLeak with
                {
                    SelectedLibraries = NormalizeGlobalLeakSelectedLibraries(globalLeak.SelectedLibraries)
                }
            },
            Venue = settings.Venue ?? VenueSelectionSettings.Default,
            Dashboard = settings.Dashboard ?? DashboardMetrics.Default,
            Updates = updates,
            MobileControl = mobileControl with
            {
                Port = MobileControlSettings.IsValidPort(mobileControl.Port) ? mobileControl.Port : 0,
                AccessToken = mobileControl.AccessToken?.Trim() ?? string.Empty,
                AutoStart = mobileControl.AutoStart,
                NetworkMode = MobileControlSettings.NormalizeNetworkMode(mobileControl.NetworkMode),
                TunnelProxyMode = tunnelProxyMode,
                TunnelManualProxyUrl = hasValidTunnelProxyUrl ? normalizedTunnelProxyUrl : string.Empty,
                ClashMihomoCompatibilityEnabled = mobileControl.ClashMihomoCompatibilityEnabled,
                ClashMihomoConfigPath = hasValidClashConfigPath ? normalizedClashConfigPath : string.Empty,
                ClashMihomoRoutePolicy = hasValidClashRoutePolicy
                    ? normalizedClashRoutePolicy
                    : MobileControlSettings.DefaultClashMihomoRoutePolicy
            },
            RemoteCheckIn = remoteCheckIn with
            {
                VenueProfiles = NormalizeRemoteCheckInVenueProfiles(remoteCheckIn.VenueProfiles)
            },
            Logging = LogFileSettings.Normalize(settings.Logging)
        };
    }

    private static bool IsCanonicalAndLegacyFree(JsonElement root)
    {
        return IsCanonicalShape(root) && !ContainsLegacySettingsFields(root);
    }

    private static bool IsCanonicalShape(JsonElement root)
    {
        var notifications = ReadObject(root, "notifications");
        var taskEventAlerts = ReadObject(notifications, "taskEventAlerts");
        var taskEventAlertEvents = ReadObject(taskEventAlerts, "events");
        var ui = ReadObject(root, "ui");
        var windowSize = ReadObject(ui, "windowSize");
        var logging = ReadObject(root, "logging");
        return ReadBool(taskEventAlertEvents, "cookieExpiring").HasValue &&
               windowSize.ValueKind == JsonValueKind.Object &&
               ReadBool(windowSize, "rememberSize").HasValue &&
               HasCanonicalWindowSizeDimensions(windowSize) &&
               root.TryGetProperty("traceIntProtocol", out _) &&
               root.TryGetProperty("network", out _) &&
               root.TryGetProperty("tasks", out var tasks) &&
               tasks.ValueKind == JsonValueKind.Object &&
               tasks.TryGetProperty("grab", out _) &&
               tasks.TryGetProperty("occupy", out _) &&
               tasks.TryGetProperty("autoRelease", out _) &&
               tasks.TryGetProperty("tomorrowReservation", out _) &&
               tasks.TryGetProperty("globalLeak", out _) &&
               root.TryGetProperty("updates", out _) &&
               root.TryGetProperty("mobileControl", out _) &&
               root.TryGetProperty("remoteCheckIn", out _) &&
               logging.ValueKind == JsonValueKind.Object &&
               ReadBool(logging, "enabled").HasValue &&
               ReadInt(logging, "retainedFileCount").HasValue;
    }

    private static bool HasCanonicalWindowSizeDimensions(JsonElement windowSize)
    {
        var hasWidth = windowSize.TryGetProperty("clientWidth", out var clientWidth);
        var hasHeight = windowSize.TryGetProperty("clientHeight", out var clientHeight);
        if (!hasWidth || !hasHeight)
        {
            return !hasWidth && !hasHeight;
        }

        return IsNullOrDouble(clientWidth) && IsNullOrDouble(clientHeight);
    }

    private static bool IsNullOrDouble(JsonElement value)
    {
        return value.ValueKind == JsonValueKind.Null ||
               value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out _);
    }

    private static IReadOnlyList<RemoteCheckInVenueProfileSettings> NormalizeRemoteCheckInVenueProfiles(
        IReadOnlyList<RemoteCheckInVenueProfileSettings>? profiles)
    {
        if (profiles is null || profiles.Count == 0)
        {
            return [];
        }

        return profiles
            .Where(static profile => profile is not null && profile.LibraryId > 0)
            .Select(static profile =>
            {
                var beaconUuid = Guid.TryParse(profile.BeaconUuid?.Trim(), out var uuid)
                    ? uuid.ToString("D").ToUpperInvariant()
                    : string.Empty;
                return profile with
                {
                    LibraryName = profile.LibraryName?.Trim() ?? string.Empty,
                    BeaconUuid = beaconUuid,
                    Major = profile.Major is >= ushort.MinValue and <= ushort.MaxValue ? profile.Major : null,
                    Minor = profile.Minor is >= ushort.MinValue and <= ushort.MaxValue ? profile.Minor : null,
                    Latitude = profile.Latitude is >= -90m and <= 90m ? profile.Latitude : null,
                    Longitude = profile.Longitude is >= -180m and <= 180m ? profile.Longitude : null
                };
            })
            .GroupBy(static profile => profile.LibraryId)
            .Select(static group => group.Last())
            .OrderBy(static profile => profile.LibraryId)
            .ToArray();
    }

    private static bool ContainsLegacySettingsFields(JsonElement root)
    {
        var taskEventAlerts = ReadObject(ReadObject(root, "notifications"), "taskEventAlerts");
        return HasAnyProperty(
                   root,
                   "cookieExpiryAlerts",
                   "notificationsEnabled",
                   "advancedMode",
                   "customApiOverridesEnabled",
                   "apiTimeoutSeconds",
                   "retryCount",
                   "grabReservationStrategy",
                   "themeMode") ||
               HasAnyProperty(ReadObject(root, "notifications"), "appBannerNotificationsEnabled") ||
               HasAnyProperty(ReadObject(root, "protocol"), "templateOverridesEnabled") ||
               HasAnyProperty(ReadObject(root, "requestPolicy"), "timeoutSeconds", "retryCount") ||
               HasAnyProperty(ReadObject(root, "tasks"), "grabReservationStrategy") ||
               !HasAnyProperty(ReadObject(ReadObject(root, "tasks"), "grab"), "defaultScheduledStartTime") ||
               !HasAnyProperty(ReadObject(ReadObject(root, "tasks"), "grab"), "optimalStrategyReminderEnabled") ||
               !HasAnyProperty(ReadObject(ReadObject(root, "tasks"), "autoRelease"), "enabled") ||
               !HasAnyProperty(ReadObject(ReadObject(root, "tasks"), "autoRelease"), "leadSeconds") ||
               !HasAnyProperty(ReadObject(ReadObject(root, "tasks"), "tomorrowReservation"), "defaultScheduledStartTime") ||
               !HasAnyProperty(ReadObject(ReadObject(root, "tasks"), "globalLeak"), "selectedLibraries") ||
               !HasAnyProperty(ReadObject(root, "mobileControl"), "port") ||
               !HasAnyProperty(ReadObject(root, "mobileControl"), "accessToken") ||
               !HasAnyProperty(ReadObject(root, "mobileControl"), "autoStart") ||
                !HasAnyProperty(ReadObject(root, "mobileControl"), "networkMode") ||
                !HasAnyProperty(ReadObject(root, "mobileControl"), "tunnelProxyMode") ||
                !HasAnyProperty(ReadObject(root, "mobileControl"), "tunnelManualProxyUrl") ||
                !HasAnyProperty(ReadObject(root, "mobileControl"), "fallbackToLocalNetworkOnTunnelFailure") ||
                !HasAnyProperty(ReadObject(root, "mobileControl"), "clashMihomoCompatibilityEnabled") ||
               !HasAnyProperty(ReadObject(root, "mobileControl"), "clashMihomoConfigPath") ||
               !HasAnyProperty(ReadObject(root, "mobileControl"), "clashMihomoRoutePolicy") ||
               HasAnyProperty(
                   ReadObject(
                       ReadObject(
                           ReadObject(root, "notifications"),
                           "taskEventAlerts"),
                       "local"),
                   "toastEnabled") ||
               taskEventAlerts.ValueKind == JsonValueKind.Object &&
               (!HasAnyProperty(taskEventAlerts, "events") ||
                !HasAnyProperty(taskEventAlerts, "bark") ||
                !HasAnyProperty(taskEventAlerts, "wxPusher") ||
                !HasAnyProperty(taskEventAlerts, "serverChan"));
    }

    private static bool HasAnyProperty(JsonElement parent, params string[] propertyNames)
    {
        if (parent.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var propertyName in propertyNames)
        {
            if (parent.TryGetProperty(propertyName, out _))
            {
                return true;
            }
        }

        return false;
    }

    private static void WriteTaskEventAlerts(
        Utf8JsonWriter writer,
        JsonElement root,
        JsonElement notifications,
        TaskEventAlertSettings defaults)
    {
        var alerts = ReadObject(notifications, "taskEventAlerts");
        if (alerts.ValueKind == JsonValueKind.Undefined)
        {
            alerts = ReadObject(root, "cookieExpiryAlerts");
        }

        if (alerts.ValueKind == JsonValueKind.Undefined)
        {
            JsonSerializer.Serialize(writer, defaults, AppJson.Default);
            return;
        }

        writer.WriteStartObject();
        writer.WritePropertyName("email");
        WriteObjectOrDefault(writer, ReadObject(alerts, "email"), defaults.Email);
        writer.WritePropertyName("local");
        WriteLocalDesktopAlert(writer, ReadObject(alerts, "local"), defaults.Local);
        writer.WritePropertyName("telegram");
        WriteObjectOrDefault(writer, ReadObject(alerts, "telegram"), defaults.Telegram);
        writer.WritePropertyName("bark");
        WriteObjectOrDefault(writer, ReadObject(alerts, "bark"), defaults.Bark);
        writer.WritePropertyName("wxPusher");
        WriteObjectOrDefault(writer, ReadObject(alerts, "wxPusher"), defaults.WxPusher);
        writer.WritePropertyName("serverChan");
        WriteObjectOrDefault(writer, ReadObject(alerts, "serverChan"), defaults.ServerChan);
        writer.WritePropertyName("events");
        WriteTaskEventAlertEvents(writer, ReadObject(alerts, "events"), defaults.Events);
        writer.WriteEndObject();
    }

    private static void WriteTaskEventAlertEvents(
        Utf8JsonWriter writer,
        JsonElement events,
        TaskEventAlertEventSettings defaults)
    {
        writer.WriteStartObject();
        writer.WriteBoolean(
            "cookieExpiring",
            ReadBool(events, "cookieExpiring") ?? defaults.CookieExpiring);
        writer.WriteBoolean(
            "grabSucceeded",
            ReadBool(events, "grabSucceeded") ?? defaults.GrabSucceeded);
        writer.WriteBoolean(
            "occupyReReserveSucceeded",
            ReadBool(events, "occupyReReserveSucceeded") ?? defaults.OccupyReReserveSucceeded);
        writer.WriteBoolean(
            "tomorrowReservationSucceeded",
            ReadBool(events, "tomorrowReservationSucceeded") ?? defaults.TomorrowReservationSucceeded);
        writer.WriteBoolean(
            "globalLeakSucceeded",
            ReadBool(events, "globalLeakSucceeded") ?? defaults.GlobalLeakSucceeded);
        writer.WriteBoolean(
            "sessionInvalid",
            ReadBool(events, "sessionInvalid") ?? defaults.SessionInvalid);
        writer.WriteBoolean(
            "taskFailed",
            ReadBool(events, "taskFailed") ?? defaults.TaskFailed);
        writer.WriteEndObject();
    }

    private static void WriteLocalDesktopAlert(
        Utf8JsonWriter writer,
        JsonElement local,
        LocalDesktopAlertSettings defaults)
    {
        writer.WriteStartObject();
        writer.WriteBoolean(
            "popupEnabled",
            ReadBool(local, "popupEnabled")
            ?? ReadBool(local, "toastEnabled")
            ?? defaults.PopupEnabled);
        writer.WriteBoolean(
            "soundEnabled",
            ReadBool(local, "soundEnabled") ?? defaults.SoundEnabled);
        writer.WriteEndObject();
    }

    private static void WriteObjectOrDefault<T>(
        Utf8JsonWriter writer,
        JsonElement element,
        T defaultValue)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            element.WriteTo(writer);
            return;
        }

        JsonSerializer.Serialize(writer, defaultValue, AppJson.Default);
    }

    private static JsonElement ReadObject(JsonElement parent, string propertyName)
    {
        return parent.ValueKind == JsonValueKind.Object &&
               parent.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.Object
            ? property
            : default;
    }

    private static JsonElement ReadArray(JsonElement parent, string propertyName)
    {
        return parent.ValueKind == JsonValueKind.Object &&
               parent.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.Array
            ? property
            : default;
    }

    private static bool? ReadBool(JsonElement parent, string propertyName)
    {
        if (parent.ValueKind != JsonValueKind.Object ||
            !parent.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(property.GetString(), out var value) => value,
            _ => null
        };
    }

    private static int? ReadInt(JsonElement parent, string propertyName)
    {
        if (parent.ValueKind != JsonValueKind.Object ||
            !parent.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var intValue))
        {
            return intValue;
        }

        return property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), out intValue)
            ? intValue
            : null;
    }

    private static double? ReadDouble(JsonElement parent, string propertyName)
    {
        if (parent.ValueKind != JsonValueKind.Object ||
            !parent.TryGetProperty(propertyName, out var property) ||
            property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out var doubleValue))
        {
            return doubleValue;
        }

        return property.ValueKind == JsonValueKind.String &&
               double.TryParse(
                   property.GetString(),
                   System.Globalization.NumberStyles.Float,
                   System.Globalization.CultureInfo.InvariantCulture,
                   out doubleValue)
            ? doubleValue
            : null;
    }

    private static long? ReadLong(JsonElement parent, string propertyName)
    {
        if (parent.ValueKind != JsonValueKind.Object ||
            !parent.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out var longValue))
        {
            return longValue;
        }

        return property.ValueKind == JsonValueKind.String && long.TryParse(property.GetString(), out longValue)
            ? longValue
            : null;
    }

    private static TimeSpan? ReadTimeSpan(JsonElement parent, string propertyName)
    {
        if (parent.ValueKind != JsonValueKind.Object ||
            !parent.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.String &&
            TimeSpan.TryParse(property.GetString(), out var value))
        {
            return value;
        }

        return null;
    }

    private static DateTimeOffset? ReadDateTimeOffset(JsonElement parent, string propertyName)
    {
        if (parent.ValueKind != JsonValueKind.Object ||
            !parent.TryGetProperty(propertyName, out var property) ||
            property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String &&
               DateTimeOffset.TryParse(property.GetString(), out var value)
            ? value
            : null;
    }

    private static TimeSpan NormalizeTimeOfDay(TimeSpan? value, TimeSpan fallback)
    {
        return value is { } timeOfDay && IsTimeOfDay(timeOfDay)
            ? timeOfDay
            : fallback;
    }

    private static TimeSpan NormalizeTimeOfDay(TimeSpan value, TimeSpan fallback)
    {
        return IsTimeOfDay(value) ? value : fallback;
    }

    private static int NormalizeAutoReleaseLeadSeconds(int? value, int fallback)
    {
        return AutoReleaseTaskSettings.NormalizeLeadSeconds(value ?? fallback);
    }

    private static int NormalizeHomeReservationProgressMode(
        int? value,
        HomeReservationProgressTimingMode fallback)
    {
        var mode = value.HasValue
            ? (HomeReservationProgressTimingMode)value.Value
            : fallback;
        return (int)HomeReservationProgressSettings.NormalizeMode(mode);
    }

    private static int NormalizeHomeReservationFixedDurationMinutes(int? value, int fallback)
    {
        return HomeReservationProgressSettings.NormalizeFixedDurationMinutes(value ?? fallback);
    }

    private static int NormalizeHomeCookieProgressMode(
        int? value,
        HomeCookieProgressTimingMode fallback)
    {
        var mode = value.HasValue
            ? (HomeCookieProgressTimingMode)value.Value
            : fallback;
        return (int)HomeCookieProgressSettings.NormalizeMode(mode);
    }

    private static int NormalizeHomeCookieFixedDurationMinutes(int? value, int fallback)
    {
        return HomeCookieProgressSettings.NormalizeFixedDurationMinutes(value ?? fallback);
    }

    private static bool IsTimeOfDay(TimeSpan value)
    {
        return value >= TimeSpan.Zero && value < TimeSpan.FromDays(1);
    }

    private static IReadOnlyList<GlobalLeakLibrarySelectionSettings> NormalizeGlobalLeakSelectedLibraries(
        IReadOnlyList<GlobalLeakLibrarySelectionSettings>? selectedLibraries)
    {
        if (selectedLibraries is null || selectedLibraries.Count == 0)
        {
            return [];
        }

        return selectedLibraries
            .Select(static library => new GlobalLeakLibrarySelectionSettings(
                library.LibraryId,
                library.LibraryName ?? string.Empty,
                library.Floor ?? string.Empty))
            .ToArray();
    }

    private static void WriteNullableInt(
        Utf8JsonWriter writer,
        string propertyName,
        JsonElement currentParent,
        JsonElement legacyParent,
        string legacyPropertyName)
    {
        var value = ReadInt(currentParent, propertyName) ?? ReadInt(legacyParent, legacyPropertyName);
        if (value is null)
        {
            writer.WriteNull(propertyName);
            return;
        }

        writer.WriteNumber(propertyName, value.Value);
    }

    private static void WriteNullableString(
        Utf8JsonWriter writer,
        string propertyName,
        JsonElement currentParent,
        JsonElement legacyParent,
        string legacyPropertyName)
    {
        var value = ReadString(currentParent, propertyName) ?? ReadString(legacyParent, legacyPropertyName);
        if (value is null)
        {
            writer.WriteNull(propertyName);
            return;
        }

        writer.WriteString(propertyName, value);
    }

    private static void WriteNullableDateTimeOffset(
        Utf8JsonWriter writer,
        string propertyName,
        JsonElement currentParent)
    {
        var value = ReadDateTimeOffset(currentParent, propertyName);
        if (value is null)
        {
            writer.WriteNull(propertyName);
            return;
        }

        writer.WriteString(propertyName, value.Value);
    }

    private static string? ReadString(JsonElement parent, string propertyName)
    {
        if (parent.ValueKind != JsonValueKind.Object ||
            !parent.TryGetProperty(propertyName, out var property) ||
            property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : property.ToString();
    }
}
