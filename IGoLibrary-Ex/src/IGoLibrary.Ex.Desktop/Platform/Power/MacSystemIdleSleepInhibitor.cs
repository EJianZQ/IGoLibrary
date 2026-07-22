using System.Runtime.InteropServices;

namespace IGoLibrary.Ex.Desktop.Platform.Power;

internal sealed class MacSystemIdleSleepInhibitor(
    IMacPowerAssertionNativeApi? nativeApi = null) : ISystemIdleSleepInhibitor
{
    private readonly IMacPowerAssertionNativeApi _nativeApi = nativeApi ?? new MacPowerAssertionNativeApi();
    private uint? _assertionId;

    public event EventHandler<SystemSleepInhibitorException>? CleanupFailed
    {
        add { }
        remove { }
    }

    public string PlatformName => "macOS";

    public bool IsSupported => true;

    public bool IsActive => _assertionId.HasValue;

    public void Activate(string reason)
    {
        if (_assertionId.HasValue)
        {
            return;
        }

        _assertionId = _nativeApi.CreatePreventUserIdleSystemSleepAssertion(reason);
    }

    public void Deactivate()
    {
        if (_assertionId is not { } assertionId)
        {
            return;
        }

        _nativeApi.ReleaseAssertion(assertionId);
        _assertionId = null;
    }

    public void Dispose()
    {
        if (_assertionId is not { } assertionId)
        {
            return;
        }

        _assertionId = null;

        try
        {
            _nativeApi.ReleaseAssertion(assertionId);
        }
        catch
        {
            // Process exit remains the final OS-level cleanup path.
        }
    }
}

internal interface IMacPowerAssertionNativeApi
{
    uint CreatePreventUserIdleSystemSleepAssertion(string reason);

    void ReleaseAssertion(uint assertionId);
}

internal interface IMacPowerAssertionPlatformApi
{
    nint CFStringCreateWithCString(nint allocator, string value, uint encoding);

    void CFRelease(nint value);

    int IOPMAssertionCreateWithName(
        nint assertionType,
        uint assertionLevel,
        nint assertionName,
        out uint assertionId);

    int IOPMAssertionRelease(uint assertionId);
}

internal sealed class MacPowerAssertionNativeApi(
    IMacPowerAssertionPlatformApi? platformApi = null) : IMacPowerAssertionNativeApi
{
    internal const string PreventUserIdleSystemSleep = "PreventUserIdleSystemSleep";
    internal const uint AssertionLevelOn = 255;
    internal const uint Utf8Encoding = 0x08000100;
    private const int IoReturnSuccess = 0;

    private readonly IMacPowerAssertionPlatformApi _platformApi =
        platformApi ?? new MacPowerAssertionPlatformApi();

    public uint CreatePreventUserIdleSystemSleepAssertion(string reason)
    {
        var assertionType = CreateString(PreventUserIdleSystemSleep, "assertion type");
        try
        {
            var assertionName = CreateString(reason, "assertion name");
            try
            {
                var result = _platformApi.IOPMAssertionCreateWithName(
                    assertionType,
                    AssertionLevelOn,
                    assertionName,
                    out var assertionId);
                if (result != IoReturnSuccess)
                {
                    throw CreateException("IOPMAssertionCreateWithName", result);
                }

                return assertionId;
            }
            finally
            {
                _platformApi.CFRelease(assertionName);
            }
        }
        finally
        {
            _platformApi.CFRelease(assertionType);
        }
    }

    public void ReleaseAssertion(uint assertionId)
    {
        var result = _platformApi.IOPMAssertionRelease(assertionId);
        if (result != IoReturnSuccess)
        {
            throw CreateException("IOPMAssertionRelease", result);
        }
    }

    private nint CreateString(string value, string purpose)
    {
        var handle = _platformApi.CFStringCreateWithCString(nint.Zero, value, Utf8Encoding);
        if (handle == nint.Zero)
        {
            throw new SystemSleepInhibitorException(
                "macOS",
                "CFStringCreateWithCString",
                -1,
                $"CFStringCreateWithCString failed while creating the {purpose}.");
        }

        return handle;
    }

    private static SystemSleepInhibitorException CreateException(string operation, int errorCode)
    {
        return new SystemSleepInhibitorException(
            "macOS",
            operation,
            errorCode,
            $"{operation} failed with IOKit return code 0x{errorCode:X8}.");
    }
}

internal sealed class MacPowerAssertionPlatformApi : IMacPowerAssertionPlatformApi
{
    public nint CFStringCreateWithCString(nint allocator, string value, uint encoding)
        => NativeMethods.CFStringCreateWithCString(allocator, value, encoding);

    public void CFRelease(nint value) => NativeMethods.CFRelease(value);

    public int IOPMAssertionCreateWithName(
        nint assertionType,
        uint assertionLevel,
        nint assertionName,
        out uint assertionId)
        => NativeMethods.IOPMAssertionCreateWithName(
            assertionType,
            assertionLevel,
            assertionName,
            out assertionId);

    public int IOPMAssertionRelease(uint assertionId)
        => NativeMethods.IOPMAssertionRelease(assertionId);

    private static class NativeMethods
    {
        private const string IOKitLibrary = "/System/Library/Frameworks/IOKit.framework/IOKit";
        private const string CoreFoundationLibrary =
            "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

        [DllImport(IOKitLibrary)]
        internal static extern int IOPMAssertionCreateWithName(
            nint assertionType,
            uint assertionLevel,
            nint assertionName,
            out uint assertionId);

        [DllImport(IOKitLibrary)]
        internal static extern int IOPMAssertionRelease(uint assertionId);

        [DllImport(CoreFoundationLibrary)]
        internal static extern nint CFStringCreateWithCString(
            nint allocator,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string value,
            uint encoding);

        [DllImport(CoreFoundationLibrary)]
        internal static extern void CFRelease(nint value);
    }
}
