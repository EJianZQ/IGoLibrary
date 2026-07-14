using IGoLibrary.Ex.Updater.Core;

namespace IGoLibrary.Ex.Desktop.Services;

internal sealed record VerifiedUpdateCache(
    int SchemaVersion,
    string TransactionId,
    string TargetVersion,
    string PackageDigest,
    long PackageSize,
    DateTimeOffset VerifiedAtUtc)
{
    public static bool IsStructurallyValid(
        VerifiedUpdateCache cache,
        string expectedTransactionId)
    {
        return cache.SchemaVersion == UpdateProtocol.SchemaVersion &&
               string.Equals(
                   cache.TransactionId,
                   expectedTransactionId,
                   StringComparison.Ordinal) &&
               StableUpdateVersion.TryParseCanonical(cache.TargetVersion, out _) &&
               cache.PackageSize is > 0 and <= 512L * 1024 * 1024 &&
               cache.PackageDigest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) &&
               cache.PackageDigest.Length == 71 &&
               cache.PackageDigest[7..].All(Uri.IsHexDigit) &&
               cache.VerifiedAtUtc != default;
    }
}
