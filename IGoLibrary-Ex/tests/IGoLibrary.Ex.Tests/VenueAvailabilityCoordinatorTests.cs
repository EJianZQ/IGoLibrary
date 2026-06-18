using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Exceptions;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Application.State;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Tests;

public sealed class VenueAvailabilityCoordinatorTests
{
    [Fact]
    public async Task RunAsync_NotifiesWhenVenueChangesFromFullToAvailable()
    {
        var runtime = new FakeCoordinatorRuntime
        {
            BlockDelaysStartingAtCall = 2
        };
        var eventPublisher = new FakeCoordinatorEventPublisher();
        var layouts = new Queue<LibraryLayout>([
            CreateLayout(availableSeats: 0),
            CreateLayout(availableSeats: 3)
        ]);
        var apiClient = new FakeTraceIntApiClient
        {
            OnGetLibraryLayoutAsync = (_, _, _) => Task.FromResult(layouts.Dequeue())
        };
        var coordinator = CreateCoordinator(apiClient, runtime, eventPublisher);

        await coordinator.StartAsync(CreatePlan());
        await WaitForAsync(() => eventPublisher.EventsOf<VenueAvailableCoordinatorEvent>().Count == 1);
        await coordinator.StopAsync();

        var alert = Assert.Single(eventPublisher.EventsOf<VenueAvailableCoordinatorEvent>());
        Assert.Equal("自科阅览区一", alert.LibraryName);
        Assert.Equal(3, alert.AvailableSeats);
        Assert.Equal(CoordinatorStatusReason.Stopped, coordinator.GetStatus().Reason);
    }

    [Fact]
    public async Task RunAsync_DoesNotNotifyInitialAvailabilityUntilVenueBecomesFullAgain()
    {
        var runtime = new FakeCoordinatorRuntime
        {
            BlockDelaysStartingAtCall = 4
        };
        var eventPublisher = new FakeCoordinatorEventPublisher();
        var layouts = new Queue<LibraryLayout>([
            CreateLayout(availableSeats: 2),
            CreateLayout(availableSeats: 1),
            CreateLayout(availableSeats: 0),
            CreateLayout(availableSeats: 4)
        ]);
        var apiClient = new FakeTraceIntApiClient
        {
            OnGetLibraryLayoutAsync = (_, _, _) => Task.FromResult(layouts.Dequeue())
        };
        var coordinator = CreateCoordinator(apiClient, runtime, eventPublisher);

        await coordinator.StartAsync(CreatePlan());
        await WaitForAsync(() => eventPublisher.EventsOf<VenueAvailableCoordinatorEvent>().Count == 1);
        await coordinator.StopAsync();

        var alert = Assert.Single(eventPublisher.EventsOf<VenueAvailableCoordinatorEvent>());
        Assert.Equal(4, alert.AvailableSeats);
    }

    [Fact]
    public async Task RunAsync_PublishesSessionInvalid_WhenApiRejectsAuthorization()
    {
        var expiresAt = DateTimeOffset.Now.AddMinutes(-1);
        var runtime = new FakeCoordinatorRuntime
        {
            Now = DateTimeOffset.Now
        };
        var eventPublisher = new FakeCoordinatorEventPublisher();
        var layoutCallCount = 0;
        var apiClient = new FakeTraceIntApiClient
        {
            OnGetLibraryLayoutAsync = (_, _, _) =>
            {
                layoutCallCount++;
                throw new TraceIntApiException("access denied!", 40001, "access denied!", isAuthorizationDenied: true);
            }
        };
        var coordinator = CreateCoordinator(
            apiClient,
            runtime,
            eventPublisher,
            new SessionCredentials(BuildAuthorizationCookie(expiresAt), SessionSource.ManualCookie, DateTimeOffset.Now, true));

        await coordinator.StartAsync(CreatePlan());
        await WaitForStatusAsync(coordinator, CoordinatorTaskState.Failed);

        Assert.Equal(CoordinatorStatusReason.SessionInvalid, coordinator.GetStatus().Reason);
        Assert.Equal(1, layoutCallCount);
        var alert = Assert.Single(eventPublisher.EventsOf<SessionInvalidCoordinatorEvent>());
        Assert.Equal("空座追踪", alert.Source);
    }

    private static VenueAvailabilityCoordinator CreateCoordinator(
        FakeTraceIntApiClient apiClient,
        FakeCoordinatorRuntime runtime,
        FakeCoordinatorEventPublisher eventPublisher,
        SessionCredentials? session = null)
    {
        var state = new AppRuntimeState
        {
            Session = session ?? new SessionCredentials("cookie", SessionSource.ManualCookie, DateTimeOffset.Now, true)
        };
        var runner = new VenueAvailabilityWorkflowRunner(
            apiClient,
            eventPublisher,
            new ActivityLogService(),
            state,
            state,
            runtime);

        return new VenueAvailabilityCoordinator(runner, runtime);
    }

    private static VenueAvailabilityWatchPlan CreatePlan()
    {
        return new VenueAvailabilityWatchPlan(
            LibraryId: 12,
            LibraryName: "自科阅览区一",
            PollingInterval: TimeSpan.FromSeconds(5));
    }

    private static LibraryLayout CreateLayout(int availableSeats)
    {
        const int totalSeats = 10;
        return new LibraryLayout(
            12,
            "自科阅览区一",
            "3层",
            true,
            totalSeats,
            BookedSeats: 0,
            UsedSeats: totalSeats - availableSeats,
            Seats: []);
    }

    private static async Task WaitForStatusAsync(IVenueAvailabilityCoordinator coordinator, CoordinatorTaskState expectedState)
    {
        await WaitForAsync(() => coordinator.GetStatus().State == expectedState);
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

            await Task.Delay(50);
        }

        throw new TimeoutException("Condition was not met within the expected time.");
    }

    private static string BuildAuthorizationCookie(DateTimeOffset expiresAt)
    {
        var payloadJson = $$"""{"expireAt":{{expiresAt.ToUnixTimeSeconds()}}}""";
        var payload = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(payloadJson))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return $"Authorization=eyFake.{payload}.signature";
    }
}
