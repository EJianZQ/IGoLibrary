using System.Runtime.InteropServices;
using System.Text.Json;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Domain.Models;
using IGoLibrary.Ex.Infrastructure.Persistence;

namespace IGoLibrary.Ex.Infrastructure.Security;

public sealed class WindowsCredentialStore : ICredentialStore
{
    private const string TargetName = "IGoLibrary-Ex.Session";

    public Task SaveSessionAsync(SessionCredentials credentials, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(credentials, AppJson.Default);
        var bytes = System.Text.Encoding.Unicode.GetBytes(payload);
        if (bytes.Length > 5120)
        {
            throw new InvalidOperationException("会话数据超出 Windows 凭据管理器限制。");
        }

        var credential = new NativeCredential
        {
            Type = 1,
            TargetName = TargetName,
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
                throw new InvalidOperationException("写入 Windows 凭据管理器失败。");
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

    public Task<SessionCredentials?> LoadSessionAsync(CancellationToken cancellationToken = default)
    {
        if (!CredRead(TargetName, 1, 0, out var credentialPtr))
        {
            return Task.FromResult<SessionCredentials?>(null);
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(credentialPtr);
            if (credential.CredentialBlob == IntPtr.Zero)
            {
                return Task.FromResult<SessionCredentials?>(null);
            }

            var json = Marshal.PtrToStringUni(credential.CredentialBlob, (int)credential.CredentialBlobSize / 2);
            return Task.FromResult(JsonSerializer.Deserialize<SessionCredentials>(json!, AppJson.Default));
        }
        finally
        {
            CredFree(credentialPtr);
        }
    }

    public Task ClearSessionAsync(CancellationToken cancellationToken = default)
    {
        CredDelete(TargetName, 1, 0);
        return Task.CompletedTask;
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
