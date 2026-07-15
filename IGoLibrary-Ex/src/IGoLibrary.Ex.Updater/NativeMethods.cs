using System.Runtime.InteropServices;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace IGoLibrary.Ex.Updater;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct TaskDialogButton
{
    public int ButtonId;
    public nint ButtonText;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct TaskDialogConfig
{
    public uint Size;
    public nint ParentWindow;
    public nint Instance;
    public uint Flags;
    public uint CommonButtons;
    public nint WindowTitle;
    public nint MainIcon;
    public nint MainInstruction;
    public nint Content;
    public uint ButtonCount;
    public nint Buttons;
    public int DefaultButton;
    public uint RadioButtonCount;
    public nint RadioButtons;
    public int DefaultRadioButton;
    public nint VerificationText;
    public nint ExpandedInformation;
    public nint ExpandedControlText;
    public nint CollapsedControlText;
    public nint FooterIcon;
    public nint Footer;
    public nint Callback;
    public nint CallbackData;
    public uint Width;
}

internal static partial class NativeMethods
{
    [LibraryImport("comctl32.dll", EntryPoint = "TaskDialogIndirect")]
    internal static unsafe partial int TaskDialogIndirect(
        TaskDialogConfig* taskConfig,
        int* selectedButton,
        int* selectedRadioButton,
        int* verificationChecked);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    internal static partial nint SendMessage(
        nint windowHandle,
        uint message,
        nuint wParam,
        nint lParam);

    [LibraryImport(
        "user32.dll",
        EntryPoint = "MessageBoxW",
        StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int MessageBox(
        nint parentWindow,
        string text,
        string caption,
        uint type);
}
