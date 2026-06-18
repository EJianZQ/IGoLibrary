using System.Text.Json;
using IGoLibrary.Ex.Application.Exceptions;
using IGoLibrary.Ex.Domain.Helpers;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Infrastructure.Api;

internal static class TraceIntGraphQlResponseMapper
{
    public static IReadOnlyList<LibrarySummary> MapLibraries(string raw)
    {
        using var document = JsonDocument.Parse(raw);

        ThrowIfGraphQlError(document.RootElement);
        var libs = document.RootElement
            .GetProperty("data")
            .GetProperty("userAuth")
            .GetProperty("reserve")
            .GetProperty("libs");

        var results = new List<LibrarySummary>();
        foreach (var item in libs.EnumerateArray())
        {
            var floor = item.GetProperty("lib_floor").GetString() ?? string.Empty;
            if (floor == "0")
            {
                continue;
            }

            var runtime = item.TryGetProperty("lib_rt", out var runtimeElement)
                ? runtimeElement
                : default;

            results.Add(new LibrarySummary(
                item.GetProperty("lib_id").GetInt32(),
                item.GetProperty("lib_name").GetString() ?? "Unknown",
                floor,
                item.GetProperty("is_open").GetBoolean(),
                ReadOptionalIntProperty(runtime, "seats_total"),
                ReadOptionalIntProperty(runtime, "seats_used"),
                ReadOptionalIntProperty(runtime, "seats_booking")));
        }

        return results;
    }

    public static LibraryLayout MapLibraryLayout(string raw)
    {
        using var document = JsonDocument.Parse(raw);

        ThrowIfGraphQlError(document.RootElement);
        var lib = document.RootElement
            .GetProperty("data")
            .GetProperty("userAuth")
            .GetProperty("reserve")
            .GetProperty("libs")[0];

        var layout = lib.GetProperty("lib_layout");
        var seats = new List<SeatSnapshot>();
        foreach (var seat in layout.GetProperty("seats").EnumerateArray())
        {
            if (!IsSeatLayoutItem(seat))
            {
                continue;
            }

            var key = ReadOptionalStringProperty(seat, "key").Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            if (!TryReadBooleanLikeProperty(seat, "status", out var isOccupied) ||
                !TryReadRequiredIntProperty(seat, "x", out var x) ||
                !TryReadRequiredIntProperty(seat, "y", out var y))
            {
                continue;
            }

            var name = ReadOptionalStringProperty(seat, "name").Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                name = key;
            }

            seats.Add(new SeatSnapshot(
                key,
                name,
                isOccupied,
                x,
                y));
        }

        return new LibraryLayout(
            lib.GetProperty("lib_id").GetInt32(),
            lib.GetProperty("lib_name").GetString() ?? "Unknown",
            lib.GetProperty("lib_floor").GetString() ?? string.Empty,
            lib.GetProperty("is_open").GetBoolean(),
            layout.GetProperty("seats_total").GetInt32(),
            layout.GetProperty("seats_booking").GetInt32(),
            layout.GetProperty("seats_used").GetInt32(),
            seats.OrderBy(x => int.TryParse(x.SeatName, out var number) ? number : int.MaxValue).ToList());
    }

    public static LibraryRule MapLibraryRule(string raw, int libraryId)
    {
        using var document = JsonDocument.Parse(raw);

        ThrowIfGraphQlError(document.RootElement);
        var rule = document.RootElement
            .GetProperty("data")
            .GetProperty("userAuth")
            .GetProperty("reserve")
            .GetProperty("libRule");

        return new LibraryRule(
            libraryId,
            rule.GetProperty("advance_booking").GetString() ?? string.Empty,
            rule.GetProperty("lib_seat_ttl").GetString() ?? string.Empty,
            rule.GetProperty("lib_hold_ttl").GetString() ?? string.Empty,
            rule.GetProperty("lib_renew_time").GetString() ?? string.Empty,
            rule.GetProperty("hold_reason").GetString() ?? string.Empty,
            rule.TryGetProperty("close_start_date", out var closeStartDate) && closeStartDate.ValueKind != JsonValueKind.Null
                ? closeStartDate.GetString()
                : null,
            rule.TryGetProperty("close_end_date", out var closeEndDate) && closeEndDate.ValueKind != JsonValueKind.Null
                ? closeEndDate.GetString()
                : null,
            rule.GetProperty("open_time").GetInt64(),
            rule.GetProperty("open_time_str").GetString() ?? string.Empty,
            rule.GetProperty("close_time").GetInt64(),
            rule.GetProperty("close_time_str").GetString() ?? string.Empty,
            rule.GetProperty("lib_validate_time").GetInt32());
    }

    public static ReservationInfo? MapReservationInfo(string raw)
    {
        using var document = JsonDocument.Parse(raw);

        ThrowIfGraphQlError(document.RootElement);
        var reserveNode = document.RootElement
            .GetProperty("data")
            .GetProperty("userAuth")
            .GetProperty("reserve");

        if (!reserveNode.TryGetProperty("reserve", out var reservation) || reservation.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        var token = reserveNode.GetProperty("getSToken").GetString() ?? string.Empty;
        var expirationTimestamp = reservation.GetProperty("exp_date").GetInt64();
        var validateTimestamp = TryReadUnixTimestamp(reservation, "validate_date");
        var holdTimestamp = TryReadUnixTimestamp(reservation, "hold_date");

        return new ReservationInfo(
            token,
            reservation.GetProperty("lib_id").GetInt32(),
            reservation.GetProperty("lib_name").GetString() ?? string.Empty,
            reservation.GetProperty("seat_key").GetString() ?? string.Empty,
            reservation.GetProperty("seat_name").GetString() ?? string.Empty,
            ReservationTimeHelper.FromUnixSeconds(expirationTimestamp),
            TryReadIntProperty(reservation, "status"),
            validateTimestamp.HasValue ? ReservationTimeHelper.FromUnixSeconds(validateTimestamp.Value) : null,
            holdTimestamp.HasValue ? ReservationTimeHelper.FromUnixSeconds(holdTimestamp.Value) : null);
    }

    private static long? TryReadUnixTimestamp(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt64(out var value) && value > 0 => value,
            JsonValueKind.String when long.TryParse(property.GetString(), out var value) && value > 0 => value,
            _ => null
        };
    }

    private static int? TryReadIntProperty(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
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

    public static bool MapReserveSeat(string raw)
    {
        using var document = JsonDocument.Parse(raw);

        ThrowIfGraphQlError(document.RootElement);
        var reserveResult = document.RootElement
            .GetProperty("data")
            .GetProperty("userAuth")
            .GetProperty("reserve")
            .GetProperty("reserueSeat");

        return ReadBooleanLike(reserveResult, "reserueSeat");
    }

    public static bool MapCancelReservation(string raw)
    {
        using var document = JsonDocument.Parse(raw);

        if (TryFindErrorInfo(document.RootElement, out var errorInfo))
        {
            return errorInfo.Message.Contains("成功", StringComparison.OrdinalIgnoreCase);
        }

        if (document.RootElement.TryGetProperty("data", out var data) &&
            data.TryGetProperty("userAuth", out var userAuth) &&
            userAuth.TryGetProperty("reserve", out var reserve) &&
            reserve.TryGetProperty("reserveCancle", out _))
        {
            return true;
        }

        return false;
    }

    public static void MapTomorrowReservationWarmUp(string raw)
    {
        using var document = JsonDocument.Parse(raw);

        ThrowIfGraphQlError(document.RootElement);
    }

    public static bool MapTomorrowReservationSave(string raw)
    {
        using var document = JsonDocument.Parse(raw);

        ThrowIfGraphQlError(document.RootElement);
        return true;
    }

    public static TomorrowReservationInfo? MapTomorrowReservationInfo(string raw)
    {
        using var document = JsonDocument.Parse(raw);

        ThrowIfGraphQlError(document.RootElement);
        var prereserveNode = document.RootElement
            .GetProperty("data")
            .GetProperty("userAuth")
            .GetProperty("prereserve");

        if (!prereserveNode.TryGetProperty("prereserve", out var reservation) ||
            reservation.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return new TomorrowReservationInfo(
            ReadOptionalStringProperty(reservation, "day"),
            ReadOptionalIntProperty(reservation, "lib_id"),
            ReadOptionalStringProperty(reservation, "seat_key"),
            ReadOptionalStringProperty(reservation, "seat_name"),
            TryReadBooleanLikeProperty(reservation, "is_used", out var isUsed) && isUsed);
    }

    private static void ThrowIfGraphQlError(JsonElement root)
    {
        if (TryGetAuthorizationDeniedError(root, out var authError))
        {
            throw new TraceIntApiException(
                authError.Message,
                authError.Code,
                authError.Message,
                isAuthorizationDenied: true);
        }

        if (TryFindErrorInfo(root, out var errorInfo))
        {
            throw new TraceIntApiException(
                errorInfo.Message,
                errorInfo.Code,
                errorInfo.Message,
                isAuthorizationDenied: IsAccessDeniedMessage(errorInfo.Message));
        }
    }

    private static bool TryGetAuthorizationDeniedError(JsonElement root, out GraphQlErrorInfo errorInfo)
    {
        errorInfo = default;
        if (root.ValueKind is not JsonValueKind.Object ||
            !root.TryGetProperty("errors", out var errors) ||
            errors.ValueKind is not JsonValueKind.Array ||
            errors.GetArrayLength() == 0 ||
            !TryReadErrorInfo(errors[0], out var candidate) ||
            candidate.Code != 40001 ||
            !IsAccessDeniedMessage(candidate.Message))
        {
            return false;
        }

        if (!root.TryGetProperty("data", out var data) || data.ValueKind is not JsonValueKind.Object)
        {
            return false;
        }

        if (!data.TryGetProperty("userAuth", out var userAuth) || userAuth.ValueKind is not JsonValueKind.Null)
        {
            return false;
        }

        errorInfo = candidate;
        return true;
    }

    private static bool TryFindErrorInfo(JsonElement element, out GraphQlErrorInfo errorInfo)
    {
        if (TryReadErrorInfo(element, out errorInfo))
        {
            return true;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (TryFindErrorInfo(property.Value, out errorInfo))
                    {
                        return true;
                    }
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    if (TryFindErrorInfo(item, out errorInfo))
                    {
                        return true;
                    }
                }

                break;
        }

        errorInfo = default;
        return false;
    }

    private static bool TryReadErrorInfo(JsonElement element, out GraphQlErrorInfo errorInfo)
    {
        errorInfo = default;
        if (element.ValueKind is not JsonValueKind.Object)
        {
            return false;
        }

        if (!element.TryGetProperty("message", out var messageElement) ||
            messageElement.ValueKind is not JsonValueKind.String)
        {
            if (!element.TryGetProperty("msg", out messageElement) ||
                messageElement.ValueKind is not JsonValueKind.String)
            {
                return false;
            }
        }

        var message = messageElement.GetString();
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        int? code = null;
        if (element.TryGetProperty("code", out var codeElement))
        {
            code = codeElement.ValueKind switch
            {
                JsonValueKind.Number when codeElement.TryGetInt32(out var intValue) => intValue,
                JsonValueKind.String when int.TryParse(codeElement.GetString(), out var intValue) => intValue,
                _ => null
            };
        }

        errorInfo = new GraphQlErrorInfo(message, code);
        return true;
    }

    private static bool IsAccessDeniedMessage(string message)
    {
        return string.Equals(message.Trim(), "access denied!", StringComparison.OrdinalIgnoreCase)
            || string.Equals(message.Trim(), "access denied", StringComparison.OrdinalIgnoreCase);
    }

    private readonly record struct GraphQlErrorInfo(string Message, int? Code);

    private static bool ReadBooleanLike(JsonElement element, string fieldName)
    {
        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => string.Equals(element.GetString(), "true", StringComparison.OrdinalIgnoreCase),
            JsonValueKind.Number when element.TryGetInt32(out var intValue) => intValue != 0,
            _ => throw new InvalidOperationException($"字段 {fieldName} 的返回类型不受支持: {element.ValueKind}")
        };
    }

    private static bool IsSeatLayoutItem(JsonElement element)
    {
        if (element.ValueKind is not JsonValueKind.Object)
        {
            return false;
        }

        if (!element.TryGetProperty("type", out _))
        {
            return true;
        }

        return TryReadRequiredIntProperty(element, "type", out var type) && type == 1;
    }

    private static bool TryReadBooleanLikeProperty(JsonElement element, string propertyName, out bool value)
    {
        value = default;
        if (element.ValueKind is not JsonValueKind.Object || !element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        try
        {
            value = ReadBooleanLike(property, propertyName);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TryReadRequiredIntProperty(JsonElement element, string propertyName, out int value)
    {
        value = default;
        if (element.ValueKind is not JsonValueKind.Object || !element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt32(out value) => true,
            JsonValueKind.String when int.TryParse(property.GetString(), out value) => true,
            _ => false
        };
    }

    private static string ReadOptionalStringProperty(JsonElement element, string propertyName)
    {
        if (element.ValueKind is not JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind is JsonValueKind.Null)
        {
            return string.Empty;
        }

        return property.ValueKind is JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : property.ToString();
    }

    private static int ReadOptionalIntProperty(JsonElement element, string propertyName)
    {
        if (element.ValueKind is not JsonValueKind.Object || !element.TryGetProperty(propertyName, out var property))
        {
            return 0;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt32(out var intValue) => intValue,
            JsonValueKind.String when int.TryParse(property.GetString(), out var intValue) => intValue,
            _ => 0
        };
    }
}
