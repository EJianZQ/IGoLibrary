using System.Reflection;
using System.Text.Json;

namespace IGoLibrary.Ex.Desktop.Services;

internal static class MobileControlMobilePage
{
    private const string TokenPlaceholder = "__MOBILE_CONTROL_TOKEN_JSON__";
    private const string TemplateResourceName =
        "IGoLibrary.Ex.Desktop.Resources.MobileControl.MobileControlPage.html";

    private static readonly Lazy<string> Template = new(LoadTemplate);

    public static string Build(string token)
    {
        var tokenJson = JsonSerializer.Serialize(token);
        return Template.Value.Replace(TokenPlaceholder, tokenJson, StringComparison.Ordinal);
    }

    private static string LoadTemplate()
    {
        var assembly = typeof(MobileControlMobilePage).GetTypeInfo().Assembly;
        using var stream = assembly.GetManifestResourceStream(TemplateResourceName)
            ?? throw new InvalidOperationException($"手机控制页面模板资源不存在：{TemplateResourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
