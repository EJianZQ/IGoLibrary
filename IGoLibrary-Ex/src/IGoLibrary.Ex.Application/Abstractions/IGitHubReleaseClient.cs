namespace IGoLibrary.Ex.Application.Abstractions;

public interface IGitHubReleaseClient
{
    Task<GitHubReleaseQueryResult> GetReleasesAsync(
        string? etag,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}

public sealed record GitHubReleaseQueryResult(
    bool NotModified,
    string? ETag,
    IReadOnlyList<GitHubReleaseItem> Releases);

public sealed record GitHubReleaseItem(
    string TagName,
    string? Name,
    string? Body,
    Uri HtmlUrl,
    DateTimeOffset? PublishedAt,
    bool Draft,
    bool Prerelease,
    IReadOnlyList<string> AssetNames);
