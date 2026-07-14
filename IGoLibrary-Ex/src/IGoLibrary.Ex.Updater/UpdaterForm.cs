using System.Diagnostics;

namespace IGoLibrary.Ex.Updater;

internal sealed class UpdaterForm : Form
{
    private readonly string _requestPath;
    private readonly bool _externalWorker;
    private readonly Label _statusLabel;
    private bool _canClose;

    public UpdaterForm(string requestPath, bool externalWorker)
    {
        _requestPath = requestPath;
        _externalWorker = externalWorker;
        Text = "我去图书馆 - 正在更新";
        Width = 460;
        Height = 170;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;

        _statusLabel = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 62,
            Padding = new Padding(22, 20, 22, 0),
            Text = "正在准备更新…",
            TextAlign = ContentAlignment.MiddleLeft
        };
        var progressBar = new ProgressBar
        {
            Dock = DockStyle.Top,
            Height = 18,
            Margin = new Padding(22),
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 25
        };
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(22, 0, 22, 18)
        };
        panel.Controls.Add(progressBar);
        Controls.Add(panel);
        Controls.Add(_statusLabel);

        Shown += OnShown;
        FormClosing += OnFormClosing;
    }

    public int ExitCode { get; private set; } = 1;

    private async void OnShown(object? sender, EventArgs eventArgs)
    {
        try
        {
            var runner = new CoordinatorRunner(_requestPath, _externalWorker, ReportStatus);
            var result = await runner.RunAsync(CancellationToken.None);
            ExitCode = result.Succeeded ? 0 : 1;
            if (!result.Succeeded && result.ShouldShowMessage)
            {
                ShowFailureDialog(result.Message);
            }
        }
        finally
        {
            _canClose = true;
            Close();
        }
    }

    private void ReportStatus(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => ReportStatus(message));
            return;
        }

        _statusLabel.Text = message;
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        if (!_canClose)
        {
            eventArgs.Cancel = true;
        }
    }

    private void ShowFailureDialog(string message)
    {
        using var dialog = new Form
        {
            Text = "我去图书馆 - 自动更新",
            Width = 500,
            Height = 210,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog
        };
        var messageLabel = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            Text = message,
            TextAlign = ContentAlignment.MiddleLeft
        };
        var closeButton = new Button
        {
            Text = "关闭",
            Width = 96,
            DialogResult = DialogResult.Cancel
        };
        var githubButton = new Button
        {
            Text = "前往 GitHub",
            Width = 120
        };
        githubButton.Click += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo(
                    "https://github.com/EJianZQ/IGoLibrary/releases")
                {
                    UseShellExecute = true
                });
            }
            catch
            {
            }
        };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 54,
            Padding = new Padding(0, 8, 14, 8),
            FlowDirection = FlowDirection.RightToLeft
        };
        buttons.Controls.Add(closeButton);
        buttons.Controls.Add(githubButton);
        dialog.Controls.Add(messageLabel);
        dialog.Controls.Add(buttons);
        dialog.CancelButton = closeButton;
        dialog.ShowDialog(this);
    }
}
