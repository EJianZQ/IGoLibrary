using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Desktop.Services;

internal interface ICloudflaredToolLocator
{
    Task<CloudflaredToolAvailability> FindAsync(CancellationToken cancellationToken = default);

    Task<bool> ValidateDirectoryAsync(
        string directory,
        CancellationToken cancellationToken = default);

    void Invalidate();
}

internal sealed record CloudflaredToolAvailability(
    bool IsAvailable,
    string? ExecutablePath,
    CloudflaredAssetDescriptor Asset,
    CloudflaredToolSource Source = CloudflaredToolSource.None);

internal enum CloudflaredToolSource
{
    None,
    Bundled,
    UserInstalled
}

internal sealed class CloudflaredToolLocator(
    CloudflaredAssetCatalog catalog,
    ICloudflaredPathProvider paths,
    ILogger<CloudflaredToolLocator> logger) : ICloudflaredToolLocator
{
    private const UnixFileMode RequiredUnixExecutableMode =
        UnixFileMode.UserRead |
        UnixFileMode.UserWrite |
        UnixFileMode.UserExecute |
        UnixFileMode.GroupRead |
        UnixFileMode.GroupExecute |
        UnixFileMode.OtherRead |
        UnixFileMode.OtherExecute;

    private readonly SemaphoreSlim _validationGate = new(1, 1);
    private readonly Dictionary<string, ValidationCache> _cache =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    public async Task<CloudflaredToolAvailability> FindAsync(
        CancellationToken cancellationToken = default)
    {
        var asset = catalog.Current;
        if (await ValidateDirectoryAsync(paths.BundledDirectory, cancellationToken))
        {
            logger.LogInformation(
                "使用应用内置 cloudflared。版本={Version}，运行时={RuntimeIdentifier}。",
                asset.Version,
                asset.RuntimeIdentifier);
            return new CloudflaredToolAvailability(
                true,
                Path.Combine(paths.BundledDirectory, asset.ExecutableName),
                asset,
                CloudflaredToolSource.Bundled);
        }

        var managedDirectory = paths.GetManagedInstallDirectory(asset);
        var managedPathIsSafe = true;
        try
        {
            CloudflaredFileSystemSafety.EnsureNoLinksInExistingPath(
                paths.ManagedInstallRoot,
                managedDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            managedPathIsSafe = false;
            logger.LogWarning(exception, "用户级 cloudflared 路径不安全，将视为不可用。目录={Directory}。", managedDirectory);
        }

        if (managedPathIsSafe && await ValidateDirectoryAsync(managedDirectory, cancellationToken))
        {
            logger.LogInformation(
                "使用用户级 cloudflared。版本={Version}，运行时={RuntimeIdentifier}。",
                asset.Version,
                asset.RuntimeIdentifier);
            return new CloudflaredToolAvailability(
                true,
                Path.Combine(managedDirectory, asset.ExecutableName),
                asset,
                CloudflaredToolSource.UserInstalled);
        }

        logger.LogInformation(
            "未找到有效 cloudflared。版本={Version}，运行时={RuntimeIdentifier}。",
            asset.Version,
            asset.RuntimeIdentifier);
        return new CloudflaredToolAvailability(false, null, asset);
    }

    public async Task<bool> ValidateDirectoryAsync(
        string directory,
        CancellationToken cancellationToken = default)
    {
        var asset = catalog.Current;
        var executablePath = Path.Combine(directory, asset.ExecutableName);
        var licensePath = Path.Combine(directory, "LICENSE.txt");
        var noticesPath = Path.Combine(directory, "THIRD-PARTY-NOTICES.txt");
        if (!File.Exists(executablePath) || !File.Exists(licensePath) || !File.Exists(noticesPath))
        {
            return false;
        }

        try
        {
            if (CloudflaredFileSystemSafety.IsLink(new DirectoryInfo(directory)) ||
                CloudflaredFileSystemSafety.IsLink(new FileInfo(executablePath)) ||
                CloudflaredFileSystemSafety.IsLink(new FileInfo(licensePath)) ||
                CloudflaredFileSystemSafety.IsLink(new FileInfo(noticesPath)))
            {
                logger.LogWarning("cloudflared 安装包含符号链接或重解析点，将视为不可用。目录={Directory}。", directory);
                return false;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "检查 cloudflared 安装文件类型时失败。目录={Directory}。", directory);
            return false;
        }

        var stamp = TryCreateStamp(executablePath, licensePath, noticesPath);
        if (stamp is null)
        {
            return false;
        }

        await _validationGate.WaitAsync(cancellationToken);
        try
        {
            var normalizedDirectory = Path.GetFullPath(directory);
            if (_cache.TryGetValue(normalizedDirectory, out var cached) && cached.Stamp == stamp)
            {
                return cached.IsValid;
            }

            var valid = await ValidateFilesAsync(
                executablePath,
                licensePath,
                noticesPath,
                asset,
                cancellationToken);
            _cache[normalizedDirectory] = new ValidationCache(stamp, valid);
            if (!valid)
            {
                logger.LogWarning(
                    "cloudflared 文件校验失败，将视为不可用。目录={Directory}，版本={Version}，运行时={RuntimeIdentifier}。",
                    directory,
                    asset.Version,
                    asset.RuntimeIdentifier);
            }

            return valid;
        }
        finally
        {
            _validationGate.Release();
        }
    }

    public void Invalidate()
    {
        _validationGate.Wait();
        try
        {
            _cache.Clear();
        }
        finally
        {
            _validationGate.Release();
        }
    }

    private async Task<bool> ValidateFilesAsync(
        string executablePath,
        string licensePath,
        string noticesPath,
        CloudflaredAssetDescriptor asset,
        CancellationToken cancellationToken)
    {
        try
        {
            var executableInfo = new FileInfo(executablePath);
            var licenseInfo = new FileInfo(licensePath);
            var noticesInfo = new FileInfo(noticesPath);
            if (executableInfo.Length != asset.ExecutableSize ||
                licenseInfo.Length != catalog.LicenseBytes.Length ||
                noticesInfo.Length != catalog.NoticesBytes.Length)
            {
                return false;
            }

            if (!OperatingSystem.IsWindows())
            {
                var mode = File.GetUnixFileMode(executablePath);
                if ((mode & RequiredUnixExecutableMode) != RequiredUnixExecutableMode)
                {
                    logger.LogWarning(
                        "cloudflared 缺少必需的 Unix 读取或执行权限，将视为不可用。路径={ExecutablePath}，权限={UnixFileMode}。",
                        executablePath,
                        mode);
                    return false;
                }
            }

            await using var executable = new FileStream(
                executablePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(executable, cancellationToken));
            if (!hash.Equals(asset.ExecutableSha256, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var license = await File.ReadAllBytesAsync(licensePath, cancellationToken);
            var notices = await File.ReadAllBytesAsync(noticesPath, cancellationToken);
            return license.AsSpan().SequenceEqual(catalog.LicenseBytes) &&
                   notices.AsSpan().SequenceEqual(catalog.NoticesBytes);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or CryptographicException)
        {
            logger.LogWarning(exception, "读取 cloudflared 文件进行校验时失败。目录={Directory}。", Path.GetDirectoryName(executablePath));
            return false;
        }
    }

    private static string? TryCreateStamp(
        string executablePath,
        string licensePath,
        string noticesPath)
    {
        try
        {
            var fileStamp = string.Join('|', new[] { executablePath, licensePath, noticesPath }.Select(static path =>
            {
                var info = new FileInfo(path);
                return $"{info.FullName}\0{info.Length}\0{info.LastWriteTimeUtc.Ticks}";
            }));
            return OperatingSystem.IsWindows()
                ? fileStamp
                : $"{fileStamp}|mode\0{(int)File.GetUnixFileMode(executablePath)}";
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return null;
        }
    }

    private sealed record ValidationCache(string Stamp, bool IsValid);
}
