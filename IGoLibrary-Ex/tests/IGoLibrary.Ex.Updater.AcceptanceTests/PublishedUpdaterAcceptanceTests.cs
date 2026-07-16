using System.Diagnostics;
using IGoLibrary.Ex.Updater.Core;

namespace IGoLibrary.Ex.Updater.AcceptanceTests;

public sealed class PublishedUpdaterAcceptanceTests
{
    [Fact]
    public async Task PublishedAotWorker_CommitsAndPreservesTools_InUnicodeLongPath()
    {
        await using var scenario = new AcceptanceDirectory("worker-commit");
        var aotUpdater = PublishedUpdaterEnvironment.AotUpdaterPath;
        await PublishedUpdaterTestHarness.CreateInstallationAsync(
            scenario,
            "1.0.0",
            aotUpdater);

        await PublishedUpdaterTestHarness.RunWorkerTransactionAsync(
            scenario,
            "1.0.0",
            "1.0.1",
            aotUpdater,
            aotUpdater,
            UpdateDecisionKind.Commit);
    }

    [Fact]
    public async Task PublishedAotWorker_InstallsManagedCloudflaredAndPreservesOtherTools()
    {
        await using var scenario = new AcceptanceDirectory("worker-cloudflared-commit");
        var aotUpdater = PublishedUpdaterEnvironment.AotUpdaterPath;
        await PublishedUpdaterTestHarness.CreateInstallationAsync(
            scenario,
            "1.0.0",
            aotUpdater);
        var customToolPath = Path.Combine(
            scenario.InstallationDirectory,
            "tools",
            "cloudflared",
            "custom.json");
        Directory.CreateDirectory(Path.GetDirectoryName(customToolPath)!);
        await File.WriteAllTextAsync(customToolPath, "custom-tool-content");

        await PublishedUpdaterTestHarness.RunWorkerTransactionAsync(
            scenario,
            "1.0.0",
            "1.0.1",
            aotUpdater,
            aotUpdater,
            UpdateDecisionKind.Commit,
            includeManagedCloudflared: true);

        Assert.Equal("custom-tool-content", await File.ReadAllTextAsync(customToolPath));
    }

    [Fact]
    public async Task PublishedAotWorker_RollbackRestoresUserReplacedCloudflared()
    {
        await using var scenario = new AcceptanceDirectory("worker-cloudflared-rollback");
        var aotUpdater = PublishedUpdaterEnvironment.AotUpdaterPath;
        await PublishedUpdaterTestHarness.CreateInstallationAsync(
            scenario,
            "1.0.0",
            aotUpdater);
        foreach (var (relativePath, content) in new[]
                 {
                     (UpdateProtocol.ManagedCloudflaredExecutablePath, "user-cloudflared"),
                     (UpdateProtocol.ManagedCloudflaredLicensePath, "user-license"),
                     (UpdateProtocol.ManagedCloudflaredNoticesPath, "user-notices")
                 })
        {
            var path = UpdatePathSafety.GetSafeChildPath(
                scenario.InstallationDirectory,
                relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, content);
        }

        await PublishedUpdaterTestHarness.RunWorkerTransactionAsync(
            scenario,
            "1.0.0",
            "1.0.1",
            aotUpdater,
            aotUpdater,
            UpdateDecisionKind.Rollback,
            includeManagedCloudflared: true);

        Assert.Equal("user-cloudflared", await File.ReadAllTextAsync(
            UpdatePathSafety.GetSafeChildPath(
                scenario.InstallationDirectory,
                UpdateProtocol.ManagedCloudflaredExecutablePath)));
        Assert.Equal("user-license", await File.ReadAllTextAsync(
            UpdatePathSafety.GetSafeChildPath(
                scenario.InstallationDirectory,
                UpdateProtocol.ManagedCloudflaredLicensePath)));
        Assert.Equal("user-notices", await File.ReadAllTextAsync(
            UpdatePathSafety.GetSafeChildPath(
                scenario.InstallationDirectory,
                UpdateProtocol.ManagedCloudflaredNoticesPath)));
    }

    [Fact]
    public async Task PublishedAotWorker_RollsBackAfterExplicitDecision()
    {
        await using var scenario = new AcceptanceDirectory("worker-rollback");
        var aotUpdater = PublishedUpdaterEnvironment.AotUpdaterPath;
        await PublishedUpdaterTestHarness.CreateInstallationAsync(
            scenario,
            "1.0.0",
            aotUpdater);

        await PublishedUpdaterTestHarness.RunWorkerTransactionAsync(
            scenario,
            "1.0.0",
            "1.0.1",
            aotUpdater,
            aotUpdater,
            UpdateDecisionKind.Rollback);
    }

    [Fact]
    public async Task PublishedAotWorker_RejectsArchiveAndManifestTamperingBeforeMutation()
    {
        await using var scenario = new AcceptanceDirectory("tamper-before-apply");
        var aotUpdater = PublishedUpdaterEnvironment.AotUpdaterPath;
        await PublishedUpdaterTestHarness.CreateInstallationAsync(
            scenario,
            "1.0.0",
            aotUpdater);

        await PublishedUpdaterTestHarness.RunWorkerTransactionAsync(
            scenario,
            "1.0.0",
            "1.0.1",
            aotUpdater,
            aotUpdater,
            decision: null,
            corruptArchiveAfterDigest: true);

        await PublishedUpdaterTestHarness.RunWorkerTransactionAsync(
            scenario,
            "1.0.0",
            "1.0.1",
            aotUpdater,
            aotUpdater,
            decision: null,
            corruptPayloadAfterManifest: true);

        await PublishedUpdaterTestHarness.RunWorkerTransactionAsync(
            scenario,
            "1.0.0",
            "1.0.1",
            aotUpdater,
            aotUpdater,
            decision: null,
            corruptPayloadAfterManifest: true,
            corruptManagedCloudflaredAfterManifest: true);
    }

    [Fact]
    public async Task PublishedAotRecovery_RestoresApplyInterruptedBeforeDecision()
    {
        await using var scenario = new AcceptanceDirectory("interrupted-recovery");
        var aotUpdater = PublishedUpdaterEnvironment.AotUpdaterPath;
        await PublishedUpdaterTestHarness.CreateInstallationAsync(
            scenario,
            "1.0.0",
            aotUpdater);
        var artifacts = await PublishedUpdaterTestHarness.CreateTransactionArtifactsAsync(
            scenario,
            "1.0.1",
            aotUpdater,
            aotUpdater);
        using var currentProcess = Process.GetCurrentProcess();
        var request = PublishedUpdaterTestHarness.CreateRequest(
            scenario,
            artifacts,
            "1.0.0",
            "1.0.1",
            currentProcess.Id,
            new DateTimeOffset(
                currentProcess.StartTime.ToUniversalTime(),
                TimeSpan.Zero));
        PublishedUpdaterTestHarness.WriteRequest(artifacts, request);

        await UpdateTransaction.PrepareCandidateFromArchiveAsync(request);
        UpdateTransaction.Apply(request);
        Assert.True(Directory.Exists(request.BackupDirectory));
        await PublishedUpdaterTestHarness.AssertInstalledVersionAsync(
            scenario.InstallationDirectory,
            "1.0.1",
            aotUpdater,
            expectPreservedTools: true,
            expectedManagedCloudflaredVersion: "1.0.1");

        using var recovery = PublishedUpdaterTestHarness.StartProcess(
            artifacts.TransactionUpdaterPath,
            ["--recover", "--request", artifacts.RequestPath],
            artifacts.ControlDirectory);
        Assert.Equal(
            0,
            await PublishedUpdaterTestHarness.WaitForExitCodeAsync(
                recovery,
                TimeSpan.FromMinutes(2)));
        await PublishedUpdaterTestHarness.AssertInstalledVersionAsync(
            scenario.InstallationDirectory,
            "1.0.0",
            aotUpdater,
            expectPreservedTools: true);
        PublishedUpdaterTestHarness.AssertManagedCloudflaredAbsent(
            scenario.InstallationDirectory);
        Assert.False(Directory.Exists(request.BackupDirectory));
        Assert.False(Directory.Exists(request.CandidateDirectory));
    }

    [Fact]
    public async Task ManagedUpdaterInstallsAot_ThenInstalledAotInstallsNextVersion()
    {
        await using var scenario = new AcceptanceDirectory("managed-to-aot-to-aot");
        var aotUpdater = PublishedUpdaterEnvironment.AotUpdaterPath;
        var managedUpdater = PublishedUpdaterEnvironment.ManagedUpdaterBaselinePath;
        Assert.NotEqual(
            await PublishedUpdaterTestHarness.ComputeSha256Async(managedUpdater),
            await PublishedUpdaterTestHarness.ComputeSha256Async(aotUpdater));
        await PublishedUpdaterTestHarness.CreateInstallationAsync(
            scenario,
            "1.0.0",
            managedUpdater);

        await PublishedUpdaterTestHarness.RunWorkerTransactionAsync(
            scenario,
            "1.0.0",
            "1.0.1",
            aotUpdater,
            managedUpdater,
            UpdateDecisionKind.Commit,
            includeManagedCloudflared: false);

        var installedAotUpdater = Path.Combine(
            scenario.InstallationDirectory,
            UpdateProtocol.UpdaterExecutableName);
        await PublishedUpdaterTestHarness.RunWorkerTransactionAsync(
            scenario,
            "1.0.1",
            "1.0.2",
            aotUpdater,
            installedAotUpdater,
            UpdateDecisionKind.Commit);
        await PublishedUpdaterTestHarness.AssertInstalledVersionAsync(
            scenario.InstallationDirectory,
            "1.0.2",
            aotUpdater,
            expectPreservedTools: true,
            expectedManagedCloudflaredVersion: "1.0.2");
    }

    [Fact]
    public Task PublishedAotCoordinator_CommitsHealthyNewProcess()
    {
        return RunCoordinatorScenarioAsync(
            "coordinator-success",
            healthMode: "success",
            invalidEntryExecutable: false,
            expectSuccess: true);
    }

    [Fact]
    public Task PublishedAotCoordinator_RollsBackWhenNewProcessCrashes()
    {
        return RunCoordinatorScenarioAsync(
            "coordinator-crash",
            healthMode: "crash",
            invalidEntryExecutable: false,
            expectSuccess: false);
    }

    [Fact]
    public Task PublishedAotCoordinator_RollsBackAfterHealthTimeout()
    {
        return RunCoordinatorScenarioAsync(
            "coordinator-health-timeout",
            healthMode: "no-health",
            invalidEntryExecutable: false,
            expectSuccess: false);
    }

    [Fact]
    public Task PublishedAotCoordinator_RollsBackWhenNewExecutableCannotStart()
    {
        return RunCoordinatorScenarioAsync(
            "coordinator-start-failure",
            healthMode: "success",
            invalidEntryExecutable: true,
            expectSuccess: false);
    }

    private static async Task RunCoordinatorScenarioAsync(
        string scenarioName,
        string healthMode,
        bool invalidEntryExecutable,
        bool expectSuccess)
    {
        await using var scenario = new AcceptanceDirectory(scenarioName);
        var aotUpdater = PublishedUpdaterEnvironment.AotUpdaterPath;
        await PublishedUpdaterTestHarness.CreateInstallationAsync(
            scenario,
            "1.0.0",
            aotUpdater);
        var artifacts = await PublishedUpdaterTestHarness.CreateTransactionArtifactsAsync(
            scenario,
            "1.0.1",
            aotUpdater,
            aotUpdater,
            invalidEntryExecutable: invalidEntryExecutable);
        await using var parent = await ParentProcessLease.StartAsync(
            scenario.InstallationDirectory,
            artifacts.ControlDirectory);
        var request = PublishedUpdaterTestHarness.CreateRequest(
            scenario,
            artifacts,
            "1.0.0",
            "1.0.1",
            parent.ProcessId,
            parent.StartedAtUtc);
        PublishedUpdaterTestHarness.WriteRequest(artifacts, request);

        var newApplicationReadyPath = Path.Combine(
            artifacts.ControlDirectory,
            "test-new-app-ready.txt");
        var newApplicationReleasePath = Path.Combine(
            artifacts.ControlDirectory,
            "test-new-app-release.txt");
        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["IGOLIBRARY_TEST_UPDATE_CONTROL_DIRECTORY"] = artifacts.ControlDirectory,
            ["IGOLIBRARY_TEST_TARGET_VERSION"] = "1.0.1",
            ["IGOLIBRARY_TEST_NEW_APP_READY_PATH"] = newApplicationReadyPath,
            ["IGOLIBRARY_TEST_NEW_APP_RELEASE_PATH"] = newApplicationReleasePath,
            ["IGOLIBRARY_TEST_HEALTH_MODE"] = healthMode
        };
        using var coordinator = PublishedUpdaterTestHarness.StartProcess(
            artifacts.TransactionUpdaterPath,
            ["--request", artifacts.RequestPath],
            artifacts.ControlDirectory,
            environment,
            createNoWindow: false);
        try
        {
            await PublishedUpdaterTestHarness.WaitForCoordinatorReadyAsync(
                request,
                coordinator,
                TimeSpan.FromMinutes(1));
            parent.Release();
            await parent.WaitForExitAsync(TimeSpan.FromSeconds(30));

            if (expectSuccess)
            {
                await PublishedUpdaterTestHarness.WaitForFileAsync(
                    newApplicationReadyPath,
                    TimeSpan.FromMinutes(1),
                    "新应用健康就绪信号",
                    coordinator);
                var healthReport = await UpdateJsonFile.ReadAsync(
                    request.HealthReportPath,
                    UpdateJsonTypeInfo.HealthReport);
                Assert.Equal(
                    0,
                    await PublishedUpdaterTestHarness.WaitForExitCodeAsync(
                        coordinator,
                        TimeSpan.FromMinutes(2)));
                File.WriteAllText(newApplicationReleasePath, "release");
                await WaitForProcessToExitIfPresentAsync(
                    healthReport.ProcessId,
                    TimeSpan.FromSeconds(30));
                await PublishedUpdaterTestHarness.WaitForWorkerPhaseAsync(
                    request,
                    UpdateWorkerPhase.Committed,
                    TimeSpan.FromSeconds(30));
                await PublishedUpdaterTestHarness.AssertInstalledVersionAsync(
                    scenario.InstallationDirectory,
                    "1.0.1",
                    aotUpdater,
                    expectPreservedTools: true,
                    expectedManagedCloudflaredVersion: "1.0.1");
                Assert.False(File.Exists(artifacts.PackagePath));
                Assert.False(Directory.Exists(artifacts.StagingDirectory));
                return;
            }

            if (!invalidEntryExecutable)
            {
                await PublishedUpdaterTestHarness.WaitForFileAsync(
                    newApplicationReadyPath,
                    TimeSpan.FromMinutes(1),
                    "失败新应用启动信号",
                    coordinator);
            }

            await PublishedUpdaterTestHarness.WaitForWorkerPhaseAsync(
                request,
                UpdateWorkerPhase.RolledBack,
                TimeSpan.FromMinutes(2));
            await PublishedUpdaterTestHarness.CloseFailureDialogAndWaitAsync(
                coordinator,
                TimeSpan.FromMinutes(1));
            File.WriteAllText(newApplicationReleasePath, "release");
            await PublishedUpdaterTestHarness.AssertInstalledVersionAsync(
                scenario.InstallationDirectory,
                "1.0.0",
                aotUpdater,
                expectPreservedTools: true);
            PublishedUpdaterTestHarness.AssertManagedCloudflaredAbsent(
                scenario.InstallationDirectory);
            Assert.False(Directory.Exists(request.BackupDirectory));
            Assert.False(Directory.Exists(request.CandidateDirectory));
        }
        finally
        {
            File.WriteAllText(newApplicationReleasePath, "release");
            TryStopLaunchedApplication(request);
            if (!coordinator.HasExited)
            {
                coordinator.Kill(entireProcessTree: true);
                await coordinator.WaitForExitAsync();
            }
        }
    }

    private static async Task WaitForProcessToExitIfPresentAsync(
        int processId,
        TimeSpan timeout)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            await process.WaitForExitAsync().WaitAsync(timeout);
        }
        catch (ArgumentException)
        {
        }
    }

    private static void TryStopLaunchedApplication(UpdateTransactionRequest request)
    {
        try
        {
            if (!File.Exists(request.LaunchedProcessPath))
            {
                return;
            }

            var launched = UpdateJsonFile.Read(
                request.LaunchedProcessPath,
                UpdateJsonTypeInfo.LaunchedProcessInfo);
            using var process = Process.GetProcessById(launched.ProcessId);
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(10_000);
            }
        }
        catch
        {
        }
    }
}
