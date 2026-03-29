using System.Runtime.InteropServices;

namespace IGoLibrary.Ex.Infrastructure.Persistence;

internal static class AppDataPaths
{
    private const string OverrideEnvironmentVariable = "IGOLIBRARY_EX_DATA_DIR";

    public static string RootDirectory
    {
        get
        {
            var overridden = Environment.GetEnvironmentVariable(OverrideEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(overridden))
            {
                return Path.GetFullPath(overridden);
            }

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
    }

    public static string DatabasePath => Path.Combine(RootDirectory, "igolibrary-ex.db");
}
