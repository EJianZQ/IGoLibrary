using System.Net;
using Avalonia.Controls;
using Avalonia.Media;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;
using IGoLibrary.Ex.Infrastructure.Notifications;
using MailKit.Security;
using MimeKit;

namespace IGoLibrary.Ex.Tests;
internal sealed class FakeErrorDialogService : IErrorDialogService
{
    public List<(string Title, string ErrorType, string ErrorMessage)> Errors { get; } = [];

    public Task ShowErrorAsync(string title, string errorType, string errorMessage, CancellationToken cancellationToken = default)
    {
        Errors.Add((title, errorType, errorMessage));
        return Task.CompletedTask;
    }
}

internal sealed class FakeAppThemeService : IAppThemeService
{
    private static readonly AppThemePalette LightPalette = new(
        IdleBrush: new SolidColorBrush(Color.Parse("#86909C")),
        RunningBrush: new SolidColorBrush(Color.Parse("#0077FA")),
        SuccessBrush: new SolidColorBrush(Color.Parse("#14804A")),
        WarningBrush: new SolidColorBrush(Color.Parse("#C27803")),
        FailureBrush: new SolidColorBrush(Color.Parse("#C93C37")),
        RunningSoftBrush: new SolidColorBrush(Color.Parse("#E8F3FF")),
        SuccessSoftBrush: new SolidColorBrush(Color.Parse("#E8FFF1")),
        WarningSoftBrush: new SolidColorBrush(Color.Parse("#FFF5E7")),
        NeutralSoftBrush: new SolidColorBrush(Color.Parse("#F1F5F9")),
        NotificationSegmentActiveTextBrush: new SolidColorBrush(Color.Parse("#1D2129")),
        NotificationSegmentInactiveTextBrush: new SolidColorBrush(Color.Parse("#86909C")),
        LogDefaultBrush: new SolidColorBrush(Color.Parse("#1D2129")),
        LogSuccessBrush: new SolidColorBrush(Color.Parse("#16A34A")),
        LogErrorBrush: new SolidColorBrush(Color.Parse("#DC2626")));

    private static readonly AppThemePalette DarkPalette = new(
        IdleBrush: new SolidColorBrush(Color.Parse("#94A3B8")),
        RunningBrush: new SolidColorBrush(Color.Parse("#0077FA")),
        SuccessBrush: new SolidColorBrush(Color.Parse("#4ADE80")),
        WarningBrush: new SolidColorBrush(Color.Parse("#FBBF24")),
        FailureBrush: new SolidColorBrush(Color.Parse("#FB7185")),
        RunningSoftBrush: new SolidColorBrush(Color.Parse("#182C45")),
        SuccessSoftBrush: new SolidColorBrush(Color.Parse("#123021")),
        WarningSoftBrush: new SolidColorBrush(Color.Parse("#3A2A0E")),
        NeutralSoftBrush: new SolidColorBrush(Color.Parse("#182230")),
        NotificationSegmentActiveTextBrush: new SolidColorBrush(Color.Parse("#F8FAFC")),
        NotificationSegmentInactiveTextBrush: new SolidColorBrush(Color.Parse("#94A3B8")),
        LogDefaultBrush: new SolidColorBrush(Color.Parse("#E2E8F0")),
        LogSuccessBrush: new SolidColorBrush(Color.Parse("#4ADE80")),
        LogErrorBrush: new SolidColorBrush(Color.Parse("#F87171")));

    public event EventHandler<AppThemePalette>? PaletteChanged;

    public AppThemePalette CurrentPalette { get; private set; } = LightPalette;

    public int InitializeCalls { get; private set; }

    public int ApplySettingsCalls { get; private set; }

    public ThemeSettings? LastAppliedTheme { get; private set; }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        InitializeCalls++;
        return Task.CompletedTask;
    }

    public Task ApplyThemeAsync(ThemeSettings theme, CancellationToken cancellationToken = default)
    {
        ApplySettingsCalls++;
        LastAppliedTheme = theme;
        CurrentPalette = theme.Mode == AppThemeMode.Dark
            ? DarkPalette
            : LightPalette;
        PaletteChanged?.Invoke(this, CurrentPalette);
        return Task.CompletedTask;
    }

    public void AttachTopLevel(TopLevel topLevel)
    {
    }
}

internal sealed class FakeSettingsService(AppSettings settings) : ISettingsService
{
    public AppSettings CurrentSettings { get; private set; } = settings;

    public int SaveCalls { get; private set; }

    public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(CurrentSettings);

    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        SaveCalls++;
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

    public Exception? AuthenticateFromCookieException { get; set; }

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
        if (AuthenticateFromCookieException is not null)
        {
            throw AuthenticateFromCookieException;
        }

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

    public int StopCalls { get; private set; }

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
        StopCalls++;
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
