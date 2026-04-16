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

    private static MainWindowViewModel CreateViewModel()
    {
        return new MainWindowViewModel(
            new FakeSessionService(),
            new FakeLibraryService(),
            new FakeTraceIntApiClient(),
            new FakeSettingsService(AppSettings.Default),
            new FakeProtocolTemplateStore(new ProtocolTemplateSet("", "", "", "", "", "", "")),
            new FakeGrabSeatCoordinator(),
            new FakeOccupySeatCoordinator(),
            new FakeTaskAlertService(),
            new ActivityLogService(),
            new FakeNotificationService(),
            new FakeErrorDialogService(),
            new FakeAppThemeService(),
            new AppWindowService());
    }
}
