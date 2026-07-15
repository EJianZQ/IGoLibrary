using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace IGoLibrary.Ex.TestProcess;

public sealed class TestProcessMarker;

internal static class Program
{
    private static readonly TimeSpan ReleaseTimeout = TimeSpan.FromSeconds(30);

    public static int Main(string[] args)
    {
        try
        {
            return args switch
            {
                ["wait-for-release", var readyPath, var releasePath] =>
                    WaitForRelease(readyPath, releasePath),
                ["hold-mutex", var mutexName, var readyPath, var releasePath] =>
                    HoldMutex(mutexName, readyPath, releasePath),
                ["--update-transaction", var transactionId] =>
                    RunUpdateHealthProbe(transactionId),
                _ => 2
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static int HoldMutex(string mutexName, string readyPath, string releasePath)
    {
        using var mutex = new Mutex(false, mutexName, new NamedWaitHandleOptions
        {
            CurrentUserOnly = true,
            CurrentSessionOnly = false
        });
        mutex.WaitOne();
        try
        {
            return WaitForRelease(readyPath, releasePath);
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    private static int WaitForRelease(
        string readyPath,
        string releasePath,
        TimeSpan? timeout = null)
    {
        File.WriteAllText(readyPath, "ready");
        var stopwatch = Stopwatch.StartNew();
        while (!File.Exists(releasePath))
        {
            if (stopwatch.Elapsed >= (timeout ?? ReleaseTimeout))
            {
                return 3;
            }

            Thread.Sleep(10);
        }

        return 0;
    }

    private static int RunUpdateHealthProbe(string transactionId)
    {
        if (!Guid.TryParseExact(transactionId, "N", out _))
        {
            return 2;
        }

        var controlDirectory = Environment.GetEnvironmentVariable(
            "IGOLIBRARY_TEST_UPDATE_CONTROL_DIRECTORY");
        var targetVersion = Environment.GetEnvironmentVariable(
            "IGOLIBRARY_TEST_TARGET_VERSION");
        var readyPath = Environment.GetEnvironmentVariable(
            "IGOLIBRARY_TEST_NEW_APP_READY_PATH");
        var releasePath = Environment.GetEnvironmentVariable(
            "IGOLIBRARY_TEST_NEW_APP_RELEASE_PATH");
        var mode = Environment.GetEnvironmentVariable(
                       "IGOLIBRARY_TEST_HEALTH_MODE") ??
                   "success";
        if (string.IsNullOrWhiteSpace(controlDirectory) ||
            string.IsNullOrWhiteSpace(targetVersion) ||
            string.IsNullOrWhiteSpace(readyPath) ||
            string.IsNullOrWhiteSpace(releasePath) ||
            !string.Equals(
                Path.GetFileName(Path.GetFullPath(controlDirectory)),
                transactionId,
                StringComparison.Ordinal))
        {
            return 2;
        }

        Directory.CreateDirectory(controlDirectory);
        if (string.Equals(mode, "crash", StringComparison.Ordinal))
        {
            File.WriteAllText(readyPath, "crash");
            return 9;
        }

        if (string.Equals(mode, "success", StringComparison.Ordinal))
        {
            var report = new
            {
                schemaVersion = 2,
                transactionId,
                version = targetVersion,
                processId = Environment.ProcessId,
                createdAtUtc = DateTimeOffset.UtcNow
            };
            var json = JsonSerializer.Serialize(
                report,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)
                {
                    WriteIndented = true
                });
            File.WriteAllText(
                Path.Combine(controlDirectory, "health.json"),
                json,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        return WaitForRelease(
            readyPath,
            releasePath,
            string.Equals(mode, "no-health", StringComparison.Ordinal)
                ? TimeSpan.FromSeconds(90)
                : null);
    }
}
