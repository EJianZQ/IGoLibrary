using IGoLibrary.Ex.Application.Updates;

namespace IGoLibrary.Ex.Desktop.Services;

internal static class AutomaticUpdateCompatibilityMessages
{
    public static string BuildBlockedMessage(
        AutomaticUpdatePolicy policy,
        ReleaseVersion currentVersion)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(currentVersion);

        return policy.Evaluate(currentVersion) switch
        {
            AutomaticUpdateEligibility.CurrentVersionTooOld =>
                $"当前版本 v{currentVersion} 低于此版本允许自动更新的最低版本 v{policy.MinimumVersion}，无法自动更新，请前往 GitHub 手动下载",
            AutomaticUpdateEligibility.InvalidReleasePolicy =>
                "此版本的自动更新兼容性标记无效，为避免升级失败，无法自动更新，请前往 GitHub 手动下载",
            _ => throw new InvalidOperationException("允许自动更新的策略没有阻止原因")
        };
    }
}
