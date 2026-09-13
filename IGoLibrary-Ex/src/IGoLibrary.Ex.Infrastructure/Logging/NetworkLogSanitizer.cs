using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using IGoLibrary.Ex.Application.Logging;

namespace IGoLibrary.Ex.Infrastructure.Logging;

public static partial class NetworkLogSanitizer
{
    public const int BodyLimit = 65_536;
    private const string Hidden = "<redacted>";

    public static string Address(string? address, string? service = null)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri)) return "（地址不可用）";
        var path = uri.AbsolutePath.Replace("%3Credacted%3E", Hidden, StringComparison.OrdinalIgnoreCase);
        if (uri.Host.Contains("api.day.app", StringComparison.OrdinalIgnoreCase) ||
            service?.Contains("Bark", StringComparison.OrdinalIgnoreCase) == true)
            path = "/<redacted>";
        if (uri.Host.EndsWith("ftqq.com", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith("push.ft07.com", StringComparison.OrdinalIgnoreCase) ||
            service?.Contains("ServerChan", StringComparison.OrdinalIgnoreCase) == true)
            path = "/<redacted>";
        path = SecretPath().Replace(path, "/<redacted>");
        path = HealthPath().Replace(path, "/_igolibrary/health/<redacted>");
        var host = uri.HostNameType == UriHostNameType.IPv6 ? $"[{uri.Host}]" : uri.Host;
        return AppLogSanitizer.Sanitize($"{uri.Scheme}://{host}{(uri.IsDefaultPort ? "" : $":{uri.Port}")}{path}" +
            (uri.Query.Length > 0 ? "?<redacted>" : "") + (uri.Fragment.Length > 0 ? "#<redacted>" : ""));
    }

    public static bool IsText(string? contentType) => contentType is not null &&
        (contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
         contentType.Contains("json", StringComparison.OrdinalIgnoreCase) ||
         contentType.Contains("xml", StringComparison.OrdinalIgnoreCase) ||
         contentType.Contains("x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase));

    public static string Text(string value)
    {
        var text = Url().Replace(value, SanitizeAddressMatch);
        text = NamedSecret().Replace(text, "$1\"<redacted>\"");
        return AppLogSanitizer.Sanitize(text);
    }

    private static string SanitizeAddressMatch(Match match)
    {
        // Preserve placeholders emitted inside JSON/XML when the complete log is sanitized again.
        const string jsonHidden = @"\u003Credacted\u003E";
        const string xmlHidden = "&lt;redacted&gt;";
        var original = match.Groups["url"].Value;
        var quote = match.Groups["quote"].Value;
        var address = original.Replace(jsonHidden, Hidden, StringComparison.OrdinalIgnoreCase)
            .Replace(xmlHidden, Hidden, StringComparison.Ordinal);
        var sanitized = Address(address);
        if (original.Contains(jsonHidden, StringComparison.OrdinalIgnoreCase))
            return quote + sanitized.Replace(Hidden, jsonHidden, StringComparison.Ordinal);
        if (original.Contains(xmlHidden, StringComparison.Ordinal))
            return quote + sanitized.Replace(Hidden, xmlHidden, StringComparison.Ordinal);
        return quote + sanitized;
    }

    public static string Body(ReadOnlySpan<byte> bytes, string contentType, IEnumerable<string>? secrets = null)
    {
        var encoding = Encoding.GetEncoding("utf-8", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        var charset = Charset().Match(contentType);
        if (charset.Success)
            encoding = Encoding.GetEncoding(charset.Groups[1].Value, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        var text = encoding.GetString(bytes);
        if (secrets is not null)
            foreach (var secret in secrets.Where(s => !string.IsNullOrEmpty(s)))
            {
                text = text.Replace(secret, Hidden, StringComparison.Ordinal);
                text = text.Replace(Uri.EscapeDataString(secret), Hidden, StringComparison.OrdinalIgnoreCase);
            }

        string result;
        if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            var node = JsonNode.Parse(text);
            if (node is JsonValue scalar && scalar.TryGetValue<string>(out var value))
                node = JsonValue.Create(NestedText(value, 0));
            else SanitizeNode(node, 0);
            result = node?.ToJsonString() ?? "null";
        }
        else if (contentType.Contains("x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
        {
            result = string.Join("&", text.Split('&').Select(field =>
            {
                var pair = field.Split('=', 2);
                var key = Uri.UnescapeDataString(pair[0].Replace('+', ' '));
                var value = pair.Length > 1 ? Uri.UnescapeDataString(pair[1].Replace('+', ' ')) : "";
                return $"{Text(key)}={Uri.EscapeDataString(IsSecret(key) ? Hidden : NestedText(value, 0))}";
            }));
        }
        else if (contentType.Contains("xml", StringComparison.OrdinalIgnoreCase))
        {
            using var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = BodyLimit
            });
            var document = XDocument.Load(reader);
            foreach (var element in document.Descendants().ToArray())
            {
                if (IsSecret(element.Name.LocalName)) element.Value = Hidden;
                else if (!element.HasElements) element.Value = NestedText(element.Value, 0);
                foreach (var attribute in element.Attributes())
                    attribute.Value = IsSecret(attribute.Name.LocalName) ? Hidden : Text(attribute.Value);
            }
            foreach (var comment in document.DescendantNodes().OfType<XComment>().ToArray()) comment.Remove();
            result = document.ToString(SaveOptions.DisableFormatting);
        }
        else result = NestedText(text, 0);

        result = Text(result);
        // Never expose a partially serialized secret or a split UTF-8 character.
        return Encoding.UTF8.GetByteCount(result) <= BodyLimit ? result : "（脱敏后正文超过上限，已省略）";
    }

    private static string NestedText(string text, int depth)
    {
        if (depth > 12) return "（嵌套内容过深，已省略）";
        var trimmed = text.TrimStart();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            try
            {
                var node = JsonNode.Parse(text);
                SanitizeNode(node, depth + 1);
                return node?.ToJsonString() ?? "null";
            }
            catch { return "（嵌套正文无法安全脱敏）"; }
        }
        return Text(text);
    }

    private static void SanitizeNode(JsonNode? node, int depth)
    {
        if (depth > 32) throw new InvalidDataException("正文嵌套过深");
        if (node is JsonObject obj)
            foreach (var (key, value) in obj.ToArray())
            {
                if (IsSecret(key)) obj[key] = Hidden;
                else if (value is JsonValue scalar && scalar.TryGetValue<string>(out var text)) obj[key] = NestedText(text, depth + 1);
                else SanitizeNode(value, depth + 1);
            }
        else if (node is JsonArray array)
            for (var i = 0; i < array.Count; i++)
                if (array[i] is JsonValue scalar && scalar.TryGetValue<string>(out var text)) array[i] = NestedText(text, depth + 1);
                else SanitizeNode(array[i], depth + 1);
        else if (node is JsonValue) { /* Root strings are handled by the final text sanitizer. */ }
    }

    private static bool IsSecret(string name)
    {
        var normalized = name.Replace("_", "").Replace("-", "").ToLowerInvariant();
        return normalized.Contains("token") || normalized.Contains("password") || normalized.Contains("secret") ||
               normalized.Contains("cookie") || normalized.Contains("credential") || normalized.Contains("authorization") ||
               // The remote-check-in protocol sends wechatSESS_ID under the alias "t".
               normalized is "t" or "code" or "passwd" or "sendkey" or "apikey" or "devicekey" or
                   "wechatsessid" or "serverid" or "chatid";
    }

    [GeneratedRegex(@"/(?:bot\d+(?::|%3a)[^/\s]+|SCT[^/\s]+|sctp[^/\s]+)", RegexOptions.IgnoreCase)]
    private static partial Regex SecretPath();
    [GeneratedRegex(@"/_igolibrary/health/[^/]+", RegexOptions.IgnoreCase)]
    private static partial Regex HealthPath();
    [GeneratedRegex("""(?:(?<quote>\\*(?:["']|\\u0022|\\u0027))|(?<!["']|\\u0022|\\u0027))(?<url>(?:https?|wss?)://(?:(?!\k<quote>)(?:<redacted>|\\(?:u[0-9a-f]{4}|["\\/bfnrt])|&lt;redacted&gt;|[^\s"<>\\|；]))+)""", RegexOptions.IgnoreCase)]
    private static partial Regex Url();
    [GeneratedRegex("([\"']?\\b(?:\\w*token|cookie|set-cookie|t|code|password|passwd|secret|sendkey|apikey|devicekey|wechatSESS_ID|credentials|authorization)[\"']?\\s*[:=]\\s*)(?:<redacted>(?=$|[\\s,;|}\\]>])|\"[^\"]*\"|'[^']*'|[^\\s,;&}]+)", RegexOptions.IgnoreCase)]
    private static partial Regex NamedSecret();
    [GeneratedRegex("charset\\s*=\\s*[\"']?([^;\\s\"']+)", RegexOptions.IgnoreCase)]
    private static partial Regex Charset();
}
