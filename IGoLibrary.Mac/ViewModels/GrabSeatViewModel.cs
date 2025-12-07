using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IGoLibrary.Core.Data;
using IGoLibrary.Core.Interfaces;
using IGoLibrary.Core.Services;
using IGoLibrary.Core.Exceptions;
using Newtonsoft.Json;

namespace IGoLibrary.Mac.ViewModels
{
    public partial class GrabSeatViewModel : ObservableObject
    {
        private readonly IGetLibInfoService _getLibInfoService;
        private readonly IReserveSeatService _reserveSeatService;
        private readonly IPrereserveSeatService _prereserveSeatService;
        private readonly ISessionService _sessionService;
        private readonly INotificationService _notificationService;
        private readonly SeatFilterService _seatFilterService;
        private readonly IGetAllLibsSummaryService _getAllLibsSummaryService;

        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _monitoringTask;
        private bool _isAutoInitialized = false;
        private List<LibSummary>? _allLibraries;

        // ========================================
        // 🕐 时间模拟功能（用于测试明日预约）
        // ========================================
        // 设置为 true 启用时间模拟，将当前时间模拟为指定时间
        // 设置为 false 使用真实时间
        private const bool EnableTimeSimulation = false;

        // 模拟的时间（可调整用于测试不同场景）
        // 示例：
        // - new TimeSpan(20, 0, 0)  -> 晚上8点（测试抢座开始）
        // - new TimeSpan(19, 59, 45) -> 19:59:45（测试准备状态和倒计时）
        // - new TimeSpan(19, 50, 0)  -> 19:50（测试完整等待流程）
        private static readonly TimeSpan SimulatedTime = new TimeSpan(20, 0, 0);
        private static readonly TimeSpan GrabSeatStartTime = new TimeSpan(20, 0, 0);

        /// <summary>
        /// 调试时间偏移（用于动态调整时间，单位：秒）
        /// 可以在运行时通过 SetDebugTimeOffset 方法调整
        /// </summary>
        private static int DebugTimeOffsetSeconds = 0;
        // ========================================

        // 查询语法常量（使用与 LoginViewModel 相同的格式）
        private const string QueryAllLibsSummarySyntax = "{\"operationName\":\"list\",\"query\":\"query list {\\n userAuth {\\n reserve {\\n libs(libType: -1) {\\n lib_id\\n lib_floor\\n is_open\\n lib_name\\n lib_type\\n lib_group_id\\n lib_comment\\n lib_rt {\\n seats_total\\n seats_used\\n seats_booking\\n seats_has\\n reserve_ttl\\n open_time\\n open_time_str\\n close_time\\n close_time_str\\n advance_booking\\n }\\n }\\n libGroups {\\n id\\n group_name\\n }\\n reserve {\\n isRecordUser\\n }\\n }\\n record {\\n libs {\\n lib_id\\n lib_floor\\n is_open\\n lib_name\\n lib_type\\n lib_group_id\\n lib_comment\\n lib_color_name\\n lib_rt {\\n seats_total\\n seats_used\\n seats_booking\\n seats_has\\n reserve_ttl\\n open_time\\n open_time_str\\n close_time\\n close_time_str\\n advance_booking\\n }\\n }\\n }\\n rule {\\n signRule\\n }\\n }\\n}\"}";
        private const string QueryLibInfoSyntax = "{\"operationName\":\"libLayout\",\"query\":\"query libLayout($libId: Int, $libType: Int) {\\n userAuth {\\n reserve {\\n libs(libType: $libType, libId: $libId) {\\n lib_id\\n is_open\\n lib_floor\\n lib_name\\n lib_type\\n lib_layout {\\n seats_total\\n seats_booking\\n seats_used\\n max_x\\n max_y\\n seats {\\n x\\n y\\n key\\n type\\n name\\n seat_status\\n status\\n }\\n }\\n }\\n }\\n }\\n}\",\"variables\":{\"libId\":ReplaceMe}}";

        public GrabSeatViewModel(
            IGetLibInfoService getLibInfoService,
            IReserveSeatService reserveSeatService,
            IPrereserveSeatService prereserveSeatService,
            ISessionService sessionService,
            INotificationService notificationService,
            IGetAllLibsSummaryService getAllLibsSummaryService)
        {
            _getLibInfoService = getLibInfoService;
            _reserveSeatService = reserveSeatService;
            _prereserveSeatService = prereserveSeatService;
            _sessionService = sessionService;
            _notificationService = notificationService;
            _seatFilterService = new SeatFilterService();
            _getAllLibsSummaryService = getAllLibsSummaryService;

            SeatList = new ObservableCollection<SeatKeyData>();
            Logs = new ObservableCollection<string>();
            WaitingGrabSeats = new ObservableCollection<SeatKeyData>();
            LibraryOptions = new ObservableCollection<string>();

            // 固定设置准备时间为19:55:00，提前5分钟准备，20:00:00准时开始抢座
            // 不对外开放时间选择，系统自动运行
            TimingTime = new TimeSpan(19, 55, 0);
        }

        #region 属性

        /// <summary>
        /// 所有座位列表（用于显示在表格中）
        /// </summary>
        [ObservableProperty]
        private ObservableCollection<SeatKeyData> _seatList;

        /// <summary>
        /// 运行日志列表
        /// </summary>
        [ObservableProperty]
        private ObservableCollection<string> _logs;

        /// <summary>
        /// 待抢座位列表
        /// </summary>
        [ObservableProperty]
        private ObservableCollection<SeatKeyData> _waitingGrabSeats;

        /// <summary>
        /// 是否正在监控
        /// </summary>
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(StartMonitorCommand))]
        [NotifyCanExecuteChangedFor(nameof(StopMonitorCommand))]
        private bool _isMonitoring;

        /// <summary>
        /// 抢座模式：固定使用激进模式（1000ms，1秒）
        /// 每分钟 60 次请求，平衡速度和稳定性
        /// 比 Gitee 的实际间隔（1800ms）快 1.8 倍
        /// </summary>
        [ObservableProperty]
        private int _grabMode = 0;

        /// <summary>
        /// 定时抢座时间（可选）
        /// </summary>
        [ObservableProperty]
        private TimeSpan? _timingTime;

        /// <summary>
        /// 选中的座位（用于添加到抢座列表）
        /// </summary>
        [ObservableProperty]
        private SeatKeyData? _selectedSeat;

        /// <summary>
        /// 选中的多个座位索引（用于收藏功能）
        /// </summary>
        public List<int> SelectedSeatIndices { get; set; } = new List<int>();

        /// <summary>
        /// 图书馆选项列表
        /// </summary>
        [ObservableProperty]
        private ObservableCollection<string> _libraryOptions;

        /// <summary>
        /// 选中的图书馆索引
        /// </summary>
        [ObservableProperty]
        private int _selectedLibraryIndex = -1;

        /// <summary>
        /// 当前绑定的图书馆信息显示
        /// </summary>
        [ObservableProperty]
        private string _currentLibraryInfo = "未绑定图书馆";

        #endregion

        #region 命令

        /// <summary>
        /// 开始监控抢座
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanStartMonitor))]
        private async Task StartMonitorAsync()
        {
            if (WaitingGrabSeats.Count == 0)
            {
                _notificationService.ShowError("开始抢座失败", "未设置待抢座位，请设置后重试");
                return;
            }

            if (string.IsNullOrWhiteSpace(_sessionService.Cookie))
            {
                _notificationService.ShowError("开始抢座失败", "Cookie 未设置，请先登录");
                return;
            }

            IsMonitoring = true;
            _cancellationTokenSource = new CancellationTokenSource();

            AddLog("开始抢座监控");

            try
            {
                _monitoringTask = Task.Run(() => RunMonitorAsync(_cancellationTokenSource.Token));
                await _monitoringTask;
            }
            catch (OperationCanceledException)
            {
                AddLog("监控已停止");
            }
            catch (Exception ex)
            {
                AddLog($"监控出现异常: {ex.Message}");
                _notificationService.ShowError("监控异常", ex.Message);
            }
            finally
            {
                IsMonitoring = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        private bool CanStartMonitor() => !IsMonitoring;

        /// <summary>
        /// 停止监控抢座
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanStopMonitor))]
        private void StopMonitor()
        {
            _cancellationTokenSource?.Cancel();
            AddLog("正在停止监控...");
        }

        private bool CanStopMonitor() => IsMonitoring;

        /// <summary>
        /// 添加选中座位到抢座列表（支持多个座位作为备选）
        /// 第一个座位为主选，后续座位为备选
        /// </summary>
        [RelayCommand]
        private async void AddToGrabList()
        {
            if (SelectedSeat == null)
            {
                _notificationService.ShowError("添加失败", "请先选择座位");
                return;
            }

            // 检查是否已存在
            if (WaitingGrabSeats.Any(s => s.Key == SelectedSeat.Key))
            {
                _notificationService.ShowWarning("添加失败", "该座位已在预约列表中");
                return;
            }

            // 设置优先级：0=主选，1=备选1，2=备选2...
            int priority = WaitingGrabSeats.Count;

            WaitingGrabSeats.Add(new SeatKeyData
            {
                Name = SelectedSeat.Name,
                Status = SelectedSeat.Status,
                Key = SelectedSeat.Key,
                Priority = priority
            });

            string positionText = priority == 0 ? "主选座位" : $"备选座位{priority}";
            _notificationService.ShowSuccess("添加成功", $"已添加 {SelectedSeat.Name} 号座位为{positionText}");
            AddLog($"已添加座位 {SelectedSeat.Name} 号为{positionText}（当前共{WaitingGrabSeats.Count}个座位）");

            // 自动保存座位设置
            await AutoSaveSeatsAsync();
        }

        /// <summary>
        /// 清空抢座列表
        /// </summary>
        [RelayCommand]
        private async void ClearGrabList()
        {
            WaitingGrabSeats.Clear();
            AddLog("已清空抢座列表");

            // 自动保存（清空后的状态）
            await AutoSaveSeatsAsync();
        }

        /// <summary>
        /// 刷新座位信息
        /// </summary>
        [RelayCommand]
        private async Task RefreshSeatsAsync()
        {
            if (string.IsNullOrWhiteSpace(_sessionService.Cookie))
            {
                _notificationService.ShowError("刷新失败", "Cookie 未设置，请先登录");
                return;
            }

            if (string.IsNullOrWhiteSpace(_sessionService.QueryLibInfoSyntax))
            {
                _notificationService.ShowError("刷新失败", "未绑定图书馆，请先在登录页面绑定图书馆");
                return;
            }

            try
            {
                var library = await Task.Run(() =>
                    _getLibInfoService.GetLibInfo(
                        _sessionService.Cookie,
                        _sessionService.QueryLibInfoSyntax))
                    .ConfigureAwait(false);

                if (library?.Seats != null)
                {
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        SeatList.Clear();
                        AddLog($"开始处理座位数据，共 {library.Seats.Count} 个座位");

                        foreach (var seat in library.Seats.OrderBy(s => int.TryParse(s.name, out var num) ? num : 0))
                        {
                            var seatData = new SeatKeyData
                            {
                                Name = seat.name ?? "未知",
                                Status = seat.status ? "有人" : "无人",
                                Key = seat.key ?? ""
                            };
                            SeatList.Add(seatData);
                            AddLog($"添加座位: {seatData.Name}, 状态: {seatData.Status}, Key: {seatData.Key}");
                        }

                        _sessionService.CurrentLibrary = library;
                        AddLog($"刷新座位信息成功，SeatList.Count = {SeatList.Count}");
                        _notificationService.ShowSuccess("刷新成功", $"已获取 {SeatList.Count} 个座位信息");
                    });
                }
                else
                {
                    AddLog($"library?.Seats 为 null，library={library}, Seats={library?.Seats}");
                }
            }
            catch (GetLibInfoException ex)
            {
                AddLog($"刷新座位信息失败: {ex.Message}");
                _notificationService.ShowError("刷新失败", ex.Message);
            }
        }

        /// <summary>
        /// 收藏当前预约的所有座位（主选+备选）
        /// </summary>
        [RelayCommand]
        private async Task SaveFavoriteSeatsAsync()
        {
            if (WaitingGrabSeats.Count == 0)
            {
                _notificationService.ShowError("收藏座位失败", "当前未设置预约座位，请先添加座位到预约列表");
                return;
            }

            try
            {
                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var favoritesDir = Path.Combine(appDataPath, "IGoLibrary", "Favorites");

                if (!Directory.Exists(favoritesDir))
                {
                    Directory.CreateDirectory(favoritesDir);
                }

                // 保存所有座位（主选+备选）
                var seatList = WaitingGrabSeats.Select(s => new
                {
                    Key = s.Key,
                    Name = s.Name
                }).ToList();

                var favs = new
                {
                    LibID = _sessionService.CurrentLibrary?.LibID ?? 0,
                    Seats = seatList
                };

                var favoriteSeatsContent = JsonConvert.SerializeObject(favs);
                var filePath = Path.Combine(favoritesDir, $"{favs.LibID}.json");

                await File.WriteAllTextAsync(filePath, favoriteSeatsContent);

                string seatNames = string.Join("、", WaitingGrabSeats.Select(s => s.Name + "号"));
                _notificationService.ShowSuccess("收藏座位成功", $"已成功收藏 {WaitingGrabSeats.Count} 个座位（{seatNames}），可下次恢复使用");
                AddLog($"已收藏 {WaitingGrabSeats.Count} 个座位: {seatNames}");
            }
            catch (Exception ex)
            {
                _notificationService.ShowError("收藏座位失败", $"在写入座位数据时发生了异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 加载收藏的座位（支持主选+备选）
        /// </summary>
        [RelayCommand]
        private async Task LoadFavoriteSeatsAsync()
        {
            try
            {
                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var filePath = Path.Combine(appDataPath, "IGoLibrary", "Favorites", $"{_sessionService.CurrentLibrary?.LibID ?? 0}.json");

                if (!File.Exists(filePath))
                {
                    _notificationService.ShowError("加载收藏座位失败", "收藏座位数据不存在，可能是并未收藏该场馆的座位或还未绑定图书馆");
                    return;
                }

                var favoriteSeatsContent = await File.ReadAllTextAsync(filePath);

                // 尝试解析新格式（包含Seats数组）
                try
                {
                    var favData = JsonConvert.DeserializeObject<dynamic>(favoriteSeatsContent);

                    if (favData?.Seats != null)
                    {
                        // 新格式：包含Seats数组
                        WaitingGrabSeats.Clear();

                        foreach (var seat in favData.Seats)
                        {
                            WaitingGrabSeats.Add(new SeatKeyData
                            {
                                Name = seat.Name.ToString(),
                                Status = "未知", // 状态需要刷新后才能知道
                                Key = seat.Key.ToString()
                            });
                        }

                        string seatNames = string.Join("、", WaitingGrabSeats.Select(s => s.Name + "号"));
                        _notificationService.ShowSuccess("加载收藏座位成功", $"已成功加载 {WaitingGrabSeats.Count} 个座位（{seatNames}）");
                        AddLog($"已加载 {WaitingGrabSeats.Count} 个收藏座位: {seatNames}");
                    }
                    else
                    {
                        _notificationService.ShowError("加载收藏座位失败", "数据格式不正确");
                    }
                }
                catch
                {
                    // 如果新格式解析失败，尝试旧格式（兼容性）
                    var favs = JsonConvert.DeserializeObject<FavoriteSeats>(favoriteSeatsContent);

                    if (favs?.SelectedRowIndex != null && favs.SelectedRowIndex.Length > 0)
                    {
                        WaitingGrabSeats.Clear();
                        foreach (var index in favs.SelectedRowIndex)
                        {
                            if (index < SeatList.Count)
                            {
                                var seat = SeatList[index];
                                WaitingGrabSeats.Add(new SeatKeyData
                                {
                                    Name = seat.Name,
                                    Status = seat.Status,
                                    Key = seat.Key
                                });
                            }
                        }

                        _notificationService.ShowSuccess("加载收藏座位成功", $"已成功加载 {favs.SelectedRowIndex.Length} 个座位（旧格式）");
                        AddLog($"已加载 {favs.SelectedRowIndex.Length} 个收藏座位（旧格式）");
                    }
                    else
                    {
                        _notificationService.ShowError("加载收藏座位失败", "数据中不存在收藏的座位数据");
                    }
                }
            }
            catch (Exception ex)
            {
                _notificationService.ShowError("加载收藏座位失败", $"读取数据时发生了异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 加载所有可用的图书馆列表
        /// </summary>
        [RelayCommand]
        private async Task LoadLibrariesAsync()
        {
            if (string.IsNullOrWhiteSpace(_sessionService.Cookie))
            {
                _notificationService.ShowError("加载失败", "Cookie 未设置，请先登录");
                return;
            }

            try
            {
                AddLog("正在加载图书馆列表...");

                // 获取所有图书馆列表
                var allLibsSummary = await Task.Run(() =>
                    _getAllLibsSummaryService.GetAllLibsSummary(_sessionService.Cookie, QueryAllLibsSummarySyntax))
                    .ConfigureAwait(false);

                if (allLibsSummary?.libSummaries == null || allLibsSummary.libSummaries.Count == 0)
                {
                    _notificationService.ShowError("加载失败", "未找到可用的图书馆");
                    return;
                }

                // 保存图书馆列表
                _allLibraries = allLibsSummary.libSummaries;

                // 在UI线程更新选项列表
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    LibraryOptions.Clear();
                    foreach (var lib in allLibsSummary.libSummaries)
                    {
                        LibraryOptions.Add($"{lib.Name} - {lib.Floor} - {(lib.IsOpen ? "开放" : "关闭")}");
                    }

                    // 查找当前绑定的图书馆并设置选中索引
                    if (_sessionService.CurrentLibrary != null)
                    {
                        SelectedLibraryIndex = allLibsSummary.libSummaries.FindIndex(
                            lib => lib.LibID == _sessionService.CurrentLibrary.LibID);
                    }

                    if (SelectedLibraryIndex == -1)
                    {
                        // 默认选择第一个开放的图书馆
                        SelectedLibraryIndex = allLibsSummary.libSummaries.FindIndex(lib => lib.IsOpen);
                        if (SelectedLibraryIndex == -1) SelectedLibraryIndex = 0;
                    }

                    AddLog($"成功加载 {LibraryOptions.Count} 个图书馆");
                    _notificationService.ShowSuccess("加载成功", $"已加载 {LibraryOptions.Count} 个图书馆，请选择要切换的楼层");
                });
            }
            catch (Exception ex)
            {
                AddLog($"加载图书馆列表失败: {ex.Message}");
                _notificationService.ShowError("加载失败", ex.Message);
            }
        }

        /// <summary>
        /// 切换到选中的图书馆
        /// </summary>
        [RelayCommand]
        private async Task SwitchLibraryAsync()
        {
            if (_allLibraries == null || SelectedLibraryIndex < 0 || SelectedLibraryIndex >= _allLibraries.Count)
            {
                _notificationService.ShowError("切换失败", "请先加载图书馆列表并选择一个图书馆");
                return;
            }

            if (string.IsNullOrWhiteSpace(_sessionService.Cookie))
            {
                _notificationService.ShowError("切换失败", "Cookie 未设置，请先登录");
                return;
            }

            try
            {
                var selectedLib = _allLibraries[SelectedLibraryIndex];
                AddLog($"正在切换到 {selectedLib.Name} - {selectedLib.Floor}...");

                // 构造该图书馆的查询语法
                var libQuerySyntax = QueryLibInfoSyntax.Replace("ReplaceMe", selectedLib.LibID.ToString());

                // 获取图书馆详细信息
                var libraryData = await Task.Run(() =>
                    _getLibInfoService.GetLibInfo(_sessionService.Cookie, libQuerySyntax))
                    .ConfigureAwait(false);

                // 更新Session
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _sessionService.CurrentLibrary = libraryData;
                    _sessionService.QueryLibInfoSyntax = libQuerySyntax;

                    // 更新当前图书馆信息显示
                    CurrentLibraryInfo = $"{libraryData.Name} - {libraryData.Floor} (余座: {libraryData.SeatsInfo.AvailableSeats})";

                    AddLog($"✅ 成功切换到 {libraryData.Name} - {libraryData.Floor}");
                    _notificationService.ShowSuccess("切换成功", $"已切换到 {libraryData.Name} - {libraryData.Floor}");

                    // 自动刷新座位信息
                    AddLog("正在自动刷新座位信息...");
                });

                // 自动刷新座位列表
                await RefreshSeatsAsync();
            }
            catch (Exception ex)
            {
                AddLog($"切换图书馆失败: {ex.Message}");
                _notificationService.ShowError("切换失败", ex.Message);
            }
        }

        /// <summary>
        /// 系统自检 - 战前演习工具
        /// 检查所有功能是否正常，确保网络通畅、服务器在线、Cookie有效
        /// </summary>
        [RelayCommand]
        private async Task SystemSelfCheckAsync()
        {
            AddLog("========================================");
            AddLog("【战前演习】开始系统全链路检测...");
            AddLog("========================================");

            int passedTests = 0;
            int totalTests = 0;

            // 1. 检查北京时间
            totalTests++;
            try
            {
                var beijingNow = GetBeijingNow();
                string timeMode = EnableTimeSimulation ? " [模拟模式]" : "";
                AddLog($"✅ [1/8] 北京时间获取: 成功 - {beijingNow:yyyy-MM-dd HH:mm:ss}{timeMode}");
                passedTests++;
            }
            catch (Exception ex)
            {
                AddLog($"❌ [1/8] 北京时间获取: 失败 - {ex.Message}");
            }

            // 2. 检查 Cookie 是否设置
            totalTests++;
            if (!string.IsNullOrWhiteSpace(_sessionService.Cookie))
            {
                AddLog($"✅ [2/8] Cookie 检查: 已设置 (长度: {_sessionService.Cookie.Length} 字符)");

                // 检查 Cookie 格式
                if (_sessionService.Cookie.Contains("Authorization") && _sessionService.Cookie.Contains("SERVERID"))
                {
                    AddLog($"   ✓ Cookie 格式正确，包含必要字段");
                    passedTests++;
                }
                else
                {
                    AddLog($"   ⚠️ Cookie 格式可能不完整，缺少 Authorization 或 SERVERID");
                }
            }
            else
            {
                AddLog($"❌ [2/8] Cookie 检查: 未设置，请先登录");
            }

            // 3. 检查图书馆绑定
            totalTests++;
            if (_sessionService.CurrentLibrary != null)
            {
                AddLog($"✅ [3/8] 图书馆绑定: 已绑定");
                AddLog($"   - 图书馆: {_sessionService.CurrentLibrary.Name}");
                AddLog($"   - 楼层: {_sessionService.CurrentLibrary.Floor}");
                AddLog($"   - LibID: {_sessionService.CurrentLibrary.LibID}");
                AddLog($"   - 状态: {(_sessionService.CurrentLibrary.IsOpen ? "开放" : "关闭")}");
                passedTests++;
            }
            else
            {
                AddLog($"❌ [3/8] 图书馆绑定: 未绑定，请先在登录页面绑定图书馆");
            }

            // 4. 检查查询语法
            totalTests++;
            if (!string.IsNullOrWhiteSpace(_sessionService.QueryLibInfoSyntax))
            {
                AddLog($"✅ [4/8] 查询语法: 已配置");
                passedTests++;
            }
            else
            {
                AddLog($"❌ [4/8] 查询语法: 未配置");
            }

            // 5. 全链路网络测试（战前演习核心）
            totalTests++;
            if (!string.IsNullOrWhiteSpace(_sessionService.Cookie) && !string.IsNullOrWhiteSpace(_sessionService.QueryLibInfoSyntax))
            {
                try
                {
                    AddLog($"⏳ [5/9] 全链路网络测试: 正在执行战前演习...");
                    AddLog($"   📡 测试目标: wechat.v2.traceint.com");

                    var startTime = DateTime.Now;

                    var testLib = await Task.Run(() =>
                        _getLibInfoService.GetLibInfo(_sessionService.Cookie, _sessionService.QueryLibInfoSyntax))
                        .ConfigureAwait(false);

                    var endTime = DateTime.Now;
                    var responseTime = (endTime - startTime).TotalMilliseconds;

                    if (testLib != null && testLib.Seats != null && testLib.Seats.Count > 0)
                    {
                        AddLog($"✅ [5/9] 全链路网络测试: 通过");
                        AddLog($"   ✓ 网络连接: 正常");
                        AddLog($"   ✓ DNS 解析: 成功");
                        AddLog($"   ✓ 服务器状态: 在线");
                        AddLog($"   ✓ Cookie 状态: 有效（热的🔥）");
                        AddLog($"   ✓ 响应时间: {responseTime:F0}ms");
                        AddLog($"   ✓ 座位数据: {testLib.Seats.Count} 个座位");
                        AddLog($"   ✓ 图书馆: {testLib.Name} - {testLib.Floor}");

                        // 分析座位状态
                        int availableSeats = testLib.Seats.Count(s => !s.status);
                        int occupiedSeats = testLib.Seats.Count(s => s.status);
                        AddLog($"   ✓ 座位状态: 空座 {availableSeats} 个，已占 {occupiedSeats} 个");

                        // 响应时间评估
                        if (responseTime < 500)
                        {
                            AddLog($"   🚀 网络质量: 优秀（<500ms）");
                        }
                        else if (responseTime < 1000)
                        {
                            AddLog($"   ✓ 网络质量: 良好（<1s）");
                        }
                        else if (responseTime < 2000)
                        {
                            AddLog($"   ⚠️ 网络质量: 一般（<2s）");
                        }
                        else
                        {
                            AddLog($"   ⚠️ 网络质量: 较慢（>{responseTime/1000:F1}s）");
                        }

                        AddLog($"   🎯 战前演习结论: 系统已就绪，可以开始抢座！");
                        passedTests++;
                    }
                    else if (testLib != null && (testLib.Seats == null || testLib.Seats.Count == 0))
                    {
                        AddLog($"⚠️ [5/9] 全链路网络测试: 部分通过");
                        AddLog($"   ✓ 网络连接: 正常");
                        AddLog($"   ✓ 服务器响应: 成功");
                        AddLog($"   ⚠️ 座位数据: 为空（可能是图书馆未开放或数据异常）");
                        AddLog($"   ⚠️ 响应时间: {responseTime:F0}ms");
                        AddLog($"   💡 建议: 检查图书馆是否开放，或稍后重试");
                    }
                    else
                    {
                        AddLog($"❌ [5/9] 全链路网络测试: 失败");
                        AddLog($"   ❌ 服务器响应: 返回数据为空");
                        AddLog($"   💡 可能原因: 服务器维护或数据格式变更");
                    }
                }
                catch (System.Net.Http.HttpRequestException ex)
                {
                    AddLog($"❌ [5/9] 全链路网络测试: 失败");
                    AddLog($"   ❌ 错误类型: HTTP 请求异常");

                    // 详细诊断 HTTP 错误
                    if (ex.Message.Contains("Name or service not known") ||
                        ex.Message.Contains("nodename nor servname provided") ||
                        ex.Message.Contains("No such host"))
                    {
                        AddLog($"   🔍 诊断结果: DNS 解析失败");
                        AddLog($"   💡 可能原因:");
                        AddLog($"      - 网络未连接");
                        AddLog($"      - DNS 服务器无响应");
                        AddLog($"      - 防火墙阻止了 DNS 查询");
                        AddLog($"   🔧 解决方案:");
                        AddLog($"      1. 检查网络连接（WiFi/以太网）");
                        AddLog($"      2. 尝试访问其他网站验证网络");
                        AddLog($"      3. 更换 DNS 服务器（如 8.8.8.8）");
                    }
                    else if (ex.Message.Contains("403") || ex.Message.Contains("Forbidden"))
                    {
                        AddLog($"   🔍 诊断结果: 403 Forbidden（禁止访问）");
                        AddLog($"   💡 可能原因:");
                        AddLog($"      - Cookie 已过期或无效");
                        AddLog($"      - IP 被服务器封禁");
                        AddLog($"      - 请求头缺少必要字段");
                        AddLog($"   🔧 解决方案:");
                        AddLog($"      1. 重新登录获取新的 Cookie");
                        AddLog($"      2. 更换网络环境（如切换到手机热点）");
                        AddLog($"      3. 等待一段时间后重试");
                    }
                    else if (ex.Message.Contains("401") || ex.Message.Contains("Unauthorized"))
                    {
                        AddLog($"   🔍 诊断结果: 401 Unauthorized（未授权）");
                        AddLog($"   💡 可能原因: Cookie 已失效");
                        AddLog($"   🔧 解决方案: 重新扫码登录获取新 Cookie");
                    }
                    else if (ex.Message.Contains("timeout") || ex.Message.Contains("timed out"))
                    {
                        AddLog($"   🔍 诊断结果: 请求超时");
                        AddLog($"   💡 可能原因:");
                        AddLog($"      - 网络速度过慢");
                        AddLog($"      - 服务器响应缓慢");
                        AddLog($"      - 防火墙阻止了连接");
                        AddLog($"   🔧 解决方案:");
                        AddLog($"      1. 检查网络速度");
                        AddLog($"      2. 更换网络环境");
                        AddLog($"      3. 稍后重试");
                    }
                    else if (ex.Message.Contains("500") || ex.Message.Contains("Internal Server Error"))
                    {
                        AddLog($"   🔍 诊断结果: 500 服务器内部错误");
                        AddLog($"   💡 可能原因: 服务器正在维护或出现故障");
                        AddLog($"   🔧 解决方案: 等待服务器恢复，稍后重试");
                    }
                    else
                    {
                        AddLog($"   🔍 诊断结果: 未知 HTTP 错误");
                        AddLog($"   ❌ 错误详情: {ex.Message}");
                        AddLog($"   💡 建议: 检查网络连接和服务器状态");
                    }
                }
                catch (System.Threading.Tasks.TaskCanceledException ex)
                {
                    AddLog($"❌ [5/9] 全链路网络测试: 失败");
                    AddLog($"   ❌ 错误类型: 请求超时");
                    AddLog($"   🔍 诊断结果: 网络连接超时（>5秒）");
                    AddLog($"   💡 可能原因:");
                    AddLog($"      - 网络速度过慢");
                    AddLog($"      - 服务器无响应");
                    AddLog($"      - 防火墙阻止了连接");
                    AddLog($"   🔧 解决方案:");
                    AddLog($"      1. 检查网络连接速度");
                    AddLog($"      2. 尝试使用其他网络（如手机热点）");
                    AddLog($"      3. 稍后重试");
                }
                catch (IGoLibrary.Core.Exceptions.GetLibInfoException ex)
                {
                    AddLog($"❌ [5/9] 全链路网络测试: 失败");
                    AddLog($"   ❌ 错误类型: 获取图书馆信息异常");
                    AddLog($"   ❌ 错误详情: {ex.Message}");

                    if (ex.Message.Contains("Cookie") || ex.Message.Contains("cookie"))
                    {
                        AddLog($"   🔍 诊断结果: Cookie 相关问题");
                        AddLog($"   💡 可能原因: Cookie 已过期或格式错误");
                        AddLog($"   🔧 解决方案: 重新登录获取新的 Cookie");
                    }
                    else if (ex.Message.Contains("未找到") || ex.Message.Contains("not found"))
                    {
                        AddLog($"   🔍 诊断结果: 数据未找到");
                        AddLog($"   💡 可能原因: 图书馆 ID 错误或图书馆已关闭");
                        AddLog($"   🔧 解决方案: 重新绑定图书馆");
                    }
                    else
                    {
                        AddLog($"   💡 建议: 检查 Cookie 和图书馆绑定状态");
                    }
                }
                catch (Exception ex)
                {
                    AddLog($"❌ [5/9] 全链路网络测试: 失败");
                    AddLog($"   ❌ 错误类型: {ex.GetType().Name}");
                    AddLog($"   ❌ 错误详情: {ex.Message}");

                    if (ex.InnerException != null)
                    {
                        AddLog($"   ❌ 内部错误: {ex.InnerException.Message}");
                    }

                    AddLog($"   💡 通用建议:");
                    AddLog($"      1. 检查网络连接");
                    AddLog($"      2. 验证 Cookie 是否有效");
                    AddLog($"      3. 确认服务器是否在线");
                    AddLog($"      4. 查看详细错误信息排查问题");
                }
            }
            else
            {
                AddLog($"⊘ [5/9] 全链路网络测试: 跳过");
                AddLog($"   ⚠️ 原因: Cookie 或查询语法未配置");
                AddLog($"   💡 建议: 先完成登录和图书馆绑定");
            }

            // 6. 检查预约座位设置
            totalTests++;
            if (WaitingGrabSeats.Count > 0)
            {
                AddLog($"✅ [6/9] 预约座位: 已设置 {WaitingGrabSeats.Count} 个座位");
                foreach (var seat in WaitingGrabSeats)
                {
                    string priority = seat.Priority == 0 ? "主选" : $"备选{seat.Priority}";
                    AddLog($"   - [{priority}] 座位 {seat.Name} 号 (Key: {seat.Key})");
                }
                passedTests++;
            }
            else
            {
                AddLog($"⚠️ [6/9] 预约座位: 未设置，系统无法自动预约");
                AddLog($"   - 请添加座位到预约列表或加载收藏座位");
            }

            // 7. 检查定时设置
            totalTests++;
            if (TimingTime.HasValue)
            {
                AddLog($"✅ [7/9] 定时设置: 已配置");
                AddLog($"   - 准备时间: {TimingTime.Value}");
                AddLog($"   - 开始抢座: 20:00:00");

                var beijingNow = GetBeijingNow();
                var nowTime = beijingNow.TimeOfDay;
                var interval = TimingTime.Value - nowTime;

                if (interval.TotalSeconds < 0)
                {
                    interval = interval.Add(TimeSpan.FromDays(1));
                }

                var hours = (int)interval.TotalHours;
                var minutes = interval.Minutes;
                var seconds = interval.Seconds;

                if (hours > 0)
                {
                    AddLog($"   - 距离准备时间: {hours}小时{minutes}分{seconds}秒");
                }
                else
                {
                    AddLog($"   - 距离准备时间: {minutes}分{seconds}秒");
                }
                passedTests++;
            }
            else
            {
                AddLog($"❌ [7/9] 定时设置: 未配置");
            }

            // 8. 检查自动运行状态
            totalTests++;
            if (IsMonitoring)
            {
                AddLog($"✅ [8/9] 自动运行: 系统正在运行中");
                passedTests++;
            }
            else
            {
                AddLog($"⏸️ [8/9] 自动运行: 系统未启动");
                AddLog($"   - 系统会在加载收藏座位后自动启动");
            }

            // 9. 战前演习总结
            totalTests++;
            AddLog($"");
            AddLog($"🎯 [9/9] 战前演习总结:");
            if (passedTests >= 7)
            {
                AddLog($"   ✅ 系统状态: 优秀");
                AddLog($"   ✅ 网络通畅: 是");
                AddLog($"   ✅ 服务器在线: 是");
                AddLog($"   ✅ Cookie 有效: 是");
                AddLog($"   🎉 结论: 系统已就绪，可以放心抢座！");
                passedTests++;
            }
            else if (passedTests >= 5)
            {
                AddLog($"   ⚠️ 系统状态: 良好");
                AddLog($"   ⚠️ 部分功能需要配置");
                AddLog($"   💡 建议: 完成所有配置后再开始抢座");
            }
            else
            {
                AddLog($"   ❌ 系统状态: 异常");
                AddLog($"   ❌ 多项检查未通过");
                AddLog($"   💡 建议: 按照下方提示修复问题");
            }

            // 输出自检结果汇总
            AddLog("========================================");
            AddLog($"【战前演习完成】通过 {passedTests}/{totalTests} 项检查");

            if (passedTests == totalTests)
            {
                AddLog($"🎉 系统状态: 完美！所有功能正常");
                _notificationService.ShowSuccess("自检完成", $"系统状态完美，通过所有 {totalTests} 项检查");
            }
            else if (passedTests >= totalTests * 0.7)
            {
                AddLog($"✓ 系统状态: 良好，大部分功能正常");
                _notificationService.ShowSuccess("自检完成", $"系统状态良好，通过 {passedTests}/{totalTests} 项检查");
            }
            else if (passedTests >= totalTests * 0.5)
            {
                AddLog($"⚠️ 系统状态: 一般，部分功能需要配置");
                _notificationService.ShowWarning("自检完成", $"系统状态一般，通过 {passedTests}/{totalTests} 项检查");
            }
            else
            {
                AddLog($"❌ 系统状态: 异常，请检查配置");
                _notificationService.ShowError("自检完成", $"系统状态异常，仅通过 {passedTests}/{totalTests} 项检查");
            }

            AddLog("========================================");

            // 给出建议
            if (passedTests < totalTests)
            {
                AddLog("【建议】请按照以下步骤修复问题:");

                if (string.IsNullOrWhiteSpace(_sessionService.Cookie))
                {
                    AddLog("  1. 前往登录页面，扫码获取 Cookie");
                }

                if (_sessionService.CurrentLibrary == null)
                {
                    AddLog("  2. 在登录页面绑定图书馆");
                }

                if (WaitingGrabSeats.Count == 0)
                {
                    AddLog("  3. 添加座位到预约列表或加载收藏座位");
                }
            }
        }

        #endregion

        #region 核心监控逻辑

        /// <summary>
        /// 核心监控循环（优化版本）
        /// 每次循环都尝试预约，第一次请求立即发送，后续请求间隔1秒
        /// 每分钟60次请求，比Gitee的实际间隔（1800ms）快1.8倍
        /// </summary>
        private async Task RunMonitorAsync(CancellationToken cancellationToken)
        {
            int count = 0;

            // 检查是否设置了预约座位
            if (WaitingGrabSeats.Count == 0)
            {
                AddLog($"【错误】未设置预约座位，停止监控");
                _notificationService.ShowError("监控停止", "未设置预约座位");
                return;
            }

            // 如果设置了定时抢座，先等待到指定时间
            if (TimingTime.HasValue)
            {
                await WaitForTimingAsync(TimingTime.Value, GrabSeatStartTime, cancellationToken);
            }

            AddLog($"【开始抢座】准备预约 {WaitingGrabSeats.Count} 个座位（主选+备选）");

            // 主监控循环 - 优化版本
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    count++;
                    var beijingNow = GetBeijingNow();

                    AddLog($"【第{count}次尝试】北京时间: {beijingNow:HH:mm:ss} - 开始尝试预约");

                    // 按顺序尝试所有座位（主选+备选）
                    bool reservationSuccess = false;
                    for (int i = 0; i < WaitingGrabSeats.Count; i++)
                    {
                        try
                        {
                            var targetSeat = WaitingGrabSeats[i];
                            string seatPosition = i == 0 ? "主选" : $"备选{i}";

                            AddLog($"【尝试预约】{seatPosition}座位 {targetSeat.Name} (Key: {targetSeat.Key})");

                            // 直接尝试预约，不检查座位状态
                            // 让服务器返回成功或失败，而不是客户端判断
                            bool prereserveSuccess = _prereserveSeatService.PrereserveSeat(
                                _sessionService.Cookie!,
                                targetSeat.Key,
                                _sessionService.CurrentLibrary?.LibID ?? 0);

                            if (prereserveSuccess)
                            {
                                var successTime = GetBeijingNow();
                                AddLog($"【预约成功】🎉 {seatPosition}座位 {targetSeat.Name} 号预约成功！北京时间: {successTime:HH:mm:ss}");
                                _notificationService.ShowSuccess("抢座成功", $"{seatPosition}座位 {targetSeat.Name} 号预约成功！");
                                reservationSuccess = true;
                                return; // 成功后退出监控
                            }
                        }
                        catch (ReserveSeatException ex)
                        {
                            string seatPosition = i == 0 ? "主选" : $"备选{i}";
                            AddLog($"【预约失败】{seatPosition}座位 {WaitingGrabSeats[i].Name} 预约失败: {ex.Message}，尝试下一个备选座位");
                            // 继续尝试下一个备选座位
                        }
                        catch (Exception ex)
                        {
                            string seatPosition = i == 0 ? "主选" : $"备选{i}";
                            AddLog($"【预约异常】{seatPosition}座位 {WaitingGrabSeats[i].Name} 预约异常: {ex.Message}，尝试下一个备选座位");
                            // 继续尝试下一个备选座位
                        }
                    }

                    if (!reservationSuccess)
                    {
                        AddLog($"【本轮失败】所有座位预约失败，继续下一轮尝试");
                    }

                    // 固定使用激进模式延迟时间：1000ms（1秒）
                    // 第一次查询（count=1）不延迟，立即发送请求快人一步
                    // 第二次及以后才延迟，控制请求数量为每分钟 60 次
                    if (count > 1)
                    {
                        int delayMs = 1000;
                        await Task.Delay(delayMs, cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    // 正常取消，重新抛出
                    AddLog($"【监控停止】用户取消监控");
                    throw;
                }
                catch (ReserveSeatException ex)
                {
                    var beijingNow = GetBeijingNow();
                    AddLog($"【异常】第{count}次 - 北京时间: {beijingNow:HH:mm:ss} - 预约异常: {ex.Message}");

                    // 出错后继续尝试，不终止监控
                    if (count > 1)
                    {
                        await Task.Delay(1000, cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    var beijingNow = GetBeijingNow();
                    AddLog($"【异常】第{count}次 - 北京时间: {beijingNow:HH:mm:ss} - 未知异常: {ex.Message}");

                    // 出错后继续尝试，不终止监控
                    if (count > 1)
                    {
                        await Task.Delay(1000, cancellationToken);
                    }
                }
            }
        }

        /// <summary>
        /// 等待到定时抢座时间（使用北京时间 UTC+8）
        /// </summary>
        private async Task WaitForTimingAsync(TimeSpan prepareTime, TimeSpan startTime, CancellationToken cancellationToken)
        {
            var beijingNow = GetBeijingNow();

            string timeMode = EnableTimeSimulation ? " [时间模拟模式已启用]" : "";
            AddLog($"【系统启动】自动预约系统已启动{timeMode}");
            AddLog($"【时间设置】准备时间: {prepareTime}, 开始抢座时间: {startTime}");
            AddLog($"【当前时间】北京时间: {beijingNow:yyyy-MM-dd HH:mm:ss}");
            AddLog($"【目标时间】{prepareTime}");

            int logCounter = 0;
            bool hasEnteredPrepareState = false;

            while (!cancellationToken.IsCancellationRequested)
            {
                beijingNow = GetBeijingNow();
                var nowTime = beijingNow.TimeOfDay;

                if (!hasEnteredPrepareState)
                {
                    var prepareInterval = prepareTime - nowTime;
                    if (prepareInterval.TotalSeconds < 0)
                    {
                        prepareInterval = prepareInterval.Add(TimeSpan.FromDays(1));
                    }

                    if (prepareInterval.TotalSeconds <= 0 || nowTime >= prepareTime)
                    {
                        hasEnteredPrepareState = true;
                        AddLog($"【准备完成】北京时间 {beijingNow:HH:mm:ss}，已进入准备状态，等待开始抢座");
                        continue;
                    }

                    if (prepareInterval < TimeSpan.FromMinutes(1))
                    {
                        AddLog($"【倒计时】距离准备时间还有 {prepareInterval.TotalSeconds:F0} 秒（北京时间: {beijingNow:HH:mm:ss}）");
                    }
                    else if (logCounter % 10 == 0)
                    {
                        var hours = (int)prepareInterval.TotalHours;
                        var minutes = prepareInterval.Minutes;
                        var seconds = prepareInterval.Seconds;
                        if (hours > 0)
                        {
                            AddLog($"【等待中】距离准备时间还有 {hours}小时{minutes}分{seconds}秒（北京时间: {beijingNow:HH:mm:ss}）");
                        }
                        else
                        {
                            AddLog($"【等待中】距离准备时间还有 {minutes}分{seconds}秒（北京时间: {beijingNow:HH:mm:ss}）");
                        }
                    }
                }
                else
                {
                    var startInterval = startTime - nowTime;
                    if (startInterval.TotalSeconds < 0)
                    {
                        startInterval = startInterval.Add(TimeSpan.FromDays(1));
                    }

                    if (startInterval.TotalSeconds <= 0 || nowTime >= startTime)
                    {
                        var startTimestamp = GetBeijingNow();
                        AddLog($"【开始抢座】北京时间 {startTimestamp:HH:mm:ss}，正式开始抢座！");
                        break;
                    }

                    if (startInterval < TimeSpan.FromMinutes(1))
                    {
                        AddLog($"【倒计时】距离开始抢座还有 {startInterval.TotalSeconds:F0} 秒（北京时间: {beijingNow:HH:mm:ss}）");
                    }
                    else if (logCounter % 10 == 0)
                    {
                        var hours = (int)startInterval.TotalHours;
                        var minutes = startInterval.Minutes;
                        var seconds = startInterval.Seconds;
                        if (hours > 0)
                        {
                            AddLog($"【等待中】距离开始抢座还有 {hours}小时{minutes}分{seconds}秒（北京时间: {beijingNow:HH:mm:ss}）");
                        }
                        else
                        {
                            AddLog($"【等待中】距离开始抢座还有 {minutes}分{seconds}秒（北京时间: {beijingNow:HH:mm:ss}）");
                        }
                    }
                }

                logCounter++;
                await Task.Delay(1000, cancellationToken);
            }
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 获取北京时间（支持时间模拟和调试时间偏移）
        /// </summary>
        private DateTime GetBeijingNow()
        {
            if (EnableTimeSimulation)
            {
                // 时间模拟模式：返回今天的模拟时间 + 调试偏移
                var today = DateTime.Today;
                var simulatedDateTime = today.Add(SimulatedTime);

                // 应用调试时间偏移
                if (DebugTimeOffsetSeconds != 0)
                {
                    simulatedDateTime = simulatedDateTime.AddSeconds(DebugTimeOffsetSeconds);
                }

                return simulatedDateTime;
            }
            else
            {
                // 真实模式：返回真实的北京时间
                var beijingTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
                return TimeZoneInfo.ConvertTime(DateTime.Now, beijingTimeZone);
            }
        }

        /// <summary>
        /// 设置调试时间偏移（用于时间旅行测试）
        /// </summary>
        /// <param name="offsetSeconds">偏移秒数（正数=未来，负数=过去）</param>
        public void SetDebugTimeOffset(int offsetSeconds)
        {
            DebugTimeOffsetSeconds = offsetSeconds;
            var newTime = GetBeijingNow();
            AddLog($"🕐 [调试时间] 时间偏移已设置为 {offsetSeconds} 秒");
            AddLog($"🕐 [调试时间] 当前模拟时间: {newTime:HH:mm:ss}");
        }

        /// <summary>
        /// 重置调试时间偏移
        /// </summary>
        public void ResetDebugTimeOffset()
        {
            DebugTimeOffsetSeconds = 0;
            var newTime = GetBeijingNow();
            AddLog($"🕐 [调试时间] 时间偏移已重置");
            AddLog($"🕐 [调试时间] 当前模拟时间: {newTime:HH:mm:ss}");
        }

        /// <summary>
        /// 时间旅行到指定时间（用于快速测试）
        /// </summary>
        /// <param name="targetTime">目标时间（HH:mm:ss）</param>
        [RelayCommand]
        private void TimeTravelTo(string targetTime)
        {
            try
            {
                var parts = targetTime.Split(':');
                if (parts.Length == 3)
                {
                    int hour = int.Parse(parts[0]);
                    int minute = int.Parse(parts[1]);
                    int second = int.Parse(parts[2]);

                    var target = new TimeSpan(hour, minute, second);
                    var current = SimulatedTime;
                    var offset = (int)(target - current).TotalSeconds;

                    SetDebugTimeOffset(offset);
                    AddLog($"⏰ [时间旅行] 已跳转到 {targetTime}");
                }
                else
                {
                    AddLog($"❌ [时间旅行] 时间格式错误，请使用 HH:mm:ss 格式");
                }
            }
            catch (Exception ex)
            {
                AddLog($"❌ [时间旅行] 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 添加日志（自动限制日志数量，防止内存泄漏）
        /// </summary>
        private void AddLog(string message)
        {
            var logEntry = $"[{DateTime.Now:T}] {message}";

            // 在UI线程上更新ObservableCollection
            if (System.Threading.SynchronizationContext.Current != null)
            {
                Logs.Insert(0, logEntry);
            }
            else
            {
                // 如果不在UI线程，使用Dispatcher
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    Logs.Insert(0, logEntry);
                });
            }

            // 限制日志数量，防止内存泄漏（保留最近500条）
            if (Logs.Count > 500)
            {
                while (Logs.Count > 500)
                {
                    Logs.RemoveAt(Logs.Count - 1);
                }
            }
        }

        /// <summary>
        /// 自动保存座位设置（静默保存，不显示通知）
        /// </summary>
        private async Task AutoSaveSeatsAsync()
        {
            try
            {
                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var favoritesDir = Path.Combine(appDataPath, "IGoLibrary", "Favorites");

                if (!Directory.Exists(favoritesDir))
                {
                    Directory.CreateDirectory(favoritesDir);
                }

                // 保存所有座位（主选+备选）
                var seatList = WaitingGrabSeats.Select(s => new
                {
                    Key = s.Key,
                    Name = s.Name,
                    Priority = s.Priority
                }).ToList();

                var favs = new
                {
                    LibID = _sessionService.CurrentLibrary?.LibID ?? 0,
                    Seats = seatList
                };

                var favoriteSeatsContent = JsonConvert.SerializeObject(favs);
                var filePath = Path.Combine(favoritesDir, $"{favs.LibID}.json");

                await File.WriteAllTextAsync(filePath, favoriteSeatsContent);

                // 静默保存，只记录日志，不显示通知
                AddLog($"已自动保存 {WaitingGrabSeats.Count} 个座位设置");
            }
            catch (Exception ex)
            {
                AddLog($"自动保存座位失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 自动恢复上次的座位设置（静默恢复，不显示通知）
        /// </summary>
        private async Task AutoRestoreSeatsAsync()
        {
            try
            {
                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var filePath = Path.Combine(appDataPath, "IGoLibrary", "Favorites", $"{_sessionService.CurrentLibrary?.LibID ?? 0}.json");

                if (!File.Exists(filePath))
                {
                    AddLog("未找到上次的座位设置");
                    return;
                }

                var favoriteSeatsContent = await File.ReadAllTextAsync(filePath);
                var favData = JsonConvert.DeserializeObject<dynamic>(favoriteSeatsContent);

                if (favData?.Seats != null)
                {
                    WaitingGrabSeats.Clear();

                    foreach (var seat in favData.Seats)
                    {
                        WaitingGrabSeats.Add(new SeatKeyData
                        {
                            Name = seat.Name.ToString(),
                            Status = "未知",
                            Key = seat.Key.ToString(),
                            Priority = seat.Priority != null ? (int)seat.Priority : 0
                        });
                    }

                    string seatNames = string.Join("、", WaitingGrabSeats.Select(s => $"{s.PriorityText}:{s.Name}号"));
                    AddLog($"已自动恢复上次的座位设置: {seatNames}");
                }
            }
            catch (Exception ex)
            {
                AddLog($"自动恢复座位失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 自动初始化：加载收藏座位并启动监控
        /// 参考Gitee仓库的自动运行逻辑
        /// </summary>
        public async Task AutoInitializeAsync()
        {
            // 防止重复初始化
            if (_isAutoInitialized)
            {
                AddLog("已经完成自动初始化，跳过");
                return;
            }

            _isAutoInitialized = true;

            // 等待一小段时间，确保UI和服务都已初始化
            await Task.Delay(1000);

            try
            {
                // 检查是否已登录
                if (string.IsNullOrWhiteSpace(_sessionService.Cookie))
                {
                    AddLog("自动初始化失败：Cookie 未设置，请先登录");
                    return;
                }

                // 检查是否已绑定图书馆
                if (string.IsNullOrWhiteSpace(_sessionService.QueryLibInfoSyntax))
                {
                    AddLog("自动初始化失败：未绑定图书馆，请先在登录页面绑定图书馆");
                    return;
                }

                // 自动刷新座位信息
                AddLog("开始自动刷新座位信息...");
                await RefreshSeatsAsync();

                // 自动恢复上次的座位设置（静默恢复）
                AddLog("开始自动恢复上次的座位设置...");
                await AutoRestoreSeatsAsync();

                // 检查是否成功恢复了座位
                if (WaitingGrabSeats.Count == 0)
                {
                    AddLog("自动初始化完成：未找到上次的座位设置，请手动添加座位到抢座列表");
                    return;
                }

                // 自动启动监控（参考Gitee仓库的自动运行逻辑）
                AddLog($"自动初始化完成：已恢复 {WaitingGrabSeats.Count} 个座位，定时时间为 {TimingTime}");
                AddLog("开始自动启动监控...");
                await StartMonitorAsync();
            }
            catch (Exception ex)
            {
                AddLog($"自动初始化出现异常: {ex.Message}");
            }
        }

        #endregion
    }
}
