using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Tests;

public sealed class WorkflowServiceTests
{
    [Fact]
    public async Task SessionWorkflowService_AuthenticateFromCodeAsync_ReturnsSessionAndCookie_WhenAutoValidationSucceeds()
    {
        var apiClient = new FakeTraceIntApiClient
        {
            OnGetCookieFromCodeAsync = (_, _) => Task.FromResult("Authorization=token; SERVERID=s")
        };
        var sessionService = new FakeSessionService();
        var service = new SessionWorkflowService(apiClient, sessionService);

        var result = await service.AuthenticateFromCodeAsync("code", remember: true);

        Assert.NotNull(result.Session);
        Assert.Equal("Authorization=token; SERVERID=s", result.Cookie);
        Assert.True(result.ShouldLoadLibraries);
        Assert.Equal(1, sessionService.AuthenticateFromCookieCalls);
    }

    [Fact]
    public async Task SessionWorkflowService_AuthenticateFromCodeAsync_ReturnsCookie_WhenAutoValidationFails()
    {
        var apiClient = new FakeTraceIntApiClient
        {
            OnGetCookieFromCodeAsync = (_, _) => Task.FromResult("cookie")
        };
        var sessionService = new FakeSessionService
        {
            AuthenticateFromCookieException = new InvalidOperationException("invalid cookie")
        };
        var service = new SessionWorkflowService(apiClient, sessionService);

        var result = await service.AuthenticateFromCodeAsync("code", remember: false);

        Assert.Null(result.Session);
        Assert.Equal("cookie", result.Cookie);
        Assert.False(result.ShouldLoadLibraries);
        Assert.Equal("invalid cookie", result.AuthenticationFailureMessage);
    }

    [Fact]
    public async Task VenueWorkflowService_LoadLibrariesAsync_RestoresPreferredSelection()
    {
        var libraryA = new LibrarySummary(1, "A", "3F", true);
        var libraryB = new LibrarySummary(2, "B", "5F", true);
        var libraryService = new FakeLibraryService
        {
            LibrariesToLoad = [libraryA, libraryB]
        };
        var settingsService = new FakeSettingsService(AppSettings.Default with
        {
            Venue = new VenueSelectionSettings(2, "B")
        });
        var service = new VenueWorkflowService(
            libraryService,
            new FakeSessionService(),
            new FakeTraceIntApiClient(),
            settingsService);

        var result = await service.LoadLibrariesAsync(restorePreferredSelection: true);

        Assert.Equal([libraryA, libraryB], result.Libraries);
        Assert.Equal(libraryB, result.SelectedLibrary);
    }

    [Fact]
    public async Task VenueWorkflowService_BindLibraryAsync_ReturnsLayoutRuleAndFavorites()
    {
        var library = new LibrarySummary(7, "自科", "4F", true);
        var layout = new LibraryLayout(7, "自科", "4F", true, 10, 1, 2, []);
        var rule = new LibraryRule(7, "", "", "", "", "", null, null, 0, "08:00", 0, "22:00", 0);
        var favorites = new SeatReference[] { new("seat-1", "1") };
        var libraryService = new FakeLibraryService
        {
            LibrariesToLoad = [library]
        };
        libraryService.LayoutsByLibraryId[7] = layout;
        libraryService.FavoritesByLibraryId[7] = favorites;
        var apiClient = new FakeTraceIntApiClient
        {
            OnGetLibraryRuleAsync = (_, _, _) => Task.FromResult(rule)
        };
        var service = new VenueWorkflowService(
            libraryService,
            new FakeSessionService
            {
                CurrentSession = new SessionCredentials("cookie", SessionSource.ManualCookie, DateTimeOffset.Now, true)
            },
            apiClient,
            new FakeSettingsService(AppSettings.Default));

        var result = await service.BindLibraryAsync(7);

        Assert.Equal(layout, result.Layout);
        Assert.Equal(rule, result.Rule);
        Assert.Equal(favorites, result.Favorites);
    }

    [Fact]
    public async Task VenueWorkflowService_BindLibraryAsync_ReturnsRuleFailureMessage_WhenRuleLoadFails()
    {
        var library = new LibrarySummary(7, "自科", "4F", true);
        var layout = new LibraryLayout(7, "自科", "4F", true, 10, 1, 2, []);
        var libraryService = new FakeLibraryService
        {
            LibrariesToLoad = [library]
        };
        libraryService.LayoutsByLibraryId[7] = layout;
        var apiClient = new FakeTraceIntApiClient
        {
            OnGetLibraryRuleAsync = (_, _, _) => throw new InvalidOperationException("rule failed")
        };
        var service = new VenueWorkflowService(
            libraryService,
            new FakeSessionService
            {
                CurrentSession = new SessionCredentials("cookie", SessionSource.ManualCookie, DateTimeOffset.Now, true)
            },
            apiClient,
            new FakeSettingsService(AppSettings.Default));

        var result = await service.BindLibraryAsync(7);

        Assert.Equal(layout, result.Layout);
        Assert.Null(result.Rule);
        Assert.Equal("rule failed", result.RuleFailureMessage);
    }

    [Fact]
    public async Task ReservationWorkflowService_CancelCurrentReservationAsync_StopsOccupyBeforeRemoteCancel()
    {
        var reservation = new ReservationInfo("token", 1, "馆", "seat-1", "1", DateTimeOffset.Now.AddMinutes(20));
        var apiClient = new FakeTraceIntApiClient
        {
            OnCancelReservationAsync = (_, _, _) => Task.FromResult(true)
        };
        var occupySeatCoordinator = new FakeOccupySeatCoordinator();
        var service = new ReservationWorkflowService(
            new FakeSessionService
            {
                CurrentSession = new SessionCredentials("cookie", SessionSource.ManualCookie, DateTimeOffset.Now, true)
            },
            apiClient,
            occupySeatCoordinator,
            new ActivityLogService());

        var result = await service.CancelCurrentReservationAsync(reservation, stopOccupyFirst: true);

        Assert.True(result.Succeeded);
        Assert.Null(result.Reservation);
        Assert.Equal(1, occupySeatCoordinator.StopCalls);
    }

    [Fact]
    public async Task ReservationWorkflowService_RefreshReservationAsync_ReturnsNoSessionResult_WhenSessionMissing()
    {
        var service = new ReservationWorkflowService(
            new FakeSessionService(),
            new FakeTraceIntApiClient(),
            new FakeOccupySeatCoordinator(),
            new ActivityLogService());

        var result = await service.RefreshReservationAsync();

        Assert.True(result.Succeeded);
        Assert.False(result.HasSession);
        Assert.Null(result.Reservation);
    }

    [Fact]
    public async Task SettingsWorkflowService_SaveSystemSettingsAsync_PreservesVenueAndDashboard()
    {
        var initial = AppSettings.Default with
        {
            Venue = new VenueSelectionSettings(3, "三楼"),
            Dashboard = new DashboardMetrics(8, 900)
        };
        var settingsService = new FakeSettingsService(initial);
        var service = new SettingsWorkflowService(settingsService);

        await service.SaveSystemSettingsAsync(new SystemSettingsSnapshot(
            AppBannerNotificationsEnabled: false,
            MinimizeToTray: false,
            TraceIntGraphQlOverridesEnabled: true,
            CheckUpdatesOnStartup: false,
            RequestTimeoutSeconds: 1,
            NetworkMaxRetries: 0,
            Theme: new ThemePreferences(AppThemeMode.Dark, useSystemAccent: false),
            GrabReservationStrategy: GrabReservationStrategy.ReserveDirectly,
            TaskEventAlerts: TaskEventAlertSettings.Default));

        Assert.Equal(initial.Venue, settingsService.CurrentSettings.Venue);
        Assert.Equal(initial.Dashboard, settingsService.CurrentSettings.Dashboard);
        Assert.Equal(3, settingsService.CurrentSettings.Network.TimeoutSeconds);
        Assert.Equal(0, settingsService.CurrentSettings.Network.MaxRetries);
        Assert.False(settingsService.CurrentSettings.Updates.CheckOnStartup);
        Assert.Equal(GrabReservationStrategy.ReserveDirectly, settingsService.CurrentSettings.Tasks.Grab.ReservationStrategy);
    }

    [Fact]
    public async Task SettingsWorkflowService_SaveNotificationSettingsAsync_OnlyChangesAlertSettings()
    {
        var initial = AppSettings.Default with
        {
            Notifications = new NotificationSettings(true, TaskEventAlertSettings.Default),
            Ui = new UiPreferences(false, new ThemePreferences(AppThemeMode.Dark, false))
        };
        var settingsService = new FakeSettingsService(initial);
        var service = new SettingsWorkflowService(settingsService);
        var alerts = new TaskEventAlertSettings(
            EmailAlertChannelSettings.Default with { Enabled = true },
            LocalDesktopAlertSettings.Default,
            TelegramAlertChannelSettings.Default);

        await service.SaveNotificationSettingsAsync(alerts);

        Assert.Equal(alerts, settingsService.CurrentSettings.Notifications.TaskEventAlerts);
        Assert.Equal(initial.Notifications.AppBannerNotificationsEnabled, settingsService.CurrentSettings.Notifications.AppBannerNotificationsEnabled);
        Assert.Equal(initial.Ui, settingsService.CurrentSettings.Ui);
    }

    [Fact]
    public async Task SettingsWorkflowService_SaveGrabScheduledStartDefaultAsync_OnlyChangesGrabTime()
    {
        var initial = AppSettings.Default with
        {
            Venue = new VenueSelectionSettings(3, "三楼"),
            Dashboard = new DashboardMetrics(8, 900),
            Tasks = new TaskExecutionSettings(
                new GrabTaskSettings(GrabReservationStrategy.ReserveDirectly),
                new OccupyTaskSettings(6),
                new TomorrowReservationTaskSettings(new TimeSpan(21, 0, 0)))
        };
        var settingsService = new FakeSettingsService(initial);
        var service = new SettingsWorkflowService(settingsService);

        await service.SaveGrabScheduledStartDefaultAsync(new TimeSpan(7, 59, 55));

        Assert.Equal(new TimeSpan(7, 59, 55), settingsService.CurrentSettings.Tasks.Grab.DefaultScheduledStartTime);
        Assert.Equal(GrabReservationStrategy.ReserveDirectly, settingsService.CurrentSettings.Tasks.Grab.ReservationStrategy);
        Assert.Equal(6, settingsService.CurrentSettings.Tasks.Occupy.ReReservationMaxAttempts);
        Assert.Equal(new TimeSpan(21, 0, 0), settingsService.CurrentSettings.Tasks.TomorrowReservation.DefaultScheduledStartTime);
        Assert.Equal(initial.Venue, settingsService.CurrentSettings.Venue);
        Assert.Equal(initial.Dashboard, settingsService.CurrentSettings.Dashboard);
    }

    [Fact]
    public async Task SettingsWorkflowService_SaveTomorrowScheduledStartDefaultAsync_OnlyChangesTomorrowTime()
    {
        var initial = AppSettings.Default with
        {
            Tasks = new TaskExecutionSettings(
                new GrabTaskSettings(GrabReservationStrategy.ReserveDirectly, new TimeSpan(7, 59, 55)),
                new OccupyTaskSettings(6),
                TomorrowReservationTaskSettings.Default)
        };
        var settingsService = new FakeSettingsService(initial);
        var service = new SettingsWorkflowService(settingsService);

        await service.SaveTomorrowScheduledStartDefaultAsync(new TimeSpan(22, 1, 2));

        Assert.Equal(new TimeSpan(7, 59, 55), settingsService.CurrentSettings.Tasks.Grab.DefaultScheduledStartTime);
        Assert.Equal(GrabReservationStrategy.ReserveDirectly, settingsService.CurrentSettings.Tasks.Grab.ReservationStrategy);
        Assert.Equal(6, settingsService.CurrentSettings.Tasks.Occupy.ReReservationMaxAttempts);
        Assert.Equal(new TimeSpan(22, 1, 2), settingsService.CurrentSettings.Tasks.TomorrowReservation.DefaultScheduledStartTime);
    }

    [Fact]
    public async Task SettingsWorkflowService_SaveGlobalLeakSelectedLibrariesAsync_UpdatesSelection()
    {
        var settingsService = new FakeSettingsService(AppSettings.Default);
        var service = new SettingsWorkflowService(settingsService);

        await service.SaveGlobalLeakSelectedLibrariesAsync(
            [
                new GlobalLeakLibraryTarget(1, "场馆A", "3层"),
                new GlobalLeakLibraryTarget(2, "场馆B", "5层")
            ]);

        Assert.Equal(1, settingsService.SaveCalls);
        Assert.Equal([1, 2], settingsService.CurrentSettings.Tasks.GlobalLeak.SelectedLibraries.Select(x => x.LibraryId).ToArray());
        Assert.Equal(["场馆A", "场馆B"], settingsService.CurrentSettings.Tasks.GlobalLeak.SelectedLibraries.Select(x => x.LibraryName).ToArray());
        Assert.Equal(["3层", "5层"], settingsService.CurrentSettings.Tasks.GlobalLeak.SelectedLibraries.Select(x => x.Floor).ToArray());
    }

    [Fact]
    public async Task SettingsWorkflowService_SaveGlobalLeakSelectedLibrariesAsync_DoesNotSave_WhenSelectionUnchanged()
    {
        var selectedLibraries = new[]
        {
            new GlobalLeakLibrarySelectionSettings(1, "场馆A", "3层")
        };
        var settingsService = new FakeSettingsService(AppSettings.Default with
        {
            Tasks = AppSettings.Default.Tasks with
            {
                GlobalLeak = new GlobalLeakTaskSettings(selectedLibraries)
            }
        });
        var service = new SettingsWorkflowService(settingsService);

        await service.SaveGlobalLeakSelectedLibrariesAsync(
            [
                new GlobalLeakLibraryTarget(1, "场馆A", "3层")
            ]);

        Assert.Equal(0, settingsService.SaveCalls);
    }

    [Fact]
    public async Task SettingsWorkflowService_SaveGlobalLeakSelectedLibrariesAsync_ClearsSelection()
    {
        var settingsService = new FakeSettingsService(AppSettings.Default with
        {
            Tasks = AppSettings.Default.Tasks with
            {
                GlobalLeak = new GlobalLeakTaskSettings(
                [
                    new GlobalLeakLibrarySelectionSettings(1, "场馆A", "3层")
                ])
            }
        });
        var service = new SettingsWorkflowService(settingsService);

        await service.SaveGlobalLeakSelectedLibrariesAsync([]);

        Assert.Equal(1, settingsService.SaveCalls);
        Assert.Empty(settingsService.CurrentSettings.Tasks.GlobalLeak.SelectedLibraries);
    }

    [Fact]
    public async Task SettingsWorkflowService_ScheduledStartDefaults_IgnoresOutOfRangeValues()
    {
        var settingsService = new FakeSettingsService(AppSettings.Default);
        var service = new SettingsWorkflowService(settingsService);

        await service.SaveGrabScheduledStartDefaultAsync(TimeSpan.FromDays(1));
        await service.SaveTomorrowScheduledStartDefaultAsync(TimeSpan.FromSeconds(-1));

        Assert.Equal(0, settingsService.SaveCalls);
        Assert.Equal(TimeSpan.Zero, settingsService.CurrentSettings.Tasks.Grab.DefaultScheduledStartTime);
        Assert.Equal(new TimeSpan(20, 0, 0), settingsService.CurrentSettings.Tasks.TomorrowReservation.DefaultScheduledStartTime);
    }

    [Fact]
    public async Task SettingsService_UpdateAsync_SerializesConcurrentPartialUpdates()
    {
        var repository = new SlowSettingsRepository(AppSettings.Default);
        var settingsService = new SettingsService(repository);
        var workflowService = new SettingsWorkflowService(settingsService);

        await Task.WhenAll(
            settingsService.UpdateAsync(settings => settings with
            {
                Venue = new VenueSelectionSettings(7, "自科")
            }),
            workflowService.SaveGrabScheduledStartDefaultAsync(new TimeSpan(7, 59, 55)));

        var settings = await settingsService.LoadAsync();
        Assert.Equal(7, settings.Venue.LastLibraryId);
        Assert.Equal("自科", settings.Venue.LastLibraryName);
        Assert.Equal(new TimeSpan(7, 59, 55), settings.Tasks.Grab.DefaultScheduledStartTime);
    }

    [Fact]
    public async Task ProtocolTemplateEditorService_DelegatesLoadSaveAndReset()
    {
        var templates = new TraceIntGraphQlTemplates("cookie", "libs", "layout", "rule", "reservation", "reserve", "cancel");
        var store = new FakeProtocolTemplateStore(templates);
        var service = new ProtocolTemplateEditorService(store);
        var overrides = new TraceIntGraphQlTemplateOverrides("a", "b", "c", "d", "e", "f", "g");

        var loaded = await service.LoadTemplatesAsync();
        await service.SaveOverridesAsync(overrides);
        await service.ResetOverridesAsync();

        Assert.Equal(templates, loaded);
        Assert.Equal(1, store.SaveCalls);
        Assert.Equal(overrides, store.LastOverrides);
        Assert.Equal(1, store.ResetCalls);
    }

    [Fact]
    public async Task NotificationTestService_DelegatesAndPropagatesFailures()
    {
        var alertService = new FakeTaskEventAlertDispatcher
        {
            SendTestLocalException = new InvalidOperationException("local failed")
        };
        var service = alertService;

        await service.SendTestEmailAsync(EmailAlertChannelSettings.Default);
        await service.SendTestTelegramAsync(TelegramAlertChannelSettings.Default);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SendTestLocalAlertAsync(LocalDesktopAlertSettings.Default));

        Assert.Equal("local failed", exception.Message);
        Assert.Single(alertService.TestEmailRequests);
        Assert.Single(alertService.TestTelegramRequests);
    }

    private sealed class SlowSettingsRepository(AppSettings settings) : ISettingsRepository
    {
        private AppSettings _settings = settings;

        public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
        {
            await Task.Delay(25, cancellationToken);
            return _settings;
        }

        public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            await Task.Delay(25, cancellationToken);
            _settings = settings;
        }
    }
}
