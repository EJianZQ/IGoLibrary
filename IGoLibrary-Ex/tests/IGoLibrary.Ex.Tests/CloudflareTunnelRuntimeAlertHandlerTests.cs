using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;

namespace IGoLibrary.Ex.Tests;

public sealed class CloudflareTunnelRuntimeAlertHandlerTests
{
    [Theory]
    [InlineData(TaskEventAlertDispatchResult.Dispatched, LogEntryKind.Warning, "已分发")]
    [InlineData(TaskEventAlertDispatchResult.Suppressed, LogEntryKind.Info, "短期内已处理相同事件")]
    [InlineData(TaskEventAlertDispatchResult.Disabled, LogEntryKind.Info, "事件开关已关闭")]
    public async Task HandleAsync_LogsWhetherAlertWasDispatched(
        TaskEventAlertDispatchResult dispatchResult,
        LogEntryKind expectedKind,
        string expectedMessage)
    {
        var dispatcher = new FakeTaskEventAlertDispatcher
        {
            CloudflareTunnelInterruptedDispatchResult = dispatchResult
        };
        var activityLog = new ActivityLogService();
        var handler = new CloudflareTunnelRuntimeAlertHandler(
            dispatcher,
            activityLog,
            NullLogger<CloudflareTunnelRuntimeAlertHandler>.Instance);

        await handler.HandleAsync(CloudflareTunnelInterruptionOutcome.FellBackToLocalNetwork);

        Assert.Equal(
            [CloudflareTunnelInterruptionOutcome.FellBackToLocalNetwork],
            dispatcher.CloudflareTunnelInterruptedNotifications);
        Assert.Contains(
            activityLog.Entries,
            entry => entry.Kind == expectedKind &&
                     entry.Category == "Alert" &&
                     entry.Message.Contains(expectedMessage, StringComparison.Ordinal));
    }

    [Fact]
    public async Task HandleAsync_IsolatesDispatcherFailureAndWritesWarning()
    {
        var dispatcher = new FakeTaskEventAlertDispatcher
        {
            NotifyCloudflareTunnelInterruptedException = new InvalidOperationException("settings unavailable")
        };
        var activityLog = new ActivityLogService();
        var handler = new CloudflareTunnelRuntimeAlertHandler(
            dispatcher,
            activityLog,
            NullLogger<CloudflareTunnelRuntimeAlertHandler>.Instance);

        await handler.HandleAsync(CloudflareTunnelInterruptionOutcome.TunnelModeRetained);

        Assert.Contains(
            activityLog.Entries,
            entry => entry.Kind == LogEntryKind.Warning &&
                     entry.Category == "Alert" &&
                     entry.Message.Contains("settings unavailable", StringComparison.Ordinal));
    }
}
