namespace IGoLibrary.Ex.Updater;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var emergencyLog = UpdaterLog.TryCreateEmergency("entry");
        try
        {
            var parsed = UpdaterCommandLine.Parse(args, AppContext.BaseDirectory);
            if (!parsed.Succeeded)
            {
                emergencyLog?.Error(
                    $"更新器命令行无效。模式={parsed.Mode}，原因={parsed.ErrorMessage}");
                if (parsed.Mode == UpdaterMode.Coordinator)
                {
                    NativeUpdaterDialog.ShowError(parsed.ErrorMessage!);
                }

                return 2;
            }

            var command = parsed.Command!;
            emergencyLog?.Info($"更新器入口已解析。模式={command.Mode}。");
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
        catch (Exception ex)
        {
            emergencyLog?.Error("更新器入口发生未处理异常。", ex);
            return 1;
        }
    }
}
