using IGoLibrary.Ex.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Desktop.Services;

internal sealed class WindowsPortableUpdateService(
    IUpdateInstallGuard installGuard,
    IAppVersionProvider appVersionProvider,
    AppWindowService appWindowService,
    WindowsUpdatePackagePreparationService packagePreparationService,
    WindowsUpdateHandoffService handoffService,
    WindowsUpdateWorkspaceManager workspaceManager,
    ILogger<WindowsPortableUpdateOperation> operationLogger) : IWindowsPortableUpdateService
{
    public IWindowsPortableUpdateOperation CreateOperation(ReleaseUpdateInfo release)
    {
        ArgumentNullException.ThrowIfNull(release);
        return new WindowsPortableUpdateOperation(
            release,
            installGuard,
            appVersionProvider,
            appWindowService,
            packagePreparationService,
            handoffService,
            workspaceManager,
            operationLogger);
    }
}
