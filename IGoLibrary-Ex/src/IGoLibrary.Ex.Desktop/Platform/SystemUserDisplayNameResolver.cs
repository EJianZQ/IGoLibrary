using System.Runtime.InteropServices;

namespace IGoLibrary.Ex.Desktop.Platform;

internal static class SystemUserDisplayNameResolver
{
    private const string DefaultDisplayName = "同学";
    private static readonly Lazy<string> CachedDisplayName = new(ResolveCurrentDisplayName);

    public static string GetCurrentDisplayName() => CachedDisplayName.Value;

    private static string ResolveCurrentDisplayName()
    {
        var loginName = Environment.UserName?.Trim();
        if (OperatingSystem.IsWindows())
        {
            var fullName = TryReadWindowsFullName(loginName);
            if (!string.IsNullOrWhiteSpace(fullName))
            {
                return fullName.Trim();
            }
        }

        return string.IsNullOrWhiteSpace(loginName)
            ? DefaultDisplayName
            : loginName;
    }

    private static string? TryReadWindowsFullName(string? loginName)
    {
        if (string.IsNullOrWhiteSpace(loginName))
        {
            return null;
        }

        IntPtr buffer = IntPtr.Zero;
        try
        {
            var status = NetUserGetInfo(null, loginName, 2, out buffer);
            if (status != 0 || buffer == IntPtr.Zero)
            {
                return null;
            }

            var userInfo = Marshal.PtrToStructure<UserInfo2>(buffer);
            return string.IsNullOrWhiteSpace(userInfo.FullName)
                ? null
                : userInfo.FullName;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (buffer != IntPtr.Zero)
            {
                NetApiBufferFree(buffer);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct UserInfo2
    {
        public string? Name;
        public string? Password;
        public uint PasswordAge;
        public uint Privilege;
        public string? HomeDirectory;
        public string? Comment;
        public uint Flags;
        public string? ScriptPath;
        public uint AuthFlags;
        public string? FullName;
        public string? UserComment;
        public string? Parameters;
        public string? Workstations;
        public uint LastLogon;
        public uint LastLogoff;
        public uint AccountExpires;
        public uint MaxStorage;
        public uint UnitsPerWeek;
        public IntPtr LogonHours;
        public uint BadPasswordCount;
        public uint NumberOfLogons;
        public string? LogonServer;
        public uint CountryCode;
        public uint CodePage;
    }

    [DllImport("Netapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int NetUserGetInfo(string? serverName, string userName, int level, out IntPtr buffer);

    [DllImport("Netapi32.dll", SetLastError = true)]
    private static extern int NetApiBufferFree(IntPtr buffer);
}
