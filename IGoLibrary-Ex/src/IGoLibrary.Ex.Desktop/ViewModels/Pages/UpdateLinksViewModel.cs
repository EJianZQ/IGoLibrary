using System.Diagnostics;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Desktop.ViewModels;

public sealed partial class UpdateLinksViewModel(
    IActivityLogService activityLogService,
    INotificationService notificationService,
    IUpdateCheckService updateCheckService,
    IUpdateDialogService updateDialogService,
    IExternalLinkService externalLinkService,
    IAppVersionProvider appVersionProvider) : ViewModelBase
{
    private readonly SemaphoreSlim _updateCheckGate = new(1, 1);

    public string CurrentAppVersionText { get; } = $"v{appVersionProvider.CurrentVersionText}";

    public const string ProjectGitHubUrl = "https://github.com/EJianZQ/IGoLibrary";

    public const string AuthorSponsorUrl = "https://latiao.vip/%E9%A1%B9%E7%9B%AE%E5%8F%91%E5%B8%83/14.html";

    public const string ProjectAuthorName = "EJianZQ";

    public const string ProjectAuthorAvatarUrl = "https://avatars.githubusercontent.com/u/52780714";

    public bool HasProjectAuthorAvatar => ProjectAuthorAvatar is not null;

    public bool HasNoProjectAuthorAvatar => !HasProjectAuthorAvatar;

    [ObservableProperty]
    private bool isCheckingForUpdates;

    [ObservableProperty]
    private IImage? projectAuthorAvatar;

    partial void OnProjectAuthorAvatarChanged(IImage? value)
    {
        OnPropertyChanged(nameof(HasProjectAuthorAvatar));
        OnPropertyChanged(nameof(HasNoProjectAuthorAvatar));
    }

    public bool CanCheckForUpdates => !IsCheckingForUpdates;

    public string CheckForUpdatesButtonText => IsCheckingForUpdates ? "正在检查..." : "立即检查更新";

    partial void OnIsCheckingForUpdatesChanged(bool value)
    {
        OnPropertyChanged(nameof(CanCheckForUpdates));
        OnPropertyChanged(nameof(CheckForUpdatesButtonText));
    }

    [RelayCommand]
    private void OpenProjectPage()
    {
        Process.Start(new ProcessStartInfo("https://xn--e-5g8az75bbi3a.com/%E9%A1%B9%E7%9B%AE%E5%8F%91%E5%B8%83/14.html")
        {
            UseShellExecute = true
        });
    }

    [RelayCommand]
    private void OpenReleasesPage()
    {
        Process.Start(new ProcessStartInfo("https://github.com/EJianZQ/IGoLibrary/releases")
        {
            UseShellExecute = true
        });
    }

    [RelayCommand]
    private async Task OpenProjectGitHubPageAsync()
    {
        try
        {
            await externalLinkService.OpenAsync(new Uri(ProjectGitHubUrl));
        }
        catch (Exception ex)
        {
            activityLogService.Write(LogEntryKind.Warning, "About", $"打开项目 GitHub 地址失败：{ex.Message}");
            await notificationService.ShowWarningAsync("打开 GitHub 失败", ex.Message);
        }
    }

    [RelayCommand]
    private async Task OpenAuthorSponsorPageAsync()
    {
        try
        {
            await externalLinkService.OpenAsync(new Uri(AuthorSponsorUrl));
            await notificationService.ShowInfoAsync(
                "赞赏作者提示",
                "已打开作者项目博客\n滚动到页面底部点击打赏按钮");
        }
        catch (Exception ex)
        {
            activityLogService.Write(LogEntryKind.Warning, "About", $"打开作者赞赏页面失败：{ex.Message}");
            await notificationService.ShowWarningAsync("打开赞赏页面失败", ex.Message);
        }
    }

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        await RunUpdateCheckAsync(UpdateCheckMode.Manual, notifyWhenNoUpdate: true);
    }

    public async Task RunStartupUpdateCheckAsync()
    {
        await RunUpdateCheckAsync(UpdateCheckMode.Automatic, notifyWhenNoUpdate: false);
    }

    private async Task RunUpdateCheckAsync(
        UpdateCheckMode mode,
        bool notifyWhenNoUpdate)
    {
        if (IsCheckingForUpdates || !(await _updateCheckGate.WaitAsync(0)))
        {
            return;
        }

        IsCheckingForUpdates = true;
        try
        {
            var result = await updateCheckService.CheckAsync(mode);
            await HandleUpdateCheckResultAsync(result, notifyWhenNoUpdate);
        }
        catch (Exception ex)
        {
            activityLogService.Write(LogEntryKind.Warning, "Update", $"检查更新失败：{ex.Message}");
            if (notifyWhenNoUpdate)
            {
                await notificationService.ShowWarningAsync("检查更新失败", ex.Message);
            }
        }
        finally
        {
            IsCheckingForUpdates = false;
            _updateCheckGate.Release();
        }
    }

    private async Task HandleUpdateCheckResultAsync(
        UpdateCheckResult result,
        bool notifyWhenNoUpdate)
    {
        if (result.HasUpdate && result.Release is { } release)
        {
            activityLogService.Write(LogEntryKind.Info, "Update", $"发现新版本：{release.TagName}");
            var dialogResult = await updateDialogService.ShowUpdateAsync(release);
            if (dialogResult == UpdateDialogResult.OpenReleasePage)
            {
                await OpenUpdateReleasePageAsync(release.HtmlUrl);
            }
            else if (dialogResult == UpdateDialogResult.SkipVersion)
            {
                await updateCheckService.SkipVersionAsync(release.Version);
                await notificationService.ShowSuccessAsync(
                    "已跳过此版本",
                    $"{release.TagName} 将不再提示");
            }

            return;
        }

        if (result.Status == UpdateCheckStatus.Failed)
        {
            activityLogService.Write(LogEntryKind.Warning, "Update", result.Message);
            if (notifyWhenNoUpdate)
            {
                await notificationService.ShowWarningAsync("检查更新失败", result.Message);
            }

            return;
        }

        if (notifyWhenNoUpdate)
        {
            await notificationService.ShowSuccessAsync("检查更新完成", result.Message);
        }
    }

    private async Task OpenUpdateReleasePageAsync(Uri releaseUrl)
    {
        try
        {
            await externalLinkService.OpenAsync(releaseUrl);
        }
        catch (Exception ex)
        {
            activityLogService.Write(LogEntryKind.Warning, "Update", $"打开 Release 页面失败：{ex.Message}");
            await notificationService.ShowWarningAsync("打开 Release 页面失败", ex.Message);
        }
    }

    public async Task LoadProjectAuthorAvatarAsync()
    {
        if (ProjectAuthorAvatar is not null)
        {
            return;
        }

        try
        {
            using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "IGoLibrary-Ex");

            var bytes = await httpClient.GetByteArrayAsync(ProjectAuthorAvatarUrl, cancellationTokenSource.Token);
            using var stream = new MemoryStream(bytes);
            var avatar = new Bitmap(stream);

            if (Dispatcher.UIThread.CheckAccess())
            {
                ProjectAuthorAvatar = avatar;
                return;
            }

            Dispatcher.UIThread.Post(() => ProjectAuthorAvatar = avatar);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            activityLogService.Write(LogEntryKind.Warning, "About", $"加载作者头像失败：{ex.Message}");
        }
    }
}
