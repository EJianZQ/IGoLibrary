using System.Security.Cryptography;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Services;

public sealed class SettingsWorkflowService(ISettingsService settingsService) : ISettingsWorkflowService
{
    public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        return settingsService.LoadAsync(cancellationToken);
    }

    public async Task<AppSettings> SaveSystemSettingsAsync(
        SystemSettingsSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        return await settingsService.UpdateAsync(current => current with
        {
            Notifications = current.Notifications with
            {
                TaskEventAlerts = snapshot.TaskEventAlerts
            },
            Ui = current.Ui with
            {
                MinimizeToTray = snapshot.MinimizeToTray,
                LaunchOnStartup = snapshot.LaunchOnStartup,
                Theme = snapshot.Theme,
                HomeReservationProgress = HomeReservationProgressSettings.Normalize(snapshot.HomeReservationProgress),
                HomeCookieProgress = HomeCookieProgressSettings.Normalize(snapshot.HomeCookieProgress)
            },
            TraceIntProtocol = current.TraceIntProtocol with
            {
                GraphQlOverridesEnabled = snapshot.TraceIntGraphQlOverridesEnabled
            },
            Updates = (current.Updates ?? UpdateCheckSettings.Default) with
            {
                CheckOnStartup = snapshot.CheckUpdatesOnStartup
            },
            Network = new NetworkRequestSettings(
                Math.Max(3, snapshot.RequestTimeoutSeconds),
                Math.Max(0, snapshot.NetworkMaxRetries)),
            Tasks = current.Tasks with
            {
                Grab = current.Tasks.Grab with
                {
                    ReservationStrategy = snapshot.GrabReservationStrategy
                },
                AutoRelease = current.Tasks.AutoRelease with
                {
                    Enabled = snapshot.AutoReleaseEnabled,
                    LeadSeconds = AutoReleaseTaskSettings.NormalizeLeadSeconds(snapshot.AutoReleaseLeadSeconds)
                }
            }
        }, cancellationToken);
    }

    public async Task SaveNotificationSettingsAsync(
        TaskEventAlertSettings alerts,
        CancellationToken cancellationToken = default)
    {
        await settingsService.UpdateAsync(current => current with
        {
            Notifications = current.Notifications with
            {
                TaskEventAlerts = alerts
            }
        }, cancellationToken);
    }

    public async Task SaveGrabReservationStrategyAsync(
        GrabReservationStrategy strategy,
        CancellationToken cancellationToken = default)
    {
        await settingsService.UpdateAsync(current =>
        {
            if (current.Tasks.Grab.ReservationStrategy == strategy)
            {
                return current;
            }

            return current with
            {
                Tasks = current.Tasks with
                {
                    Grab = current.Tasks.Grab with
                    {
                        ReservationStrategy = strategy
                    }
                }
            };
        }, cancellationToken);
    }

    public async Task SaveGrabScheduledStartDefaultAsync(
        TimeSpan value,
        CancellationToken cancellationToken = default)
    {
        if (!IsTimeOfDay(value))
        {
            return;
        }

        await settingsService.UpdateAsync(current =>
        {
            if (current.Tasks.Grab.DefaultScheduledStartTime == value)
            {
                return current;
            }

            return current with
            {
                Tasks = current.Tasks with
                {
                    Grab = current.Tasks.Grab with
                    {
                        DefaultScheduledStartTime = value
                    }
                }
            };
        }, cancellationToken);
    }

    public async Task SaveTomorrowScheduledStartDefaultAsync(
        TimeSpan value,
        CancellationToken cancellationToken = default)
    {
        if (!IsTimeOfDay(value))
        {
            return;
        }

        await settingsService.UpdateAsync(current =>
        {
            if (current.Tasks.TomorrowReservation.DefaultScheduledStartTime == value)
            {
                return current;
            }

            return current with
            {
                Tasks = current.Tasks with
                {
                    TomorrowReservation = current.Tasks.TomorrowReservation with
                    {
                        DefaultScheduledStartTime = value
                    }
                }
            };
        }, cancellationToken);
    }

    public async Task SaveGlobalLeakSelectedLibrariesAsync(
        IReadOnlyList<GlobalLeakLibraryTarget> libraries,
        CancellationToken cancellationToken = default)
    {
        var selectedLibraries = libraries
            .Select(static library => new GlobalLeakLibrarySelectionSettings(
                library.LibraryId,
                library.LibraryName,
                library.Floor))
            .ToArray();

        await settingsService.UpdateAsync(current =>
        {
            if (AreGlobalLeakSelectionsEqual(current.Tasks.GlobalLeak.SelectedLibraries, selectedLibraries))
            {
                return current;
            }

            return current with
            {
                Tasks = current.Tasks with
                {
                    GlobalLeak = current.Tasks.GlobalLeak with
                    {
                        SelectedLibraries = selectedLibraries
                    }
                }
            };
        }, cancellationToken);
    }

    public async Task ClearStoredLibrarySelectionAsync(CancellationToken cancellationToken = default)
    {
        await settingsService.UpdateAsync(current =>
        {
            if (current.Venue.LastLibraryId is null && string.IsNullOrWhiteSpace(current.Venue.LastLibraryName))
            {
                return current;
            }

            return current with
            {
                Venue = VenueSelectionSettings.Default
            };
        }, cancellationToken);
    }

    public async Task SaveDashboardMetricsAsync(
        DashboardMetrics metrics,
        CancellationToken cancellationToken = default)
    {
        await settingsService.UpdateAsync(current =>
        {
            if (current.Dashboard == metrics)
            {
                return current;
            }

            return current with
            {
                Dashboard = metrics
            };
        }, cancellationToken);
    }

    public async Task<MobileControlSettings> EnsureMobileControlSettingsAsync(
        CancellationToken cancellationToken = default)
    {
        var settings = await settingsService.UpdateAsync(current =>
        {
            var mobileControl = NormalizeMobileControlSettings(current.MobileControl);
            if (mobileControl == current.MobileControl)
            {
                return current;
            }

            return current with
            {
                MobileControl = mobileControl
            };
        }, cancellationToken);

        return settings.MobileControl;
    }

    public async Task<MobileControlSettings> SaveMobileControlPortAsync(
        int port,
        CancellationToken cancellationToken = default)
    {
        if (!MobileControlSettings.IsValidPort(port))
        {
            throw new ArgumentOutOfRangeException(
                nameof(port),
                $"手机控制端口必须介于 {MobileControlSettings.MinPort} 和 {MobileControlSettings.MaxPort} 之间");
        }

        var settings = await settingsService.UpdateAsync(current =>
        {
            var mobileControl = NormalizeMobileControlSettings(current.MobileControl) with
            {
                Port = port
            };
            if (mobileControl == current.MobileControl)
            {
                return current;
            }

            return current with
            {
                MobileControl = mobileControl
            };
        }, cancellationToken);

        return settings.MobileControl;
    }

    public async Task<MobileControlSettings> SaveMobileControlAccessTokenAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new ArgumentException("手机控制访问令牌不能为空", nameof(accessToken));
        }

        var normalizedToken = accessToken.Trim();
        var settings = await settingsService.UpdateAsync(current =>
        {
            var mobileControl = NormalizeMobileControlSettings(current.MobileControl) with
            {
                AccessToken = normalizedToken
            };
            if (mobileControl == current.MobileControl)
            {
                return current;
            }

            return current with
            {
                MobileControl = mobileControl
            };
        }, cancellationToken);

        return settings.MobileControl;
    }

    private static bool IsTimeOfDay(TimeSpan value)
    {
        return value >= TimeSpan.Zero && value < TimeSpan.FromDays(1);
    }

    private static MobileControlSettings NormalizeMobileControlSettings(MobileControlSettings? settings)
    {
        var current = settings ?? MobileControlSettings.Default;
        return new MobileControlSettings(
            MobileControlSettings.IsValidPort(current.Port)
                ? current.Port
                : CreateRandomMobileControlPort(),
            string.IsNullOrWhiteSpace(current.AccessToken)
                ? CreateMobileControlAccessToken()
                : current.AccessToken.Trim());
    }

    public static int CreateRandomMobileControlPort()
    {
        return RandomNumberGenerator.GetInt32(
            MobileControlSettings.RandomPortMinInclusive,
            MobileControlSettings.RandomPortMaxExclusive);
    }

    public static string CreateMobileControlAccessToken()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
    }

    private static bool AreGlobalLeakSelectionsEqual(
        IReadOnlyList<GlobalLeakLibrarySelectionSettings> current,
        IReadOnlyList<GlobalLeakLibrarySelectionSettings> updated)
    {
        if (current.Count != updated.Count)
        {
            return false;
        }

        for (var index = 0; index < current.Count; index++)
        {
            if (current[index] != updated[index])
            {
                return false;
            }
        }

        return true;
    }
}
