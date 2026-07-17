using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;

namespace IGoLibrary.Ex.Desktop.Services;

internal static class MobileControlTaskStartRequestReader
{
    private const int MaximumBodyBytes = 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static async Task<string?> ReadRecordIdAsync(HttpRequest request)
    {
        if (!request.HasJsonContentType())
        {
            throw new MobileControlTaskStartBodyException("请求内容格式无效，请提交 JSON");
        }

        var body = await ReadBodyAsync(request);
        if (body.Length == 0)
        {
            throw new MobileControlTaskStartBodyException("缺少任务记录 ID");
        }

        var requestBody = JsonSerializer.Deserialize<MobileControlTaskStartRequest>(
            body,
            JsonOptions);
        return requestBody?.RecordId;
    }

    public static async Task EnsureEmptyBodyAsync(HttpRequest request)
    {
        if ((await ReadBodyAsync(request)).Length > 0)
        {
            throw new MobileControlTaskStartBodyException("占座启动请求不接受提交内容");
        }
    }

    private static async Task<byte[]> ReadBodyAsync(HttpRequest request)
    {
        if (request.ContentLength > MaximumBodyBytes)
        {
            throw new MobileControlTaskStartBodyTooLargeException();
        }

        var buffer = new byte[MaximumBodyBytes + 1];
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = await request.Body.ReadAsync(
                buffer.AsMemory(totalRead, buffer.Length - totalRead),
                request.HttpContext.RequestAborted);
            if (read == 0)
            {
                break;
            }

            totalRead += read;
        }

        if (totalRead > MaximumBodyBytes)
        {
            throw new MobileControlTaskStartBodyTooLargeException();
        }

        return buffer.AsSpan(0, totalRead).ToArray();
    }

    private sealed record MobileControlTaskStartRequest(string? RecordId);
}

internal sealed class MobileControlTaskStartBodyException(string message) : Exception(message);

internal sealed class MobileControlTaskStartBodyTooLargeException : Exception
{
}
