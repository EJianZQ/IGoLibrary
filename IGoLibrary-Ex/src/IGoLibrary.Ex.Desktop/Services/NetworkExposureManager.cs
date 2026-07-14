using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Configuration;
using IGoLibrary.Ex.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Desktop.Services;

internal sealed class NetworkExposureManager(
    ICloudflareQuickTunnelRunner tunnelRunner,
    ISettingsWorkflowService settingsWorkflowService,
    IActivityLogService activityLogService,
    INotificationService notificationService,
    ILogger<NetworkExposureManager> logger) : INetworkExposureManager
{
    internal const string TunnelStartupFailureUserMessage =
        "Cloudflare Tunnel 启动失败，请检查网络或代理设置，详情请查看日志";

    internal const string TunnelUnavailableWithoutFallbackUserMessage =
        "Cloudflare Tunnel 不可用，已保持 Tunnel 模式且未回退。请检查网络或代理设置，详情请查看日志";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<Guid, NetworkExposureLease> _leases = [];
    private MobileControlNetworkMode _currentMode = MobileControlNetworkMode.LocalNetwork;
    private CloudflareTunnelProxyOptions _proxyOptions = new(CloudflareTunnelProxyMode.Auto, string.Empty);
    private ClashMihomoCompatibilityOptions _compatibilityOptions = ClashMihomoCompatibilityOptions.Disabled;
    private bool _fallbackToLocalNetworkOnTunnelFailure = true;
    private bool _disposed;

    public event EventHandler<NetworkModeChangedEventArgs>? ModeChanged;

    public MobileControlNetworkMode CurrentMode => _currentMode;

    public void Initialize(
        MobileControlNetworkMode networkMode,
        CloudflareTunnelProxyMode tunnelProxyMode,
        string tunnelManualProxyUrl,
        bool clashMihomoCompatibilityEnabled = false,
        string clashMihomoConfigPath = "",
        string clashMihomoRoutePolicy = "DIRECT",
        bool fallbackToLocalNetworkOnTunnelFailure = true)
    {
        if (_leases.Count != 0)
        {
            throw new InvalidOperationException("网络发布已启动，不能再次初始化网络方式");
        }

        _currentMode = MobileControlSettings.NormalizeNetworkMode(networkMode);
        _proxyOptions = NormalizeProxyOptions(tunnelProxyMode, tunnelManualProxyUrl);
        _compatibilityOptions = NormalizeCompatibilityOptions(
            clashMihomoCompatibilityEnabled,
            clashMihomoConfigPath,
            clashMihomoRoutePolicy);
        _fallbackToLocalNetworkOnTunnelFailure = fallbackToLocalNetworkOnTunnelFailure;
    }

    public async Task<MobileControlNetworkMode> SetModeAsync(
        MobileControlNetworkMode networkMode,
        CancellationToken cancellationToken = default)
    {
        var normalizedMode = MobileControlSettings.NormalizeNetworkMode(networkMode);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            if (_currentMode == normalizedMode)
            {
                return _currentMode;
            }

            if (normalizedMode == MobileControlNetworkMode.LocalNetwork)
            {
                await settingsWorkflowService.SaveMobileControlNetworkModeAsync(
                    MobileControlNetworkMode.LocalNetwork,
                    cancellationToken);
                await CommitLocalNetworkUnderGateAsync("已切换到本机局域网");
                return _currentMode;
            }

            Dictionary<Guid, ICloudflareQuickTunnelSession> prepared = [];
            var preparedTunnelsCommitted = false;
            try
            {
                prepared = await PrepareAllTunnelsUnderGateAsync(
                    _proxyOptions,
                    _compatibilityOptions,
                    cancellationToken);
                await settingsWorkflowService.SaveMobileControlNetworkModeAsync(
                    MobileControlNetworkMode.CloudflareTunnel,
                    cancellationToken);
                _currentMode = MobileControlNetworkMode.CloudflareTunnel;
                foreach (var pair in prepared)
                {
                    AttachTunnelUnderGate(_leases[pair.Key], pair.Value);
                }

                preparedTunnelsCommitted = true;
                PublishModeChanged(_currentMode);
                activityLogService.Write(LogEntryKind.Info, "Network", "手机控制网络方式已切换到 Cloudflare Tunnel");
                return _currentMode;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (CloudflareTunnelProxyConflictException)
            {
                throw;
            }
            catch (CloudflaredUnavailableException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Cloudflare Tunnel startup failed while switching network mode.");
                if (_fallbackToLocalNetworkOnTunnelFailure)
                {
                    await FallbackToLocalNetworkUnderGateAsync($"Cloudflare Tunnel 启动失败：{ex.Message}");
                    return _currentMode;
                }

                throw CreateTunnelStartupException(ex);
            }
            finally
            {
                if (!preparedTunnelsCommitted)
                {
                    foreach (var tunnel in prepared.Values)
                    {
                        await DisposeTunnelSafelyAsync(tunnel);
                    }
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<MobileControlSettings> SetCloudflareTunnelFallbackAsync(
        bool fallbackToLocalNetworkOnTunnelFailure,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            var saved = await settingsWorkflowService.SaveCloudflareTunnelFallbackAsync(
                fallbackToLocalNetworkOnTunnelFailure,
                cancellationToken);
            _fallbackToLocalNetworkOnTunnelFailure = saved.FallbackToLocalNetworkOnTunnelFailure;
            activityLogService.Write(
                LogEntryKind.Info,
                "Network",
                _fallbackToLocalNetworkOnTunnelFailure
                    ? "Cloudflare Tunnel 故障自动回退已开启"
                    : "Cloudflare Tunnel 故障自动回退已关闭");
            return saved;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<MobileControlSettings> SetCloudflareTunnelProxyAsync(
        CloudflareTunnelProxyMode proxyMode,
        string manualProxyUrl,
        CancellationToken cancellationToken = default)
    {
        var requested = NormalizeProxyOptions(proxyMode, manualProxyUrl);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            if (_proxyOptions == requested && _currentMode != MobileControlNetworkMode.CloudflareTunnel)
            {
                return await settingsWorkflowService.SaveCloudflareTunnelProxyAsync(
                    requested.Mode,
                    requested.ManualProxyUrl,
                    cancellationToken);
            }

            Dictionary<Guid, ICloudflareQuickTunnelSession> prepared = [];
            try
            {
                if (_currentMode == MobileControlNetworkMode.CloudflareTunnel)
                {
                    prepared = await PrepareAllTunnelsUnderGateAsync(
                        requested,
                        _compatibilityOptions,
                        cancellationToken);
                }

                var saved = await settingsWorkflowService.SaveCloudflareTunnelProxyAsync(
                    requested.Mode,
                    requested.ManualProxyUrl,
                    cancellationToken);
                var normalizedSaved = CloudflareTunnelProxyOptions.From(saved);
                if (_currentMode == MobileControlNetworkMode.CloudflareTunnel)
                {
                    var replaced = new List<ICloudflareQuickTunnelSession>();
                    foreach (var pair in prepared)
                    {
                        var lease = _leases[pair.Key];
                        if (lease.Tunnel is not null)
                        {
                            replaced.Add(lease.Tunnel);
                        }

                        AttachTunnelUnderGate(lease, pair.Value);
                    }

                    foreach (var oldTunnel in replaced)
                    {
                        await DisposeTunnelSafelyAsync(oldTunnel);
                    }
                }

                _proxyOptions = normalizedSaved;
                activityLogService.Write(LogEntryKind.Info, "Network", "Cloudflare Tunnel 代理设置已应用");
                return saved;
            }
            catch
            {
                foreach (var tunnel in prepared.Values)
                {
                    await DisposeTunnelSafelyAsync(tunnel);
                }

                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<MobileControlSettings> SetClashMihomoCompatibilityAsync(
        bool enabled,
        string configPath,
        string routePolicy,
        CancellationToken cancellationToken = default)
    {
        var requested = NormalizeCompatibilityOptions(enabled, configPath, routePolicy);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            Dictionary<Guid, ICloudflareQuickTunnelSession> prepared = [];
            try
            {
                if (_currentMode == MobileControlNetworkMode.CloudflareTunnel && requested != _compatibilityOptions)
                {
                    prepared = await PrepareAllTunnelsUnderGateAsync(
                        _proxyOptions,
                        requested,
                        cancellationToken);
                }

                var saved = await settingsWorkflowService.SaveClashMihomoCompatibilityAsync(
                    requested.Enabled,
                    requested.ConfigPath,
                    requested.RoutePolicy,
                    cancellationToken);
                var normalizedSaved = NormalizeCompatibilityOptions(
                    saved.ClashMihomoCompatibilityEnabled,
                    saved.ClashMihomoConfigPath,
                    saved.ClashMihomoRoutePolicy);
                if (prepared.Count > 0)
                {
                    var replaced = new List<ICloudflareQuickTunnelSession>();
                    foreach (var pair in prepared)
                    {
                        var lease = _leases[pair.Key];
                        if (lease.Tunnel is not null)
                        {
                            replaced.Add(lease.Tunnel);
                        }

                        AttachTunnelUnderGate(lease, pair.Value);
                    }

                    foreach (var oldTunnel in replaced)
                    {
                        await DisposeTunnelSafelyAsync(oldTunnel);
                    }
                }

                _compatibilityOptions = normalizedSaved;
                activityLogService.Write(
                    LogEntryKind.Info,
                    "Network",
                    saved.ClashMihomoCompatibilityEnabled
                        ? "Clash/Mihomo 兼容模式已应用"
                        : "Clash/Mihomo 兼容模式已关闭");
                return saved;
            }
            catch
            {
                foreach (var tunnel in prepared.Values)
                {
                    await DisposeTunnelSafelyAsync(tunnel);
                }

                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<INetworkExposureLease> PublishAsync(
        Uri lanUrl,
        string healthCheckPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lanUrl);
        if (!lanUrl.IsAbsoluteUri || lanUrl.Scheme != Uri.UriSchemeHttp)
        {
            throw new ArgumentException("局域网发布地址必须是绝对 HTTP 地址", nameof(lanUrl));
        }

        var lease = new NetworkExposureLease(this, lanUrl, healthCheckPath);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            _leases.Add(lease.Id, lease);
            if (_currentMode == MobileControlNetworkMode.CloudflareTunnel)
            {
                try
                {
                    tunnelRunner.ValidateConfiguration(_proxyOptions, _compatibilityOptions);
                    var tunnel = await tunnelRunner.StartAsync(
                        lanUrl,
                        healthCheckPath,
                        _proxyOptions,
                        _compatibilityOptions,
                        cancellationToken);
                    AttachTunnelUnderGate(lease, tunnel);
                }
                catch (CloudflareTunnelProxyConflictException)
                {
                    throw;
                }
                catch (CloudflaredUnavailableException)
                {
                    throw;
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    logger.LogWarning(ex, "Cloudflare Tunnel startup failed while publishing a service.");
                    var message = $"Cloudflare Tunnel 启动失败：{ex.Message}";
                    if (_fallbackToLocalNetworkOnTunnelFailure)
                    {
                        await FallbackToLocalNetworkUnderGateAsync(message);
                    }
                    else
                    {
                        ReportTunnelFailureWithoutFallbackUnderGate(message, showNotification: false);
                        throw CreateTunnelStartupException(ex);
                    }
                }
            }

            return lease;
        }
        catch
        {
            _leases.Remove(lease.Id);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var lease in _leases.Values.ToArray())
            {
                await lease.StopTunnelUnderManagerLockAsync();
                lease.MarkDisposed();
            }

            _leases.Clear();
        }
        finally
        {
            _gate.Release();
        }
    }

    private void AttachTunnelUnderGate(
        NetworkExposureLease lease,
        ICloudflareQuickTunnelSession tunnel)
    {
        lease.AttachTunnel(tunnel);
        _ = ObserveTunnelAsync(lease.Id, tunnel);
    }

    private async Task<Dictionary<Guid, ICloudflareQuickTunnelSession>> PrepareAllTunnelsUnderGateAsync(
        CloudflareTunnelProxyOptions options,
        ClashMihomoCompatibilityOptions compatibilityOptions,
        CancellationToken cancellationToken)
    {
        tunnelRunner.ValidateConfiguration(options, compatibilityOptions);
        var tasks = _leases.Values
            .Select(async lease => (
                lease.Id,
                Session: await tunnelRunner.StartAsync(
                    lease.LanUrl,
                    lease.HealthCheckPath,
                    options,
                    compatibilityOptions,
                    cancellationToken)))
            .ToArray();
        try
        {
            return (await Task.WhenAll(tasks)).ToDictionary(static result => result.Id, static result => result.Session);
        }
        catch
        {
            foreach (var task in tasks.Where(static task => task.IsCompletedSuccessfully))
            {
                await DisposeTunnelSafelyAsync(task.Result.Session);
            }

            throw;
        }
    }

    private async Task DisposeTunnelSafelyAsync(ICloudflareQuickTunnelSession tunnel)
    {
        try
        {
            await tunnel.DisposeAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to dispose replaced Cloudflare Tunnel process.");
        }
    }

    private static CloudflareTunnelProxyOptions NormalizeProxyOptions(
        CloudflareTunnelProxyMode mode,
        string manualProxyUrl)
    {
        var normalizedMode = MobileControlSettings.NormalizeTunnelProxyMode(mode);
        var hasValidManualUrl = MobileControlSettings.TryNormalizeManualProxyUrl(
            manualProxyUrl,
            out var normalizedManualUrl);
        if (normalizedMode == CloudflareTunnelProxyMode.ManualHttpProxy && !hasValidManualUrl)
        {
            throw new ArgumentException(
                "手动代理地址必须是有效 HTTP 地址，例如 http://127.0.0.1:7897",
                nameof(manualProxyUrl));
        }

        return new CloudflareTunnelProxyOptions(
            normalizedMode,
            hasValidManualUrl ? normalizedManualUrl : string.Empty);
    }

    private static ClashMihomoCompatibilityOptions NormalizeCompatibilityOptions(
        bool enabled,
        string configPath,
        string routePolicy)
    {
        if (!MobileControlSettings.TryNormalizeClashMihomoConfigPath(configPath, out var normalizedConfigPath))
        {
            throw new ArgumentException("Mihomo 活动配置必须是绝对路径的 .yaml 或 .yml 文件", nameof(configPath));
        }

        if (!MobileControlSettings.TryNormalizeClashMihomoRoutePolicy(routePolicy, out var normalizedRoutePolicy))
        {
            throw new ArgumentException("Mihomo 路由策略不能为空、不能包含逗号或 #，且最多 128 个字符", nameof(routePolicy));
        }

        return new ClashMihomoCompatibilityOptions(enabled, normalizedConfigPath, normalizedRoutePolicy);
    }

    private async Task ObserveTunnelAsync(
        Guid leaseId,
        ICloudflareQuickTunnelSession tunnel)
    {
        var fault = await tunnel.Completion;
        if (fault is null)
        {
            return;
        }

        await _gate.WaitAsync();
        try
        {
            if (_disposed ||
                _currentMode != MobileControlNetworkMode.CloudflareTunnel ||
                !_leases.TryGetValue(leaseId, out var lease) ||
                !ReferenceEquals(lease.Tunnel, tunnel))
            {
                return;
            }

            if (_fallbackToLocalNetworkOnTunnelFailure)
            {
                await FallbackToLocalNetworkUnderGateAsync(fault.Message);
            }
            else
            {
                var faultedTunnel = lease.DetachFaultedTunnelWithoutChangingEndpoint(tunnel);
                if (faultedTunnel is not null)
                {
                    await DisposeTunnelSafelyAsync(faultedTunnel);
                }

                ReportTunnelFailureWithoutFallbackUnderGate(fault.Message, showNotification: true);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task FallbackToLocalNetworkUnderGateAsync(string diagnostic)
    {
        LogTunnelDiagnostic(diagnostic);
        _currentMode = MobileControlNetworkMode.LocalNetwork;
        var tunnels = _leases.Values
            .Select(static lease => lease.DetachTunnelAndUseLan())
            .Where(static tunnel => tunnel is not null)
            .Cast<ICloudflareQuickTunnelSession>()
            .ToArray();
        foreach (var tunnel in tunnels)
        {
            await tunnel.DisposeAsync();
        }

        var persistenceFailed = false;
        try
        {
            await settingsWorkflowService.SaveMobileControlNetworkModeAsync(
                MobileControlNetworkMode.LocalNetwork);
        }
        catch (Exception ex)
        {
            persistenceFailed = true;
            logger.LogWarning(ex, "Failed to persist Cloudflare Tunnel fallback.");
        }

        var userMessage = persistenceFailed
            ? "Cloudflare Tunnel 不可用，已回退到本机局域网，但设置保存失败。详情请查看日志"
            : "Cloudflare Tunnel 不可用，已自动回退到本机局域网。详情请查看日志";
        PublishModeChanged(_currentMode, userMessage);
        activityLogService.Write(LogEntryKind.Warning, "Network", userMessage);
        _ = ShowFallbackNotificationAsync(userMessage);
    }

    private void ReportTunnelFailureWithoutFallbackUnderGate(
        string diagnostic,
        bool showNotification)
    {
        LogTunnelDiagnostic(diagnostic);
        PublishModeChanged(_currentMode, TunnelUnavailableWithoutFallbackUserMessage);
        activityLogService.Write(
            LogEntryKind.Warning,
            "Network",
            TunnelUnavailableWithoutFallbackUserMessage);
        if (showNotification)
        {
            _ = ShowTunnelFailureNotificationAsync(TunnelUnavailableWithoutFallbackUserMessage);
        }
    }

    private static Exception CreateTunnelStartupException(Exception innerException)
    {
        return new InvalidOperationException(TunnelStartupFailureUserMessage, innerException);
    }

    private void LogTunnelDiagnostic(string diagnostic)
    {
        logger.LogWarning("Cloudflare Tunnel diagnostic: {Diagnostic}", diagnostic);
    }

    private async Task CommitLocalNetworkUnderGateAsync(string message)
    {
        _currentMode = MobileControlNetworkMode.LocalNetwork;
        var tunnels = _leases.Values
            .Select(static lease => lease.DetachTunnelAndUseLan())
            .Where(static tunnel => tunnel is not null)
            .Cast<ICloudflareQuickTunnelSession>()
            .ToArray();
        foreach (var tunnel in tunnels)
        {
            await tunnel.DisposeAsync();
        }

        PublishModeChanged(_currentMode);
        activityLogService.Write(LogEntryKind.Info, "Network", message);
    }

    private async Task RemoveAsync(NetworkExposureLease lease)
    {
        await _gate.WaitAsync();
        try
        {
            if (_leases.Remove(lease.Id))
            {
                await lease.StopTunnelUnderManagerLockAsync();
            }

            lease.MarkDisposed();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ShowFallbackNotificationAsync(string message)
    {
        try
        {
            await notificationService.ShowWarningAsync("Cloudflare Tunnel 已回退", message);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to show Cloudflare Tunnel fallback notification.");
        }
    }

    private async Task ShowTunnelFailureNotificationAsync(string message)
    {
        try
        {
            await notificationService.ShowWarningAsync("Cloudflare Tunnel 不可用", message);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to show Cloudflare Tunnel failure notification.");
        }
    }

    private void PublishModeChanged(MobileControlNetworkMode mode, string? message = null)
    {
        ModeChanged?.Invoke(this, new NetworkModeChangedEventArgs(mode, message));
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private sealed class NetworkExposureLease(
        NetworkExposureManager owner,
        Uri lanUrl,
        string healthCheckPath) : INetworkExposureLease
    {
        private int _disposed;

        public event EventHandler<NetworkExposureChangedEventArgs>? EndpointChanged;

        public Guid Id { get; } = Guid.NewGuid();

        public Uri LanUrl { get; } = lanUrl;

        public string HealthCheckPath { get; } = healthCheckPath;

        public Uri Url { get; private set; } = lanUrl;

        public MobileControlNetworkMode EffectiveMode { get; private set; } = MobileControlNetworkMode.LocalNetwork;

        public ICloudflareQuickTunnelSession? Tunnel { get; private set; }

        public ValueTask DisposeAsync()
        {
            return Interlocked.Exchange(ref _disposed, 1) == 0
                ? new ValueTask(owner.RemoveAsync(this))
                : ValueTask.CompletedTask;
        }

        public void AttachTunnel(ICloudflareQuickTunnelSession tunnel)
        {
            Tunnel = tunnel;
            EffectiveMode = MobileControlNetworkMode.CloudflareTunnel;
            Url = ReplaceAuthority(LanUrl, tunnel.PublicBaseUri);
            EndpointChanged?.Invoke(this, new NetworkExposureChangedEventArgs(Url, EffectiveMode));
        }

        public ICloudflareQuickTunnelSession? DetachTunnelAndUseLan()
        {
            var tunnel = Tunnel;
            Tunnel = null;
            EffectiveMode = MobileControlNetworkMode.LocalNetwork;
            Url = LanUrl;
            EndpointChanged?.Invoke(this, new NetworkExposureChangedEventArgs(Url, EffectiveMode));
            return tunnel;
        }

        public ICloudflareQuickTunnelSession? DetachFaultedTunnelWithoutChangingEndpoint(
            ICloudflareQuickTunnelSession expectedTunnel)
        {
            if (!ReferenceEquals(Tunnel, expectedTunnel))
            {
                return null;
            }

            Tunnel = null;
            return expectedTunnel;
        }

        public async Task StopTunnelUnderManagerLockAsync()
        {
            var tunnel = Tunnel;
            Tunnel = null;
            if (tunnel is not null)
            {
                await tunnel.DisposeAsync();
            }
        }

        public void MarkDisposed()
        {
            Interlocked.Exchange(ref _disposed, 1);
        }

        private static Uri ReplaceAuthority(Uri source, Uri authority)
        {
            return new UriBuilder(source)
            {
                Scheme = authority.Scheme,
                Host = authority.Host,
                Port = -1
            }.Uri;
        }
    }
}
