using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Services;

public sealed class SettingsService(
    ISettingsRepository settingsRepository,
    IPersistentDataChangeTracker? changeTracker = null) : ISettingsService
{
    private readonly SemaphoreSlim _settingsGate = new(1, 1);
    private AppSettings? _cachedSettings;
    private long _cachedVersion = -1;

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _settingsGate.WaitAsync(cancellationToken);
        try
        {
            return await LoadUnderLockAsync(cancellationToken);
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
            await settingsRepository.SaveAsync(settings, cancellationToken);
            UpdateCacheAfterSave(settings);
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
        ArgumentNullException.ThrowIfNull(update);

        await _settingsGate.WaitAsync(cancellationToken);
        try
        {
            var current = await LoadUnderLockAsync(cancellationToken);
            var updated = update(current);
            if (updated == current)
            {
                return current;
            }

            await settingsRepository.SaveAsync(updated, cancellationToken);
            UpdateCacheAfterSave(updated);
            return updated;
        }
        finally
        {
            _settingsGate.Release();
        }
    }

    private async Task<AppSettings> LoadUnderLockAsync(CancellationToken cancellationToken)
    {
        var currentVersion = changeTracker?.Version;
        if (_cachedSettings is not null &&
            (currentVersion is null || _cachedVersion == currentVersion.Value))
        {
            return _cachedSettings;
        }

        var settings = await settingsRepository.LoadAsync(cancellationToken);
        _cachedSettings = settings;
        _cachedVersion = changeTracker?.Version ?? 0;
        return settings;
    }

    private void UpdateCacheAfterSave(AppSettings settings)
    {
        _cachedSettings = settings;
        _cachedVersion = changeTracker?.Version ?? 0;
    }
}
