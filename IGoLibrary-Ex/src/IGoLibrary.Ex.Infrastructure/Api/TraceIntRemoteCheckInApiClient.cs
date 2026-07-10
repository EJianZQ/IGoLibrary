using System.Net;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Infrastructure.Api;

internal sealed class TraceIntRemoteCheckInApiClient(TraceIntRemoteCheckInTransport transport)
    : IRemoteCheckInApiClient
{
    public async Task<string> ExchangeOAuthCodeAsync(
        string code,
        CancellationToken cancellationToken = default)
    {
        if (code.Length != 32 || !code.All(char.IsAsciiLetterOrDigit))
        {
            throw new ArgumentException("签到授权 code 必须是 32 位字母数字。", nameof(code));
        }

        using var response = await transport.SendAuthorizationAsync(code, cancellationToken);
        var token = ExtractSessionToken(response);
        if (token is not null)
        {
            return token;
        }

        if ((int)response.StatusCode >= 400)
        {
            throw new HttpRequestException(
                $"获取签到授权失败，HTTP {(int)response.StatusCode} {response.StatusCode}。",
                null,
                response.StatusCode);
        }

        throw new InvalidOperationException("授权响应未返回 wechatSESS_ID，授权链接可能已被使用或已过期，请重新扫码获取。");
    }

    public async Task<RemoteCheckInDeviceInfo> GetDeviceInfoAsync(
        string sessionToken,
        CancellationToken cancellationToken = default)
    {
        using var response = await transport.SendDevicesAsync(sessionToken, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        return TraceIntRemoteCheckInResponseMapper.MapDeviceInfo(raw);
    }

    public async Task<RemoteCheckInServerTime> GetServerTimeAsync(CancellationToken cancellationToken = default)
    {
        using var response = await transport.SendServerTimeAsync(cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        return TraceIntRemoteCheckInResponseMapper.MapServerTime(raw);
    }

    public async Task<RemoteCheckInResult> SignAsync(
        string sessionToken,
        RemoteCheckInSignRequest request,
        CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["t"] = sessionToken,
            ["devices"] = TraceIntRemoteCheckInPayloadEncoder.EncodeDevices(request),
            ["location"] = TraceIntRemoteCheckInPayloadEncoder.EncodeLocation(request),
            ["pass"] = TraceIntRemoteCheckInPayloadEncoder.EncryptTimestamp(request.ServerTimestamp)
        };
        using var response = await transport.SendSignAsync(form, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        return TraceIntRemoteCheckInResponseMapper.MapSignResult(raw);
    }

    internal static string? ExtractSessionToken(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            return null;
        }

        foreach (var value in values)
        {
            var cookieHeader = value.Trim();
            if (cookieHeader.Length >= 2 && cookieHeader[0] == '"' && cookieHeader[^1] == '"')
            {
                cookieHeader = cookieHeader[1..^1].Trim();
            }

            var firstSegment = cookieHeader.Split(';', 2)[0];
            var separatorIndex = firstSegment.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var name = firstSegment[..separatorIndex].Trim();
            var rawToken = firstSegment[(separatorIndex + 1)..].Trim();
            if (name.Equals("wechatSESS_ID", StringComparison.OrdinalIgnoreCase) &&
                RemoteCheckInSessionTokenValidator.TryNormalize(rawToken, out var token))
            {
                return token;
            }
        }

        return null;
    }
}
