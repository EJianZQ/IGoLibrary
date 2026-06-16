using System.Globalization;

namespace IGoLibrary.Ex.Application.Updates;

public sealed record ReleaseVersion(
    int Major,
    int Minor,
    int Patch,
    string? PrereleaseLabel = null,
    int? PrereleaseNumber = null) : IComparable<ReleaseVersion>
{
    public bool IsPrerelease => !string.IsNullOrWhiteSpace(PrereleaseLabel);

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

        var dashIndex = text.IndexOf('-');
        var core = dashIndex >= 0 ? text[..dashIndex] : text;
        var prerelease = dashIndex >= 0 ? text[(dashIndex + 1)..] : null;
        var coreParts = core.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (coreParts.Length is < 2 or > 3)
        {
            return false;
        }

        if (!TryParseNonNegative(coreParts[0], out var major) ||
            !TryParseNonNegative(coreParts[1], out var minor))
        {
            return false;
        }

        var patch = 0;
        if (coreParts.Length == 3 && !TryParseNonNegative(coreParts[2], out patch))
        {
            return false;
        }

        string? prereleaseLabel = null;
        int? prereleaseNumber = null;
        if (!string.IsNullOrWhiteSpace(prerelease))
        {
            var prereleaseParts = prerelease.Split(
                '.',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (prereleaseParts.Length is < 1 or > 2 ||
                !IsValidPrereleaseLabel(prereleaseParts[0]))
            {
                return false;
            }

            prereleaseLabel = prereleaseParts[0].ToLowerInvariant();
            var parsedPrereleaseNumber = 0;
            if (prereleaseParts.Length == 2 &&
                (!TryParseNonNegative(prereleaseParts[1], out parsedPrereleaseNumber) ||
                 parsedPrereleaseNumber == 0))
            {
                return false;
            }

            if (prereleaseParts.Length == 2)
            {
                prereleaseNumber = parsedPrereleaseNumber;
            }
        }

        version = new ReleaseVersion(major, minor, patch, prereleaseLabel, prereleaseNumber);
        return true;
    }

    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var coreComparison = Major.CompareTo(other.Major);
        if (coreComparison != 0)
        {
            return coreComparison;
        }

        coreComparison = Minor.CompareTo(other.Minor);
        if (coreComparison != 0)
        {
            return coreComparison;
        }

        coreComparison = Patch.CompareTo(other.Patch);
        if (coreComparison != 0)
        {
            return coreComparison;
        }

        if (!IsPrerelease && other.IsPrerelease)
        {
            return 1;
        }

        if (IsPrerelease && !other.IsPrerelease)
        {
            return -1;
        }

        if (!IsPrerelease && !other.IsPrerelease)
        {
            return 0;
        }

        var labelComparison = GetPrereleaseRank(PrereleaseLabel)
            .CompareTo(GetPrereleaseRank(other.PrereleaseLabel));
        if (labelComparison != 0)
        {
            return labelComparison;
        }

        labelComparison = string.Compare(
            PrereleaseLabel,
            other.PrereleaseLabel,
            StringComparison.OrdinalIgnoreCase);
        if (labelComparison != 0)
        {
            return labelComparison;
        }

        return ComparePrereleaseNumber(PrereleaseNumber, other.PrereleaseNumber);
    }

    public override string ToString()
    {
        var core = string.Create(
            CultureInfo.InvariantCulture,
            $"{Major}.{Minor}.{Patch}");
        if (!IsPrerelease)
        {
            return core;
        }

        return PrereleaseNumber is null
            ? $"{core}-{PrereleaseLabel}"
            : $"{core}-{PrereleaseLabel}.{PrereleaseNumber.Value}";
    }

    private static bool TryParseNonNegative(string value, out int result)
    {
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result) &&
               result >= 0;
    }

    private static bool IsValidPrereleaseLabel(string value)
    {
        return value.Length > 0 &&
               value.All(static c => char.IsAsciiLetterOrDigit(c) || c == '-');
    }

    private static int GetPrereleaseRank(string? label)
    {
        return label?.ToLowerInvariant() switch
        {
            "alpha" => 0,
            "beta" => 1,
            "preview" => 1,
            "rc" => 2,
            _ => 3
        };
    }

    private static int ComparePrereleaseNumber(int? left, int? right)
    {
        return (left, right) switch
        {
            (null, null) => 0,
            (null, _) => -1,
            (_, null) => 1,
            _ => left.Value.CompareTo(right.Value)
        };
    }

    public static bool operator >(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) > 0;

    public static bool operator <(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) < 0;

    public static bool operator >=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) >= 0;

    public static bool operator <=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) <= 0;
}
