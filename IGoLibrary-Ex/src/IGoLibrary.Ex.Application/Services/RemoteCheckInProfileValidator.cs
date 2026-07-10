using IGoLibrary.Ex.Application.Configuration;

namespace IGoLibrary.Ex.Application.Services;

public static class RemoteCheckInProfileValidator
{
    public static RemoteCheckInVenueProfileSettings NormalizeAndValidate(
        RemoteCheckInVenueProfileSettings profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (profile.LibraryId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(profile), "场馆编号无效。");
        }

        if (!TryNormalizeUuid(profile.BeaconUuid, out var beaconUuid))
        {
            throw new ArgumentException("请选择服务端返回的有效 Beacon UUID。", nameof(profile));
        }

        if (profile.Major is not >= ushort.MinValue or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(profile), "Major 必须介于 0 和 65535 之间。");
        }

        if (profile.Minor is not >= ushort.MinValue or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(profile), "Minor 必须介于 0 和 65535 之间。");
        }

        if (profile.Latitude is not >= -90m or > 90m)
        {
            throw new ArgumentOutOfRangeException(nameof(profile), "纬度必须介于 -90 和 90 之间。");
        }

        if (profile.Longitude is not >= -180m or > 180m)
        {
            throw new ArgumentOutOfRangeException(nameof(profile), "经度必须介于 -180 和 180 之间。");
        }

        return profile with
        {
            LibraryName = profile.LibraryName?.Trim() ?? string.Empty,
            BeaconUuid = beaconUuid
        };
    }

    public static bool TryNormalizeUuid(string? value, out string normalized)
    {
        if (Guid.TryParse(value?.Trim(), out var uuid))
        {
            normalized = uuid.ToString("D").ToUpperInvariant();
            return true;
        }

        normalized = string.Empty;
        return false;
    }
}
