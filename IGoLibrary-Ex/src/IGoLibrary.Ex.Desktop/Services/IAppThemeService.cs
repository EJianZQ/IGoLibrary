using Avalonia.Controls;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Desktop.Services;

public interface IAppThemeService
{
    event EventHandler<AppThemePalette>? PaletteChanged;

    AppThemePalette CurrentPalette { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task ApplySettingsAsync(AppSettings settings, CancellationToken cancellationToken = default);

    void AttachTopLevel(TopLevel topLevel);
}
