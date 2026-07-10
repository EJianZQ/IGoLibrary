using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Configuration;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Tests;

internal sealed class FakeRemoteCheckInApiClient : IRemoteCheckInApiClient
{
    public Func<string, CancellationToken, Task<string>>? OnExchangeOAuthCodeAsync { get; set; }
    public Func<string, CancellationToken, Task<RemoteCheckInDeviceInfo>>? OnGetDeviceInfoAsync { get; set; }
    public Func<CancellationToken, Task<RemoteCheckInServerTime>>? OnGetServerTimeAsync { get; set; }
    public Func<string, RemoteCheckInSignRequest, CancellationToken, Task<RemoteCheckInResult>>? OnSignAsync { get; set; }

    public Task<string> ExchangeOAuthCodeAsync(string code, CancellationToken cancellationToken = default)
        => OnExchangeOAuthCodeAsync?.Invoke(code, cancellationToken) ?? Task.FromResult(new string('a', 40));

    public Task<RemoteCheckInDeviceInfo> GetDeviceInfoAsync(string sessionToken, CancellationToken cancellationToken = default)
        => OnGetDeviceInfoAsync?.Invoke(sessionToken, cancellationToken)
           ?? Task.FromResult(new RemoteCheckInDeviceInfo(
               new RemoteCheckInUserSummary("测试用户", "测试学校", "测试学生", "20240001"),
               ["E2C56DB5-DFFB-48D2-B060-D0F5A71096E0"]));

    public Task<RemoteCheckInServerTime> GetServerTimeAsync(CancellationToken cancellationToken = default)
        => OnGetServerTimeAsync?.Invoke(cancellationToken)
           ?? Task.FromResult(new RemoteCheckInServerTime("1782346868", 1782346868));

    public Task<RemoteCheckInResult> SignAsync(
        string sessionToken,
        RemoteCheckInSignRequest request,
        CancellationToken cancellationToken = default)
        => OnSignAsync?.Invoke(sessionToken, request, cancellationToken)
           ?? Task.FromResult(new RemoteCheckInResult(
               "验证成功", 2, 1, "测试场馆", "1楼", "1,1", "001", null, null));
}

internal sealed class FakeRemoteCheckInWorkflowService : IRemoteCheckInWorkflowService
{
    public RemoteCheckInSessionCredentials? CurrentSession { get; set; }

    public Func<string, bool, CancellationToken, Task<RemoteCheckInAuthorizationResult>>? OnAuthorizeAsync { get; set; }
    public Func<CancellationToken, Task<RemoteCheckInDeviceInfo>>? OnGetDeviceInfoAsync { get; set; }
    public Func<RemoteCheckInSignPlan, CancellationToken, Task<RemoteCheckInResult>>? OnSignAsync { get; set; }

    public Exception? ClearException { get; set; }

    public Task<RemoteCheckInSessionCredentials?> RestoreAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(CurrentSession);

    public async Task<RemoteCheckInAuthorizationResult> AuthorizeFromCodeAsync(
        string code,
        bool remember,
        CancellationToken cancellationToken = default)
    {
        if (OnAuthorizeAsync is not null)
        {
            var configured = await OnAuthorizeAsync(code, remember, cancellationToken);
            CurrentSession = configured.Session;
            return configured;
        }

        CurrentSession = new RemoteCheckInSessionCredentials(new string('a', 40), DateTimeOffset.UtcNow, remember);
        return new RemoteCheckInAuthorizationResult(
            CurrentSession,
            new RemoteCheckInDeviceInfo(
                new RemoteCheckInUserSummary("测试用户", "测试学校", "测试学生", "20240001"),
                ["E2C56DB5-DFFB-48D2-B060-D0F5A71096E0"]),
            null);
    }

    public Task<RemoteCheckInDeviceInfo> GetDeviceInfoAsync(CancellationToken cancellationToken = default)
        => OnGetDeviceInfoAsync?.Invoke(cancellationToken)
           ?? Task.FromResult(new RemoteCheckInDeviceInfo(
               new RemoteCheckInUserSummary("测试用户", "测试学校", "测试学生", "20240001"),
               ["E2C56DB5-DFFB-48D2-B060-D0F5A71096E0"]));

    public Task<RemoteCheckInResult> SignAsync(RemoteCheckInSignPlan plan, CancellationToken cancellationToken = default)
        => OnSignAsync?.Invoke(plan, cancellationToken)
           ?? Task.FromResult(new RemoteCheckInResult(
               "验证成功", 2, plan.ExpectedLibraryId, plan.ExpectedLibraryName, "", "1,1", "001", null, null));

    public Task ClearSessionAsync(CancellationToken cancellationToken = default)
    {
        if (ClearException is not null)
        {
            throw ClearException;
        }

        CurrentSession = null;
        return Task.CompletedTask;
    }
}

internal sealed class FakeRemoteCheckInProfileService : IRemoteCheckInProfileService
{
    public Dictionary<int, RemoteCheckInVenueProfileSettings> Profiles { get; } = [];

    public Func<int, CancellationToken, Task<RemoteCheckInVenueProfileSettings?>>? OnGetForLibraryAsync { get; set; }

    public Task<RemoteCheckInVenueProfileSettings?> GetForLibraryAsync(int libraryId, CancellationToken cancellationToken = default)
    {
        if (OnGetForLibraryAsync is not null)
        {
            return OnGetForLibraryAsync(libraryId, cancellationToken);
        }

        Profiles.TryGetValue(libraryId, out var profile);
        return Task.FromResult(profile);
    }

    public Task<RemoteCheckInVenueProfileSettings> SaveAsync(
        RemoteCheckInVenueProfileSettings profile,
        CancellationToken cancellationToken = default)
    {
        Profiles[profile.LibraryId] = profile;
        return Task.FromResult(profile);
    }
}

internal sealed class FakeReservationWorkflowService : IReservationWorkflowService
{
    public Func<CancellationToken, Task<ReservationOperationResult>>? OnRefreshAsync { get; set; }

    public Task<ReservationOperationResult> RefreshReservationAsync(CancellationToken cancellationToken = default)
        => OnRefreshAsync?.Invoke(cancellationToken)
           ?? Task.FromResult(new ReservationOperationResult(true, null));

    public Task<ReservationOperationResult> CancelCurrentReservationAsync(
        ReservationInfo reservation,
        bool stopOccupyFirst,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new ReservationOperationResult(true, null));
}
