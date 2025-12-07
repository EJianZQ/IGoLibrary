using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using IGoLibrary.Core.Interfaces;
using IGoLibrary.Core.Services;
using IGoLibrary.Mac.Services;
using IGoLibrary.Mac.ViewModels;
using IGoLibrary.Mac.Views;

namespace IGoLibrary.Mac
{
    public partial class App : Application
    {
        // ========================================
        // 🔧 模拟模式开关
        // ========================================
        // 设置为 true 启用模拟模式（用于测试 UI 逻辑）
        // 设置为 false 使用真实服务（连接真实服务器）
        private const bool IsSimulationMode = false;
        // ========================================

        public static ServiceProvider? ServiceProvider { get; private set; }

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            // 配置依赖注入容器
            var services = new ServiceCollection();

            // 根据模拟模式选择注入的服务
            if (IsSimulationMode)
            {
                // ========================================
                // 🎭 模拟模式 - 使用 Mock 服务
                // ========================================
                System.Console.WriteLine("========================================");
                System.Console.WriteLine("🎭 [模拟模式] 已启用");
                System.Console.WriteLine("========================================");
                System.Console.WriteLine("✓ 使用 MockGetLibInfoService（模拟座位信息）");
                System.Console.WriteLine("✓ 使用 MockPrereserveSeatService（模拟预约座位）");
                System.Console.WriteLine("✓ 其他服务使用真实实现");
                System.Console.WriteLine("========================================");

                // 注册模拟服务
                services.AddSingleton<IGetLibInfoService, MockGetLibInfoService>();
                services.AddSingleton<IPrereserveSeatService, MockPrereserveSeatService>();

                // 其他服务使用真实实现
                services.AddSingleton<IGetCookieService, GetCookieServiceImpl>();
                services.AddSingleton<IGetAllLibsSummaryService, GetAllLibsSummaryImpl>();
                services.AddSingleton<IReserveSeatService, ReserveSeatServiceImpl>();
                services.AddSingleton<IGetReserveInfoService, GetReserveInfoServiceImpl>();
                services.AddSingleton<ICancelReserveService, CancelReserveServiceImpl>();
            }
            else
            {
                // ========================================
                // 🌐 真实模式 - 使用真实服务
                // ========================================
                System.Console.WriteLine("========================================");
                System.Console.WriteLine("🌐 [真实模式] 已启用");
                System.Console.WriteLine("========================================");

                // 注册真实服务
                services.AddSingleton<IGetCookieService, GetCookieServiceImpl>();
                services.AddSingleton<IGetLibInfoService, GetLibInfoServiceImpl>();
                services.AddSingleton<IGetAllLibsSummaryService, GetAllLibsSummaryImpl>();
                services.AddSingleton<IReserveSeatService, ReserveSeatServiceImpl>();
                services.AddSingleton<IPrereserveSeatService, PrereserveSeatServiceImpl>();
                services.AddSingleton<IGetReserveInfoService, GetReserveInfoServiceImpl>();
                services.AddSingleton<ICancelReserveService, CancelReserveServiceImpl>();
            }

            // 注册 Mac 层的 UI 服务
            services.AddSingleton<INotificationService, NotificationService>();
            services.AddSingleton<IStorageService, FileStorageService>();
            services.AddSingleton<ISessionService, SessionService>();

            // 注册 ViewModels
            services.AddTransient<MainViewModel>();
            services.AddTransient<LoginViewModel>();
            services.AddTransient<GrabSeatViewModel>();
            services.AddTransient<OccupySeatViewModel>();
            services.AddTransient<LibraryInfoViewModel>();
            services.AddTransient<SettingsViewModel>();

            // 构建服务提供者
            ServiceProvider = services.BuildServiceProvider();

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow
                {
                    DataContext = ServiceProvider.GetRequiredService<MainViewModel>()
                };
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
