using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;

namespace IGoLibrary.Ex.Desktop.ViewModels;

public partial class MainWindowWorkflowViewModel
{
    public string CurrentAppVersionText => UpdateLinks.CurrentAppVersionText;

    public const string ProjectGitHubUrl = UpdateLinksViewModel.ProjectGitHubUrl;

    public const string AuthorSponsorUrl = UpdateLinksViewModel.AuthorSponsorUrl;

    public const string ProjectAuthorName = UpdateLinksViewModel.ProjectAuthorName;

    public const string ProjectAuthorAvatarUrl = UpdateLinksViewModel.ProjectAuthorAvatarUrl;

    public bool HasProjectAuthorAvatar => UpdateLinks.HasProjectAuthorAvatar;

    public bool HasNoProjectAuthorAvatar => UpdateLinks.HasNoProjectAuthorAvatar;

    public bool IsCheckingForUpdates
    {
        get => UpdateLinks.IsCheckingForUpdates;
        set => UpdateLinks.IsCheckingForUpdates = value;
    }

    public IImage? ProjectAuthorAvatar
    {
        get => UpdateLinks.ProjectAuthorAvatar;
        set => UpdateLinks.ProjectAuthorAvatar = value;
    }

    public bool CanCheckForUpdates => UpdateLinks.CanCheckForUpdates;

    public string CheckForUpdatesButtonText => UpdateLinks.CheckForUpdatesButtonText;

    public IRelayCommand OpenProjectPageCommand => UpdateLinks.OpenProjectPageCommand;

    public IRelayCommand OpenReleasesPageCommand => UpdateLinks.OpenReleasesPageCommand;

    public IAsyncRelayCommand OpenProjectGitHubPageCommand => UpdateLinks.OpenProjectGitHubPageCommand;

    public IAsyncRelayCommand OpenAuthorSponsorPageCommand => UpdateLinks.OpenAuthorSponsorPageCommand;

    public IAsyncRelayCommand CheckForUpdatesCommand => UpdateLinks.CheckForUpdatesCommand;

    private void ConfigureUpdateLinksPropertyBridge(ViewModelPropertyBridge propertyBridge)
    {
        propertyBridge.Forward(
            UpdateLinks,
            nameof(UpdateLinksViewModel.IsCheckingForUpdates),
            nameof(IsCheckingForUpdates),
            nameof(CanCheckForUpdates),
            nameof(CheckForUpdatesButtonText));
        propertyBridge.Forward(
            UpdateLinks,
            nameof(UpdateLinksViewModel.ProjectAuthorAvatar),
            nameof(ProjectAuthorAvatar),
            nameof(HasProjectAuthorAvatar),
            nameof(HasNoProjectAuthorAvatar));
    }

    private Task RunStartupUpdateCheckAsync()
    {
        return UpdateLinks.RunStartupUpdateCheckAsync();
    }

    private Task LoadProjectAuthorAvatarAsync()
    {
        return UpdateLinks.LoadProjectAuthorAvatarAsync();
    }
}
