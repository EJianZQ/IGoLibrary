using System.Globalization;

namespace IGoLibrary.Ex.Updater.Core;

public readonly record struct StableUpdateVersion(
    int Major,
    int Minor,
    int Patch) : IComparable<StableUpdateVersion>
{
    public static bool TryParseCanonical(string? value, out StableUpdateVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split('.', StringSplitOptions.None);
        if (parts.Length != 3 ||
            !TryParseComponent(parts[0], out var major) ||
            !TryParseComponent(parts[1], out var minor) ||
            !TryParseComponent(parts[2], out var patch))
        {
            return false;
        }

        version = new StableUpdateVersion(major, minor, patch);
        return true;
    }

    public int CompareTo(StableUpdateVersion other)
    {
        var comparison = Major.CompareTo(other.Major);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = Minor.CompareTo(other.Minor);
        return comparison != 0 ? comparison : Patch.CompareTo(other.Patch);
    }

    public override string ToString()
    {
        return string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}");
    }

    private static bool TryParseComponent(string value, out int result)
    {
        result = 0;
        return value.Length > 0 &&
               value.All(static character => char.IsAsciiDigit(character)) &&
               (value.Length == 1 || value[0] != '0') &&
               int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result);
    }
}
