using IGoLibrary.Ex.Application.Abstractions;

namespace IGoLibrary.Ex.Application.Services;

internal sealed class DefaultAppSettingsDefaults : IAppSettingsDefaults
{
    public AppSettings CreateDefault()
    {
        return AppSettings.Default;
    }
}
