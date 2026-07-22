using System.Runtime.InteropServices;
using IGoLibrary.Ex.Desktop.Platform.Power;

namespace IGoLibrary.Ex.Tests;

public sealed class SystemIdleSleepInhibitorTests
{
    [Fact]
    public void WindowsReasonContext_UsesNativeUnionLayout()
    {
        Assert.Equal(8, Marshal.OffsetOf<WindowsPowerRequestNativeApi.PowerRequestContext>(
            nameof(WindowsPowerRequestNativeApi.PowerRequestContext.Reason)).ToInt32());
        Assert.Equal(
            nint.Size == 8 ? 32 : 24,
            Marshal.SizeOf<WindowsPowerRequestNativeApi.PowerRequestContext>());
    }

    [Fact]
    public void WindowsNativeApi_PassesSimpleReasonAndSystemRequiredRequestType()
    {
        var platformApi = new RecordingWindowsPowerRequestPlatformApi();
        var nativeApi = new WindowsPowerRequestNativeApi(platformApi);

        using var handle = nativeApi.CreateRequest("IGoLibrary-Ex 正在执行图书馆任务");
        nativeApi.SetSystemRequired(handle);
        nativeApi.ClearSystemRequired(handle);

        Assert.Equal(0u, platformApi.ContextVersion);
        Assert.Equal(1u, platformApi.ContextFlags);
        Assert.Equal("IGoLibrary-Ex 正在执行图书馆任务", platformApi.Reason);
        Assert.Equal(
            [WindowsPowerRequestNativeApi.PowerRequestType.SystemRequired],
            platformApi.SetRequestTypes);
        Assert.Equal(
            [WindowsPowerRequestNativeApi.PowerRequestType.SystemRequired],
            platformApi.ClearRequestTypes);
    }

    [Fact]
    public void WindowsNativeApi_CloseFailureReportsOperationAndWin32Error()
    {
        var platformApi = new RecordingWindowsPowerRequestPlatformApi
        {
            CloseResult = false,
            LastError = 6
        };
        var failures = new List<SystemSleepInhibitorException>();
        var nativeApi = new WindowsPowerRequestNativeApi(platformApi, failures.Add);
        var handle = nativeApi.CreateRequest("测试原因");

        handle.Dispose();

        var failure = Assert.Single(failures);
        Assert.Equal("Windows", failure.PlatformName);
        Assert.Equal("CloseHandle", failure.Operation);
        Assert.Equal(6, failure.NativeErrorCode);
        Assert.Equal([platformApi.RawHandle], platformApi.ClosedHandles);
    }

    [Fact]
    public void WindowsInhibitor_ForwardsSafeHandleCleanupFailure()
    {
        var platformApi = new RecordingWindowsPowerRequestPlatformApi
        {
            CloseResult = false,
            LastError = 6
        };
        using var inhibitor = new WindowsSystemIdleSleepInhibitor(platformApi: platformApi);
        var failures = new List<SystemSleepInhibitorException>();
        inhibitor.CleanupFailed += (_, exception) => failures.Add(exception);

        inhibitor.Activate("测试原因");
        inhibitor.Deactivate();

        var failure = Assert.Single(failures);
        Assert.Equal("CloseHandle", failure.Operation);
        Assert.Equal(6, failure.NativeErrorCode);
    }

    [Fact]
    public void WindowsInhibitor_UsesOneSystemRequestAndDisposesItsHandle()
    {
        var nativeApi = new RecordingWindowsPowerRequestNativeApi();
        using var inhibitor = new WindowsSystemIdleSleepInhibitor(nativeApi);

        inhibitor.Activate("测试原因");
        inhibitor.Activate("不会重复申请");
        inhibitor.Deactivate();
        inhibitor.Deactivate();

        Assert.False(inhibitor.IsActive);
        Assert.Equal(["测试原因"], nativeApi.Reasons);
        Assert.Equal(1, nativeApi.SetCalls);
        Assert.Equal(1, nativeApi.ClearCalls);
        Assert.Equal(1, nativeApi.Handle.DisposeCalls);
    }

    [Fact]
    public void WindowsInhibitor_SetFailureClosesNewHandleWithoutBecomingActive()
    {
        var nativeApi = new RecordingWindowsPowerRequestNativeApi
        {
            SetException = new SystemSleepInhibitorException(
                "Windows",
                "PowerSetRequest",
                5,
                "access denied")
        };
        using var inhibitor = new WindowsSystemIdleSleepInhibitor(nativeApi);

        var error = Assert.Throws<SystemSleepInhibitorException>(() => inhibitor.Activate("测试原因"));

        Assert.Equal(5, error.NativeErrorCode);
        Assert.False(inhibitor.IsActive);
        Assert.Equal(1, nativeApi.Handle.DisposeCalls);
    }

    [Fact]
    public void WindowsInhibitor_ClearFailureRetainsHandleForRetry()
    {
        var nativeApi = new RecordingWindowsPowerRequestNativeApi
        {
            ClearException = new SystemSleepInhibitorException(
                "Windows",
                "PowerClearRequest",
                31,
                "device error")
        };
        using var inhibitor = new WindowsSystemIdleSleepInhibitor(nativeApi);
        inhibitor.Activate("测试原因");

        Assert.Throws<SystemSleepInhibitorException>(inhibitor.Deactivate);
        Assert.True(inhibitor.IsActive);
        Assert.Equal(0, nativeApi.Handle.DisposeCalls);

        nativeApi.ClearException = null;
        inhibitor.Deactivate();
        Assert.False(inhibitor.IsActive);
        Assert.Equal(2, nativeApi.ClearCalls);
        Assert.Equal(1, nativeApi.Handle.DisposeCalls);
    }

    [Fact]
    public void MacInhibitor_CreatesAndReleasesOnePreventUserIdleSystemSleepAssertion()
    {
        var nativeApi = new RecordingMacPowerAssertionNativeApi { AssertionId = 42 };
        using var inhibitor = new MacSystemIdleSleepInhibitor(nativeApi);

        inhibitor.Activate("测试原因");
        inhibitor.Activate("不会重复申请");
        inhibitor.Deactivate();
        inhibitor.Deactivate();

        Assert.False(inhibitor.IsActive);
        Assert.Equal(["测试原因"], nativeApi.Reasons);
        Assert.Equal([42u], nativeApi.ReleasedAssertionIds);
        Assert.Equal("PreventUserIdleSystemSleep", MacPowerAssertionNativeApi.PreventUserIdleSystemSleep);
    }

    [Fact]
    public void MacInhibitor_ReleaseFailureRetainsAssertionForRetry()
    {
        var nativeApi = new RecordingMacPowerAssertionNativeApi
        {
            AssertionId = 7,
            ReleaseException = new SystemSleepInhibitorException(
                "macOS",
                "IOPMAssertionRelease",
                -1,
                "release failed")
        };
        using var inhibitor = new MacSystemIdleSleepInhibitor(nativeApi);
        inhibitor.Activate("测试原因");

        Assert.Throws<SystemSleepInhibitorException>(inhibitor.Deactivate);
        Assert.True(inhibitor.IsActive);

        nativeApi.ReleaseException = null;
        inhibitor.Deactivate();
        Assert.False(inhibitor.IsActive);
        Assert.Equal([7u, 7u], nativeApi.ReleasedAssertionIds);
    }

    [Fact]
    public void MacNativeApi_PassesAssertionTypeLevelAndReasonAndReleasesStrings()
    {
        var platformApi = new RecordingMacPowerAssertionPlatformApi { AssertionId = 42 };
        var nativeApi = new MacPowerAssertionNativeApi(platformApi);

        var assertionId = nativeApi.CreatePreventUserIdleSystemSleepAssertion("测试原因");
        nativeApi.ReleaseAssertion(assertionId);

        Assert.Equal(42u, assertionId);
        Assert.Equal(
            [MacPowerAssertionNativeApi.PreventUserIdleSystemSleep, "测试原因"],
            platformApi.CreatedStrings.Select(item => item.Value).ToArray());
        Assert.All(
            platformApi.CreatedStrings,
            item =>
            {
                Assert.Equal(nint.Zero, item.Allocator);
                Assert.Equal(MacPowerAssertionNativeApi.Utf8Encoding, item.Encoding);
            });
        Assert.Equal(MacPowerAssertionNativeApi.AssertionLevelOn, platformApi.AssertionLevel);
        Assert.Equal(MacPowerAssertionNativeApi.PreventUserIdleSystemSleep, platformApi.AssertionType);
        Assert.Equal("测试原因", platformApi.AssertionName);
        Assert.Equal(
            platformApi.CreatedStrings.Select(item => item.Handle).Reverse(),
            platformApi.ReleasedStrings);
        Assert.Equal([42u], platformApi.ReleasedAssertionIds);
    }

    [Fact]
    public void MacNativeApi_CreateFailureStillReleasesBothCoreFoundationStrings()
    {
        var platformApi = new RecordingMacPowerAssertionPlatformApi { CreateResult = -536870211 };
        var nativeApi = new MacPowerAssertionNativeApi(platformApi);

        var error = Assert.Throws<SystemSleepInhibitorException>(() =>
            nativeApi.CreatePreventUserIdleSystemSleepAssertion("测试原因"));

        Assert.Equal("IOPMAssertionCreateWithName", error.Operation);
        Assert.Equal(-536870211, error.NativeErrorCode);
        Assert.Equal(
            platformApi.CreatedStrings.Select(item => item.Handle).Reverse(),
            platformApi.ReleasedStrings);
    }

    private sealed class RecordingWindowsPowerRequestPlatformApi : IWindowsPowerRequestPlatformApi
    {
        public nint RawHandle { get; set; } = 123;

        public bool SetResult { get; set; } = true;

        public bool ClearResult { get; set; } = true;

        public bool CloseResult { get; set; } = true;

        public int LastError { get; set; }

        public uint ContextVersion { get; private set; }

        public uint ContextFlags { get; private set; }

        public string? Reason { get; private set; }

        public List<WindowsPowerRequestNativeApi.PowerRequestType> SetRequestTypes { get; } = [];

        public List<WindowsPowerRequestNativeApi.PowerRequestType> ClearRequestTypes { get; } = [];

        public List<nint> ClosedHandles { get; } = [];

        public nint PowerCreateRequest(ref WindowsPowerRequestNativeApi.PowerRequestContext context)
        {
            ContextVersion = context.Version;
            ContextFlags = context.Flags;
            Reason = Marshal.PtrToStringUni(context.Reason.SimpleReasonString);
            return RawHandle;
        }

        public bool PowerSetRequest(
            SafePowerRequestHandle handle,
            WindowsPowerRequestNativeApi.PowerRequestType requestType)
        {
            Assert.Equal(RawHandle, handle.DangerousGetHandle());
            SetRequestTypes.Add(requestType);
            return SetResult;
        }

        public bool PowerClearRequest(
            SafePowerRequestHandle handle,
            WindowsPowerRequestNativeApi.PowerRequestType requestType)
        {
            Assert.Equal(RawHandle, handle.DangerousGetHandle());
            ClearRequestTypes.Add(requestType);
            return ClearResult;
        }

        public bool CloseHandle(nint handle)
        {
            ClosedHandles.Add(handle);
            return CloseResult;
        }

        public int GetLastError() => LastError;
    }

    private sealed class RecordingWindowsPowerRequestNativeApi : IWindowsPowerRequestNativeApi
    {
        public RecordingWindowsPowerRequestHandle Handle { get; } = new();

        public List<string> Reasons { get; } = [];

        public int SetCalls { get; private set; }

        public int ClearCalls { get; private set; }

        public Exception? SetException { get; set; }

        public Exception? ClearException { get; set; }

        public IWindowsPowerRequestHandle CreateRequest(string reason)
        {
            Reasons.Add(reason);
            return Handle;
        }

        public void SetSystemRequired(IWindowsPowerRequestHandle handle)
        {
            Assert.Same(Handle, handle);
            SetCalls++;
            if (SetException is not null)
            {
                throw SetException;
            }
        }

        public void ClearSystemRequired(IWindowsPowerRequestHandle handle)
        {
            Assert.Same(Handle, handle);
            ClearCalls++;
            if (ClearException is not null)
            {
                throw ClearException;
            }
        }
    }

    private sealed class RecordingWindowsPowerRequestHandle : IWindowsPowerRequestHandle
    {
        public int DisposeCalls { get; private set; }

        public void Dispose() => DisposeCalls++;
    }

    private sealed class RecordingMacPowerAssertionNativeApi : IMacPowerAssertionNativeApi
    {
        public uint AssertionId { get; set; }

        public Exception? ReleaseException { get; set; }

        public List<string> Reasons { get; } = [];

        public List<uint> ReleasedAssertionIds { get; } = [];

        public uint CreatePreventUserIdleSystemSleepAssertion(string reason)
        {
            Reasons.Add(reason);
            return AssertionId;
        }

        public void ReleaseAssertion(uint assertionId)
        {
            ReleasedAssertionIds.Add(assertionId);
            if (ReleaseException is not null)
            {
                throw ReleaseException;
            }
        }
    }

    private sealed class RecordingMacPowerAssertionPlatformApi : IMacPowerAssertionPlatformApi
    {
        private readonly Dictionary<nint, string> _strings = [];
        private int _nextHandle = 100;

        public uint AssertionId { get; set; }

        public int CreateResult { get; set; }

        public int ReleaseResult { get; set; }

        public uint AssertionLevel { get; private set; }

        public string? AssertionType { get; private set; }

        public string? AssertionName { get; private set; }

        public List<(nint Allocator, string Value, uint Encoding, nint Handle)> CreatedStrings { get; } = [];

        public List<nint> ReleasedStrings { get; } = [];

        public List<uint> ReleasedAssertionIds { get; } = [];

        public nint CFStringCreateWithCString(nint allocator, string value, uint encoding)
        {
            var handle = new nint(Interlocked.Increment(ref _nextHandle));
            _strings.Add(handle, value);
            CreatedStrings.Add((allocator, value, encoding, handle));
            return handle;
        }

        public void CFRelease(nint value) => ReleasedStrings.Add(value);

        public int IOPMAssertionCreateWithName(
            nint assertionType,
            uint assertionLevel,
            nint assertionName,
            out uint assertionId)
        {
            AssertionType = _strings[assertionType];
            AssertionLevel = assertionLevel;
            AssertionName = _strings[assertionName];
            assertionId = AssertionId;
            return CreateResult;
        }

        public int IOPMAssertionRelease(uint assertionId)
        {
            ReleasedAssertionIds.Add(assertionId);
            return ReleaseResult;
        }
    }
}
