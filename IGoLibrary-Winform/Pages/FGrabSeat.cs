using IGoLibrary_Winform.Data;
using IGoLibrary_Winform.Notify;
using Sunny.UI;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Notifications.Wpf;
using Microsoft.Extensions.DependencyInjection;
using IGoLibrary_Winform.CustomException;
using IGoLibrary_Winform.Controller;

namespace IGoLibrary_Winform.Pages
{
    public partial class FGrabSeat : UIPage
    {
        public List<SeatKeyData> waitingGrabSeats = new List<SeatKeyData>();
        public bool _grabSeatsSignal = false;
        private readonly IGetLibInfoService getLibInfoService;
        private readonly IReserveSeatService reserveSeatService;
        public FGrabSeat()
        {
            InitializeComponent();
            using (var serviceProvider = MainForm.services.BuildServiceProvider())
            {
                getLibInfoService = serviceProvider.GetRequiredService<IGetLibInfoService>();
                reserveSeatService = serviceProvider.GetRequiredService<IReserveSeatService>();
            }
            Control.CheckForIllegalCrossThreadCalls = false;
            uiTextBox_RealTimeData.FillColor = Color.FromArgb(243, 249, 255);
            uiListBox_SelectedGrabSeats.FillColor = Color.FromArgb(243, 249, 255);
            uiDataGridView_SeatInfo.AddColumn("座位号", "Name");
            uiDataGridView_SeatInfo.AddColumn("座位状态", "Status");
            uiDataGridView_SeatInfo.AddColumn("座位坐标", "Key");
            uiComboBox_GrabSeatMode.SelectedIndex= 2;
            Thread thread = new Thread(() =>
            {
                uiTextBox_RealTimeData.AppendText("test");
            });
            //thread.Start();
        }

        public void UpdateSeatsGridView(List<SeatsItem> seats)
        {
            uiDataGridView_SeatInfo.ClearAll();
            var SeatGridViewDataList = new List<SeatKeyData>();
            for (int i = 0; i < seats.Count; i++)
            {
                var temp = new SeatKeyData() { Name = seats[i].name, Status = seats[i].status ? "有人" : "无人", Key = seats[i].key };
                SeatGridViewDataList.Add(temp);
            }
            var OrderedSeatGridViewDataList = SeatGridViewDataList.OrderBy(x => x.Name.ToInt()); //按照Seat的Name(座位号)进行排序
            uiDataGridView_SeatInfo.DataSource = OrderedSeatGridViewDataList.ToList<SeatKeyData>();
            //uiSymbolLabel_GridViewUpdateTime.Text = $"数据更新时间：{MainForm.authentication.LastAuthenticationTime.ToString("T")}";
        }

        private void uiDataGridView_SeatInfo_SelectIndexChange(object sender, int index)
        {
            
        }

        private void uiSymbolButton_AddGrabList_Click(object sender, EventArgs e)
        {
            if(uiDataGridView_SeatInfo.Rows.Count >= 2)
            {
                var allSeatsList = (List<SeatKeyData>)uiDataGridView_SeatInfo.DataSource;
                waitingGrabSeats = new List<SeatKeyData>();
                uiListBox_SelectedGrabSeats.Items.Clear();
                for (int i = 0;i < uiDataGridView_SeatInfo.SelectedRows.Count; i++)
                {
                    waitingGrabSeats.Add(allSeatsList[uiDataGridView_SeatInfo.SelectedRows[i].Index]);
                    uiListBox_SelectedGrabSeats.Items.Add(allSeatsList[uiDataGridView_SeatInfo.SelectedRows[i].Index].Name + "号");
                    
                }
                Toast.ShowNotifiy("添加抢座列表成功",$"已成功添加{waitingGrabSeats.Count}个座位至抢座列表",NotificationType.Success);
            }
            else
            {
                Toast.ShowNotifiy("添加抢座列表失败","座位数据不存在",NotificationType.Error);
            }
        }

        private void uiSwitch_GrabSeatSwitch_ActiveChanged(object sender, EventArgs e)
        {
            if (uiSwitch_GrabSeatSwitch.Active == true)
            {
                if(waitingGrabSeats.Count > 0)
                {
                    _grabSeatsSignal = true;
                    int count = 1;
                    Random rd = new Random();
                    Thread grabSeatThread = new Thread(() =>
                    {
                        if (uiTimePicker_TimingTime.Text != "00:00:00") //先检查是否设置了定时，如果设置了定时就用循环卡住不让程序往下走进监控环节
                        {
                            while (_grabSeatsSignal)
                            {
                                var settingTime = DateTime.Parse(uiTimePicker_TimingTime.Text).TimeOfDay;
                                var nowTime = DateTime.Now.TimeOfDay;
                                TimeSpan interval = (settingTime - nowTime).Duration(); // 取开始和现在时间的时间间隔绝对值
                                TimeSpan tenSeconds = new TimeSpan(0, 0, 10); //创建一个10秒的时间间隔
                                if(interval < tenSeconds)
                                {
                                    if (nowTime >= settingTime)
                                    {
                                        uiTextBox_RealTimeData.AppendText("已到抢座时间，开始监控并抢座" + Environment.NewLine);
                                        break;
                                    }
                                    else
                                    {
                                        uiTextBox_RealTimeData.AppendText("倒计时已进入10秒内，即将开始抢座" + Environment.NewLine);
                                        Thread.Sleep(1000);
                                    }
                                }
                                else
                                {
                                    uiTextBox_RealTimeData.AppendText("由于设定了定时抢座，当前未到设定时间" + Environment.NewLine);
                                    Thread.Sleep(1000);
                                }
                            }
                        }
                        while (_grabSeatsSignal)
                        {
                            try
                            {
                                var templibraryData = getLibInfoService.GetLibInfo(MainForm.authentication.Authenticator.Cookies, MainForm.authentication.Authenticator.Syntax.QueryLibInfo);
                                uiTextBox_RealTimeData.AppendText($"[第{count}次][{DateTime.Now.ToString("T")}]获取座位信息成功" + Environment.NewLine);
                                foreach (var singleKeyData in templibraryData.GetSelectedSeatsKeyData(waitingGrabSeats))
                                {
                                    if (singleKeyData.Status == "无人")
                                    {
                                        uiTextBox_RealTimeData.AppendText($"[第{count}次][{DateTime.Now.ToString("T")}]{singleKeyData.Name}号座位无人，开始预定该位置" + Environment.NewLine);
                                        Thread.Sleep(100);
                                        if(reserveSeatService.ReserveSeat(MainForm.authentication.Authenticator.Cookies,MainForm.authentication.Authenticator.Syntax.ReserveSeat.Replace("ReplaceMeBySeatKey",singleKeyData.Key)) == true)
                                        {
                                            uiTextBox_RealTimeData.AppendText($"[第{count}次][{DateTime.Now.ToString("T")}]{singleKeyData.Name}号座位预定成功，结束监控" + Environment.NewLine);
                                        }
                                        else
                                        {
                                            uiTextBox_RealTimeData.AppendText($"[第{count}次][{DateTime.Now.ToString("T")}]{singleKeyData.Name}号座位预定失败，原因未知，结束监控" + Environment.NewLine); //这个地方可能永远也用不到 因为如果预定失败的话按照协议的规则应该是返回错误原因的，不会没有错误原因
                                        }
                                        uiSwitch_GrabSeatSwitch.Active = false;
                                        _grabSeatsSignal = false;
                                        break;
                                    }
                                    else
                                        uiTextBox_RealTimeData.AppendText($"[第{count}次][{DateTime.Now.ToString("T")}]{singleKeyData.Name}号座位:有人" + Environment.NewLine);
                                }
                                count++;
                                if(count % 50 == 0)
                                {
                                    uiTextBox_RealTimeData.AppendText($"[第{count}次][{DateTime.Now.ToString("T")}]已运行一个大循环，额外延迟5~10秒" + Environment.NewLine);
                                    Thread.Sleep(rd.Next(5000,10000));
                                }
                                //根据模式不同，延时时间不同
                                Thread.Sleep(uiComboBox_GrabSeatMode.SelectedIndex == 0 ? 1000 : uiComboBox_GrabSeatMode.SelectedIndex == 1 ? rd.Next(4000,8000) : 5000);
                            }
                            catch (GetLibInfoException ex)
                            {
                                _grabSeatsSignal = false; //抛异常了可能是cookie过期了或者其他，另行判断
                                uiTextBox_RealTimeData.AppendText($"[第{count}次][{DateTime.Now.ToString("T")}]出现异常，异常信息:{ex.Message}，需要重新填写Cookie并验证后再使用" + Environment.NewLine);
                                Toast.ShowNotifiy("获取座位信息时出现致命错误",$"错误信息：{ex.Message}",NotificationType.Warning);
                                uiSwitch_GrabSeatSwitch.Active = false;
                            }
                            catch(ReserveSeatException ex)
                            {
                                _grabSeatsSignal = false; //抛异常了可能是cookie过期了或者其他，另行判断
                                uiTextBox_RealTimeData.AppendText($"[第{count}次][{DateTime.Now.ToString("T")}]预定座位失败，失败信息:{ex.Message}，结束监控" + Environment.NewLine);
                                Toast.ShowNotifiy("预定座位信息时出现错误", $"错误信息：{ex.Message}", NotificationType.Warning);
                                uiSwitch_GrabSeatSwitch.Active = false;
                            }
                        }
                    });
                    grabSeatThread.Start();
                }
                else
                {
                    _grabSeatsSignal = false;
                    uiSwitch_GrabSeatSwitch.Active = false;
                    Toast.ShowNotifiy("开始抢座失败","未设置待抢座位，请设置后重试",NotificationType.Error);
                }
            }
            else
                _grabSeatsSignal = false;
        }

        private void uiTextBox_RealTimeData_TextChanged(object sender, EventArgs e)
        {
            if(uiTextBox_RealTimeData.Text.Length >= 15000)
            {
                uiTextBox_RealTimeData.Clear();
                uiTextBox_RealTimeData.AppendText("由于内容过多防止内存泄露，已清空并重新开始记录" + Environment.NewLine);
            }
        }
    }
}
