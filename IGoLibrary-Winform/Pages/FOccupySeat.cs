using IGoLibrary_Winform.Controller;
using IGoLibrary_Winform.CustomException;
using IGoLibrary_Winform.Data;
using IGoLibrary_Winform.Notify;
using Microsoft.Extensions.DependencyInjection;
using Sunny.UI;

namespace IGoLibrary_Winform.Pages
{
    public partial class FOccupySeat : UIPage
    {
        private readonly IGetReserveInfoService getReserveInfoService;
        private readonly ICancelReserveService cancelReserveService;
        private readonly IReserveSeatService reserveSeatService;
        private bool _occupyLocker = false;
        public bool _occupySeatSignal;
        public FOccupySeat()
        {
            InitializeComponent();
            using (var serviceProvider = MainForm.services.BuildServiceProvider())
            {
                getReserveInfoService = serviceProvider.GetRequiredService<IGetReserveInfoService>();
                cancelReserveService = serviceProvider.GetRequiredService<ICancelReserveService>();
                reserveSeatService = serviceProvider.GetRequiredService<IReserveSeatService>();
            }
            Control.CheckForIllegalCrossThreadCalls = false;
            uiTextBox_RealTimeData.FillColor = Color.FromArgb(243, 249, 255);
        }

        public void UpdateStatus()
        {
            try
            {
                ReserveInfo info = getReserveInfoService.GetReserveInfo(MainForm.authentication.Authenticator.Cookies, MainForm.authentication.Authenticator.Syntax.QueryReserveInfo);
                this.uiSymbolLabel_LibName.Text = info.LibName;
                this.uiSymbolLabel_SeatName.Text = "座位号：" + info.SeatKeyDta.Name;
                this.uiSymbolLabel_SeatKey.Text = "座位坐标：" + info.SeatKeyDta.Key;
                Toast.ShowNotifiy("刷新预约状态成功","已有预约，可以开始进行占座",Notifications.Wpf.NotificationType.Success);
                _occupyLocker = true; //说明已经有了预定好的座位 可以进行占座
            }
            catch(GetReserveInfoException ex)
            {
                this.uiSymbolLabel_LibName.Text = ex.Message;
                this.uiSymbolLabel_SeatName.Text = "请先预约好座位";
                this.uiSymbolLabel_SeatKey.Text = "才能使用占座功能";
                //Toast.ShowNotifiy("刷新预约状态失败", $"失败原因：\n{ex.Message}", Notifications.Wpf.NotificationType.Error);
            }
        }

        private void uiSymbolButton_UpdateStatus_Click(object sender, EventArgs e)
        {
            if(MainForm.authentication.IsAuthenticated == true)
            {
                UpdateStatus();
            }
            else
            {
                Toast.ShowNotifiy("刷新预约状态失败", "需要先在数据源页面绑定好图书馆才可以刷新预约状态", Notifications.Wpf.NotificationType.Error);
            }
        }

        private void uiSwitch_OccupySeat_ActiveChanged(object sender, EventArgs e)
        {
            if(uiSwitch_OccupySeat.Active == true)
            {
                uiSymbolButton_UpdateStatus.Enabled = false;
                if (_occupyLocker == true)
                {
                    _occupySeatSignal = true;
                    Thread occupySeatThread = new Thread(() =>
                    {
                        while (_occupySeatSignal)
                        {
                            ReserveInfo info = new ReserveInfo();
                            int _getInfoRetryCount = 0;
                            int _cancelReserveRetryCount = 0;
                            int _reReserveRetryCount = 0;
                        GetInfo: try
                            {
                                if (_occupySeatSignal == false)
                                {
                                    uiTextBox_RealTimeData.AppendText($"[{DateTime.Now.ToString("T")}]已获取到终止信号，占座功能将被完全终止" + Environment.NewLine);
                                    break;
                                }//每个功能之间都加入验证，让功能更快地终止
                                if (_getInfoRetryCount <= 2)
                                {
                                    info = getReserveInfoService.GetReserveInfo(MainForm.authentication.Authenticator.Cookies, MainForm.authentication.Authenticator.Syntax.QueryReserveInfo);
                                    uiTextBox_RealTimeData.AppendText($"[{DateTime.Now.ToString("T")}]获取预约信息成功" + Environment.NewLine);
                                    _getInfoRetryCount = 0;
                                }
                                else
                                {
                                    uiTextBox_RealTimeData.AppendText($"[{DateTime.Now.ToString("T")}]获取预约信息失败重试次数已达上限，占座功能将被完全终止" + Environment.NewLine);
                                    _occupySeatSignal = false;
                                    break;
                                }
                            }
                            catch(GetReserveInfoException ex)
                            {
                                if (_occupySeatSignal == false)
                                {
                                    uiTextBox_RealTimeData.AppendText($"[{DateTime.Now.ToString("T")}]已获取到终止信号，占座功能将被完全终止" + Environment.NewLine);
                                    break;
                                }//每个功能之间都加入验证，让功能更快地终止
                                uiTextBox_RealTimeData.AppendText($"[{DateTime.Now.ToString("T")}]获取预约信息失败：{ex.Message}，现在开始第{++_getInfoRetryCount}次重试" + Environment.NewLine);
                                goto GetInfo;
                            }
                            if(_occupySeatSignal == false) 
                            {
                                uiTextBox_RealTimeData.AppendText($"[{DateTime.Now.ToString("T")}]已获取到终止信号，占座功能将被完全终止" + Environment.NewLine);
                                break;
                            }//每个功能之间都加入验证，让功能更快地终止
                        TimeHandle:
                            {
                                if (_occupySeatSignal == false)
                                {
                                    uiTextBox_RealTimeData.AppendText($"[{DateTime.Now.ToString("T")}]已获取到终止信号，占座功能将被完全终止" + Environment.NewLine);
                                    break;
                                }//每个功能之间都加入验证，让功能更快地终止
                                TimeSpan ts = info.ExpiredTime - DateTime.Now;
                                if(ts.TotalSeconds > 60) 
                                {
                                    //如果还大于60秒，则延迟10秒后进入下一次循环。如果小于60秒了则取消预定
                                    uiTextBox_RealTimeData.AppendText($"[{DateTime.Now.ToString("T")}]距离预约过期还剩{(int)ts.TotalSeconds}秒，10秒后继续检测" + Environment.NewLine);
                                    Thread.Sleep(10000);
                                    continue; //直接进入下一次循环
                                }
                            }
                        CancelReserve: try
                            {
                                if (_occupySeatSignal == false)
                                {
                                    uiTextBox_RealTimeData.AppendText($"[{DateTime.Now.ToString("T")}]已获取到终止信号，占座功能将被完全终止" + Environment.NewLine);
                                    break;
                                }//每个功能之间都加入验证，让功能更快地终止
                                string errorMessage = "By EJianZQ";
                                if (_cancelReserveRetryCount <= 2)
                                {
                                    if(cancelReserveService.CancelReserve(MainForm.authentication.Authenticator.Cookies, MainForm.authentication.Authenticator.Syntax.CancelReserve.Replace("ReplaceMe", info.Token), ref errorMessage) == true)
                                    {
                                        uiTextBox_RealTimeData.AppendText($"[{DateTime.Now.ToString("T")}]取消预约成功，将在1分钟后重新预约" + Environment.NewLine);
                                        Thread.Sleep(61000); //延迟61秒后重新预约该座位
                                        _cancelReserveRetryCount = 0;
                                    }
                                    else
                                    {
                                        uiTextBox_RealTimeData.AppendText($"[{DateTime.Now.ToString("T")}]取消预约失败：{errorMessage}。将在10秒后重试最多3次，即将开始第{++_getInfoRetryCount}次尝试" + Environment.NewLine);
                                        goto CancelReserve;
                                    }
                                }
                                else
                                {
                                    uiTextBox_RealTimeData.AppendText($"[{DateTime.Now.ToString("T")}]取消预约失败重试次数已达上限，占座功能将被完全终止" + Environment.NewLine);
                                    _occupySeatSignal = false;
                                    break;
                                }
                            }
                            catch(CancelReserveException ex)
                            {
                                if (_occupySeatSignal == false)
                                {
                                    uiTextBox_RealTimeData.AppendText($"[{DateTime.Now.ToString("T")}]已获取到终止信号，占座功能将被完全终止" + Environment.NewLine);
                                    break;
                                }//每个功能之间都加入验证，让功能更快地终止
                                uiTextBox_RealTimeData.AppendText($"[{DateTime.Now.ToString("T")}]取消预约失败，现在开始第{++_getInfoRetryCount}次重试" + Environment.NewLine);
                                goto CancelReserve;
                            }
                        ReReserve: try
                            {
                                if (_occupySeatSignal == false)
                                {
                                    uiTextBox_RealTimeData.AppendText($"[{DateTime.Now.ToString("T")}]已获取到终止信号，占座功能将被完全终止" + Environment.NewLine);
                                    break;
                                }//每个功能之间都加入验证，让功能更快地终止
                                if (_reReserveRetryCount <= 2)
                                {
                                    if(reserveSeatService.ReserveSeat(MainForm.authentication.Authenticator.Cookies, MainForm.authentication.Authenticator.Syntax.ReserveSeat.Replace("ReplaceMeBySeatKey", info.SeatKeyDta.Key)) == true)
                                    {
                                        uiTextBox_RealTimeData.AppendText($"[{DateTime.Now.ToString("T")}]重新预约座位成功，延迟5秒后重新开始监控" + Environment.NewLine);
                                        Thread.Sleep(5000);
                                    }
                                    else
                                    {
                                        uiTextBox_RealTimeData.AppendText($"[{DateTime.Now.ToString("T")}]取消预约失败且原因未知。将在10秒后重试最多3次，即将开始第{++_getInfoRetryCount}次尝试" + Environment.NewLine);
                                        goto ReReserve;
                                    }
                                }
                                else
                                {
                                    uiTextBox_RealTimeData.AppendText($"[{DateTime.Now.ToString("T")}]重新预约座位失败重试次数已达上限，占座功能将被完全终止" + Environment.NewLine);
                                    _occupySeatSignal = false;
                                    break;
                                }
                            }
                            catch(ReserveSeatException ex)
                            {
                                if (_occupySeatSignal == false)
                                {
                                    uiTextBox_RealTimeData.AppendText($"[{DateTime.Now.ToString("T")}]已获取到终止信号，占座功能将被完全终止" + Environment.NewLine);
                                    break;
                                }//每个功能之间都加入验证，让功能更快地终止
                                uiTextBox_RealTimeData.AppendText($"[{DateTime.Now.ToString("T")}]重新预约座位失败：{ex.Message}，现在开始第{++_getInfoRetryCount}次重试" + Environment.NewLine);
                                goto ReReserve;
                            }
                        }
                        _occupyLocker = false;
                        Toast.ShowNotifiy("占座功能提示", "每次关闭占座功能后都需要重新刷新预约状态才能再次开启功能", Notifications.Wpf.NotificationType.Warning);
                    });
                    occupySeatThread.Start();
                }
                else
                {
                    Toast.ShowNotifiy("占座开启失败","需要先预约好座位才能开始占座\n如已预约请点击刷新预约状态",Notifications.Wpf.NotificationType.Error);
                    uiSwitch_OccupySeat.Active = false;
                }
            }
            else
            {
                _occupySeatSignal = false;
                _occupyLocker = false;
                Toast.ShowNotifiy("占座功能提示","每次关闭占座功能后都需要重新刷新预约状态才能再次开启功能",Notifications.Wpf.NotificationType.Warning);
                uiSymbolButton_UpdateStatus.Enabled = true;
            }
        }
        public static DateTime ConvertToDateTime(long timestamp)
        {
            long begtime = timestamp * 10000000;
            DateTime dt_1970 = new DateTime(1970, 1, 1, 8, 0, 0);
            long tricks_1970 = dt_1970.Ticks;//1970年1月1日刻度
            long time_tricks = tricks_1970 + begtime;//日志日期刻度
            DateTime dt = new DateTime(time_tricks);//转化为DateTime
            return dt;
        }

        private void uiTextBox_RealTimeData_TextChanged(object sender, EventArgs e)
        {
            if (uiTextBox_RealTimeData.Text.Length >= 20000)
            {
                uiTextBox_RealTimeData.Clear();
                uiTextBox_RealTimeData.AppendText("由于内容过多防止内存泄露，已清空并重新开始记录" + Environment.NewLine);
            }
        }

        private void uiSymbolButton_Help_Click(object sender, EventArgs e)
        {
            UIMessageDialog.ShowMessageDialog("使用步骤：\n1.自己预约好座位或使用抢座功能抢到座位\n2.刷新预约状态直到预约座位信息处显示正确的信息\n3.打开占座开关，保持软件运行状态即可\n更多详细说明和介绍可访问：e剑终情.com", "占座使用帮助", false, Style,false,false);
        }
    }
}