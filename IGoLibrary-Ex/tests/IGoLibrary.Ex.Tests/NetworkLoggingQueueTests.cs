using IGoLibrary.Ex.Application.Logging;
using IGoLibrary.Ex.Infrastructure.Logging;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Tests;

public sealed class NetworkLoggingQueueTests
{
    [Fact]
    public async Task SlowDiskDropsNetworkLogsAtByteBudgetWithoutBlockingAndReleasesBudget()
    {
        var directory = Directory.CreateTempSubdirectory("network-queue-");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            using var writer = new AppLogFileWriter(directory.FullName, 30, null, 2048, async () =>
            {
                started.TrySetResult();
                await release.Task;
            });
            try
            {
                writer.Write(LogLevel.Information, "Test", "开始测试");
                await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                var fill = Task.Run(() =>
                {
                    for (var i = 0; i < 70; i++)
                        writer.Write(LogLevel.Information, NetworkTrafficLogger.Category, new string('a', 65536));
                });
                await fill.WaitAsync(TimeSpan.FromSeconds(30));
                Assert.InRange(writer.PendingNetworkBytes, 1, NetworkLogQueueBudget.Capacity);
            }
            finally { release.TrySetResult(); }
            writer.Flush();
            writer.Dispose();
            Assert.Equal(0, writer.PendingNetworkBytes);
            var text = File.ReadAllText(Assert.Single(Directory.GetFiles(directory.FullName, "*.log")));
            Assert.Contains("网络日志队列或字节预算不足", text);
            Assert.DoesNotContain("已丢弃 0 条网络记录", text);
        }
        finally { release.TrySetResult(); directory.Delete(true); }
    }

    [Fact]
    public async Task FinalFileRetainsBusinessFieldsAndContainsNoSecretsOrInjectedLogLines()
    {
        var directory = Directory.CreateTempSubdirectory("network-file-");
        try
        {
            using var writer = new AppLogFileWriter(directory.FullName);
            var state = new NetworkLogState();
            state.Apply(true);
            var logger = new NetworkTrafficLogger(state, writer);
            using var client = new HttpClient(new NetworkLoggingHandler(logger, "测试")
            {
                InnerHandler = new NetworkTestHandler((request, _) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("<root><seat token='hidden-secret'>A001</seat></root>", System.Text.Encoding.UTF8, "application/xml")
                }))
            });
            using var response = await client.GetAsync("https://a.test/?token=hidden-secret");
            response.Dispose();
            logger.Start("测试", "出站", "GET", "https://a.test")!.Write("正文\n伪造日志");
            var json = NetworkLogSanitizer.Body(
                "{\"url\":\"https://a.test/?x=hidden-secret\",\"seat\":\"URL-SEAT-002\",\"status\":201}"u8,
                "application/json");
            logger.Start("测试", "出站", "GET", "https://a.test")!.Write($"响应结束；正文={json}");
            writer.Flush();
            writer.Dispose();
            var file = Assert.Single(Directory.GetFiles(directory.FullName, "*.log"));
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("hidden-secret", text);
            Assert.Contains("A001", text);
            Assert.Contains("URL-SEAT-002", text);
            Assert.Contains("\"status\":201", text);
            Assert.Contains("；任务=", text);
            Assert.DoesNotContain("\n伪造日志", text);
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public void BudgetRemainsBoundedUnderConcurrentReservations()
    {
        var budget = new NetworkLogQueueBudget();
        Parallel.For(0, 1000, _ =>
        {
            if (!budget.TryAcquire(65536)) return;
            try { Assert.InRange(budget.Used, 0, NetworkLogQueueBudget.Capacity); }
            finally { budget.Release(65536); }
        });
        Assert.Equal(0, budget.Used);
    }
}
