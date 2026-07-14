namespace IGoLibrary.Ex.Updater.Core;

public static class UpdateInstallationPermissions
{
    public static bool RequiresElevation(string installationDirectory)
    {
        var parent = Path.GetDirectoryName(Path.GetFullPath(installationDirectory))
                     ?? throw new InvalidOperationException("无法确定安装目录父目录");
        var probePath = Path.Combine(parent, $".IGoLibrary-Ex.permission-{Guid.NewGuid():N}.tmp");
        try
        {
            using (new FileStream(probePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
            }

            File.Delete(probePath);
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
        catch (IOException)
        {
            return true;
        }
        finally
        {
            try
            {
                if (File.Exists(probePath))
                {
                    File.Delete(probePath);
                }
            }
            catch
            {
            }
        }
    }
}
