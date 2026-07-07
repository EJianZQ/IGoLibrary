using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace IGoLibrary.Ex.Desktop.Services;

internal static class SubmittedLinkReader
{
    public const long MaxRequestBodyBytes = 8 * 1024;

    public static async Task<string> ReadLinkAsync(
        HttpContext context,
        long maxRequestBodyBytes = MaxRequestBodyBytes)
    {
        if (context.Request.ContentLength > maxRequestBodyBytes)
        {
            throw new SubmittedLinkBodyTooLargeException();
        }

        using var stream = new MemoryStream();
        var buffer = new byte[1024];
        long totalBytes = 0;
        while (true)
        {
            var bytesRead = await context.Request.Body.ReadAsync(buffer, context.RequestAborted);
            if (bytesRead == 0)
            {
                break;
            }

            totalBytes += bytesRead;
            if (totalBytes > maxRequestBodyBytes)
            {
                throw new SubmittedLinkBodyTooLargeException();
            }

            stream.Write(buffer, 0, bytesRead);
        }

        return ExtractLink(Encoding.UTF8.GetString(stream.ToArray()).Trim());
    }

    private static string ExtractLink(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        if (body.StartsWith('{'))
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("link", out var linkElement) &&
                linkElement.ValueKind == JsonValueKind.String)
            {
                return linkElement.GetString()?.Trim() ?? string.Empty;
            }

            return string.Empty;
        }

        if (body.Contains("link=", StringComparison.Ordinal))
        {
            foreach (var segment in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = segment.Split('=', 2);
                if (parts.Length == 2 &&
                    string.Equals(Uri.UnescapeDataString(parts[0]), "link", StringComparison.Ordinal))
                {
                    return Uri.UnescapeDataString(parts[1].Replace('+', ' ')).Trim();
                }
            }
        }

        return body;
    }
}

internal sealed class SubmittedLinkBodyTooLargeException : Exception
{
}
