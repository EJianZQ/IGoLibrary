using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using AvaloniaApplication = Avalonia.Application;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Desktop.Services;

public sealed class AppThemeService(ISettingsService settingsService) : IAppThemeService
{
    private static readonly Color DefaultAccentColor = Color.Parse("#0077FA");
    private static readonly Color LightSuccessColor = Color.Parse("#14804A");
    private static readonly Color DarkSuccessColor = Color.Parse("#4ADE80");
    private static readonly Color LightWarningColor = Color.Parse("#C27803");
    private static readonly Color DarkWarningColor = Color.Parse("#FBBF24");
    private static readonly Color LightFailureColor = Color.Parse("#C93C37");
    private static readonly Color DarkFailureColor = Color.Parse("#FB7185");
    private static readonly Color LightIdleColor = Color.Parse("#86909C");
    private static readonly Color DarkIdleColor = Color.Parse("#94A3B8");
    private static readonly Color LightLogDefaultColor = Color.Parse("#1D2129");
    private static readonly Color DarkLogDefaultColor = Color.Parse("#E2E8F0");

    private TopLevel? _topLevel;
    private IPlatformSettings? _platformSettings;
    private AppSettings _lastAppliedSettings = AppSettings.Default;

    public event EventHandler<AppThemePalette>? PaletteChanged;

    public AppThemePalette CurrentPalette { get; private set; } = CreatePalette(
        isDarkTheme: false,
        accentColor: DefaultAccentColor);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var settings = await settingsService.LoadAsync(cancellationToken);
        await ApplySettingsAsync(settings, cancellationToken);
    }

    public Task ApplySettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _lastAppliedSettings = settings;

        var app = AvaloniaApplication.Current;
        if (app is null)
        {
            CurrentPalette = CreatePalette(isDarkTheme: false, accentColor: DefaultAccentColor);
            PaletteChanged?.Invoke(this, CurrentPalette);
            return Task.CompletedTask;
        }

        var requestedVariant = settings.ThemeMode switch
        {
            AppThemeMode.Light => ThemeVariant.Light,
            AppThemeMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };

        app.RequestedThemeVariant = requestedVariant;
        RefreshThemeResources();
        return Task.CompletedTask;
    }

    public void AttachTopLevel(TopLevel topLevel)
    {
        if (ReferenceEquals(_topLevel, topLevel))
        {
            return;
        }

        if (_topLevel is not null)
        {
            _topLevel.ActualThemeVariantChanged -= OnActualThemeVariantChanged;
        }

        if (_platformSettings is not null)
        {
            _platformSettings.ColorValuesChanged -= OnPlatformColorValuesChanged;
        }

        _topLevel = topLevel;
        _platformSettings = topLevel.PlatformSettings;

        _topLevel.ActualThemeVariantChanged += OnActualThemeVariantChanged;
        if (_platformSettings is not null)
        {
            _platformSettings.ColorValuesChanged += OnPlatformColorValuesChanged;
        }

        RefreshThemeResources();
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs e)
    {
        if (_lastAppliedSettings.ThemeMode != AppThemeMode.FollowSystem)
        {
            return;
        }

        RefreshThemeResources();
    }

    private void OnPlatformColorValuesChanged(object? sender, PlatformColorValues e)
    {
        if (!_lastAppliedSettings.UseSystemAccent && _lastAppliedSettings.ThemeMode != AppThemeMode.FollowSystem)
        {
            return;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            RefreshThemeResources();
            return;
        }

        Dispatcher.UIThread.Post(RefreshThemeResources);
    }

    private void RefreshThemeResources()
    {
        var app = AvaloniaApplication.Current;
        if (app is null)
        {
            return;
        }

        var isDarkTheme = ResolveIsDarkTheme(app);
        var accentColor = ResolveAccentColor();

        UpdateBrushResource(app, "SemiAccentBrush", accentColor);
        UpdateBrushResource(app, "AppAccentBrush", accentColor);
        UpdateBrushResource(app, "SemiAccentSoftBrush", BuildAccentSurface(accentColor, isDarkTheme, 0.18, 0.12));
        UpdateBrushResource(app, "AppAccentSoftBrush", BuildAccentSurface(accentColor, isDarkTheme, 0.18, 0.12));
        UpdateBrushResource(app, "SemiAccentGlowBrush", BuildAccentSurface(accentColor, isDarkTheme, 0.12, 0.06));
        UpdateBrushResource(app, "AppAccentGlowBrush", BuildAccentSurface(accentColor, isDarkTheme, 0.12, 0.06));
        UpdateBrushResource(app, "AppAccentPanelBrush", BuildAccentSurface(accentColor, isDarkTheme, 0.16, 0.08));
        UpdateBrushResource(app, "AppAccentPanelBorderBrush", BuildAccentSurface(accentColor, isDarkTheme, 0.28, 0.16));
        UpdateBrushResource(app, "AppAccentForegroundBrush", ChooseAccentForeground(accentColor));

        var palette = CreatePalette(isDarkTheme, accentColor);
        CurrentPalette = palette;
        PaletteChanged?.Invoke(this, palette);
    }

    private bool ResolveIsDarkTheme(AvaloniaApplication app)
    {
        var actualVariant = _topLevel?.ActualThemeVariant ?? app.ActualThemeVariant;
        return actualVariant == ThemeVariant.Dark;
    }

    private Color ResolveAccentColor()
    {
        if (!_lastAppliedSettings.UseSystemAccent || _platformSettings is null)
        {
            return DefaultAccentColor;
        }

        try
        {
            return _platformSettings.GetColorValues().AccentColor1;
        }
        catch
        {
            return DefaultAccentColor;
        }
    }

    private static void UpdateBrushResource(AvaloniaApplication app, string key, Color color)
    {
        app.Resources[key] = new SolidColorBrush(color);
    }

    private static AppThemePalette CreatePalette(bool isDarkTheme, Color accentColor)
    {
        var idle = isDarkTheme ? DarkIdleColor : LightIdleColor;
        var success = isDarkTheme ? DarkSuccessColor : LightSuccessColor;
        var warning = isDarkTheme ? DarkWarningColor : LightWarningColor;
        var failure = isDarkTheme ? DarkFailureColor : LightFailureColor;

        return new AppThemePalette(
            IdleBrush: BrushFrom(idle),
            RunningBrush: BrushFrom(accentColor),
            SuccessBrush: BrushFrom(success),
            WarningBrush: BrushFrom(warning),
            FailureBrush: BrushFrom(failure),
            RunningSoftBrush: BrushFrom(BuildAccentSurface(accentColor, isDarkTheme, 0.20, 0.12)),
            SuccessSoftBrush: BrushFrom(isDarkTheme ? Color.Parse("#123021") : Color.Parse("#E8FFF1")),
            WarningSoftBrush: BrushFrom(isDarkTheme ? Color.Parse("#3A2A0E") : Color.Parse("#FFF5E7")),
            NeutralSoftBrush: BrushFrom(isDarkTheme ? Color.Parse("#182230") : Color.Parse("#F1F5F9")),
            NotificationSegmentActiveTextBrush: BrushFrom(isDarkTheme ? Color.Parse("#F8FAFC") : Color.Parse("#1D2129")),
            NotificationSegmentInactiveTextBrush: BrushFrom(isDarkTheme ? Color.Parse("#94A3B8") : Color.Parse("#86909C")),
            LogDefaultBrush: BrushFrom(isDarkTheme ? DarkLogDefaultColor : LightLogDefaultColor),
            LogSuccessBrush: BrushFrom(isDarkTheme ? Color.Parse("#4ADE80") : Color.Parse("#16A34A")),
            LogErrorBrush: BrushFrom(isDarkTheme ? Color.Parse("#F87171") : Color.Parse("#DC2626")));
    }

    private static SolidColorBrush BrushFrom(Color color) => new(color);

    private static Color BuildAccentSurface(Color accentColor, bool isDarkTheme, double darkOpacity, double lightOpacity)
    {
        var opacity = isDarkTheme ? darkOpacity : lightOpacity;
        var background = isDarkTheme ? Color.Parse("#111827") : Colors.White;
        return Blend(background, accentColor, opacity);
    }

    private static Color Blend(Color background, Color foreground, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        var inverse = 1d - amount;

        return Color.FromArgb(
            255,
            (byte)Math.Round((background.R * inverse) + (foreground.R * amount)),
            (byte)Math.Round((background.G * inverse) + (foreground.G * amount)),
            (byte)Math.Round((background.B * inverse) + (foreground.B * amount)));
    }

    private static Color ChooseAccentForeground(Color accentColor)
    {
        var luminance = ((0.299 * accentColor.R) + (0.587 * accentColor.G) + (0.114 * accentColor.B)) / 255d;
        return luminance > 0.58 ? Color.Parse("#111827") : Colors.White;
    }
}
