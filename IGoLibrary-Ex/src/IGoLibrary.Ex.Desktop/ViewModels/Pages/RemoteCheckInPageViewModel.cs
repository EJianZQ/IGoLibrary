using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Configuration;
using IGoLibrary.Ex.Application.Exceptions;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Helpers;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Desktop.ViewModels;

public sealed partial class RemoteCheckInPageViewModel : ViewModelBase
{
    private readonly IRemoteCheckInWorkflowService _workflowService;
    private readonly IRemoteCheckInProfileService _profileService;
    private readonly IReservationWorkflowService _reservationWorkflowService;
    private readonly ShellWorkflowState _workflowState;
    private readonly OAuthCodeConsumptionRegistry _oauthCodeConsumptionRegistry;
    private readonly LanCookieRelayViewModel _lanCookieRelay;
    private readonly IActivityLogService _activityLogService;
    private readonly INotificationService _notificationService;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private RemoteCheckInVenueProfileSettings? _savedProfile;
    private int? _loadedLibraryId;
    private int _profileLoadVersion;
    private string? _selectedBeaconUuidBeforeChange;
    private bool _suppressDraftChanges;
    private bool _initialized;

    public RemoteCheckInPageViewModel(
        IRemoteCheckInWorkflowService workflowService,
        IRemoteCheckInProfileService profileService,
        IReservationWorkflowService reservationWorkflowService,
        ShellWorkflowState workflowState,
        OAuthCodeConsumptionRegistry oauthCodeConsumptionRegistry,
        LanCookieRelayViewModel lanCookieRelay,
        IActivityLogService activityLogService,
        INotificationService notificationService)
    {
        _workflowService = workflowService;
        _profileService = profileService;
        _reservationWorkflowService = reservationWorkflowService;
        _workflowState = workflowState;
        _oauthCodeConsumptionRegistry = oauthCodeConsumptionRegistry;
        _lanCookieRelay = lanCookieRelay;
        _activityLogService = activityLogService;
        _notificationService = notificationService;
        _workflowState.PropertyChanged += OnWorkflowStatePropertyChanged;
    }

    public ObservableCollection<string> AllowedBeaconUuids { get; } = [];

    public bool IsAuthorized => _workflowState.IsAuthorized;

    public bool HasLockedLibrary => _workflowState.LockedLibrary is not null;

    public string LockedLibraryText => _workflowState.LockedLibrary is { } library
        ? $"{library.Name} · {library.Floor}"
        : "尚未锁定场馆";

    public string CurrentReservationText => _workflowState.CurrentReservation is { } reservation
        ? $"{reservation.LibraryName} · {reservation.SeatName} · 到期 {reservation.ExpirationTime:HH:mm:ss}"
        : "当前未查询到预约";

    public bool HasMatchingReservation =>
        _workflowState.LockedLibrary is { } library &&
        _workflowState.CurrentReservation?.LibraryId == library.LibraryId;

    public bool CanAuthorize => IsAuthorized && !IsBusy;

    public bool CanRefreshDevices => HasRemoteCheckInSession && IsAuthorized && !IsBusy;

    public bool CanSaveProfile =>
        HasLockedLibrary &&
        !IsBusy &&
        SelectedBeaconUuid is not null &&
        AllowedBeaconUuids.Contains(SelectedBeaconUuid, StringComparer.OrdinalIgnoreCase) &&
        BeaconMajor is >= ushort.MinValue and <= ushort.MaxValue &&
        BeaconMinor is >= ushort.MinValue and <= ushort.MaxValue &&
        Latitude is >= -90m and <= 90m &&
        Longitude is >= -180m and <= 180m;

    public bool CanSign =>
        IsAuthorized &&
        HasLockedLibrary &&
        HasRemoteCheckInSession &&
        IsProfileSaved &&
        !IsBusy;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool hasRemoteCheckInSession;

    [ObservableProperty]
    private bool rememberRemoteCheckInSession = true;

    [ObservableProperty]
    private string authorizationLinkText = string.Empty;

    [ObservableProperty]
    private string authorizationStatusText = "尚未获取签到授权";

    [ObservableProperty]
    private string accountSummaryText = "等待获取签到账号信息";

    [ObservableProperty]
    private string deviceStatusText = "请先获取独立签到授权并刷新信标";

    [ObservableProperty]
    private string? selectedBeaconUuid;

    [ObservableProperty]
    private int? beaconMajor;

    [ObservableProperty]
    private int? beaconMinor;

    [ObservableProperty]
    private decimal? latitude;

    [ObservableProperty]
    private decimal? longitude;

    [ObservableProperty]
    private bool isProfileSaved;

    [ObservableProperty]
    private string profileStatusText = "尚未保存当前场馆配置";

    [ObservableProperty]
    private string lastResultText = "尚未执行远程签到";

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        var restored = await _workflowService.RestoreAsync(cancellationToken);
        HasRemoteCheckInSession = restored is not null;
        AuthorizationStatusText = restored is null
            ? "尚未获取签到授权"
            : "已加载安全保存的签到授权，等待服务端验证";
        await LoadProfileForLockedLibraryAsync(cancellationToken);
        RaiseAvailabilityProperties();
    }

    private async Task<bool> TryEnterOperationAsync()
    {
        if (!await _operationGate.WaitAsync(0))
        {
            return false;
        }

        IsBusy = true;
        RaiseAvailabilityProperties();
        return true;
    }

    private void ExitOperation()
    {
        IsBusy = false;
        _operationGate.Release();
        RaiseAvailabilityProperties();
    }

    partial void OnIsBusyChanged(bool value) => RaiseAvailabilityProperties();

    partial void OnHasRemoteCheckInSessionChanged(bool value) => RaiseAvailabilityProperties();

    partial void OnIsProfileSavedChanged(bool value) => RaiseAvailabilityProperties();

    private void OnWorkflowStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellWorkflowState.LockedLibrary))
        {
            _ = LoadProfileAfterLockedLibraryChangedAsync();
        }

        if (e.PropertyName == nameof(ShellWorkflowState.IsAuthorized) && !_workflowState.IsAuthorized)
        {
            HasRemoteCheckInSession = false;
            AccountSummaryText = "等待获取签到账号信息";
            AllowedBeaconUuids.Clear();
        }

        if (e.PropertyName is nameof(ShellWorkflowState.IsAuthorized)
            or nameof(ShellWorkflowState.LockedLibrary)
            or nameof(ShellWorkflowState.CurrentReservation))
        {
            OnPropertyChanged(nameof(IsAuthorized));
            OnPropertyChanged(nameof(HasLockedLibrary));
            OnPropertyChanged(nameof(LockedLibraryText));
            OnPropertyChanged(nameof(CurrentReservationText));
            OnPropertyChanged(nameof(HasMatchingReservation));
            RaiseAvailabilityProperties();
        }
    }

    private void RaiseAvailabilityProperties()
    {
        OnPropertyChanged(nameof(CanAuthorize));
        OnPropertyChanged(nameof(CanRefreshDevices));
        OnPropertyChanged(nameof(CanSaveProfile));
        OnPropertyChanged(nameof(CanSign));
    }

    private async Task LoadProfileAfterLockedLibraryChangedAsync()
    {
        try
        {
            await LoadProfileForLockedLibraryAsync();
        }
        catch (Exception ex)
        {
            ProfileStatusText = $"加载当前场馆签到配置失败：{ex.Message}";
            _activityLogService.Write(
                LogEntryKind.Warning,
                "RemoteCheckIn",
                $"加载当前场馆签到配置失败：{ex.Message}");
        }
    }

}
