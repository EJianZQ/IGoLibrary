using System.Diagnostics;

namespace IGoLibrary.Ex.Desktop.Services;

public sealed class ExternalLinkService : IExternalLinkService
{
    public Task OpenAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var process = Process.Start(new ProcessStartInfo(uri.ToString())
        {
            UseShellExecute = true
        });
        if (process is null)
        {
            throw new InvalidOperationException("无法启动默认浏览器。");
        }

        return Task.CompletedTask;
    }
}
