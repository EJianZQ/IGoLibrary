using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace IGoLibrary.Ex.Updater;

internal static class NativeUpdaterDialog
{
    private const string ReleasesUrl = "https://github.com/EJianZQ/IGoLibrary/releases";
    private const int ProgressButtonId = 1001;
    private const int GitHubButtonId = 1002;
    private const int CloseButtonId = 1003;
    private const int SucceededHResult = 0;
    private const int PreventCloseHResult = 1;

    private const uint TaskDialogFlagShowMarqueeProgressBar = 0x00000400;
    private const uint TaskDialogFlagSizeToContent = 0x01000000;
    private const uint TaskDialogFlagAllowDialogCancellation = 0x00000008;
    private const uint TaskDialogCommonButtonClose = 0x00000020;
    private const uint TaskDialogNotificationCreated = 0;
    private const uint TaskDialogNotificationButtonClicked = 2;
    private const uint TaskDialogNotificationDestroyed = 5;
    private const uint TaskDialogMessageClickButton = 0x0400 + 102;
    private const uint TaskDialogMessageSetProgressBarMarquee = 0x0400 + 107;
    private const uint TaskDialogMessageSetElementText = 0x0400 + 108;
    private const uint TaskDialogMessageEnableButton = 0x0400 + 111;
    private const uint TaskDialogElementContent = 0;
    private const uint MessageBoxOk = 0x00000000;
    private const uint MessageBoxIconError = 0x00000010;
    private const uint MessageBoxSetForeground = 0x00010000;

    private static readonly nint TaskDialogInformationIcon = (nint)0xFFFD;
    private static readonly nint TaskDialogErrorIcon = (nint)0xFFFE;

    public static int RunCoordinator(string requestPath, bool externalWorker)
    {
        var state = new ProgressDialogState(requestPath, externalWorker);
        var dialogResult = int.MinValue;
        Exception? dialogException = null;
        try
        {
            dialogResult = ShowProgressDialog(state);
        }
        catch (Exception exception)
        {
            dialogException = exception;
        }

        var operation = state.Operation;
        if (operation is not null)
        {
            operation.GetAwaiter().GetResult();
        }

        var result = state.Result;
        if (result is null)
        {
            var message = (dialogException is not null || dialogResult < 0) && operation is null
                ? $"无法创建原生更新界面，安装尚未开始。请返回应用后重试。{FormatNativeError(dialogException)}"
                : "原生更新界面意外结束，请查看更新日志并确认程序状态。";
            ShowMessageBox(message, "我去图书馆 - 更新程序");
            return 1;
        }

        if (!result.Succeeded && result.ShouldShowMessage)
        {
            ShowFailureDialog(result.Message);
        }

        return result.Succeeded ? 0 : 1;
    }

    public static void ShowError(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        try
        {
            if (ShowSimpleErrorDialog(message) >= 0)
            {
                return;
            }
        }
        catch
        {
        }

        ShowMessageBox(message, "我去图书馆 - 更新程序");
    }

    private static unsafe int ShowProgressDialog(ProgressDialogState state)
    {
        const string windowTitle = "我去图书馆 - 正在更新";
        const string mainInstruction = "正在安装更新";
        const string initialContent = "正在准备更新…";
        const string buttonText = "正在更新，请稍候";

        fixed (char* windowTitlePointer = windowTitle)
        fixed (char* mainInstructionPointer = mainInstruction)
        fixed (char* initialContentPointer = initialContent)
        fixed (char* buttonTextPointer = buttonText)
        {
            var button = new TaskDialogButton
            {
                ButtonId = ProgressButtonId,
                ButtonText = (nint)buttonTextPointer
            };
            var stateHandle = GCHandle.Alloc(state);
            try
            {
                var config = new TaskDialogConfig
                {
                    Size = (uint)sizeof(TaskDialogConfig),
                    Flags = TaskDialogFlagShowMarqueeProgressBar | TaskDialogFlagSizeToContent,
                    WindowTitle = (nint)windowTitlePointer,
                    MainIcon = TaskDialogInformationIcon,
                    MainInstruction = (nint)mainInstructionPointer,
                    Content = (nint)initialContentPointer,
                    ButtonCount = 1,
                    Buttons = (nint)(&button),
                    DefaultButton = ProgressButtonId,
                    Callback = (nint)(delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nint, int>)
                        &ProgressDialogCallback,
                    CallbackData = GCHandle.ToIntPtr(stateHandle)
                };
                var selectedButton = 0;
                var selectedRadioButton = 0;
                var verificationChecked = 0;
                return NativeMethods.TaskDialogIndirect(
                    &config,
                    &selectedButton,
                    &selectedRadioButton,
                    &verificationChecked);
            }
            finally
            {
                stateHandle.Free();
            }
        }
    }

    private static unsafe int ShowSimpleErrorDialog(string message)
    {
        const string windowTitle = "我去图书馆 - 更新程序";
        const string mainInstruction = "无法启动自动更新";
        fixed (char* windowTitlePointer = windowTitle)
        fixed (char* mainInstructionPointer = mainInstruction)
        fixed (char* contentPointer = message)
        {
            var config = new TaskDialogConfig
            {
                Size = (uint)sizeof(TaskDialogConfig),
                Flags = TaskDialogFlagSizeToContent | TaskDialogFlagAllowDialogCancellation,
                CommonButtons = TaskDialogCommonButtonClose,
                WindowTitle = (nint)windowTitlePointer,
                MainIcon = TaskDialogErrorIcon,
                MainInstruction = (nint)mainInstructionPointer,
                Content = (nint)contentPointer
            };
            var selectedButton = 0;
            var selectedRadioButton = 0;
            var verificationChecked = 0;
            return NativeMethods.TaskDialogIndirect(
                &config,
                &selectedButton,
                &selectedRadioButton,
                &verificationChecked);
        }
    }

    private static unsafe void ShowFailureDialog(string message)
    {
        const string windowTitle = "我去图书馆 - 自动更新";
        const string mainInstruction = "自动更新未完成";
        const string githubButtonText = "前往 GitHub";
        const string closeButtonText = "关闭";

        fixed (char* windowTitlePointer = windowTitle)
        fixed (char* mainInstructionPointer = mainInstruction)
        fixed (char* contentPointer = message)
        fixed (char* githubButtonTextPointer = githubButtonText)
        fixed (char* closeButtonTextPointer = closeButtonText)
        {
            var buttons = stackalloc TaskDialogButton[2];
            buttons[0] = new TaskDialogButton
            {
                ButtonId = GitHubButtonId,
                ButtonText = (nint)githubButtonTextPointer
            };
            buttons[1] = new TaskDialogButton
            {
                ButtonId = CloseButtonId,
                ButtonText = (nint)closeButtonTextPointer
            };
            var state = new FailureDialogState(message);
            var stateHandle = GCHandle.Alloc(state);
            try
            {
                var config = new TaskDialogConfig
                {
                    Size = (uint)sizeof(TaskDialogConfig),
                    Flags = TaskDialogFlagSizeToContent | TaskDialogFlagAllowDialogCancellation,
                    WindowTitle = (nint)windowTitlePointer,
                    MainIcon = TaskDialogErrorIcon,
                    MainInstruction = (nint)mainInstructionPointer,
                    Content = (nint)contentPointer,
                    ButtonCount = 2,
                    Buttons = (nint)buttons,
                    DefaultButton = CloseButtonId,
                    Callback = (nint)(delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nint, int>)
                        &FailureDialogCallback,
                    CallbackData = GCHandle.ToIntPtr(stateHandle)
                };
                var selectedButton = 0;
                var selectedRadioButton = 0;
                var verificationChecked = 0;
                try
                {
                    var hResult = NativeMethods.TaskDialogIndirect(
                        &config,
                        &selectedButton,
                        &selectedRadioButton,
                        &verificationChecked);
                    if (hResult < 0)
                    {
                        ShowMessageBox(message, windowTitle);
                    }
                }
                catch
                {
                    ShowMessageBox(message, windowTitle);
                }
            }
            finally
            {
                stateHandle.Free();
            }
        }
    }

    private static void ShowMessageBox(string message, string title)
    {
        try
        {
            _ = NativeMethods.MessageBox(
                0,
                message,
                title,
                MessageBoxOk | MessageBoxIconError | MessageBoxSetForeground);
        }
        catch
        {
        }
    }

    private static string FormatNativeError(Exception? exception)
    {
        return exception is null || string.IsNullOrWhiteSpace(exception.Message)
            ? string.Empty
            : $"\n\n系统错误：{exception.Message}";
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int ProgressDialogCallback(
        nint windowHandle,
        uint notification,
        nuint wParam,
        nint lParam,
        nint referenceData)
    {
        try
        {
            var state = GCHandle.FromIntPtr(referenceData).Target as ProgressDialogState;
            if (state is null)
            {
                return notification == TaskDialogNotificationButtonClicked
                    ? PreventCloseHResult
                    : SucceededHResult;
            }

            switch (notification)
            {
                case TaskDialogNotificationCreated:
                    state.OnCreated(windowHandle);
                    break;
                case TaskDialogNotificationButtonClicked:
                    return state.IsCompleted ? SucceededHResult : PreventCloseHResult;
                case TaskDialogNotificationDestroyed:
                    state.OnDestroyed(windowHandle);
                    break;
            }

            return SucceededHResult;
        }
        catch
        {
            return notification == TaskDialogNotificationButtonClicked
                ? PreventCloseHResult
                : SucceededHResult;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int FailureDialogCallback(
        nint windowHandle,
        uint notification,
        nuint wParam,
        nint lParam,
        nint referenceData)
    {
        try
        {
            if (notification != TaskDialogNotificationButtonClicked ||
                (int)wParam != GitHubButtonId)
            {
                return SucceededHResult;
            }

            var state = GCHandle.FromIntPtr(referenceData).Target as FailureDialogState;
            state?.OpenReleasesPage(windowHandle);
            return PreventCloseHResult;
        }
        catch
        {
            return PreventCloseHResult;
        }
    }

    internal sealed class ProgressDialogState
    {
        private readonly Func<Action<string>, Task<CoordinatorResult>> _runCoordinator;
        private readonly ITaskDialogMessageSink _messageSink;
        private nint _windowHandle;
        private int _started;
        private int _completed;
        private Task? _operation;
        private CoordinatorResult? _result;

        public ProgressDialogState(string requestPath, bool externalWorker)
            : this(
                reportStatus => new CoordinatorRunner(requestPath, externalWorker, reportStatus)
                    .RunAsync(CancellationToken.None),
                Win32TaskDialogMessageSink.Instance)
        {
        }

        internal ProgressDialogState(
            Func<Action<string>, Task<CoordinatorResult>> runCoordinator,
            ITaskDialogMessageSink messageSink)
        {
            _runCoordinator = runCoordinator;
            _messageSink = messageSink;
        }

        public bool IsCompleted => Volatile.Read(ref _completed) != 0;

        public Task? Operation => Volatile.Read(ref _operation);

        public CoordinatorResult? Result => Volatile.Read(ref _result);

        public void OnCreated(nint windowHandle)
        {
            if (Interlocked.Exchange(ref _started, 1) != 0)
            {
                return;
            }

            Volatile.Write(ref _windowHandle, windowHandle);
            try
            {
                _messageSink.EnableButton(windowHandle, ProgressButtonId, enabled: false);
                _messageSink.StartMarquee(windowHandle, 25);
                Volatile.Write(ref _operation, Task.Run(RunAsync));
            }
            catch (Exception exception)
            {
                Volatile.Write(
                    ref _result,
                    new CoordinatorResult(
                        false,
                        $"无法初始化原生更新界面，安装尚未开始：{exception.Message}",
                        true));
                Interlocked.Exchange(ref _completed, 1);
                Volatile.Write(ref _operation, Task.CompletedTask);
                TryCloseDialog(windowHandle);
            }
        }

        public void OnDestroyed(nint windowHandle)
        {
            Interlocked.CompareExchange(ref _windowHandle, 0, windowHandle);
        }

        private async Task RunAsync()
        {
            CoordinatorResult result;
            try
            {
                result = await _runCoordinator(ReportStatus).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                result = new CoordinatorResult(
                    false,
                    $"自动更新失败：{exception.Message}",
                    true);
            }

            Volatile.Write(ref _result, result);
            Interlocked.Exchange(ref _completed, 1);
            var windowHandle = Volatile.Read(ref _windowHandle);
            if (windowHandle == 0)
            {
                return;
            }

            TryCloseDialog(windowHandle);
        }

        private void ReportStatus(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            var windowHandle = Volatile.Read(ref _windowHandle);
            if (windowHandle != 0 && !IsCompleted)
            {
                try
                {
                    _messageSink.SetContent(windowHandle, message);
                }
                catch
                {
                }
            }
        }

        private void TryCloseDialog(nint windowHandle)
        {
            try
            {
                _messageSink.EnableButton(windowHandle, ProgressButtonId, enabled: true);
            }
            catch
            {
            }

            try
            {
                _messageSink.ClickButton(windowHandle, ProgressButtonId);
            }
            catch
            {
            }
        }
    }

    internal interface ITaskDialogMessageSink
    {
        void EnableButton(nint windowHandle, int buttonId, bool enabled);

        void StartMarquee(nint windowHandle, int intervalMilliseconds);

        void SetContent(nint windowHandle, string message);

        void ClickButton(nint windowHandle, int buttonId);
    }

    private sealed class Win32TaskDialogMessageSink : ITaskDialogMessageSink
    {
        public static Win32TaskDialogMessageSink Instance { get; } = new();

        public void EnableButton(nint windowHandle, int buttonId, bool enabled)
        {
            NativeMethods.SendMessage(
                windowHandle,
                TaskDialogMessageEnableButton,
                (nuint)buttonId,
                enabled ? 1 : 0);
        }

        public void StartMarquee(nint windowHandle, int intervalMilliseconds)
        {
            NativeMethods.SendMessage(
                windowHandle,
                TaskDialogMessageSetProgressBarMarquee,
                1,
                intervalMilliseconds);
        }

        public unsafe void SetContent(nint windowHandle, string message)
        {
            fixed (char* messagePointer = message)
            {
                NativeMethods.SendMessage(
                    windowHandle,
                    TaskDialogMessageSetElementText,
                    TaskDialogElementContent,
                    (nint)messagePointer);
            }
        }

        public void ClickButton(nint windowHandle, int buttonId)
        {
            NativeMethods.SendMessage(
                windowHandle,
                TaskDialogMessageClickButton,
                (nuint)buttonId,
                0);
        }
    }

    internal sealed class FailureDialogState
    {
        private readonly string _message;
        private readonly Func<bool> _openReleasesPage;
        private readonly Action<nint, string> _setContent;

        public FailureDialogState(string message)
            : this(message, TryOpenReleasesPage, SetContent)
        {
        }

        internal FailureDialogState(
            string message,
            Func<bool> openReleasesPage,
            Action<nint, string> setContent)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(message);
            ArgumentNullException.ThrowIfNull(openReleasesPage);
            ArgumentNullException.ThrowIfNull(setContent);
            _message = message;
            _openReleasesPage = openReleasesPage;
            _setContent = setContent;
        }

        public void OpenReleasesPage(nint windowHandle)
        {
            if (_openReleasesPage())
            {
                return;
            }

            _setContent(
                windowHandle,
                $"{_message}\n\n无法打开浏览器，请手动访问：\n{ReleasesUrl}");
        }

        private static bool TryOpenReleasesPage()
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo(ReleasesUrl)
                {
                    UseShellExecute = true
                });
                return process is not null;
            }
            catch
            {
                return false;
            }
        }

        private static unsafe void SetContent(nint windowHandle, string message)
        {
            fixed (char* messagePointer = message)
            {
                NativeMethods.SendMessage(
                    windowHandle,
                    TaskDialogMessageSetElementText,
                    TaskDialogElementContent,
                    (nint)messagePointer);
            }
        }
    }
}
