using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using IGoLibrary.Ex.Application.Abstractions;

namespace IGoLibrary.Ex.Infrastructure.Updates;

public sealed class GitHubReleaseClient(HttpClient httpClient) : IGitHubReleaseClient
{
    private static readonly Uri ReleasesUri = new("https://api.github.com/repos/EJianZQ/IGoLibrary/releases?per_page=20");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<GitHubReleaseQueryResult> GetReleasesAsync(
        string? etag,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesUri);
        if (!string.IsNullOrWhiteSpace(etag))
        {
            if (EntityTagHeaderValue.TryParse(etag, out var parsedEtag))
            {
                request.Headers.IfNoneMatch.Add(parsedEtag);
            }
            else
            {
                request.Headers.TryAddWithoutValidation("If-None-Match", etag);
            }
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutCts.Token);

            var responseEtag = response.Headers.ETag?.ToString();
            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                return new GitHubReleaseQueryResult(true, responseEtag ?? etag, []);
            }

            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(timeoutCts.Token);
            var releases = await JsonSerializer.DeserializeAsync<List<GitHubReleaseDto>>(
                stream,
                JsonOptions,
                timeoutCts.Token) ?? [];

            return new GitHubReleaseQueryResult(
                false,
                responseEtag,
                releases.Select(MapRelease).OfType<GitHubReleaseItem>().ToArray());
        }
        catch (OperationCanceledException ex) when (
            !cancellationToken.IsCancellationRequested &&
            timeoutCts.IsCancellationRequested)
        {
            throw new TimeoutException("GitHub Release request timed out.", ex);
        }
    }

    private static GitHubReleaseItem? MapRelease(GitHubReleaseDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.TagName))
        {
            return null;
        }

        if (!Uri.TryCreate(dto.HtmlUrl, UriKind.Absolute, out var htmlUrl))
        {
            htmlUrl = new Uri($"https://github.com/EJianZQ/IGoLibrary/releases/tag/{Uri.EscapeDataString(dto.TagName)}");
        }

        return new GitHubReleaseItem(
            dto.TagName.Trim(),
            dto.Name,
            dto.Body,
            htmlUrl,
            dto.PublishedAt,
            dto.Draft,
            dto.Prerelease,
            dto.Assets?
                .Select(static asset => asset.Name)
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .Select(static name => name!)
                .ToArray() ?? []);
    }

    private sealed record GitHubReleaseDto(
        [property: JsonPropertyName("tag_name")] string? TagName,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("body")] string? Body,
        [property: JsonPropertyName("html_url")] string? HtmlUrl,
        [property: JsonPropertyName("published_at")] DateTimeOffset? PublishedAt,
        [property: JsonPropertyName("draft")] bool Draft,
        [property: JsonPropertyName("prerelease")] bool Prerelease,
        [property: JsonPropertyName("assets")] IReadOnlyList<GitHubReleaseAssetDto>? Assets);

    private sealed record GitHubReleaseAssetDto(
        [property: JsonPropertyName("name")] string? Name);
}
