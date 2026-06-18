using IGoLibrary.Ex.Domain.Enums;

namespace IGoLibrary.Ex.Domain.Models;

public sealed record SystemHealthSnapshot(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<HealthCheckItem> Items)
{
    public bool HasBlockingIssues => Items.Any(static item => item.Severity == HealthSeverity.Blocking);

    public bool HasWarnings => Items.Any(static item => item.Severity == HealthSeverity.Warning);
}
