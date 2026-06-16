using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Desktop;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Desktop.ViewModels;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Tests;

public sealed class MainWindowClipboardTests
{
    [Fact]
    public void CanTryAutoParseClipboard_ReturnsFalse_WhenActivePageIsNotAccountAndVenue()
    {
        var viewModel = CreateViewModel();
        viewModel.IsInitializationComplete = true;
        viewModel.SelectedTabIndex = 0;

        var canParse = MainWindow.CanTryAutoParseClipboard(
            viewModel,
            isWindowActive: true,
            clipboardAvailable: true,
            isAutoParsingClipboard: false);

        Assert.False(canParse);
    }

    [Fact]
    public void CanTryAutoParseClipboard_ReturnsTrue_WhenAccountAndVenuePageIsActive()
    {
        var viewModel = CreateViewModel();
        viewModel.IsInitializationComplete = true;
        viewModel.SelectedTabIndex = MainWindowViewModel.AccountAndVenueTabIndex;

        var canParse = MainWindow.CanTryAutoParseClipboard(
            viewModel,
            isWindowActive: true,
            clipboardAvailable: true,
            isAutoParsingClipboard: false);

        Assert.True(canParse);
    }

    [Fact]
    public void CanTryAutoParseClipboard_ReturnsFalse_WhenUserIsAlreadyAuthorized()
    {
        var viewModel = CreateViewModel();
        viewModel.IsInitializationComplete = true;
        viewModel.IsAuthorized = true;
        viewModel.SelectedTabIndex = MainWindowViewModel.AccountAndVenueTabIndex;

        var canParse = MainWindow.CanTryAutoParseClipboard(
            viewModel,
            isWindowActive: true,
            clipboardAvailable: true,
            isAutoParsingClipboard: false);

        Assert.False(canParse);
    }

    [Fact]
    public void ShouldSkipClipboardText_ReturnsTrue_WhenClipboardMatchesPreviousAttempt()
    {
        var shouldSkip = MainWindow.ShouldSkipClipboardText(
            "https://example.com/?code=1234567890abcdef1234567890abcdef",
            "https://example.com/?code=1234567890abcdef1234567890abcdef");

        Assert.True(shouldSkip);
    }

    [Fact]
    public void ShouldSkipClipboardText_ReturnsFalse_WhenClipboardChanged()
    {
        var shouldSkip = MainWindow.ShouldSkipClipboardText(
            "https://example.com/?code=1234567890abcdef1234567890abcdef",
            "https://example.com/?code=abcdef1234567890abcdef1234567890");

        Assert.False(shouldSkip);
    }

    [Fact]
    public void ShouldSkipClipboardText_ReturnsTrue_WhenCodeMatchesButLinkTextDiffers()
    {
        var shouldSkip = MainWindow.ShouldSkipClipboardText(
            "https://example.com/callback?code=1234567890abcdef1234567890abcdef&state=1",
            "https://another.example.com/auth?foo=1&code=1234567890abcdef1234567890abcdef");

        Assert.True(shouldSkip);
    }

    [Fact]
    public async Task RunUiEventHandlerAsync_ShowsWarning_WhenHandlerThrows()
    {
        var notificationService = new FakeNotificationService();

        await MainWindow.RunUiEventHandlerAsync(
            () => throw new InvalidOperationException("boom"),
            notificationService,
            "界面操作失败");

        var warning = Assert.Single(notificationService.Warnings);
        Assert.Equal("界面操作失败", warning.Title);
        Assert.Equal("boom", warning.Message);
    }

    [Fact]
    public async Task RunUiEventHandlerAsync_IgnoresCancellation()
    {
        var notificationService = new FakeNotificationService();

        await MainWindow.RunUiEventHandlerAsync(
            () => throw new OperationCanceledException(),
            notificationService,
            "界面操作失败");

        Assert.Empty(notificationService.Warnings);
    }

    [Fact]
    public async Task RunUiEventHandlerAsync_DoesNotThrow_WhenWarningNotificationFails()
    {
        var notificationService = new ThrowingNotificationService();

        await MainWindow.RunUiEventHandlerAsync(
            () => throw new InvalidOperationException("boom"),
            notificationService,
            "界面操作失败");

        Assert.True(notificationService.WarningAttempted);
    }

    private static MainWindowViewModel CreateViewModel()
    {
        var sessionService = new FakeSessionService();
        var libraryService = new FakeLibraryService();
        var apiClient = new FakeTraceIntApiClient();
        var settingsService = new FakeSettingsService(AppSettings.Default);
        var occupySeatCoordinator = new FakeOccupySeatCoordinator();
        var activityLogService = new ActivityLogService();
        var taskAlertService = new FakeTaskEventAlertDispatcher();

        return new MainWindowViewModel(
            new SessionWorkflowService(apiClient, sessionService),
            new VenueWorkflowService(libraryService, sessionService, apiClient, settingsService),
            new ReservationWorkflowService(sessionService, apiClient, occupySeatCoordinator, activityLogService),
            new SettingsWorkflowService(settingsService),
            new ProtocolTemplateEditorService(new FakeProtocolTemplateStore(new TraceIntGraphQlTemplates("", "", "", "", "", "", ""))),
            taskAlertService,
            new FakeGrabSeatCoordinator(),
            occupySeatCoordinator,
            new FakeTomorrowReservationCoordinator(),
            activityLogService,
            new FakeNotificationService(),
            new FakeErrorDialogService(),
            new FakeUpdateCheckService(),
            new FakeUpdateDialogService(),
            new FakeExternalLinkService(),
            new FakeAppThemeService(),
            new AppWindowService());
    }

    private sealed class ThrowingNotificationService : INotificationService
    {
        public bool WarningAttempted { get; private set; }

        public Task ShowInfoAsync(string title, string message, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ShowWarningAsync(string title, string message, CancellationToken cancellationToken = default)
        {
            WarningAttempted = true;
            throw new InvalidOperationException("toast failed");
        }

        public Task ShowSuccessAsync(string title, string message, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
