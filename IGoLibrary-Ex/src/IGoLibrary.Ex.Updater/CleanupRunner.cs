using System.Diagnostics;
using IGoLibrary.Ex.Updater.Core;

namespace IGoLibrary.Ex.Updater;

internal static class CleanupRunner
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromMinutes(5);

    public static async Task<int> RunAsync(
        string requestPath,
        IReadOnlyList<int> processIds)
    {
        try
        {
            var request = UpdateJsonFile.Read(requestPath, UpdateJsonTypeInfo.TransactionRequest);
            UpdateTransaction.ValidateRequestFile(requestPath, request);
            var secureDirectory = UpdateTransaction.GetSecureWorkingDirectory(request);
            if (!string.Equals(
                    Path.GetFullPath(request.WorkingDirectory),
                    Path.GetFullPath(secureDirectory),
                    StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            var processPath = Environment.ProcessPath
                              ?? throw new InvalidOperationException("无法确定清理程序路径");
            var expectedUpdater = Path.Combine(
                request.InstallationDirectory,
                UpdateProtocol.UpdaterExecutableName);
            EnsureExactPath(processPath, expectedUpdater);

            using var timeout = new CancellationTokenSource(WaitTimeout);
            foreach (var processId in processIds.Distinct())
            {
                await WaitForProcessExitAsync(processId, timeout.Token);
            }

            await WaitForCleanupAuthorizationAsync(request, timeout.Token);

            if (Directory.Exists(secureDirectory))
            {
                Directory.Delete(secureDirectory, recursive: true);
            }

            var controlDirectory = Path.GetDirectoryName(request.HealthReportPath)
                                   ?? throw new InvalidDataException("无法确定更新控制目录");
            UpdateJsonFile.WriteAtomic(
                Path.Combine(controlDirectory, "cleanup-complete.json"),
                new UpdateCleanupCompletion(
                    UpdateProtocol.SchemaVersion,
                    request.TransactionId,
                    DateTimeOffset.UtcNow),
                UpdateJsonTypeInfo.CleanupCompletion);

            return 0;
        }
        catch
        {
            return 1;
        }
    }

    private static async Task WaitForCleanupAuthorizationAsync(
        UpdateTransactionRequest request,
        CancellationToken cancellationToken)
    {
        var controlDirectory = Path.GetDirectoryName(request.HealthReportPath)
                               ?? throw new InvalidDataException("无法确定更新控制目录");
        var authorizationPath = Path.Combine(controlDirectory, "cleanup-ready.json");
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(authorizationPath))
            {
                try
                {
                    var authorization = UpdateJsonFile.Read(
                        authorizationPath,
                        UpdateJsonTypeInfo.CleanupAuthorization);
                    if (authorization.SchemaVersion == UpdateProtocol.SchemaVersion &&
                        string.Equals(
                            authorization.TransactionId,
                            request.TransactionId,
                            StringComparison.Ordinal))
                    {
                        return;
                    }
                }
                catch (IOException)
                {
                }
            }

            await Task.Delay(250, cancellationToken);
        }
    }

    private static async Task WaitForProcessExitAsync(
        int processId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (ArgumentException)
        {
        }
    }

    private static void EnsureExactPath(string actual, string expected)
    {
        if (!string.Equals(
                Path.GetFullPath(actual),
                Path.GetFullPath(expected),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("更新清理程序路径不符合事务约束");
        }
    }
}
