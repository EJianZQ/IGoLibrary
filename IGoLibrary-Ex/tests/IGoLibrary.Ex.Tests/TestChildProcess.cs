using System.Diagnostics;
using IGoLibrary.Ex.TestProcess;

namespace IGoLibrary.Ex.Tests;

internal sealed class TestChildProcess : IAsyncDisposable
{
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(10);

    private readonly Process _process;
    private readonly string _directory;
    private readonly string _releasePath;
    private bool _released;

    private TestChildProcess(Process process, string directory, string releasePath)
    {
        _process = process;
        _directory = directory;
        _releasePath = releasePath;
    }

    public int Id => _process.Id;

    public static async Task<TestChildProcess> StartAsync(string mode, params string[] modeArguments)
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "IGoLibrary-Ex-Tests",
            "child-process",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var readyPath = Path.Combine(directory, "ready");
        var releasePath = Path.Combine(directory, "release");
        var dotnetHost = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
        var startInfo = new ProcessStartInfo(dotnetHost)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add(typeof(TestProcessMarker).Assembly.Location);
        startInfo.ArgumentList.Add(mode);
        foreach (var argument in modeArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.ArgumentList.Add(readyPath);
        startInfo.ArgumentList.Add(releasePath);

        var process = Process.Start(startInfo)
                      ?? throw new InvalidOperationException("Unable to start the test child process.");
        var child = new TestChildProcess(process, directory, releasePath);
        try
        {
            await child.WaitUntilReadyAsync(readyPath);
            return child;
        }
        catch
        {
            await child.DisposeAsync();
            throw;
        }
    }

    public void Release()
    {
        if (_released)
        {
            return;
        }

        File.WriteAllText(_releasePath, "release");
        _released = true;
    }

    public async Task WaitForSuccessfulExitAsync()
    {
        await _process.WaitForExitAsync().WaitAsync(OperationTimeout);
        if (_process.ExitCode == 0)
        {
            return;
        }

        var standardOutput = await _process.StandardOutput.ReadToEndAsync();
        var standardError = await _process.StandardError.ReadToEndAsync();
        throw new InvalidOperationException(
            $"Test child process exited with code {_process.ExitCode}.{Environment.NewLine}" +
            $"stdout: {standardOutput}{Environment.NewLine}stderr: {standardError}");
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                Release();
                try
                {
                    await _process.WaitForExitAsync().WaitAsync(OperationTimeout);
                }
                catch (TimeoutException)
                {
                    _process.Kill(entireProcessTree: true);
                    await _process.WaitForExitAsync();
                }
            }
        }
        finally
        {
            _process.Dispose();
            try
            {
                Directory.Delete(_directory, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private async Task WaitUntilReadyAsync(string readyPath)
    {
        using var timeout = new CancellationTokenSource(OperationTimeout);
        while (!File.Exists(readyPath))
        {
            if (_process.HasExited)
            {
                await WaitForSuccessfulExitAsync();
                throw new InvalidOperationException("Test child process exited before becoming ready.");
            }

            await Task.Delay(10, timeout.Token);
        }
    }
}
