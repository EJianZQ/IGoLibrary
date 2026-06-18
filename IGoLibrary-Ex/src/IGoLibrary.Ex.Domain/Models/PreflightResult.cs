using IGoLibrary.Ex.Domain.Enums;

namespace IGoLibrary.Ex.Domain.Models;

public sealed record PreflightResult(
    PreflightTarget Target,
    DateTimeOffset CheckedAt,
    IReadOnlyList<HealthCheckItem> Items)
{
    public bool CanStart => Items.All(static item => item.Severity != HealthSeverity.Blocking);

    public IReadOnlyList<HealthCheckItem> BlockingItems =>
        Items.Where(static item => item.Severity == HealthSeverity.Blocking).ToArray();

    public IReadOnlyList<HealthCheckItem> WarningItems =>
        Items.Where(static item => item.Severity == HealthSeverity.Warning).ToArray();
}
