using System;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace IGoLibrary.Mac.ViewModels
{
    public partial class LibraryInfoViewModel : ObservableObject
    {
        public LibraryInfoViewModel()
        {
        }

        [RelayCommand]
        private void OpenProjectPage()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://xn--e-5g8az75bbi3a.com/%E9%A1%B9%E7%9B%AE%E5%8F%91%E5%B8%83/14.html",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"无法打开项目页面: {ex.Message}");
            }
        }

        [RelayCommand]
        private void OpenGithub()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://github.com/EJianZQ/IGoLibrary",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"无法打开Github: {ex.Message}");
            }
        }

        [RelayCommand]
        private void CheckUpdate()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://github.com/EJianZQ/IGoLibrary/releases",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"无法打开更新页面: {ex.Message}");
            }
        }
    }
}
