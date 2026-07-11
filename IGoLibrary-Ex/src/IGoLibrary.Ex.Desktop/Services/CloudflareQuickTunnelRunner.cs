using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Desktop.Services;

internal sealed partial class CloudflareQuickTunnelRunner(
    ICloudflareTunnelProxyResolver proxyResolver,
    ICloudflareTunnelHealthProbeFactory healthProbeFactory,
    IClashMihomoCompatibilityService compatibilityService,
    ILogger<CloudflareQuickTunnelRunner> logger) : ICloudflareQuickTunnelRunner
{
    internal static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan ProbeInterval = TimeSpan.FromSeconds(10);
    internal static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);
    internal const int ConsecutiveFailureThreshold = 3;

    public async Task<ICloudflareQuickTunnelSession> StartAsync(
        Uri originBaseUri,
        string healthCheckPath,
        CloudflareTunnelProxyOptions proxyOptions,
        ClashMihomoCompatibilityOptions compatibilityOptions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(originBaseUri);
        if (!originBaseUri.IsAbsoluteUri || originBaseUri.Scheme != Uri.UriSchemeHttp)
        {
            throw new ArgumentException("Cloudflare Tunnel 本机服务地址必须是绝对 HTTP 地址。", nameof(originBaseUri));
        }

        if (string.IsNullOrWhiteSpace(healthCheckPath) || !healthCheckPath.StartsWith("/", StringComparison.Ordinal))
        {
            throw new ArgumentException("Tunnel 健康检查路径无效。", nameof(healthCheckPath));
        }

        var executablePath = ResolveExecutablePath();
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException("未找到内置 cloudflared，无法启动 Cloudflare Tunnel。", executablePath);
        }

        var proxyResolution = proxyResolver.Resolve(proxyOptions);
        logger.LogInformation(
            "Starting Cloudflare Quick Tunnel with proxy mode {ProxyMode} and HTTP/2 transport.",
            proxyResolution.EffectiveMode);
        var healthProbe = healthProbeFactory.Create(proxyResolution.ProxyUri);
        var isolatedHome = Path.Combine(
            Path.GetTempPath(),
            "IGoLibrary-Ex",
            "cloudflared",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(isolatedHome);
        var metricsPort = ReserveLoopbackPort();
        var startInfo = BuildStartInfo(
            executablePath,
            originBaseUri,
            metricsPort,
            isolatedHome,
            proxyResolution.ProxyUri);
        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };
        var diagnostics = new CloudflaredDiagnosticBuffer();
        var publicUriSource = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);

        DataReceivedEventHandler inspectLine = (_, args) =>
        {
            diagnostics.Add(args.Data);
            if (TryExtractPublicBaseUri(args.Data, out var publicBaseUri))
            {
                publicUriSource.TrySetResult(publicBaseUri);
            }
        };
        process.OutputDataReceived += inspectLine;
        process.ErrorDataReceived += inspectLine;

        IAsyncDisposable? compatibilityLease = null;
        try
        {
            compatibilityLease = await compatibilityService.AcquireAsync(
                compatibilityOptions,
                cancellationToken);
            if (!process.Start())
            {
                throw new InvalidOperationException("cloudflared 进程未能启动。");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            using var startupSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            startupSource.CancelAfter(StartupTimeout);
            var exitTask = process.WaitForExitAsync(CancellationToken.None);
            Uri publicBaseUri;
            try
            {
                publicBaseUri = await WaitForPublicUriAsync(
                    publicUriSource.Task,
                    process,
                    exitTask,
                    diagnostics,
                    startupSource.Token);
                var publicHealthUri = BuildHealthCheckUri(publicBaseUri, healthCheckPath);
                await WaitForHealthyAsync(
                    publicHealthUri,
                    process,
                    exitTask,
                    diagnostics,
                    healthProbe,
                    startupSource.Token);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"Cloudflare Tunnel 未能在 {StartupTimeout.TotalSeconds:0} 秒内就绪。", ex);
            }

            var session = new CloudflareQuickTunnelSession(
                process,
                isolatedHome,
                publicBaseUri,
                BuildHealthCheckUri(publicBaseUri, healthCheckPath),
                healthProbe,
                compatibilityLease,
                logger);
            compatibilityLease = null;
            return session;
        }
        catch
        {
            await StopProcessAsync(process);
            process.Dispose();
            healthProbe.Dispose();
            TryDeleteDirectory(isolatedHome);
            if (compatibilityLease is not null)
            {
                await compatibilityLease.DisposeAsync();
            }
            throw;
        }
    }

    internal static ProcessStartInfo BuildStartInfo(
        string executablePath,
        Uri originBaseUri,
        int metricsPort,
        string isolatedHome,
        Uri? proxyUri)
    {
        var info = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = isolatedHome
        };
        info.ArgumentList.Add("tunnel");
        info.ArgumentList.Add("--no-autoupdate");
        info.ArgumentList.Add("--protocol");
        info.ArgumentList.Add("http2");
        info.ArgumentList.Add("--metrics");
        info.ArgumentList.Add($"127.0.0.1:{metricsPort}");
        info.ArgumentList.Add("--url");
        info.ArgumentList.Add(originBaseUri.GetLeftPart(UriPartial.Authority));
        info.Environment["HOME"] = isolatedHome;
        info.Environment["USERPROFILE"] = isolatedHome;
        info.Environment["XDG_CONFIG_HOME"] = isolatedHome;
        ApplyProxyEnvironment(info, proxyUri, originBaseUri.Host);
        return info;
    }

    private static void ApplyProxyEnvironment(
        ProcessStartInfo info,
        Uri? proxyUri,
        string originHost)
    {
        foreach (var name in new[]
                 {
                     "HTTP_PROXY", "http_proxy",
                     "HTTPS_PROXY", "https_proxy",
                     "ALL_PROXY", "all_proxy",
                     "NO_PROXY", "no_proxy"
                 })
        {
            info.Environment.Remove(name);
        }

        if (proxyUri is not null)
        {
            var value = proxyUri.GetLeftPart(UriPartial.Authority);
            info.Environment["HTTP_PROXY"] = value;
            info.Environment["HTTPS_PROXY"] = value;
        }

        var noProxyHosts = new[] { "localhost", "127.0.0.1", "::1", originHost }
            .Where(static host => !string.IsNullOrWhiteSpace(host))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        info.Environment["NO_PROXY"] = string.Join(',', noProxyHosts);
    }

    internal static bool TryExtractPublicBaseUri(string? line, out Uri publicBaseUri)
    {
        publicBaseUri = null!;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var match = TryCloudflareUrlRegex().Match(line);
        if (!match.Success || !Uri.TryCreate(match.Value, UriKind.Absolute, out var candidate))
        {
            return false;
        }

        if (candidate.Scheme != Uri.UriSchemeHttps ||
            !candidate.IsDefaultPort ||
            !candidate.Host.EndsWith(".trycloudflare.com", StringComparison.OrdinalIgnoreCase) ||
            candidate.Host.Length <= ".trycloudflare.com".Length ||
            candidate.Host.Equals("api.trycloudflare.com", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(candidate.UserInfo))
        {
            return false;
        }

        publicBaseUri = new UriBuilder(Uri.UriSchemeHttps, candidate.Host)
        {
            Path = "/",
            Query = string.Empty,
            Fragment = string.Empty
        }.Uri;
        return true;
    }

    private async Task WaitForHealthyAsync(
        Uri publicHealthUri,
        Process process,
        Task processExitTask,
        CloudflaredDiagnosticBuffer diagnostics,
        ICloudflareTunnelHealthProbeSession healthProbe,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (processExitTask.IsCompleted)
            {
                await processExitTask;
                throw CreateUnexpectedExitException(process, "在 Tunnel 就绪前", diagnostics);
            }

            if (await healthProbe.IsHealthyAsync(publicHealthUri, ProbeTimeout, cancellationToken))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }
    }

    private static async Task<Uri> WaitForPublicUriAsync(
        Task<Uri> publicUriTask,
        Process process,
        Task processExitTask,
        CloudflaredDiagnosticBuffer diagnostics,
        CancellationToken cancellationToken)
    {
        var cancellationTask = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        var completed = await Task.WhenAny(publicUriTask, processExitTask, cancellationTask);
        if (completed == publicUriTask)
        {
            return await publicUriTask;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await processExitTask;
        throw CreateUnexpectedExitException(process, "在生成公网地址前", diagnostics);
    }

    private static InvalidOperationException CreateUnexpectedExitException(
        Process process,
        string phase,
        CloudflaredDiagnosticBuffer diagnostics)
    {
        var exitCode = process.ExitCode;
        var diagnosticSummary = diagnostics.GetSummary();
        var message = $"cloudflared {phase}已退出（退出码 {exitCode}）";
        if (!string.IsNullOrWhiteSpace(diagnosticSummary))
        {
            message += $"。最后输出：{diagnosticSummary}";
        }

        if (IsLikelyProxyOrTunFailure(diagnosticSummary))
        {
            message += "。检测到 Cloudflare API 的 TLS 连接被中断，请检查系统代理、TUN/Fake-IP 规则或当前代理节点";
        }

        return new InvalidOperationException(message);
    }

    internal static bool IsLikelyProxyOrTunFailure(string diagnosticSummary)
    {
        return diagnosticSummary.Contains("api.trycloudflare.com/tunnel", StringComparison.OrdinalIgnoreCase) &&
               (diagnosticSummary.Contains("EOF", StringComparison.OrdinalIgnoreCase) ||
                diagnosticSummary.Contains("TLS", StringComparison.OrdinalIgnoreCase));
    }

    internal static string SanitizeDiagnosticLine(string line)
    {
        var sanitized = SensitiveValueRegex().Replace(line, "${prefix}[redacted]");
        const int maxLength = 500;
        return sanitized.Length <= maxLength
            ? sanitized
            : sanitized[..maxLength] + "…";
    }

    private static Uri BuildHealthCheckUri(Uri publicBaseUri, string healthCheckPath)
    {
        var builder = new UriBuilder(new Uri(publicBaseUri, healthCheckPath))
        {
            Query = $"probe={Guid.NewGuid():N}"
        };
        return builder.Uri;
    }

    private static int ReserveLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static string ResolveExecutablePath()
    {
        var executableName = OperatingSystem.IsWindows() ? "cloudflared.exe" : "cloudflared";
        return Path.Combine(AppContext.BaseDirectory, "tools", "cloudflared", executableName);
    }

    internal static async Task StopProcessAsync(Process process)
    {
        try
        {
            if (process.HasExited)
            {
                return;
            }
        }
        catch (InvalidOperationException)
        {
            return;
        }

        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            return;
        }

        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException)
        {
        }
    }

    internal static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }

    [GeneratedRegex(@"https://[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\.trycloudflare\.com(?![a-z0-9.-])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TryCloudflareUrlRegex();

    [GeneratedRegex(@"(?<prefix>\b(?:token|access_token|authorization|code)=)[^&\s]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveValueRegex();

    private sealed class CloudflaredDiagnosticBuffer
    {
        private const int Capacity = 12;
        private const int SummaryLineCount = 3;
        private readonly object _gate = new();
        private readonly Queue<string> _lines = new(Capacity);

        public void Add(string? line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            var sanitized = SanitizeDiagnosticLine(line.Trim());
            lock (_gate)
            {
                if (_lines.Count == Capacity)
                {
                    _lines.Dequeue();
                }

                _lines.Enqueue(sanitized);
            }
        }

        public string GetSummary()
        {
            lock (_gate)
            {
                return string.Join(" | ", _lines.TakeLast(SummaryLineCount));
            }
        }
    }

    private sealed class CloudflareQuickTunnelSession : ICloudflareQuickTunnelSession
    {
        private readonly Process _process;
        private readonly string _isolatedHome;
        private readonly CancellationTokenSource _stopSource = new();
        private readonly ICloudflareTunnelHealthProbeSession _healthProbe;
        private readonly IAsyncDisposable? _compatibilityLease;
        private readonly ILogger _logger;
        private int _disposed;

        public CloudflareQuickTunnelSession(
            Process process,
            string isolatedHome,
            Uri publicBaseUri,
            Uri publicHealthUri,
            ICloudflareTunnelHealthProbeSession healthProbe,
            IAsyncDisposable? compatibilityLease,
            ILogger logger)
        {
            _process = process;
            _isolatedHome = isolatedHome;
            PublicBaseUri = publicBaseUri;
            _healthProbe = healthProbe;
            _compatibilityLease = compatibilityLease;
            _logger = logger;
            Completion = MonitorAsync(
                publicHealthUri,
                process.WaitForExitAsync(CancellationToken.None),
                _stopSource.Token);
        }

        public Uri PublicBaseUri { get; }

        public Task<CloudflareTunnelFault?> Completion { get; }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _stopSource.Cancel();
            await CloudflareQuickTunnelRunner.StopProcessAsync(_process);
            try
            {
                await Completion;
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                _process.Dispose();
                _healthProbe.Dispose();
                _stopSource.Dispose();
                CloudflareQuickTunnelRunner.TryDeleteDirectory(_isolatedHome);
                if (_compatibilityLease is not null)
                {
                    await _compatibilityLease.DisposeAsync();
                }
            }
        }

        private async Task<CloudflareTunnelFault?> MonitorAsync(
            Uri publicHealthUri,
            Task processExitTask,
            CancellationToken cancellationToken)
        {
            var healthState = new CloudflareTunnelHealthState(ConsecutiveFailureThreshold);
            try
            {
                while (true)
                {
                    var delayTask = Task.Delay(ProbeInterval, cancellationToken);
                    var completed = await Task.WhenAny(delayTask, processExitTask);
                    if (completed == processExitTask)
                    {
                        await processExitTask;
                        return new CloudflareTunnelFault("cloudflared 进程已意外退出");
                    }

                    await delayTask;
                    var healthy = await _healthProbe.IsHealthyAsync(
                        publicHealthUri,
                        ProbeTimeout,
                        cancellationToken);
                    if (healthState.RecordProbe(healthy))
                    {
                        return new CloudflareTunnelFault("Cloudflare Tunnel 公网健康检查连续失败 3 次");
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cloudflare Tunnel monitor failed.");
                return new CloudflareTunnelFault("Cloudflare Tunnel 监控异常");
            }
        }
    }
}
