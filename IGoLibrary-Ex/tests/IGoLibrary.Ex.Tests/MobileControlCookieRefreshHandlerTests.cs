using Avalonia.Threading;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Desktop.ViewModels;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Tests;

public sealed class MobileControlCookieRefreshHandlerTests
{
    [Fact]
    public async Task RefreshCookieFromLinkAsync_WhenRequestIsCanceledAfterUiWorkStarts_WaitsForUiWorkToComplete()
    {
        const string code = "1234567890abcdef1234567890abcdef";
        const string cookie = "Authorization=mobile-refresh-test; SERVERID=s";
        var authStarted = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var authRelease = new TaskCompletionSource<SessionWorkflowResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var sessionViewModel = CreateSessionViewModel((receivedCode, _) =>
        {
            Assert.Equal(code, receivedCode);
            authStarted.TrySetResult(null);
            return authRelease.Task;
        });
        var handler = new MobileControlCookieRefreshHandler(sessionViewModel);
        using var cancellation = new CancellationTokenSource();

        var refreshTask = Task.Run(() => handler.RefreshCookieFromLinkAsync(
            $"https://example.test/auth?code={code}",
            cancellation.Token));
        await WaitForAsync(() =>
        {
            Dispatcher.UIThread.RunJobs();
            return authStarted.Task.IsCompleted;
        });

        cancellation.Cancel();
        await Task.Delay(50);
        Dispatcher.UIThread.RunJobs();

        Assert.False(refreshTask.IsCompleted);

        authRelease.SetResult(new SessionWorkflowResult(
            new SessionCredentials(cookie, SessionSource.ManualCookie, DateTimeOffset.Now, true),
            cookie,
            CookieExpirationTime: null,
            ShouldLoadLibraries: false,
            StatusMessage: "Cookie 已刷新"));
        await WaitForAsync(() =>
        {
            Dispatcher.UIThread.RunJobs();
            return refreshTask.IsCompleted;
        });

        var result = await refreshTask;
        Assert.True(result.Authenticated);
        Assert.True(sessionViewModel.IsAuthorized);
        Assert.Equal(cookie, sessionViewModel.ManualCookieText);
    }

    private static SessionViewModel CreateSessionViewModel(
        Func<string, bool, Task<SessionWorkflowResult>> authenticateFromCodeAsync)
    {
        var viewModel = new SessionViewModel(
            new ActivityLogService(),
            new FakeNotificationService(),
            new FakeAppThemeService(),
            new FakeTimeProvider(),
            new OAuthCodeConsumptionRegistry());
        viewModel.ConfigureOrchestration(
            authenticateFromCodeAsync,
            (_, _) => throw new InvalidOperationException("Cookie auth is not used by this test."),
            () => Task.FromResult(new SessionWorkflowResult(null, null, null, false, "未恢复")),
            () => Task.CompletedTask,
            (_, _) => Task.CompletedTask,
            _ => Task.CompletedTask,
            () => Task.CompletedTask,
            () => Task.CompletedTask,
            () => true,
            _ => { },
            () => { },
            () => { },
            () => { },
            _ => { },
            () => { },
            () => { });
        return viewModel;
    }

    private static async Task WaitForAsync(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);

        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("Condition was not met within the expected time.");
    }
}
