using IGoLibrary_Winform.CustomException;
using Microsoft.Extensions.DependencyInjection;
using Sunny.UI;
using System.Text.RegularExpressions;
using IGoLibrary_Winform.Notify;
using Notifications.Wpf;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using IGoLibrary_Winform.Controller;
using IGoLibrary_Winform.Crypt;
using System;
using System.IO;

namespace IGoLibrary_Winform.Pages
{
    public partial class FDataSource : UIPage
    {
        private readonly IGetLibInfoService getLibInfoService;
        private readonly IGetCookieService getCookieService;
        private readonly IGetAllLibsSummaryService getAllLibsSummaryService;
        private MainForm mainForm;
        public FDataSource(MainForm mainForm)
        {
            InitializeComponent();
            this.uiTextBox_Cookies.FillColor = Color.FromArgb(243, 249, 255);
            this.uiTextBox_QueryLibInfoSyntax.FillColor = Color.FromArgb(243, 249, 255);
            this.uiTextBox_CodeSourceURL.FillColor = Color.FromArgb(243, 249, 255);
            using (var serviceProvider = MainForm.services.BuildServiceProvider())
            {
                getLibInfoService = serviceProvider.GetRequiredService<IGetLibInfoService>();
                getCookieService = serviceProvider.GetRequiredService<IGetCookieService>();
                getAllLibsSummaryService = serviceProvider.GetRequiredService<IGetAllLibsSummaryService>();
            }

            this.mainForm = mainForm;
        }

        private void uiSymbolButton_Verify_Click(object sender, EventArgs e)
        {
            uiSymbolButton_BindLibrary.Enabled = false;
            if (uiTextBox_Cookies.Text.Contains("Authorization")  && uiTextBox_Cookies.Text.Contains("SERVERID"))
            {
                try
                {
                    var allLibsSummary = getAllLibsSummaryService.GetAllLibsSummary(this.uiTextBox_Cookies.Text,this.uiTextBox_QueryAllLibsSummarySyntax.Text);
                    int libSelectedIndex = 0;
                    List<string> items = new List<string>();
                    foreach (var item in allLibsSummary.libSummaries)
                    {
                        items.Add(string.Format("{0} - {1} - {2}", item.Name,item.Floor,item.IsOpen ? "开放" :"关闭"));
                    }
                    if (this.ShowSelectDialog(ref libSelectedIndex, items,"选择想要绑定的图书馆","绑定成功后在抢座页面选择待抢座位并监控"))
                    {
                        var libraryData = getLibInfoService.GetLibInfo(this.uiTextBox_Cookies.Text, this.uiTextBox_QueryLibInfoSyntax.Text.Replace("ReplaceMe", allLibsSummary.libSummaries[libSelectedIndex].LibID.ToString()));
                        uiIntegerUpDown_LibID.Value = allLibsSummary.libSummaries[libSelectedIndex].LibID;
                        uiSymbolLabel_LibStatus.Text = "状态：" + (libraryData.IsOpen ? "开放中" : "已关闭");
                        uiSymbolLabel_LibName.Text = "图书馆(室)名称：" + libraryData.Name;
                        uiSymbolLabel_LibFloor.Text = "图书馆(室)楼层：" + libraryData.Floor;
                        uiSymbolLabel_LibAvailableSeatsNum.Text = "图书馆(室)余座：" + libraryData.SeatsInfo.AvailableSeats.ToString();

                        MainForm.authentication.IsAuthenticated = true;
                        MainForm.authentication.LastAuthenticationTime = DateTime.Now;
                        MainForm.authentication.LatestLibraryData = libraryData;
                        MainForm.authentication.Authenticator.Cookies = this.uiTextBox_Cookies.Text;
                        MainForm.authentication.Authenticator.LibID = libraryData.LibID;
                        MainForm.authentication.Authenticator.Syntax.QueryLibInfo = this.uiTextBox_QueryLibInfoSyntax.Text.Replace("ReplaceMe", libraryData.LibID.ToString());
                        MainForm.authentication.Authenticator.Syntax.ReserveSeat = this.uiTextBox_ReserveSeatSyntax.Text.Replace("ReplaceMeByLibID", libraryData.LibID.ToString());

                        //向抢座页面的座位信息列表添加数据
                        var grabSeatPage = mainForm.GetPage<FGrabSeat>();
                        if (grabSeatPage != null)
                        {
                            grabSeatPage.UpdateSeatsGridView(libraryData.Seats);
                            grabSeatPage.uiSwitch_GrabSeatSwitch.Enabled = true;
                        }
                    }
                }
                catch (GetLibInfoException ex)
                {
                    uiSymbolLabel_LibStatus.Text = $"状态：{ex.Message}";
                    uiSymbolLabel_LibName.Text = "图书馆(室)名称：Error";
                    uiSymbolLabel_LibFloor.Text = "图书馆(室)楼层：Error";
                    uiSymbolLabel_LibAvailableSeatsNum.Text = "图书馆(室)余座：Error";
                }
            }
            else
            {
                Toast.ShowNotifiy("Cookies验证失败", "Cookies不合法，不包含关键要素", NotificationType.Error);
            }
            uiSymbolButton_BindLibrary.Enabled = true;
        }

        private void uiSymbolButton_SaveDataSource_Click(object sender, EventArgs e)
        {
            if (uiTextBox_Cookies.Text.Contains("Authorization") && uiTextBox_Cookies.Text.Contains("SERVERID"))
            {
                try
                {
                    File.WriteAllText("SavedCookie", Encrypt.DES(uiTextBox_Cookies.Text, "ejianzqq"));
                    Toast.ShowNotifiy("保存Cookie成功","已将Cookie加密保存至目录下的SavedCookie文件中",NotificationType.Success);
                }
                catch
                {
                    Toast.ShowNotifiy("保存Cookie失败","将Cookie写入文件时发生了错误",NotificationType.Error);
                }
            }
            else
                Toast.ShowNotifiy("保存Cookie失败", "当前Cookie不合法，不包含关键要素，禁止写入", NotificationType.Warning);
        }

        private void uiSymbolButton_ReadDataSource_Click(object sender, EventArgs e)
        {
            try
            {
                uiTextBox_Cookies.Text = Decrypt.DES(File.ReadAllText("SavedCookie"), "ejianzqq");
                Toast.ShowNotifiy("读取Cookie成功", "已将Cookie解密并读取至Cookie文本框中", NotificationType.Success);
            }
            catch
            {
                Toast.ShowNotifiy("读取Cookie失败", "将Cookie从文件取出时发生了错误", NotificationType.Error);
            }
        }

        private void uiSymbolButton_GetCookie_Click(object sender, EventArgs e)
        {
            if(Regex.IsMatch(uiTextBox_CodeSourceURL.Text, @".*wechat\.v2\.traceint\.com\/index\.php\/graphql\/\?operationName=index&query=query.*&code=.{32}&state=(0|1)"))
            {
                var match = Regex.Match(uiTextBox_CodeSourceURL.Text, @"code=.{32}");
                if(match.Success)
                {
                    try
                    {
                        uiTextBox_Cookies.Text = getCookieService.GetCookie(match.Value.Replace("code=", string.Empty));
                        uiTabControl_DataSource.SelectedIndex = 0;
                        Toast.ShowNotifiy("获取Cookies成功","已自动填写Cookie并跳转至验证页面，请点击\"验证\"按钮",NotificationType.Success);
                    }
                    catch(GetCookieException ex)
                    {
                        Toast.ShowNotifiy("获取Cookie失败", ex.Message, NotificationType.Error);
                    }
                }
                else
                    Toast.ShowNotifiy("获取Cookie失败", "从链接中匹配出code失败", NotificationType.Error);
            }
            else
            {
                Toast.ShowNotifiy("获取Cookie失败","提供的含code链接格式有误，不符合格式规范",NotificationType.Error);
            }
        }
    }
}