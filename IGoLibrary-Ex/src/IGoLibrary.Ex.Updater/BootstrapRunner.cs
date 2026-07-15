using System.Diagnostics;
using System.IO.Pipes;
using IGoLibrary.Ex.Updater.Core;

namespace IGoLibrary.Ex.Updater;

internal static class BootstrapRunner
{
    private static readonly TimeSpan PipeTimeout = TimeSpan.FromMinutes(2);

    public static async Task<int> RunAsync(string pipeName)
    {
        using var timeout = new CancellationTokenSource(PipeTimeout);
        await using var pipe = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        await pipe.ConnectAsync(timeout.Token);

        UpdateBootstrapPayload? payload = null;
        UpdateBootstrapResult result;
        try
        {
            payload = await UpdatePipeProtocol.ReadAsync(
                pipe,
                UpdateJsonTypeInfo.BootstrapPayload,
                timeout.Token);
            if (payload.SchemaVersion != UpdateProtocol.SchemaVersion)
            {
                throw new InvalidDataException("更新引导协议版本不匹配");
            }

            var request = payload.Request;
            UpdateTransaction.ValidateRequestFile(payload.SourceRequestPath, request);
            UpdateTransaction.ValidateRequest(request);
            var trustedUpdaterEntry = await ValidateTrustedBootstrapAsync(
                request,
                timeout.Token);

            var secureDirectory = UpdateTransaction.GetSecureWorkingDirectory(request);
            if (Directory.Exists(secureDirectory))
            {
                throw new IOException($"受保护的更新工作目录已经存在：{secureDirectory}");
            }

            Directory.CreateDirectory(secureDirectory);
            try
            {
                UpdatePathSafety.RejectReparsePoint(secureDirectory);
                var secureUpdater = Path.Combine(
                    secureDirectory,
                    UpdateProtocol.UpdaterExecutableName);
                var securePackage = Path.Combine(secureDirectory, "package.zip");
                await CopyFileAsync(Environment.ProcessPath!, secureUpdater, timeout.Token);
                await UpdatePackageValidator.ValidateFileAsync(
                    secureUpdater,
                    trustedUpdaterEntry.Size,
                    trustedUpdaterEntry.Sha256,
                    timeout.Token);
                await CopyFileAsync(request.PackagePath, securePackage, timeout.Token);
                await UpdatePackageValidator.ValidateArchiveDigestAsync(
                    securePackage,
                    request.PackageDigest,
                    timeout.Token);
                if (new FileInfo(securePackage).Length != request.PackageSize)
                {
                    throw new InvalidDataException("受保护更新包大小不匹配");
                }

                var protectedRequest = request with
                {
                    WorkingDirectory = secureDirectory,
                    PackagePath = securePackage
                };
                var protectedRequestPath = Path.Combine(secureDirectory, "request.json");
                UpdateJsonFile.WriteAtomic(
                    protectedRequestPath,
                    protectedRequest,
                    UpdateJsonTypeInfo.TransactionRequest);
                var persistedRequest = UpdateJsonFile.Read(
                    protectedRequestPath,
                    UpdateJsonTypeInfo.TransactionRequest);
                if (persistedRequest != protectedRequest)
                {
                    throw new InvalidDataException("受保护事务请求写入后不一致");
                }

                UpdateTransaction.ValidateRequestFile(protectedRequestPath, persistedRequest);
                UpdateTransaction.ValidateRequest(persistedRequest);

                using var worker = Process.Start(
                    UpdateProcessStartInfoFactory.CreateWorker(
                        secureUpdater,
                        protectedRequestPath))
                    ?? throw new InvalidOperationException("无法启动受保护的文件更新组件");
                result = new UpdateBootstrapResult(
                    UpdateProtocol.SchemaVersion,
                    request.TransactionId,
                    true,
                    "受保护的文件更新组件已启动",
                    worker.Id);
            }
            catch
            {
                TryDeleteSecureDirectory(request, secureDirectory);
                throw;
            }
        }
        catch (Exception exception)
        {
            result = new UpdateBootstrapResult(
                UpdateProtocol.SchemaVersion,
                payload?.Request.TransactionId ?? string.Empty,
                false,
                exception.Message);
        }

        await UpdatePipeProtocol.WriteAsync(
            pipe,
            result,
            UpdateJsonTypeInfo.BootstrapResult,
            timeout.Token);
        return result.Succeeded ? 0 : 1;
    }

    private static async Task<UpdateManifestFile> ValidateTrustedBootstrapAsync(
        UpdateTransactionRequest request,
        CancellationToken cancellationToken)
    {
        var processPath = Environment.ProcessPath
                          ?? throw new InvalidOperationException("无法确定更新引导器路径");
        var expectedPath = Path.Combine(
            request.InstallationDirectory,
            UpdateProtocol.UpdaterExecutableName);
        if (!string.Equals(
                Path.GetFullPath(processPath),
                Path.GetFullPath(expectedPath),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("提权引导器不是当前安装目录中的受信 updater");
        }

        var manifest = UpdatePackageValidator.LoadAndValidateManifest(
            Path.Combine(request.InstallationDirectory, request.ManifestFileName),
            request.CurrentVersion);
        var updaterEntry = manifest.Files.SingleOrDefault(file =>
            string.Equals(
                UpdatePathSafety.NormalizeRelativePath(file.Path),
                UpdateProtocol.UpdaterExecutableName,
                StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("当前 manifest 缺少 updater 文件项");
        await UpdatePackageValidator.ValidateFileAsync(
            processPath,
            updaterEntry.Size,
            updaterEntry.Sha256,
            cancellationToken);
        return updaterEntry;
    }

    private static async Task CopyFileAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        await using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await source.CopyToAsync(destination, 128 * 1024, cancellationToken);
        await destination.FlushAsync(cancellationToken);
    }

    private static void TryDeleteSecureDirectory(
        UpdateTransactionRequest request,
        string secureDirectory)
    {
        try
        {
            if (!Directory.Exists(secureDirectory))
            {
                return;
            }

            var expected = UpdateTransaction.GetSecureWorkingDirectory(request);
            if (string.Equals(
                    Path.GetFullPath(secureDirectory),
                    Path.GetFullPath(expected),
                    StringComparison.OrdinalIgnoreCase))
            {
                Directory.Delete(secureDirectory, recursive: true);
            }
        }
        catch
        {
        }
    }
}
