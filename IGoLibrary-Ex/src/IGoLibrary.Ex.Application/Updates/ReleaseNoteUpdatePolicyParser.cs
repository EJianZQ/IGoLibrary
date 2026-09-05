using System.Text;

namespace IGoLibrary.Ex.Application.Updates;

internal static class ReleaseNoteUpdatePolicyParser
{
    internal const string MarkerKey = "IGoLibrary-Ex-MinAutoUpdateVersion";

    public static ReleaseNoteUpdatePolicyParseResult Parse(
        string? releaseBody,
        ReleaseVersion targetVersion)
    {
        ArgumentNullException.ThrowIfNull(targetVersion);

        var body = releaseBody ?? string.Empty;
        var lines = SplitLines(body);
        var markerLineIndexes = lines
            .Select(static (line, index) => (line, index))
            .Where(static item => ContainsReservedMarkerKey(item.line.Content))
            .Select(static item => item.index)
            .ToArray();
        var displayBody = RemoveReservedMarkerLines(lines);

        if (markerLineIndexes.Length == 0)
        {
            return new ReleaseNoteUpdatePolicyParseResult(
                AutomaticUpdatePolicy.Unrestricted,
                displayBody,
                ReleaseNoteUpdatePolicyIssue.None);
        }

        if (markerLineIndexes.Length > 1)
        {
            return Invalid(displayBody, ReleaseNoteUpdatePolicyIssue.DuplicateMarker);
        }

        var markerLineIndex = markerLineIndexes[0];
        if (!TryParseMarkerLine(lines[markerLineIndex].Content, out var minimumVersion))
        {
            return Invalid(displayBody, ReleaseNoteUpdatePolicyIssue.InvalidMarkerSyntax);
        }

        var firstContentLineIndex = lines.FindIndex(
            static line => !string.IsNullOrWhiteSpace(line.Content));
        if (markerLineIndex != firstContentLineIndex)
        {
            return Invalid(displayBody, ReleaseNoteUpdatePolicyIssue.MarkerNotFirstContentLine);
        }

        if (minimumVersion > targetVersion)
        {
            return Invalid(displayBody, ReleaseNoteUpdatePolicyIssue.MinimumVersionAfterTarget);
        }

        return new ReleaseNoteUpdatePolicyParseResult(
            AutomaticUpdatePolicy.RequireMinimumVersion(minimumVersion),
            displayBody,
            ReleaseNoteUpdatePolicyIssue.None);
    }

    private static ReleaseNoteUpdatePolicyParseResult Invalid(
        string displayBody,
        ReleaseNoteUpdatePolicyIssue issue)
    {
        return new ReleaseNoteUpdatePolicyParseResult(
            AutomaticUpdatePolicy.Invalid,
            displayBody,
            issue);
    }

    private static bool TryParseMarkerLine(
        string line,
        out ReleaseVersion minimumVersion)
    {
        minimumVersion = default!;
        var trimmedLine = line.Trim(' ', '\t');
        if (!trimmedLine.StartsWith("<!--", StringComparison.Ordinal) ||
            !trimmedLine.EndsWith("-->", StringComparison.Ordinal))
        {
            return false;
        }

        var content = trimmedLine[4..^3].Trim(' ', '\t');
        var separatorIndex = content.IndexOf(':');
        if (separatorIndex < 0 ||
            !string.Equals(
                content[..separatorIndex].Trim(' ', '\t'),
                MarkerKey,
                StringComparison.Ordinal))
        {
            return false;
        }

        var versionText = content[(separatorIndex + 1)..].Trim(' ', '\t');
        return ReleaseVersion.TryParse(versionText, out minimumVersion) &&
               string.Equals(
                   versionText,
                   minimumVersion.ToString(),
                   StringComparison.Ordinal);
    }

    private static string RemoveReservedMarkerLines(IReadOnlyList<ReleaseNoteLine> lines)
    {
        var builder = new StringBuilder();
        foreach (var line in lines)
        {
            if (ContainsReservedMarkerKey(line.Content))
            {
                continue;
            }

            builder.Append(line.Content);
            builder.Append(line.Terminator);
        }

        return builder.ToString();
    }

    private static bool ContainsReservedMarkerKey(string line)
    {
        return line.Contains(MarkerKey, StringComparison.OrdinalIgnoreCase);
    }

    private static List<ReleaseNoteLine> SplitLines(string value)
    {
        var lines = new List<ReleaseNoteLine>();
        var lineStart = 0;
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] is not ('\r' or '\n'))
            {
                continue;
            }

            var terminatorLength = value[index] == '\r' &&
                                   index + 1 < value.Length &&
                                   value[index + 1] == '\n'
                ? 2
                : 1;
            lines.Add(new ReleaseNoteLine(
                value[lineStart..index],
                value.Substring(index, terminatorLength)));
            index += terminatorLength - 1;
            lineStart = index + 1;
        }

        if (lineStart < value.Length)
        {
            lines.Add(new ReleaseNoteLine(value[lineStart..], string.Empty));
        }

        return lines;
    }

    private sealed record ReleaseNoteLine(string Content, string Terminator);
}

internal enum ReleaseNoteUpdatePolicyIssue
{
    None,
    InvalidMarkerSyntax,
    MarkerNotFirstContentLine,
    DuplicateMarker,
    MinimumVersionAfterTarget
}

internal sealed record ReleaseNoteUpdatePolicyParseResult(
    AutomaticUpdatePolicy Policy,
    string DisplayBody,
    ReleaseNoteUpdatePolicyIssue Issue);
