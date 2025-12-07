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
    public partial class OccupySeatViewModel : ObservableObject
    {
        private readonly IGetLibInfoService _getLibInfoService;
        private readonly IReserveSeatService _reserveSeatService;
        private readonly ISessionService _sessionService;
        private readonly INotificationService _notificationService;
        private readonly SeatFilterService _seatFilterService;

        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _reserveTask;

        public OccupySeatViewModel(
            IGetLibInfoService getLibInfoService,
            IReserveSeatService reserveSeatService,
            ISessionService sessionService,
            INotificationService notificationService)
        {
            _getLibInfoService = getLibInfoService;
            _reserveSeatService = reserveSeatService;
            _sessionService = sessionService;
            _notificationService = notificationService;
            _seatFilterService = new SeatFilterService();

            SeatList = new ObservableCollection<SeatKeyData>();
            Logs = new ObservableCollection<string>();
            WaitingReserveSeats = new ObservableCollection<SeatKeyData>();
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
        /// 待占座位列表
        /// </summary>
        [ObservableProperty]
        private ObservableCollection<SeatKeyData> _waitingReserveSeats;

        /// <summary>
        /// 是否正在占座
        /// </summary>
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(StartReserveCommand))]
        [NotifyCanExecuteChangedFor(nameof(StopReserveCommand))]
        private bool _isReserving;

        /// <summary>
        /// 占座模式：0=激进(1秒), 1=随机(4-8秒), 2=保守(5秒)
        /// </summary>
        [ObservableProperty]
        private int _reserveMode = 2;

        /// <summary>
        /// 选中的座位（用于添加到占座列表）
        /// </summary>
        [ObservableProperty]
        private SeatKeyData? _selectedSeat;

        /// <summary>
        /// 选中的多个座位索引（用于收藏功能）
        /// </summary>
        public List<int> SelectedSeatIndices { get; set; } = new List<int>();

        #endregion

        #region 命令

        /// <summary>
        /// 开始即时占座
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanStartReserve))]
        private async Task StartReserveAsync()
        {
            if (WaitingReserveSeats.Count == 0)
            {
                _notificationService.ShowError("开始占座失败", "未设置待占座位，请设置后重试");
                return;
            }

            if (string.IsNullOrWhiteSpace(_sessionService.Cookie))
            {
                _notificationService.ShowError("开始占座失败", "Cookie 未设置，请先登录");
                return;
            }

            IsReserving = true;
            _cancellationTokenSource = new CancellationTokenSource();

            AddLog("开始即时占座");

            try
            {
                _reserveTask = RunReserveAsync(_cancellationTokenSource.Token);
                await _reserveTask;
            }
            catch (OperationCanceledException)
            {
                AddLog("占座已停止");
            }
            catch (Exception ex)
            {
                AddLog($"占座出现异常: {ex.Message}");
                _notificationService.ShowError("占座异常", ex.Message);
            }
            finally
            {
                IsReserving = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        private bool CanStartReserve() => !IsReserving;

        /// <summary>
        /// 停止即时占座
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanStopReserve))]
        private void StopReserve()
        {
            _cancellationTokenSource?.Cancel();
            AddLog("正在停止占座...");
        }

        private bool CanStopReserve() => IsReserving;

        /// <summary>
        /// 添加选中座位到占座列表
        /// </summary>
        [RelayCommand]
        private void AddToReserveList()
        {
            if (SelectedSeat == null)
            {
                _notificationService.ShowError("添加失败", "请先选择座位");
                return;
            }

            if (!WaitingReserveSeats.Any(s => s.Key == SelectedSeat.Key))
            {
                WaitingReserveSeats.Add(new SeatKeyData
                {
                    Name = SelectedSeat.Name,
                    Status = SelectedSeat.Status,
                    Key = SelectedSeat.Key
                });

                _notificationService.ShowSuccess("添加成功", $"已添加 {SelectedSeat.Name} 号座位到占座列表");
            }
            else
            {
                _notificationService.ShowWarning("添加失败", "该座位已在占座列表中");
            }
        }

        /// <summary>
        /// 清空占座列表
        /// </summary>
        [RelayCommand]
        private void ClearReserveList()
        {
            WaitingReserveSeats.Clear();
            AddLog("已清空占座列表");
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
        /// 收藏选中的座位
        /// </summary>
        [RelayCommand]
        private async Task SaveFavoriteSeatsAsync()
        {
            if (SelectedSeatIndices.Count < 1)
            {
                _notificationService.ShowError("收藏选中座位失败", "还未选中任何座位，无法收藏");
                return;
            }

            try
            {
                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var favoritesDir = Path.Combine(appDataPath, "IGoLibrary", "Favorites", "Instant");

                if (!Directory.Exists(favoritesDir))
                {
                    Directory.CreateDirectory(favoritesDir);
                }

                var favs = new FavoriteSeats
                {
                    SelectedRowIndex = SelectedSeatIndices.ToArray(),
                    LibID = _sessionService.CurrentLibrary?.LibID ?? 0
                };

                var favoriteSeatsContent = JsonConvert.SerializeObject(favs);
                var filePath = Path.Combine(favoritesDir, $"{favs.LibID}.json");

                await File.WriteAllTextAsync(filePath, favoriteSeatsContent);

                _notificationService.ShowSuccess("收藏选中座位成功", $"已成功收藏{favs.SelectedRowIndex.Length}个座位，可下次恢复使用");
                AddLog($"已收藏 {favs.SelectedRowIndex.Length} 个座位");
            }
            catch (Exception ex)
            {
                _notificationService.ShowError("收藏选中座位失败", $"在写入座位数据时发生了异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 加载收藏的座位
        /// </summary>
        [RelayCommand]
        private async Task LoadFavoriteSeatsAsync()
        {
            try
            {
                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var filePath = Path.Combine(appDataPath, "IGoLibrary", "Favorites", "Instant", $"{_sessionService.CurrentLibrary?.LibID ?? 0}.json");

                if (!File.Exists(filePath))
                {
                    _notificationService.ShowError("加载收藏座位失败", "收藏座位数据不存在，可能是并未收藏该场馆的座位或还未绑定图书馆");
                    return;
                }

                var favoriteSeatsContent = await File.ReadAllTextAsync(filePath);
                var favs = JsonConvert.DeserializeObject<FavoriteSeats>(favoriteSeatsContent);

                if (favs?.SelectedRowIndex != null && favs.SelectedRowIndex.Length > 0)
                {
                    SelectedSeatIndices.Clear();
                    SelectedSeatIndices.AddRange(favs.SelectedRowIndex);

                    // 将收藏的座位添加到待占列表
                    WaitingReserveSeats.Clear();
                    foreach (var index in favs.SelectedRowIndex)
                    {
                        if (index < SeatList.Count)
                        {
                            var seat = SeatList[index];
                            WaitingReserveSeats.Add(new SeatKeyData
                            {
                                Name = seat.Name,
                                Status = seat.Status,
                                Key = seat.Key
                            });
                        }
                    }

                    _notificationService.ShowSuccess("加载收藏座位成功", $"已成功加载{favs.SelectedRowIndex.Length}个座位至列表");
                    AddLog($"已加载 {favs.SelectedRowIndex.Length} 个收藏座位");
                }
                else
                {
                    _notificationService.ShowError("加载收藏座位失败", "数据中不存在收藏的座位数据");
                }
            }
            catch (Exception ex)
            {
                _notificationService.ShowError("加载收藏座位失败", $"读取数据时发生了异常: {ex.Message}");
            }
        }

        #endregion

        #region 核心占座逻辑

        /// <summary>
        /// 核心即时占座循环（使用 reserve API）
        /// </summary>
        private async Task RunReserveAsync(CancellationToken cancellationToken)
        {
            var random = new Random();
            int count = 1;

            // 主占座循环
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // 获取最新座位信息
                    var library = _getLibInfoService.GetLibInfo(
                        _sessionService.Cookie!,
                        _sessionService.QueryLibInfoSyntax ?? "");

                    AddLog($"[第{count}次][{DateTime.Now:T}] 获取座位信息成功");

                    // 筛选出待占座位的最新状态
                    var selectedSeatsStatus = _seatFilterService.GetSelectedSeatsKeyData(
                        library,
                        WaitingReserveSeats.ToList());

                    // 检查每个座位
                    foreach (var seatData in selectedSeatsStatus)
                    {
                        if (seatData.Status == "无人")
                        {
                            AddLog($"[第{count}次][{DateTime.Now:T}] {seatData.Name}号座位无人，开始预约该位置");

                            // 短暂延迟后预约
                            await Task.Delay(100, cancellationToken);

                            // 使用即时预约服务（reserve）
                            var reserveQuery = $"query=mutation{{reserveSeat(libId:{_sessionService.CurrentLibrary?.LibID ?? 0},seatKey:\"{seatData.Key}\",captchaCode:\"\",captchaToken:\"\"){{id,status}}}}";

                            bool reserveSuccess = _reserveSeatService.ReserveSeat(
                                _sessionService.Cookie!,
                                reserveQuery);

                            if (reserveSuccess)
                            {
                                AddLog($"[第{count}次][{DateTime.Now:T}] {seatData.Name}号座位预约成功，结束占座");
                                _notificationService.ShowSuccess("占座成功", $"{seatData.Name}号座位预约成功");
                                return; // 成功后退出占座
                            }
                            else
                            {
                                AddLog($"[第{count}次][{DateTime.Now:T}] {seatData.Name}号座位预约失败，原因未知，结束占座");
                                _notificationService.ShowWarning("占座失败", $"{seatData.Name}号座位预约失败");
                                return;
                            }
                        }
                        else
                        {
                            AddLog($"[第{count}次][{DateTime.Now:T}] {seatData.Name}号座位: 有人");
                        }
                    }

                    count++;

                    // 每50次循环额外延迟5-10秒
                    if (count % 50 == 0)
                    {
                        AddLog($"[第{count}次][{DateTime.Now:T}] 已运行一个大循环，额外延迟5~10秒");
                        await Task.Delay(random.Next(5000, 10000), cancellationToken);
                    }

                    // 根据模式选择延迟时间
                    int delayMs = ReserveMode switch
                    {
                        0 => 1000,                      // 激进模式: 1秒
                        1 => random.Next(4000, 8000),   // 随机模式: 4-8秒
                        _ => 5000                       // 保守模式: 5秒
                    };

                    await Task.Delay(delayMs, cancellationToken);
                }
                catch (GetLibInfoException ex)
                {
                    AddLog($"[第{count}次][{DateTime.Now:T}] 出现异常，异常信息: {ex.Message}，需要重新填写Cookie并验证后再使用");
                    _notificationService.ShowError("获取座位信息失败", ex.Message);
                    throw; // 抛出异常，终止占座
                }
                catch (ReserveSeatException ex)
                {
                    AddLog($"[第{count}次][{DateTime.Now:T}] 预定座位失败，失败信息: {ex.Message}，结束占座");
                    _notificationService.ShowError("预定座位失败", ex.Message);
                    throw; // 抛出异常，终止占座
                }
                catch (OperationCanceledException)
                {
                    // 正常取消，重新抛出
                    throw;
                }
                catch (Exception ex)
                {
                    AddLog($"[第{count}次][{DateTime.Now:T}] 未知异常: {ex.Message}");
                    _notificationService.ShowError("未知异常", ex.Message);
                    throw;
                }
            }
        }

        #endregion

        #region 辅助方法

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

        #endregion
    }
}
