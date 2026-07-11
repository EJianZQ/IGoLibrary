using System.Runtime.InteropServices;

namespace IGoLibrary.Ex.Infrastructure.Persistence;

internal static class StorageLocationDefaults
{
    internal const string DataDirectoryEnvironmentVariable = "IGOLIBRARY_EX_DATA_DIR";
    internal const string DatabaseFileName = "igolibrary-ex.db";
    internal const string LocatorFileName = "storage-locations.json";

    public static StorageLocations GetDefaults()
    {
        var overridden = Environment.GetEnvironmentVariable(DataDirectoryEnvironmentVariable);
        var dataDirectory = string.IsNullOrWhiteSpace(overridden)
            ? GetPlatformDefaultRootDirectory()
            : Path.GetFullPath(overridden);
        return new StorageLocations(dataDirectory, Path.Combine(dataDirectory, "logs"));
    }

    public static StorageLocations GetPlatformDefaults()
    {
        var dataDirectory = GetPlatformDefaultRootDirectory();
        return new StorageLocations(dataDirectory, Path.Combine(dataDirectory, "logs"));
    }

    public static string GetLocatorFilePath()
        => Path.Combine(GetPlatformDefaultRootDirectory(), LocatorFileName);

    public static string GetPlatformDefaultRootDirectory()
    {
        var baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            baseDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Personal),
                "Library",
                "Application Support");
        }

        return Path.Combine(baseDirectory, "IGoLibrary-Ex");
    }

    public static bool IsDatabaseArtifactFileName(string fileName)
    {
        return string.Equals(fileName, DatabaseFileName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fileName, DatabaseFileName + "-wal", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fileName, DatabaseFileName + "-shm", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fileName, DatabaseFileName + "-journal", StringComparison.OrdinalIgnoreCase);
    }
}
