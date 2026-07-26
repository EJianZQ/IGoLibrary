using System.Reflection;
using System.Text.Json;

namespace IGoLibrary.Ex.Desktop.Services;

internal static class MobileControlMobilePage
{
    private const string TokenPlaceholder = "__MOBILE_CONTROL_TOKEN_JSON__";
    private const string BluetoothScannerPlaceholder = "__MOBILE_CONTROL_BLUETOOTH_SCANNER_JS__";
    private const string TemplateResourceName =
        "IGoLibrary.Ex.Desktop.Resources.MobileControl.MobileControlPage.html";
    private const string BluetoothScannerResourceName =
        "IGoLibrary.Ex.Desktop.Resources.MobileControl.MobileControlBluetoothScanner.js";

    private static readonly Lazy<string> Template = new(LoadTemplate);
    private static readonly Lazy<string> BluetoothScanner = new(LoadBluetoothScanner);

    public static string Build(string token)
    {
        var tokenJson = JsonSerializer.Serialize(token);
        return Template.Value
            .Replace(TokenPlaceholder, tokenJson, StringComparison.Ordinal)
            .Replace(BluetoothScannerPlaceholder, BluetoothScanner.Value, StringComparison.Ordinal);
    }

    private static string LoadTemplate() => LoadResource(
        TemplateResourceName,
        "手机控制页面模板");

    private static string LoadBluetoothScanner() => LoadResource(
        BluetoothScannerResourceName,
        "手机控制蓝牙扫描脚本");

    private static string LoadResource(string resourceName, string displayName)
    {
        var assembly = typeof(MobileControlMobilePage).GetTypeInfo().Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"{displayName}资源不存在：{resourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
