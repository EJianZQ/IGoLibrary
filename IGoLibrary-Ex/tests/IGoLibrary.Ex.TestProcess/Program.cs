using System.Diagnostics;

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

    private static int WaitForRelease(string readyPath, string releasePath)
    {
        File.WriteAllText(readyPath, "ready");
        var stopwatch = Stopwatch.StartNew();
        while (!File.Exists(releasePath))
        {
            if (stopwatch.Elapsed >= ReleaseTimeout)
            {
                return 3;
            }

            Thread.Sleep(10);
        }

        return 0;
    }
}
