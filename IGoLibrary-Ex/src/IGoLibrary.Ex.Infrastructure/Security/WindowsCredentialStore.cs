using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Domain.Models;
using IGoLibrary.Ex.Infrastructure.Persistence;

namespace IGoLibrary.Ex.Infrastructure.Security;

public sealed class WindowsCredentialStore(IPersistentDataChangeTracker? changeTracker = null) : ICredentialStore
{
    private const int ErrorNotFound = 1168;
    private const string SessionTargetName = "IGoLibrary-Ex.Session";
    private const string RemoteCheckInTargetName = "IGoLibrary-Ex.RemoteCheckIn";

    public Task SaveSessionAsync(SessionCredentials credentials, CancellationToken cancellationToken = default)
        => SaveTrackedAsync(SessionTargetName, credentials);

    public Task<SessionCredentials?> LoadSessionAsync(CancellationToken cancellationToken = default)
        => LoadAsync<SessionCredentials>(SessionTargetName);

    public Task ClearSessionAsync(CancellationToken cancellationToken = default)
        => ClearTrackedAsync(SessionTargetName);

    public Task SaveRemoteCheckInSessionAsync(
        RemoteCheckInSessionCredentials credentials,
        CancellationToken cancellationToken = default)
        => SaveTrackedAsync(RemoteCheckInTargetName, credentials);

    public Task<RemoteCheckInSessionCredentials?> LoadRemoteCheckInSessionAsync(
        CancellationToken cancellationToken = default)
        => LoadAsync<RemoteCheckInSessionCredentials>(RemoteCheckInTargetName);

    public Task ClearRemoteCheckInSessionAsync(CancellationToken cancellationToken = default)
        => ClearTrackedAsync(RemoteCheckInTargetName);

    private async Task SaveTrackedAsync<T>(string targetName, T value)
    {
        await SaveAsync(targetName, value);
        changeTracker?.MarkChanged();
    }

    private async Task ClearTrackedAsync(string targetName)
    {
        await ClearAsync(targetName);
        changeTracker?.MarkChanged();
    }

    private static Task SaveAsync<T>(string targetName, T value)
    {
        var payload = JsonSerializer.Serialize(value, AppJson.Default);
        var bytes = System.Text.Encoding.Unicode.GetBytes(payload);
        if (bytes.Length > 5120)
        {
            throw new InvalidOperationException("会话数据超出 Windows 凭据管理器限制");
        }

        var credential = new NativeCredential
        {
            Type = 1,
            TargetName = targetName,
            CredentialBlobSize = (uint)bytes.Length,
            CredentialBlob = Marshal.StringToCoTaskMemUni(payload),
            Persist = 2,
            AttributeCount = 0,
            Attributes = IntPtr.Zero,
            TargetAlias = null,
            UserName = "IGoLibrary-Ex"
        };

        try
        {
            if (!CredWrite(ref credential, 0))
            {
                var errorCode = Marshal.GetLastWin32Error();
                throw new Win32Exception(errorCode, $"写入 Windows 凭据管理器失败（错误码 {errorCode}）");
            }
        }
        finally
        {
            if (credential.CredentialBlob != IntPtr.Zero)
            {
                Marshal.ZeroFreeCoTaskMemUnicode(credential.CredentialBlob);
            }
        }

        return Task.CompletedTask;
    }

    private static Task<T?> LoadAsync<T>(string targetName)
    {
        if (!CredRead(targetName, 1, 0, out var credentialPtr))
        {
            var errorCode = Marshal.GetLastWin32Error();
            if (errorCode == ErrorNotFound)
            {
                return Task.FromResult<T?>(default);
            }

            throw new Win32Exception(errorCode, $"读取 Windows 凭据管理器失败（错误码 {errorCode}）");
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(credentialPtr);
            if (credential.CredentialBlob == IntPtr.Zero)
            {
                return Task.FromResult<T?>(default);
            }

            var json = Marshal.PtrToStringUni(credential.CredentialBlob, (int)credential.CredentialBlobSize / 2);
            return Task.FromResult(JsonSerializer.Deserialize<T>(json!, AppJson.Default));
        }
        finally
        {
            CredFree(credentialPtr);
        }
    }

    private static Task ClearAsync(string targetName)
    {
        var deleted = CredDelete(targetName, 1, 0);
        var errorCode = deleted ? 0 : Marshal.GetLastWin32Error();
        EnsureCredentialDeleted(deleted, errorCode);
        return Task.CompletedTask;
    }

    internal static void EnsureCredentialDeleted(bool deleted, int errorCode)
    {
        if (deleted || errorCode == ErrorNotFound)
        {
            return;
        }

        throw new InvalidOperationException(
            $"删除 Windows 安全凭据失败（Win32 错误 {errorCode}）",
            new Win32Exception(errorCode));
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public string? TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref NativeCredential userCredential, uint flags);

    [DllImport("advapi32", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, uint type, uint reservedFlag, out IntPtr credentialPtr);

    [DllImport("advapi32", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32", EntryPoint = "CredFree", SetLastError = true)]
    private static extern void CredFree([In] IntPtr cred);
}
