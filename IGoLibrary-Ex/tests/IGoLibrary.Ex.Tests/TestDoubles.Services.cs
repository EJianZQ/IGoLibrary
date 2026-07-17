using System.Net;
using Avalonia.Controls;
using Avalonia.Media;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Application.Updates;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;
using IGoLibrary.Ex.Infrastructure.Notifications;
using MailKit.Security;
using MimeKit;

namespace IGoLibrary.Ex.Tests;

internal sealed class FakeNetworkExposureManager : INetworkExposureManager
{
    private readonly List<FakeNetworkExposureLease> _leases = [];

    public event EventHandler<NetworkModeChangedEventArgs>? ModeChanged;

    public MobileControlNetworkMode CurrentMode { get; private set; } = MobileControlNetworkMode.LocalNetwork;

    public CloudflareTunnelProxyMode TunnelProxyMode { get; private set; } = CloudflareTunnelProxyMode.Auto;

    public string TunnelManualProxyUrl { get; private set; } = string.Empty;

    public bool ClashMihomoCompatibilityEnabled { get; private set; }

    public string ClashMihomoConfigPath { get; private set; } = string.Empty;

    public string ClashMihomoRoutePolicy { get; private set; } = "DIRECT";

    public bool FallbackToLocalNetworkOnTunnelFailure { get; private set; } = true;

    public Uri TunnelBaseUri { get; set; } = new("https://unit-test.trycloudflare.com/");

    public Exception? SetModeException { get; set; }

    public void Initialize(
        MobileControlNetworkMode networkMode,
        CloudflareTunnelProxyMode tunnelProxyMode,
        string tunnelManualProxyUrl,
        bool clashMihomoCompatibilityEnabled = false,
        string clashMihomoConfigPath = "",
        string clashMihomoRoutePolicy = "DIRECT",
        bool fallbackToLocalNetworkOnTunnelFailure = true)
    {
        CurrentMode = MobileControlSettings.NormalizeNetworkMode(networkMode);
        TunnelProxyMode = MobileControlSettings.NormalizeTunnelProxyMode(tunnelProxyMode);
        TunnelManualProxyUrl = tunnelManualProxyUrl;
        ClashMihomoCompatibilityEnabled = clashMihomoCompatibilityEnabled;
        ClashMihomoConfigPath = clashMihomoConfigPath;
        ClashMihomoRoutePolicy = clashMihomoRoutePolicy;
        FallbackToLocalNetworkOnTunnelFailure = fallbackToLocalNetworkOnTunnelFailure;
    }

    public Task<MobileControlSettings> SetClashMihomoCompatibilityAsync(
        bool enabled,
        string configPath,
        string routePolicy,
        CancellationToken cancellationToken = default)
    {
        ClashMihomoCompatibilityEnabled = enabled;
        ClashMihomoConfigPath = configPath;
        ClashMihomoRoutePolicy = routePolicy;
        return Task.FromResult(new MobileControlSettings(
            NetworkMode: CurrentMode,
            TunnelProxyMode: TunnelProxyMode,
            TunnelManualProxyUrl: TunnelManualProxyUrl,
            ClashMihomoCompatibilityEnabled: enabled,
            ClashMihomoConfigPath: configPath,
            ClashMihomoRoutePolicy: routePolicy));
    }

    public Task<MobileControlSettings> SetCloudflareTunnelProxyAsync(
        CloudflareTunnelProxyMode proxyMode,
        string manualProxyUrl,
        CancellationToken cancellationToken = default)
    {
        TunnelProxyMode = MobileControlSettings.NormalizeTunnelProxyMode(proxyMode);
        TunnelManualProxyUrl = manualProxyUrl;
        return Task.FromResult(new MobileControlSettings(
            NetworkMode: CurrentMode,
            TunnelProxyMode: TunnelProxyMode,
            TunnelManualProxyUrl: TunnelManualProxyUrl));
    }

    public Task<MobileControlSettings> SetCloudflareTunnelFallbackAsync(
        bool fallbackToLocalNetworkOnTunnelFailure,
        CancellationToken cancellationToken = default)
    {
        FallbackToLocalNetworkOnTunnelFailure = fallbackToLocalNetworkOnTunnelFailure;
        return Task.FromResult(new MobileControlSettings(
            NetworkMode: CurrentMode,
            TunnelProxyMode: TunnelProxyMode,
            TunnelManualProxyUrl: TunnelManualProxyUrl,
            FallbackToLocalNetworkOnTunnelFailure: fallbackToLocalNetworkOnTunnelFailure));
    }

    public Task<MobileControlNetworkMode> SetModeAsync(
        MobileControlNetworkMode networkMode,
        CancellationToken cancellationToken = default)
    {
        if (SetModeException is not null)
        {
            return Task.FromException<MobileControlNetworkMode>(SetModeException);
        }

        ApplyModeChange(networkMode);
        return Task.FromResult(CurrentMode);
    }

    public void SimulateModeChange(MobileControlNetworkMode networkMode, string? message = null)
    {
        ApplyModeChange(networkMode, message);
    }

    private void ApplyModeChange(MobileControlNetworkMode networkMode, string? message = null)
    {
        CurrentMode = MobileControlSettings.NormalizeNetworkMode(networkMode);
        foreach (var lease in _leases)
        {
            lease.ApplyMode(CurrentMode, TunnelBaseUri);
        }

        ModeChanged?.Invoke(this, new NetworkModeChangedEventArgs(CurrentMode, message));
    }

    public Task<INetworkExposureLease> PublishAsync(
        NetworkExposurePurpose purpose,
        Uri lanUrl,
        string healthCheckPath,
        CancellationToken cancellationToken = default)
    {
        var lease = new FakeNetworkExposureLease(lanUrl, Remove);
        _leases.Add(lease);
        lease.ApplyMode(CurrentMode, TunnelBaseUri);
        return Task.FromResult<INetworkExposureLease>(lease);
    }

    public ValueTask DisposeAsync()
    {
        _leases.Clear();
        return ValueTask.CompletedTask;
    }

    private void Remove(FakeNetworkExposureLease lease)
    {
        _leases.Remove(lease);
    }

    private sealed class FakeNetworkExposureLease(
        Uri lanUrl,
        Action<FakeNetworkExposureLease> remove) : INetworkExposureLease
    {
        public event EventHandler<NetworkExposureChangedEventArgs>? EndpointChanged;

        public Guid Id { get; } = Guid.NewGuid();

        public Uri LanUrl { get; } = lanUrl;

        public Uri Url { get; private set; } = lanUrl;

        public MobileControlNetworkMode EffectiveMode { get; private set; } = MobileControlNetworkMode.LocalNetwork;

        public ValueTask DisposeAsync()
        {
            remove(this);
            return ValueTask.CompletedTask;
        }

        public void ApplyMode(MobileControlNetworkMode mode, Uri tunnelBaseUri)
        {
            EffectiveMode = mode;
            Url = mode == MobileControlNetworkMode.CloudflareTunnel
                ? new UriBuilder(LanUrl)
                {
                    Scheme = tunnelBaseUri.Scheme,
                    Host = tunnelBaseUri.Host,
                    Port = -1
                }.Uri
                : LanUrl;
            EndpointChanged?.Invoke(this, new NetworkExposureChangedEventArgs(Url, EffectiveMode));
        }
    }
}

internal sealed class FakeMobileControlNetworkModeWorkflow(
    INetworkExposureManager networkExposureManager) : IMobileControlNetworkModeWorkflow
{
    public int ReconcileCalls { get; private set; }

    public int ApplyCalls { get; private set; }

    public Exception? ApplyException { get; set; }

    public MobileControlNetworkMode? ReconciledMode { get; set; }

    public Task<MobileControlNetworkMode> ReconcilePersistedModeAsync(
        MobileControlNetworkMode persistedMode,
        CancellationToken cancellationToken = default)
    {
        ReconcileCalls++;
        return Task.FromResult(ReconciledMode ?? persistedMode);
    }

    public Task<MobileControlNetworkMode> ApplyAsync(
        MobileControlNetworkMode requestedMode,
        CancellationToken cancellationToken = default)
    {
        ApplyCalls++;
        return ApplyException is null
            ? networkExposureManager.SetModeAsync(requestedMode, cancellationToken)
            : Task.FromException<MobileControlNetworkMode>(ApplyException);
    }
}

internal sealed class FakeLanCookieRelayService : ILanCookieRelayService
{
    public event EventHandler<LanCookieRelayStoppedEventArgs>? Stopped;

    public event EventHandler<LanCookieRelayEndpointChangedEventArgs>? EndpointChanged;

    public int StartCalls { get; private set; }

    public int StopCalls { get; private set; }

    public Func<string, CancellationToken, Task<LanCookieRelaySubmitResult>>? SubmitHandler { get; private set; }

    public LanAuthLinkRelayPurpose LastPurpose { get; private set; }

    public LanCookieRelaySession NextSession { get; set; } = new(
        Guid.NewGuid(),
        new Uri("http://127.0.0.1:49152/?token=test-token"),
        new Uri("http://127.0.0.1:49152/?token=test-token"),
        "127.0.0.1",
        49152,
        DateTimeOffset.Now,
        TimeSpan.FromMinutes(10),
        MobileControlNetworkMode.LocalNetwork);

    public Exception? StartException { get; set; }

    public Task<LanCookieRelaySession> StartAsync(
        Func<string, CancellationToken, Task<LanCookieRelaySubmitResult>> submitHandler,
        CancellationToken cancellationToken = default)
        => StartAsync(submitHandler, LanAuthLinkRelayPurpose.GraphQlSession, cancellationToken);

    public Task<LanCookieRelaySession> StartAsync(
        Func<string, CancellationToken, Task<LanCookieRelaySubmitResult>> submitHandler,
        LanAuthLinkRelayPurpose purpose,
        CancellationToken cancellationToken = default)
    {
        StartCalls++;
        SubmitHandler = submitHandler;
        LastPurpose = purpose;
        if (StartException is not null)
        {
            throw StartException;
        }

        return Task.FromResult(NextSession);
    }

    public Task StopAsync(
        LanCookieRelayStopReason reason = LanCookieRelayStopReason.Manual,
        CancellationToken cancellationToken = default)
    {
        StopCalls++;
        RaiseStopped(reason);
        return Task.CompletedTask;
    }

    public async Task<LanCookieRelaySubmitResult> SubmitAsync(
        string linkText,
        CancellationToken cancellationToken = default)
    {
        if (SubmitHandler is null)
        {
            throw new InvalidOperationException("LAN cookie relay session has not been started.");
        }

        var result = await SubmitHandler(linkText, cancellationToken);
        if (result.Success)
        {
            RaiseStopped(LanCookieRelayStopReason.Submitted);
        }

        return result;
    }

    public void RaiseStopped(LanCookieRelayStopReason reason, string? message = null)
    {
        Stopped?.Invoke(this, new LanCookieRelayStoppedEventArgs(NextSession.SessionId, reason, message));
    }

    public void RaiseEndpointChanged(LanCookieRelaySession session)
    {
        NextSession = session;
        EndpointChanged?.Invoke(this, new LanCookieRelayEndpointChangedEventArgs(session));
    }
}

internal sealed class FakeMobileControlService : IMobileControlService
{
    public event EventHandler<MobileControlStoppedEventArgs>? Stopped;

    public event EventHandler<MobileControlDeviceCountChangedEventArgs>? DeviceCountChanged;

    public event EventHandler<MobileControlEndpointChangedEventArgs>? EndpointChanged;

    public int StartCalls { get; private set; }

    public int StopCalls { get; private set; }

    public MobileControlSession? CurrentSession { get; private set; }

    public int ConnectedDeviceCount { get; private set; }

    public Exception? StartException { get; set; }

    public TaskCompletionSource<object?> StartEntered { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task? StartBlocker { get; set; }

    public MobileControlSession NextSession { get; set; } = new(
        Guid.NewGuid(),
        new Uri("http://127.0.0.1:49153/?token=test-token"),
        new Uri("http://127.0.0.1:49153/?token=test-token"),
        "127.0.0.1",
        49153,
        DateTimeOffset.Now,
        MobileControlNetworkMode.LocalNetwork);

    public async Task<MobileControlSession> StartAsync(
        MobileControlSettings settings,
        CancellationToken cancellationToken = default)
    {
        StartCalls++;
        StartEntered.TrySetResult(null);
        if (StartException is not null)
        {
            throw StartException;
        }

        if (StartBlocker is not null)
        {
            await StartBlocker.WaitAsync(cancellationToken);
        }

        CurrentSession = NextSession with
        {
            Url = new Uri($"http://127.0.0.1:{settings.Port}/?token={settings.AccessToken}"),
            LanUrl = new Uri($"http://127.0.0.1:{settings.Port}/?token={settings.AccessToken}"),
            Port = settings.Port
        };
        return CurrentSession;
    }

    public Task StopAsync(
        MobileControlStopReason reason = MobileControlStopReason.Manual,
        CancellationToken cancellationToken = default)
    {
        StopCalls++;
        var session = CurrentSession;
        CurrentSession = null;
        if (session is not null)
        {
            Stopped?.Invoke(this, new MobileControlStoppedEventArgs(session.SessionId, reason));
        }

        return Task.CompletedTask;
    }

    public void SetConnectedDeviceCount(int connectedDeviceCount)
    {
        ConnectedDeviceCount = connectedDeviceCount;
        DeviceCountChanged?.Invoke(this, new MobileControlDeviceCountChangedEventArgs(connectedDeviceCount));
    }

    public void RaiseEndpointChanged(MobileControlSession session)
    {
        CurrentSession = session;
        EndpointChanged?.Invoke(this, new MobileControlEndpointChangedEventArgs(session));
    }
}

internal sealed class FakeQrCodeImageFactory : IQrCodeImageFactory
{
    public List<string> CreatedTexts { get; } = [];

    public IImage Create(string text)
    {
        CreatedTexts.Add(text);
        return new DrawingImage();
    }
}

internal sealed class FakeErrorDialogService : IErrorDialogService
{
    public List<(string Title, string ErrorType, string ErrorMessage)> Errors { get; } = [];

    public Task ShowErrorAsync(string title, string errorType, string errorMessage, CancellationToken cancellationToken = default)
    {
        Errors.Add((title, errorType, errorMessage));
        return Task.CompletedTask;
    }
}

internal sealed class FakeSeatLabelDialogService : ISeatLabelDialogService
{
    public Queue<string?> Results { get; } = [];

    public List<SeatLabelDialogRequest> Requests { get; } = [];

    public Task<string?> ShowAsync(
        SeatLabelDialogRequest request,
        CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        return Task.FromResult(Results.Count > 0 ? Results.Dequeue() : null);
    }
}

internal sealed class FakeGrabStrategyReminderDialogService : IGrabStrategyReminderDialogService
{
    public Queue<GrabStrategyReminderResult> Results { get; } = [];

    public int ShowCount { get; private set; }

    public Exception? ShowException { get; set; }

    public Task<GrabStrategyReminderResult> ShowAsync(
        CancellationToken cancellationToken = default)
    {
        ShowCount++;
        if (ShowException is not null)
        {
            throw ShowException;
        }

        return Task.FromResult(Results.Count > 0
            ? Results.Dequeue()
            : new GrabStrategyReminderResult(
                GrabStrategyReminderDecision.KeepCurrent,
                DisableReminder: false));
    }
}

internal sealed class FakeUpdateCheckService : IUpdateCheckService
{
    public Queue<UpdateCheckResult> Results { get; } = [];

    public List<UpdateCheckMode> CheckModes { get; } = [];

    public List<ReleaseVersion> SkippedVersions { get; } = [];

    public Exception? CheckException { get; set; }

    public Func<UpdateCheckMode, CancellationToken, Task<UpdateCheckResult>>? CheckHandler { get; set; }

    public async Task<UpdateCheckResult> CheckAsync(
        UpdateCheckMode mode,
        CancellationToken cancellationToken = default)
    {
        CheckModes.Add(mode);
        if (CheckException is not null)
        {
            throw CheckException;
        }

        if (CheckHandler is not null)
        {
            return await CheckHandler(mode, cancellationToken);
        }

        return Results.Count > 0
            ? Results.Dequeue()
            : UpdateCheckResult.NoUpdate("当前已是最新版本");
    }

    public Task SkipVersionAsync(
        ReleaseVersion version,
        CancellationToken cancellationToken = default)
    {
        SkippedVersions.Add(version);
        return Task.CompletedTask;
    }
}

internal sealed class FakeUpdateDialogService : IUpdateDialogService
{
    public List<ReleaseUpdateInfo> Releases { get; } = [];

    public Queue<UpdateDialogResult> Results { get; } = new();

    public UpdateDialogResult Result { get; set; } = UpdateDialogResult.Later;

    public Task<UpdateDialogResult> ShowUpdateAsync(
        ReleaseUpdateInfo release,
        CancellationToken cancellationToken = default)
    {
        Releases.Add(release);
        return Task.FromResult(Results.Count > 0 ? Results.Dequeue() : Result);
    }
}

internal sealed class FakeWindowsUpdateProgressDialogService : IWindowsUpdateProgressDialogService
{
    public WindowsPortableUpdateResult Result { get; set; } = new(
        WindowsPortableUpdateOutcome.Canceled,
        "已取消");

    public List<ReleaseUpdateInfo> Releases { get; } = [];

    public Task<WindowsPortableUpdateResult> ShowAsync(
        ReleaseUpdateInfo release,
        CancellationToken cancellationToken = default)
    {
        Releases.Add(release);
        return Task.FromResult(Result);
    }
}

internal sealed class FakeExternalLinkService : IExternalLinkService
{
    public List<Uri> OpenedUris { get; } = [];

    public Exception? OpenException { get; set; }

    public Task OpenAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        if (OpenException is not null)
        {
            throw OpenException;
        }

        OpenedUris.Add(uri);
        return Task.CompletedTask;
    }
}

internal sealed class FakeAppVersionProvider(ReleaseVersion? currentVersion = null) : IAppVersionProvider
{
    public ReleaseVersion CurrentVersion { get; } = currentVersion ?? new ReleaseVersion(1, 0, 0);

    public string CurrentVersionText => CurrentVersion.ToString();
}

internal sealed class FakeAppThemeService : IAppThemeService
{
    private static readonly AppThemePalette LightPalette = new(
        IdleBrush: new SolidColorBrush(Color.Parse("#86909C")),
        RunningBrush: new SolidColorBrush(Color.Parse("#0077FA")),
        SuccessBrush: new SolidColorBrush(Color.Parse("#14804A")),
        WarningBrush: new SolidColorBrush(Color.Parse("#C27803")),
        FailureBrush: new SolidColorBrush(Color.Parse("#C93C37")),
        RunningSoftBrush: new SolidColorBrush(Color.Parse("#E8F3FF")),
        SuccessSoftBrush: new SolidColorBrush(Color.Parse("#E8FFF1")),
        WarningSoftBrush: new SolidColorBrush(Color.Parse("#FFF5E7")),
        NeutralSoftBrush: new SolidColorBrush(Color.Parse("#F1F5F9")),
        NotificationSegmentActiveTextBrush: new SolidColorBrush(Color.Parse("#1D2129")),
        NotificationSegmentInactiveTextBrush: new SolidColorBrush(Color.Parse("#86909C")),
        LogDefaultBrush: new SolidColorBrush(Color.Parse("#1D2129")),
        LogSuccessBrush: new SolidColorBrush(Color.Parse("#16A34A")),
        LogErrorBrush: new SolidColorBrush(Color.Parse("#DC2626")));

    private static readonly AppThemePalette DarkPalette = new(
        IdleBrush: new SolidColorBrush(Color.Parse("#94A3B8")),
        RunningBrush: new SolidColorBrush(Color.Parse("#0077FA")),
        SuccessBrush: new SolidColorBrush(Color.Parse("#4ADE80")),
        WarningBrush: new SolidColorBrush(Color.Parse("#FBBF24")),
        FailureBrush: new SolidColorBrush(Color.Parse("#FB7185")),
        RunningSoftBrush: new SolidColorBrush(Color.Parse("#182C45")),
        SuccessSoftBrush: new SolidColorBrush(Color.Parse("#123021")),
        WarningSoftBrush: new SolidColorBrush(Color.Parse("#3A2A0E")),
        NeutralSoftBrush: new SolidColorBrush(Color.Parse("#182230")),
        NotificationSegmentActiveTextBrush: new SolidColorBrush(Color.Parse("#F8FAFC")),
        NotificationSegmentInactiveTextBrush: new SolidColorBrush(Color.Parse("#94A3B8")),
        LogDefaultBrush: new SolidColorBrush(Color.Parse("#E2E8F0")),
        LogSuccessBrush: new SolidColorBrush(Color.Parse("#4ADE80")),
        LogErrorBrush: new SolidColorBrush(Color.Parse("#F87171")));

    public event EventHandler<AppThemePalette>? PaletteChanged;

    public AppThemePalette CurrentPalette { get; private set; } = LightPalette;

    public int InitializeCalls { get; private set; }

    public int ApplySettingsCalls { get; private set; }

    public ThemePreferences? LastAppliedTheme { get; private set; }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        InitializeCalls++;
        return Task.CompletedTask;
    }

    public Task ApplyThemeAsync(ThemePreferences theme, CancellationToken cancellationToken = default)
    {
        ApplySettingsCalls++;
        LastAppliedTheme = theme;
        CurrentPalette = theme.Mode == AppThemeMode.Dark
            ? DarkPalette
            : LightPalette;
        PaletteChanged?.Invoke(this, CurrentPalette);
        return Task.CompletedTask;
    }

    public void AttachTopLevel(TopLevel topLevel)
    {
    }
}

internal sealed class FakeSettingsService : ISettingsService
{
    private readonly SemaphoreSlim _settingsGate = new(1, 1);

    public FakeSettingsService(AppSettings settings, bool normalizeMobileControl = true)
    {
        CurrentSettings = normalizeMobileControl
            ? EnsureMobileControlSettings(settings)
            : settings;
    }

    public AppSettings CurrentSettings { get; private set; }

    public int SaveCalls { get; private set; }

    public Queue<Exception> LoadExceptions { get; } = [];

    public Queue<Exception> UpdateExceptions { get; } = [];

    public TaskCompletionSource<object?>? UpdateStarted { get; set; }

    public Task? UpdateBlocker { get; set; }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _settingsGate.WaitAsync(cancellationToken);
        try
        {
            if (LoadExceptions.Count > 0)
            {
                throw LoadExceptions.Dequeue();
            }

            return CurrentSettings;
        }
        finally
        {
            _settingsGate.Release();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        await _settingsGate.WaitAsync(cancellationToken);
        try
        {
            SaveCalls++;
            CurrentSettings = settings;
        }
        finally
        {
            _settingsGate.Release();
        }
    }

    public async Task<AppSettings> UpdateAsync(
        Func<AppSettings, AppSettings> update,
        CancellationToken cancellationToken = default)
    {
        await _settingsGate.WaitAsync(cancellationToken);
        try
        {
            UpdateStarted?.TrySetResult(null);
            if (UpdateBlocker is not null)
            {
                await UpdateBlocker.WaitAsync(cancellationToken);
            }

            if (UpdateExceptions.Count > 0)
            {
                throw UpdateExceptions.Dequeue();
            }

            var updated = update(CurrentSettings);
            if (updated != CurrentSettings)
            {
                SaveCalls++;
                CurrentSettings = updated;
            }

            return CurrentSettings;
        }
        finally
        {
            _settingsGate.Release();
        }
    }

    private static AppSettings EnsureMobileControlSettings(AppSettings settings)
    {
        if (MobileControlSettings.IsValidPort(settings.MobileControl.Port) &&
            !string.IsNullOrWhiteSpace(settings.MobileControl.AccessToken))
        {
            return settings;
        }

        return settings with
        {
            MobileControl = new MobileControlSettings(
                49153,
                "test-mobile-token",
                settings.MobileControl.AutoStart,
                settings.MobileControl.NetworkMode,
                settings.MobileControl.TunnelProxyMode,
                settings.MobileControl.TunnelManualProxyUrl)
        };
    }
}

internal sealed class RecordingMainWindowSizePersistenceService : IMainWindowSizePersistenceService
{
    public List<(bool Enabled, bool CaptureCurrentSize)> Changes { get; } = [];

    public Task InitializeAsync(Window window, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public void SetRememberSizeEnabled(bool enabled, bool captureCurrentSize)
    {
        Changes.Add((enabled, captureCurrentSize));
    }

    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

internal sealed class FakeCoordinatorRuntime : ICoordinatorRuntime
{
    private readonly Queue<TimeSpan> _randomDelays = [];
    private readonly Queue<int> _nextInts = [];

    public DateTimeOffset Now { get; set; } = DateTimeOffset.Now;

    public bool CompleteDelaysImmediately { get; set; } = true;

    public bool AdvanceOnDelay { get; set; }

    public int? BlockDelaysStartingAtCall { get; set; }

    public List<TimeSpan> DelayRequests { get; } = [];

    public TaskCompletionSource<object?>? DelayStarted { get; set; }

    public void EnqueueRandomDelay(TimeSpan delay)
    {
        _randomDelays.Enqueue(delay);
    }

    public void EnqueueNextInt(int value)
    {
        _nextInts.Enqueue(value);
    }

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        DelayRequests.Add(delay);
        if (AdvanceOnDelay)
        {
            Now += delay;
        }

        DelayStarted?.TrySetResult(null);
        if (BlockDelaysStartingAtCall is not null &&
            DelayRequests.Count >= BlockDelaysStartingAtCall)
        {
            return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        return CompleteDelaysImmediately
            ? Task.CompletedTask
            : Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    public TimeSpan RandomBetween(TimeSpan minimum, TimeSpan maximum)
    {
        return _randomDelays.Count > 0
            ? _randomDelays.Dequeue()
            : minimum;
    }

    public int NextInt(int minInclusive, int maxExclusive)
    {
        return _nextInts.Count > 0
            ? _nextInts.Dequeue()
            : minInclusive;
    }
}

internal sealed class FakeSessionService : ISessionService
{
    public SessionCredentials? CurrentSession { get; set; }

    public SessionCredentials AuthenticateFromCookieResult { get; set; }
        = new("cookie", SessionSource.ManualCookie, DateTimeOffset.Now, true);

    public SessionCredentials? RestoreResult { get; set; }

    public Exception? AuthenticateFromCookieException { get; set; }

    public Exception? SignOutException { get; set; }

    public int AuthenticateFromCookieCalls { get; private set; }

    public int RestoreCalls { get; private set; }

    public int SignOutCalls { get; private set; }

    public Task<SessionCredentials> AuthenticateFromCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        CurrentSession = AuthenticateFromCookieResult with
        {
            Source = SessionSource.QrCodeLink
        };
        return Task.FromResult(CurrentSession);
    }

    public Task<SessionCredentials> AuthenticateFromCookieAsync(string cookie, bool remember, CancellationToken cancellationToken = default)
    {
        AuthenticateFromCookieCalls++;
        if (AuthenticateFromCookieException is not null)
        {
            throw AuthenticateFromCookieException;
        }

        CurrentSession = AuthenticateFromCookieResult with
        {
            Cookie = cookie,
            SavedAt = DateTimeOffset.Now,
            CanAutoRestore = remember
        };
        return Task.FromResult(CurrentSession);
    }

    public Task<SessionCredentials?> RestoreAsync(CancellationToken cancellationToken = default)
    {
        RestoreCalls++;
        CurrentSession = RestoreResult;
        return Task.FromResult(RestoreResult);
    }

    public Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        SignOutCalls++;
        CurrentSession = null;
        if (SignOutException is not null)
        {
            throw SignOutException;
        }

        return Task.CompletedTask;
    }
}

internal sealed class FakeLibraryService : ILibraryService
{
    public LibrarySummary? BoundLibrary { get; private set; }

    public IReadOnlyList<LibrarySummary> LibrariesToLoad { get; set; } = [];

    public Dictionary<int, LibraryLayout> LayoutsByLibraryId { get; } = [];

    public Dictionary<int, IReadOnlyList<SeatReference>> FavoritesByLibraryId { get; } = [];

    public int LoadLibrariesCalls { get; private set; }

    public int BindLibraryCalls { get; private set; }

    public int RefreshBoundLibraryCalls { get; private set; }

    public int SaveFavoritesCalls { get; private set; }

    public Task<IReadOnlyList<LibrarySummary>> LoadLibrariesAsync(CancellationToken cancellationToken = default)
    {
        LoadLibrariesCalls++;
        return Task.FromResult(LibrariesToLoad);
    }

    public Task<LibraryLayout> BindLibraryAsync(int libraryId, CancellationToken cancellationToken = default)
    {
        BindLibraryCalls++;
        BoundLibrary = LibrariesToLoad.FirstOrDefault(x => x.LibraryId == libraryId);
        return Task.FromResult(LayoutsByLibraryId[libraryId]);
    }

    public Task<LibraryLayout> RefreshBoundLibraryAsync(CancellationToken cancellationToken = default)
    {
        RefreshBoundLibraryCalls++;
        if (BoundLibrary is null)
        {
            throw new InvalidOperationException("No bound library configured.");
        }

        return Task.FromResult(LayoutsByLibraryId[BoundLibrary.LibraryId]);
    }

    public Task<IReadOnlyList<SeatReference>> GetFavoritesAsync(int libraryId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(
            FavoritesByLibraryId.TryGetValue(libraryId, out var favorites)
                ? favorites
                : Array.Empty<SeatReference>() as IReadOnlyList<SeatReference>);
    }

    public Task SaveFavoritesAsync(int libraryId, IReadOnlyList<SeatReference> seats, CancellationToken cancellationToken = default)
    {
        SaveFavoritesCalls++;
        FavoritesByLibraryId[libraryId] = seats.ToArray();
        return Task.CompletedTask;
    }
}

internal sealed class FakeSeatLabelService : ISeatLabelService
{
    public Dictionary<int, IReadOnlyList<SeatLabel>> LabelsByLibraryId { get; } = [];

    public int SetCalls { get; private set; }

    public int DeleteCalls { get; private set; }

    public Exception? SetException { get; set; }

    public Exception? DeleteException { get; set; }

    public Task<IReadOnlyList<SeatLabel>> GetLabelsAsync(
        int libraryId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(
            LabelsByLibraryId.TryGetValue(libraryId, out var labels)
                ? labels
                : Array.Empty<SeatLabel>() as IReadOnlyList<SeatLabel>);
    }

    public Task<IReadOnlyList<SeatLabel>> SetLabelsAsync(
        int libraryId,
        IReadOnlyList<SeatReference> seats,
        string text,
        CancellationToken cancellationToken = default)
    {
        SetCalls++;
        if (SetException is not null)
        {
            throw SetException;
        }

        var saved = seats
            .DistinctBy(static seat => seat.SeatKey, StringComparer.Ordinal)
            .Select(seat => new SeatLabel(seat.SeatKey, seat.SeatName, text.Trim()))
            .ToArray();
        LabelsByLibraryId[libraryId] = saved;
        return Task.FromResult<IReadOnlyList<SeatLabel>>(saved);
    }

    public Task DeleteLabelsAsync(
        int libraryId,
        IReadOnlyList<string> seatKeys,
        CancellationToken cancellationToken = default)
    {
        DeleteCalls++;
        if (DeleteException is not null)
        {
            throw DeleteException;
        }

        var keys = seatKeys.ToHashSet(StringComparer.Ordinal);
        LabelsByLibraryId[libraryId] = LabelsByLibraryId
            .GetValueOrDefault(libraryId, Array.Empty<SeatLabel>())
            .Where(label => !keys.Contains(label.SeatKey))
            .ToArray();
        return Task.CompletedTask;
    }
}

internal sealed class FakeGrabSeatCoordinator : IGrabSeatCoordinator
{
    private CoordinatorStatus _status = CoordinatorStatus.Idle("抢座");

    public event EventHandler<CoordinatorStatus>? StatusChanged;

    public GrabSeatPlan? LastPlan { get; private set; }

    public int StopCalls { get; private set; }

    public Task StartAsync(GrabSeatPlan plan, CancellationToken cancellationToken = default)
    {
        LastPlan = plan;
        _status = new CoordinatorStatus(
            CoordinatorTaskState.Running,
            "抢座",
            "测试中的抢座任务",
            DateTimeOffset.Now,
            DateTimeOffset.Now,
            Reason: CoordinatorStatusReason.Running);
        StatusChanged?.Invoke(this, _status);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        StopCalls++;
        _status = new CoordinatorStatus(
            CoordinatorTaskState.Completed,
            "抢座",
            "测试中的抢座任务已结束",
            _status.StartedAt,
            DateTimeOffset.Now,
            Reason: CoordinatorStatusReason.Stopped);
        StatusChanged?.Invoke(this, _status);
        return Task.CompletedTask;
    }

    public void EmitStatus(CoordinatorStatus status)
    {
        _status = status;
        StatusChanged?.Invoke(this, _status);
    }

    public CoordinatorStatus GetStatus() => _status;
}

internal sealed class FakeGlobalLeakCoordinator : IGlobalLeakCoordinator
{
    private CoordinatorStatus _status = CoordinatorStatus.Idle("全域捡漏");

    public event EventHandler<CoordinatorStatus>? StatusChanged;

    public GlobalLeakPlan? LastPlan { get; private set; }

    public int StopCalls { get; private set; }

    public Task StartAsync(GlobalLeakPlan plan, CancellationToken cancellationToken = default)
    {
        LastPlan = plan;
        _status = new CoordinatorStatus(
            CoordinatorTaskState.Running,
            "全域捡漏",
            "测试中的全域捡漏任务",
            DateTimeOffset.Now,
            DateTimeOffset.Now,
            Reason: CoordinatorStatusReason.Running);
        StatusChanged?.Invoke(this, _status);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        StopCalls++;
        _status = new CoordinatorStatus(
            CoordinatorTaskState.Completed,
            "全域捡漏",
            "测试中的全域捡漏任务已停止",
            _status.StartedAt,
            DateTimeOffset.Now,
            Reason: CoordinatorStatusReason.Stopped);
        StatusChanged?.Invoke(this, _status);
        return Task.CompletedTask;
    }

    public void EmitStatus(CoordinatorStatus status)
    {
        _status = status;
        StatusChanged?.Invoke(this, _status);
    }

    public CoordinatorStatus GetStatus() => _status;
}

internal sealed class FakeOccupySeatCoordinator : IOccupySeatCoordinator
{
    private CoordinatorStatus _status = CoordinatorStatus.Idle("占座");

    public event EventHandler<CoordinatorStatus>? StatusChanged;

    public int StopCalls { get; private set; }

    public Task StartAsync(OccupySeatPlan plan, CancellationToken cancellationToken = default)
    {
        _status = new CoordinatorStatus(
            CoordinatorTaskState.Running,
            "占座",
            "测试中的占座任务",
            DateTimeOffset.Now,
            DateTimeOffset.Now,
            Reason: CoordinatorStatusReason.Running);
        StatusChanged?.Invoke(this, _status);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        StopCalls++;
        _status = new CoordinatorStatus(
            CoordinatorTaskState.Completed,
            "占座",
            "测试中的占座任务已停止",
            _status.StartedAt,
            DateTimeOffset.Now,
            Reason: CoordinatorStatusReason.Stopped);
        StatusChanged?.Invoke(this, _status);
        return Task.CompletedTask;
    }

    public void EmitStatus(CoordinatorStatus status)
    {
        _status = status;
        StatusChanged?.Invoke(this, _status);
    }

    public CoordinatorStatus GetStatus() => _status;
}

internal sealed class FakeTaskLaunchService(
    IGrabSeatCoordinator? grabSeatCoordinator = null,
    IGlobalLeakCoordinator? globalLeakCoordinator = null,
    IOccupySeatCoordinator? occupySeatCoordinator = null) : ITaskLaunchService
{
    public GrabSeatPlan? LastGrabPlan { get; private set; }

    public GlobalLeakPlan? LastGlobalLeakPlan { get; private set; }

    public OccupySeatPlan? LastOccupyPlan { get; private set; }

    public TaskLaunchSource? LastSource { get; private set; }

    public Task StartGrabAsync(
        GrabSeatPlan plan,
        TaskLaunchSource source,
        CancellationToken cancellationToken = default)
    {
        LastGrabPlan = plan;
        LastSource = source;
        return grabSeatCoordinator?.StartAsync(plan, cancellationToken) ?? Task.CompletedTask;
    }

    public Task StartGlobalLeakAsync(
        GlobalLeakPlan plan,
        TaskLaunchSource source,
        CancellationToken cancellationToken = default)
    {
        LastGlobalLeakPlan = plan;
        LastSource = source;
        return globalLeakCoordinator?.StartAsync(plan, cancellationToken) ?? Task.CompletedTask;
    }

    public Task StartOccupyAsync(
        OccupySeatPlan plan,
        TaskLaunchSource source,
        CancellationToken cancellationToken = default)
    {
        LastOccupyPlan = plan;
        LastSource = source;
        return occupySeatCoordinator?.StartAsync(plan, cancellationToken) ?? Task.CompletedTask;
    }
}

internal sealed class FakeTomorrowReservationCoordinator : ITomorrowReservationCoordinator
{
    private CoordinatorStatus _status = CoordinatorStatus.Idle("明日预约");

    public event EventHandler<CoordinatorStatus>? StatusChanged;

    public TomorrowReservationPlan? LastPlan { get; private set; }

    public int StopCalls { get; private set; }

    public Task StartAsync(TomorrowReservationPlan plan, CancellationToken cancellationToken = default)
    {
        LastPlan = plan;
        _status = new CoordinatorStatus(
            CoordinatorTaskState.Running,
            "明日预约",
            "测试中的明日预约任务",
            DateTimeOffset.Now,
            DateTimeOffset.Now,
            Reason: CoordinatorStatusReason.Running);
        StatusChanged?.Invoke(this, _status);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        StopCalls++;
        _status = new CoordinatorStatus(
            CoordinatorTaskState.Completed,
            "明日预约",
            "测试中的明日预约任务已停止",
            _status.StartedAt,
            DateTimeOffset.Now,
            Reason: CoordinatorStatusReason.Stopped);
        StatusChanged?.Invoke(this, _status);
        return Task.CompletedTask;
    }

    public void EmitStatus(CoordinatorStatus status)
    {
        _status = status;
        StatusChanged?.Invoke(this, _status);
    }

    public CoordinatorStatus GetStatus() => _status;
}

internal sealed class FakeStartupEntryService : IStartupEntryService
{
    public bool IsSupported { get; set; } = true;

    public bool EnableCalled { get; private set; }

    public bool DisableCalled { get; private set; }

    public Exception? EnableException { get; set; }

    public Exception? DisableException { get; set; }

    public bool IsEnabledResult { get; set; }

    public Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(IsEnabledResult);

    public Task EnableAsync(CancellationToken cancellationToken = default)
    {
        EnableCalled = true;
        if (EnableException is not null)
        {
            throw EnableException;
        }
        return Task.CompletedTask;
    }

    public Task DisableAsync(CancellationToken cancellationToken = default)
    {
        DisableCalled = true;
        if (DisableException is not null)
        {
            throw DisableException;
        }
        return Task.CompletedTask;
    }

    public void Reset()
    {
        EnableCalled = false;
        DisableCalled = false;
        EnableException = null;
        DisableException = null;
        IsEnabledResult = false;
        IsSupported = true;
    }
}

internal sealed class FakeStorageLocationService : IStorageLocationService
{
    public FakeStorageLocationService()
    {
        var root = Path.Combine(Path.GetTempPath(), "IGoLibrary-Ex-Tests");
        Current = new StorageLocations(Path.Combine(root, "data"), Path.Combine(root, "logs"));
        Defaults = Current;
    }

    public StorageLocations Current { get; set; }

    public StorageLocations Defaults { get; set; }

    public StorageLocationChangeRequest? StagedChange { get; private set; }

    public StorageLocationStartupResult? StartupResult { get; set; }

    public StorageTargetDatabaseInspection TargetDatabaseInspection { get; set; } =
        new(false, true, null);

    public Task ValidateAsync(StorageLocations locations, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<StorageTargetDatabaseInspection> InspectTargetDatabaseAsync(
        string dataDirectory,
        CancellationToken cancellationToken = default)
        => Task.FromResult(TargetDatabaseInspection);

    public Task StageChangeAsync(
        StorageLocationChangeRequest request,
        CancellationToken cancellationToken = default)
    {
        StagedChange = request;
        return Task.CompletedTask;
    }

    public Task CancelPendingChangeAsync(CancellationToken cancellationToken = default)
    {
        StagedChange = null;
        return Task.CompletedTask;
    }

    public Task<StorageLocationStartupResult?> ConsumeStartupResultAsync(
        CancellationToken cancellationToken = default)
    {
        var result = StartupResult;
        StartupResult = null;
        return Task.FromResult(result);
    }
}

internal sealed class FakeFolderPickerService : IFolderPickerService
{
    public string? SelectedPath { get; set; }

    public Task<string?> PickFolderAsync(string title, CancellationToken cancellationToken = default)
        => Task.FromResult(SelectedPath);
}

internal sealed class FakeStorageChangeWorkflowService : IStorageChangeWorkflowService
{
    public StorageLocations? LastTarget { get; private set; }

    public bool Result { get; set; }

    public Task<bool> ApplyAsync(StorageLocations target, CancellationToken cancellationToken = default)
    {
        LastTarget = target;
        return Task.FromResult(Result);
    }
}

internal sealed class FakeLoggingSettingsWorkflowService : ILoggingSettingsWorkflowService
{
    public List<LogFileSettings> SavedSettings { get; } = [];

    public Func<LogFileSettings, Task<LoggingSettingsUpdateResult>>? SaveHandler { get; set; }

    public Task<LoggingSettingsUpdateResult> SaveAsync(
        LogFileSettings settings,
        CancellationToken cancellationToken = default)
    {
        SavedSettings.Add(settings);
        return SaveHandler?.Invoke(settings)
               ?? Task.FromResult(new LoggingSettingsUpdateResult(
                   LogFileSettings.Normalize(settings),
                   LogRuntimeApplyResult.Success));
    }
}

internal sealed class FakeStorageChangeDialogService : IStorageChangeDialogService
{
    public StorageMigrationDecision MigrationDecision { get; set; } = StorageMigrationDecision.Migrate;

    public bool ConfirmOverwriteResult { get; set; } = true;

    public bool ConfirmUseExistingResult { get; set; } = true;

    public bool ConfirmStopTasksResult { get; set; } = true;

    public int MigrationPrompts { get; private set; }

    public int OverwritePrompts { get; private set; }

    public int UseExistingPrompts { get; private set; }

    public IReadOnlyList<string>? LastStopTaskNames { get; private set; }

    public Task<StorageMigrationDecision> ConfirmMigrationAsync(
        StorageLocations current,
        StorageLocations target,
        bool dataDirectoryChanged,
        bool logDirectoryChanged,
        CancellationToken cancellationToken = default)
    {
        MigrationPrompts++;
        return Task.FromResult(MigrationDecision);
    }

    public Task<bool> ConfirmOverwriteDatabaseAsync(
        string databasePath,
        CancellationToken cancellationToken = default)
    {
        OverwritePrompts++;
        return Task.FromResult(ConfirmOverwriteResult);
    }

    public Task<bool> ConfirmUseExistingDatabaseAsync(
        string databasePath,
        CancellationToken cancellationToken = default)
    {
        UseExistingPrompts++;
        return Task.FromResult(ConfirmUseExistingResult);
    }

    public Task<bool> ConfirmStopTasksAsync(
        IReadOnlyList<string> taskNames,
        CancellationToken cancellationToken = default)
    {
        LastStopTaskNames = taskNames;
        return Task.FromResult(ConfirmStopTasksResult);
    }
}

internal sealed class FakeApplicationRestartService : IApplicationRestartService
{
    public int RestartCalls { get; private set; }

    public Exception? Exception { get; set; }

    public Task RestartAsync(CancellationToken cancellationToken = default)
    {
        RestartCalls++;
        return Exception is null ? Task.CompletedTask : Task.FromException(Exception);
    }
}
