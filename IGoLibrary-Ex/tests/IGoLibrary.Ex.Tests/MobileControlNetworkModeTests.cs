using System.Text.Json;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Infrastructure.Persistence;

namespace IGoLibrary.Ex.Tests;

public sealed class MobileControlNetworkModeTests
{
    [Fact]
    public void LegacySettingsWithoutNetworkMode_MigrateToLocalNetwork()
    {
        const string json =
            """
            {
              "mobileControl": {
                "port": 9527,
                "accessToken": "token",
                "autoStart": true
              }
            }
            """;

        var migrated = SqliteSettingsRepository.MigrateAppSettingsJson(json);
        var settings = Assert.IsType<AppSettings>(JsonSerializer.Deserialize<AppSettings>(migrated, AppJson.Default));

        Assert.Equal(MobileControlNetworkMode.LocalNetwork, settings.MobileControl.NetworkMode);
        Assert.Equal(CloudflareTunnelProxyMode.Auto, settings.MobileControl.TunnelProxyMode);
        Assert.Equal(string.Empty, settings.MobileControl.TunnelManualProxyUrl);
        Assert.True(settings.MobileControl.FallbackToLocalNetworkOnTunnelFailure);
        Assert.False(settings.MobileControl.ClashMihomoCompatibilityEnabled);
        Assert.Equal(string.Empty, settings.MobileControl.ClashMihomoConfigPath);
        Assert.Equal("DIRECT", settings.MobileControl.ClashMihomoRoutePolicy);
        Assert.Contains("\"networkMode\": 0", migrated);
        Assert.Contains("\"tunnelProxyMode\": 0", migrated);
        Assert.Contains("\"tunnelManualProxyUrl\": \"\"", migrated);
        Assert.Contains("\"fallbackToLocalNetworkOnTunnelFailure\": true", migrated);
        Assert.Contains("\"clashMihomoCompatibilityEnabled\": false", migrated);
        Assert.Contains("\"clashMihomoRoutePolicy\": \"DIRECT\"", migrated);
    }

    [Fact]
    public void InvalidPersistedNetworkMode_IsNormalizedToLocalNetwork()
    {
        const string json =
            """
            {
              "mobileControl": {
                "port": 9527,
                "accessToken": "token",
                "autoStart": false,
                "networkMode": 99
              }
            }
            """;

        var migrated = SqliteSettingsRepository.MigrateAppSettingsJson(json);
        var settings = Assert.IsType<AppSettings>(JsonSerializer.Deserialize<AppSettings>(migrated, AppJson.Default));

        Assert.Equal(MobileControlNetworkMode.LocalNetwork, settings.MobileControl.NetworkMode);
    }

    [Fact]
    public void AppSettingsSerialization_PreservesCloudflareTunnelMode()
    {
        var expected = AppSettings.Default with
        {
            MobileControl = new MobileControlSettings(
                9527,
                "token",
                NetworkMode: MobileControlNetworkMode.CloudflareTunnel)
        };

        var json = JsonSerializer.Serialize(expected, AppJson.Default);
        var actual = Assert.IsType<AppSettings>(JsonSerializer.Deserialize<AppSettings>(json, AppJson.Default));

        Assert.Equal(MobileControlNetworkMode.CloudflareTunnel, actual.MobileControl.NetworkMode);
        Assert.Contains("\"networkMode\": 1", json);
    }

    [Fact]
    public async Task SettingsWorkflowService_SaveNetworkModeOnlyChangesNetworkMode()
    {
        var settingsService = new FakeSettingsService(AppSettings.Default with
        {
            MobileControl = new MobileControlSettings(
                9527,
                "token",
                true,
                TunnelProxyMode: CloudflareTunnelProxyMode.ManualHttpProxy,
                TunnelManualProxyUrl: "http://127.0.0.1:7897")
        });
        var service = new SettingsWorkflowService(settingsService);

        var updated = await service.SaveMobileControlNetworkModeAsync(
            MobileControlNetworkMode.CloudflareTunnel);

        Assert.Equal(9527, updated.Port);
        Assert.Equal("token", updated.AccessToken);
        Assert.True(updated.AutoStart);
        Assert.Equal(MobileControlNetworkMode.CloudflareTunnel, updated.NetworkMode);
        Assert.Equal(CloudflareTunnelProxyMode.ManualHttpProxy, updated.TunnelProxyMode);
        Assert.Equal("http://127.0.0.1:7897", updated.TunnelManualProxyUrl);
        Assert.Equal(updated, settingsService.CurrentSettings.MobileControl);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(99)]
    public async Task SettingsWorkflowService_InvalidNetworkModeFallsBackToLocalNetwork(int rawMode)
    {
        var settingsService = new FakeSettingsService(AppSettings.Default with
        {
            MobileControl = new MobileControlSettings(
                9527,
                "token",
                NetworkMode: MobileControlNetworkMode.CloudflareTunnel)
        });
        var service = new SettingsWorkflowService(settingsService);

        var updated = await service.SaveMobileControlNetworkModeAsync((MobileControlNetworkMode)rawMode);

        Assert.Equal(MobileControlNetworkMode.LocalNetwork, updated.NetworkMode);
    }

    [Theory]
    [InlineData(CloudflareTunnelProxyMode.Auto)]
    [InlineData(CloudflareTunnelProxyMode.SystemProxy)]
    [InlineData(CloudflareTunnelProxyMode.ManualHttpProxy)]
    [InlineData(CloudflareTunnelProxyMode.Direct)]
    public async Task SettingsWorkflowService_ProxyModesRoundTrip(CloudflareTunnelProxyMode mode)
    {
        var settingsService = new FakeSettingsService(AppSettings.Default with
        {
            MobileControl = new MobileControlSettings(9527, "token")
        });
        var service = new SettingsWorkflowService(settingsService);

        var updated = await service.SaveCloudflareTunnelProxyAsync(mode, "http://127.0.0.1:7897/");

        Assert.Equal(mode, updated.TunnelProxyMode);
        Assert.Equal("http://127.0.0.1:7897", updated.TunnelManualProxyUrl);
    }

    [Fact]
    public async Task SettingsWorkflowService_TunnelFallbackSettingRoundTripsWithoutChangingNetworkMode()
    {
        var settingsService = new FakeSettingsService(AppSettings.Default with
        {
            MobileControl = new MobileControlSettings(
                9527,
                "token",
                NetworkMode: MobileControlNetworkMode.CloudflareTunnel)
        });
        var service = new SettingsWorkflowService(settingsService);

        var updated = await service.SaveCloudflareTunnelFallbackAsync(false);

        Assert.False(updated.FallbackToLocalNetworkOnTunnelFailure);
        Assert.Equal(MobileControlNetworkMode.CloudflareTunnel, updated.NetworkMode);
        Assert.False(settingsService.CurrentSettings.MobileControl.FallbackToLocalNetworkOnTunnelFailure);
    }

    [Fact]
    public void AppSettingsSerialization_PreservesDisabledTunnelFallback()
    {
        var expected = AppSettings.Default with
        {
            MobileControl = new MobileControlSettings(
                9527,
                "token",
                FallbackToLocalNetworkOnTunnelFailure: false)
        };

        var json = JsonSerializer.Serialize(expected, AppJson.Default);
        var actual = Assert.IsType<AppSettings>(JsonSerializer.Deserialize<AppSettings>(json, AppJson.Default));

        Assert.False(actual.MobileControl.FallbackToLocalNetworkOnTunnelFailure);
        Assert.Contains("\"fallbackToLocalNetworkOnTunnelFailure\": false", json);
    }

    [Fact]
    public async Task SettingsWorkflowService_ManualProxyRejectsUnsafeOrMalformedUrl()
    {
        var service = new SettingsWorkflowService(new FakeSettingsService(AppSettings.Default));

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SaveCloudflareTunnelProxyAsync(
                CloudflareTunnelProxyMode.ManualHttpProxy,
                "http://user:secret@127.0.0.1:7897/path?token=secret"));

        Assert.DoesNotContain("user:secret", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidPersistedProxySettings_NormalizeToAutoWithoutCredentials()
    {
        const string json =
            """
            {
              "mobileControl": {
                "port": 9527,
                "accessToken": "token",
                "autoStart": false,
                "networkMode": 0,
                "tunnelProxyMode": 99,
                "tunnelManualProxyUrl": "http://user:secret@127.0.0.1:7897"
              }
            }
            """;

        var migrated = SqliteSettingsRepository.MigrateAppSettingsJson(json);
        var settings = Assert.IsType<AppSettings>(JsonSerializer.Deserialize<AppSettings>(migrated, AppJson.Default));

        Assert.Equal(CloudflareTunnelProxyMode.Auto, settings.MobileControl.TunnelProxyMode);
        Assert.Equal(string.Empty, settings.MobileControl.TunnelManualProxyUrl);
        Assert.DoesNotContain("secret", migrated, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SettingsWorkflowService_ClashMihomoCompatibilityRoundTripsGenericOptions()
    {
        var service = new SettingsWorkflowService(new FakeSettingsService(AppSettings.Default));
        var configPath = Path.GetFullPath("custom-mihomo.yaml");

        var updated = await service.SaveClashMihomoCompatibilityAsync(
            true,
            configPath,
            "Cloudflare 专线");

        Assert.True(updated.ClashMihomoCompatibilityEnabled);
        Assert.Equal(configPath, updated.ClashMihomoConfigPath);
        Assert.Equal("Cloudflare 专线", updated.ClashMihomoRoutePolicy);
    }

    [Theory]
    [InlineData("relative-config.yaml", "DIRECT")]
    [InlineData("", "")]
    [InlineData("", "Group,WithComma")]
    [InlineData("", "Group#Comment")]
    public async Task SettingsWorkflowService_RejectsUnsafeCompatibilityOptions(
        string configPath,
        string routePolicy)
    {
        var service = new SettingsWorkflowService(new FakeSettingsService(AppSettings.Default));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SaveClashMihomoCompatibilityAsync(true, configPath, routePolicy));
    }

    [Fact]
    public void ClashMihomoConfigPath_WithInvalidPathCharactersReturnsFalse()
    {
        var invalidPath = Path.GetFullPath(".") + Path.DirectorySeparatorChar + "invalid\0config.yaml";

        var valid = MobileControlSettings.TryNormalizeClashMihomoConfigPath(
            invalidPath,
            out var normalized);

        Assert.False(valid);
        Assert.Equal(string.Empty, normalized);
    }
}
