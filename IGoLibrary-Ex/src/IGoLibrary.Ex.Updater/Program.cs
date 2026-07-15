namespace IGoLibrary.Ex.Updater;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var parsed = UpdaterCommandLine.Parse(args, AppContext.BaseDirectory);
        if (!parsed.Succeeded)
        {
            if (parsed.Mode == UpdaterMode.Coordinator)
            {
                NativeUpdaterDialog.ShowError(parsed.ErrorMessage!);
            }

            return 2;
        }

        var command = parsed.Command!;
        return command.Mode switch
        {
            UpdaterMode.Bootstrap => BootstrapRunner.RunAsync(command.PipeName!)
                .GetAwaiter()
                .GetResult(),
            UpdaterMode.Cleanup => CleanupRunner.RunAsync(
                    command.RequestPath!,
                    command.WaitProcessIds)
                .GetAwaiter()
                .GetResult(),
            UpdaterMode.RecoveryWorker => RecoveryRunner.RunWorkerAsync(
                    command.RequestPath!,
                    command.RecoveryCoordinatorProcessId)
                .GetAwaiter()
                .GetResult(),
            UpdaterMode.RecoveryCoordinator => RecoveryRunner.RunCoordinatorAsync(
                    command.RequestPath!)
                .GetAwaiter()
                .GetResult(),
            UpdaterMode.Worker => WorkerRunner.RunAsync(command.RequestPath!)
                .GetAwaiter()
                .GetResult(),
            UpdaterMode.Coordinator => NativeUpdaterDialog.RunCoordinator(
                command.RequestPath!,
                command.ExternalWorker),
            _ => 2
        };
    }
}
