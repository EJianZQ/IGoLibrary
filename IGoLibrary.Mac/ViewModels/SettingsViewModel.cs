using CommunityToolkit.Mvvm.ComponentModel;

namespace IGoLibrary.Mac.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        public SettingsViewModel()
        {
            AppVersion = "1.0.0 Mac版";
        }

        [ObservableProperty]
        private string _appVersion = "1.0.0 Mac版";
    }
}
