using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Services;

public sealed class SettingsService(ISettingsRepository settingsRepository) : ISettingsService
{
    private readonly SemaphoreSlim _settingsGate = new(1, 1);

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _settingsGate.WaitAsync(cancellationToken);
        try
        {
            return await settingsRepository.LoadAsync(cancellationToken);
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
            var current = await settingsRepository.LoadAsync(cancellationToken);
            var updated = update(current);
            if (updated == current)
            {
                return current;
            }

            await settingsRepository.SaveAsync(updated, cancellationToken);
            return updated;
        }
        finally
        {
            _settingsGate.Release();
        }
    }
}
