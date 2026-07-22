using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using IGoLibrary.Ex.Application.Abstractions;

namespace IGoLibrary.Ex.Infrastructure.Security;

public sealed class PlatformBackupSecretStore : IBackupSecretStore
{
    private const string BackupPasswordKey = "backup-encryption";
    private const string PreviousBackupPasswordKey = "backup-encryption-previous";
    private const string WebDavPasswordKey = "webdav";
    private readonly IBackupSecretBackend _backend;
    private readonly IPersistentDataChangeTracker? _changeTracker;

    public PlatformBackupSecretStore(IPersistentDataChangeTracker? changeTracker = null)
        : this(CreateBackend(), changeTracker)
    {
    }

    internal PlatformBackupSecretStore(
        IBackupSecretBackend backend,
        IPersistentDataChangeTracker? changeTracker = null)
    {
        _backend = backend;
        _changeTracker = changeTracker;
    }

    public bool IsPersistent => _backend.IsPersistent;

    public Task<string?> LoadBackupPasswordAsync(CancellationToken cancellationToken = default)
        => _backend.ReadAsync(BackupPasswordKey, cancellationToken);

    public Task SaveBackupPasswordAsync(string password, CancellationToken cancellationToken = default)
        => _backend.WriteAsync(BackupPasswordKey, password, cancellationToken);

    public Task ClearBackupPasswordAsync(CancellationToken cancellationToken = default)
        => _backend.DeleteAsync(BackupPasswordKey, cancellationToken);

    public Task<string?> LoadPreviousBackupPasswordAsync(CancellationToken cancellationToken = default)
        => _backend.ReadAsync(PreviousBackupPasswordKey, cancellationToken);

    public Task SavePreviousBackupPasswordAsync(
        string password,
        CancellationToken cancellationToken = default)
        => _backend.WriteAsync(PreviousBackupPasswordKey, password, cancellationToken);

    public Task ClearPreviousBackupPasswordAsync(CancellationToken cancellationToken = default)
        => _backend.DeleteAsync(PreviousBackupPasswordKey, cancellationToken);

    public Task<string?> LoadWebDavPasswordAsync(CancellationToken cancellationToken = default)
        => _backend.ReadAsync(WebDavPasswordKey, cancellationToken);

    public async Task SaveWebDavPasswordAsync(string password, CancellationToken cancellationToken = default)
    {
        await _backend.WriteAsync(WebDavPasswordKey, password, cancellationToken);
        _changeTracker?.MarkChanged();
    }

    public async Task ClearWebDavPasswordAsync(CancellationToken cancellationToken = default)
    {
        await _backend.DeleteAsync(WebDavPasswordKey, cancellationToken);
        _changeTracker?.MarkChanged();
    }

    public Task<string?> LoadRestoreSecretAsync(
        string transactionId,
        CancellationToken cancellationToken = default)
        => _backend.ReadAsync(GetRestoreKey(transactionId), cancellationToken);

    public Task SaveRestoreSecretAsync(
        string transactionId,
        string value,
        CancellationToken cancellationToken = default)
        => _backend.WriteAsync(GetRestoreKey(transactionId), value, cancellationToken);

    public Task ClearRestoreSecretAsync(
        string transactionId,
        CancellationToken cancellationToken = default)
        => _backend.DeleteAsync(GetRestoreKey(transactionId), cancellationToken);

    private static string GetRestoreKey(string transactionId)
    {
        if (!Guid.TryParseExact(transactionId, "N", out _))
        {
            throw new ArgumentException("恢复事务标识无效", nameof(transactionId));
        }

        return "restore-" + transactionId;
    }

    private static IBackupSecretBackend CreateBackend()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsBackupSecretBackend();
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MacBackupSecretBackend();
        }

        return new InMemoryBackupSecretBackend();
    }
}

internal interface IBackupSecretBackend
{
    bool IsPersistent { get; }

    Task<string?> ReadAsync(string key, CancellationToken cancellationToken);

    Task WriteAsync(string key, string value, CancellationToken cancellationToken);

    Task DeleteAsync(string key, CancellationToken cancellationToken);
}

internal sealed class InMemoryBackupSecretBackend : IBackupSecretBackend
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public bool IsPersistent => false;

    public Task<string?> ReadAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(_values.GetValueOrDefault(key));
        }
    }

    public Task WriteAsync(string key, string value, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _values[key] = value;
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _values.Remove(key);
        }

        return Task.CompletedTask;
    }
}

internal sealed class WindowsBackupSecretBackend : IBackupSecretBackend
{
    private const int ErrorNotFound = 1168;
    private const string Prefix = "IGoLibrary-Ex.";

    public bool IsPersistent => true;

    public Task<string?> ReadAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var target = GetTarget(key);
        if (!CredRead(target, 1, 0, out var pointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound)
            {
                return Task.FromResult<string?>(null);
            }

            throw new InvalidOperationException(
                $"读取 Windows 安全凭据失败（Win32 错误 {error}）",
                new Win32Exception(error));
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(pointer);
            var value = credential.CredentialBlob == IntPtr.Zero
                ? null
                : Marshal.PtrToStringUni(
                    credential.CredentialBlob,
                    checked((int)credential.CredentialBlobSize / 2));
            return Task.FromResult(value);
        }
        finally
        {
            CredFree(pointer);
        }
    }

    public Task WriteAsync(string key, string value, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var byteCount = checked(System.Text.Encoding.Unicode.GetByteCount(value));
        if (byteCount > 5120)
        {
            throw new InvalidOperationException("安全凭据内容超出 Windows 凭据管理器限制");
        }

        var credential = new NativeCredential
        {
            Type = 1,
            TargetName = GetTarget(key),
            CredentialBlobSize = (uint)byteCount,
            CredentialBlob = Marshal.StringToCoTaskMemUni(value),
            Persist = 2,
            UserName = "IGoLibrary-Ex"
        };
        try
        {
            if (!CredWrite(ref credential, 0))
            {
                var error = Marshal.GetLastWin32Error();
                throw new InvalidOperationException(
                    $"写入 Windows 安全凭据失败（Win32 错误 {error}）",
                    new Win32Exception(error));
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

    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!CredDelete(GetTarget(key), 1, 0))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != ErrorNotFound)
            {
                throw new InvalidOperationException(
                    $"删除 Windows 安全凭据失败（Win32 错误 {error}）",
                    new Win32Exception(error));
            }
        }

        return Task.CompletedTask;
    }

    private static string GetTarget(string key)
        => Prefix + (key switch
        {
            "webdav" => "WebDav",
            "backup-encryption" => "BackupEncryption",
            "backup-encryption-previous" => "BackupEncryptionPrevious",
            _ => key
        });

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
    private static extern bool CredWrite(ref NativeCredential credential, uint flags);

    [DllImport("advapi32", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32", EntryPoint = "CredFree")]
    private static extern void CredFree(IntPtr credential);
}

internal sealed class MacBackupSecretBackend : IBackupSecretBackend
{
    private const string Service = "IGoLibrary-Ex";
    private const string EncodedValuePrefix = "IGOB64:";
    private const int ErrSecSuccess = 0;
    private const int ErrSecItemNotFound = -25300;
    private const string SecurityFramework =
        "/System/Library/Frameworks/Security.framework/Security";
    private const string CoreFoundationFramework =
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    public bool IsPersistent => true;

    public Task<string?> ReadAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var serviceBytes = Encoding.UTF8.GetBytes(Service);
        var accountBytes = Encoding.UTF8.GetBytes(key);
        var status = SecKeychainFindGenericPassword(
            IntPtr.Zero,
            checked((uint)serviceBytes.Length),
            serviceBytes,
            checked((uint)accountBytes.Length),
            accountBytes,
            out var passwordLength,
            out var passwordData,
            out var itemRef);
        if (status == ErrSecItemNotFound)
        {
            return Task.FromResult<string?>(null);
        }

        EnsureSuccess(status, "读取 macOS Keychain 备份凭据失败");
        try
        {
            var length = checked((int)passwordLength);
            var bytes = new byte[length];
            try
            {
                if (length > 0)
                {
                    Marshal.Copy(passwordData, bytes, 0, length);
                }

                return Task.FromResult<string?>(DecodeStoredValue(Encoding.UTF8.GetString(bytes)));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }
        finally
        {
            FreeFoundItem(passwordData, itemRef);
        }
    }

    public Task WriteAsync(string key, string value, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var serviceBytes = Encoding.UTF8.GetBytes(Service);
        var accountBytes = Encoding.UTF8.GetBytes(key);
        var valueBytes = Encoding.UTF8.GetBytes(EncodeStoredValue(value));
        try
        {
            var status = SecKeychainFindGenericPasswordItem(
                IntPtr.Zero,
                checked((uint)serviceBytes.Length),
                serviceBytes,
                checked((uint)accountBytes.Length),
                accountBytes,
                IntPtr.Zero,
                IntPtr.Zero,
                out var existingItem);
            if (status == ErrSecSuccess)
            {
                try
                {
                    EnsureSuccess(
                        SecKeychainItemModifyContent(
                            existingItem,
                            IntPtr.Zero,
                            checked((uint)valueBytes.Length),
                            valueBytes),
                        "更新 macOS Keychain 备份凭据失败");
                }
                finally
                {
                    ReleaseItem(existingItem);
                }

                return Task.CompletedTask;
            }

            if (status != ErrSecItemNotFound)
            {
                EnsureSuccess(status, "查找 macOS Keychain 备份凭据失败");
            }

            var addStatus = SecKeychainAddGenericPassword(
                IntPtr.Zero,
                checked((uint)serviceBytes.Length),
                serviceBytes,
                checked((uint)accountBytes.Length),
                accountBytes,
                checked((uint)valueBytes.Length),
                valueBytes,
                out var addedItem);
            try
            {
                EnsureSuccess(addStatus, "写入 macOS Keychain 备份凭据失败");
            }
            finally
            {
                if (addedItem != IntPtr.Zero)
                {
                    CFRelease(addedItem);
                }
            }

            return Task.CompletedTask;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(valueBytes);
        }
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var serviceBytes = Encoding.UTF8.GetBytes(Service);
        var accountBytes = Encoding.UTF8.GetBytes(key);
        var status = SecKeychainFindGenericPasswordItem(
            IntPtr.Zero,
            checked((uint)serviceBytes.Length),
            serviceBytes,
            checked((uint)accountBytes.Length),
            accountBytes,
            IntPtr.Zero,
            IntPtr.Zero,
            out var itemRef);
        if (status == ErrSecItemNotFound)
        {
            return Task.CompletedTask;
        }

        EnsureSuccess(status, "查找 macOS Keychain 备份凭据失败");
        try
        {
            EnsureSuccess(
                SecKeychainItemDelete(itemRef),
                "删除 macOS Keychain 备份凭据失败");
            return Task.CompletedTask;
        }
        finally
        {
            ReleaseItem(itemRef);
        }
    }

    internal static string DecodeStoredValue(string value)
    {
        if (!value.StartsWith(EncodedValuePrefix, StringComparison.Ordinal))
        {
            return value;
        }

        try
        {
            return Encoding.UTF8.GetString(
                Convert.FromBase64String(value[EncodedValuePrefix.Length..]));
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException("macOS Keychain 中的备份凭据格式无效", ex);
        }
    }

    internal static string EncodeStoredValue(string value)
        => EncodedValuePrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    private static void FreeFoundItem(IntPtr passwordData, IntPtr itemRef)
    {
        if (passwordData != IntPtr.Zero)
        {
            _ = SecKeychainItemFreeContent(IntPtr.Zero, passwordData);
        }

        ReleaseItem(itemRef);
    }

    private static void ReleaseItem(IntPtr itemRef)
    {
        if (itemRef != IntPtr.Zero)
        {
            CFRelease(itemRef);
        }
    }

    private static void EnsureSuccess(int status, string message)
    {
        if (status != ErrSecSuccess)
        {
            throw new InvalidOperationException($"{message}（Security 状态 {status}）");
        }
    }

    [DllImport(SecurityFramework, CallingConvention = CallingConvention.Cdecl)]
    private static extern int SecKeychainFindGenericPassword(
        IntPtr keychainOrArray,
        uint serviceNameLength,
        byte[] serviceName,
        uint accountNameLength,
        byte[] accountName,
        out uint passwordLength,
        out IntPtr passwordData,
        out IntPtr itemRef);

    [DllImport(
        SecurityFramework,
        EntryPoint = "SecKeychainFindGenericPassword",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int SecKeychainFindGenericPasswordItem(
        IntPtr keychainOrArray,
        uint serviceNameLength,
        byte[] serviceName,
        uint accountNameLength,
        byte[] accountName,
        IntPtr passwordLength,
        IntPtr passwordData,
        out IntPtr itemRef);

    [DllImport(SecurityFramework, CallingConvention = CallingConvention.Cdecl)]
    private static extern int SecKeychainAddGenericPassword(
        IntPtr keychain,
        uint serviceNameLength,
        byte[] serviceName,
        uint accountNameLength,
        byte[] accountName,
        uint passwordLength,
        byte[] passwordData,
        out IntPtr itemRef);

    [DllImport(SecurityFramework, CallingConvention = CallingConvention.Cdecl)]
    private static extern int SecKeychainItemModifyContent(
        IntPtr itemRef,
        IntPtr attributes,
        uint length,
        byte[] data);

    [DllImport(SecurityFramework, CallingConvention = CallingConvention.Cdecl)]
    private static extern int SecKeychainItemFreeContent(IntPtr attributes, IntPtr data);

    [DllImport(SecurityFramework, CallingConvention = CallingConvention.Cdecl)]
    private static extern int SecKeychainItemDelete(IntPtr itemRef);

    [DllImport(CoreFoundationFramework, CallingConvention = CallingConvention.Cdecl)]
    private static extern void CFRelease(IntPtr handle);
}
