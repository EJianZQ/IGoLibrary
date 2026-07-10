using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Domain.Enums;

namespace IGoLibrary.Ex.Desktop.ViewModels;

public sealed partial class LanCookieRelayViewModel(
    ILanCookieRelayService lanCookieRelayService,
    IQrCodeImageFactory qrCodeImageFactory,
    IActivityLogService activityLogService,
    INotificationService notificationService) : ViewModelBase
{
    private Guid? _activeLanCookieRelaySessionId;
    private Func<string, Task<LanCookieRelayLinkSubmitResult>>? _submitLinkAsync;

    [ObservableProperty]
    private bool isLanCookieRelayDialogOpen;

    [ObservableProperty]
    private bool isLanCookieRelayRunning;

    public bool CanStartLanCookieRelay => !IsLanCookieRelayRunning;

    public string LanCookieRelayCloseButtonText => IsLanCookieRelayRunning ? "停止并关闭" : "关闭";

    [ObservableProperty]
    private string lanCookieRelayUrlText = string.Empty;

    [ObservableProperty]
    private string lanCookieRelayStatusText = "局域网快传尚未启动";

    [ObservableProperty]
    private bool showLanCookieRelayStartedStatusIcon;

    [ObservableProperty]
    private string lanCookieRelayDialogTitle = "登录授权快传";

    [ObservableProperty]
    private IImage? lanCookieRelayQrImage;

    public bool HasLanCookieRelayQrImage => LanCookieRelayQrImage is not null;

    public bool HasNoLanCookieRelayQrImage => !HasLanCookieRelayQrImage;

    public void Configure(Func<string, Task<LanCookieRelayLinkSubmitResult>> submitLinkAsync)
    {
        _submitLinkAsync = submitLinkAsync;
    }

    public void ApplyStopped(LanCookieRelayStoppedEventArgs e)
    {
        if (_activeLanCookieRelaySessionId is not null &&
            _activeLanCookieRelaySessionId != e.SessionId)
        {
            return;
        }

        _activeLanCookieRelaySessionId = null;
        IsLanCookieRelayRunning = false;
        ShowLanCookieRelayStartedStatusIcon = false;

        if (e.Reason == LanCookieRelayStopReason.Timeout)
        {
            LanCookieRelayStatusText = e.Message ?? "局域网快传已超时关闭";
            return;
        }

        if (e.Reason == LanCookieRelayStopReason.Failed)
        {
            LanCookieRelayStatusText = e.Message ?? "局域网快传已异常关闭";
            return;
        }

        if (e.Reason == LanCookieRelayStopReason.Manual && IsLanCookieRelayDialogOpen)
        {
            LanCookieRelayStatusText = "局域网快传已停止";
        }
    }

    public async Task StopSessionAsync(bool closeDialog)
    {
        try
        {
            await lanCookieRelayService.StopAsync();
        }
        catch (Exception ex)
        {
            activityLogService.Write(LogEntryKind.Warning, "Auth", $"停止局域网快传失败：{ex.Message}");
        }
        finally
        {
            IsLanCookieRelayRunning = false;
            ShowLanCookieRelayStartedStatusIcon = false;
            _activeLanCookieRelaySessionId = null;
            if (closeDialog)
            {
                IsLanCookieRelayDialogOpen = false;
                LanCookieRelayUrlText = string.Empty;
                LanCookieRelayQrImage = null;
            }
        }
    }

    partial void OnIsLanCookieRelayRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(CanStartLanCookieRelay));
        OnPropertyChanged(nameof(LanCookieRelayCloseButtonText));
    }

    partial void OnLanCookieRelayQrImageChanged(IImage? value)
    {
        OnPropertyChanged(nameof(HasLanCookieRelayQrImage));
        OnPropertyChanged(nameof(HasNoLanCookieRelayQrImage));
    }

    [RelayCommand]
    private async Task StartLanCookieRelayAsync()
    {
        var handler = _submitLinkAsync
            ?? throw new InvalidOperationException("局域网快传尚未完成初始化");
        await StartSessionAsync(LanAuthLinkRelayPurpose.GraphQlSession, handler);
    }

    public async Task StartSessionAsync(
        LanAuthLinkRelayPurpose purpose,
        Func<string, Task<LanCookieRelayLinkSubmitResult>> submitLinkAsync)
    {
        if (IsLanCookieRelayRunning)
        {
            await StopSessionAsync(closeDialog: false);
        }

        try
        {
            IsLanCookieRelayDialogOpen = true;
            LanCookieRelayDialogTitle = purpose == LanAuthLinkRelayPurpose.RemoteCheckIn
                ? "远程签到授权快传"
                : "登录授权快传";
            LanCookieRelayStatusText = "正在启动局域网快传...";
            ShowLanCookieRelayStartedStatusIcon = false;
            LanCookieRelayUrlText = string.Empty;
            LanCookieRelayQrImage = null;

            var session = await lanCookieRelayService.StartAsync(
                (link, cancellationToken) => SubmitLanCookieRelayLinkAsync(link, submitLinkAsync, cancellationToken),
                purpose);
            _activeLanCookieRelaySessionId = session.SessionId;
            LanCookieRelayUrlText = session.Url.ToString();
            LanCookieRelayQrImage = qrCodeImageFactory.Create(LanCookieRelayUrlText);
            IsLanCookieRelayRunning = true;
            LanCookieRelayStatusText = $"服务已启动，监听端口 {session.Port}";
            ShowLanCookieRelayStartedStatusIcon = true;
            activityLogService.Write(LogEntryKind.Info, "Auth", $"局域网 Cookie 快传已启动：{LanCookieRelayUrlText}");
        }
        catch (Exception ex)
        {
            IsLanCookieRelayRunning = false;
            ShowLanCookieRelayStartedStatusIcon = false;
            _activeLanCookieRelaySessionId = null;
            LanCookieRelayStatusText = $"局域网快传启动失败：{ex.Message}";
            activityLogService.Write(LogEntryKind.Error, "Auth", $"局域网快传启动失败：{ex.Message}");
            await notificationService.ShowWarningAsync("局域网快传启动失败", ex.Message);
        }
    }

    [RelayCommand]
    private async Task CloseLanCookieRelayAsync()
    {
        await StopSessionAsync(closeDialog: true);
    }

    private async Task<LanCookieRelaySubmitResult> SubmitLanCookieRelayLinkAsync(
        string linkText,
        Func<string, Task<LanCookieRelayLinkSubmitResult>> submitLinkAsync,
        CancellationToken cancellationToken)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            return await SubmitLanCookieRelayLinkOnUiThreadAsync(linkText, submitLinkAsync);
        }

        var completion = new TaskCompletionSource<LanCookieRelaySubmitResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                completion.SetResult(await SubmitLanCookieRelayLinkOnUiThreadAsync(linkText, submitLinkAsync));
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        });

        return await completion.Task.WaitAsync(cancellationToken);
    }

    private async Task<LanCookieRelaySubmitResult> SubmitLanCookieRelayLinkOnUiThreadAsync(
        string linkText,
        Func<string, Task<LanCookieRelayLinkSubmitResult>> submitLinkAsync)
    {
        LanCookieRelayStatusText = "已收到手机提交，正在解析授权链接...";
        ShowLanCookieRelayStartedStatusIcon = false;
        var parseResult = await submitLinkAsync(linkText.Trim());
        if (parseResult.Authenticated)
        {
            LanCookieRelayStatusText = "授权成功，局域网快传已完成";
            IsLanCookieRelayDialogOpen = false;
            return LanCookieRelaySubmitResult.Succeeded(parseResult.Message);
        }

        LanCookieRelayStatusText = parseResult.Message;
        try
        {
            await notificationService.ShowWarningAsync("局域网快传失败", parseResult.Message);
        }
        catch (Exception ex)
        {
            activityLogService.Write(LogEntryKind.Warning, "Auth", $"显示局域网快传失败通知失败：{ex.Message}");
        }

        return LanCookieRelaySubmitResult.Failed(parseResult.Message);
    }
}

public sealed record LanCookieRelayLinkSubmitResult(bool Authenticated, string Message);
