using Avalonia.Controls;

namespace IGoLibrary.Ex.Desktop.Startup;

internal sealed partial class StartupNoticeWindow : Window
{
    public StartupNoticeWindow()
        : this(StartupNotice.DuplicateInstance)
    {
    }

    internal StartupNoticeWindow(StartupNotice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);

        InitializeComponent();
        Title = notice.Title;
        TitleText.Text = notice.Title;
        MessageText.Text = notice.Message;
        ConfirmButton.Click += (_, _) => Close();
    }
}
