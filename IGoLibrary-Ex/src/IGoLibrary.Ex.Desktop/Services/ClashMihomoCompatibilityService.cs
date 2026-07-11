using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Configuration;
using IGoLibrary.Ex.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Desktop.Services;

internal sealed partial class ClashMihomoCompatibilityService(
    IClashMihomoConfigurationLocator configurationLocator,
    IMihomoControllerClient controllerClient,
    IActivityLogService activityLogService,
    ILogger<ClashMihomoCompatibilityService> logger) : IClashMihomoCompatibilityService
{
    private const string TemporaryFilePrefix = ".igolibrary-cloudflare-";
    private static readonly JsonSerializerOptions RuleJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ActiveConfiguration? _active;
    private int _referenceCount;

    public async Task<IAsyncDisposable?> AcquireAsync(
        ClashMihomoCompatibilityOptions options,
        CancellationToken cancellationToken = default)
    {
        if (!options.Enabled)
        {
            return null;
        }

        if (!MobileControlSettings.TryNormalizeClashMihomoConfigPath(options.ConfigPath, out var configPath) ||
            !MobileControlSettings.TryNormalizeClashMihomoRoutePolicy(options.RoutePolicy, out var routePolicy))
        {
            throw new InvalidOperationException("Clash/Mihomo 兼容设置无效。");
        }
        var normalizedOptions = new ClashMihomoCompatibilityOptions(true, configPath, routePolicy);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_active is not null)
            {
                if (_active.Options != normalizedOptions)
                {
                    throw new InvalidOperationException(
                        "已有 Tunnel 正在使用另一组 Clash/Mihomo 兼容规则；请先切换到本机局域网，再应用兼容设置。");
                }

                _referenceCount++;
                return new Lease(this, _active.Id);
            }

            var configurations = await configurationLocator.FindAsync(configPath, cancellationToken);
            if (configurations.Count == 0)
            {
                throw new InvalidOperationException(
                    "未检测到已启用本机控制接口的 Clash/Mihomo。可在设置中手动指定包含 rules、external-controller 或 external-controller-pipe 的活动配置文件。");
            }

            var failures = new List<string>();
            foreach (var configuration in configurations)
            {
                var temporaryPath = Path.Combine(
                    configuration.WorkingDirectory,
                    $"{TemporaryFilePrefix}{Guid.NewGuid():N}.yaml");
                try
                {
                    var source = await File.ReadAllTextAsync(configuration.SourcePath, cancellationToken);
                    var compatible = AddCompatibilityRules(source, routePolicy);
                    await WritePrivateFileAsync(temporaryPath, compatible, cancellationToken);
                    await controllerClient.ReloadAsync(configuration, temporaryPath, cancellationToken);

                    _active = new ActiveConfiguration(
                        Guid.NewGuid(),
                        configuration,
                        temporaryPath,
                        normalizedOptions);
                    _referenceCount = 1;
                    activityLogService.Write(
                        LogEntryKind.Info,
                        "Network",
                        $"已启用 {configuration.ClientName} 临时兼容规则（路由策略：{routePolicy}）");
                    return new Lease(this, _active.Id);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    TryDeleteTemporaryFile(temporaryPath);
                    throw;
                }
                catch (Exception ex)
                {
                    TryDeleteTemporaryFile(temporaryPath);
                    logger.LogWarning(ex, "Failed to activate {ClientName} compatibility rules.", configuration.ClientName);
                    failures.Add($"{configuration.ClientName}：{ex.Message}");
                }
            }

            throw new InvalidOperationException(
                "Clash/Mihomo 兼容规则应用失败。" + string.Join("；", failures));
        }
        finally
        {
            _gate.Release();
        }
    }

    internal static string AddCompatibilityRules(string source, string routePolicy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (!MobileControlSettings.TryNormalizeClashMihomoRoutePolicy(routePolicy, out var normalizedPolicy))
        {
            throw new ArgumentException("Mihomo 路由策略无效。", nameof(routePolicy));
        }

        var match = RulesHeaderRegex().Match(source);
        if (!match.Success)
        {
            throw new InvalidOperationException("活动 Mihomo 配置中没有可注入的 rules 列表。");
        }

        var newline = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var rules = new[]
        {
            $"PROCESS-NAME,cloudflared.exe,{normalizedPolicy}",
            $"PROCESS-NAME,cloudflared,{normalizedPolicy}",
            $"DOMAIN,api.trycloudflare.com,{normalizedPolicy}",
            $"DOMAIN-SUFFIX,trycloudflare.com,{normalizedPolicy}",
            $"DOMAIN-SUFFIX,argotunnel.com,{normalizedPolicy}",
            $"DOMAIN-SUFFIX,cftunnel.com,{normalizedPolicy}"
        };
        var injected = string.Join(newline,
            new[] { "# IGoLibrary-Ex temporary Cloudflare Tunnel compatibility rules" }
                .Concat(rules.Select(static rule => $"- {JsonSerializer.Serialize(rule, RuleJsonOptions)}")));
        return source.Insert(match.Index + match.Length, newline + injected);
    }

    private async ValueTask ReleaseAsync(Guid id)
    {
        await _gate.WaitAsync();
        try
        {
            if (_active is null || _active.Id != id || _referenceCount <= 0)
            {
                return;
            }

            _referenceCount--;
            if (_referenceCount > 0)
            {
                return;
            }

            var active = _active;
            try
            {
                await controllerClient.ReloadAsync(active.Configuration, active.Configuration.SourcePath);
                TryDeleteTemporaryFile(active.TemporaryPath);
                activityLogService.Write(
                    LogEntryKind.Info,
                    "Network",
                    $"已恢复 {active.Configuration.ClientName} 原始运行配置");
                _active = null;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to restore original Mihomo configuration.");
                activityLogService.Write(
                    LogEntryKind.Warning,
                    "Network",
                    $"恢复 {active.Configuration.ClientName} 原始配置失败：{ex.Message}；临时配置文件已保留");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task WritePrivateFileAsync(
        string path,
        string content,
        CancellationToken cancellationToken)
    {
        await using (var stream = new FileStream(
                         path,
                         FileMode.CreateNew,
                         FileAccess.Write,
                         FileShare.Read,
                         64 * 1024,
                         FileOptions.Asynchronous))
        await using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
        {
            await writer.WriteAsync(content.AsMemory(), cancellationToken);
        }

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private static void TryDeleteTemporaryFile(string path)
    {
        try
        {
            if (Path.GetFileName(path).StartsWith(TemporaryFilePrefix, StringComparison.Ordinal) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    [GeneratedRegex(@"(?m)^rules:\s*(?:#.*)?\r?$")]
    private static partial Regex RulesHeaderRegex();

    private sealed record ActiveConfiguration(
        Guid Id,
        MihomoConfiguration Configuration,
        string TemporaryPath,
        ClashMihomoCompatibilityOptions Options);

    private sealed class Lease(ClashMihomoCompatibilityService owner, Guid id) : IAsyncDisposable
    {
        private int _disposed;

        public ValueTask DisposeAsync()
        {
            return Interlocked.Exchange(ref _disposed, 1) == 0
                ? owner.ReleaseAsync(id)
                : ValueTask.CompletedTask;
        }
    }
}
