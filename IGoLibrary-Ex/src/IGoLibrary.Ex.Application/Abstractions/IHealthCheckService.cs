using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Abstractions;

public interface IHealthCheckService
{
    Task<SystemHealthSnapshot> BuildSnapshotAsync(CancellationToken cancellationToken = default);

    Task<PreflightResult> RunPreflightAsync(
        PreflightTarget target,
        CancellationToken cancellationToken = default);
}
