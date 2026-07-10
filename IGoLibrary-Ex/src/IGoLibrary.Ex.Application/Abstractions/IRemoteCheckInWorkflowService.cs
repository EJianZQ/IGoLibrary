using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Abstractions;

public interface IRemoteCheckInWorkflowService
{
    RemoteCheckInSessionCredentials? CurrentSession { get; }

    Task<RemoteCheckInSessionCredentials?> RestoreAsync(CancellationToken cancellationToken = default);

    Task<RemoteCheckInAuthorizationResult> AuthorizeFromCodeAsync(
        string code,
        bool remember,
        CancellationToken cancellationToken = default);

    Task<RemoteCheckInDeviceInfo> GetDeviceInfoAsync(CancellationToken cancellationToken = default);

    Task<RemoteCheckInResult> SignAsync(
        RemoteCheckInSignPlan plan,
        CancellationToken cancellationToken = default);

    Task ClearSessionAsync(CancellationToken cancellationToken = default);
}
