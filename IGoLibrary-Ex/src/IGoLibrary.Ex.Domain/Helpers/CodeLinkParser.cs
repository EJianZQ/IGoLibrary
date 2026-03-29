using System.Text.RegularExpressions;

namespace IGoLibrary.Ex.Domain.Helpers;

public static partial class CodeLinkParser
{
    [GeneratedRegex(@"code=([A-Za-z0-9]{32})", RegexOptions.IgnoreCase)]
    private static partial Regex CodeRegex();

    public static bool TryExtractCode(string? url, out string code)
    {
        code = string.Empty;
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        var match = CodeRegex().Match(url);
        if (!match.Success)
        {
            return false;
        }

        code = match.Groups[1].Value;
        return code.Length == 32;
    }
}
