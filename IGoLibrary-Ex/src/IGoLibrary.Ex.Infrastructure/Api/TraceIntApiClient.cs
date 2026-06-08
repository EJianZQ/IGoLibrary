using System.Net;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Domain.Models;
using RestSharp;

namespace IGoLibrary.Ex.Infrastructure.Api;

internal sealed class TraceIntApiClient(
    TraceIntCookieTransport cookieTransport,
    IProtocolTemplateStore protocolTemplateStore,
    TraceIntGraphQlTransport graphQlTransport,
    TraceIntTomorrowReservationQueueTransport tomorrowReservationQueueTransport) : ITraceIntApiClient
{
    public async Task<string> GetCookieFromCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var result = await cookieTransport.GetCookieAsync(code, cancellationToken);
        ThrowIfCookieResponseFailed(result.Response, result.Cookies);
        return BuildCookieHeaderFromResponseCookies(result.Cookies);
    }

    public async Task ValidateCookieAsync(string cookie, CancellationToken cancellationToken = default)
    {
        _ = await GetLibrariesAsync(cookie, cancellationToken);
    }

    public async Task<IReadOnlyList<LibrarySummary>> GetLibrariesAsync(string cookie, CancellationToken cancellationToken = default)
    {
        var templates = await protocolTemplateStore.GetEffectiveTemplatesAsync(cancellationToken);
        using var response = await graphQlTransport.SendAsync(cookie, templates.QueryLibrariesTemplate, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        return TraceIntGraphQlResponseMapper.MapLibraries(raw);
    }

    public async Task<LibraryLayout> GetLibraryLayoutAsync(string cookie, int libraryId, CancellationToken cancellationToken = default)
    {
        var templates = await protocolTemplateStore.GetEffectiveTemplatesAsync(cancellationToken);
        var payload = templates.QueryLibraryLayoutTemplate.Replace("ReplaceMe", libraryId.ToString(), StringComparison.Ordinal);
        using var response = await graphQlTransport.SendAsync(cookie, payload, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        return TraceIntGraphQlResponseMapper.MapLibraryLayout(raw);
    }

    public async Task<LibraryRule> GetLibraryRuleAsync(string cookie, int libraryId, CancellationToken cancellationToken = default)
    {
        var templates = await protocolTemplateStore.GetEffectiveTemplatesAsync(cancellationToken);
        var payload = templates.QueryLibraryRuleTemplate.Replace("ReplaceMe", libraryId.ToString(), StringComparison.Ordinal);
        using var response = await graphQlTransport.SendAsync(cookie, payload, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        return TraceIntGraphQlResponseMapper.MapLibraryRule(raw, libraryId);
    }

    public async Task<ReservationInfo?> GetReservationInfoAsync(string cookie, CancellationToken cancellationToken = default)
    {
        var templates = await protocolTemplateStore.GetEffectiveTemplatesAsync(cancellationToken);
        using var response = await graphQlTransport.SendAsync(cookie, templates.QueryReservationInfoTemplate, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        return TraceIntGraphQlResponseMapper.MapReservationInfo(raw);
    }

    public async Task<bool> ReserveSeatAsync(string cookie, int libraryId, string seatKey, CancellationToken cancellationToken = default)
    {
        var templates = await protocolTemplateStore.GetEffectiveTemplatesAsync(cancellationToken);
        var payload = templates.ReserveSeatTemplate
            .Replace("ReplaceMeBySeatKey", seatKey, StringComparison.Ordinal)
            .Replace("ReplaceMeByLibID", libraryId.ToString(), StringComparison.Ordinal);

        using var response = await graphQlTransport.SendAsync(cookie, payload, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        return TraceIntGraphQlResponseMapper.MapReserveSeat(raw);
    }

    public async Task<bool> CancelReservationAsync(string cookie, string reservationToken, CancellationToken cancellationToken = default)
    {
        var templates = await protocolTemplateStore.GetEffectiveTemplatesAsync(cancellationToken);
        var payload = templates.CancelReservationTemplate.Replace("ReplaceMe", reservationToken, StringComparison.Ordinal);

        using var response = await graphQlTransport.SendAsync(cookie, payload, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        return TraceIntGraphQlResponseMapper.MapCancelReservation(raw);
    }

    public async Task<TomorrowReservationQueueResult> EnterTomorrowReservationQueueAsync(
        string cookie,
        CancellationToken cancellationToken = default)
    {
        var templates = await protocolTemplateStore.GetEffectiveTemplatesAsync(cancellationToken);
        return await tomorrowReservationQueueTransport.EnterAsync(
            templates.TomorrowReservationQueueUrlTemplate,
            cookie,
            cancellationToken);
    }

    public async Task WarmUpTomorrowReservationAsync(
        string cookie,
        int libraryId,
        CancellationToken cancellationToken = default)
    {
        var templates = await protocolTemplateStore.GetEffectiveTemplatesAsync(cancellationToken);
        var payload = templates.TomorrowReservationWarmUpTemplate
            .Replace("ReplaceMeByLibID", libraryId.ToString(), StringComparison.Ordinal)
            .Replace("ReplaceMe", libraryId.ToString(), StringComparison.Ordinal);

        using var response = await graphQlTransport.SendAsync(
            cookie,
            payload,
            TraceIntGraphQlTransport.TomorrowReservationProfile,
            cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        TraceIntGraphQlResponseMapper.MapTomorrowReservationWarmUp(raw);
    }

    public async Task<bool> SaveTomorrowReservationAsync(
        string cookie,
        int libraryId,
        string seatKey,
        CancellationToken cancellationToken = default)
    {
        var templates = await protocolTemplateStore.GetEffectiveTemplatesAsync(cancellationToken);
        var payload = templates.TomorrowReservationSaveTemplate
            .Replace("ReplaceMeBySeatKey", $"{seatKey}.", StringComparison.Ordinal)
            .Replace("ReplaceMeByLibID", libraryId.ToString(), StringComparison.Ordinal);

        using var response = await graphQlTransport.SendAsync(
            cookie,
            payload,
            TraceIntGraphQlTransport.TomorrowReservationProfile,
            cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        return TraceIntGraphQlResponseMapper.MapTomorrowReservationSave(raw);
    }

    public async Task<TomorrowReservationInfo?> GetTomorrowReservationInfoAsync(
        string cookie,
        CancellationToken cancellationToken = default)
    {
        var templates = await protocolTemplateStore.GetEffectiveTemplatesAsync(cancellationToken);
        using var response = await graphQlTransport.SendAsync(
            cookie,
            templates.TomorrowReservationInfoTemplate,
            TraceIntGraphQlTransport.TomorrowReservationProfile,
            cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        return TraceIntGraphQlResponseMapper.MapTomorrowReservationInfo(raw);
    }

    internal static string BuildCookieHeaderFromResponseCookies(IReadOnlyList<string>? responseCookies)
    {
        if (responseCookies is null)
        {
            throw new InvalidOperationException("响应报文返回的Cookie为空");
        }

        if (responseCookies.Count < 2)
        {
            throw new InvalidOperationException("Cookie不包含关键身份信息，可能是code过期，重新填写含code的链接");
        }

        return $"{responseCookies[1]}; {responseCookies[0]}";
    }

    internal static void ThrowIfCookieResponseFailed(RestResponse response, IReadOnlyList<string>? responseCookies)
    {
        if (response.IsSuccessful || responseCookies?.Count >= 2)
        {
            return;
        }

        var reason = response.ErrorMessage;
        if (string.IsNullOrWhiteSpace(reason))
        {
            reason = response.StatusDescription;
        }

        if (string.IsNullOrWhiteSpace(reason) && response.ResponseStatus != ResponseStatus.Completed)
        {
            reason = response.ResponseStatus.ToString();
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            reason = "请检查授权链接是否过期或网络是否可用";
        }

        if (response.StatusCode is not 0)
        {
            throw new HttpRequestException(
                $"获取 Cookie 请求失败，HTTP {(int)response.StatusCode} {response.StatusCode}：{reason}",
                response.ErrorException,
                response.StatusCode);
        }

        throw new InvalidOperationException($"获取 Cookie 请求失败：{reason}", response.ErrorException);
    }
}
