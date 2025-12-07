using Avalonia.Controls;
using IGoLibrary.Mac.ViewModels;
using System;

namespace IGoLibrary.Mac.Views
{
    public partial class GrabSeatView : UserControl
    {
        public GrabSeatView()
        {
            InitializeComponent();

            if (Design.IsDesignMode)
                return;

            // 从依赖注入容器获取ViewModel
            if (App.ServiceProvider != null)
            {
                DataContext = App.ServiceProvider.GetService(typeof(GrabSeatViewModel));
            }

            // 在View加载完成后触发自动初始化
            this.Loaded += OnLoaded;
        }

        private async void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            // 只执行一次
            this.Loaded -= OnLoaded;

            if (DataContext is GrabSeatViewModel viewModel)
            {
                try
                {
                    await viewModel.AutoInitializeAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"自动初始化失败: {ex.Message}");
                }
            }
        }
    }
}
