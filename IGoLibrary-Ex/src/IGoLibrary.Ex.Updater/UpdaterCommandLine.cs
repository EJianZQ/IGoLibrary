namespace IGoLibrary.Ex.Updater;

internal enum UpdaterMode
{
    Bootstrap,
    Cleanup,
    RecoveryWorker,
    RecoveryCoordinator,
    Worker,
    Coordinator
}

internal sealed record UpdaterCommand(
    UpdaterMode Mode,
    string? RequestPath,
    string? PipeName,
    bool ExternalWorker,
    int RecoveryCoordinatorProcessId,
    IReadOnlyList<int> WaitProcessIds);

internal sealed record UpdaterCommandLineResult(
    UpdaterMode Mode,
    UpdaterCommand? Command,
    string? ErrorMessage)
{
    public bool Succeeded => Command is not null;
}

internal static class UpdaterCommandLine
{
    public static UpdaterCommandLineResult Parse(
        IReadOnlyList<string> args,
        string baseDirectory)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

        var mode = GetMode(args);
        if (mode == UpdaterMode.Bootstrap)
        {
            var pipeName = GetArgumentValue(args, "--pipe");
            return string.IsNullOrWhiteSpace(pipeName)
                ? Failure(mode, "更新引导管道参数无效")
                : Success(mode, requestPath: null, pipeName, externalWorker: false, 0, []);
        }

        var requestPath = GetArgumentValue(args, "--request");
        if (string.IsNullOrWhiteSpace(requestPath) && mode == UpdaterMode.RecoveryCoordinator)
        {
            requestPath = Path.Combine(baseDirectory, "request.json");
        }

        if (string.IsNullOrWhiteSpace(requestPath))
        {
            return Failure(mode, "更新请求参数无效。请返回应用后重试");
        }

        try
        {
            requestPath = Path.GetFullPath(requestPath);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Failure(mode, $"更新请求路径无效：{exception.Message}");
        }

        if (mode == UpdaterMode.RecoveryWorker)
        {
            var coordinatorText = GetArgumentValue(args, "--recovery-coordinator-pid");
            if (!int.TryParse(coordinatorText, out var coordinatorProcessId) ||
                coordinatorProcessId <= 0)
            {
                return Failure(mode, "恢复协调器进程参数无效");
            }

            return Success(mode, requestPath, null, false, coordinatorProcessId, []);
        }

        var processIds = mode == UpdaterMode.Cleanup
            ? GetArgumentValues(args, "--wait-pid")
                .Select(static value => int.TryParse(value, out var processId) ? processId : 0)
                .Where(static processId => processId > 0)
                .ToArray()
            : [];
        var externalWorker = mode == UpdaterMode.Coordinator &&
                             HasArgument(args, "--external-worker");
        return Success(mode, requestPath, null, externalWorker, 0, processIds);
    }

    private static UpdaterMode GetMode(IReadOnlyList<string> args)
    {
        if (HasArgument(args, "--bootstrap"))
        {
            return UpdaterMode.Bootstrap;
        }

        if (HasArgument(args, "--cleanup"))
        {
            return UpdaterMode.Cleanup;
        }

        if (HasArgument(args, "--recover-worker"))
        {
            return UpdaterMode.RecoveryWorker;
        }

        if (HasArgument(args, "--recover"))
        {
            return UpdaterMode.RecoveryCoordinator;
        }

        return HasArgument(args, "--worker")
            ? UpdaterMode.Worker
            : UpdaterMode.Coordinator;
    }

    private static bool HasArgument(IReadOnlyList<string> args, string name)
    {
        return args.Any(value => string.Equals(value, name, StringComparison.Ordinal));
    }

    private static string? GetArgumentValue(IReadOnlyList<string> args, string name)
    {
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.Ordinal))
            {
                return args[index + 1];
            }
        }

        return null;
    }

    private static IEnumerable<string> GetArgumentValues(
        IReadOnlyList<string> args,
        string name)
    {
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.Ordinal))
            {
                yield return args[index + 1];
                index++;
            }
        }
    }

    private static UpdaterCommandLineResult Success(
        UpdaterMode mode,
        string? requestPath,
        string? pipeName,
        bool externalWorker,
        int recoveryCoordinatorProcessId,
        IReadOnlyList<int> waitProcessIds)
    {
        return new UpdaterCommandLineResult(
            mode,
            new UpdaterCommand(
                mode,
                requestPath,
                pipeName,
                externalWorker,
                recoveryCoordinatorProcessId,
                waitProcessIds),
            null);
    }

    private static UpdaterCommandLineResult Failure(UpdaterMode mode, string errorMessage)
    {
        return new UpdaterCommandLineResult(mode, null, errorMessage);
    }
}
