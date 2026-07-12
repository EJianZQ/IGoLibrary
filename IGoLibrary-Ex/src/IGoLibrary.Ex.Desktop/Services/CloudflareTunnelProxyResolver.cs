using System.Net;
using IGoLibrary.Ex.Application.Configuration;

namespace IGoLibrary.Ex.Desktop.Services;

internal sealed record CloudflareTunnelProxyOptions(
    CloudflareTunnelProxyMode Mode,
    string ManualProxyUrl)
{
    public static CloudflareTunnelProxyOptions From(MobileControlSettings settings)
    {
        return new CloudflareTunnelProxyOptions(
            MobileControlSettings.NormalizeTunnelProxyMode(settings.TunnelProxyMode),
            settings.TunnelManualProxyUrl);
    }
}

internal sealed record CloudflareTunnelProxyResolution(
    Uri? ProxyUri,
    CloudflareTunnelProxyMode EffectiveMode);

internal interface ICloudflareTunnelProxyResolver
{
    CloudflareTunnelProxyResolution Resolve(CloudflareTunnelProxyOptions options);
}

internal interface ICloudflareSystemProxyProvider
{
    IWebProxy GetDefaultProxy();
}

internal sealed class CloudflareSystemProxyProvider : ICloudflareSystemProxyProvider
{
    public IWebProxy GetDefaultProxy() => HttpClient.DefaultProxy;
}

internal sealed class CloudflareTunnelProxyResolver(
    ICloudflareSystemProxyProvider systemProxyProvider) : ICloudflareTunnelProxyResolver
{
    internal static readonly Uri ProbeTarget = new("https://api.trycloudflare.com/tunnel");

    public CloudflareTunnelProxyResolution Resolve(CloudflareTunnelProxyOptions options)
    {
        var mode = MobileControlSettings.NormalizeTunnelProxyMode(options.Mode);
        return mode switch
        {
            CloudflareTunnelProxyMode.Auto => ResolveSystemProxy(required: false)
                ?? new CloudflareTunnelProxyResolution(null, CloudflareTunnelProxyMode.Direct),
            CloudflareTunnelProxyMode.SystemProxy => ResolveSystemProxy(required: true)!,
            CloudflareTunnelProxyMode.ManualHttpProxy => new CloudflareTunnelProxyResolution(
                ParseManualProxy(options.ManualProxyUrl),
                CloudflareTunnelProxyMode.ManualHttpProxy),
            CloudflareTunnelProxyMode.Direct => new CloudflareTunnelProxyResolution(
                null,
                CloudflareTunnelProxyMode.Direct),
            _ => throw new InvalidOperationException("Cloudflare Tunnel 代理方式无效")
        };
    }

    private CloudflareTunnelProxyResolution? ResolveSystemProxy(bool required)
    {
        try
        {
            var proxy = systemProxyProvider.GetDefaultProxy();
            if (proxy.IsBypassed(ProbeTarget))
            {
                return required
                    ? throw new InvalidOperationException("未检测到可用于 Cloudflare Tunnel 的系统代理")
                    : null;
            }

            var proxyUri = proxy.GetProxy(ProbeTarget);
            if (proxyUri is null || Uri.Compare(
                    proxyUri,
                    ProbeTarget,
                    UriComponents.HttpRequestUrl,
                    UriFormat.SafeUnescaped,
                    StringComparison.OrdinalIgnoreCase) == 0)
            {
                return required
                    ? throw new InvalidOperationException("未检测到可用于 Cloudflare Tunnel 的系统代理")
                    : null;
            }

            return new CloudflareTunnelProxyResolution(
                ValidateProxyUri(proxyUri, "系统代理"),
                CloudflareTunnelProxyMode.SystemProxy);
        }
        catch (InvalidOperationException) when (!required)
        {
            return null;
        }
        catch (Exception ex) when (!required && ex is not OperationCanceledException)
        {
            return null;
        }
    }

    private static Uri ParseManualProxy(string value)
    {
        if (!MobileControlSettings.TryNormalizeManualProxyUrl(value, out var normalized))
        {
            throw new InvalidOperationException(
                "手动代理地址无效，应类似 http://127.0.0.1:7897，且不能包含账号、密码、路径或查询参数");
        }

        return new Uri(normalized, UriKind.Absolute);
    }

    private static Uri ValidateProxyUri(Uri uri, string source)
    {
        if (!uri.IsAbsoluteUri ||
            uri.Scheme != Uri.UriSchemeHttp ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new InvalidOperationException($"{source}不是受支持的 HTTP 代理地址");
        }

        return new Uri(uri.GetLeftPart(UriPartial.Authority), UriKind.Absolute);
    }
}
