namespace IGoLibrary.Ex.Application.Updates;

public enum AutomaticUpdatePolicyKind
{
    Unrestricted,
    MinimumVersion,
    Invalid
}

public enum AutomaticUpdateEligibility
{
    Supported,
    CurrentVersionTooOld,
    InvalidReleasePolicy
}

public sealed record AutomaticUpdatePolicy
{
    private AutomaticUpdatePolicy(
        AutomaticUpdatePolicyKind kind,
        ReleaseVersion? minimumVersion)
    {
        Kind = kind;
        MinimumVersion = minimumVersion;
    }

    public static AutomaticUpdatePolicy Unrestricted { get; } = new(
        AutomaticUpdatePolicyKind.Unrestricted,
        minimumVersion: null);

    public static AutomaticUpdatePolicy Invalid { get; } = new(
        AutomaticUpdatePolicyKind.Invalid,
        minimumVersion: null);

    public AutomaticUpdatePolicyKind Kind { get; }

    public ReleaseVersion? MinimumVersion { get; }

    public static AutomaticUpdatePolicy RequireMinimumVersion(ReleaseVersion minimumVersion)
    {
        ArgumentNullException.ThrowIfNull(minimumVersion);
        return new AutomaticUpdatePolicy(
            AutomaticUpdatePolicyKind.MinimumVersion,
            minimumVersion);
    }

    public AutomaticUpdateEligibility Evaluate(ReleaseVersion currentVersion)
    {
        ArgumentNullException.ThrowIfNull(currentVersion);

        return Kind switch
        {
            AutomaticUpdatePolicyKind.Unrestricted => AutomaticUpdateEligibility.Supported,
            AutomaticUpdatePolicyKind.MinimumVersion when currentVersion >= MinimumVersion! =>
                AutomaticUpdateEligibility.Supported,
            AutomaticUpdatePolicyKind.MinimumVersion =>
                AutomaticUpdateEligibility.CurrentVersionTooOld,
            AutomaticUpdatePolicyKind.Invalid =>
                AutomaticUpdateEligibility.InvalidReleasePolicy,
            _ => throw new InvalidOperationException($"未知的自动更新策略类型：{Kind}")
        };
    }
}
