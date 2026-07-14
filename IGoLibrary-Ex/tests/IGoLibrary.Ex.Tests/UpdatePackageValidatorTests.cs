using System.IO.Compression;
using System.Security.Cryptography;
using IGoLibrary.Ex.Updater.Core;

namespace IGoLibrary.Ex.Tests;

public sealed class UpdatePackageValidatorTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "IGoLibrary-Ex-package-tests",
        Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("../escape.dll")]
    [InlineData("/absolute.dll")]
    [InlineData("C:/drive.dll")]
    [InlineData("folder/file.dll:stream")]
    [InlineData("CON/file.dll")]
    [InlineData("folder/trailing./file.dll")]
    public async Task ExtractAndValidateAsync_RejectsUnsafeZipPaths(string entryPath)
    {
        Directory.CreateDirectory(_root);
        var archivePath = Path.Combine(_root, "unsafe.zip");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry(entryPath);
            await using var stream = entry.Open();
            await stream.WriteAsync("bad"u8.ToArray());
        }

        await Assert.ThrowsAnyAsync<Exception>(() =>
            UpdatePackageValidator.ExtractAndValidateAsync(
                archivePath,
                Path.Combine(_root, "staging"),
                "1.0.1"));

        Assert.False(File.Exists(Path.Combine(_root, "escape.dll")));
    }

    [Fact]
    public async Task ExtractAndValidateAsync_RejectsCaseInsensitiveDuplicates()
    {
        Directory.CreateDirectory(_root);
        var archivePath = Path.Combine(_root, "duplicate.zip");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            archive.CreateEntry("Folder/File.dll");
            archive.CreateEntry("folder/file.dll");
        }

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            UpdatePackageValidator.ExtractAndValidateAsync(
                archivePath,
                Path.Combine(_root, "staging"),
                "1.0.1"));

        Assert.Contains("重复路径", exception.Message);
    }

    [Fact]
    public async Task ExtractAndValidateAsync_RejectsUnixSymbolicLinkEntries()
    {
        Directory.CreateDirectory(_root);
        var archivePath = Path.Combine(_root, "symlink.zip");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("linked.dll");
            entry.ExternalAttributes = 0xA000 << 16;
            await using var stream = entry.Open();
            await stream.WriteAsync("target.dll"u8.ToArray());
        }

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            UpdatePackageValidator.ExtractAndValidateAsync(
                archivePath,
                Path.Combine(_root, "staging"),
                "1.0.1"));

        Assert.Contains("符号链接", exception.Message);
    }

    [Fact]
    public async Task ExtractAndValidateAsync_ReportsRequiredManifestFileName_WhenManifestIsMissing()
    {
        Directory.CreateDirectory(_root);
        var archivePath = Path.Combine(_root, "missing-manifest.zip");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("legacy-app.exe");
            await using var stream = entry.Open();
            await stream.WriteAsync("legacy"u8.ToArray());
        }

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            UpdatePackageValidator.ExtractAndValidateAsync(
                archivePath,
                Path.Combine(_root, "staging"),
                "1.0.1"));

        Assert.Contains(UpdateProtocol.ManifestFileName, exception.Message);
        Assert.IsType<FileNotFoundException>(exception.InnerException);
    }

    [Fact]
    public async Task ExtractAndValidateAsync_RequiresExactManifestFileSetAndHashes()
    {
        var package = Path.Combine(_root, "package");
        WritePackage(package, "1.0.1", new Dictionary<string, string>
        {
            [UpdateProtocol.EntryExecutableName] = "desktop",
            [UpdateProtocol.UpdaterExecutableName] = "updater",
            ["feature.dll"] = "feature"
        });
        File.WriteAllText(Path.Combine(package, "extra.dll"), "extra");
        var archivePath = Path.Combine(_root, "extra.zip");
        ZipFile.CreateFromDirectory(package, archivePath);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            UpdatePackageValidator.ExtractAndValidateAsync(
                archivePath,
                Path.Combine(_root, "staging"),
                "1.0.1"));

        Assert.Contains("文件集合", exception.Message);
    }

    [Fact]
    public async Task ExtractAndValidateAsync_ExtractsAValidPackage()
    {
        var package = Path.Combine(_root, "package");
        WritePackage(package, "1.0.1", new Dictionary<string, string>
        {
            [UpdateProtocol.EntryExecutableName] = "desktop",
            [UpdateProtocol.UpdaterExecutableName] = "updater",
            ["sub/feature.dll"] = "feature"
        });
        var archivePath = Path.Combine(_root, "valid.zip");
        ZipFile.CreateFromDirectory(package, archivePath);
        var staging = Path.Combine(_root, "staging");

        var manifest = await UpdatePackageValidator.ExtractAndValidateAsync(
            archivePath,
            staging,
            "1.0.1");

        Assert.Equal("1.0.1", manifest.Version);
        Assert.Equal("feature", File.ReadAllText(Path.Combine(staging, "sub", "feature.dll")));
    }

    [Fact]
    public void ValidateManifest_RejectsPackageWithoutPortableMarker()
    {
        var manifest = new UpdatePackageManifest(
            UpdateProtocol.SchemaVersion,
            UpdateProtocol.ProductName,
            "1.0.1",
            UpdateProtocol.WindowsX64Runtime,
            UpdateProtocol.EntryExecutableName,
            [
                new UpdateManifestFile(UpdateProtocol.EntryExecutableName, 1, new string('0', 64)),
                new UpdateManifestFile(UpdateProtocol.UpdaterExecutableName, 1, new string('1', 64))
            ]);

        var exception = Assert.Throws<InvalidDataException>(() =>
            UpdatePackageValidator.ValidateManifest(manifest, "1.0.1"));

        Assert.Contains("绿色版标记", exception.Message);
    }

    [Fact]
    public void ValidateManifest_RejectsNonAsciiVersionDigits()
    {
        var manifest = new UpdatePackageManifest(
            UpdateProtocol.SchemaVersion,
            UpdateProtocol.ProductName,
            "1.0.１",
            UpdateProtocol.WindowsX64Runtime,
            UpdateProtocol.EntryExecutableName,
            [
                new UpdateManifestFile(UpdateProtocol.EntryExecutableName, 1, new string('0', 64)),
                new UpdateManifestFile(UpdateProtocol.UpdaterExecutableName, 1, new string('1', 64)),
                new UpdateManifestFile(UpdateProtocol.PortableMarkerFileName, 1, new string('2', 64))
            ]);

        Assert.Throws<InvalidDataException>(() =>
            UpdatePackageValidator.ValidateManifest(manifest));
    }

    [Fact]
    public async Task ValidateDirectoryAsync_RejectsModifiedPortableMarkerContent()
    {
        var package = Path.Combine(_root, "package");
        WritePackage(package, "1.0.1", new Dictionary<string, string>
        {
            [UpdateProtocol.EntryExecutableName] = "desktop",
            [UpdateProtocol.UpdaterExecutableName] = "updater"
        });
        File.WriteAllText(
            Path.Combine(package, UpdateProtocol.PortableMarkerFileName),
            "not-a-portable-release");
        var manifest = UpdatePackageValidator.LoadAndValidateManifest(
            Path.Combine(package, UpdateProtocol.ManifestFileName));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            UpdatePackageValidator.ValidateDirectoryAsync(
                package,
                manifest,
                allowAdditionalFiles: false));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
        }
    }

    internal static void WritePackage(
        string directory,
        string version,
        IReadOnlyDictionary<string, string> files)
    {
        Directory.CreateDirectory(directory);
        var packageFiles = new Dictionary<string, string>(files, StringComparer.OrdinalIgnoreCase)
        {
            [UpdateProtocol.PortableMarkerFileName] = UpdateProtocol.PortableMarkerContent
        };
        foreach (var (relativePath, content) in packageFiles)
        {
            var fullPath = Path.Combine(directory, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content);
        }

        var manifestFiles = packageFiles.Keys
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path =>
            {
                var fullPath = Path.Combine(directory, path.Replace('/', Path.DirectorySeparatorChar));
                var bytes = File.ReadAllBytes(fullPath);
                return new UpdateManifestFile(
                    path,
                    bytes.Length,
                    Convert.ToHexString(SHA256.HashData(bytes)));
            })
            .ToArray();
        UpdateJsonFile.WriteAtomic(
            Path.Combine(directory, UpdateProtocol.ManifestFileName),
            new UpdatePackageManifest(
                UpdateProtocol.SchemaVersion,
                UpdateProtocol.ProductName,
                version,
                UpdateProtocol.WindowsX64Runtime,
                UpdateProtocol.EntryExecutableName,
                manifestFiles));
    }
}
