namespace IGoLibrary.Ex.Application.Protocol;

public sealed record TraceIntProtocolValidationIssue(string PropertyName, string Message);

public sealed record TraceIntProtocolValidationResult(
    IReadOnlyList<TraceIntProtocolValidationIssue> Errors,
    IReadOnlyList<TraceIntProtocolValidationIssue> Warnings)
{
    public bool IsValid => Errors.Count == 0;
}

public sealed class TraceIntProtocolValidationException(TraceIntProtocolValidationResult validationResult)
    : ArgumentException(BuildMessage(validationResult))
{
    public TraceIntProtocolValidationResult ValidationResult { get; } = validationResult;

    private static string BuildMessage(TraceIntProtocolValidationResult result)
    {
        return result.Errors.Count == 0
            ? "TraceInt 协议地址无效"
            : string.Join("；", result.Errors.Select(static issue => issue.Message));
    }
}

public static class TraceIntProtocolValidator
{
    public const string CodePlaceholder = "ReplaceMeByCode";
    public const string ReturnUrlPlaceholder = "ReplaceMeByReturnUrl";

    private const string ValidationCode = "0123456789abcdef0123456789abcdef";

    public static TraceIntProtocolTemplates Normalize(TraceIntProtocolTemplates templates)
    {
        ArgumentNullException.ThrowIfNull(templates);
        return templates with
        {
            GetCookieUrlTemplate = Trim(templates.GetCookieUrlTemplate),
            CookieAuthorizationReturnUrl = Trim(templates.CookieAuthorizationReturnUrl),
            GraphQlEndpointUrl = Trim(templates.GraphQlEndpointUrl),
            GraphQlDefaultRefererUrl = Trim(templates.GraphQlDefaultRefererUrl),
            GraphQlDefaultOriginUrl = NormalizeOrigin(templates.GraphQlDefaultOriginUrl),
            GraphQlTomorrowRefererUrl = Trim(templates.GraphQlTomorrowRefererUrl),
            GraphQlTomorrowOriginUrl = NormalizeOrigin(templates.GraphQlTomorrowOriginUrl),
            TomorrowReservationQueueUrlTemplate = Trim(templates.TomorrowReservationQueueUrlTemplate),
            RemoteCheckInAuthUrlTemplate = Trim(templates.RemoteCheckInAuthUrlTemplate),
            RemoteCheckInAuthorizationReturnUrl = Trim(templates.RemoteCheckInAuthorizationReturnUrl),
            RemoteCheckInAuthRefererUrl = Trim(templates.RemoteCheckInAuthRefererUrl),
            RemoteCheckInDevicesEndpointUrl = Trim(templates.RemoteCheckInDevicesEndpointUrl),
            RemoteCheckInTimeEndpointUrl = Trim(templates.RemoteCheckInTimeEndpointUrl),
            RemoteCheckInSignEndpointUrl = Trim(templates.RemoteCheckInSignEndpointUrl),
            RemoteCheckInApiRefererUrl = Trim(templates.RemoteCheckInApiRefererUrl)
        };
    }

    public static TraceIntProtocolTemplateOverrides Normalize(TraceIntProtocolTemplateOverrides overrides)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        return overrides with
        {
            GetCookieUrlTemplate = TrimNullable(overrides.GetCookieUrlTemplate),
            CookieAuthorizationReturnUrl = TrimNullable(overrides.CookieAuthorizationReturnUrl),
            GraphQlEndpointUrl = TrimNullable(overrides.GraphQlEndpointUrl),
            GraphQlDefaultRefererUrl = TrimNullable(overrides.GraphQlDefaultRefererUrl),
            GraphQlDefaultOriginUrl = NormalizeOriginNullable(overrides.GraphQlDefaultOriginUrl),
            GraphQlTomorrowRefererUrl = TrimNullable(overrides.GraphQlTomorrowRefererUrl),
            GraphQlTomorrowOriginUrl = NormalizeOriginNullable(overrides.GraphQlTomorrowOriginUrl),
            TomorrowReservationQueueUrlTemplate = TrimNullable(overrides.TomorrowReservationQueueUrlTemplate),
            RemoteCheckInAuthUrlTemplate = TrimNullable(overrides.RemoteCheckInAuthUrlTemplate),
            RemoteCheckInAuthorizationReturnUrl = TrimNullable(overrides.RemoteCheckInAuthorizationReturnUrl),
            RemoteCheckInAuthRefererUrl = TrimNullable(overrides.RemoteCheckInAuthRefererUrl),
            RemoteCheckInDevicesEndpointUrl = TrimNullable(overrides.RemoteCheckInDevicesEndpointUrl),
            RemoteCheckInTimeEndpointUrl = TrimNullable(overrides.RemoteCheckInTimeEndpointUrl),
            RemoteCheckInSignEndpointUrl = TrimNullable(overrides.RemoteCheckInSignEndpointUrl),
            RemoteCheckInApiRefererUrl = TrimNullable(overrides.RemoteCheckInApiRefererUrl)
        };
    }

    public static TraceIntProtocolValidationResult Validate(TraceIntProtocolTemplates templates)
    {
        ArgumentNullException.ThrowIfNull(templates);
        var normalized = Normalize(templates);
        var errors = new List<TraceIntProtocolValidationIssue>();
        var warnings = new List<TraceIntProtocolValidationIssue>();

        ValidateAuthorizationTemplate(
            normalized.GetCookieUrlTemplate,
            normalized.CookieAuthorizationReturnUrl,
            nameof(TraceIntProtocolTemplates.GetCookieUrlTemplate),
            "获取 Cookie 地址模板",
            requireReturnUrlPlaceholder: false,
            errors,
            warnings);
        ValidateHttpUrl(
            normalized.CookieAuthorizationReturnUrl,
            nameof(TraceIntProtocolTemplates.CookieAuthorizationReturnUrl),
            "Cookie 授权回调地址",
            errors);
        ValidateHttpUrl(
            normalized.GraphQlEndpointUrl,
            nameof(TraceIntProtocolTemplates.GraphQlEndpointUrl),
            "GraphQL 接口地址",
            errors);
        ValidateHttpUrl(
            normalized.GraphQlDefaultRefererUrl,
            nameof(TraceIntProtocolTemplates.GraphQlDefaultRefererUrl),
            "普通 GraphQL Referer",
            errors);
        ValidateOrigin(
            normalized.GraphQlDefaultOriginUrl,
            nameof(TraceIntProtocolTemplates.GraphQlDefaultOriginUrl),
            "普通 GraphQL Origin",
            errors);
        ValidateHttpUrl(
            normalized.GraphQlTomorrowRefererUrl,
            nameof(TraceIntProtocolTemplates.GraphQlTomorrowRefererUrl),
            "明日预约 GraphQL Referer",
            errors);
        ValidateOrigin(
            normalized.GraphQlTomorrowOriginUrl,
            nameof(TraceIntProtocolTemplates.GraphQlTomorrowOriginUrl),
            "明日预约 GraphQL Origin",
            errors);
        ValidateWebSocketUrl(
            normalized.TomorrowReservationQueueUrlTemplate,
            nameof(TraceIntProtocolTemplates.TomorrowReservationQueueUrlTemplate),
            "明日预约 WebSocket 地址",
            errors);
        ValidateAuthorizationTemplate(
            normalized.RemoteCheckInAuthUrlTemplate,
            normalized.RemoteCheckInAuthorizationReturnUrl,
            nameof(TraceIntProtocolTemplates.RemoteCheckInAuthUrlTemplate),
            "远程签到授权地址模板",
            requireReturnUrlPlaceholder: true,
            errors,
            warnings);
        ValidateHttpUrl(
            normalized.RemoteCheckInAuthorizationReturnUrl,
            nameof(TraceIntProtocolTemplates.RemoteCheckInAuthorizationReturnUrl),
            "远程签到授权回调地址",
            errors);
        ValidateHttpUrl(
            normalized.RemoteCheckInAuthRefererUrl,
            nameof(TraceIntProtocolTemplates.RemoteCheckInAuthRefererUrl),
            "远程签到授权 Referer",
            errors);
        ValidateHttpUrl(
            normalized.RemoteCheckInDevicesEndpointUrl,
            nameof(TraceIntProtocolTemplates.RemoteCheckInDevicesEndpointUrl),
            "远程签到设备接口地址",
            errors);
        ValidateHttpUrl(
            normalized.RemoteCheckInTimeEndpointUrl,
            nameof(TraceIntProtocolTemplates.RemoteCheckInTimeEndpointUrl),
            "远程签到服务器时间接口地址",
            errors);
        ValidateHttpUrl(
            normalized.RemoteCheckInSignEndpointUrl,
            nameof(TraceIntProtocolTemplates.RemoteCheckInSignEndpointUrl),
            "远程签到提交接口地址",
            errors);
        ValidateHttpUrl(
            normalized.RemoteCheckInApiRefererUrl,
            nameof(TraceIntProtocolTemplates.RemoteCheckInApiRefererUrl),
            "远程签到 API Referer",
            errors);

        return new TraceIntProtocolValidationResult(errors, warnings);
    }

    public static void EnsureValid(TraceIntProtocolTemplates templates)
    {
        var result = Validate(templates);
        if (!result.IsValid)
        {
            throw new TraceIntProtocolValidationException(result);
        }
    }

    public static string BuildAuthorizationUrl(string template, string code, string returnUrl)
    {
        return template
            .Replace(ReturnUrlPlaceholder, Uri.EscapeDataString(returnUrl), StringComparison.Ordinal)
            .Replace(CodePlaceholder, Uri.EscapeDataString(code), StringComparison.Ordinal);
    }

    private static void ValidateAuthorizationTemplate(
        string template,
        string returnUrl,
        string propertyName,
        string displayName,
        bool requireReturnUrlPlaceholder,
        ICollection<TraceIntProtocolValidationIssue> errors,
        ICollection<TraceIntProtocolValidationIssue> warnings)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            errors.Add(new TraceIntProtocolValidationIssue(propertyName, $"{displayName}不能为空"));
            return;
        }

        if (!template.Contains(CodePlaceholder, StringComparison.Ordinal))
        {
            errors.Add(new TraceIntProtocolValidationIssue(
                propertyName,
                $"{displayName}必须包含 {CodePlaceholder}"));
        }

        if (!template.Contains(ReturnUrlPlaceholder, StringComparison.Ordinal))
        {
            var issue = new TraceIntProtocolValidationIssue(
                propertyName,
                $"{displayName}未包含 {ReturnUrlPlaceholder}，将按兼容模式使用模板中已有的回调地址");
            if (requireReturnUrlPlaceholder)
            {
                errors.Add(issue with { Message = $"{displayName}必须包含 {ReturnUrlPlaceholder}" });
            }
            else
            {
                warnings.Add(issue);
            }
        }

        var requestUrl = BuildAuthorizationUrl(template, ValidationCode, returnUrl);
        ValidateHttpUrl(requestUrl, propertyName, $"{displayName}替换后的地址", errors);
    }

    private static void ValidateHttpUrl(
        string value,
        string propertyName,
        string displayName,
        ICollection<TraceIntProtocolValidationIssue> errors)
    {
        if (!TryCreateAbsoluteUri(value, out var uri) ||
            (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
             !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add(new TraceIntProtocolValidationIssue(
                propertyName,
                $"{displayName}必须是绝对 http/https 地址"));
            return;
        }

        ValidateCommonUriParts(uri, propertyName, displayName, errors);
    }

    private static void ValidateWebSocketUrl(
        string value,
        string propertyName,
        string displayName,
        ICollection<TraceIntProtocolValidationIssue> errors)
    {
        if (!TryCreateAbsoluteUri(value, out var uri) || uri.Scheme is not ("ws" or "wss"))
        {
            errors.Add(new TraceIntProtocolValidationIssue(
                propertyName,
                $"{displayName}必须是绝对 ws/wss 地址"));
            return;
        }

        ValidateCommonUriParts(uri, propertyName, displayName, errors);
    }

    private static void ValidateOrigin(
        string value,
        string propertyName,
        string displayName,
        ICollection<TraceIntProtocolValidationIssue> errors)
    {
        var initialCount = errors.Count;
        ValidateHttpUrl(value, propertyName, displayName, errors);
        if (errors.Count != initialCount || !Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return;
        }

        if (uri.AbsolutePath is not ("" or "/") || !string.IsNullOrEmpty(uri.Query))
        {
            errors.Add(new TraceIntProtocolValidationIssue(
                propertyName,
                $"{displayName}只能包含协议、主机和可选端口"));
        }
    }

    private static void ValidateCommonUriParts(
        Uri uri,
        string propertyName,
        string displayName,
        ICollection<TraceIntProtocolValidationIssue> errors)
    {
        if (string.IsNullOrWhiteSpace(uri.Host))
        {
            errors.Add(new TraceIntProtocolValidationIssue(propertyName, $"{displayName}缺少主机名"));
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            errors.Add(new TraceIntProtocolValidationIssue(propertyName, $"{displayName}不能包含用户信息"));
        }

        if (!string.IsNullOrEmpty(uri.Fragment))
        {
            errors.Add(new TraceIntProtocolValidationIssue(propertyName, $"{displayName}不能包含片段标识"));
        }
    }

    private static bool TryCreateAbsoluteUri(string value, out Uri uri)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out uri!) && !string.IsNullOrWhiteSpace(value);
    }

    private static string Trim(string value) => value?.Trim() ?? string.Empty;

    private static string? TrimNullable(string? value) => value?.Trim();

    private static string NormalizeOrigin(string value)
    {
        var trimmed = Trim(value);
        return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
               (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) &&
               uri.AbsolutePath is "" or "/" &&
               string.IsNullOrEmpty(uri.Query) &&
               string.IsNullOrEmpty(uri.Fragment) &&
               string.IsNullOrEmpty(uri.UserInfo)
            ? uri.GetLeftPart(UriPartial.Authority)
            : trimmed;
    }

    private static string? NormalizeOriginNullable(string? value)
    {
        return value is null ? null : NormalizeOrigin(value);
    }
}
