using System.Globalization;

namespace IGoLibrary.Ex.Application.Updates;

public sealed record ReleaseVersion(
    int Major,
    int Minor,
    int Patch) : IComparable<ReleaseVersion>
{
    public static bool TryParse(string? value, out ReleaseVersion version)
    {
        version = default!;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();
        if (text.StartsWith('v') || text.StartsWith('V'))
        {
            text = text[1..];
        }

        var parts = text.Split('.', StringSplitOptions.None);
        if (parts.Length != 3 ||
            !TryParseCanonicalComponent(parts[0], out var major) ||
            !TryParseCanonicalComponent(parts[1], out var minor) ||
            !TryParseCanonicalComponent(parts[2], out var patch))
        {
            return false;
        }

        version = new ReleaseVersion(major, minor, patch);
        return true;
    }

    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

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

    private static bool TryParseCanonicalComponent(string value, out int result)
    {
        result = 0;
        return value.Length > 0 &&
               value.All(static character => char.IsAsciiDigit(character)) &&
               (value.Length == 1 || value[0] != '0') &&
               int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result);
    }

    public static bool operator >(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) > 0;

    public static bool operator <(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) < 0;

    public static bool operator >=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) >= 0;

    public static bool operator <=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) <= 0;
}
