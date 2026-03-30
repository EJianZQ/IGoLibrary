using System.Net;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Tests;

internal sealed class FakeTraceIntApiClient : ITraceIntApiClient
{
    public Func<string, CancellationToken, Task<string>>? OnGetCookieFromCodeAsync { get; set; }
    public Func<string, CancellationToken, Task>? OnValidateCookieAsync { get; set; }
    public Func<string, CancellationToken, Task<IReadOnlyList<LibrarySummary>>>? OnGetLibrariesAsync { get; set; }
    public Func<string, int, CancellationToken, Task<LibraryLayout>>? OnGetLibraryLayoutAsync { get; set; }
    public Func<string, int, CancellationToken, Task<LibraryRule>>? OnGetLibraryRuleAsync { get; set; }
    public Func<string, CancellationToken, Task<ReservationInfo?>>? OnGetReservationInfoAsync { get; set; }
    public Func<string, int, string, CancellationToken, Task<bool>>? OnReserveSeatAsync { get; set; }
    public Func<string, string, CancellationToken, Task<bool>>? OnCancelReservationAsync { get; set; }

    public Task<string> GetCookieFromCodeAsync(string code, CancellationToken cancellationToken = default)
        => OnGetCookieFromCodeAsync?.Invoke(code, cancellationToken) ?? Task.FromResult(string.Empty);

    public Task ValidateCookieAsync(string cookie, CancellationToken cancellationToken = default)
        => OnValidateCookieAsync?.Invoke(cookie, cancellationToken) ?? Task.CompletedTask;

    public Task<IReadOnlyList<LibrarySummary>> GetLibrariesAsync(string cookie, CancellationToken cancellationToken = default)
        => OnGetLibrariesAsync?.Invoke(cookie, cancellationToken) ?? Task.FromResult<IReadOnlyList<LibrarySummary>>([]);

    public Task<LibraryLayout> GetLibraryLayoutAsync(string cookie, int libraryId, CancellationToken cancellationToken = default)
        => OnGetLibraryLayoutAsync?.Invoke(cookie, libraryId, cancellationToken)
           ?? throw new NotSupportedException();

    public Task<LibraryRule> GetLibraryRuleAsync(string cookie, int libraryId, CancellationToken cancellationToken = default)
        => OnGetLibraryRuleAsync?.Invoke(cookie, libraryId, cancellationToken)
           ?? throw new NotSupportedException();

    public Task<ReservationInfo?> GetReservationInfoAsync(string cookie, CancellationToken cancellationToken = default)
        => OnGetReservationInfoAsync?.Invoke(cookie, cancellationToken) ?? Task.FromResult<ReservationInfo?>(null);

    public Task<bool> ReserveSeatAsync(string cookie, int libraryId, string seatKey, CancellationToken cancellationToken = default)
        => OnReserveSeatAsync?.Invoke(cookie, libraryId, seatKey, cancellationToken) ?? Task.FromResult(false);

    public Task<bool> CancelReservationAsync(string cookie, string reservationToken, CancellationToken cancellationToken = default)
        => OnCancelReservationAsync?.Invoke(cookie, reservationToken, cancellationToken) ?? Task.FromResult(false);
}

internal sealed class FakeCredentialStore : ICredentialStore
{
    public SessionCredentials? StoredSession { get; set; }

    public int SaveCalls { get; private set; }

    public int ClearCalls { get; private set; }

    public Exception? ClearException { get; set; }

    public Exception? LoadException { get; set; }

    public Task SaveSessionAsync(SessionCredentials credentials, CancellationToken cancellationToken = default)
    {
        SaveCalls++;
        StoredSession = credentials;
        return Task.CompletedTask;
    }

    public Task<SessionCredentials?> LoadSessionAsync(CancellationToken cancellationToken = default)
    {
        if (LoadException is not null)
        {
            throw LoadException;
        }

        return Task.FromResult(StoredSession);
    }

    public Task ClearSessionAsync(CancellationToken cancellationToken = default)
    {
        if (ClearException is not null)
        {
            throw ClearException;
        }

        ClearCalls++;
        StoredSession = null;
        return Task.CompletedTask;
    }
}

internal sealed class FakeNotificationService : INotificationService
{
    public List<(string Title, string Message)> Infos { get; } = [];
    public List<(string Title, string Message)> Warnings { get; } = [];
    public List<(string Title, string Message)> Successes { get; } = [];

    public Task ShowInfoAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        Infos.Add((title, message));
        return Task.CompletedTask;
    }

    public Task ShowWarningAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        Warnings.Add((title, message));
        return Task.CompletedTask;
    }

    public Task ShowSuccessAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        Successes.Add((title, message));
        return Task.CompletedTask;
    }
}

internal sealed class FakeSettingsService(AppSettings settings) : ISettingsService
{
    public AppSettings CurrentSettings { get; private set; } = settings;

    public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(CurrentSettings);

    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        CurrentSettings = settings;
        return Task.CompletedTask;
    }
}

internal sealed class FakeSessionService : ISessionService
{
    public SessionCredentials? CurrentSession { get; set; }

    public SessionCredentials AuthenticateFromCookieResult { get; set; }
        = new("cookie", SessionSource.ManualCookie, DateTimeOffset.Now, true);

    public SessionCredentials? RestoreResult { get; set; }

    public int AuthenticateFromCookieCalls { get; private set; }

    public int RestoreCalls { get; private set; }

    public int SignOutCalls { get; private set; }

    public Task<SessionCredentials> AuthenticateFromCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        CurrentSession = AuthenticateFromCookieResult with
        {
            Source = SessionSource.QrCodeLink
        };
        return Task.FromResult(CurrentSession);
    }

    public Task<SessionCredentials> AuthenticateFromCookieAsync(string cookie, bool remember, CancellationToken cancellationToken = default)
    {
        AuthenticateFromCookieCalls++;
        CurrentSession = AuthenticateFromCookieResult with
        {
            Cookie = cookie,
            SavedAt = DateTimeOffset.Now,
            CanAutoRestore = remember
        };
        return Task.FromResult(CurrentSession);
    }

    public Task<SessionCredentials?> RestoreAsync(CancellationToken cancellationToken = default)
    {
        RestoreCalls++;
        CurrentSession = RestoreResult;
        return Task.FromResult(RestoreResult);
    }

    public Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        SignOutCalls++;
        CurrentSession = null;
        return Task.CompletedTask;
    }
}

internal sealed class FakeLibraryService : ILibraryService
{
    public LibrarySummary? BoundLibrary { get; private set; }

    public IReadOnlyList<LibrarySummary> LibrariesToLoad { get; set; } = [];

    public Dictionary<int, LibraryLayout> LayoutsByLibraryId { get; } = [];

    public Dictionary<int, IReadOnlyList<TrackedSeat>> FavoritesByLibraryId { get; } = [];

    public int LoadLibrariesCalls { get; private set; }

    public int BindLibraryCalls { get; private set; }

    public int RefreshBoundLibraryCalls { get; private set; }

    public int SaveFavoritesCalls { get; private set; }

    public Task<IReadOnlyList<LibrarySummary>> LoadLibrariesAsync(CancellationToken cancellationToken = default)
    {
        LoadLibrariesCalls++;
        return Task.FromResult(LibrariesToLoad);
    }

    public Task<LibraryLayout> BindLibraryAsync(int libraryId, CancellationToken cancellationToken = default)
    {
        BindLibraryCalls++;
        BoundLibrary = LibrariesToLoad.FirstOrDefault(x => x.LibraryId == libraryId);
        return Task.FromResult(LayoutsByLibraryId[libraryId]);
    }

    public Task<LibraryLayout> RefreshBoundLibraryAsync(CancellationToken cancellationToken = default)
    {
        RefreshBoundLibraryCalls++;
        if (BoundLibrary is null)
        {
            throw new InvalidOperationException("No bound library configured.");
        }

        return Task.FromResult(LayoutsByLibraryId[BoundLibrary.LibraryId]);
    }

    public Task<IReadOnlyList<TrackedSeat>> GetFavoritesAsync(int libraryId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(
            FavoritesByLibraryId.TryGetValue(libraryId, out var favorites)
                ? favorites
                : Array.Empty<TrackedSeat>() as IReadOnlyList<TrackedSeat>);
    }

    public Task SaveFavoritesAsync(int libraryId, IReadOnlyList<TrackedSeat> seats, CancellationToken cancellationToken = default)
    {
        SaveFavoritesCalls++;
        FavoritesByLibraryId[libraryId] = seats.ToArray();
        return Task.CompletedTask;
    }
}

internal sealed class FakeGrabSeatCoordinator : IGrabSeatCoordinator
{
    private CoordinatorStatus _status = CoordinatorStatus.Idle("抢座");

    public event EventHandler<CoordinatorStatus>? StatusChanged;

    public Task StartAsync(GrabSeatPlan plan, CancellationToken cancellationToken = default)
    {
        _status = new CoordinatorStatus(
            CoordinatorTaskState.Running,
            "抢座",
            "测试中的抢座任务",
            DateTimeOffset.Now,
            DateTimeOffset.Now);
        StatusChanged?.Invoke(this, _status);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        _status = new CoordinatorStatus(
            CoordinatorTaskState.Completed,
            "抢座",
            "测试中的抢座任务已停止",
            _status.StartedAt,
            DateTimeOffset.Now);
        StatusChanged?.Invoke(this, _status);
        return Task.CompletedTask;
    }

    public CoordinatorStatus GetStatus() => _status;
}

internal sealed class FakeOccupySeatCoordinator : IOccupySeatCoordinator
{
    private CoordinatorStatus _status = CoordinatorStatus.Idle("占座");

    public event EventHandler<CoordinatorStatus>? StatusChanged;

    public Task StartAsync(OccupySeatPlan plan, CancellationToken cancellationToken = default)
    {
        _status = new CoordinatorStatus(
            CoordinatorTaskState.Running,
            "占座",
            "测试中的占座任务",
            DateTimeOffset.Now,
            DateTimeOffset.Now);
        StatusChanged?.Invoke(this, _status);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        _status = new CoordinatorStatus(
            CoordinatorTaskState.Completed,
            "占座",
            "测试中的占座任务已停止",
            _status.StartedAt,
            DateTimeOffset.Now);
        StatusChanged?.Invoke(this, _status);
        return Task.CompletedTask;
    }

    public CoordinatorStatus GetStatus() => _status;
}

internal sealed class FakeProtocolTemplateStore(ProtocolTemplateSet templates) : IProtocolTemplateStore
{
    public ProtocolTemplateSet Templates { get; private set; } = templates;

    public Task<ProtocolTemplateSet> GetEffectiveTemplatesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Templates);

    public Task SaveOverridesAsync(ProtocolTemplateOverrides overrides, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task ResetOverridesAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

internal sealed class SequenceHttpMessageHandler(params Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>[] steps)
    : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _steps = new(steps);

    public int CallCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CallCount++;
        if (_steps.Count == 0)
        {
            throw new InvalidOperationException("没有更多预设响应。");
        }

        return _steps.Dequeue().Invoke(request, cancellationToken);
    }

    public static Task<HttpResponseMessage> JsonResponseAsync(string json)
    {
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json)
        });
    }
}
