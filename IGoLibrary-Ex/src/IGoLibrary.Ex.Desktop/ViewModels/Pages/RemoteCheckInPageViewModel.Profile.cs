using CommunityToolkit.Mvvm.Input;
using IGoLibrary.Ex.Application.Configuration;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Desktop.ViewModels;

public sealed partial class RemoteCheckInPageViewModel
{
    [RelayCommand]
    private async Task SaveProfileAsync()
    {
        if (!await TryEnterOperationAsync())
        {
            return;
        }

        try
        {
            var library = _workflowState.LockedLibrary
                ?? throw new InvalidOperationException("请先锁定场馆。");
            if (SelectedBeaconUuid is null ||
                !AllowedBeaconUuids.Contains(SelectedBeaconUuid, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("请选择本次服务端返回的 Beacon UUID。");
            }

            var saved = await _profileService.SaveAsync(new RemoteCheckInVenueProfileSettings
            {
                LibraryId = library.LibraryId,
                LibraryName = library.Name,
                BeaconUuid = SelectedBeaconUuid,
                Major = BeaconMajor,
                Minor = BeaconMinor,
                Latitude = Latitude,
                Longitude = Longitude
            });
            _savedProfile = saved;
            IsProfileSaved = true;
            ProfileStatusText = $"已保存 {library.Name} 的远程签到配置";
            await _notificationService.ShowSuccessAsync("场馆配置已保存", library.Name);
        }
        catch (Exception ex)
        {
            await _notificationService.ShowWarningAsync("保存场馆配置失败", ex.Message);
        }
        finally
        {
            ExitOperation();
        }
    }

    private void ApplyDeviceInfo(RemoteCheckInDeviceInfo info)
    {
        var previousUuid = SelectedBeaconUuid;
        AllowedBeaconUuids.Clear();
        foreach (var uuid in info.BeaconUuids)
        {
            AllowedBeaconUuids.Add(uuid);
        }

        AccountSummaryText = BuildAccountSummary(info.User);
        var savedUuid = _savedProfile?.BeaconUuid;
        var savedStillAllowed = HasValidSavedProfile() &&
                                !string.IsNullOrWhiteSpace(savedUuid) &&
                                AllowedBeaconUuids.Contains(savedUuid, StringComparer.OrdinalIgnoreCase);
        var draftMatchesSavedProfile = DraftMatchesSavedProfile();

        _suppressDraftChanges = true;
        try
        {
            if (savedStillAllowed && draftMatchesSavedProfile)
            {
                SelectedBeaconUuid = savedUuid;
                IsProfileSaved = true;
                DeviceStatusText = $"已获取 {AllowedBeaconUuids.Count} 个允许信标，当前配置仍有效";
            }
            else if (savedStillAllowed)
            {
                IsProfileSaved = false;
                ProfileStatusText = "配置已修改，请重新保存";
                DeviceStatusText =
                    SelectedBeaconUuid is not null &&
                    AllowedBeaconUuids.Contains(SelectedBeaconUuid, StringComparer.OrdinalIgnoreCase)
                        ? $"已获取 {AllowedBeaconUuids.Count} 个允许信标，未保存的配置修改已保留"
                        : "当前草稿选择的 UUID 已不在允许列表中，请重新选择并保存";
            }
            else
            {
                SelectedBeaconUuid = AllowedBeaconUuids.Count == 1 ? AllowedBeaconUuids[0] : null;
                if (!string.Equals(previousUuid, SelectedBeaconUuid, StringComparison.OrdinalIgnoreCase) ||
                    !string.IsNullOrWhiteSpace(savedUuid))
                {
                    BeaconMajor = null;
                    BeaconMinor = null;
                }

                IsProfileSaved = false;
                ProfileStatusText = string.IsNullOrWhiteSpace(savedUuid)
                    ? "请选择信标并保存场馆配置"
                    : "服务端允许的 UUID 已变化，请重新填写 Major/Minor 并保存";
                DeviceStatusText = AllowedBeaconUuids.Count == 1
                    ? "已自动选择唯一 UUID，请填写并保存 Beacon 参数"
                    : $"服务端返回 {AllowedBeaconUuids.Count} 个 UUID，请选择实际信标";
            }
        }
        finally
        {
            _suppressDraftChanges = false;
        }

        RaiseAvailabilityProperties();
    }

    internal async Task LoadProfileForLockedLibraryAsync(CancellationToken cancellationToken = default)
    {
        var loadVersion = Interlocked.Increment(ref _profileLoadVersion);
        var library = _workflowState.LockedLibrary;
        if (library is null)
        {
            _loadedLibraryId = null;
            _savedProfile = null;
            ClearProfileDraft();
            return;
        }

        if (_loadedLibraryId == library.LibraryId)
        {
            return;
        }

        AllowedBeaconUuids.Clear();
        AccountSummaryText = HasRemoteCheckInSession
            ? "请刷新信标以验证签到账号与当前预约"
            : "等待获取签到账号信息";
        RemoteCheckInVenueProfileSettings? loadedProfile;
        try
        {
            loadedProfile = await _profileService.GetForLibraryAsync(library.LibraryId, cancellationToken);
        }
        catch when (!IsCurrentProfileLoad(loadVersion, library.LibraryId))
        {
            return;
        }

        if (!IsCurrentProfileLoad(loadVersion, library.LibraryId))
        {
            return;
        }

        _loadedLibraryId = library.LibraryId;
        _savedProfile = loadedProfile;

        _suppressDraftChanges = true;
        try
        {
            SelectedBeaconUuid = _savedProfile?.BeaconUuid;
            BeaconMajor = _savedProfile?.Major;
            BeaconMinor = _savedProfile?.Minor;
            Latitude = _savedProfile?.Latitude;
            Longitude = _savedProfile?.Longitude;
            IsProfileSaved = HasValidSavedProfile();
            ProfileStatusText = _savedProfile is null
                ? $"尚未保存 {library.Name} 的签到配置"
                : IsProfileSaved
                    ? $"已加载 {library.Name} 的签到配置，请刷新信标验证 UUID"
                    : $"{library.Name} 的旧配置不完整，请重新填写并保存";
        }
        finally
        {
            _suppressDraftChanges = false;
        }

        RaiseAvailabilityProperties();
    }

    private void ClearProfileDraft()
    {
        _suppressDraftChanges = true;
        try
        {
            SelectedBeaconUuid = null;
            BeaconMajor = null;
            BeaconMinor = null;
            Latitude = null;
            Longitude = null;
            IsProfileSaved = false;
            ProfileStatusText = "请先锁定场馆";
            AllowedBeaconUuids.Clear();
        }
        finally
        {
            _suppressDraftChanges = false;
        }

        RaiseAvailabilityProperties();
    }

    private void MarkProfileDraftChanged()
    {
        if (_suppressDraftChanges)
        {
            return;
        }

        IsProfileSaved = false;
        ProfileStatusText = "配置已修改，请重新保存";
        RaiseAvailabilityProperties();
    }

    partial void OnSelectedBeaconUuidChanged(string? value)
    {
        var previousValue = _selectedBeaconUuidBeforeChange;
        _selectedBeaconUuidBeforeChange = value;
        if (!_suppressDraftChanges &&
            previousValue is not null &&
            !string.Equals(previousValue, value, StringComparison.OrdinalIgnoreCase))
        {
            _suppressDraftChanges = true;
            try
            {
                BeaconMajor = null;
                BeaconMinor = null;
            }
            finally
            {
                _suppressDraftChanges = false;
            }
        }

        MarkProfileDraftChanged();
    }

    partial void OnBeaconMajorChanged(int? value) => MarkProfileDraftChanged();

    partial void OnBeaconMinorChanged(int? value) => MarkProfileDraftChanged();

    partial void OnLatitudeChanged(decimal? value) => MarkProfileDraftChanged();

    partial void OnLongitudeChanged(decimal? value) => MarkProfileDraftChanged();

    private bool HasValidSavedProfile()
    {
        if (_savedProfile is null)
        {
            return false;
        }

        try
        {
            _ = RemoteCheckInProfileValidator.NormalizeAndValidate(_savedProfile);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private bool DraftMatchesSavedProfile()
    {
        return _savedProfile is not null &&
               string.Equals(
                   _savedProfile.BeaconUuid,
                   SelectedBeaconUuid,
                   StringComparison.OrdinalIgnoreCase) &&
               _savedProfile.Major == BeaconMajor &&
               _savedProfile.Minor == BeaconMinor &&
               _savedProfile.Latitude == Latitude &&
               _savedProfile.Longitude == Longitude;
    }

    private bool IsCurrentProfileLoad(int loadVersion, int libraryId)
    {
        return Volatile.Read(ref _profileLoadVersion) == loadVersion &&
               _workflowState.LockedLibrary?.LibraryId == libraryId;
    }
}
