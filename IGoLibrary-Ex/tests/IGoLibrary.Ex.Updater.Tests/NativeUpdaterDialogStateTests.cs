using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.InteropServices;

namespace IGoLibrary.Ex.Updater.Tests;

public sealed class NativeUpdaterDialogStateTests
{
    [Fact]
    public void NativeStructuresMatchWindowsX64Layout()
    {
        Assert.True(Environment.Is64BitProcess);
        Assert.Equal(160, Marshal.SizeOf<TaskDialogConfig>());
        Assert.Equal(12, Marshal.SizeOf<TaskDialogButton>());
        Assert.Equal((nint)4, Marshal.OffsetOf<TaskDialogButton>(nameof(TaskDialogButton.ButtonText)));
        Assert.Equal((nint)4, Marshal.OffsetOf<TaskDialogConfig>(nameof(TaskDialogConfig.ParentWindow)));
        Assert.Equal((nint)64, Marshal.OffsetOf<TaskDialogConfig>(nameof(TaskDialogConfig.Buttons)));
        Assert.Equal((nint)140, Marshal.OffsetOf<TaskDialogConfig>(nameof(TaskDialogConfig.Callback)));
        Assert.Equal((nint)156, Marshal.OffsetOf<TaskDialogConfig>(nameof(TaskDialogConfig.Width)));
    }

    [Fact]
    public void NativeConstantsPreserveTaskDialogProtocol()
    {
        Assert.Equal(1001, GetConstant<int>("ProgressButtonId"));
        Assert.Equal(1002, GetConstant<int>("GitHubButtonId"));
        Assert.Equal(1003, GetConstant<int>("CloseButtonId"));
        Assert.Equal(0U, GetConstant<uint>("TaskDialogNotificationCreated"));
        Assert.Equal(2U, GetConstant<uint>("TaskDialogNotificationButtonClicked"));
        Assert.Equal(5U, GetConstant<uint>("TaskDialogNotificationDestroyed"));
        Assert.Equal(0x0400U + 102, GetConstant<uint>("TaskDialogMessageClickButton"));
        Assert.Equal(0x0400U + 107, GetConstant<uint>("TaskDialogMessageSetProgressBarMarquee"));
        Assert.Equal(0x0400U + 108, GetConstant<uint>("TaskDialogMessageSetElementText"));
        Assert.Equal(0x0400U + 111, GetConstant<uint>("TaskDialogMessageEnableButton"));
    }

    [Fact]
    public void NativeBoundaryUsesSourceGeneratedImportsAndHardenedManifest()
    {
        var updaterRoot = Path.Combine(FindProjectRoot(), "src", "IGoLibrary.Ex.Updater");
        var nativeMethods = File.ReadAllText(Path.Combine(updaterRoot, "NativeMethods.cs"));
        var manifest = File.ReadAllText(Path.Combine(updaterRoot, "app.manifest"));

        Assert.Contains("[LibraryImport", nativeMethods, StringComparison.Ordinal);
        Assert.DoesNotContain("[DllImport", nativeMethods, StringComparison.Ordinal);
        Assert.Contains("DllImportSearchPath.System32", nativeMethods, StringComparison.Ordinal);
        Assert.Contains("TaskDialogIndirect", nativeMethods, StringComparison.Ordinal);
        Assert.Contains("SendMessageW", nativeMethods, StringComparison.Ordinal);
        Assert.Contains("MessageBoxW", nativeMethods, StringComparison.Ordinal);
        Assert.Contains("Microsoft.Windows.Common-Controls", manifest, StringComparison.Ordinal);
        Assert.Contains("PerMonitorV2", manifest, StringComparison.Ordinal);
        Assert.Contains("level=\"asInvoker\"", manifest, StringComparison.Ordinal);
        Assert.Contains("uiAccess=\"false\"", manifest, StringComparison.Ordinal);
    }

    [Fact]
    public void ProgressState_DoesNotStartBeforeDialogCreation()
    {
        var sink = new RecordingMessageSink();
        var state = new NativeUpdaterDialog.ProgressDialogState(
            _ => Task.FromResult(new CoordinatorResult(true, "完成", false)),
            sink);

        Assert.Null(state.Operation);
        Assert.Null(state.Result);
        Assert.False(state.IsCompleted);
        Assert.Empty(sink.Messages);
    }

    [Fact]
    public async Task ProgressState_UiInitializationFailureDoesNotStartCoordinator()
    {
        var coordinatorCalls = 0;
        var state = new NativeUpdaterDialog.ProgressDialogState(
            _ =>
            {
                Interlocked.Increment(ref coordinatorCalls);
                return Task.FromResult(new CoordinatorResult(true, "不应运行", false));
            },
            new ThrowingMessageSink());

        state.OnCreated(21);
        await state.Operation!;

        Assert.Equal(0, coordinatorCalls);
        Assert.True(state.IsCompleted);
        Assert.False(state.Result!.Succeeded);
        Assert.Contains("安装尚未开始", state.Result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProgressState_DisablesUntilResultThenClosesProgrammatically()
    {
        var completion = new TaskCompletionSource<CoordinatorResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Action<string>? reportStatus = null;
        var sink = new RecordingMessageSink();
        var state = new NativeUpdaterDialog.ProgressDialogState(
            report =>
            {
                reportStatus = report;
                return completion.Task;
            },
            sink);

        state.OnCreated(42);
        Assert.True(SpinWait.SpinUntil(() => reportStatus is not null, TimeSpan.FromSeconds(2)));
        Assert.False(state.IsCompleted);
        reportStatus!("正在验证中文路径…");

        var expected = new CoordinatorResult(true, "完成", false);
        completion.SetResult(expected);
        await state.Operation!;

        Assert.True(state.IsCompleted);
        Assert.Same(expected, state.Result);
        Assert.Contains("enable:42:1001:False", sink.Messages);
        Assert.Contains("marquee:42:25", sink.Messages);
        Assert.Contains("content:42:正在验证中文路径…", sink.Messages);
        Assert.Contains("enable:42:1001:True", sink.Messages);
        Assert.Contains("click:42:1001", sink.Messages);
    }

    [Fact]
    public async Task ProgressState_DoesNotSendCompletionToDestroyedDialog()
    {
        var completion = new TaskCompletionSource<CoordinatorResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var sink = new RecordingMessageSink();
        var state = new NativeUpdaterDialog.ProgressDialogState(
            _ => completion.Task,
            sink);

        state.OnCreated(84);
        state.OnDestroyed(84);
        completion.SetResult(new CoordinatorResult(false, "失败", true));
        await state.Operation!;

        Assert.True(state.IsCompleted);
        Assert.DoesNotContain("click:84:1001", sink.Messages);
    }

    [Fact]
    public async Task ProgressState_ContainsUnexpectedRunnerException()
    {
        var sink = new RecordingMessageSink();
        var state = new NativeUpdaterDialog.ProgressDialogState(
            _ => throw new InvalidOperationException("boom"),
            sink);

        state.OnCreated(126);
        await state.Operation!;

        Assert.True(state.IsCompleted);
        Assert.False(state.Result!.Succeeded);
        Assert.True(state.Result.ShouldShowMessage);
        Assert.Contains("boom", state.Result.Message, StringComparison.Ordinal);
        Assert.Contains("click:126:1001", sink.Messages);
    }

    [Fact]
    public async Task ProgressState_AcceptsConcurrentStatusUpdates()
    {
        var completion = new TaskCompletionSource<CoordinatorResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var statusReady = new TaskCompletionSource<Action<string>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var sink = new RecordingMessageSink();
        var state = new NativeUpdaterDialog.ProgressDialogState(
            report =>
            {
                statusReady.SetResult(report);
                return completion.Task;
            },
            sink);

        state.OnCreated(168);
        var reportStatus = await statusReady.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.WhenAll(
            Enumerable.Range(0, 32)
                .Select(index => Task.Run(() => reportStatus($"状态-{index}"))));
        completion.SetResult(new CoordinatorResult(true, "完成", false));
        await state.Operation!;

        Assert.Equal(
            32,
            sink.Messages.Count(message => message.StartsWith("content:168:状态-", StringComparison.Ordinal)));
        Assert.Contains("click:168:1001", sink.Messages);
    }

    [Fact]
    public void FailureState_ShowsCopyableUrlWhenBrowserCannotOpen()
    {
        nint actualWindowHandle = 0;
        string? actualContent = null;
        var state = new NativeUpdaterDialog.FailureDialogState(
            "安装失败",
            () => false,
            (windowHandle, content) =>
            {
                actualWindowHandle = windowHandle;
                actualContent = content;
            });

        state.OpenReleasesPage(210);

        Assert.Equal((nint)210, actualWindowHandle);
        Assert.Contains("安装失败", actualContent, StringComparison.Ordinal);
        Assert.Contains(
            "https://github.com/EJianZQ/IGoLibrary/releases",
            actualContent,
            StringComparison.Ordinal);
    }

    private static T GetConstant<T>(string name)
    {
        var field = typeof(NativeUpdaterDialog).GetField(
            name,
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsType<T>(field.GetRawConstantValue());
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IGoLibrary-Ex.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate IGoLibrary-Ex.sln.");
    }

    private sealed class RecordingMessageSink : NativeUpdaterDialog.ITaskDialogMessageSink
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public void EnableButton(nint windowHandle, int buttonId, bool enabled)
        {
            Messages.Enqueue($"enable:{windowHandle}:{buttonId}:{enabled}");
        }

        public void StartMarquee(nint windowHandle, int intervalMilliseconds)
        {
            Messages.Enqueue($"marquee:{windowHandle}:{intervalMilliseconds}");
        }

        public void SetContent(nint windowHandle, string message)
        {
            Messages.Enqueue($"content:{windowHandle}:{message}");
        }

        public void ClickButton(nint windowHandle, int buttonId)
        {
            Messages.Enqueue($"click:{windowHandle}:{buttonId}");
        }
    }

    private sealed class ThrowingMessageSink : NativeUpdaterDialog.ITaskDialogMessageSink
    {
        public void EnableButton(nint windowHandle, int buttonId, bool enabled)
        {
            throw new InvalidOperationException("native UI unavailable");
        }

        public void StartMarquee(nint windowHandle, int intervalMilliseconds)
        {
            throw new InvalidOperationException("native UI unavailable");
        }

        public void SetContent(nint windowHandle, string message)
        {
            throw new InvalidOperationException("native UI unavailable");
        }

        public void ClickButton(nint windowHandle, int buttonId)
        {
            throw new InvalidOperationException("native UI unavailable");
        }
    }
}
