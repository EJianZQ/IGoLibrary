using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
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
        private bool _occupyLocker = false;
        private bool _occupySeatSignal;
        public FOccupySeat()
        {
            InitializeComponent();
            using (var serviceProvider = MainForm.services.BuildServiceProvider())
            {
                getReserveInfoService = serviceProvider.GetRequiredService<IGetReserveInfoService>();
            }
            Control.CheckForIllegalCrossThreadCalls = false;
        }

        public void UpdateStatus()
        {
            try
            {
                ReserveInfo info = getReserveInfoService.GetReserveInfo(MainForm.authentication.Authenticator.Cookies, MainForm.authentication.Authenticator.Syntax.QueryReserveInfo);
                this.uiSymbolLabel_LibName.Text = info.LibName;
                this.uiSymbolLabel_SeatName.Text = info.SeatKeyDta.Name;
                this.uiSymbolLabel_SeatKey.Text = info.SeatKeyDta.Key + "坐标";
                _occupyLocker = true; //说明已经有了预定好的座位 可以进行占座
            }
            catch(GetReserveInfoException ex)
            {
                this.uiSymbolLabel_LibName.Text = ex.Message;
                this.uiSymbolLabel_SeatName.Text = "Error";
                this.uiSymbolLabel_SeatKey.Text = "Error";
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
                if(_occupyLocker == true)
                {
                    _occupySeatSignal = true;
                    Thread occupySeatThread = new Thread(() =>
                    {
                        while (_occupySeatSignal)
                        {
                            ReserveInfo info = new ReserveInfo();
                            int _getInfoRetryCount = 0;
                        GetInfo: try
                            {
                                if(_getInfoRetryCount <= 2)
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
                                uiTextBox_RealTimeData.AppendText($"[{DateTime.Now.ToString("T")}]获取预约信息失败，现在开始第{++_getInfoRetryCount}次重试" + Environment.NewLine);
                                goto GetInfo;
                            }
                            if(_occupySeatSignal == false) //每个功能之间都加入验证，让功能更快地终止
                            {
                                uiTextBox_RealTimeData.AppendText($"[{DateTime.Now.ToString("T")}]已获取到终止信号，占座功能将被完全终止" + Environment.NewLine);
                                break;
                            }
                        TimeHandle:
                            {
                                TimeSpan ts = info.ExpiredTime - DateTime.Now;
                                if(ts.TotalSeconds < 60) //如果还大于60秒，则延迟10秒后进入下一次循环。如果小于60秒了则取消预定
                                {

                                }
                                else
                                {
                                    uiTextBox_RealTimeData.AppendText($"[{DateTime.Now.ToString("T")}]距离预约过期还剩{ts.TotalSeconds}秒，10秒后继续检测" + Environment.NewLine);
                                    Thread.Sleep(10000);
                                    continue; //直接进入下一次循环
                                }
                            }
                        }
                    });
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
    }
}
