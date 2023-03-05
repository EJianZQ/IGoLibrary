using IGoLibrary_Winform.CustomException;
using Microsoft.Extensions.DependencyInjection;
using Sunny.UI;
using System.Text.RegularExpressions;
using IGoLibrary_Winform.Notify;
using Notifications.Wpf;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace IGoLibrary_Winform.Pages
{
    public partial class FDataSource : UIPage
    {
        private readonly IGetLibInfoService getLibInfoService;
        private MainForm mainForm;
        public FDataSource(MainForm mainForm)
        {
            InitializeComponent();
            this.uiTextBox_Cookies.FillColor = Color.FromArgb(243, 249, 255);
            this.uiTextBox_QueryLibInfoSyntax.FillColor = Color.FromArgb(243, 249, 255);
            this.uiTextBox_Session.FillColor = Color.FromArgb(243, 249, 255);
            using (var serviceProvider = MainForm.services.BuildServiceProvider())
            {
                getLibInfoService = serviceProvider.GetRequiredService<IGetLibInfoService>();
            }

            this.mainForm = mainForm;
        }

        private void uiSymbolButton_Verify_Click(object sender, EventArgs e)
        {
            uiSymbolButton_Verify.Enabled = false;
            if (uiTextBox_Cookies.Text.Contains("Hm_lvt")  && uiTextBox_Cookies.Text.Contains("SERVERID"))
            {
                try
                {
                    var libraryData = getLibInfoService.GetLibInfo(this.uiTextBox_Cookies.Text, this.uiTextBox_QueryLibInfoSyntax.Text.Replace("ReplaceMe", uiIntegerUpDown_LibID.Value.ToString()));
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
                    if(grabSeatPage != null)
                    {
                        grabSeatPage.UpdateSeatsGridView(libraryData.Seats);
                        grabSeatPage.uiSwitch_GrabSeatSwitch.Enabled= true;
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
            uiSymbolButton_Verify.Enabled = true;
        }

        private void uiSymbolButton_SaveDataSource_Click(object sender, EventArgs e)
        {

        }

        private void uiSymbolButton_ReadDataSource_Click(object sender, EventArgs e)
        {

        }

        private void uiSymbolButton_AutoIdentify_Click(object sender, EventArgs e)
        {
            uiSymbolButton_AutoIdentify.Enabled = false;
            if (uiTextBox_Session.Text.Contains("libId") && uiTextBox_Session.Text.Contains("Cookie:"))
            {
                Match match = Regex.Match(uiTextBox_Session.Text, @"libId"":\d{6,8}");
                if (match.Success)
                {
                    int libID = match.Value.Replace(@"libId"":", string.Empty).ToInt();
                    string header = uiTextBox_Session.Text.Substring(0, (uiTextBox_Session.Text.IndexOf(@"{""operationName")));
                    var headerArray = header.Split(Environment.NewLine);
                    foreach (var headerSingle in headerArray)
                    {
                        if (headerSingle.Contains("Cookie"))
                        {
                            if (headerSingle.Contains("Hm_lvt")&& headerSingle.Contains("Hm_lpvt") && headerSingle.Contains("SERVERID"))
                            {
                                uiTextBox_Cookies.Text = headerSingle.Replace(@"Cookie: ", string.Empty);
                                uiIntegerUpDown_LibID.Value = libID;
                                Toast.ShowNotifiy("自动识别成功","已自动填写Cookie和LibID并返回验证界面",NotificationType.Success);
                                uiTabControl_DataSource.SelectedIndex = 0;
                                uiSymbolButton_AutoIdentify.Enabled = true;
                                return;
                            }
                            else
                            {
                                Toast.ShowNotifiy("自动识别失败", "解析后的Cookie不合法，不包含关键要素", NotificationType.Error);
                                uiSymbolButton_AutoIdentify.Enabled = true;
                                return;
                            }
                        }
                    }
                    Toast.ShowNotifiy("自动识别失败", "解析后的文本未寻找到Cookie", NotificationType.Error);
                }
                else
                {
                    Toast.ShowNotifiy("自动识别失败", "该Session中不包含LibID", NotificationType.Error);
                }
            }
            else
            {
                Toast.ShowNotifiy("自动识别失败", "Session不合法，不包含关键要素", NotificationType.Error);
            }
            uiSymbolButton_AutoIdentify.Enabled= true;
        }
    }
}