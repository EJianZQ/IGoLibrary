namespace IGoLibrary.Ex.Application.Services;

public static class RemoteCheckInSessionTokenValidator
{
    public static bool TryNormalize(string? value, out string normalized)
    {
        var candidate = value?.Trim() ?? string.Empty;
        if (candidate.Length >= 2 && candidate[0] == '"' && candidate[^1] == '"')
        {
            candidate = candidate[1..^1].Trim();
        }

        if (candidate.Length is not (40 or 48) || !candidate.All(Uri.IsHexDigit))
        {
            normalized = string.Empty;
            return false;
        }

        normalized = candidate.ToLowerInvariant();
        return true;
    }
}
