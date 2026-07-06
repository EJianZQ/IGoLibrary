using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using IGoLibrary.Ex.Desktop.Services;

namespace IGoLibrary.Ex.Desktop.ViewModels;

public partial class MainWindowWorkflowViewModel
{
    private bool _lanCookieRelayServiceSubscribed;
    private bool _lanCookieRelayConfigured;

    public bool IsLanCookieRelayDialogOpen
    {
        get => LanCookieRelay.IsLanCookieRelayDialogOpen;
        set => LanCookieRelay.IsLanCookieRelayDialogOpen = value;
    }

    public bool IsLanCookieRelayRunning
    {
        get => LanCookieRelay.IsLanCookieRelayRunning;
        set => LanCookieRelay.IsLanCookieRelayRunning = value;
    }

    public bool CanStartLanCookieRelay => LanCookieRelay.CanStartLanCookieRelay;

    public string LanCookieRelayCloseButtonText => LanCookieRelay.LanCookieRelayCloseButtonText;

    public int SelectedLanCookieRelayStepIndex
    {
        get => LanCookieRelay.SelectedLanCookieRelayStepIndex;
        set => LanCookieRelay.SelectedLanCookieRelayStepIndex = value;
    }

    public bool IsLanCookieRelayAuthorizationQrMode => LanCookieRelay.IsLanCookieRelayAuthorizationQrMode;

    public bool IsLanCookieRelaySubmitQrMode => LanCookieRelay.IsLanCookieRelaySubmitQrMode;

    public bool CanGoToPreviousLanCookieRelayStep => LanCookieRelay.CanGoToPreviousLanCookieRelayStep;

    public bool CanGoToNextLanCookieRelayStep => LanCookieRelay.CanGoToNextLanCookieRelayStep;

    public string LanCookieRelayStepTitle => LanCookieRelay.LanCookieRelayStepTitle;

    public string LanCookieRelayUrlText
    {
        get => LanCookieRelay.LanCookieRelayUrlText;
        set => LanCookieRelay.LanCookieRelayUrlText = value;
    }

    public string LanCookieRelayStatusText
    {
        get => LanCookieRelay.LanCookieRelayStatusText;
        set => LanCookieRelay.LanCookieRelayStatusText = value;
    }

    public bool ShowLanCookieRelayStartedStatusIcon
    {
        get => LanCookieRelay.ShowLanCookieRelayStartedStatusIcon;
        set => LanCookieRelay.ShowLanCookieRelayStartedStatusIcon = value;
    }

    public IImage? LanCookieRelayQrImage
    {
        get => LanCookieRelay.LanCookieRelayQrImage;
        set => LanCookieRelay.LanCookieRelayQrImage = value;
    }

    public bool HasLanCookieRelayQrImage => LanCookieRelay.HasLanCookieRelayQrImage;

    public bool ShowLanCookieRelaySubmitQrImage => LanCookieRelay.ShowLanCookieRelaySubmitQrImage;

    public bool ShowLanCookieRelaySubmitQrLoading => LanCookieRelay.ShowLanCookieRelaySubmitQrLoading;

    public string LanCookieRelayStepHint => LanCookieRelay.LanCookieRelayStepHint;

    public IAsyncRelayCommand StartLanCookieRelayCommand
    {
        get
        {
            EnsureLanCookieRelayConfigured();
            return LanCookieRelay.StartLanCookieRelayCommand;
        }
    }

    public IAsyncRelayCommand CloseLanCookieRelayCommand
    {
        get
        {
            EnsureLanCookieRelayConfigured();
            return LanCookieRelay.CloseLanCookieRelayCommand;
        }
    }

    public IRelayCommand GoToNextLanCookieRelayStepCommand => LanCookieRelay.GoToNextLanCookieRelayStepCommand;

    public IRelayCommand GoToPreviousLanCookieRelayStepCommand => LanCookieRelay.GoToPreviousLanCookieRelayStepCommand;

    private void EnsureLanCookieRelayConfigured()
    {
        if (_lanCookieRelayConfigured)
        {
            return;
        }

        _lanCookieRelayConfigured = true;
        LanCookieRelay.Configure(async linkText =>
        {
            QrLinkText = linkText.Trim();
            var parseResult = await ParseCookieFromLinkAsync(QrLinkText, notifyOnInvalidLink: false);
            return new LanCookieRelayLinkSubmitResult(parseResult.Authenticated, parseResult.Message);
        });
    }

    private async Task StopLanCookieRelaySessionAsync(bool closeDialog)
    {
        await LanCookieRelay.StopSessionAsync(closeDialog);
    }

    private void OnLanCookieRelayStopped(object? sender, LanCookieRelayStoppedEventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            LanCookieRelay.ApplyStopped(e);
            return;
        }

        Dispatcher.UIThread.Post(() => LanCookieRelay.ApplyStopped(e));
    }

    private void ConfigureLanCookieRelayPropertyBridge(ViewModelPropertyBridge propertyBridge)
    {
        propertyBridge.ForwardSame(
            LanCookieRelay,
            nameof(LanCookieRelay.IsLanCookieRelayDialogOpen),
            nameof(LanCookieRelay.IsLanCookieRelayRunning),
            nameof(LanCookieRelay.CanStartLanCookieRelay),
            nameof(LanCookieRelay.LanCookieRelayCloseButtonText),
            nameof(LanCookieRelay.SelectedLanCookieRelayStepIndex),
            nameof(LanCookieRelay.IsLanCookieRelayAuthorizationQrMode),
            nameof(LanCookieRelay.IsLanCookieRelaySubmitQrMode),
            nameof(LanCookieRelay.CanGoToPreviousLanCookieRelayStep),
            nameof(LanCookieRelay.CanGoToNextLanCookieRelayStep),
            nameof(LanCookieRelay.LanCookieRelayStepTitle),
            nameof(LanCookieRelay.LanCookieRelayUrlText),
            nameof(LanCookieRelay.LanCookieRelayStatusText),
            nameof(LanCookieRelay.ShowLanCookieRelayStartedStatusIcon),
            nameof(LanCookieRelay.LanCookieRelayQrImage),
            nameof(LanCookieRelay.HasLanCookieRelayQrImage),
            nameof(LanCookieRelay.ShowLanCookieRelaySubmitQrImage),
            nameof(LanCookieRelay.ShowLanCookieRelaySubmitQrLoading),
            nameof(LanCookieRelay.LanCookieRelayStepHint));
    }
}
