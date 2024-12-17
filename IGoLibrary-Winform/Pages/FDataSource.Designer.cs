namespace IGoLibrary_Winform.Pages
{
    partial class FDataSource
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(FDataSource));
            uiTabControl_DataSource = new Sunny.UI.UITabControl();
            tabPage_VerifyCookie = new TabPage();
            uiTitlePanel_Operation1 = new Sunny.UI.UITitlePanel();
            uiSymbolButton_ReadDataSource = new Sunny.UI.UISymbolButton();
            uiSymbolButton_BindLibrary = new Sunny.UI.UISymbolButton();
            uiSymbolButton_SaveDataSource = new Sunny.UI.UISymbolButton();
            uiTitlePanel_LibID = new Sunny.UI.UITitlePanel();
            uiIntegerUpDown_LibID = new Sunny.UI.UIIntegerUpDown();
            uiTitlePanel_LibInfo = new Sunny.UI.UITitlePanel();
            uiSymbolLabel_LibAvailableSeatsNum = new Sunny.UI.UISymbolLabel();
            uiSymbolLabel_LibFloor = new Sunny.UI.UISymbolLabel();
            uiSymbolLabel_LibName = new Sunny.UI.UISymbolLabel();
            uiSymbolLabel_LibStatus = new Sunny.UI.UISymbolLabel();
            uiTitlePanel_Cookie = new Sunny.UI.UITitlePanel();
            uiTextBox_Cookies = new Sunny.UI.UITextBox();
            tabPage_GetCookie = new TabPage();
            uiTitlePanel1 = new Sunny.UI.UITitlePanel();
            uiLabel1 = new Sunny.UI.UILabel();
            pictureBox_QR = new PictureBox();
            uiTitlePanel_Operation2 = new Sunny.UI.UITitlePanel();
            uiSymbolButton_GetCookie = new Sunny.UI.UISymbolButton();
            uiTitlePanel_CodeSourceURL = new Sunny.UI.UITitlePanel();
            uiTextBox_CodeSourceURL = new Sunny.UI.UITextBox();
            tabPage_BuiltinQuerySyntax = new TabPage();
            uiTitlePanel_CancelReserveSyntax = new Sunny.UI.UITitlePanel();
            uiTextBox_CancelReserveSyntax = new Sunny.UI.UITextBox();
            uiTitlePanel_QueryReserveInfo = new Sunny.UI.UITitlePanel();
            uiTextBox_QueryReserveInfo = new Sunny.UI.UITextBox();
            uiTitlePanel_QueryAllLibsSummarySyntax = new Sunny.UI.UITitlePanel();
            uiTextBox_QueryAllLibsSummarySyntax = new Sunny.UI.UITextBox();
            uiTitlePanel_ReserveSeatSyntax = new Sunny.UI.UITitlePanel();
            uiTextBox_ReserveSeatSyntax = new Sunny.UI.UITextBox();
            uiTitlePanel_QueryLibInfoSyntax = new Sunny.UI.UITitlePanel();
            uiTextBox_QueryLibInfoSyntax = new Sunny.UI.UITextBox();
            uiTabControl_DataSource.SuspendLayout();
            tabPage_VerifyCookie.SuspendLayout();
            uiTitlePanel_Operation1.SuspendLayout();
            uiTitlePanel_LibID.SuspendLayout();
            uiTitlePanel_LibInfo.SuspendLayout();
            uiTitlePanel_Cookie.SuspendLayout();
            tabPage_GetCookie.SuspendLayout();
            uiTitlePanel1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)pictureBox_QR).BeginInit();
            uiTitlePanel_Operation2.SuspendLayout();
            uiTitlePanel_CodeSourceURL.SuspendLayout();
            tabPage_BuiltinQuerySyntax.SuspendLayout();
            uiTitlePanel_CancelReserveSyntax.SuspendLayout();
            uiTitlePanel_QueryReserveInfo.SuspendLayout();
            uiTitlePanel_QueryAllLibsSummarySyntax.SuspendLayout();
            uiTitlePanel_ReserveSeatSyntax.SuspendLayout();
            uiTitlePanel_QueryLibInfoSyntax.SuspendLayout();
            SuspendLayout();
            // 
            // uiTabControl_DataSource
            // 
            uiTabControl_DataSource.Controls.Add(tabPage_VerifyCookie);
            uiTabControl_DataSource.Controls.Add(tabPage_GetCookie);
            uiTabControl_DataSource.Controls.Add(tabPage_BuiltinQuerySyntax);
            uiTabControl_DataSource.DrawMode = TabDrawMode.OwnerDrawFixed;
            uiTabControl_DataSource.Font = new Font("微软雅黑", 12F, FontStyle.Regular, GraphicsUnit.Point);
            uiTabControl_DataSource.Frame = null;
            uiTabControl_DataSource.ItemSize = new Size(150, 40);
            uiTabControl_DataSource.Location = new Point(12, 12);
            uiTabControl_DataSource.MainPage = "";
            uiTabControl_DataSource.MenuStyle = Sunny.UI.UIMenuStyle.Custom;
            uiTabControl_DataSource.Name = "uiTabControl_DataSource";
            uiTabControl_DataSource.SelectedIndex = 0;
            uiTabControl_DataSource.Size = new Size(633, 491);
            uiTabControl_DataSource.SizeMode = TabSizeMode.Fixed;
            uiTabControl_DataSource.TabBackColor = Color.FromArgb(243, 249, 255);
            uiTabControl_DataSource.TabIndex = 0;
            uiTabControl_DataSource.TabSelectedColor = Color.FromArgb(243, 249, 255);
            uiTabControl_DataSource.TabUnSelectedForeColor = Color.Black;
            uiTabControl_DataSource.ZoomScaleRect = new Rectangle(0, 0, 0, 0);
            // 
            // tabPage_VerifyCookie
            // 
            tabPage_VerifyCookie.BackColor = Color.FromArgb(243, 249, 255);
            tabPage_VerifyCookie.Controls.Add(uiTitlePanel_Operation1);
            tabPage_VerifyCookie.Controls.Add(uiTitlePanel_LibID);
            tabPage_VerifyCookie.Controls.Add(uiTitlePanel_LibInfo);
            tabPage_VerifyCookie.Controls.Add(uiTitlePanel_Cookie);
            tabPage_VerifyCookie.Location = new Point(0, 40);
            tabPage_VerifyCookie.Name = "tabPage_VerifyCookie";
            tabPage_VerifyCookie.Size = new Size(633, 451);
            tabPage_VerifyCookie.TabIndex = 0;
            tabPage_VerifyCookie.Text = "绑定图书馆";
            // 
            // uiTitlePanel_Operation1
            // 
            uiTitlePanel_Operation1.Controls.Add(uiSymbolButton_ReadDataSource);
            uiTitlePanel_Operation1.Controls.Add(uiSymbolButton_BindLibrary);
            uiTitlePanel_Operation1.Controls.Add(uiSymbolButton_SaveDataSource);
            uiTitlePanel_Operation1.Font = new Font("微软雅黑", 12F, FontStyle.Regular, GraphicsUnit.Point);
            uiTitlePanel_Operation1.Location = new Point(4, 281);
            uiTitlePanel_Operation1.Margin = new Padding(4, 5, 4, 5);
            uiTitlePanel_Operation1.MinimumSize = new Size(1, 1);
            uiTitlePanel_Operation1.Name = "uiTitlePanel_Operation1";
            uiTitlePanel_Operation1.Padding = new Padding(0, 35, 0, 0);
            uiTitlePanel_Operation1.ShowText = false;
            uiTitlePanel_Operation1.Size = new Size(229, 165);
            uiTitlePanel_Operation1.TabIndex = 3;
            uiTitlePanel_Operation1.Text = "操作";
            uiTitlePanel_Operation1.TextAlignment = ContentAlignment.MiddleCenter;
            uiTitlePanel_Operation1.ZoomScaleRect = new Rectangle(0, 0, 0, 0);
            // 
            // uiSymbolButton_ReadDataSource
            // 
            uiSymbolButton_ReadDataSource.Font = new Font("微软雅黑", 12F, FontStyle.Regular, GraphicsUnit.Point);
            uiSymbolButton_ReadDataSource.Location = new Point(116, 54);
            uiSymbolButton_ReadDataSource.MinimumSize = new Size(1, 1);
            uiSymbolButton_ReadDataSource.Name = "uiSymbolButton_ReadDataSource";
            uiSymbolButton_ReadDataSource.Size = new Size(98, 35);
            uiSymbolButton_ReadDataSource.Symbol = 57433;
            uiSymbolButton_ReadDataSource.TabIndex = 2;
            uiSymbolButton_ReadDataSource.Text = "读取";
            uiSymbolButton_ReadDataSource.ZoomScaleRect = new Rectangle(0, 0, 0, 0);
            uiSymbolButton_ReadDataSource.Click += uiSymbolButton_ReadDataSource_Click;
            // 
            // uiSymbolButton_BindLibrary
            // 
            uiSymbolButton_BindLibrary.Font = new Font("微软雅黑", 12F, FontStyle.Regular, GraphicsUnit.Point);
            uiSymbolButton_BindLibrary.Location = new Point(14, 104);
            uiSymbolButton_BindLibrary.MinimumSize = new Size(1, 1);
            uiSymbolButton_BindLibrary.Name = "uiSymbolButton_BindLibrary";
            uiSymbolButton_BindLibrary.Size = new Size(200, 35);
            uiSymbolButton_BindLibrary.Symbol = 61667;
            uiSymbolButton_BindLibrary.TabIndex = 1;
            uiSymbolButton_BindLibrary.Text = "绑定";
            uiSymbolButton_BindLibrary.ZoomScaleRect = new Rectangle(0, 0, 0, 0);
            uiSymbolButton_BindLibrary.Click += uiSymbolButton_BindLibrary_Click;
            // 
            // uiSymbolButton_SaveDataSource
            // 
            uiSymbolButton_SaveDataSource.Font = new Font("微软雅黑", 12F, FontStyle.Regular, GraphicsUnit.Point);
            uiSymbolButton_SaveDataSource.Location = new Point(14, 54);
            uiSymbolButton_SaveDataSource.MinimumSize = new Size(1, 1);
            uiSymbolButton_SaveDataSource.Name = "uiSymbolButton_SaveDataSource";
            uiSymbolButton_SaveDataSource.Size = new Size(98, 35);
            uiSymbolButton_SaveDataSource.Symbol = 57432;
            uiSymbolButton_SaveDataSource.TabIndex = 0;
            uiSymbolButton_SaveDataSource.Text = "保存";
            uiSymbolButton_SaveDataSource.ZoomScaleRect = new Rectangle(0, 0, 0, 0);
            uiSymbolButton_SaveDataSource.Click += uiSymbolButton_SaveDataSource_Click;
            // 
            // uiTitlePanel_LibID
            // 
            uiTitlePanel_LibID.Controls.Add(uiIntegerUpDown_LibID);
            uiTitlePanel_LibID.Font = new Font("微软雅黑", 12F, FontStyle.Regular, GraphicsUnit.Point);
            uiTitlePanel_LibID.Location = new Point(4, 191);
            uiTitlePanel_LibID.Margin = new Padding(4, 5, 4, 5);
            uiTitlePanel_LibID.MinimumSize = new Size(1, 1);
            uiTitlePanel_LibID.Name = "uiTitlePanel_LibID";
            uiTitlePanel_LibID.Padding = new Padding(0, 35, 0, 0);
            uiTitlePanel_LibID.ShowText = false;
            uiTitlePanel_LibID.Size = new Size(229, 80);
            uiTitlePanel_LibID.TabIndex = 2;
            uiTitlePanel_LibID.Text = "Lib ID";
            uiTitlePanel_LibID.TextAlignment = ContentAlignment.MiddleCenter;
            uiTitlePanel_LibID.ZoomScaleRect = new Rectangle(0, 0, 0, 0);
            // 
            // uiIntegerUpDown_LibID
            // 
            uiIntegerUpDown_LibID.Font = new Font("微软雅黑", 12F, FontStyle.Regular, GraphicsUnit.Point);
            uiIntegerUpDown_LibID.Location = new Point(14, 43);
            uiIntegerUpDown_LibID.Margin = new Padding(4, 5, 4, 5);
            uiIntegerUpDown_LibID.Maximum = 1000000;
            uiIntegerUpDown_LibID.Minimum = 0;
            uiIntegerUpDown_LibID.MinimumSize = new Size(100, 0);
            uiIntegerUpDown_LibID.Name = "uiIntegerUpDown_LibID";
            uiIntegerUpDown_LibID.ShowText = false;
            uiIntegerUpDown_LibID.Size = new Size(200, 29);
            uiIntegerUpDown_LibID.TabIndex = 0;
            uiIntegerUpDown_LibID.Text = null;
            uiIntegerUpDown_LibID.TextAlignment = ContentAlignment.MiddleCenter;
            uiIntegerUpDown_LibID.ZoomScaleRect = new Rectangle(0, 0, 0, 0);
            // 
            // uiTitlePanel_LibInfo
            // 
            uiTitlePanel_LibInfo.Controls.Add(uiSymbolLabel_LibAvailableSeatsNum);
            uiTitlePanel_LibInfo.Controls.Add(uiSymbolLabel_LibFloor);
            uiTitlePanel_LibInfo.Controls.Add(uiSymbolLabel_LibName);
            uiTitlePanel_LibInfo.Controls.Add(uiSymbolLabel_LibStatus);
            uiTitlePanel_LibInfo.Font = new Font("微软雅黑", 12F, FontStyle.Regular, GraphicsUnit.Point);
            uiTitlePanel_LibInfo.Location = new Point(241, 191);
            uiTitlePanel_LibInfo.Margin = new Padding(4, 5, 4, 5);
            uiTitlePanel_LibInfo.MinimumSize = new Size(1, 1);
            uiTitlePanel_LibInfo.Name = "uiTitlePanel_LibInfo";
            uiTitlePanel_LibInfo.Padding = new Padding(0, 35, 0, 0);
            uiTitlePanel_LibInfo.ShowText = false;
            uiTitlePanel_LibInfo.Size = new Size(388, 255);
            uiTitlePanel_LibInfo.TabIndex = 1;
            uiTitlePanel_LibInfo.Text = "图书馆(室)信息";
            uiTitlePanel_LibInfo.TextAlignment = ContentAlignment.MiddleCenter;
            uiTitlePanel_LibInfo.ZoomScaleRect = new Rectangle(0, 0, 0, 0);
            // 
            // uiSymbolLabel_LibAvailableSeatsNum
            // 
            uiSymbolLabel_LibAvailableSeatsNum.Font = new Font("微软雅黑", 12F, FontStyle.Regular, GraphicsUnit.Point);
            uiSymbolLabel_LibAvailableSeatsNum.ImageInterval = 1;
            uiSymbolLabel_LibAvailableSeatsNum.Location = new Point(14, 195);
            uiSymbolLabel_LibAvailableSeatsNum.MinimumSize = new Size(1, 1);
            uiSymbolLabel_LibAvailableSeatsNum.Name = "uiSymbolLabel_LibAvailableSeatsNum";
            uiSymbolLabel_LibAvailableSeatsNum.Padding = new Padding(26, 0, 0, 0);
            uiSymbolLabel_LibAvailableSeatsNum.Size = new Size(354, 35);
            uiSymbolLabel_LibAvailableSeatsNum.Symbol = 61720;
            uiSymbolLabel_LibAvailableSeatsNum.TabIndex = 3;
            uiSymbolLabel_LibAvailableSeatsNum.Text = "图书馆(室)余座：";
            uiSymbolLabel_LibAvailableSeatsNum.TextAlign = ContentAlignment.MiddleLeft;
            uiSymbolLabel_LibAvailableSeatsNum.ZoomScaleRect = new Rectangle(0, 0, 0, 0);
            // 
            // uiSymbolLabel_LibFloor
            // 
            uiSymbolLabel_LibFloor.Font = new Font("微软雅黑", 12F, FontStyle.Regular, GraphicsUnit.Point);
            uiSymbolLabel_LibFloor.ImageInterval = 1;
            uiSymbolLabel_LibFloor.Location = new Point(14, 148);
            uiSymbolLabel_LibFloor.MinimumSize = new Size(1, 1);
            uiSymbolLabel_LibFloor.Name = "uiSymbolLabel_LibFloor";
            uiSymbolLabel_LibFloor.Padding = new Padding(26, 0, 0, 0);
            uiSymbolLabel_LibFloor.Size = new Size(354, 35);
            uiSymbolLabel_LibFloor.Symbol = 61505;
            uiSymbolLabel_LibFloor.TabIndex = 2;
            uiSymbolLabel_LibFloor.Text = "图书馆(室)楼层：";
            uiSymbolLabel_LibFloor.TextAlign = ContentAlignment.MiddleLeft;
            uiSymbolLabel_LibFloor.ZoomScaleRect = new Rectangle(0, 0, 0, 0);
            // 
            // uiSymbolLabel_LibName
            // 
            uiSymbolLabel_LibName.Font = new Font("微软雅黑", 12F, FontStyle.Regular, GraphicsUnit.Point);
            uiSymbolLabel_LibName.ImageInterval = 1;
            uiSymbolLabel_LibName.Location = new Point(14, 101);
            uiSymbolLabel_LibName.MinimumSize = new Size(1, 1);
            uiSymbolLabel_LibName.Name = "uiSymbolLabel_LibName";
            uiSymbolLabel_LibName.Padding = new Padding(26, 0, 0, 0);
            uiSymbolLabel_LibName.Size = new Size(354, 35);
            uiSymbolLabel_LibName.Symbol = 61687;
            uiSymbolLabel_LibName.TabIndex = 1;
            uiSymbolLabel_LibName.Text = "图书馆(室)名称：";
            uiSymbolLabel_LibName.TextAlign = ContentAlignment.MiddleLeft;
            uiSymbolLabel_LibName.ZoomScaleRect = new Rectangle(0, 0, 0, 0);
            // 
            // uiSymbolLabel_LibStatus
            // 
            uiSymbolLabel_LibStatus.Font = new Font("微软雅黑", 12F, FontStyle.Regular, GraphicsUnit.Point);
            uiSymbolLabel_LibStatus.ImageInterval = 1;
            uiSymbolLabel_LibStatus.Location = new Point(14, 54);
            uiSymbolLabel_LibStatus.MinimumSize = new Size(1, 1);
            uiSymbolLabel_LibStatus.Name = "uiSymbolLabel_LibStatus";
            uiSymbolLabel_LibStatus.Padding = new Padding(26, 0, 0, 0);
            uiSymbolLabel_LibStatus.Size = new Size(354, 35);
            uiSymbolLabel_LibStatus.Symbol = 61568;
            uiSymbolLabel_LibStatus.TabIndex = 0;
            uiSymbolLabel_LibStatus.Text = "状态：";
            uiSymbolLabel_LibStatus.TextAlign = ContentAlignment.MiddleLeft;
            uiSymbolLabel_LibStatus.ZoomScaleRect = new Rectangle(0, 0, 0, 0);
            // 
            // uiTitlePanel_Cookie
            // 
            uiTitlePanel_Cookie.Controls.Add(uiTextBox_Cookies);
            uiTitlePanel_Cookie.Font = new Font("微软雅黑", 12F, FontStyle.Regular, GraphicsUnit.Point);
            uiTitlePanel_Cookie.Location = new Point(4, 12);
            uiTitlePanel_Cookie.Margin = new Padding(4, 5, 4, 5);
            uiTitlePanel_Cookie.MinimumSize = new Size(1, 1);
            uiTitlePanel_Cookie.Name = "uiTitlePanel_Cookie";
            uiTitlePanel_Cookie.Padding = new Padding(0, 35, 0, 0);
            uiTitlePanel_Cookie.ShowText = false;
            uiTitlePanel_Cookie.Size = new Size(625, 169);
            uiTitlePanel_Cookie.TabIndex = 0;
            uiTitlePanel_Cookie.Text = "Cookie";
            uiTitlePanel_Cookie.TextAlignment = ContentAlignment.MiddleCenter;
            uiTitlePanel_Cookie.ZoomScaleRect = new Rectangle(0, 0, 0, 0);
            // 
            // uiTextBox_Cookies
            // 
            uiTextBox_Cookies.Font = new Font("微软雅黑", 12F, FontStyle.Regular, GraphicsUnit.Point);
            uiTextBox_Cookies.Location = new Point(4, 40);
            uiTextBox_Cookies.Margin = new Padding(4, 5, 4, 5);
            uiTextBox_Cookies.Maximum = 1500D;
            uiTextBox_Cookies.MinimumSize = new Size(1, 16);
            uiTextBox_Cookies.Multiline = true;
            uiTextBox_Cookies.Name = "uiTextBox_Cookies";
            uiTextBox_Cookies.RectSides = ToolStripStatusLabelBorderSides.None;
            uiTextBox_Cookies.ShowScrollBar = true;
            uiTextBox_Cookies.ShowText = false;
            uiTextBox_Cookies.Size = new Size(617, 124);
            uiTextBox_Cookies.TabIndex = 0;
            uiTextBox_Cookies.TextAlignment = ContentAlignment.MiddleLeft;
            uiTextBox_Cookies.Watermark = "如不会填写Cookie请阅读教程并使用获取Cookie功能";
            uiTextBox_Cookies.ZoomScaleRect = new Rectangle(0, 0, 0, 0);
            // 
            // tabPage_GetCookie
            // 
            tabPage_GetCookie.BackColor = Color.FromArgb(243, 249, 255);
            tabPage_GetCookie.Controls.Add(uiTitlePanel1);
            tabPage_GetCookie.Controls.Add(uiTitlePanel_Operation2);
            tabPage_GetCookie.Controls.Add(uiTitlePanel_CodeSourceURL);
            tabPage_GetCookie.Location = new Point(0, 40);
            tabPage_GetCookie.Name = "tabPage_GetCookie";
            tabPage_GetCookie.Size = new Size(633, 451);
            tabPage_GetCookie.TabIndex = 1;
            tabPage_GetCookie.Text = "获取Cookie";
            // 
            // uiTitlePanel1
            // 
            uiTitlePanel1.Controls.Add(uiLabel1);
            uiTitlePanel1.Controls.Add(pictureBox_QR);
            uiTitlePanel1.Font = new Font("微软雅黑", 12F, FontStyle.Regular, GraphicsUnit.Point);
            uiTitlePanel1.Location = new Point(8, 196);
            uiTitlePanel1.Margin = new Padding(4, 5, 4, 5);
            uiTitlePanel1.MinimumSize = new Size(1, 1);
            uiTitlePanel1.Name = "uiTitlePanel1";
            uiTitlePanel1.Padding = new Padding(0, 35, 0, 0);
            uiTitlePanel1.ShowText = false;
            uiTitlePanel1.Size = new Size(376, 247);
            uiTitlePanel1.TabIndex = 2;
            uiTitlePanel1.Text = "含code链接获取教程";
            uiTitlePanel1.TextAlignment = ContentAlignment.MiddleCenter;
            uiTitlePanel1.ZoomScaleRect = new Rectangle(0, 0, 0, 0);
            // 
            // uiLabel1
            // 
            uiLabel1.Font = new Font("微软雅黑", 12F, FontStyle.Regular, GraphicsUnit.Point);
            uiLabel1.Location = new Point(181, 39);
            uiLabel1.Name = "uiLabel1";
            uiLabel1.Size = new Size(187, 194);
            uiLabel1.TabIndex = 1;
            uiLabel1.Text = "1.使用微信扫左侧二维码\r\n2.进入页面后点击右上角“…”符号，选择“复制链接”\r\n3.将链接粘贴至上方文本框";
            uiLabel1.TextAlign = ContentAlignment.MiddleLeft;
            uiLabel1.ZoomScaleRect = new Rectangle(0, 0, 0, 0);
            // 
            // pictureBox_QR
            // 
            pictureBox_QR.Image = Properties.Resources.qrcode;
            pictureBox_QR.Location = new Point(9, 49);
            pictureBox_QR.Name = "pictureBox_QR";
            pictureBox_QR.Size = new Size(166, 179);
            pictureBox_QR.SizeMode = PictureBoxSizeMode.Zoom;
            pictureBox_QR.TabIndex = 0;
            pictureBox_QR.TabStop = false;
            // 
            // uiTitlePanel_Operation2
            // 
            uiTitlePanel_Operation2.Controls.Add(uiSymbolButton_GetCookie);
            uiTitlePanel_Operation2.Font = new Font("微软雅黑", 12F, FontStyle.Regular, GraphicsUnit.Point);
            uiTitlePanel_Operation2.Location = new Point(392, 286);
            uiTitlePanel_Operation2.Margin = new Padding(4, 5, 4, 5);
            uiTitlePanel_Operation2.MinimumSize = new Size(1, 1);
            uiTitlePanel_Operation2.Name = "uiTitlePanel_Operation2";
            uiTitlePanel_Operation2.Padding = new Padding(0, 35, 0, 0);
            uiTitlePanel_Operation2.ShowText = false;
            uiTitlePanel_Operation2.Size = new Size(237, 157);
            uiTitlePanel_Operation2.TabIndex = 1;
            uiTitlePanel_Operation2.Text = "操作";
            uiTitlePanel_Operation2.TextAlignment = ContentAlignment.MiddleCenter;
            uiTitlePanel_Operation2.ZoomScaleRect = new Rectangle(0, 0, 0, 0);
            // 
            // uiSymbolButton_GetCookie
            // 
            uiSymbolButton_GetCookie.Font = new Font("微软雅黑", 12F, FontStyle.Regular, GraphicsUnit.Point);
            uiSymbolButton_GetCookie.Location = new Point(14, 79);
            uiSymbolButton_GetCookie.MinimumSize = new Size(1, 1);
            uiSymbolButton_GetCookie.Name = "uiSymbolButton_GetCookie";
            uiSymbolButton_GetCookie.Size = new Size(206, 37);
            uiSymbolButton_GetCookie.Symbol = 61648;
            uiSymbolButton_GetCookie.TabIndex = 0;
            uiSymbolButton_GetCookie.Text = "获取并填写Cookie";
            uiSymbolButton_GetCookie.ZoomScaleRect = new Rectangle(0, 0, 0, 0);
            uiSymbolButton_GetCookie.Click += uiSymbolButton_GetCookie_Click;
            // 
            // uiTitlePanel_CodeSourceURL
            // 
            uiTitlePanel_CodeSourceURL.Controls.Add(uiTextBox_CodeSourceURL);
            uiTitlePanel_CodeSourceURL.Font = new Font("微软雅黑", 12F, FontStyle.Regular, GraphicsUnit.Point);
            uiTitlePanel_CodeSourceURL.Location = new Point(4, 12);
            uiTitlePanel_CodeSourceURL.Margin = new Padding(4, 5, 4, 5);
            uiTitlePanel_CodeSourceURL.MinimumSize = new Size(1, 1);
            uiTitlePanel_CodeSourceURL.Name = "uiTitlePanel_CodeSourceURL";
            uiTitlePanel_CodeSourceURL.Padding = new Padding(0, 35, 0, 0);
            uiTitlePanel_CodeSourceURL.ShowText = false;
            uiTitlePanel_CodeSourceURL.Size = new Size(625, 174);
            uiTitlePanel_CodeSourceURL.TabIndex = 0;
            uiTitlePanel_CodeSourceURL.Text = "扫码后获取的含code链接";
            uiTitlePanel_CodeSourceURL.TextAlignment = ContentAlignment.MiddleCenter;
            uiTitlePanel_CodeSourceURL.ZoomScaleRect = new Rectangle(0, 0, 0, 0);
            // 
            // uiTextBox_CodeSourceURL
            // 
            uiTextBox_CodeSourceURL.Font = new Font("微软雅黑", 12F, FontStyle.Regular, GraphicsUnit.Point);
            uiTextBox_CodeSourceURL.Location = new Point(4, 40);
            uiTextBox_CodeSourceURL.Margin = new Padding(4, 5, 4, 5);
            uiTextBox_CodeSourceURL.MinimumSize = new Size(1, 16);
            uiTextBox_CodeSourceURL.Multiline = true;
            uiTextBox_CodeSourceURL.Name = "uiTextBox_CodeSourceURL";
            uiTextBox_CodeSourceURL.RectSides = ToolStripStatusLabelBorderSides.None;
            uiTextBox_CodeSourceURL.ShowScrollBar = true;
            uiTextBox_CodeSourceURL.ShowText = false;
            uiTextBox_CodeSourceURL.Size = new Size(617, 126);
            uiTextBox_CodeSourceURL.TabIndex = 0;
            uiTextBox_CodeSourceURL.TextAlignment = ContentAlignment.MiddleLeft;
            uiTextBox_CodeSourceURL.Watermark = "填入复制来的链接";
            uiTextBox_CodeSourceURL.ZoomScaleRect = new Rectangle(0, 0, 0, 0);
            // 
            // tabPage_BuiltinQuerySyntax
            // 
            tabPage_BuiltinQuerySyntax.AutoScroll = true;
            tabPage_BuiltinQuerySyntax.BackColor = Color.FromArgb(243, 249, 255);
            tabPage_BuiltinQuerySyntax.Controls.Add(uiTitlePanel_CancelReserveSyntax);
            tabPage_BuiltinQuerySyntax.Controls.Add(uiTitlePanel_QueryReserveInfo);
            tabPage_BuiltinQuerySyntax.Controls.Add(uiTitlePanel_QueryAllLibsSummarySyntax);
            tabPage_BuiltinQuerySyntax.Controls.Add(uiTitlePanel_ReserveSeatSyntax);
            tabPage_BuiltinQuerySyntax.Controls.Add(uiTitlePanel_QueryLibInfoSyntax);
            tabPage_BuiltinQuerySyntax.Location = new Point(0, 40);
            tabPage_BuiltinQuerySyntax.Name = "tabPage_BuiltinQuerySyntax";
            tabPage_BuiltinQuerySyntax.Size = new Size(200, 60);
            tabPage_BuiltinQuerySyntax.TabIndex = 2;
            tabPage_BuiltinQuerySyntax.Text = "内置查询语法";
            // 
            // uiTitlePanel_CancelReserveSyntax
            // 
            uiTitlePanel_CancelReserveSyntax.Controls.Add(uiTextBox_CancelReserveSyntax);
            uiTitlePanel_CancelReserveSyntax.Font = new Font("微软雅黑", 12F, FontStyle.Regular, GraphicsUnit.Point);
            uiTitlePanel_CancelReserveSyntax.Location = new Point(7, 724);
            uiTitlePanel_CancelReserveSyntax.Margin = new Padding(4, 5, 4, 5);
            uiTitlePanel_CancelReserveSyntax.MinimumSize = new Size(1, 1);
            uiTitlePanel_CancelReserveSyntax.Name = "uiTitlePanel_CancelReserveSyntax";
            uiTitlePanel_CancelReserveSyntax.Padding = new Padding(0, 35, 0, 0);
            uiTitlePanel_CancelReserveSyntax.ShowText = false;
            uiTitlePanel_CancelReserveSyntax.Size = new Size(608, 168);
            uiTitlePanel_CancelReserveSyntax.TabIndex = 4;
            uiTitlePanel_CancelReserveSyntax.Text = "取消预约语法(替换Token)";
            uiTitlePanel_CancelReserveSyntax.TextAlignment = ContentAlignment.MiddleCenter;
            uiTitlePanel_CancelReserveSyntax.ZoomScaleRect = new Rectangle(0, 0, 0, 0);
            // 
            // uiTextBox_CancelReserveSyntax
            // 
            uiTextBox_CancelReserveSyntax.Font = new Font("微软雅黑", 12F, FontStyle.Regular, GraphicsUnit.Point);
            uiTextBox_CancelReserveSyntax.Location = new Point(4, 40);
            uiTextBox_CancelReserveSyntax.Margin = new Padding(4, 5, 4, 5);
            uiTextBox_CancelReserveSyntax.MinimumSize = new Size(1, 16);
            uiTextBox_CancelReserveSyntax.Multiline = true;
            uiTextBox_CancelReserveSyntax.Name = "uiTextBox_CancelReserveSyntax";
            uiTextBox_CancelReserveSyntax.ShowScrollBar = true;
            uiTextBox_CancelReserveSyntax.ShowText = false;
            uiTextBox_CancelReserveSyntax.Size = new Size(601, 123);
            uiTextBox_CancelReserveSyntax.TabIndex = 0;
            uiTextBox_CancelReserveSyntax.Text = resources.GetString("uiTextBox_CancelReserveSyntax.Text");
            uiTextBox_CancelReserveSyntax.TextAlignment = ContentAlignment.MiddleLeft;
            uiTextBox_CancelReserveSyntax.Watermark = "";
            uiTextBox_CancelReserveSyntax.ZoomScaleRect = new Rectangle(0, 0, 0, 0);
            // 
            // uiTitlePanel_QueryReserveInfo
            // 
            uiTitlePanel_QueryReserveInfo.Controls.Add(uiTextBox_QueryReserveInfo);
            uiTitlePanel_QueryReserveInfo.Font = new Font("微软雅黑", 12F, FontStyle.Regular, GraphicsUnit.Point);
            uiTitlePanel_QueryReserveInfo.Location = new Point(7, 546);
            uiTitlePanel_QueryReserveInfo.Margin = new Padding(4, 5, 4, 5);
            uiTitlePanel_QueryReserveInfo.MinimumSize = new Size(1, 1);
            uiTitlePanel_QueryReserveInfo.Name = "uiTitlePanel_QueryReserveInfo";
            uiTitlePanel_QueryReserveInfo.Padding = new Padding(0, 35, 0, 0);
            uiTitlePanel_QueryReserveInfo.ShowText = false;
            uiTitlePanel_QueryReserveInfo.Size = new Size(608, 168);
            uiTitlePanel_QueryReserveInfo.TabIndex = 3;
            uiTitlePanel_QueryReserveInfo.Text = "查询预约信息语法(Json)";
            uiTitlePanel_QueryReserveInfo.TextAlignment = ContentAlignment.MiddleCenter;
            uiTitlePanel_QueryReserveInfo.ZoomScaleRect = new Rectangle(0, 0, 0, 0);
            // 
            // uiTextBox_QueryReserveInfo
            // 
            uiTextBox_QueryReserveInfo.Font = new Font("微软雅黑", 12F, FontStyle.Regular, GraphicsUnit.Point);
            uiTextBox_QueryReserveInfo.Location = new Point(4, 40);
            uiTextBox_QueryReserveInfo.Margin = new Padding(4, 5, 4, 5);
            uiTextBox_QueryReserveInfo.MinimumSize = new Size(1, 16);
            uiTextBox_QueryReserveInfo.Multiline = true;
            uiTextBox_QueryReserveInfo.Name = "uiTextBox_QueryReserveInfo";
            uiTextBox_QueryReserveInfo.ShowScrollBar = true;
            uiTextBox_QueryReserveInfo.ShowText = false;
            uiTextBox_QueryReserveInfo.Size = new Size(601, 123);
            uiTextBox_QueryReserveInfo.TabIndex = 0;
            uiTextBox_QueryReserveInfo.Text = resources.GetString("uiTextBox_QueryReserveInfo.Text");
            uiTextBox_QueryReserveInfo.TextAlignment = ContentAlignment.MiddleLeft;
            uiTextBox_QueryReserveInfo.Watermark = "";
            uiTextBox_QueryReserveInfo.ZoomScaleRect = new Rectangle(0, 0, 0, 0);
            // 
            // uiTitlePanel_QueryAllLibsSummarySyntax
            // 
            uiTitlePanel_QueryAllLibsSummarySyntax.Controls.Add(uiTextBox_QueryAllLibsSummarySyntax);
            uiTitlePanel_QueryAllLibsSummarySyntax.Font = new Font("微软雅黑", 12F, FontStyle.Regular, GraphicsUnit.Point);
            uiTitlePanel_QueryAllLibsSummarySyntax.Location = new Point(7, 368);
            uiTitlePanel_QueryAllLibsSummarySyntax.Margin = new Padding(4, 5, 4, 5);
            uiTitlePanel_QueryAllLibsSummarySyntax.MinimumSize = new Size(1, 1);
            uiTitlePanel_QueryAllLibsSummarySyntax.Name = "uiTitlePanel_QueryAllLibsSummarySyntax";
            uiTitlePanel_QueryAllLibsSummarySyntax.Padding = new Padding(0, 35, 0, 0);
            uiTitlePanel_QueryAllLibsSummarySyntax.ShowText = false;
            uiTitlePanel_QueryAllLibsSummarySyntax.Size = new Size(608, 168);
            uiTitlePanel_QueryAllLibsSummarySyntax.TabIndex = 2;
            uiTitlePanel_QueryAllLibsSummarySyntax.Text = "查询账号中所有图书馆语法(Json)";
            uiTitlePanel_QueryAllLibsSummarySyntax.TextAlignment = ContentAlignment.MiddleCenter;
            uiTitlePanel_QueryAllLibsSummarySyntax.ZoomScaleRect = new Rectangle(0, 0, 0, 0);
            // 
            // uiTextBox_QueryAllLibsSummarySyntax
            // 
            uiTextBox_QueryAllLibsSummarySyntax.Font = new Font("微软雅黑", 12F, FontStyle.Regular, GraphicsUnit.Point);
            uiTextBox_QueryAllLibsSummarySyntax.Location = new Point(4, 40);
            uiTextBox_QueryAllLibsSummarySyntax.Margin = new Padding(4, 5, 4, 5);
            uiTextBox_QueryAllLibsSummarySyntax.MinimumSize = new Size(1, 16);
            uiTextBox_QueryAllLibsSummarySyntax.Multiline = true;
            uiTextBox_QueryAllLibsSummarySyntax.Name = "uiTextBox_QueryAllLibsSummarySyntax";
            uiTextBox_QueryAllLibsSummarySyntax.ShowScrollBar = true;
            uiTextBox_QueryAllLibsSummarySyntax.ShowText = false;
            uiTextBox_QueryAllLibsSummarySyntax.Size = new Size(601, 123);
            uiTextBox_QueryAllLibsSummarySyntax.TabIndex = 0;
            uiTextBox_QueryAllLibsSummarySyntax.Text = resources.GetString("uiTextBox_QueryAllLibsSummarySyntax.Text");
            uiTextBox_QueryAllLibsSummarySyntax.TextAlignment = ContentAlignment.MiddleLeft;
            uiTextBox_QueryAllLibsSummarySyntax.Watermark = "";
            uiTextBox_QueryAllLibsSummarySyntax.ZoomScaleRect = new Rectangle(0, 0, 0, 0);
            // 
            // uiTitlePanel_ReserveSeatSyntax
            // 
            uiTitlePanel_ReserveSeatSyntax.Controls.Add(uiTextBox_ReserveSeatSyntax);
            uiTitlePanel_ReserveSeatSyntax.Font = new Font("微软雅黑", 12F, FontStyle.Regular, GraphicsUnit.Point);
            uiTitlePanel_ReserveSeatSyntax.Location = new Point(7, 190);
            uiTitlePanel_ReserveSeatSyntax.Margin = new Padding(4, 5, 4, 5);
            uiTitlePanel_ReserveSeatSyntax.MinimumSize = new Size(1, 1);
            uiTitlePanel_ReserveSeatSyntax.Name = "uiTitlePanel_ReserveSeatSyntax";
            uiTitlePanel_ReserveSeatSyntax.Padding = new Padding(0, 35, 0, 0);
            uiTitlePanel_ReserveSeatSyntax.ShowText = false;
            uiTitlePanel_ReserveSeatSyntax.Size = new Size(608, 168);
            uiTitlePanel_ReserveSeatSyntax.TabIndex = 1;
            uiTitlePanel_ReserveSeatSyntax.Text = "预定指定座位语法(Json)：替换Lib ID和座位x,y坐标";
            uiTitlePanel_ReserveSeatSyntax.TextAlignment = ContentAlignment.MiddleCenter;
            uiTitlePanel_ReserveSeatSyntax.ZoomScaleRect = new Rectangle(0, 0, 0, 0);
            // 
            // uiTextBox_ReserveSeatSyntax
            // 
            uiTextBox_ReserveSeatSyntax.Font = new Font("微软雅黑", 12F, FontStyle.Regular, GraphicsUnit.Point);
            uiTextBox_ReserveSeatSyntax.Location = new Point(4, 40);
            uiTextBox_ReserveSeatSyntax.Margin = new Padding(4, 5, 4, 5);
            uiTextBox_ReserveSeatSyntax.MinimumSize = new Size(1, 16);
            uiTextBox_ReserveSeatSyntax.Multiline = true;
            uiTextBox_ReserveSeatSyntax.Name = "uiTextBox_ReserveSeatSyntax";
            uiTextBox_ReserveSeatSyntax.ShowScrollBar = true;
            uiTextBox_ReserveSeatSyntax.ShowText = false;
            uiTextBox_ReserveSeatSyntax.Size = new Size(601, 123);
            uiTextBox_ReserveSeatSyntax.TabIndex = 0;
            uiTextBox_ReserveSeatSyntax.Text = resources.GetString("uiTextBox_ReserveSeatSyntax.Text");
            uiTextBox_ReserveSeatSyntax.TextAlignment = ContentAlignment.MiddleLeft;
            uiTextBox_ReserveSeatSyntax.Watermark = "";
            uiTextBox_ReserveSeatSyntax.ZoomScaleRect = new Rectangle(0, 0, 0, 0);
            // 
            // uiTitlePanel_QueryLibInfoSyntax
            // 
            uiTitlePanel_QueryLibInfoSyntax.Controls.Add(uiTextBox_QueryLibInfoSyntax);
            uiTitlePanel_QueryLibInfoSyntax.Font = new Font("微软雅黑", 12F, FontStyle.Regular, GraphicsUnit.Point);
            uiTitlePanel_QueryLibInfoSyntax.Location = new Point(4, 12);
            uiTitlePanel_QueryLibInfoSyntax.Margin = new Padding(4, 5, 4, 5);
            uiTitlePanel_QueryLibInfoSyntax.MinimumSize = new Size(1, 1);
            uiTitlePanel_QueryLibInfoSyntax.Name = "uiTitlePanel_QueryLibInfoSyntax";
            uiTitlePanel_QueryLibInfoSyntax.Padding = new Padding(0, 35, 0, 0);
            uiTitlePanel_QueryLibInfoSyntax.ShowText = false;
            uiTitlePanel_QueryLibInfoSyntax.Size = new Size(608, 168);
            uiTitlePanel_QueryLibInfoSyntax.TabIndex = 0;
            uiTitlePanel_QueryLibInfoSyntax.Text = "查询单个图书馆(室)所有信息语法(Json)：替换入Lib ID";
            uiTitlePanel_QueryLibInfoSyntax.TextAlignment = ContentAlignment.MiddleCenter;
            uiTitlePanel_QueryLibInfoSyntax.ZoomScaleRect = new Rectangle(0, 0, 0, 0);
            // 
            // uiTextBox_QueryLibInfoSyntax
            // 
            uiTextBox_QueryLibInfoSyntax.Font = new Font("微软雅黑", 12F, FontStyle.Regular, GraphicsUnit.Point);
            uiTextBox_QueryLibInfoSyntax.Location = new Point(3, 39);
            uiTextBox_QueryLibInfoSyntax.Margin = new Padding(4, 5, 4, 5);
            uiTextBox_QueryLibInfoSyntax.MaxLength = 1000;
            uiTextBox_QueryLibInfoSyntax.MinimumSize = new Size(1, 16);
            uiTextBox_QueryLibInfoSyntax.Multiline = true;
            uiTextBox_QueryLibInfoSyntax.Name = "uiTextBox_QueryLibInfoSyntax";
            uiTextBox_QueryLibInfoSyntax.RadiusSides = Sunny.UI.UICornerRadiusSides.None;
            uiTextBox_QueryLibInfoSyntax.RectSides = ToolStripStatusLabelBorderSides.None;
            uiTextBox_QueryLibInfoSyntax.ShowScrollBar = true;
            uiTextBox_QueryLibInfoSyntax.ShowText = false;
            uiTextBox_QueryLibInfoSyntax.Size = new Size(600, 124);
            uiTextBox_QueryLibInfoSyntax.TabIndex = 0;
            uiTextBox_QueryLibInfoSyntax.Text = resources.GetString("uiTextBox_QueryLibInfoSyntax.Text");
            uiTextBox_QueryLibInfoSyntax.TextAlignment = ContentAlignment.MiddleLeft;
            uiTextBox_QueryLibInfoSyntax.Watermark = "";
            uiTextBox_QueryLibInfoSyntax.ZoomScaleRect = new Rectangle(0, 0, 0, 0);
            // 
            // FDataSource
            // 
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(657, 515);
            Controls.Add(uiTabControl_DataSource);
            MaximumSize = new Size(657, 515);
            Name = "FDataSource";
            Symbol = 104;
            SymbolSize = 30;
            Text = "数据源";
            uiTabControl_DataSource.ResumeLayout(false);
            tabPage_VerifyCookie.ResumeLayout(false);
            uiTitlePanel_Operation1.ResumeLayout(false);
            uiTitlePanel_LibID.ResumeLayout(false);
            uiTitlePanel_LibInfo.ResumeLayout(false);
            uiTitlePanel_Cookie.ResumeLayout(false);
            tabPage_GetCookie.ResumeLayout(false);
            uiTitlePanel1.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)pictureBox_QR).EndInit();
            uiTitlePanel_Operation2.ResumeLayout(false);
            uiTitlePanel_CodeSourceURL.ResumeLayout(false);
            tabPage_BuiltinQuerySyntax.ResumeLayout(false);
            uiTitlePanel_CancelReserveSyntax.ResumeLayout(false);
            uiTitlePanel_QueryReserveInfo.ResumeLayout(false);
            uiTitlePanel_QueryAllLibsSummarySyntax.ResumeLayout(false);
            uiTitlePanel_ReserveSeatSyntax.ResumeLayout(false);
            uiTitlePanel_QueryLibInfoSyntax.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private Sunny.UI.UITabControl uiTabControl_DataSource;
        private TabPage tabPage_VerifyCookie;
        private TabPage tabPage_GetCookie;
        private Sunny.UI.UITitlePanel uiTitlePanel_Cookie;
        private TabPage tabPage_BuiltinQuerySyntax;
        private Sunny.UI.UITitlePanel uiTitlePanel_LibInfo;
        private Sunny.UI.UITitlePanel uiTitlePanel_LibID;
        private Sunny.UI.UIIntegerUpDown uiIntegerUpDown_LibID;
        private Sunny.UI.UITitlePanel uiTitlePanel_Operation1;
        private Sunny.UI.UISymbolButton uiSymbolButton_ReadDataSource;
        private Sunny.UI.UISymbolButton uiSymbolButton_BindLibrary;
        private Sunny.UI.UISymbolButton uiSymbolButton_SaveDataSource;
        private Sunny.UI.UISymbolLabel uiSymbolLabel_LibStatus;
        private Sunny.UI.UISymbolLabel uiSymbolLabel_LibAvailableSeatsNum;
        private Sunny.UI.UISymbolLabel uiSymbolLabel_LibFloor;
        private Sunny.UI.UISymbolLabel uiSymbolLabel_LibName;
        public Sunny.UI.UITextBox uiTextBox_Cookies;
        private Sunny.UI.UITitlePanel uiTitlePanel_QueryLibInfoSyntax;
        public Sunny.UI.UITextBox uiTextBox_QueryLibInfoSyntax;
        private Sunny.UI.UITitlePanel uiTitlePanel_CodeSourceURL;
        private Sunny.UI.UITitlePanel uiTitlePanel_Operation2;
        private Sunny.UI.UISymbolButton uiSymbolButton_GetCookie;
        private Sunny.UI.UITextBox uiTextBox_CodeSourceURL;
        private Sunny.UI.UITitlePanel uiTitlePanel_ReserveSeatSyntax;
        private Sunny.UI.UITextBox uiTextBox_ReserveSeatSyntax;
        private Sunny.UI.UITitlePanel uiTitlePanel1;
        private PictureBox pictureBox_QR;
        private Sunny.UI.UILabel uiLabel1;
        private Sunny.UI.UITitlePanel uiTitlePanel_QueryAllLibsSummarySyntax;
        private Sunny.UI.UITextBox uiTextBox_QueryAllLibsSummarySyntax;
        private Sunny.UI.UITitlePanel uiTitlePanel_QueryReserveInfo;
        private Sunny.UI.UITextBox uiTextBox_QueryReserveInfo;
        private Sunny.UI.UITitlePanel uiTitlePanel_CancelReserveSyntax;
        private Sunny.UI.UITextBox uiTextBox_CancelReserveSyntax;
    }
}