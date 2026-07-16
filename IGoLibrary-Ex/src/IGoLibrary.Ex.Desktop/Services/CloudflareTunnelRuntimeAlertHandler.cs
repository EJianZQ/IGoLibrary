using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Desktop.Services;

internal interface ICloudflareTunnelRuntimeAlertHandler
{
    Task HandleAsync(
        CloudflareTunnelInterruptionOutcome outcome,
        CancellationToken cancellationToken = default);
}

internal sealed class CloudflareTunnelRuntimeAlertHandler(
    ITaskEventAlertDispatcher alertDispatcher,
    IActivityLogService activityLogService,
    ILogger<CloudflareTunnelRuntimeAlertHandler> logger) : ICloudflareTunnelRuntimeAlertHandler
{
    public async Task HandleAsync(
        CloudflareTunnelInterruptionOutcome outcome,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var dispatchResult = await alertDispatcher.TryNotifyCloudflareTunnelInterruptedAsync(
                outcome,
                cancellationToken);
            WriteDispatchResult(dispatchResult, outcome);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to dispatch the mobile-control Cloudflare Tunnel interruption alert.");
            activityLogService.Write(
                LogEntryKind.Warning,
                "Alert",
                $"发送手机控制 Cloudflare Tunnel 运行中断提醒失败：{ex.Message}");
        }
    }

    private void WriteDispatchResult(
        TaskEventAlertDispatchResult dispatchResult,
        CloudflareTunnelInterruptionOutcome outcome)
    {
        var (kind, message) = dispatchResult switch
        {
            TaskEventAlertDispatchResult.Dispatched => (
                LogEntryKind.Warning,
                $"手机控制 Cloudflare Tunnel 运行中断提醒已分发：{DescribeOutcome(outcome)}"),
            TaskEventAlertDispatchResult.Suppressed => (
                LogEntryKind.Info,
                "手机控制 Cloudflare Tunnel 运行中断提醒已跳过：短期内已处理相同事件"),
            TaskEventAlertDispatchResult.Disabled => (
                LogEntryKind.Info,
                "手机控制 Cloudflare Tunnel 运行中断提醒已跳过：事件开关已关闭"),
            _ => throw new ArgumentOutOfRangeException(
                nameof(dispatchResult),
                dispatchResult,
                "未知的通知分发结果")
        };
        activityLogService.Write(kind, "Alert", message);
    }

    private static string DescribeOutcome(CloudflareTunnelInterruptionOutcome outcome)
    {
        return outcome switch
        {
            CloudflareTunnelInterruptionOutcome.FellBackToLocalNetwork => "已回退本机局域网",
            CloudflareTunnelInterruptionOutcome.FellBackToLocalNetworkWithPersistenceFailure =>
                "已回退本机局域网，但设置保存失败",
            CloudflareTunnelInterruptionOutcome.TunnelModeRetained => "保持 Tunnel 模式",
            _ => "未知处理结果"
        };
    }
}
