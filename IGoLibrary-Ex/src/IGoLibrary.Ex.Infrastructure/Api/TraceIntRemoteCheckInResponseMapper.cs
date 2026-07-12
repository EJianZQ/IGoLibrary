using System.Text.Json;
using IGoLibrary.Ex.Application.Exceptions;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Infrastructure.Api;

internal static class TraceIntRemoteCheckInResponseMapper
{
    public static RemoteCheckInDeviceInfo MapDeviceInfo(string raw)
    {
        try
        {
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            ThrowIfApiError(root);
            var data = root.GetProperty("data");
            var user = data.GetProperty("user");
            var devices = data.GetProperty("devices")
                .EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())
                .Where(value => RemoteCheckInProfileValidator.TryNormalizeUuid(value, out _))
                .Select(value =>
                {
                    RemoteCheckInProfileValidator.TryNormalizeUuid(value, out var normalized);
                    return normalized;
                })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (devices.Length == 0)
            {
                throw new RemoteCheckInApiException("当前预约区域未返回可用的 Beacon UUID");
            }

            return new RemoteCheckInDeviceInfo(
                new RemoteCheckInUserSummary(
                    ReadString(user, "user_nick"),
                    ReadString(user, "user_sch"),
                    ReadString(user, "user_student_name"),
                    ReadString(user, "user_student_no")),
                devices);
        }
        catch (RemoteCheckInApiException)
        {
            throw;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw new RemoteCheckInApiException("设备信息响应格式无效", innerException: ex);
        }
    }

    public static RemoteCheckInServerTime MapServerTime(string raw)
    {
        var value = raw.Trim();
        if (!long.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var unixSeconds) ||
            unixSeconds <= 0)
        {
            throw new RemoteCheckInApiException("服务器返回了无效的时间戳");
        }

        return new RemoteCheckInServerTime(unixSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture), unixSeconds);
    }

    public static RemoteCheckInResult MapSignResult(string raw)
    {
        try
        {
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            ThrowIfApiError(root);
            var message = ReadString(root, "msg");
            var data = root.TryGetProperty("data", out var dataElement) && dataElement.ValueKind == JsonValueKind.Object
                ? dataElement
                : default;

            return new RemoteCheckInResult(
                string.IsNullOrWhiteSpace(message) ? "验证成功" : message,
                ReadNullableInt(data, "status"),
                ReadNullableInt(data, "lib_id"),
                ReadString(data, "lib_name"),
                ReadString(data, "lib_floor"),
                ReadString(data, "seat_key"),
                ReadString(data, "seat_name"),
                ReadUnixTime(data, "date"),
                ReadUnixTime(data, "exp_date"));
        }
        catch (RemoteCheckInApiException)
        {
            throw;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            throw new RemoteCheckInApiException("签到响应格式无效", innerException: ex);
        }
    }

    internal static bool IsSessionInvalidMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var value = message.Trim();
        return value.Contains("未登录", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("登录已失效", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("登录已过期", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("登录过期", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("会话失效", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("会话已过期", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("会话过期", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("授权失效", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("授权过期", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("access denied", StringComparison.OrdinalIgnoreCase);
    }

    private static void ThrowIfApiError(JsonElement root)
    {
        var code = ReadNullableInt(root, "code")
                   ?? throw new RemoteCheckInApiException("响应缺少 code 字段");
        if (code == 0)
        {
            return;
        }

        var message = ReadString(root, "msg");
        if (string.IsNullOrWhiteSpace(message))
        {
            message = "接口未返回成功结果";
        }

        throw new RemoteCheckInApiException(
            message,
            code,
            message,
            IsSessionInvalidMessage(message));
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return string.Empty;
        }

        return property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : property.ToString();
    }

    private static int? ReadNullableInt(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt32(out var value) => value,
            JsonValueKind.String when int.TryParse(property.GetString(), out var value) => value,
            _ => null
        };
    }

    private static DateTimeOffset? ReadUnixTime(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        long value = default;
        var hasValue = property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt64(out value) => true,
            JsonValueKind.String when long.TryParse(property.GetString(), out value) => true,
            _ => false
        };
        if (!hasValue)
        {
            return null;
        }

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(value).ToLocalTime();
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
