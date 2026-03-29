using System.Diagnostics;
using System.Text.Json;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Domain.Models;
using IGoLibrary.Ex.Infrastructure.Persistence;

namespace IGoLibrary.Ex.Infrastructure.Security;

public sealed class MacKeychainCredentialStore : ICredentialStore
{
    private const string ServiceName = "IGoLibrary-Ex";
    private const string AccountName = "session";

    public async Task SaveSessionAsync(SessionCredentials credentials, CancellationToken cancellationToken = default)
    {
        await ClearSessionAsync(cancellationToken);
        var payload = JsonSerializer.Serialize(credentials, AppJson.Default);

        var psi = new ProcessStartInfo("security")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        psi.ArgumentList.Add("add-generic-password");
        psi.ArgumentList.Add("-a");
        psi.ArgumentList.Add(AccountName);
        psi.ArgumentList.Add("-s");
        psi.ArgumentList.Add(ServiceName);
        psi.ArgumentList.Add("-w");
        psi.ArgumentList.Add(payload);
        psi.ArgumentList.Add("-U");

        await RunAsync(psi, cancellationToken);
    }

    public async Task<SessionCredentials?> LoadSessionAsync(CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo("security")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        psi.ArgumentList.Add("find-generic-password");
        psi.ArgumentList.Add("-a");
        psi.ArgumentList.Add(AccountName);
        psi.ArgumentList.Add("-s");
        psi.ArgumentList.Add(ServiceName);
        psi.ArgumentList.Add("-w");

        var output = await RunAsync(psi, cancellationToken, tolerateFailure: true);
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        return JsonSerializer.Deserialize<SessionCredentials>(output, AppJson.Default);
    }

    public async Task ClearSessionAsync(CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo("security")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        psi.ArgumentList.Add("delete-generic-password");
        psi.ArgumentList.Add("-a");
        psi.ArgumentList.Add(AccountName);
        psi.ArgumentList.Add("-s");
        psi.ArgumentList.Add(ServiceName);

        await RunAsync(psi, cancellationToken, tolerateFailure: true);
    }

    private static async Task<string> RunAsync(ProcessStartInfo psi, CancellationToken cancellationToken, bool tolerateFailure = false)
    {
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("无法启动 security 命令。");
        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (!tolerateFailure && process.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "Keychain 操作失败。" : error);
        }

        return output.Trim();
    }
}
