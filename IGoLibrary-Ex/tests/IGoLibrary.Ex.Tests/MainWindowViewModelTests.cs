using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Desktop.ViewModels;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;
using Avalonia.Media;

namespace IGoLibrary.Ex.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public async Task ValidateManualCookieAsync_DoesNotRestoreStoredVenueSelection_OnFreshAuthorization()
    {
        var settingsService = new FakeSettingsService(AppSettings.Default with
        {
            LastLibraryId = 1,
            LastLibraryName = "场馆A"
        });
        var libraryService = new FakeLibraryService
        {
            LibrariesToLoad =
            [
                new LibrarySummary(1, "场馆A", "3层", true, 120, 20, 10),
                new LibrarySummary(2, "场馆B", "5层", true, 80, 10, 5)
            ]
        };
        var viewModel = CreateViewModel(
            sessionService: new FakeSessionService(),
            libraryService: libraryService,
            settingsService: settingsService);

        viewModel.ManualCookieText = "Authorization=a; SERVERID=b";

        await viewModel.ValidateManualCookieCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsAuthorized);
        Assert.Null(viewModel.SelectedLibrary);
        Assert.Equal(1, libraryService.LoadLibrariesCalls);
    }

    [Fact]
    public async Task SignOutAsync_ClearsStoredLastLibrarySelection()
    {
        var settingsService = new FakeSettingsService(AppSettings.Default with
        {
            LastLibraryId = 1,
            LastLibraryName = "场馆A"
        });
        var sessionService = new FakeSessionService
        {
            CurrentSession = new SessionCredentials("cookie", SessionSource.ManualCookie, DateTimeOffset.Now, true)
        };
        var viewModel = CreateViewModel(
            sessionService: sessionService,
            settingsService: settingsService);

        viewModel.IsAuthorized = true;
        viewModel.SelectedLibrary = new LibrarySummary(1, "场馆A", "3层", true, 120, 20, 10);

        await viewModel.SignOutCommand.ExecuteAsync(null);

        Assert.Equal(1, sessionService.SignOutCalls);
        Assert.False(viewModel.IsAuthorized);
        Assert.Null(viewModel.SelectedLibrary);
        Assert.Null(settingsService.CurrentSettings.LastLibraryId);
        Assert.Null(settingsService.CurrentSettings.LastLibraryName);
    }

    [Fact]
    public async Task OpenVenuePickerAsync_PreservesCurrentLockedLibrary_WhenOneIsAlreadyBound()
    {
        var libraryA = new LibrarySummary(1, "场馆A", "3层", true, 120, 20, 10);
        var libraryB = new LibrarySummary(2, "场馆B", "5层", true, 80, 10, 5);
        var sessionService = new FakeSessionService
        {
            CurrentSession = new SessionCredentials("cookie", SessionSource.ManualCookie, DateTimeOffset.Now, true)
        };
        var settingsService = new FakeSettingsService(AppSettings.Default with
        {
            LastLibraryId = libraryB.LibraryId,
            LastLibraryName = libraryB.Name
        });
        var libraryService = new FakeLibraryService
        {
            LibrariesToLoad = [libraryA, libraryB]
        };
        libraryService.LayoutsByLibraryId[libraryA.LibraryId] = new LibraryLayout(
            libraryA.LibraryId,
            libraryA.Name,
            libraryA.Floor,
            libraryA.IsOpen,
            120,
            10,
            20,
            [new SeatSnapshot("seat-1", "1", false, 0, 0)]);

        var apiClient = new FakeTraceIntApiClient
        {
            OnGetLibraryRuleAsync = (_, _, _) => Task.FromResult(new LibraryRule(
                libraryA.LibraryId,
                "1小时",
                "30",
                "30",
                "0",
                "{}",
                null,
                null,
                0,
                "07:30",
                0,
                "22:00",
                -1))
        };
        var viewModel = CreateViewModel(
            sessionService: sessionService,
            libraryService: libraryService,
            settingsService: settingsService,
            apiClient: apiClient);

        viewModel.IsAuthorized = true;
        viewModel.SelectedLibrary = libraryA;

        await viewModel.BindSelectedLibraryCommand.ExecuteAsync(null);
        await viewModel.OpenVenuePickerCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsVenuePickerOpen);
        Assert.Equal(libraryA.LibraryId, viewModel.SelectedLibrary?.LibraryId);
        Assert.Equal(1, libraryService.LoadLibrariesCalls);
    }

    [Fact]
    public async Task GrabDashboardStatusBrush_UsesFailureColor_WhenTaskCompletedByStopping()
    {
        var grabCoordinator = new FakeGrabSeatCoordinator();
        await grabCoordinator.StopAsync();

        var viewModel = CreateViewModel(grabSeatCoordinator: grabCoordinator);
        await viewModel.InitializeAsync();

        var brush = Assert.IsType<SolidColorBrush>(viewModel.GrabDashboardStatusBrush);

        Assert.Equal("已停止", viewModel.GrabDashboardStatusText);
        Assert.Equal(Color.Parse("#C93C37"), brush.Color);
    }

    private static MainWindowViewModel CreateViewModel(
        FakeSessionService? sessionService = null,
        FakeLibraryService? libraryService = null,
        FakeSettingsService? settingsService = null,
        FakeTraceIntApiClient? apiClient = null,
        FakeGrabSeatCoordinator? grabSeatCoordinator = null)
    {
        return new MainWindowViewModel(
            sessionService ?? new FakeSessionService(),
            libraryService ?? new FakeLibraryService(),
            apiClient ?? new FakeTraceIntApiClient(),
            settingsService ?? new FakeSettingsService(AppSettings.Default),
            new FakeProtocolTemplateStore(new ProtocolTemplateSet("", "", "", "", "", "", "")),
            grabSeatCoordinator ?? new FakeGrabSeatCoordinator(),
            new FakeOccupySeatCoordinator(),
            new ActivityLogService(),
            new FakeNotificationService(),
            new AppWindowService());
    }
}
