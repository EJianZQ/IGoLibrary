using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Desktop.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace IGoLibrary.Ex.Tests;

public sealed class MobileControlNetworkModeWorkflowTests
{
    [Fact]
    public async Task ReconcilePersistedModeAsync_MissingCloudflaredFallsBackAndPersistsLocal()
    {
        var exposure = new FakeNetworkExposureManager();
        var locator = new StubCloudflaredLocator(IsAvailable: false);
        var dialog = new StubCloudflaredDialog();
        var settingsStore = new FakeSettingsService(AppSettings.Default with
        {
            MobileControl = AppSettings.Default.MobileControl with
            {
                NetworkMode = MobileControlNetworkMode.CloudflareTunnel
            }
        });
        var workflow = CreateWorkflow(
            exposure,
            locator,
            dialog,
            new SettingsWorkflowService(settingsStore));

        var result = await workflow.ReconcilePersistedModeAsync(
            MobileControlNetworkMode.CloudflareTunnel);

        Assert.Equal(MobileControlNetworkMode.LocalNetwork, result);
        Assert.Equal(
            MobileControlNetworkMode.LocalNetwork,
            settingsStore.CurrentSettings.MobileControl.NetworkMode);
        Assert.Equal(1, locator.FindCalls);
        Assert.Equal(0, dialog.ConfirmCalls);
        Assert.Equal(0, dialog.InstallCalls);
    }

    [Fact]
    public async Task ReconcilePersistedModeAsync_AvailableCloudflaredKeepsTunnel()
    {
        var exposure = new FakeNetworkExposureManager();
        var locator = new StubCloudflaredLocator(IsAvailable: true);
        var settingsStore = new FakeSettingsService(AppSettings.Default with
        {
            MobileControl = AppSettings.Default.MobileControl with
            {
                NetworkMode = MobileControlNetworkMode.CloudflareTunnel
            }
        });
        var workflow = CreateWorkflow(
            exposure,
            locator,
            new StubCloudflaredDialog(),
            new SettingsWorkflowService(settingsStore));

        var result = await workflow.ReconcilePersistedModeAsync(
            MobileControlNetworkMode.CloudflareTunnel);

        Assert.Equal(MobileControlNetworkMode.CloudflareTunnel, result);
        Assert.Equal(
            MobileControlNetworkMode.CloudflareTunnel,
            settingsStore.CurrentSettings.MobileControl.NetworkMode);
        Assert.Equal(1, locator.FindCalls);
    }

    [Fact]
    public async Task ApplyAsync_AvailableCloudflaredSwitchesWithoutPrompt()
    {
        var exposure = new FakeNetworkExposureManager();
        var locator = new StubCloudflaredLocator(IsAvailable: true);
        var dialog = new StubCloudflaredDialog();
        var workflow = CreateWorkflow(exposure, locator, dialog);

        var result = await workflow.ApplyAsync(MobileControlNetworkMode.CloudflareTunnel);

        Assert.Equal(MobileControlNetworkMode.CloudflareTunnel, result);
        Assert.Equal(MobileControlNetworkMode.CloudflareTunnel, exposure.CurrentMode);
        Assert.Equal(0, dialog.ConfirmCalls);
        Assert.Equal(0, dialog.InstallCalls);
    }

    [Fact]
    public async Task ApplyAsync_UserDeclinesDownloadKeepsLocalWithoutPersistingTunnel()
    {
        var exposure = new FakeNetworkExposureManager();
        var locator = new StubCloudflaredLocator(IsAvailable: false);
        var dialog = new StubCloudflaredDialog { ConfirmResult = false };
        var workflow = CreateWorkflow(exposure, locator, dialog);

        var result = await workflow.ApplyAsync(MobileControlNetworkMode.CloudflareTunnel);

        Assert.Equal(MobileControlNetworkMode.LocalNetwork, result);
        Assert.Equal(MobileControlNetworkMode.LocalNetwork, exposure.CurrentMode);
        Assert.Equal(1, dialog.ConfirmCalls);
        Assert.Equal(0, dialog.InstallCalls);
    }

    [Theory]
    [InlineData((int)CloudflaredInstallDialogOutcome.Canceled)]
    [InlineData((int)CloudflaredInstallDialogOutcome.Failed)]
    public async Task ApplyAsync_UnfinishedInstallKeepsLocal(int outcomeValue)
    {
        var outcome = (CloudflaredInstallDialogOutcome)outcomeValue;
        var exposure = new FakeNetworkExposureManager();
        var locator = new StubCloudflaredLocator(IsAvailable: false);
        var dialog = new StubCloudflaredDialog
        {
            ConfirmResult = true,
            InstallResult = new CloudflaredInstallDialogResult(outcome, "not installed")
        };
        var workflow = CreateWorkflow(exposure, locator, dialog);

        var result = await workflow.ApplyAsync(MobileControlNetworkMode.CloudflareTunnel);

        Assert.Equal(MobileControlNetworkMode.LocalNetwork, result);
        Assert.Equal(MobileControlNetworkMode.LocalNetwork, exposure.CurrentMode);
        Assert.Equal(1, dialog.InstallCalls);
    }

    [Fact]
    public async Task ApplyAsync_SuccessfulInstallRevalidatesAndAutomaticallySwitches()
    {
        var exposure = new FakeNetworkExposureManager();
        var locator = new StubCloudflaredLocator(IsAvailable: false);
        var dialog = new StubCloudflaredDialog
        {
            ConfirmResult = true,
            InstallResult = new CloudflaredInstallDialogResult(
                CloudflaredInstallDialogOutcome.Installed,
                "installed"),
            OnInstall = () => locator.IsAvailable = true
        };
        var workflow = CreateWorkflow(exposure, locator, dialog);

        var result = await workflow.ApplyAsync(MobileControlNetworkMode.CloudflareTunnel);

        Assert.Equal(MobileControlNetworkMode.CloudflareTunnel, result);
        Assert.Equal(MobileControlNetworkMode.CloudflareTunnel, exposure.CurrentMode);
        Assert.Equal(2, locator.FindCalls);
    }

    private static MobileControlNetworkModeWorkflow CreateWorkflow(
        INetworkExposureManager exposure,
        ICloudflaredToolLocator locator,
        ICloudflaredDownloadDialogService dialog,
        ISettingsWorkflowService? settingsWorkflowService = null)
        => new(
            exposure,
            locator,
            dialog,
            settingsWorkflowService ?? new SettingsWorkflowService(
                new FakeSettingsService(AppSettings.Default)),
            new ActivityLogService(),
            NullLogger<MobileControlNetworkModeWorkflow>.Instance);

    private sealed class StubCloudflaredLocator(bool IsAvailable) : ICloudflaredToolLocator
    {
        public bool IsAvailable { get; set; } = IsAvailable;

        public int FindCalls { get; private set; }

        public Task<CloudflaredToolAvailability> FindAsync(
            CancellationToken cancellationToken = default)
        {
            FindCalls++;
            return Task.FromResult(new CloudflaredToolAvailability(
                IsAvailable,
                IsAvailable ? "cloudflared" : null,
                Asset(),
                IsAvailable ? CloudflaredToolSource.UserInstalled : CloudflaredToolSource.None));
        }

        public Task<bool> ValidateDirectoryAsync(
            string directory,
            CancellationToken cancellationToken = default)
            => Task.FromResult(IsAvailable);

        public void Invalidate()
        {
        }
    }

    private sealed class StubCloudflaredDialog : ICloudflaredDownloadDialogService
    {
        public bool ConfirmResult { get; set; }

        public CloudflaredInstallDialogResult InstallResult { get; set; } = new(
            CloudflaredInstallDialogOutcome.Installed,
            "installed");

        public Action? OnInstall { get; set; }

        public int ConfirmCalls { get; private set; }

        public int InstallCalls { get; private set; }

        public Task<bool> ConfirmDownloadAsync(
            CloudflaredAssetDescriptor asset,
            CancellationToken cancellationToken = default)
        {
            ConfirmCalls++;
            return Task.FromResult(ConfirmResult);
        }

        public Task<CloudflaredInstallDialogResult> ShowInstallAsync(
            CancellationToken cancellationToken = default)
        {
            InstallCalls++;
            OnInstall?.Invoke();
            return Task.FromResult(InstallResult);
        }
    }

    private static CloudflaredAssetDescriptor Asset()
        => new(
            "2026.7.0",
            "win-x64",
            "cloudflared.exe",
            "binary",
            1,
            new string('0', 64),
            "cloudflared.exe",
            1,
            new string('0', 64),
            new Uri("https://github.com/cloudflare/cloudflared/releases/download/2026.7.0/cloudflared.exe"));
}
