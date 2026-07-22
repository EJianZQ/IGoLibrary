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
                PreventSystemSleepWhileTasksActive = snapshot.PreventSystemSleepWhileTasksActive,
                LaunchOnStartup = snapshot.LaunchOnStartup,
                MainViewSize = MainViewSizePreferences.Normalize(current.Ui.MainViewSize) with
                {
                    RememberSize = snapshot.RememberMainViewSize
                },
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
                    ReservationStrategy = snapshot.GrabReservationStrategy,
                    OptimalStrategyReminderEnabled = snapshot.OptimalGrabStrategyReminderEnabled
                },
                AutoRelease = current.Tasks.AutoRelease with
                {
                    Enabled = snapshot.AutoReleaseEnabled,
                    LeadSeconds = AutoReleaseTaskSettings.NormalizeLeadSeconds(snapshot.AutoReleaseLeadSeconds)
                }
            }
        }, cancellationToken);
    }

    public async Task SaveMainViewSizeAsync(
        double clientWidth,
        double clientHeight,
        CancellationToken cancellationToken = default)
    {
        if (!MainViewSizePreferences.TryNormalizeSize(
                clientWidth,
                clientHeight,
                out var normalizedWidth,
                out var normalizedHeight))
        {
            throw new ArgumentOutOfRangeException(
                nameof(clientWidth),
                "窗口宽高必须是大于零的有限数值");
        }

        await settingsService.UpdateAsync(current =>
        {
            var mainViewSize = MainViewSizePreferences.Normalize(current.Ui.MainViewSize) with
            {
                ClientWidth = normalizedWidth,
                ClientHeight = normalizedHeight
            };
            if (mainViewSize == current.Ui.MainViewSize)
            {
                return current;
            }

            return current with
            {
                Ui = current.Ui with
                {
                    MainViewSize = mainViewSize
                }
            };
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

    public async Task SaveGrabStartPreferencesAsync(
        GrabReservationStrategy strategy,
        bool disableOptimalStrategyReminder,
        CancellationToken cancellationToken = default)
    {
        await settingsService.UpdateAsync(current =>
        {
            var reminderEnabled = disableOptimalStrategyReminder
                ? false
                : current.Tasks.Grab.OptimalStrategyReminderEnabled;
            if (current.Tasks.Grab.ReservationStrategy == strategy &&
                current.Tasks.Grab.OptimalStrategyReminderEnabled == reminderEnabled)
            {
                return current;
            }

            return current with
            {
                Tasks = current.Tasks with
                {
                    Grab = current.Tasks.Grab with
                    {
                        ReservationStrategy = strategy,
                        OptimalStrategyReminderEnabled = reminderEnabled
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
            .DistinctBy(static library => library.LibraryId)
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

    public async Task<MobileControlSettings> SaveMobileControlAutoStartAsync(
        bool autoStart,
        CancellationToken cancellationToken = default)
    {
        var settings = await settingsService.UpdateAsync(current =>
        {
            var mobileControl = NormalizeMobileControlSettings(current.MobileControl) with
            {
                AutoStart = autoStart
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

    public async Task<MobileControlSettings> SaveMobileControlNetworkModeAsync(
        MobileControlNetworkMode networkMode,
        CancellationToken cancellationToken = default)
    {
        var normalizedMode = MobileControlSettings.NormalizeNetworkMode(networkMode);
        var settings = await settingsService.UpdateAsync(current =>
        {
            var mobileControl = NormalizeMobileControlSettings(current.MobileControl) with
            {
                NetworkMode = normalizedMode
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

    public async Task<MobileControlSettings> SaveCloudflareTunnelProxyAsync(
        CloudflareTunnelProxyMode proxyMode,
        string manualProxyUrl,
        CancellationToken cancellationToken = default)
    {
        var normalizedMode = MobileControlSettings.NormalizeTunnelProxyMode(proxyMode);
        var hasValidManualUrl = MobileControlSettings.TryNormalizeManualProxyUrl(
            manualProxyUrl,
            out var normalizedManualUrl);
        if (normalizedMode == CloudflareTunnelProxyMode.ManualHttpProxy && !hasValidManualUrl)
        {
            throw new ArgumentException(
                "手动代理地址必须是无用户名、密码、路径或查询参数的 HTTP 地址，例如 http://127.0.0.1:7897",
                nameof(manualProxyUrl));
        }

        var settings = await settingsService.UpdateAsync(current =>
        {
            var mobileControl = NormalizeMobileControlSettings(current.MobileControl) with
            {
                TunnelProxyMode = normalizedMode,
                TunnelManualProxyUrl = hasValidManualUrl ? normalizedManualUrl : string.Empty
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

    public async Task<MobileControlSettings> SaveCloudflareTunnelFallbackAsync(
        bool fallbackToLocalNetworkOnTunnelFailure,
        CancellationToken cancellationToken = default)
    {
        var settings = await settingsService.UpdateAsync(current =>
        {
            var mobileControl = NormalizeMobileControlSettings(current.MobileControl) with
            {
                FallbackToLocalNetworkOnTunnelFailure = fallbackToLocalNetworkOnTunnelFailure
            };
            return mobileControl == current.MobileControl
                ? current
                : current with { MobileControl = mobileControl };
        }, cancellationToken);

        return settings.MobileControl;
    }

    public async Task<MobileControlSettings> SaveClashMihomoCompatibilityAsync(
        bool enabled,
        string configPath,
        string routePolicy,
        CancellationToken cancellationToken = default)
    {
        if (!MobileControlSettings.TryNormalizeClashMihomoConfigPath(configPath, out var normalizedConfigPath))
        {
            throw new ArgumentException("Mihomo 活动配置必须是绝对路径的 .yaml 或 .yml 文件", nameof(configPath));
        }

        if (!MobileControlSettings.TryNormalizeClashMihomoRoutePolicy(routePolicy, out var normalizedRoutePolicy))
        {
            throw new ArgumentException("Mihomo 路由策略不能为空、不能包含逗号或 #，且最多 128 个字符", nameof(routePolicy));
        }

        var settings = await settingsService.UpdateAsync(current =>
        {
            var mobileControl = NormalizeMobileControlSettings(current.MobileControl) with
            {
                ClashMihomoCompatibilityEnabled = enabled,
                ClashMihomoConfigPath = normalizedConfigPath,
                ClashMihomoRoutePolicy = normalizedRoutePolicy
            };
            return mobileControl == current.MobileControl
                ? current
                : current with { MobileControl = mobileControl };
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
        var proxyMode = MobileControlSettings.NormalizeTunnelProxyMode(current.TunnelProxyMode);
        var hasValidManualUrl = MobileControlSettings.TryNormalizeManualProxyUrl(
            current.TunnelManualProxyUrl,
            out var normalizedManualUrl);
        if (proxyMode == CloudflareTunnelProxyMode.ManualHttpProxy && !hasValidManualUrl)
        {
            proxyMode = CloudflareTunnelProxyMode.Auto;
        }

        var hasValidClashConfigPath = MobileControlSettings.TryNormalizeClashMihomoConfigPath(
            current.ClashMihomoConfigPath,
            out var normalizedClashConfigPath);
        var hasValidClashRoutePolicy = MobileControlSettings.TryNormalizeClashMihomoRoutePolicy(
            current.ClashMihomoRoutePolicy,
            out var normalizedClashRoutePolicy);

        return new MobileControlSettings(
            MobileControlSettings.IsValidPort(current.Port)
                ? current.Port
                : CreateRandomMobileControlPort(),
            string.IsNullOrWhiteSpace(current.AccessToken)
                ? CreateMobileControlAccessToken()
                : current.AccessToken.Trim(),
            current.AutoStart,
            MobileControlSettings.NormalizeNetworkMode(current.NetworkMode),
            proxyMode,
            hasValidManualUrl ? normalizedManualUrl : string.Empty,
            current.ClashMihomoCompatibilityEnabled,
            hasValidClashConfigPath ? normalizedClashConfigPath : string.Empty,
            hasValidClashRoutePolicy
                ? normalizedClashRoutePolicy
                : MobileControlSettings.DefaultClashMihomoRoutePolicy,
            current.FallbackToLocalNetworkOnTunnelFailure);
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
