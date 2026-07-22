using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace IGoLibrary.Ex.Desktop.Platform.Power;

internal sealed class WindowsSystemIdleSleepInhibitor : ISystemIdleSleepInhibitor
{
    private readonly IWindowsPowerRequestNativeApi _nativeApi;
    private IWindowsPowerRequestHandle? _requestHandle;

    public WindowsSystemIdleSleepInhibitor(
        IWindowsPowerRequestNativeApi? nativeApi = null,
        IWindowsPowerRequestPlatformApi? platformApi = null)
    {
        if (nativeApi is not null && platformApi is not null)
        {
            throw new ArgumentException("Only one Windows power API test seam can be supplied.");
        }

        _nativeApi = nativeApi ?? new WindowsPowerRequestNativeApi(
            platformApi,
            NotifyCleanupFailure);
    }

    public event EventHandler<SystemSleepInhibitorException>? CleanupFailed;

    public string PlatformName => "Windows";

    public bool IsSupported => true;

    public bool IsActive => _requestHandle is not null;

    public void Activate(string reason)
    {
        if (_requestHandle is not null)
        {
            return;
        }

        var handle = _nativeApi.CreateRequest(reason);
        try
        {
            _nativeApi.SetSystemRequired(handle);
            _requestHandle = handle;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    public void Deactivate()
    {
        var handle = _requestHandle;
        if (handle is null)
        {
            return;
        }

        _nativeApi.ClearSystemRequired(handle);
        _requestHandle = null;
        handle.Dispose();
    }

    public void Dispose()
    {
        var handle = Interlocked.Exchange(ref _requestHandle, null);
        if (handle is null)
        {
            return;
        }

        try
        {
            _nativeApi.ClearSystemRequired(handle);
        }
        catch
        {
            // Closing the request handle still releases any request owned by this process.
        }
        finally
        {
            handle.Dispose();
        }
    }

    private void NotifyCleanupFailure(SystemSleepInhibitorException exception)
    {
        try
        {
            CleanupFailed?.Invoke(this, exception);
        }
        catch
        {
            // SafeHandle.ReleaseHandle must never let diagnostic observers break cleanup.
        }
    }
}

internal interface IWindowsPowerRequestHandle : IDisposable
{
}

internal interface IWindowsPowerRequestNativeApi
{
    IWindowsPowerRequestHandle CreateRequest(string reason);

    void SetSystemRequired(IWindowsPowerRequestHandle handle);

    void ClearSystemRequired(IWindowsPowerRequestHandle handle);
}

internal interface IWindowsPowerRequestPlatformApi
{
    nint PowerCreateRequest(ref WindowsPowerRequestNativeApi.PowerRequestContext context);

    bool PowerSetRequest(SafePowerRequestHandle handle, WindowsPowerRequestNativeApi.PowerRequestType requestType);

    bool PowerClearRequest(SafePowerRequestHandle handle, WindowsPowerRequestNativeApi.PowerRequestType requestType);

    bool CloseHandle(nint handle);

    int GetLastError();
}

internal sealed class WindowsPowerRequestNativeApi : IWindowsPowerRequestNativeApi
{
    private const uint PowerRequestContextVersion = 0;
    private const uint PowerRequestContextSimpleString = 1;

    private readonly IWindowsPowerRequestPlatformApi _platformApi;
    private readonly Action<SystemSleepInhibitorException>? _cleanupFailure;

    public WindowsPowerRequestNativeApi(
        IWindowsPowerRequestPlatformApi? platformApi = null,
        Action<SystemSleepInhibitorException>? cleanupFailure = null)
    {
        _platformApi = platformApi ?? new WindowsPowerRequestPlatformApi();
        _cleanupFailure = cleanupFailure;
    }

    public IWindowsPowerRequestHandle CreateRequest(string reason)
    {
        var reasonPointer = Marshal.StringToHGlobalUni(reason);
        try
        {
            var context = new PowerRequestContext
            {
                Version = PowerRequestContextVersion,
                Flags = PowerRequestContextSimpleString,
                Reason = new PowerRequestContextUnion
                {
                    SimpleReasonString = reasonPointer
                }
            };
            var rawHandle = _platformApi.PowerCreateRequest(ref context);
            if (rawHandle is 0 or -1)
            {
                throw CreateException("PowerCreateRequest", _platformApi.GetLastError());
            }

            return new WindowsPowerRequestHandle(
                new SafePowerRequestHandle(rawHandle, _platformApi, _cleanupFailure));
        }
        finally
        {
            Marshal.FreeHGlobal(reasonPointer);
        }
    }

    public void SetSystemRequired(IWindowsPowerRequestHandle handle)
    {
        var nativeHandle = GetHandle(handle);
        if (!_platformApi.PowerSetRequest(nativeHandle, PowerRequestType.SystemRequired))
        {
            throw CreateException("PowerSetRequest", _platformApi.GetLastError());
        }
    }

    public void ClearSystemRequired(IWindowsPowerRequestHandle handle)
    {
        var nativeHandle = GetHandle(handle);
        if (!_platformApi.PowerClearRequest(nativeHandle, PowerRequestType.SystemRequired))
        {
            throw CreateException("PowerClearRequest", _platformApi.GetLastError());
        }
    }

    private static SafePowerRequestHandle GetHandle(IWindowsPowerRequestHandle handle)
    {
        return handle is WindowsPowerRequestHandle windowsHandle
            ? windowsHandle.Handle
            : throw new ArgumentException("Windows power request handle type is invalid.", nameof(handle));
    }

    internal static SystemSleepInhibitorException CreateException(string operation, int errorCode)
    {
        var win32Exception = new Win32Exception(errorCode);
        return new SystemSleepInhibitorException(
            "Windows",
            operation,
            errorCode,
            $"{operation} failed with Win32 error {errorCode}: {win32Exception.Message}",
            win32Exception);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PowerRequestContext
    {
        public uint Version;
        public uint Flags;
        public PowerRequestContextUnion Reason;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct PowerRequestContextUnion
    {
        [FieldOffset(0)]
        public DetailedPowerRequestContext Detailed;

        [FieldOffset(0)]
        public nint SimpleReasonString;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DetailedPowerRequestContext
    {
        public nint LocalizedReasonModule;
        public uint LocalizedReasonId;
        public uint ReasonStringCount;
        public nint ReasonStrings;
    }

    internal enum PowerRequestType
    {
        DisplayRequired = 0,
        SystemRequired = 1,
        AwayModeRequired = 2,
        ExecutionRequired = 3
    }

    private sealed class WindowsPowerRequestHandle(SafePowerRequestHandle handle) : IWindowsPowerRequestHandle
    {
        internal SafePowerRequestHandle Handle { get; } = handle;

        public void Dispose() => Handle.Dispose();
    }
}

internal sealed class SafePowerRequestHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    private readonly IWindowsPowerRequestPlatformApi _platformApi;
    private readonly Action<SystemSleepInhibitorException>? _cleanupFailure;

    public SafePowerRequestHandle(
        nint handle,
        IWindowsPowerRequestPlatformApi platformApi,
        Action<SystemSleepInhibitorException>? cleanupFailure)
        : base(ownsHandle: true)
    {
        _platformApi = platformApi;
        _cleanupFailure = cleanupFailure;
        SetHandle(handle);
    }

    protected override bool ReleaseHandle()
    {
        if (_platformApi.CloseHandle(handle))
        {
            return true;
        }

        var exception = WindowsPowerRequestNativeApi.CreateException(
            "CloseHandle",
            _platformApi.GetLastError());
        try
        {
            _cleanupFailure?.Invoke(exception);
        }
        catch
        {
            // ReleaseHandle must never throw.
        }

        return false;
    }
}

internal sealed class WindowsPowerRequestPlatformApi : IWindowsPowerRequestPlatformApi
{
    public nint PowerCreateRequest(ref WindowsPowerRequestNativeApi.PowerRequestContext context)
        => NativeMethods.PowerCreateRequest(ref context);

    public bool PowerSetRequest(
        SafePowerRequestHandle handle,
        WindowsPowerRequestNativeApi.PowerRequestType requestType)
        => NativeMethods.PowerSetRequest(handle, requestType);

    public bool PowerClearRequest(
        SafePowerRequestHandle handle,
        WindowsPowerRequestNativeApi.PowerRequestType requestType)
        => NativeMethods.PowerClearRequest(handle, requestType);

    public bool CloseHandle(nint handle) => NativeMethods.CloseHandle(handle);

    public int GetLastError() => Marshal.GetLastPInvokeError();

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern nint PowerCreateRequest(
            ref WindowsPowerRequestNativeApi.PowerRequestContext context);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool PowerSetRequest(
            SafePowerRequestHandle powerRequest,
            WindowsPowerRequestNativeApi.PowerRequestType requestType);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool PowerClearRequest(
            SafePowerRequestHandle powerRequest,
            WindowsPowerRequestNativeApi.PowerRequestType requestType);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseHandle(nint handle);
    }
}
