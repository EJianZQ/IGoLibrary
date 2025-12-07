using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IGoLibrary.Core.Interfaces;
using IGoLibrary.Core.Services;

namespace IGoLibrary.Mac.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly INotificationService _notificationService;
        private readonly IStorageService _storageService;
        private readonly ISessionService _sessionService;
        private readonly IGetCookieService _getCookieService;
        private readonly IGetLibInfoService _getLibInfoService;

        public LoginViewModel LoginViewModel { get; }
        public GrabSeatViewModel GrabSeatViewModel { get; }
        public OccupySeatViewModel OccupySeatViewModel { get; }

        public MainViewModel(
            INotificationService notificationService,
            IStorageService storageService,
            ISessionService sessionService,
            IGetCookieService getCookieService,
            IGetLibInfoService getLibInfoService,
            IGetAllLibsSummaryService getAllLibsSummaryService,
            IReserveSeatService reserveSeatService,
            IPrereserveSeatService prereserveSeatService,
            IGetReserveInfoService getReserveInfoService,
            ICancelReserveService cancelReserveService)
        {
            _notificationService = notificationService;
            _storageService = storageService;
            _sessionService = sessionService;
            _getCookieService = getCookieService;
            _getLibInfoService = getLibInfoService;

            // 初始化子ViewModels
            LoginViewModel = new LoginViewModel(
                getCookieService,
                getLibInfoService,
                getAllLibsSummaryService,
                sessionService,
                storageService,
                notificationService);

            GrabSeatViewModel = new GrabSeatViewModel(
                getLibInfoService,
                reserveSeatService,
                prereserveSeatService,
                sessionService,
                notificationService,
                getAllLibsSummaryService);

            OccupySeatViewModel = new OccupySeatViewModel(
                getLibInfoService,
                reserveSeatService,
                sessionService,
                notificationService);

            // 默认显示登录页面
            CurrentPage = "Login";

            // 启动时自动加载Cookie
            _ = InitializeAsync();
        }

        /// <summary>
        /// 异步初始化，自动加载Cookie
        /// </summary>
        private async Task InitializeAsync()
        {
            await LoginViewModel.AutoLoadCookieOnStartupAsync();
        }

        [ObservableProperty]
        private string _title = "我去图书馆 - Mac 版";

        [ObservableProperty]
        private string _currentPage = "Login";

        public bool IsLoginPage => CurrentPage == "Login";
        public bool IsGrabSeatPage => CurrentPage == "GrabSeat";
        public bool IsOccupySeatPage => CurrentPage == "OccupySeat";
        public bool IsSettingsPage => CurrentPage == "Settings";

        [RelayCommand]
        private void NavigateTo(string page)
        {
            System.Diagnostics.Debug.WriteLine($"[MainViewModel] NavigateTo called with page: {page}");
            Console.WriteLine($"[MainViewModel] NavigateTo called with page: {page}");
            CurrentPage = page;
            OnPropertyChanged(nameof(IsLoginPage));
            OnPropertyChanged(nameof(IsGrabSeatPage));
            OnPropertyChanged(nameof(IsOccupySeatPage));
            OnPropertyChanged(nameof(IsSettingsPage));
            System.Diagnostics.Debug.WriteLine($"[MainViewModel] CurrentPage is now: {CurrentPage}");
            Console.WriteLine($"[MainViewModel] CurrentPage is now: {CurrentPage}");
        }
    }
}
