using IGoLibrary.Ex.Desktop.Services;

namespace IGoLibrary.Ex.Tests;

public sealed class MobileControlMobilePageTests
{
    [Fact]
    public void Build_InjectsTokenAndContainsActionDom()
    {
        var html = MobileControlMobilePage.Build("secret-token");

        Assert.Contains("const token = \"secret-token\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("__MOBILE_CONTROL_TOKEN_JSON__", html, StringComparison.Ordinal);
        Assert.Contains("取消任务", html, StringComparison.Ordinal);
        Assert.Contains("取消当前预约", html, StringComparison.Ordinal);
        Assert.Contains("刷新 Cookie", html, StringComparison.Ordinal);
        Assert.Contains("<details class=\"cookie-refresh-panel\" id=\"cookieRefreshPanel\">", html, StringComparison.Ordinal);
        Assert.Contains("<summary>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"cookieRefreshPanel\" open", html, StringComparison.Ordinal);
        Assert.Contains("id=\"cookieRefreshForm\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"authQrCode\"", html, StringComparison.Ordinal);
        Assert.Contains("微信授权二维码", html, StringComparison.Ordinal);
        Assert.Contains("/api/session/auth-qrcode?token=' + encodeURIComponent(token)", html, StringComparison.Ordinal);
        Assert.Contains("/api/session/cookie/refresh", html, StringComparison.Ordinal);
        Assert.Contains("当前无任务", html, StringComparison.Ordinal);
        Assert.Contains("const display = value =>", html, StringComparison.Ordinal);
        Assert.Contains("id=\"cookieProgress\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"reservationProgress\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"grabRecordList\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"globalLeakRecordList\"", html, StringComparison.Ordinal);
        Assert.Contains("data-task-start=\"occupy\"", html, StringComparison.Ordinal);
        Assert.Contains("/api/task-records?token=", html, StringComparison.Ordinal);
        Assert.Contains("/api/tasks/' + encodeURIComponent(kind) + '/start?token=", html, StringComparison.Ordinal);
        Assert.Contains("暂无电脑端抢座记录", html, StringComparison.Ordinal);
        Assert.Contains("暂无电脑端全域捡漏记录", html, StringComparison.Ordinal);
        Assert.Contains("escapeHtml(record.libraryName)", html, StringComparison.Ordinal);
        Assert.Contains("const selectedTaskRecordIds = { grab: null, globalLeak: null }", html, StringComparison.Ordinal);
        Assert.Contains("function taskRecordPicker(kind, records, disabled, pending)", html, StringComparison.Ordinal);
        Assert.Contains("return `${record.libraryName} · ${seats}`", html, StringComparison.Ordinal);
        Assert.Contains("return `${firstLibrary} 等 ${libraryCount} 个场馆 · ${record.scanIntervalSeconds} 秒`", html, StringComparison.Ordinal);
        Assert.Contains("data-task-record-select=", html, StringComparison.Ordinal);
        Assert.Contains("id=\"${selectId}\"", html, StringComparison.Ordinal);
        Assert.Contains("使用所选记录启动", html, StringComparison.Ordinal);
        Assert.Contains("selectedTaskRecordIds[recordSelect.dataset.taskRecordSelect] = recordSelect.value", html, StringComparison.Ordinal);
        Assert.DoesNotContain("record-card", html, StringComparison.Ordinal);
        Assert.Contains("const pendingTaskStarts = new Set()", html, StringComparison.Ordinal);
        Assert.Contains("if (pendingTaskStarts.has(kind)) return", html, StringComparison.Ordinal);
        Assert.Contains("pendingTaskStarts.add(kind)", html, StringComparison.Ordinal);
        Assert.Contains("pendingTaskStarts.delete(kind)", html, StringComparison.Ordinal);
        Assert.Contains("grabActive || grabPending", html, StringComparison.Ordinal);
        Assert.Contains("occupyActive || occupyPending", html, StringComparison.Ordinal);
        Assert.Contains("latestTaskRecords = { grab: [], globalLeak: [] }", html, StringComparison.Ordinal);
        Assert.Contains("抢座记录读取失败，请稍后重试", html, StringComparison.Ordinal);
        Assert.Contains("id=\"launchHelpButton\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"查看启动任务说明\"", html, StringComparison.Ordinal);
        Assert.Contains("function iosInfo(title, message)", html, StringComparison.Ordinal);
        Assert.Contains("手机端无法选择或修改场馆和座位", html, StringComparison.Ordinal);
        Assert.DoesNotContain("http://cdn", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://cdn", html, StringComparison.OrdinalIgnoreCase);
    }
}
