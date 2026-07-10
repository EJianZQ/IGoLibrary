using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Abstractions;

public interface IRemoteCheckInApiClient
{
    Task<RemoteCheckInOAuthExchangeResult> ExchangeOAuthCodeAsync(
        string code,
        CancellationToken cancellationToken = default);

    Task<RemoteCheckInDeviceInfo> GetDeviceInfoAsync(
        string sessionToken,
        CancellationToken cancellationToken = default);

    Task<RemoteCheckInServerTime> GetServerTimeAsync(CancellationToken cancellationToken = default);

    Task<RemoteCheckInResult> SignAsync(
        string sessionToken,
        RemoteCheckInSignRequest request,
        CancellationToken cancellationToken = default);
}
