using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.State;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Services;

public sealed class HealthCheckService(
    ISessionState sessionState,
    IVenueState venueState,
    IReservationState reservationState,
    ISettingsService settingsService,
    ITraceIntApiClient apiClient,
    IProtocolTemplateStore protocolTemplateStore,
    IGrabSeatCoordinator grabSeatCoordinator,
    IVenueAvailabilityCoordinator venueAvailabilityCoordinator,
    IOccupySeatCoordinator occupySeatCoordinator,
    ITomorrowReservationCoordinator tomorrowReservationCoordinator,
    ICheckInGuardCoordinator checkInGuardCoordinator) : IHealthCheckService
{
    public async Task<SystemHealthSnapshot> BuildSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var generatedAt = DateTimeOffset.Now;
        var settings = await settingsService.LoadAsync(cancellationToken);
        var templates = await protocolTemplateStore.GetEffectiveTemplatesAsync(cancellationToken);
        var items = new List<HealthCheckItem>();

        items.Add(BuildSessionPresenceItem());
        items.Add(BuildVenueItem());
        items.Add(BuildReservationItem());
        items.Add(BuildNotificationItem(settings));
        items.Add(BuildProtocolSummaryItem(templates));
        items.Add(BuildNetworkItem(settings.Network));
        items.Add(BuildTaskItem("task.grab", grabSeatCoordinator.GetStatus()));
        items.Add(BuildTaskItem("task.venue-availability", venueAvailabilityCoordinator.GetStatus()));
        items.Add(BuildTaskItem("task.tomorrow", tomorrowReservationCoordinator.GetStatus()));
        items.Add(BuildTaskItem("task.occupy", occupySeatCoordinator.GetStatus()));
        items.Add(BuildTaskItem("task.checkin", checkInGuardCoordinator.GetStatus()));

        return new SystemHealthSnapshot(generatedAt, items);
    }

    public async Task<PreflightResult> RunPreflightAsync(
        PreflightTarget target,
        CancellationToken cancellationToken = default)
    {
        var checkedAt = DateTimeOffset.Now;
        var settings = await settingsService.LoadAsync(cancellationToken);
        var templates = await protocolTemplateStore.GetEffectiveTemplatesAsync(cancellationToken);
        var items = new List<HealthCheckItem>();

        items.Add(await BuildCookiePreflightItemAsync(cancellationToken));
        items.Add(BuildVenuePreflightItem());
        AddSeatSelectionPreflightItem(target, items);
        AddReservationPreflightItem(target, items);
        items.Add(BuildNotificationItem(settings));
        AddProtocolPreflightItems(target, templates, items);
        AddTaskConflictItems(target, items);

        return new PreflightResult(target, checkedAt, items);
    }

    private HealthCheckItem BuildSessionPresenceItem()
    {
        return sessionState.Session is null
            ? Blocking("cookie", "Cookie 状态", "当前未登录，请先完成授权。")
            : Info("cookie", "Cookie 状态", "当前已有本地会话，启动任务前会再次校验 Cookie。");
    }

    private async Task<HealthCheckItem> BuildCookiePreflightItemAsync(CancellationToken cancellationToken)
    {
        var session = sessionState.Session;
        if (session is null)
        {
            return Blocking("cookie", "Cookie 状态", "当前未登录，请先完成授权。");
        }

        try
        {
            await apiClient.ValidateCookieAsync(session.Cookie, cancellationToken);
            return Info("cookie", "Cookie 状态", "Cookie 已通过真实接口校验。");
        }
        catch (Exception ex) when (SessionAuthFailureDetector.IsSessionInvalidException(ex, session.Cookie, DateTimeOffset.Now))
        {
            return Blocking("cookie", "Cookie 状态", $"Cookie 已失效：{ex.Message}");
        }
        catch (Exception ex)
        {
            return Warning("cookie", "Cookie 状态", $"Cookie 校验暂时失败：{ex.Message}");
        }
    }

    private HealthCheckItem BuildVenueItem()
    {
        var venue = venueState.BoundLibrary;
        if (venue is null)
        {
            return Blocking("venue", "锁定场馆", "尚未锁定场馆。");
        }

        var openText = venue.IsOpen ? "开放中" : "当前未开放";
        return Info("venue", "锁定场馆", $"{venue.Name} · {venue.Floor} · {openText}");
    }

    private HealthCheckItem BuildVenuePreflightItem()
    {
        return venueState.BoundLibrary is null
            ? Blocking("venue", "锁定场馆", "请先锁定当前作业场馆。")
            : BuildVenueItem();
    }

    private HealthCheckItem BuildReservationItem()
    {
        var reservation = reservationState.CurrentReservation;
        return reservation is null
            ? Warning("reservation", "当前预约", "当前没有检测到预约。")
            : Info(
                "reservation",
                "当前预约",
                $"{reservation.LibraryName} · {reservation.SeatName} · 到期 {reservation.ExpirationTime:HH:mm:ss}");
    }

    private static void AddSeatSelectionPreflightItem(PreflightTarget target, ICollection<HealthCheckItem> items)
    {
        if (target.Kind is not (PreflightTaskKind.Grab or PreflightTaskKind.TomorrowReservation))
        {
            return;
        }

        if (target.SelectedSeats.Count == 0)
        {
            items.Add(Blocking("seat-selection", "目标座位", "请先选择目标座位。"));
            return;
        }

        items.Add(Info("seat-selection", "目标座位", $"已选择 {target.SelectedSeats.Count} 个目标座位。"));
    }

    private void AddReservationPreflightItem(PreflightTarget target, ICollection<HealthCheckItem> items)
    {
        if (target.Kind is not (PreflightTaskKind.Occupy or PreflightTaskKind.CheckInGuard))
        {
            return;
        }

        var reservation = reservationState.CurrentReservation;
        items.Add(reservation is null
            ? Blocking("reservation", "当前预约", "请先刷新并确认当前预约。")
            : Info("reservation", "当前预约", $"{reservation.LibraryName} · {reservation.SeatName}"));
    }

    private static HealthCheckItem BuildNotificationItem(AppSettings settings)
    {
        var alerts = settings.Notifications.TaskEventAlerts ?? TaskEventAlertSettings.Default;
        var usableChannels = new List<string>();
        var incompleteChannels = new List<string>();

        if (settings.Notifications.AppBannerNotificationsEnabled || alerts.Local.PopupEnabled || alerts.Local.SoundEnabled)
        {
            usableChannels.Add("本地弹窗");
        }

        AddChannelState(
            alerts.Email.Enabled,
            IsEmailConfigured(alerts.Email),
            "邮件",
            usableChannels,
            incompleteChannels);
        AddChannelState(
            alerts.Telegram.Enabled,
            IsTelegramConfigured(alerts.Telegram),
            "Telegram",
            usableChannels,
            incompleteChannels);
        AddChannelState(
            alerts.Bark.Enabled,
            IsBarkConfigured(alerts.Bark),
            "Bark",
            usableChannels,
            incompleteChannels);

        if (incompleteChannels.Count > 0)
        {
            return Warning(
                "notification",
                "通知渠道",
                $"{string.Join("、", incompleteChannels)} 已开启但配置不完整。");
        }

        return usableChannels.Count == 0
            ? Warning("notification", "通知渠道", "当前没有可用通知渠道，任务可启动但可能错过提醒。")
            : Info("notification", "通知渠道", $"可用渠道：{string.Join("、", usableChannels)}。");
    }

    private static void AddProtocolPreflightItems(
        PreflightTarget target,
        TraceIntGraphQlTemplates templates,
        ICollection<HealthCheckItem> items)
    {
        var missing = target.Kind switch
        {
            PreflightTaskKind.Grab => MissingTemplates(templates, [
                (templates.QueryLibraryLayoutTemplate, "场馆座位模板"),
                (templates.QueryReservationInfoTemplate, "预约查询模板"),
                (templates.ReserveSeatTemplate, "预约提交模板")
            ]),
            PreflightTaskKind.TomorrowReservation => MissingTemplates(templates, [
                (templates.QueryLibraryLayoutTemplate, "场馆座位模板"),
                (templates.TomorrowReservationWarmUpTemplate, "明日预约预热模板"),
                (templates.TomorrowReservationSaveTemplate, "明日预约保存模板"),
                (templates.TomorrowReservationInfoTemplate, "明日预约验证模板")
            ]),
            PreflightTaskKind.Occupy => MissingTemplates(templates, [
                (templates.QueryReservationInfoTemplate, "预约查询模板"),
                (templates.CancelReservationTemplate, "退座模板"),
                (templates.ReserveSeatTemplate, "预约提交模板")
            ]),
            PreflightTaskKind.CheckInGuard => MissingTemplates(templates, [
                (templates.QueryReservationInfoTemplate, "预约查询模板"),
                (templates.CancelReservationTemplate, "退座模板"),
                (templates.QueryLibraryLayoutTemplate, "场馆座位模板"),
                (templates.ReserveSeatTemplate, "预约提交模板")
            ]),
            _ => []
        };

        items.Add(missing.Count == 0
            ? Info("protocol-template", "协议模板", "当前任务所需协议模板已配置。")
            : Blocking("protocol-template", "协议模板", $"缺少：{string.Join("、", missing)}。"));
    }

    private static HealthCheckItem BuildProtocolSummaryItem(TraceIntGraphQlTemplates templates)
    {
        var missing = MissingTemplates(templates, [
            (templates.GetCookieUrlTemplate, "Cookie 获取模板"),
            (templates.QueryLibrariesTemplate, "场馆列表模板"),
            (templates.QueryLibraryLayoutTemplate, "场馆座位模板"),
            (templates.QueryReservationInfoTemplate, "预约查询模板"),
            (templates.ReserveSeatTemplate, "预约提交模板"),
            (templates.CancelReservationTemplate, "退座模板")
        ]);

        return missing.Count == 0
            ? Info("protocol-template", "协议模板", "核心协议模板已配置。")
            : Blocking("protocol-template", "协议模板", $"缺少：{string.Join("、", missing)}。");
    }

    private static HealthCheckItem BuildNetworkItem(NetworkRequestSettings settings)
    {
        return Info(
            "network",
            "网络配置",
            $"超时 {settings.TimeoutSeconds} 秒，最多重试 {settings.MaxRetries} 次。");
    }

    private void AddTaskConflictItems(PreflightTarget target, ICollection<HealthCheckItem> items)
    {
        var statuses = new[]
        {
            (Kind: PreflightTaskKind.Grab, Status: grabSeatCoordinator.GetStatus()),
            (Kind: PreflightTaskKind.TomorrowReservation, Status: tomorrowReservationCoordinator.GetStatus()),
            (Kind: PreflightTaskKind.Occupy, Status: occupySeatCoordinator.GetStatus()),
            (Kind: PreflightTaskKind.CheckInGuard, Status: checkInGuardCoordinator.GetStatus())
        };

        foreach (var (kind, status) in statuses)
        {
            if (!IsActive(status.State))
            {
                continue;
            }

            items.Add(kind == target.Kind
                ? Blocking("task-conflict", "任务冲突", $"{status.Title} 已在运行，请先停止后再启动。")
                : Warning("task-conflict", "任务冲突", $"{status.Title} 正在运行，请确认任务之间不会互相影响。"));
        }
    }

    private static HealthCheckItem BuildTaskItem(string key, CoordinatorStatus status)
    {
        var severity = status.State == CoordinatorTaskState.Failed
            ? HealthSeverity.Warning
            : HealthSeverity.Info;
        return new HealthCheckItem(key, status.Title, status.Message, severity);
    }

    private static bool IsActive(CoordinatorTaskState state)
    {
        return state is CoordinatorTaskState.Starting or CoordinatorTaskState.Running or CoordinatorTaskState.Stopping;
    }

    private static List<string> MissingTemplates(
        TraceIntGraphQlTemplates _,
        IEnumerable<(string Value, string Label)> candidates)
    {
        return candidates
            .Where(static candidate => string.IsNullOrWhiteSpace(candidate.Value))
            .Select(static candidate => candidate.Label)
            .ToList();
    }

    private static void AddChannelState(
        bool enabled,
        bool configured,
        string label,
        ICollection<string> usableChannels,
        ICollection<string> incompleteChannels)
    {
        if (!enabled)
        {
            return;
        }

        if (configured)
        {
            usableChannels.Add(label);
        }
        else
        {
            incompleteChannels.Add(label);
        }
    }

    private static bool IsEmailConfigured(EmailAlertChannelSettings settings)
    {
        return !string.IsNullOrWhiteSpace(settings.SmtpHost) &&
               settings.Port > 0 &&
               !string.IsNullOrWhiteSpace(settings.Username) &&
               !string.IsNullOrWhiteSpace(settings.Password) &&
               !string.IsNullOrWhiteSpace(settings.FromAddress) &&
               !string.IsNullOrWhiteSpace(settings.ToAddress);
    }

    private static bool IsTelegramConfigured(TelegramAlertChannelSettings settings)
    {
        return !string.IsNullOrWhiteSpace(settings.ApiBaseUrl) &&
               !string.IsNullOrWhiteSpace(settings.BotToken) &&
               !string.IsNullOrWhiteSpace(settings.ChatId);
    }

    private static bool IsBarkConfigured(BarkAlertChannelSettings settings)
    {
        return !string.IsNullOrWhiteSpace(settings.ServerUrl) &&
               !string.IsNullOrWhiteSpace(settings.DeviceKey);
    }

    private static HealthCheckItem Info(string key, string title, string message) =>
        new(key, title, message, HealthSeverity.Info);

    private static HealthCheckItem Warning(string key, string title, string message) =>
        new(key, title, message, HealthSeverity.Warning);

    private static HealthCheckItem Blocking(string key, string title, string message) =>
        new(key, title, message, HealthSeverity.Blocking);
}
