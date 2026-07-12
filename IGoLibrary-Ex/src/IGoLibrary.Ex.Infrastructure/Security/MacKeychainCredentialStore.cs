using System.Diagnostics;
using System.Text.Json;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Domain.Models;
using IGoLibrary.Ex.Infrastructure.Persistence;

namespace IGoLibrary.Ex.Infrastructure.Security;

public sealed class MacKeychainCredentialStore : ICredentialStore
{
    private const int ItemNotFoundExitCode = 44;
    private const string ServiceName = "IGoLibrary-Ex";
    private const string SessionAccountName = "session";
    private const string RemoteCheckInAccountName = "remote-check-in";

    public async Task SaveSessionAsync(SessionCredentials credentials, CancellationToken cancellationToken = default)
        => await SaveAsync(SessionAccountName, credentials, cancellationToken);

    public Task<SessionCredentials?> LoadSessionAsync(CancellationToken cancellationToken = default)
        => LoadAsync<SessionCredentials>(SessionAccountName, cancellationToken);

    public Task ClearSessionAsync(CancellationToken cancellationToken = default)
        => ClearAsync(SessionAccountName, cancellationToken);

    public async Task SaveRemoteCheckInSessionAsync(
        RemoteCheckInSessionCredentials credentials,
        CancellationToken cancellationToken = default)
        => await SaveAsync(RemoteCheckInAccountName, credentials, cancellationToken);

    public Task<RemoteCheckInSessionCredentials?> LoadRemoteCheckInSessionAsync(
        CancellationToken cancellationToken = default)
        => LoadAsync<RemoteCheckInSessionCredentials>(RemoteCheckInAccountName, cancellationToken);

    public Task ClearRemoteCheckInSessionAsync(CancellationToken cancellationToken = default)
        => ClearAsync(RemoteCheckInAccountName, cancellationToken);

    private static async Task SaveAsync<T>(
        string accountName,
        T value,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(value, AppJson.Default);

        var psi = new ProcessStartInfo("security")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        psi.ArgumentList.Add("add-generic-password");
        psi.ArgumentList.Add("-a");
        psi.ArgumentList.Add(accountName);
        psi.ArgumentList.Add("-s");
        psi.ArgumentList.Add(ServiceName);
        psi.ArgumentList.Add("-w");
        psi.ArgumentList.Add(payload);
        psi.ArgumentList.Add("-U");

        await RunAsync(psi, cancellationToken);
    }

    private static async Task<T?> LoadAsync<T>(string accountName, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo("security")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        psi.ArgumentList.Add("find-generic-password");
        psi.ArgumentList.Add("-a");
        psi.ArgumentList.Add(accountName);
        psi.ArgumentList.Add("-s");
        psi.ArgumentList.Add(ServiceName);
        psi.ArgumentList.Add("-w");

        var output = await RunAsync(psi, cancellationToken, tolerateItemNotFound: true);
        if (string.IsNullOrWhiteSpace(output))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(output, AppJson.Default);
    }

    private static async Task ClearAsync(string accountName, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo("security")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        psi.ArgumentList.Add("delete-generic-password");
        psi.ArgumentList.Add("-a");
        psi.ArgumentList.Add(accountName);
        psi.ArgumentList.Add("-s");
        psi.ArgumentList.Add(ServiceName);

        await RunAsync(psi, cancellationToken, tolerateItemNotFound: true);
    }

    private static async Task<string> RunAsync(
        ProcessStartInfo psi,
        CancellationToken cancellationToken,
        bool tolerateItemNotFound = false)
    {
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("无法启动 security 命令");
        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        EnsureSuccessfulExit(process.ExitCode, error, tolerateItemNotFound);

        return output.Trim();
    }

    internal static void EnsureSuccessfulExit(
        int exitCode,
        string? error,
        bool tolerateItemNotFound)
    {
        if (exitCode == 0 || tolerateItemNotFound && exitCode == ItemNotFoundExitCode)
        {
            return;
        }

        throw new InvalidOperationException(
            string.IsNullOrWhiteSpace(error) ? "Keychain 操作失败" : error.Trim());
    }
}
