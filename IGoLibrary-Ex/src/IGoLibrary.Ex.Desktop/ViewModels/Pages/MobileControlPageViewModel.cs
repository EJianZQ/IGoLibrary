using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Configuration;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Domain.Enums;

namespace IGoLibrary.Ex.Desktop.ViewModels;

public sealed partial class MobileControlPageViewModel : ViewModelBase
{
    private readonly IMobileControlService mobileControlService;
    private readonly ISettingsWorkflowService settingsWorkflowService;
    private readonly IQrCodeImageFactory qrCodeImageFactory;
    private readonly IActivityLogService activityLogService;
    private readonly INotificationService notificationService;
    private string _accessToken = string.Empty;
    private bool _isApplyingSettings;
    private bool _lastPersistedAutoStart;
    private bool _isStartingAutomatically;

    public MobileControlPageViewModel(
        IMobileControlService mobileControlService,
        ISettingsWorkflowService settingsWorkflowService,
        IQrCodeImageFactory qrCodeImageFactory,
        IActivityLogService activityLogService,
        INotificationService notificationService)
    {
        this.mobileControlService = mobileControlService;
        this.settingsWorkflowService = settingsWorkflowService;
        this.qrCodeImageFactory = qrCodeImageFactory;
        this.activityLogService = activityLogService;
        this.notificationService = notificationService;

        MobileControlConnectedDeviceCount = mobileControlService.ConnectedDeviceCount;
        mobileControlService.DeviceCountChanged += OnMobileControlDeviceCountChanged;
        mobileControlService.EndpointChanged += OnMobileControlEndpointChanged;
    }

    [ObservableProperty]
    private bool isMobileControlRunning;

    [ObservableProperty]
    private bool isMobileControlStarting;

    [ObservableProperty]
    private bool isMobileControlAutoStartEnabled;

    [ObservableProperty]
    private int mobileControlPort;

    [ObservableProperty]
    private string mobileControlStatusText = "未启动";

    [ObservableProperty]
    private string mobileControlUrlText = "启动后生成访问地址";

    [ObservableProperty]
    private string mobileControlHostText = "未监听";

    [ObservableProperty]
    private string mobileControlAccessTokenText = "未生成";

    [ObservableProperty]
    private IImage? mobileControlQrCode;

    [ObservableProperty]
    private bool isMobileControlDetailsOpen;

    [ObservableProperty]
    private int mobileControlConnectedDeviceCount;

    public bool IsMobileControlStopped => !IsMobileControlRunning;

    public string MobileControlToggleButtonText => IsMobileControlStarting
        ? "正在启动中"
        : IsMobileControlRunning
            ? "停用手机控制"
            : "启用手机控制";

    public string MobileControlAccessTokenFullText => string.IsNullOrWhiteSpace(_accessToken) ? "未生成" : _accessToken;

    public string MobileControlConnectedDeviceCountText => $"当前有 {MobileControlConnectedDeviceCount} 台设备连接";

    public bool HasMobileControlQrCode => MobileControlQrCode is not null;

    public bool HasNoMobileControlQrCode => !HasMobileControlQrCode;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var settings = await settingsWorkflowService.EnsureMobileControlSettingsAsync(cancellationToken);
        ApplySettings(settings);
    }

    public void ApplySettings(MobileControlSettings settings)
    {
        _isApplyingSettings = true;
        try
        {
            MobileControlPort = settings.Port;
            _accessToken = settings.AccessToken;
            IsMobileControlAutoStartEnabled = settings.AutoStart;
            _lastPersistedAutoStart = settings.AutoStart;
            MobileControlAccessTokenText = MaskAccessToken(_accessToken);
            if (!IsMobileControlRunning)
            {
                MobileControlStatusText = $"已停止 · {GetNetworkModeText(settings.NetworkMode)}";
            }
            OnPropertyChanged(nameof(MobileControlAccessTokenFullText));
        }
        finally
        {
            _isApplyingSettings = false;
        }
    }

    public async Task StartAutomaticallyIfEnabledAsync(CancellationToken cancellationToken = default)
    {
        if (!IsMobileControlAutoStartEnabled ||
            IsMobileControlRunning ||
            _isStartingAutomatically)
        {
            return;
        }

        _isStartingAutomatically = true;
        try
        {
            await StartMobileControlCoreAsync(
                showNotification: false,
                failureNotificationTitle: "自动开启手机控制失败",
                cancellationToken: cancellationToken,
                requireAutoStartEnabled: true);
        }
        finally
        {
            _isStartingAutomatically = false;
        }
    }

    [RelayCommand]
    private async Task ToggleMobileControlAsync()
    {
        if (IsMobileControlRunning)
        {
            await StopMobileControlCoreAsync(showNotification: true);
            return;
        }

        await StartMobileControlCoreAsync(showNotification: true);
    }

    [RelayCommand]
    private async Task StartMobileControlAsync()
    {
        if (IsMobileControlRunning)
        {
            return;
        }

        await StartMobileControlCoreAsync(showNotification: true);
    }

    [RelayCommand]
    private async Task StopMobileControlAsync()
    {
        if (!IsMobileControlRunning)
        {
            return;
        }

        await StopMobileControlCoreAsync(showNotification: true);
    }

    [RelayCommand]
    private async Task RandomizeMobileControlPortAsync()
    {
        var wasRunning = IsMobileControlRunning;
        if (wasRunning)
        {
            await StopMobileControlCoreAsync(showNotification: false);
        }

        var newPort = CreateDifferentPort(MobileControlPort);
        var settings = await settingsWorkflowService.SaveMobileControlPortAsync(newPort);
        ApplySettings(settings);
        activityLogService.Write(LogEntryKind.Info, "MobileControl", $"手机控制端口已更新为 {newPort}");

        if (wasRunning)
        {
            await StartMobileControlCoreAsync(showNotification: false);
        }
    }

    [RelayCommand]
    private async Task ResetMobileControlAccessTokenAsync()
    {
        var wasRunning = IsMobileControlRunning;
        if (wasRunning)
        {
            await StopMobileControlCoreAsync(showNotification: false);
        }

        var settings = await settingsWorkflowService.SaveMobileControlAccessTokenAsync(
            SettingsWorkflowService.CreateMobileControlAccessToken());
        ApplySettings(settings);
        activityLogService.Write(LogEntryKind.Info, "MobileControl", "手机控制访问令牌已重置");

        if (wasRunning)
        {
            await StartMobileControlCoreAsync(showNotification: false);
        }
    }

    [RelayCommand]
    private void OpenMobileControlDetails()
    {
        IsMobileControlDetailsOpen = true;
    }

    [RelayCommand]
    private void CloseMobileControlDetails()
    {
        IsMobileControlDetailsOpen = false;
    }

    private async Task StartMobileControlCoreAsync(
        bool showNotification,
        string failureNotificationTitle = "手机控制重启失败",
        CancellationToken cancellationToken = default,
        bool requireAutoStartEnabled = false)
    {
        if (IsMobileControlStarting)
        {
            return;
        }

        IsMobileControlStarting = true;
        try
        {
            var settings = await settingsWorkflowService.EnsureMobileControlSettingsAsync(cancellationToken);
            ApplySettings(settings);
            var session = await mobileControlService.StartAsync(settings, cancellationToken);
            if (requireAutoStartEnabled && !IsMobileControlAutoStartEnabled)
            {
                await mobileControlService.StopAsync(cancellationToken: cancellationToken);
                ApplyStopped("已停止");
                activityLogService.Write(LogEntryKind.Info, "MobileControl", "自动开启手机控制已取消：自动开启已关闭");
                return;
            }

            ApplyStartedSession(session);
            activityLogService.Write(LogEntryKind.Success, "MobileControl", $"手机控制已启动：{session.Host}:{session.Port}");
            if (showNotification)
            {
                await notificationService.ShowSuccessAsync(
                    "手机控制已启动",
                    session.EffectiveMode == MobileControlNetworkMode.CloudflareTunnel
                        ? "请用手机扫码访问公网控制页面"
                        : "请用手机扫码访问局域网页面");
            }
        }
        catch (Exception ex)
        {
            ApplyStopped("启动失败");
            activityLogService.Write(LogEntryKind.Error, "MobileControl", $"启动手机控制失败：{ex.Message}");
            if (showNotification)
            {
                await notificationService.ShowWarningAsync("启动手机控制失败", ex.Message);
            }
            else if (!string.IsNullOrWhiteSpace(failureNotificationTitle))
            {
                await notificationService.ShowWarningAsync(failureNotificationTitle, ex.Message);
            }
        }
        finally
        {
            IsMobileControlStarting = false;
        }
    }

    private async Task StopMobileControlCoreAsync(bool showNotification)
    {
        try
        {
            await mobileControlService.StopAsync();
            ApplyStopped("已停止");
            activityLogService.Write(LogEntryKind.Info, "MobileControl", "手机控制已停止");
            if (showNotification)
            {
                await notificationService.ShowInfoAsync("手机控制已停止", "手机控制页面已关闭");
            }
        }
        catch (Exception ex)
        {
            activityLogService.Write(LogEntryKind.Error, "MobileControl", $"停止手机控制失败：{ex.Message}");
            if (showNotification)
            {
                await notificationService.ShowWarningAsync("停止手机控制失败", ex.Message);
            }
        }
    }

    private void ApplyStartedSession(MobileControlSession session)
    {
        IsMobileControlRunning = true;
        MobileControlStatusText = $"运行中 · {GetNetworkModeText(session.EffectiveMode)}";
        MobileControlConnectedDeviceCount = mobileControlService.ConnectedDeviceCount;
        MobileControlHostText = session.EffectiveMode == MobileControlNetworkMode.CloudflareTunnel
            ? session.Url.Authority
            : $"{session.Host}:{session.Port}";
        MobileControlUrlText = session.Url.ToString();
        MobileControlQrCode = qrCodeImageFactory.Create(MobileControlUrlText);
        OnPropertyChanged(nameof(HasMobileControlQrCode));
        OnPropertyChanged(nameof(HasNoMobileControlQrCode));
    }

    private void ApplyStopped(string statusText)
    {
        IsMobileControlRunning = false;
        MobileControlStatusText = statusText;
        MobileControlConnectedDeviceCount = 0;
        MobileControlHostText = "未监听";
        MobileControlUrlText = "启动后生成访问地址";
        MobileControlQrCode = null;
        OnPropertyChanged(nameof(HasMobileControlQrCode));
        OnPropertyChanged(nameof(HasNoMobileControlQrCode));
    }

    partial void OnIsMobileControlRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(IsMobileControlStopped));
        OnPropertyChanged(nameof(MobileControlToggleButtonText));
    }

    partial void OnIsMobileControlStartingChanged(bool value)
    {
        OnPropertyChanged(nameof(MobileControlToggleButtonText));
    }

    partial void OnIsMobileControlAutoStartEnabledChanged(bool value)
    {
        if (_isApplyingSettings)
        {
            return;
        }

        _ = PersistMobileControlAutoStartAsync(value);
    }

    partial void OnMobileControlConnectedDeviceCountChanged(int value)
    {
        OnPropertyChanged(nameof(MobileControlConnectedDeviceCountText));
    }

    private void OnMobileControlDeviceCountChanged(object? sender, MobileControlDeviceCountChangedEventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            MobileControlConnectedDeviceCount = e.ConnectedDeviceCount;
            return;
        }

        Dispatcher.UIThread.Post(() => MobileControlConnectedDeviceCount = e.ConnectedDeviceCount);
    }

    private void OnMobileControlEndpointChanged(object? sender, MobileControlEndpointChangedEventArgs e)
    {
        if (!IsMobileControlRunning)
        {
            return;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            ApplyStartedSession(e.Session);
            return;
        }

        Dispatcher.UIThread.Post(() => ApplyStartedSession(e.Session));
    }

    private async Task PersistMobileControlAutoStartAsync(bool enabled)
    {
        try
        {
            var settings = await settingsWorkflowService.SaveMobileControlAutoStartAsync(enabled);
            ApplySettings(settings);
            activityLogService.Write(
                LogEntryKind.Info,
                "MobileControl",
                enabled ? "手机控制自动开启已启用" : "手机控制自动开启已关闭");

            if (settings.AutoStart)
            {
                await StartAutomaticallyIfEnabledAsync();
            }
        }
        catch (Exception ex)
        {
            activityLogService.Write(LogEntryKind.Warning, "MobileControl", $"保存手机控制自动开启设置失败：{ex.Message}");
            _isApplyingSettings = true;
            try
            {
                IsMobileControlAutoStartEnabled = _lastPersistedAutoStart;
            }
            finally
            {
                _isApplyingSettings = false;
            }

            await notificationService.ShowWarningAsync("保存自动开启设置失败", ex.Message);
        }
    }

    private static int CreateDifferentPort(int currentPort)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var port = SettingsWorkflowService.CreateRandomMobileControlPort();
            if (port != currentPort)
            {
                return port;
            }
        }

        return SettingsWorkflowService.CreateRandomMobileControlPort();
    }

    private static string MaskAccessToken(string accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return "未生成";
        }

        var token = accessToken.Trim();
        return token.Length <= 8 ? token : $"{token[..8]}...";
    }

    private static string GetNetworkModeText(MobileControlNetworkMode mode)
    {
        return mode == MobileControlNetworkMode.CloudflareTunnel
            ? "Cloudflare Tunnel"
            : "本机局域网";
    }
}
