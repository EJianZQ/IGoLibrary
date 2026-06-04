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
        var legacyProtocol = ReadObject(root, "protocol");
        var protocol = ReadObject(root, "traceIntProtocol");
        var legacyRequestPolicy = ReadObject(root, "requestPolicy");
        var network = ReadObject(root, "network");
        var tasks = ReadObject(root, "tasks");
        var grab = ReadObject(tasks, "grab");
        var occupy = ReadObject(tasks, "occupy");

        var legacyRetryCount = ReadInt(root, "retryCount")
            ?? ReadInt(legacyRequestPolicy, "retryCount");

        writer.WriteStartObject();

        writer.WritePropertyName("notifications");
        writer.WriteStartObject();
        writer.WriteBoolean(
            "appBannerNotificationsEnabled",
            ReadBool(notifications, "appBannerNotificationsEnabled")
            ?? ReadBool(root, "notificationsEnabled")
            ?? defaults.Notifications.AppBannerNotificationsEnabled);
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
        writer.WriteEndObject();
        writer.WritePropertyName("occupy");
        writer.WriteStartObject();
        writer.WriteNumber(
            "reReservationMaxAttempts",
            ReadInt(occupy, "reReservationMaxAttempts")
            ?? (legacyRetryCount.HasValue ? legacyRetryCount.Value + 1 : (int?)null)
            ?? defaults.Tasks.Occupy.ReReservationMaxAttempts);
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

        writer.WriteEndObject();
        writer.Flush();
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static AppSettings Normalize(AppSettings settings)
    {
        var notifications = settings.Notifications ?? NotificationSettings.Default;
        var ui = settings.Ui ?? UiPreferences.Default;
        var alertSettings = notifications.TaskEventAlerts ?? TaskEventAlertSettings.Default;
        var tasks = settings.Tasks ?? TaskExecutionSettings.Default;
        return settings with
        {
            Notifications = notifications with
            {
                TaskEventAlerts = new TaskEventAlertSettings(
                    alertSettings.Email ?? EmailAlertChannelSettings.Default,
                    alertSettings.Local ?? LocalDesktopAlertSettings.Default,
                    alertSettings.Telegram ?? TelegramAlertChannelSettings.Default)
            },
            Ui = ui with
            {
                Theme = ui.Theme ?? ThemePreferences.Default
            },
            TraceIntProtocol = settings.TraceIntProtocol ?? TraceIntProtocolSettings.Default,
            Network = settings.Network ?? NetworkRequestSettings.Default,
            Tasks = tasks with
            {
                Grab = tasks.Grab ?? GrabTaskSettings.Default,
                Occupy = tasks.Occupy ?? OccupyTaskSettings.Default
            },
            Venue = settings.Venue ?? VenueSelectionSettings.Default,
            Dashboard = settings.Dashboard ?? DashboardMetrics.Default
        };
    }

    private static bool IsCanonicalAndLegacyFree(JsonElement root)
    {
        return IsCanonicalShape(root) && !ContainsLegacySettingsFields(root);
    }

    private static bool IsCanonicalShape(JsonElement root)
    {
        return root.TryGetProperty("traceIntProtocol", out _) &&
               root.TryGetProperty("network", out _) &&
               root.TryGetProperty("tasks", out var tasks) &&
               tasks.ValueKind == JsonValueKind.Object &&
               tasks.TryGetProperty("grab", out _) &&
               tasks.TryGetProperty("occupy", out _);
    }

    private static bool ContainsLegacySettingsFields(JsonElement root)
    {
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
               HasAnyProperty(ReadObject(root, "protocol"), "templateOverridesEnabled") ||
               HasAnyProperty(ReadObject(root, "requestPolicy"), "timeoutSeconds", "retryCount") ||
               HasAnyProperty(ReadObject(root, "tasks"), "grabReservationStrategy") ||
               HasAnyProperty(
                   ReadObject(
                       ReadObject(
                           ReadObject(root, "notifications"),
                           "taskEventAlerts"),
                       "local"),
                   "toastEnabled");
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
