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
        Assert.DoesNotContain("__MOBILE_CONTROL_BLUETOOTH_SCANNER_JS__", html, StringComparison.Ordinal);
        Assert.Contains("root.MobileControlBluetoothScanner = api", html, StringComparison.Ordinal);
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

    [Fact]
    public void Build_ContainsClientOnlyIBeaconScannerContract()
    {
        var html = MobileControlMobilePage.Build("secret-token");

        Assert.Contains("data-tab=\"bluetooth\"", html, StringComparison.Ordinal);
        Assert.Contains("<span>蓝牙扫描</span>", html, StringComparison.Ordinal);
        Assert.Contains("data-page=\"bluetooth\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"bluetoothCompatibilitySection\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"bluetoothScanSection\" hidden", html, StringComparison.Ordinal);
        Assert.Contains("id=\"bluetoothResultsSection\" hidden", html, StringComparison.Ordinal);
        Assert.Contains("id=\"bluetoothSupportIssues\" aria-live=\"polite\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"bluetoothScanStatus\" aria-live=\"polite\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"bluetoothResultList\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"bluetoothResultList\" aria-live", html, StringComparison.Ordinal);
        Assert.Contains("id=\"bluetoothCompatibilityMode\" type=\"checkbox\" role=\"switch\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"bluetoothCompatibilityMode\" type=\"checkbox\" role=\"switch\" checked", html, StringComparison.Ordinal);

        Assert.Contains("window.isSecureContext === true", html, StringComparison.Ordinal);
        Assert.Contains("Boolean(navigator.bluetooth)", html, StringComparison.Ordinal);
        Assert.Contains("typeof navigator.bluetooth.requestLEScan === 'function'", html, StringComparison.Ordinal);
        Assert.Contains("navigator.bluetooth.getAvailability()", html, StringComparison.Ordinal);
        Assert.Contains("issues.push({", html, StringComparison.Ordinal);
        Assert.Contains("issues.filter(issue => issue.blocking)", html, StringComparison.Ordinal);
        Assert.DoesNotContain("if (!secureContextAvailable) return", html, StringComparison.Ordinal);
        Assert.DoesNotContain("if (!webBluetoothAvailable) return", html, StringComparison.Ordinal);
        Assert.Contains("const supportPassed = bluetoothSupportReady && !bluetoothSupportChecking", html, StringComparison.Ordinal);
        Assert.Contains("$('bluetoothCompatibilitySection').hidden = supportPassed", html, StringComparison.Ordinal);
        Assert.Contains("$('bluetoothScanSection').hidden = !supportPassed", html, StringComparison.Ordinal);
        Assert.Contains("$('bluetoothResultsSection').hidden = !supportPassed", html, StringComparison.Ordinal);
        Assert.Contains("updateBluetoothSectionVisibility()", html, StringComparison.Ordinal);

        Assert.Contains("await navigator.bluetooth.requestLEScan({", html, StringComparison.Ordinal);
        Assert.Contains("companyIdentifier: 0x004C", html, StringComparison.Ordinal);
        Assert.Contains("dataPrefix: new Uint8Array([0x02, 0x15])", html, StringComparison.Ordinal);
        Assert.Contains("acceptAllAdvertisements: true", html, StringComparison.Ordinal);
        Assert.Contains("keepRepeatedDevices: true", html, StringComparison.Ordinal);
        Assert.Contains("const compatibilityMode = $('bluetoothCompatibilityMode').checked", html, StringComparison.Ordinal);
        Assert.Contains("handleBluetoothAdvertisement(event, compatibilityMode)", html, StringComparison.Ordinal);
        Assert.Contains("for (const [companyIdentifier, candidate] of event.manufacturerData.entries())", html, StringComparison.Ordinal);
        Assert.Contains("const BLUETOOTH_SCAN_DURATION_MS = 30_000", html, StringComparison.Ordinal);
        Assert.Contains("setTimeout(() =>", html, StringComparison.Ordinal);
        Assert.Contains("stopBluetoothScan('timeout')", html, StringComparison.Ordinal);
        Assert.Contains("const BLUETOOTH_RESULT_RENDER_INTERVAL_MS = 250", html, StringComparison.Ordinal);
        Assert.Contains("const DEFAULT_MAX_RECORDS = 100", html, StringComparison.Ordinal);
        Assert.Contains("scheduleBluetoothResultsRender()", html, StringComparison.Ordinal);
        Assert.Contains("最多显示 ${BLUETOOTH_MAX_RECORDS} 个不同广播来源", html, StringComparison.Ordinal);

        Assert.Contains("data.byteLength === 23", html, StringComparison.Ordinal);
        Assert.Contains("data.getUint8(0) === IBEACON_DATA_PREFIX[0]", html, StringComparison.Ordinal);
        Assert.Contains("for (let offset = 2; offset <= 17; offset++)", html, StringComparison.Ordinal);
        Assert.Contains("major: match.data.getUint16(18, false)", html, StringComparison.Ordinal);
        Assert.Contains("minor: match.data.getUint16(20, false)", html, StringComparison.Ordinal);
        Assert.Contains("txPower: match.data.getInt8(22)", html, StringComparison.Ordinal);
        Assert.Contains("companyIdentifier: match.companyIdentifier", html, StringComparison.Ordinal);
        Assert.Contains("isStandardIBeacon: match.isStandardIBeacon", html, StringComparison.Ordinal);
        Assert.Contains("return `${record.uuid}|${record.major}|${record.minor}|${record.companyIdentifier}`", html, StringComparison.Ordinal);
        Assert.Contains("seenCount: existing.seenCount + 1", html, StringComparison.Ordinal);
        Assert.Contains("rightRssi - leftRssi || right.lastSeen - left.lastSeen", html, StringComparison.Ordinal);
        Assert.Contains("标准 iBeacon", html, StringComparison.Ordinal);
        Assert.Contains("兼容识别", html, StringComparison.Ordinal);

        Assert.Contains("stopBluetoothScan('manual')", html, StringComparison.Ordinal);
        Assert.Contains("stopBluetoothScan('tab-leave')", html, StringComparison.Ordinal);
        Assert.Contains("stopBluetoothScan('hidden')", html, StringComparison.Ordinal);
        Assert.Contains("window.addEventListener('pagehide'", html, StringComparison.Ordinal);
        Assert.Contains("generation !== bluetoothScanGeneration", html, StringComparison.Ordinal);
        Assert.Contains("try { scan.stop(); } catch {}", html, StringComparison.Ordinal);
        Assert.Contains("offerCompatibilityRescan(completedGeneration)", html, StringComparison.Ordinal);
        Assert.Contains("reason === 'timeout' || reason === 'manual'", html, StringComparison.Ordinal);
        Assert.Contains("标准扫描没有发现 iBeacon，是否启用兼容扫描模式并立即重新扫描", html, StringComparison.Ordinal);
        Assert.Contains("$('bluetoothCompatibilityMode').checked = true", html, StringComparison.Ordinal);
        Assert.Contains("await startBluetoothScan()", html, StringComparison.Ordinal);

        Assert.Contains("NotAllowedError:", html, StringComparison.Ordinal);
        Assert.Contains("NotFoundError:", html, StringComparison.Ordinal);
        Assert.Contains("Android 设置中允许 Chrome 使用“附近的设备”", html, StringComparison.Ordinal);
        Assert.Contains("SecurityError:", html, StringComparison.Ordinal);
        Assert.Contains("InvalidStateError:", html, StringComparison.Ordinal);
        Assert.Contains("NotSupportedError:", html, StringComparison.Ordinal);
        Assert.Contains("AbortError:", html, StringComparison.Ordinal);
        Assert.Contains("TypeError:", html, StringComparison.Ordinal);

        Assert.DoesNotContain("localStorage", html, StringComparison.Ordinal);
        Assert.DoesNotContain("sessionStorage", html, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/bluetooth", html, StringComparison.Ordinal);
    }
}
