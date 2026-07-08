using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;

namespace IGoLibrary.Ex.Desktop.ViewModels;

public partial class MainWindowWorkflowViewModel
{
    public bool IsMobileControlRunning
    {
        get => MobileControl.IsMobileControlRunning;
        set => MobileControl.IsMobileControlRunning = value;
    }

    public bool IsMobileControlAutoStartEnabled
    {
        get => MobileControl.IsMobileControlAutoStartEnabled;
        set
        {
            MobileControl.IsMobileControlAutoStartEnabled = value;
        }
    }

    public bool IsMobileControlStopped => MobileControl.IsMobileControlStopped;

    public string MobileControlToggleButtonText => MobileControl.MobileControlToggleButtonText;

    public int MobileControlPort
    {
        get => MobileControl.MobileControlPort;
        set => MobileControl.MobileControlPort = value;
    }

    public string MobileControlStatusText
    {
        get => MobileControl.MobileControlStatusText;
        set => MobileControl.MobileControlStatusText = value;
    }

    public string MobileControlUrlText
    {
        get => MobileControl.MobileControlUrlText;
        set => MobileControl.MobileControlUrlText = value;
    }

    public string MobileControlHostText
    {
        get => MobileControl.MobileControlHostText;
        set => MobileControl.MobileControlHostText = value;
    }

    public string MobileControlAccessTokenText
    {
        get => MobileControl.MobileControlAccessTokenText;
        set => MobileControl.MobileControlAccessTokenText = value;
    }

    public string MobileControlAccessTokenFullText => MobileControl.MobileControlAccessTokenFullText;

    public int MobileControlConnectedDeviceCount => MobileControl.MobileControlConnectedDeviceCount;

    public string MobileControlConnectedDeviceCountText => MobileControl.MobileControlConnectedDeviceCountText;

    public IImage? MobileControlQrCode
    {
        get => MobileControl.MobileControlQrCode;
        set => MobileControl.MobileControlQrCode = value;
    }

    public bool IsMobileControlDetailsOpen
    {
        get => MobileControl.IsMobileControlDetailsOpen;
        set => MobileControl.IsMobileControlDetailsOpen = value;
    }

    public bool HasMobileControlQrCode => MobileControl.HasMobileControlQrCode;

    public bool HasNoMobileControlQrCode => MobileControl.HasNoMobileControlQrCode;

    public IAsyncRelayCommand ToggleMobileControlCommand => MobileControl.ToggleMobileControlCommand;

    public IAsyncRelayCommand StartMobileControlCommand => MobileControl.StartMobileControlCommand;

    public IAsyncRelayCommand StopMobileControlCommand => MobileControl.StopMobileControlCommand;

    public IAsyncRelayCommand RandomizeMobileControlPortCommand => MobileControl.RandomizeMobileControlPortCommand;

    public IAsyncRelayCommand ResetMobileControlAccessTokenCommand => MobileControl.ResetMobileControlAccessTokenCommand;

    public IRelayCommand OpenMobileControlDetailsCommand => MobileControl.OpenMobileControlDetailsCommand;

    public IRelayCommand CloseMobileControlDetailsCommand => MobileControl.CloseMobileControlDetailsCommand;

    private Task InitializeMobileControlAsync(CancellationToken cancellationToken = default)
    {
        return MobileControl.InitializeAsync(cancellationToken);
    }

    private async Task StartMobileControlAutomaticallyAsync(CancellationToken cancellationToken = default)
    {
        await MobileControl.StartAutomaticallyIfEnabledAsync(cancellationToken);
    }

    private void ConfigureMobileControlPropertyBridge(ViewModelPropertyBridge propertyBridge)
    {
        propertyBridge.ForwardSame(
            MobileControl,
            nameof(MobileControl.IsMobileControlRunning),
            nameof(MobileControl.IsMobileControlAutoStartEnabled),
            nameof(MobileControl.IsMobileControlStopped),
            nameof(MobileControl.MobileControlToggleButtonText),
            nameof(MobileControl.MobileControlPort),
            nameof(MobileControl.MobileControlStatusText),
            nameof(MobileControl.MobileControlUrlText),
            nameof(MobileControl.MobileControlHostText),
            nameof(MobileControl.MobileControlAccessTokenText),
            nameof(MobileControl.MobileControlAccessTokenFullText),
            nameof(MobileControl.MobileControlConnectedDeviceCount),
            nameof(MobileControl.MobileControlConnectedDeviceCountText),
            nameof(MobileControl.MobileControlQrCode),
            nameof(MobileControl.IsMobileControlDetailsOpen),
            nameof(MobileControl.HasMobileControlQrCode),
            nameof(MobileControl.HasNoMobileControlQrCode));
    }
}
