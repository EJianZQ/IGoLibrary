using IGoLibrary.Ex.Application.Configuration;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Exceptions;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Application.State;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Tests;

public sealed class RemoteCheckInWorkflowTests
{
    private const string BeaconUuid = "E2C56DB5-DFFB-48D2-B060-D0F5A71096E0";

    [Theory]
    [InlineData(40, true)]
    [InlineData(48, true)]
    [InlineData(39, false)]
    [InlineData(41, false)]
    [InlineData(47, false)]
    [InlineData(49, false)]
    public void SessionTokenValidator_AcceptsOnlySupportedHexLengths(int length, bool expected)
    {
        var token = new string('A', length);

        var valid = RemoteCheckInSessionTokenValidator.TryNormalize(token, out var normalized);

        Assert.Equal(expected, valid);
        Assert.Equal(expected ? token.ToLowerInvariant() : string.Empty, normalized);
    }

    [Fact]
    public void SessionTokenValidator_RejectsNonHexValue()
    {
        Assert.False(RemoteCheckInSessionTokenValidator.TryNormalize(
            new string('a', 47) + "g",
            out _));
    }

    [Fact]
    public async Task Authorize_PersistsOnlyWhenRequested()
    {
        var credentials = new FakeCredentialStore();
        var service = CreateService(new FakeRemoteCheckInApiClient(), credentials, new AppRuntimeState());

        await service.AuthorizeFromCodeAsync(new string('b', 32), remember: true);
        Assert.NotNull(credentials.StoredRemoteCheckInSession);
        Assert.Equal(1, credentials.SaveRemoteCheckInCalls);

        await service.AuthorizeFromCodeAsync(new string('c', 32), remember: false);
        Assert.Null(credentials.StoredRemoteCheckInSession);
        Assert.Equal(1, credentials.ClearRemoteCheckInCalls);
    }

    [Fact]
    public async Task Authorize_AcceptsAndNormalizes48CharacterSessionToken()
    {
        var credentials = new FakeCredentialStore();
        var state = new AppRuntimeState();
        var api = new FakeRemoteCheckInApiClient
        {
            OnExchangeOAuthCodeAsync = (_, _) => Task.FromResult(new string('A', 48))
        };
        var service = CreateService(api, credentials, state);

        var result = await service.AuthorizeFromCodeAsync(new string('b', 32), remember: true);

        Assert.Equal(new string('a', 48), result.Session.Token);
        Assert.Same(result.Session, state.RemoteCheckInSession);
        Assert.Same(result.Session, credentials.StoredRemoteCheckInSession);
    }

    [Fact]
    public async Task Restore_AcceptsAndNormalizesPersisted48CharacterSessionToken()
    {
        var stored = new RemoteCheckInSessionCredentials(
            new string('A', 48),
            DateTimeOffset.UtcNow,
            true);
        var credentials = new FakeCredentialStore { StoredRemoteCheckInSession = stored };
        var state = new AppRuntimeState();
        var service = CreateService(new FakeRemoteCheckInApiClient(), credentials, state);

        var restored = await service.RestoreAsync();

        Assert.NotNull(restored);
        Assert.Equal(new string('a', 48), restored.Token);
        Assert.Same(restored, state.RemoteCheckInSession);
        Assert.Equal(0, credentials.ClearRemoteCheckInCalls);
    }

    [Fact]
    public async Task Reauthorize_WhenCandidateSessionIsInvalid_RetainsPreviousSession()
    {
        var previous = new RemoteCheckInSessionCredentials(new string('a', 40), DateTimeOffset.UtcNow, true);
        var credentials = new FakeCredentialStore { StoredRemoteCheckInSession = previous };
        var state = new AppRuntimeState { RemoteCheckInSession = previous };
        var api = new FakeRemoteCheckInApiClient
        {
            OnExchangeOAuthCodeAsync = (_, _) => Task.FromResult(new string('b', 40)),
            OnGetDeviceInfoAsync = (_, _) => throw new RemoteCheckInApiException(
                "未登录",
                isSessionInvalid: true)
        };
        var service = CreateService(api, credentials, state);

        var exception = await Assert.ThrowsAsync<RemoteCheckInAuthorizationException>(() =>
            service.AuthorizeFromCodeAsync(new string('c', 32), remember: true));

        Assert.True(exception.OAuthCodeConsumed);
        Assert.True(exception.IsSessionInvalid);
        Assert.Same(previous, state.RemoteCheckInSession);
        Assert.Same(previous, credentials.StoredRemoteCheckInSession);
        Assert.Equal(0, credentials.SaveRemoteCheckInCalls);
        Assert.Equal(0, credentials.ClearRemoteCheckInCalls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Reauthorize_WhenCredentialOperationFails_RetainsPreviousSession(bool remember)
    {
        var previous = new RemoteCheckInSessionCredentials(new string('a', 40), DateTimeOffset.UtcNow, true);
        var credentials = new FakeCredentialStore { StoredRemoteCheckInSession = previous };
        if (remember)
        {
            credentials.SaveRemoteCheckInException = new InvalidOperationException("credential write failed");
        }
        else
        {
            credentials.ClearRemoteCheckInException = new InvalidOperationException("credential delete failed");
        }

        var state = new AppRuntimeState { RemoteCheckInSession = previous };
        var api = new FakeRemoteCheckInApiClient
        {
            OnExchangeOAuthCodeAsync = (_, _) => Task.FromResult(new string('b', 40))
        };
        var service = CreateService(api, credentials, state);

        var exception = await Assert.ThrowsAsync<RemoteCheckInAuthorizationException>(() =>
            service.AuthorizeFromCodeAsync(new string('c', 32), remember));

        Assert.True(exception.OAuthCodeConsumed);
        Assert.False(exception.IsSessionInvalid);
        Assert.Same(previous, state.RemoteCheckInSession);
        Assert.Same(previous, credentials.StoredRemoteCheckInSession);
    }

    [Fact]
    public async Task Authorize_WhenInitialDeviceRefreshIsTransient_CommitsSessionWithWarning()
    {
        var credentials = new FakeCredentialStore();
        var state = new AppRuntimeState();
        var api = new FakeRemoteCheckInApiClient
        {
            OnGetDeviceInfoAsync = (_, _) => throw new HttpRequestException("offline")
        };
        var service = CreateService(api, credentials, state);

        var result = await service.AuthorizeFromCodeAsync(new string('b', 32), remember: true);

        Assert.NotNull(state.RemoteCheckInSession);
        Assert.Same(state.RemoteCheckInSession, result.Session);
        Assert.Null(result.DeviceInfo);
        Assert.Contains("offline", result.DeviceRefreshWarning);
        Assert.Same(result.Session, credentials.StoredRemoteCheckInSession);
    }

    [Fact]
    public async Task ClearSession_WhenCredentialDeleteFails_RetainsRuntimeSessionForRetry()
    {
        var previous = new RemoteCheckInSessionCredentials(new string('a', 40), DateTimeOffset.UtcNow, true);
        var credentials = new FakeCredentialStore
        {
            StoredRemoteCheckInSession = previous,
            ClearRemoteCheckInException = new InvalidOperationException("credential delete failed")
        };
        var state = new AppRuntimeState { RemoteCheckInSession = previous };
        var service = CreateService(new FakeRemoteCheckInApiClient(), credentials, state);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ClearSessionAsync());

        Assert.Same(previous, state.RemoteCheckInSession);
        Assert.Same(previous, credentials.StoredRemoteCheckInSession);
    }

    [Fact]
    public async Task Restore_ClearsMalformedTokenWithoutTouchingGraphQlSession()
    {
        var graphQl = new SessionCredentials("cookie", IGoLibrary.Ex.Domain.Enums.SessionSource.ManualCookie, DateTimeOffset.UtcNow, true);
        var credentials = new FakeCredentialStore
        {
            StoredSession = graphQl,
            StoredRemoteCheckInSession = new RemoteCheckInSessionCredentials("invalid", DateTimeOffset.UtcNow, true)
        };
        var service = CreateService(new FakeRemoteCheckInApiClient(), credentials, new AppRuntimeState());

        var restored = await service.RestoreAsync();

        Assert.Null(restored);
        Assert.Same(graphQl, credentials.StoredSession);
        Assert.Null(credentials.StoredRemoteCheckInSession);
    }

    [Fact]
    public async Task Sign_RechecksAllowedUuidAndUsesServerTime()
    {
        RemoteCheckInSignRequest? captured = null;
        var api = new FakeRemoteCheckInApiClient
        {
            OnSignAsync = (_, request, _) =>
            {
                captured = request;
                return Task.FromResult(new RemoteCheckInResult(
                    "验证成功", 2, 1, "馆", "", "1,1", "001", null, null));
            }
        };
        var state = new AppRuntimeState
        {
            RemoteCheckInSession = new RemoteCheckInSessionCredentials(new string('a', 40), DateTimeOffset.UtcNow, false)
        };
        var service = CreateService(api, new FakeCredentialStore(), state);

        await service.SignAsync(new RemoteCheckInSignPlan(1, "馆", BeaconUuid, 10001, 20002, 39.1m, 116.2m));

        Assert.NotNull(captured);
        Assert.Equal("1782346868", captured.ServerTimestamp);
        Assert.Equal((ushort)10001, captured.Major);
    }

    [Fact]
    public async Task Sign_RejectsUuidNoLongerAllowed()
    {
        var api = new FakeRemoteCheckInApiClient
        {
            OnGetDeviceInfoAsync = (_, _) => Task.FromResult(new RemoteCheckInDeviceInfo(
                new RemoteCheckInUserSummary("", "", "", ""),
                ["AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE"]))
        };
        var state = new AppRuntimeState
        {
            RemoteCheckInSession = new RemoteCheckInSessionCredentials(new string('a', 40), DateTimeOffset.UtcNow, false)
        };
        var service = CreateService(api, new FakeCredentialStore(), state);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SignAsync(new RemoteCheckInSignPlan(1, "馆", BeaconUuid, 1, 2, 39m, 116m)));

        Assert.Contains("UUID 已变化", ex.Message);
    }

    [Fact]
    public async Task SessionInvalid_ClearsOnlyRemoteCredential()
    {
        var credentials = new FakeCredentialStore
        {
            StoredSession = new SessionCredentials("cookie", IGoLibrary.Ex.Domain.Enums.SessionSource.ManualCookie, DateTimeOffset.UtcNow, true),
            StoredRemoteCheckInSession = new RemoteCheckInSessionCredentials(new string('a', 40), DateTimeOffset.UtcNow, true)
        };
        var api = new FakeRemoteCheckInApiClient
        {
            OnGetDeviceInfoAsync = (_, _) => throw new RemoteCheckInApiException("未登录", isSessionInvalid: true)
        };
        var state = new AppRuntimeState { RemoteCheckInSession = credentials.StoredRemoteCheckInSession };
        var service = CreateService(api, credentials, state);

        await Assert.ThrowsAsync<RemoteCheckInApiException>(() => service.GetDeviceInfoAsync());

        Assert.Null(state.RemoteCheckInSession);
        Assert.Null(credentials.StoredRemoteCheckInSession);
        Assert.NotNull(credentials.StoredSession);
    }

    [Fact]
    public async Task ProfileService_UpsertsNormalizedProfileByLibrary()
    {
        var settings = new FakeSettingsService(AppSettings.Default);
        var service = new RemoteCheckInProfileService(settings);

        var saved = await service.SaveAsync(new RemoteCheckInVenueProfileSettings
        {
            LibraryId = 7,
            LibraryName = " 馆 ",
            BeaconUuid = BeaconUuid.ToLowerInvariant(),
            Major = 1,
            Minor = 2,
            Latitude = 39.1m,
            Longitude = 116.2m
        });

        Assert.Equal("馆", saved.LibraryName);
        Assert.Equal(BeaconUuid, saved.BeaconUuid);
        Assert.Single(settings.CurrentSettings.RemoteCheckIn.VenueProfiles);
    }

    private static RemoteCheckInWorkflowService CreateService(
        IRemoteCheckInApiClient api,
        FakeCredentialStore credentials,
        AppRuntimeState state)
    {
        return new RemoteCheckInWorkflowService(
            api,
            credentials,
            state,
            new ActivityLogService(),
            new FakeTimeProvider());
    }
}
