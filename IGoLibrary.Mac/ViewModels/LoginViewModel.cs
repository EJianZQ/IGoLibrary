using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IGoLibrary.Core.Data;
using IGoLibrary.Core.Exceptions;
using IGoLibrary.Core.Interfaces;
using IGoLibrary.Core.Services;
using IGoLibrary.Core.Utils;
using Newtonsoft.Json;

namespace IGoLibrary.Mac.ViewModels
{
    public partial class LoginViewModel : ObservableObject
    {
        private readonly IGetCookieService _getCookieService;
        private readonly IGetLibInfoService _getLibInfoService;
        private readonly IGetAllLibsSummaryService _getAllLibsSummaryService;
        private readonly ISessionService _sessionService;
        private readonly IStorageService _storageService;
        private readonly INotificationService _notificationService;

        public LoginViewModel(
            IGetCookieService getCookieService,
            IGetLibInfoService getLibInfoService,
            IGetAllLibsSummaryService getAllLibsSummaryService,
            ISessionService sessionService,
            IStorageService storageService,
            INotificationService notificationService)
        {
            _getCookieService = getCookieService;
            _getLibInfoService = getLibInfoService;
            _getAllLibsSummaryService = getAllLibsSummaryService;
            _sessionService = sessionService;
            _storageService = storageService;
            _notificationService = notificationService;

            // 默认语法
            QueryLibInfoSyntax = "{\"operationName\":\"libLayout\",\"query\":\"query libLayout($libId: Int, $libType: Int) {\\n userAuth {\\n reserve {\\n libs(libType: $libType, libId: $libId) {\\n lib_id\\n is_open\\n lib_floor\\n lib_name\\n lib_type\\n lib_layout {\\n seats_total\\n seats_booking\\n seats_used\\n max_x\\n max_y\\n seats {\\n x\\n y\\n key\\n type\\n name\\n seat_status\\n status\\n }\\n }\\n }\\n }\\n }\\n}\",\"variables\":{\"libId\":ReplaceMe}}";
            ReserveSeatSyntax = "query=mutation{reserveSeat(libId:ReplaceMeByLibID,seatKey:\"ReplaceMeBySeatKey\",captchaCode:\"\",captchaToken:\"\"){id,status}}";
            QueryAllLibsSummarySyntax = "{\"operationName\":\"list\",\"query\":\"query list {\\n userAuth {\\n reserve {\\n libs(libType: -1) {\\n lib_id\\n lib_floor\\n is_open\\n lib_name\\n lib_type\\n lib_group_id\\n lib_comment\\n lib_rt {\\n seats_total\\n seats_used\\n seats_booking\\n seats_has\\n reserve_ttl\\n open_time\\n open_time_str\\n close_time\\n close_time_str\\n advance_booking\\n }\\n }\\n libGroups {\\n id\\n group_name\\n }\\n reserve {\\n isRecordUser\\n }\\n }\\n record {\\n libs {\\n lib_id\\n lib_floor\\n is_open\\n lib_name\\n lib_type\\n lib_group_id\\n lib_comment\\n lib_color_name\\n lib_rt {\\n seats_total\\n seats_used\\n seats_booking\\n seats_has\\n reserve_ttl\\n open_time\\n open_time_str\\n close_time\\n close_time_str\\n advance_booking\\n }\\n }\\n }\\n rule {\\n signRule\\n }\\n }\\n}\"}";
            QueryReserveInfoSyntax = "query=query{reservations{id,lib{id,name,floor},seat{id,name,key},status,startTime,endTime,token}}";
            CancelReserveSyntax = "query=mutation{cancelReservation(token:\"ReplaceMe\"){id,status}}";
            CodeSourceURL = "";
        }

        #region 属性

        [ObservableProperty]
        private string _cookie = "";

        [ObservableProperty]
        private string _queryLibInfoSyntax = "";

        [ObservableProperty]
        private string _reserveSeatSyntax = "";

        [ObservableProperty]
        private string _queryAllLibsSummarySyntax = "";

        [ObservableProperty]
        private string _queryReserveInfoSyntax = "";

        [ObservableProperty]
        private string _cancelReserveSyntax = "";

        [ObservableProperty]
        private string _codeSourceURL = "";

        [ObservableProperty]
        private int _libID = 0;

        [ObservableProperty]
        private string _libStatus = "状态：未绑定";

        [ObservableProperty]
        private string _libName = "图书馆(室)名称：未绑定";

        [ObservableProperty]
        private string _libFloor = "图书馆(室)楼层：未绑定";

        [ObservableProperty]
        private string _libAvailableSeats = "图书馆(室)余座：未绑定";

        [ObservableProperty]
        private System.Collections.ObjectModel.ObservableCollection<string> _libraryOptions = new();

        [ObservableProperty]
        private int _selectedLibraryIndex = -1;

        [ObservableProperty]
        private bool _showLibrarySelection = false;

        private List<Core.Data.LibSummary>? _allLibraries = null;

        #endregion

        #region 命令

        /// <summary>
        /// 从URL获取Cookie
        /// </summary>
        [RelayCommand]
        private async Task GetCookieFromUrlAsync()
        {
            if (string.IsNullOrWhiteSpace(CodeSourceURL))
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    _notificationService.ShowError("获取Cookie失败", "请输入含有code的URL"));
                return;
            }

            if (Regex.IsMatch(CodeSourceURL, @".*wechat\.v2\.traceint\.com\/index\.php\/graphql\/\?operationName=index&query=query.*&code=.{32}&state=(0|1)"))
            {
                var match = Regex.Match(CodeSourceURL, @"code=.{32}");
                if (match.Success)
                {
                    try
                    {
                        var code = match.Value.Replace("code=", string.Empty);
                        var cookie = await Task.Run(() => _getCookieService.GetCookie(code))
                            .ConfigureAwait(false);

                        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            Cookie = cookie;
                            _notificationService.ShowSuccess("获取Cookie成功", "已自动填写Cookie，请点击\"验证并绑定图书馆\"按钮");
                        });
                    }
                    catch (GetCookieException ex)
                    {
                        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                            _notificationService.ShowError("获取Cookie失败", ex.Message));
                    }
                }
                else
                {
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                        _notificationService.ShowError("获取Cookie失败", "从链接中匹配出code失败"));
                }
            }
            else
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    _notificationService.ShowError("获取Cookie失败", "提供的含code链接格式有误，不符合格式规范"));
            }
        }

        /// <summary>
        /// 验证Cookie并绑定图书馆
        /// </summary>
        [RelayCommand]
        private async Task BindLibraryAsync()
        {
            if (string.IsNullOrWhiteSpace(Cookie))
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    _notificationService.ShowError("验证失败", "请先输入Cookie"));
                return;
            }

            if (!Cookie.Contains("Authorization") || !Cookie.Contains("SERVERID"))
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    _notificationService.ShowError("Cookies验证失败", "Cookies不合法，不包含关键要素"));
                return;
            }

            try
            {
                // 获取所有图书馆列表 - 在后台线程执行
                var allLibsSummary = await Task.Run(() =>
                    _getAllLibsSummaryService.GetAllLibsSummary(Cookie, QueryAllLibsSummarySyntax))
                    .ConfigureAwait(false);

                if (allLibsSummary?.libSummaries == null || allLibsSummary.libSummaries.Count == 0)
                {
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                        _notificationService.ShowError("绑定失败", "未找到可用的图书馆"));
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

                    // 默认选择第一个开放的图书馆
                    SelectedLibraryIndex = allLibsSummary.libSummaries.FindIndex(lib => lib.IsOpen);
                    if (SelectedLibraryIndex == -1) SelectedLibraryIndex = 0;

                    ShowLibrarySelection = true;
                    _notificationService.ShowSuccess("获取图书馆列表成功", "请在下方选择要绑定的图书馆,然后点击\"确认绑定\"按钮");
                });
            }
            catch (GetLibInfoException ex)
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    LibStatus = $"状态：{ex.Message}";
                    LibName = "图书馆(室)名称：Error";
                    LibFloor = "图书馆(室)楼层：Error";
                    LibAvailableSeats = "图书馆(室)余座：Error";
                    _notificationService.ShowError("绑定失败", ex.Message);
                });
            }
            catch (GetAllLibsSummaryException ex)
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    LibStatus = $"状态：{ex.Message}";
                    LibName = "图书馆(室)名称：Error";
                    LibFloor = "图书馆(室)楼层：Error";
                    LibAvailableSeats = "图书馆(室)余座：Error";
                    _notificationService.ShowError("绑定失败", ex.Message);
                });
            }
        }

        /// <summary>
        /// 确认绑定选中的图书馆
        /// </summary>
        [RelayCommand]
        private async Task ConfirmBindLibraryAsync()
        {
            if (_allLibraries == null || SelectedLibraryIndex < 0 || SelectedLibraryIndex >= _allLibraries.Count)
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    _notificationService.ShowError("绑定失败", "请先选择一个图书馆"));
                return;
            }

            try
            {
                var selectedLib = _allLibraries[SelectedLibraryIndex];

                // 构造该图书馆的查询语法
                var libQuerySyntax = QueryLibInfoSyntax.Replace("ReplaceMe", selectedLib.LibID.ToString());

                // 获取图书馆详细信息 - 在后台线程执行
                var libraryData = await Task.Run(() =>
                    _getLibInfoService.GetLibInfo(Cookie, libQuerySyntax))
                    .ConfigureAwait(false);

                // 更新UI显示 - 必须在UI线程执行
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    LibID = selectedLib.LibID;
                    LibStatus = "状态：" + (libraryData.IsOpen ? "开放中" : "已关闭");
                    LibName = "图书馆(室)名称：" + libraryData.Name;
                    LibFloor = "图书馆(室)楼层：" + libraryData.Floor;
                    LibAvailableSeats = "图书馆(室)余座：" + libraryData.SeatsInfo.AvailableSeats.ToString();

                    // 保存到Session
                    _sessionService.Cookie = Cookie;
                    _sessionService.CurrentLibrary = libraryData;
                    _sessionService.QueryLibInfoSyntax = libQuerySyntax;  // 保存查询语法

                    ShowLibrarySelection = false;
                    _notificationService.ShowSuccess("绑定成功", $"已成功绑定 {libraryData.Name} - {libraryData.Floor}");
                });
            }
            catch (GetLibInfoException ex)
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    LibStatus = $"状态：{ex.Message}";
                    LibName = "图书馆(室)名称：Error";
                    LibFloor = "图书馆(室)楼层：Error";
                    LibAvailableSeats = "图书馆(室)余座：Error";
                    _notificationService.ShowError("绑定失败", ex.Message);
                });
            }
        }

        /// <summary>
        /// 保存Cookie到文件（加密）
        /// </summary>
        [RelayCommand]
        private async Task SaveCookieAsync()
        {
            if (string.IsNullOrWhiteSpace(Cookie))
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    _notificationService.ShowError("保存Cookie失败", "Cookie为空"));
                return;
            }

            if (!Cookie.Contains("Authorization") || !Cookie.Contains("SERVERID"))
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    _notificationService.ShowWarning("保存Cookie失败", "当前Cookie不合法，不包含关键要素，禁止写入"));
                return;
            }

            try
            {
                // 在后台线程执行文件操作
                await Task.Run(() =>
                {
                    // 构造包含时间戳的Cookie数据
                    var cookieData = new
                    {
                        Cookie = Cookie,
                        SavedTime = DateTime.Now.ToString("o"), // ISO 8601格式
                        LibID = _sessionService.CurrentLibrary?.LibID ?? 0,
                        QuerySyntax = _sessionService.QueryLibInfoSyntax ?? ""
                    };

                    var jsonData = JsonConvert.SerializeObject(cookieData);

                    // 加密Cookie数据
                    var encryptedData = Encrypt.DES(jsonData, "ejianzqq");

                    // 保存到文件
                    var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    var appDirectory = System.IO.Path.Combine(appDataPath, "IGoLibrary");
                    if (!System.IO.Directory.Exists(appDirectory))
                    {
                        System.IO.Directory.CreateDirectory(appDirectory);
                    }

                    var filePath = System.IO.Path.Combine(appDirectory, "SavedCookie");
                    System.IO.File.WriteAllText(filePath, encryptedData);
                }).ConfigureAwait(false);

                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    _notificationService.ShowSuccess("保存Cookie成功", "已将Cookie加密保存至目录下的SavedCookie文件中"));
            }
            catch (Exception ex)
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    _notificationService.ShowError("保存Cookie失败", $"将Cookie写入文件时发生了错误: {ex.Message}"));
            }
        }

        /// <summary>
        /// 从文件读取Cookie（解密）
        /// </summary>
        [RelayCommand]
        private async Task LoadCookieAsync()
        {
            try
            {
                // 在后台线程执行文件操作
                var cookie = await Task.Run(() =>
                {
                    var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    var filePath = System.IO.Path.Combine(appDataPath, "IGoLibrary", "SavedCookie");

                    if (!System.IO.File.Exists(filePath))
                    {
                        throw new System.IO.FileNotFoundException("SavedCookie文件不存在");
                    }

                    var encryptedData = System.IO.File.ReadAllText(filePath);
                    var decryptedData = Decrypt.DES(encryptedData, "ejianzqq");

                    // 尝试解析为新格式(JSON)
                    try
                    {
                        var cookieData = JsonConvert.DeserializeObject<dynamic>(decryptedData);
                        return cookieData?.Cookie?.ToString() ?? decryptedData;
                    }
                    catch
                    {
                        // 如果解析失败,说明是旧格式,直接返回
                        return decryptedData;
                    }
                }).ConfigureAwait(false);

                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Cookie = cookie;
                    _notificationService.ShowSuccess("读取Cookie成功", "已将Cookie解密并读取至Cookie文本框中");
                });
            }
            catch (System.IO.FileNotFoundException)
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    _notificationService.ShowError("读取Cookie失败", "SavedCookie文件不存在"));
            }
            catch (Exception ex)
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    _notificationService.ShowError("读取Cookie失败", $"将Cookie从文件取出时发生了错误: {ex.Message}"));
            }
        }

        /// <summary>
        /// 启动时自动加载Cookie（如果有效期大于1小时）
        /// </summary>
        public async Task AutoLoadCookieOnStartupAsync()
        {
            try
            {
                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var filePath = System.IO.Path.Combine(appDataPath, "IGoLibrary", "SavedCookie");

                if (!System.IO.File.Exists(filePath))
                {
                    return; // 文件不存在，静默返回
                }

                // 在后台线程读取和解析Cookie数据
                var cookieInfo = await Task.Run(() =>
                {
                    var encryptedData = System.IO.File.ReadAllText(filePath);
                    var decryptedData = Decrypt.DES(encryptedData, "ejianzqq");

                    // 尝试解析为新格式(JSON)
                    try
                    {
                        var cookieData = JsonConvert.DeserializeObject<dynamic>(decryptedData);
                        var savedTimeStr = cookieData?.SavedTime?.ToString();
                        var savedDateTime = string.IsNullOrEmpty(savedTimeStr)
                            ? DateTime.MinValue
                            : DateTime.Parse(savedTimeStr);

                        return (
                            cookie: cookieData?.Cookie?.ToString() ?? "",
                            savedTime: savedDateTime,
                            libId: (int)(cookieData?.LibID ?? 0),
                            querySyntax: cookieData?.QuerySyntax?.ToString() ?? ""
                        );
                    }
                    catch
                    {
                        // 旧格式，不支持自动加载
                        return (cookie: "", savedTime: DateTime.MinValue, libId: 0, querySyntax: "");
                    }
                }).ConfigureAwait(false);

                var cookie = cookieInfo.cookie;
                var savedTime = cookieInfo.savedTime;
                var libId = cookieInfo.libId;
                var querySyntax = cookieInfo.querySyntax;

                // 检查Cookie是否为空
                if (string.IsNullOrWhiteSpace(cookie))
                {
                    return;
                }

                // 检查保存时间是否在1小时内
                var timeSinceLastSave = DateTime.Now - savedTime;
                if (timeSinceLastSave.TotalHours > 1)
                {
                    return; // 超过1小时，不自动加载
                }

                // 验证Cookie是否仍然有效
                try
                {
                    var allLibsSummary = await Task.Run(() =>
                        _getAllLibsSummaryService.GetAllLibsSummary(cookie, QueryAllLibsSummarySyntax))
                        .ConfigureAwait(false);

                    if (allLibsSummary?.libSummaries == null || allLibsSummary.libSummaries.Count == 0)
                    {
                        return; // Cookie无效
                    }

                    // Cookie有效，自动加载
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        Cookie = cookie;
                        _sessionService.Cookie = cookie;

                        // 如果有保存的图书馆ID和查询语法，尝试恢复图书馆信息
                        if (libId > 0 && !string.IsNullOrWhiteSpace(querySyntax))
                        {
                            try
                            {
                                var libraryData = await Task.Run(() =>
                                    _getLibInfoService.GetLibInfo(cookie, querySyntax))
                                    .ConfigureAwait(false);

                                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                                {
                                    LibID = libId;
                                    LibStatus = "状态：" + (libraryData.IsOpen ? "开放中" : "已关闭");
                                    LibName = "图书馆(室)名称：" + libraryData.Name;
                                    LibFloor = "图书馆(室)楼层：" + libraryData.Floor;
                                    LibAvailableSeats = "图书馆(室)余座：" + libraryData.SeatsInfo.AvailableSeats.ToString();

                                    _sessionService.CurrentLibrary = libraryData;
                                    _sessionService.QueryLibInfoSyntax = querySyntax;

                                    _notificationService.ShowSuccess("自动登录成功",
                                        $"已自动加载上次的Cookie和图书馆信息 ({libraryData.Name} - {libraryData.Floor})");
                                });
                            }
                            catch
                            {
                                // 图书馆信息加载失败，只显示Cookie已加载
                                _notificationService.ShowSuccess("自动登录成功",
                                    "已自动加载上次的Cookie，请重新绑定图书馆");
                            }
                        }
                        else
                        {
                            _notificationService.ShowSuccess("自动登录成功",
                                "已自动加载上次的Cookie，请绑定图书馆");
                        }
                    });
                }
                catch
                {
                    // Cookie验证失败，静默返回
                    return;
                }
            }
            catch
            {
                // 任何错误都静默处理，不影响启动
                return;
            }
        }

        #endregion
    }
}
