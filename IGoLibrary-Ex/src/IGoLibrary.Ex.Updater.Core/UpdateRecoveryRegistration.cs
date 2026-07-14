using System.Runtime.Versioning;
using Microsoft.Win32;

namespace IGoLibrary.Ex.Updater.Core;

[SupportedOSPlatform("windows")]
public static class UpdateRecoveryRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValuePrefix = "IGoLibrary-Ex-UpdateRecovery-";

    public static void Register(
        string transactionId,
        string updaterPath,
        string requestPath)
    {
        if (!Guid.TryParseExact(transactionId, "N", out _))
        {
            throw new InvalidDataException("恢复事务标识无效");
        }

        var fullUpdaterPath = Path.GetFullPath(updaterPath);
        var fullRequestPath = Path.GetFullPath(requestPath);
        if (!string.Equals(
                Path.GetDirectoryName(fullUpdaterPath),
                Path.GetDirectoryName(fullRequestPath),
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                Path.GetFileName(fullRequestPath),
                "request.json",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("恢复程序与请求文件必须位于同一事务目录");
        }

        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
                        ?? throw new InvalidOperationException("无法登记自动更新恢复入口");
        key.SetValue(
            ValuePrefix + transactionId,
            $"{Quote(fullUpdaterPath)} --recover",
            RegistryValueKind.String);
    }

    public static void Unregister(string transactionId)
    {
        if (!Guid.TryParseExact(transactionId, "N", out _))
        {
            return;
        }

        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(ValuePrefix + transactionId, throwOnMissingValue: false);
    }

    private static string Quote(string value)
    {
        if (value.Contains('"'))
        {
            throw new InvalidDataException("恢复命令路径包含非法字符");
        }

        return $"\"{Path.GetFullPath(value)}\"";
    }
}
