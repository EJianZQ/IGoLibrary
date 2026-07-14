using IGoLibrary.Ex.Updater.Core;

namespace IGoLibrary.Ex.Updater;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var pipeName = GetArgumentValue(args, "--pipe");
        if (args.Any(static value => string.Equals(value, "--bootstrap", StringComparison.Ordinal)))
        {
            if (string.IsNullOrWhiteSpace(pipeName))
            {
                return 2;
            }

            return BootstrapRunner.RunAsync(pipeName).GetAwaiter().GetResult();
        }

        var isRecoveryCoordinator = args.Any(static value =>
            string.Equals(value, "--recover", StringComparison.Ordinal));
        var requestPath = GetArgumentValue(args, "--request");
        if (string.IsNullOrWhiteSpace(requestPath) && isRecoveryCoordinator)
        {
            requestPath = Path.Combine(AppContext.BaseDirectory, "request.json");
        }

        if (string.IsNullOrWhiteSpace(requestPath))
        {
            MessageBox.Show(
                "更新请求参数无效。请返回应用后重试",
                "我去图书馆 - 更新程序",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 2;
        }

        requestPath = Path.GetFullPath(requestPath);
        if (args.Any(static value => string.Equals(value, "--cleanup", StringComparison.Ordinal)))
        {
            var processIds = GetArgumentValues(args, "--wait-pid")
                .Select(static value => int.TryParse(value, out var processId) ? processId : 0)
                .Where(static processId => processId > 0)
                .ToArray();
            return CleanupRunner.RunAsync(requestPath, processIds).GetAwaiter().GetResult();
        }

        if (args.Any(static value => string.Equals(value, "--recover-worker", StringComparison.Ordinal)))
        {
            var coordinatorText = GetArgumentValue(args, "--recovery-coordinator-pid");
            if (!int.TryParse(coordinatorText, out var coordinatorProcessId) ||
                coordinatorProcessId <= 0)
            {
                return 2;
            }

            return RecoveryRunner.RunWorkerAsync(requestPath, coordinatorProcessId)
                .GetAwaiter()
                .GetResult();
        }

        if (isRecoveryCoordinator)
        {
            return RecoveryRunner.RunCoordinatorAsync(requestPath).GetAwaiter().GetResult();
        }

        if (args.Any(static value => string.Equals(value, "--worker", StringComparison.Ordinal)))
        {
            return WorkerRunner.RunAsync(requestPath).GetAwaiter().GetResult();
        }

        ApplicationConfiguration.Initialize();
        var externalWorker = args.Any(static value =>
            string.Equals(value, "--external-worker", StringComparison.Ordinal));
        using var form = new UpdaterForm(requestPath, externalWorker);
        Application.Run(form);
        return form.ExitCode;
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
}
