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
            this.uiTabControl_DataSource = new Sunny.UI.UITabControl();
            this.tabPage_DirectFilling = new System.Windows.Forms.TabPage();
            this.uiTitlePanel_Operation1 = new Sunny.UI.UITitlePanel();
            this.uiSymbolButton_ReadDataSource = new Sunny.UI.UISymbolButton();
            this.uiSymbolButton_Verify = new Sunny.UI.UISymbolButton();
            this.uiSymbolButton_SaveDataSource = new Sunny.UI.UISymbolButton();
            this.uiTitlePanel_LibID = new Sunny.UI.UITitlePanel();
            this.uiIntegerUpDown_LibID = new Sunny.UI.UIIntegerUpDown();
            this.uiTitlePanel_LibInfo = new Sunny.UI.UITitlePanel();
            this.uiSymbolLabel_LibAvailableSeatsNum = new Sunny.UI.UISymbolLabel();
            this.uiSymbolLabel_LibFloor = new Sunny.UI.UISymbolLabel();
            this.uiSymbolLabel_LibName = new Sunny.UI.UISymbolLabel();
            this.uiSymbolLabel_LibStatus = new Sunny.UI.UISymbolLabel();
            this.uiTitlePanel_Cookie = new Sunny.UI.UITitlePanel();
            this.uiTextBox_Cookies = new Sunny.UI.UITextBox();
            this.tabPage_AutoIdentify = new System.Windows.Forms.TabPage();
            this.uiTitlePanel_Operation2 = new Sunny.UI.UITitlePanel();
            this.uiSymbolButton_AutoIdentify = new Sunny.UI.UISymbolButton();
            this.uiTitlePanel_Session = new Sunny.UI.UITitlePanel();
            this.uiTextBox_Session = new Sunny.UI.UITextBox();
            this.tabPage_BuiltinQuerySyntax = new System.Windows.Forms.TabPage();
            this.uiTitlePanel_ReserveSeatSyntax = new Sunny.UI.UITitlePanel();
            this.uiTextBox_ReserveSeatSyntax = new Sunny.UI.UITextBox();
            this.uiTitlePanel_QueryLibInfoSyntax = new Sunny.UI.UITitlePanel();
            this.uiTextBox_QueryLibInfoSyntax = new Sunny.UI.UITextBox();
            this.uiTabControl_DataSource.SuspendLayout();
            this.tabPage_DirectFilling.SuspendLayout();
            this.uiTitlePanel_Operation1.SuspendLayout();
            this.uiTitlePanel_LibID.SuspendLayout();
            this.uiTitlePanel_LibInfo.SuspendLayout();
            this.uiTitlePanel_Cookie.SuspendLayout();
            this.tabPage_AutoIdentify.SuspendLayout();
            this.uiTitlePanel_Operation2.SuspendLayout();
            this.uiTitlePanel_Session.SuspendLayout();
            this.tabPage_BuiltinQuerySyntax.SuspendLayout();
            this.uiTitlePanel_ReserveSeatSyntax.SuspendLayout();
            this.uiTitlePanel_QueryLibInfoSyntax.SuspendLayout();
            this.SuspendLayout();
            // 
            // uiTabControl_DataSource
            // 
            this.uiTabControl_DataSource.Controls.Add(this.tabPage_DirectFilling);
            this.uiTabControl_DataSource.Controls.Add(this.tabPage_AutoIdentify);
            this.uiTabControl_DataSource.Controls.Add(this.tabPage_BuiltinQuerySyntax);
            this.uiTabControl_DataSource.DrawMode = System.Windows.Forms.TabDrawMode.OwnerDrawFixed;
            this.uiTabControl_DataSource.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.uiTabControl_DataSource.Frame = null;
            this.uiTabControl_DataSource.ItemSize = new System.Drawing.Size(150, 40);
            this.uiTabControl_DataSource.Location = new System.Drawing.Point(12, 12);
            this.uiTabControl_DataSource.MainPage = "";
            this.uiTabControl_DataSource.MenuStyle = Sunny.UI.UIMenuStyle.Custom;
            this.uiTabControl_DataSource.Name = "uiTabControl_DataSource";
            this.uiTabControl_DataSource.SelectedIndex = 0;
            this.uiTabControl_DataSource.Size = new System.Drawing.Size(633, 491);
            this.uiTabControl_DataSource.SizeMode = System.Windows.Forms.TabSizeMode.Fixed;
            this.uiTabControl_DataSource.TabBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(243)))), ((int)(((byte)(249)))), ((int)(((byte)(255)))));
            this.uiTabControl_DataSource.TabIndex = 0;
            this.uiTabControl_DataSource.TabSelectedColor = System.Drawing.Color.FromArgb(((int)(((byte)(243)))), ((int)(((byte)(249)))), ((int)(((byte)(255)))));
            this.uiTabControl_DataSource.TabUnSelectedForeColor = System.Drawing.Color.Black;
            this.uiTabControl_DataSource.ZoomScaleRect = new System.Drawing.Rectangle(0, 0, 0, 0);
            // 
            // tabPage_DirectFilling
            // 
            this.tabPage_DirectFilling.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(243)))), ((int)(((byte)(249)))), ((int)(((byte)(255)))));
            this.tabPage_DirectFilling.Controls.Add(this.uiTitlePanel_Operation1);
            this.tabPage_DirectFilling.Controls.Add(this.uiTitlePanel_LibID);
            this.tabPage_DirectFilling.Controls.Add(this.uiTitlePanel_LibInfo);
            this.tabPage_DirectFilling.Controls.Add(this.uiTitlePanel_Cookie);
            this.tabPage_DirectFilling.Location = new System.Drawing.Point(0, 40);
            this.tabPage_DirectFilling.Name = "tabPage_DirectFilling";
            this.tabPage_DirectFilling.Size = new System.Drawing.Size(633, 451);
            this.tabPage_DirectFilling.TabIndex = 0;
            this.tabPage_DirectFilling.Text = "手动填写";
            // 
            // uiTitlePanel_Operation1
            // 
            this.uiTitlePanel_Operation1.Controls.Add(this.uiSymbolButton_ReadDataSource);
            this.uiTitlePanel_Operation1.Controls.Add(this.uiSymbolButton_Verify);
            this.uiTitlePanel_Operation1.Controls.Add(this.uiSymbolButton_SaveDataSource);
            this.uiTitlePanel_Operation1.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.uiTitlePanel_Operation1.Location = new System.Drawing.Point(4, 281);
            this.uiTitlePanel_Operation1.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.uiTitlePanel_Operation1.MinimumSize = new System.Drawing.Size(1, 1);
            this.uiTitlePanel_Operation1.Name = "uiTitlePanel_Operation1";
            this.uiTitlePanel_Operation1.Padding = new System.Windows.Forms.Padding(0, 35, 0, 0);
            this.uiTitlePanel_Operation1.ShowText = false;
            this.uiTitlePanel_Operation1.Size = new System.Drawing.Size(229, 165);
            this.uiTitlePanel_Operation1.TabIndex = 3;
            this.uiTitlePanel_Operation1.Text = "操作";
            this.uiTitlePanel_Operation1.TextAlignment = System.Drawing.ContentAlignment.MiddleCenter;
            this.uiTitlePanel_Operation1.ZoomScaleRect = new System.Drawing.Rectangle(0, 0, 0, 0);
            // 
            // uiSymbolButton_ReadDataSource
            // 
            this.uiSymbolButton_ReadDataSource.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.uiSymbolButton_ReadDataSource.Location = new System.Drawing.Point(116, 54);
            this.uiSymbolButton_ReadDataSource.MinimumSize = new System.Drawing.Size(1, 1);
            this.uiSymbolButton_ReadDataSource.Name = "uiSymbolButton_ReadDataSource";
            this.uiSymbolButton_ReadDataSource.Size = new System.Drawing.Size(98, 35);
            this.uiSymbolButton_ReadDataSource.Symbol = 57433;
            this.uiSymbolButton_ReadDataSource.TabIndex = 2;
            this.uiSymbolButton_ReadDataSource.Text = "读取";
            this.uiSymbolButton_ReadDataSource.ZoomScaleRect = new System.Drawing.Rectangle(0, 0, 0, 0);
            this.uiSymbolButton_ReadDataSource.Click += new System.EventHandler(this.uiSymbolButton_ReadDataSource_Click);
            // 
            // uiSymbolButton_Verify
            // 
            this.uiSymbolButton_Verify.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.uiSymbolButton_Verify.Location = new System.Drawing.Point(14, 104);
            this.uiSymbolButton_Verify.MinimumSize = new System.Drawing.Size(1, 1);
            this.uiSymbolButton_Verify.Name = "uiSymbolButton_Verify";
            this.uiSymbolButton_Verify.Size = new System.Drawing.Size(200, 35);
            this.uiSymbolButton_Verify.Symbol = 61667;
            this.uiSymbolButton_Verify.TabIndex = 1;
            this.uiSymbolButton_Verify.Text = "验证";
            this.uiSymbolButton_Verify.ZoomScaleRect = new System.Drawing.Rectangle(0, 0, 0, 0);
            this.uiSymbolButton_Verify.Click += new System.EventHandler(this.uiSymbolButton_Verify_Click);
            // 
            // uiSymbolButton_SaveDataSource
            // 
            this.uiSymbolButton_SaveDataSource.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.uiSymbolButton_SaveDataSource.Location = new System.Drawing.Point(14, 54);
            this.uiSymbolButton_SaveDataSource.MinimumSize = new System.Drawing.Size(1, 1);
            this.uiSymbolButton_SaveDataSource.Name = "uiSymbolButton_SaveDataSource";
            this.uiSymbolButton_SaveDataSource.Size = new System.Drawing.Size(98, 35);
            this.uiSymbolButton_SaveDataSource.Symbol = 57432;
            this.uiSymbolButton_SaveDataSource.TabIndex = 0;
            this.uiSymbolButton_SaveDataSource.Text = "保存";
            this.uiSymbolButton_SaveDataSource.ZoomScaleRect = new System.Drawing.Rectangle(0, 0, 0, 0);
            this.uiSymbolButton_SaveDataSource.Click += new System.EventHandler(this.uiSymbolButton_SaveDataSource_Click);
            // 
            // uiTitlePanel_LibID
            // 
            this.uiTitlePanel_LibID.Controls.Add(this.uiIntegerUpDown_LibID);
            this.uiTitlePanel_LibID.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.uiTitlePanel_LibID.Location = new System.Drawing.Point(4, 191);
            this.uiTitlePanel_LibID.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.uiTitlePanel_LibID.MinimumSize = new System.Drawing.Size(1, 1);
            this.uiTitlePanel_LibID.Name = "uiTitlePanel_LibID";
            this.uiTitlePanel_LibID.Padding = new System.Windows.Forms.Padding(0, 35, 0, 0);
            this.uiTitlePanel_LibID.ShowText = false;
            this.uiTitlePanel_LibID.Size = new System.Drawing.Size(229, 80);
            this.uiTitlePanel_LibID.TabIndex = 2;
            this.uiTitlePanel_LibID.Text = "Lib ID";
            this.uiTitlePanel_LibID.TextAlignment = System.Drawing.ContentAlignment.MiddleCenter;
            this.uiTitlePanel_LibID.ZoomScaleRect = new System.Drawing.Rectangle(0, 0, 0, 0);
            // 
            // uiIntegerUpDown_LibID
            // 
            this.uiIntegerUpDown_LibID.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.uiIntegerUpDown_LibID.Location = new System.Drawing.Point(14, 43);
            this.uiIntegerUpDown_LibID.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.uiIntegerUpDown_LibID.Maximum = 1000000;
            this.uiIntegerUpDown_LibID.Minimum = 0;
            this.uiIntegerUpDown_LibID.MinimumSize = new System.Drawing.Size(100, 0);
            this.uiIntegerUpDown_LibID.Name = "uiIntegerUpDown_LibID";
            this.uiIntegerUpDown_LibID.ShowText = false;
            this.uiIntegerUpDown_LibID.Size = new System.Drawing.Size(200, 29);
            this.uiIntegerUpDown_LibID.TabIndex = 0;
            this.uiIntegerUpDown_LibID.Text = null;
            this.uiIntegerUpDown_LibID.TextAlignment = System.Drawing.ContentAlignment.MiddleCenter;
            this.uiIntegerUpDown_LibID.Value = 100000;
            this.uiIntegerUpDown_LibID.ZoomScaleRect = new System.Drawing.Rectangle(0, 0, 0, 0);
            // 
            // uiTitlePanel_LibInfo
            // 
            this.uiTitlePanel_LibInfo.Controls.Add(this.uiSymbolLabel_LibAvailableSeatsNum);
            this.uiTitlePanel_LibInfo.Controls.Add(this.uiSymbolLabel_LibFloor);
            this.uiTitlePanel_LibInfo.Controls.Add(this.uiSymbolLabel_LibName);
            this.uiTitlePanel_LibInfo.Controls.Add(this.uiSymbolLabel_LibStatus);
            this.uiTitlePanel_LibInfo.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.uiTitlePanel_LibInfo.Location = new System.Drawing.Point(241, 191);
            this.uiTitlePanel_LibInfo.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.uiTitlePanel_LibInfo.MinimumSize = new System.Drawing.Size(1, 1);
            this.uiTitlePanel_LibInfo.Name = "uiTitlePanel_LibInfo";
            this.uiTitlePanel_LibInfo.Padding = new System.Windows.Forms.Padding(0, 35, 0, 0);
            this.uiTitlePanel_LibInfo.ShowText = false;
            this.uiTitlePanel_LibInfo.Size = new System.Drawing.Size(388, 255);
            this.uiTitlePanel_LibInfo.TabIndex = 1;
            this.uiTitlePanel_LibInfo.Text = "图书馆(室)信息";
            this.uiTitlePanel_LibInfo.TextAlignment = System.Drawing.ContentAlignment.MiddleCenter;
            this.uiTitlePanel_LibInfo.ZoomScaleRect = new System.Drawing.Rectangle(0, 0, 0, 0);
            // 
            // uiSymbolLabel_LibAvailableSeatsNum
            // 
            this.uiSymbolLabel_LibAvailableSeatsNum.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.uiSymbolLabel_LibAvailableSeatsNum.ImageInterval = 1;
            this.uiSymbolLabel_LibAvailableSeatsNum.Location = new System.Drawing.Point(14, 195);
            this.uiSymbolLabel_LibAvailableSeatsNum.MinimumSize = new System.Drawing.Size(1, 1);
            this.uiSymbolLabel_LibAvailableSeatsNum.Name = "uiSymbolLabel_LibAvailableSeatsNum";
            this.uiSymbolLabel_LibAvailableSeatsNum.Padding = new System.Windows.Forms.Padding(26, 0, 0, 0);
            this.uiSymbolLabel_LibAvailableSeatsNum.Size = new System.Drawing.Size(354, 35);
            this.uiSymbolLabel_LibAvailableSeatsNum.Symbol = 61720;
            this.uiSymbolLabel_LibAvailableSeatsNum.TabIndex = 3;
            this.uiSymbolLabel_LibAvailableSeatsNum.Text = "图书馆(室)余座：";
            this.uiSymbolLabel_LibAvailableSeatsNum.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.uiSymbolLabel_LibAvailableSeatsNum.ZoomScaleRect = new System.Drawing.Rectangle(0, 0, 0, 0);
            // 
            // uiSymbolLabel_LibFloor
            // 
            this.uiSymbolLabel_LibFloor.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.uiSymbolLabel_LibFloor.ImageInterval = 1;
            this.uiSymbolLabel_LibFloor.Location = new System.Drawing.Point(14, 148);
            this.uiSymbolLabel_LibFloor.MinimumSize = new System.Drawing.Size(1, 1);
            this.uiSymbolLabel_LibFloor.Name = "uiSymbolLabel_LibFloor";
            this.uiSymbolLabel_LibFloor.Padding = new System.Windows.Forms.Padding(26, 0, 0, 0);
            this.uiSymbolLabel_LibFloor.Size = new System.Drawing.Size(354, 35);
            this.uiSymbolLabel_LibFloor.Symbol = 61505;
            this.uiSymbolLabel_LibFloor.TabIndex = 2;
            this.uiSymbolLabel_LibFloor.Text = "图书馆(室)楼层：";
            this.uiSymbolLabel_LibFloor.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.uiSymbolLabel_LibFloor.ZoomScaleRect = new System.Drawing.Rectangle(0, 0, 0, 0);
            // 
            // uiSymbolLabel_LibName
            // 
            this.uiSymbolLabel_LibName.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.uiSymbolLabel_LibName.ImageInterval = 1;
            this.uiSymbolLabel_LibName.Location = new System.Drawing.Point(14, 101);
            this.uiSymbolLabel_LibName.MinimumSize = new System.Drawing.Size(1, 1);
            this.uiSymbolLabel_LibName.Name = "uiSymbolLabel_LibName";
            this.uiSymbolLabel_LibName.Padding = new System.Windows.Forms.Padding(26, 0, 0, 0);
            this.uiSymbolLabel_LibName.Size = new System.Drawing.Size(354, 35);
            this.uiSymbolLabel_LibName.Symbol = 61687;
            this.uiSymbolLabel_LibName.TabIndex = 1;
            this.uiSymbolLabel_LibName.Text = "图书馆(室)名称：";
            this.uiSymbolLabel_LibName.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.uiSymbolLabel_LibName.ZoomScaleRect = new System.Drawing.Rectangle(0, 0, 0, 0);
            // 
            // uiSymbolLabel_LibStatus
            // 
            this.uiSymbolLabel_LibStatus.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.uiSymbolLabel_LibStatus.ImageInterval = 1;
            this.uiSymbolLabel_LibStatus.Location = new System.Drawing.Point(14, 54);
            this.uiSymbolLabel_LibStatus.MinimumSize = new System.Drawing.Size(1, 1);
            this.uiSymbolLabel_LibStatus.Name = "uiSymbolLabel_LibStatus";
            this.uiSymbolLabel_LibStatus.Padding = new System.Windows.Forms.Padding(26, 0, 0, 0);
            this.uiSymbolLabel_LibStatus.Size = new System.Drawing.Size(354, 35);
            this.uiSymbolLabel_LibStatus.Symbol = 61568;
            this.uiSymbolLabel_LibStatus.TabIndex = 0;
            this.uiSymbolLabel_LibStatus.Text = "状态：";
            this.uiSymbolLabel_LibStatus.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.uiSymbolLabel_LibStatus.ZoomScaleRect = new System.Drawing.Rectangle(0, 0, 0, 0);
            // 
            // uiTitlePanel_Cookie
            // 
            this.uiTitlePanel_Cookie.Controls.Add(this.uiTextBox_Cookies);
            this.uiTitlePanel_Cookie.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.uiTitlePanel_Cookie.Location = new System.Drawing.Point(4, 12);
            this.uiTitlePanel_Cookie.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.uiTitlePanel_Cookie.MinimumSize = new System.Drawing.Size(1, 1);
            this.uiTitlePanel_Cookie.Name = "uiTitlePanel_Cookie";
            this.uiTitlePanel_Cookie.Padding = new System.Windows.Forms.Padding(0, 35, 0, 0);
            this.uiTitlePanel_Cookie.ShowText = false;
            this.uiTitlePanel_Cookie.Size = new System.Drawing.Size(625, 169);
            this.uiTitlePanel_Cookie.TabIndex = 0;
            this.uiTitlePanel_Cookie.Text = "Cookie";
            this.uiTitlePanel_Cookie.TextAlignment = System.Drawing.ContentAlignment.MiddleCenter;
            this.uiTitlePanel_Cookie.ZoomScaleRect = new System.Drawing.Rectangle(0, 0, 0, 0);
            // 
            // uiTextBox_Cookies
            // 
            this.uiTextBox_Cookies.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.uiTextBox_Cookies.Location = new System.Drawing.Point(4, 40);
            this.uiTextBox_Cookies.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.uiTextBox_Cookies.Maximum = 1500D;
            this.uiTextBox_Cookies.MinimumSize = new System.Drawing.Size(1, 16);
            this.uiTextBox_Cookies.Multiline = true;
            this.uiTextBox_Cookies.Name = "uiTextBox_Cookies";
            this.uiTextBox_Cookies.RectSides = System.Windows.Forms.ToolStripStatusLabelBorderSides.None;
            this.uiTextBox_Cookies.ShowScrollBar = true;
            this.uiTextBox_Cookies.ShowText = false;
            this.uiTextBox_Cookies.Size = new System.Drawing.Size(617, 124);
            this.uiTextBox_Cookies.TabIndex = 0;
            this.uiTextBox_Cookies.TextAlignment = System.Drawing.ContentAlignment.MiddleLeft;
            this.uiTextBox_Cookies.Watermark = "如不会填写Cookie请阅读教程并使用自动识别功能";
            this.uiTextBox_Cookies.ZoomScaleRect = new System.Drawing.Rectangle(0, 0, 0, 0);
            // 
            // tabPage_AutoIdentify
            // 
            this.tabPage_AutoIdentify.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(243)))), ((int)(((byte)(249)))), ((int)(((byte)(255)))));
            this.tabPage_AutoIdentify.Controls.Add(this.uiTitlePanel_Operation2);
            this.tabPage_AutoIdentify.Controls.Add(this.uiTitlePanel_Session);
            this.tabPage_AutoIdentify.Location = new System.Drawing.Point(0, 40);
            this.tabPage_AutoIdentify.Name = "tabPage_AutoIdentify";
            this.tabPage_AutoIdentify.Size = new System.Drawing.Size(200, 60);
            this.tabPage_AutoIdentify.TabIndex = 1;
            this.tabPage_AutoIdentify.Text = "自动识别";
            // 
            // uiTitlePanel_Operation2
            // 
            this.uiTitlePanel_Operation2.Controls.Add(this.uiSymbolButton_AutoIdentify);
            this.uiTitlePanel_Operation2.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.uiTitlePanel_Operation2.Location = new System.Drawing.Point(364, 338);
            this.uiTitlePanel_Operation2.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.uiTitlePanel_Operation2.MinimumSize = new System.Drawing.Size(1, 1);
            this.uiTitlePanel_Operation2.Name = "uiTitlePanel_Operation2";
            this.uiTitlePanel_Operation2.Padding = new System.Windows.Forms.Padding(0, 35, 0, 0);
            this.uiTitlePanel_Operation2.ShowText = false;
            this.uiTitlePanel_Operation2.Size = new System.Drawing.Size(265, 105);
            this.uiTitlePanel_Operation2.TabIndex = 1;
            this.uiTitlePanel_Operation2.Text = "操作";
            this.uiTitlePanel_Operation2.TextAlignment = System.Drawing.ContentAlignment.MiddleCenter;
            this.uiTitlePanel_Operation2.ZoomScaleRect = new System.Drawing.Rectangle(0, 0, 0, 0);
            // 
            // uiSymbolButton_AutoIdentify
            // 
            this.uiSymbolButton_AutoIdentify.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.uiSymbolButton_AutoIdentify.Location = new System.Drawing.Point(32, 48);
            this.uiSymbolButton_AutoIdentify.MinimumSize = new System.Drawing.Size(1, 1);
            this.uiSymbolButton_AutoIdentify.Name = "uiSymbolButton_AutoIdentify";
            this.uiSymbolButton_AutoIdentify.Size = new System.Drawing.Size(206, 37);
            this.uiSymbolButton_AutoIdentify.Symbol = 61648;
            this.uiSymbolButton_AutoIdentify.TabIndex = 0;
            this.uiSymbolButton_AutoIdentify.Text = "识别并自动填写";
            this.uiSymbolButton_AutoIdentify.ZoomScaleRect = new System.Drawing.Rectangle(0, 0, 0, 0);
            this.uiSymbolButton_AutoIdentify.Click += new System.EventHandler(this.uiSymbolButton_AutoIdentify_Click);
            // 
            // uiTitlePanel_Session
            // 
            this.uiTitlePanel_Session.Controls.Add(this.uiTextBox_Session);
            this.uiTitlePanel_Session.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.uiTitlePanel_Session.Location = new System.Drawing.Point(4, 12);
            this.uiTitlePanel_Session.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.uiTitlePanel_Session.MinimumSize = new System.Drawing.Size(1, 1);
            this.uiTitlePanel_Session.Name = "uiTitlePanel_Session";
            this.uiTitlePanel_Session.Padding = new System.Windows.Forms.Padding(0, 35, 0, 0);
            this.uiTitlePanel_Session.ShowText = false;
            this.uiTitlePanel_Session.Size = new System.Drawing.Size(625, 316);
            this.uiTitlePanel_Session.TabIndex = 0;
            this.uiTitlePanel_Session.Text = "待识别内容 - Fiddler Session";
            this.uiTitlePanel_Session.TextAlignment = System.Drawing.ContentAlignment.MiddleCenter;
            this.uiTitlePanel_Session.ZoomScaleRect = new System.Drawing.Rectangle(0, 0, 0, 0);
            // 
            // uiTextBox_Session
            // 
            this.uiTextBox_Session.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.uiTextBox_Session.Location = new System.Drawing.Point(4, 40);
            this.uiTextBox_Session.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.uiTextBox_Session.MinimumSize = new System.Drawing.Size(1, 16);
            this.uiTextBox_Session.Multiline = true;
            this.uiTextBox_Session.Name = "uiTextBox_Session";
            this.uiTextBox_Session.RectSides = System.Windows.Forms.ToolStripStatusLabelBorderSides.None;
            this.uiTextBox_Session.ShowScrollBar = true;
            this.uiTextBox_Session.ShowText = false;
            this.uiTextBox_Session.Size = new System.Drawing.Size(617, 271);
            this.uiTextBox_Session.TabIndex = 0;
            this.uiTextBox_Session.TextAlignment = System.Drawing.ContentAlignment.MiddleLeft;
            this.uiTextBox_Session.Watermark = "填入Fiddler中复制来的Session";
            this.uiTextBox_Session.ZoomScaleRect = new System.Drawing.Rectangle(0, 0, 0, 0);
            // 
            // tabPage_BuiltinQuerySyntax
            // 
            this.tabPage_BuiltinQuerySyntax.AutoScroll = true;
            this.tabPage_BuiltinQuerySyntax.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(243)))), ((int)(((byte)(249)))), ((int)(((byte)(255)))));
            this.tabPage_BuiltinQuerySyntax.Controls.Add(this.uiTitlePanel_ReserveSeatSyntax);
            this.tabPage_BuiltinQuerySyntax.Controls.Add(this.uiTitlePanel_QueryLibInfoSyntax);
            this.tabPage_BuiltinQuerySyntax.Location = new System.Drawing.Point(0, 40);
            this.tabPage_BuiltinQuerySyntax.Name = "tabPage_BuiltinQuerySyntax";
            this.tabPage_BuiltinQuerySyntax.Size = new System.Drawing.Size(633, 451);
            this.tabPage_BuiltinQuerySyntax.TabIndex = 2;
            this.tabPage_BuiltinQuerySyntax.Text = "内置查询语法";
            // 
            // uiTitlePanel_ReserveSeatSyntax
            // 
            this.uiTitlePanel_ReserveSeatSyntax.Controls.Add(this.uiTextBox_ReserveSeatSyntax);
            this.uiTitlePanel_ReserveSeatSyntax.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.uiTitlePanel_ReserveSeatSyntax.Location = new System.Drawing.Point(7, 190);
            this.uiTitlePanel_ReserveSeatSyntax.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.uiTitlePanel_ReserveSeatSyntax.MinimumSize = new System.Drawing.Size(1, 1);
            this.uiTitlePanel_ReserveSeatSyntax.Name = "uiTitlePanel_ReserveSeatSyntax";
            this.uiTitlePanel_ReserveSeatSyntax.Padding = new System.Windows.Forms.Padding(0, 35, 0, 0);
            this.uiTitlePanel_ReserveSeatSyntax.ShowText = false;
            this.uiTitlePanel_ReserveSeatSyntax.Size = new System.Drawing.Size(608, 168);
            this.uiTitlePanel_ReserveSeatSyntax.TabIndex = 1;
            this.uiTitlePanel_ReserveSeatSyntax.Text = "预定指定座位语法(Json)：替换Lib ID和座位x,y坐标";
            this.uiTitlePanel_ReserveSeatSyntax.TextAlignment = System.Drawing.ContentAlignment.MiddleCenter;
            this.uiTitlePanel_ReserveSeatSyntax.ZoomScaleRect = new System.Drawing.Rectangle(0, 0, 0, 0);
            // 
            // uiTextBox_ReserveSeatSyntax
            // 
            this.uiTextBox_ReserveSeatSyntax.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.uiTextBox_ReserveSeatSyntax.Location = new System.Drawing.Point(4, 40);
            this.uiTextBox_ReserveSeatSyntax.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.uiTextBox_ReserveSeatSyntax.MinimumSize = new System.Drawing.Size(1, 16);
            this.uiTextBox_ReserveSeatSyntax.Multiline = true;
            this.uiTextBox_ReserveSeatSyntax.Name = "uiTextBox_ReserveSeatSyntax";
            this.uiTextBox_ReserveSeatSyntax.ShowScrollBar = true;
            this.uiTextBox_ReserveSeatSyntax.ShowText = false;
            this.uiTextBox_ReserveSeatSyntax.Size = new System.Drawing.Size(601, 123);
            this.uiTextBox_ReserveSeatSyntax.TabIndex = 0;
            this.uiTextBox_ReserveSeatSyntax.Text = resources.GetString("uiTextBox_ReserveSeatSyntax.Text");
            this.uiTextBox_ReserveSeatSyntax.TextAlignment = System.Drawing.ContentAlignment.MiddleLeft;
            this.uiTextBox_ReserveSeatSyntax.Watermark = "";
            this.uiTextBox_ReserveSeatSyntax.ZoomScaleRect = new System.Drawing.Rectangle(0, 0, 0, 0);
            // 
            // uiTitlePanel_QueryLibInfoSyntax
            // 
            this.uiTitlePanel_QueryLibInfoSyntax.Controls.Add(this.uiTextBox_QueryLibInfoSyntax);
            this.uiTitlePanel_QueryLibInfoSyntax.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.uiTitlePanel_QueryLibInfoSyntax.Location = new System.Drawing.Point(4, 12);
            this.uiTitlePanel_QueryLibInfoSyntax.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.uiTitlePanel_QueryLibInfoSyntax.MinimumSize = new System.Drawing.Size(1, 1);
            this.uiTitlePanel_QueryLibInfoSyntax.Name = "uiTitlePanel_QueryLibInfoSyntax";
            this.uiTitlePanel_QueryLibInfoSyntax.Padding = new System.Windows.Forms.Padding(0, 35, 0, 0);
            this.uiTitlePanel_QueryLibInfoSyntax.ShowText = false;
            this.uiTitlePanel_QueryLibInfoSyntax.Size = new System.Drawing.Size(608, 168);
            this.uiTitlePanel_QueryLibInfoSyntax.TabIndex = 0;
            this.uiTitlePanel_QueryLibInfoSyntax.Text = "查询图书馆(室)所有信息语法(Json)：替换入Lib ID";
            this.uiTitlePanel_QueryLibInfoSyntax.TextAlignment = System.Drawing.ContentAlignment.MiddleCenter;
            this.uiTitlePanel_QueryLibInfoSyntax.ZoomScaleRect = new System.Drawing.Rectangle(0, 0, 0, 0);
            // 
            // uiTextBox_QueryLibInfoSyntax
            // 
            this.uiTextBox_QueryLibInfoSyntax.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.uiTextBox_QueryLibInfoSyntax.Location = new System.Drawing.Point(3, 39);
            this.uiTextBox_QueryLibInfoSyntax.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.uiTextBox_QueryLibInfoSyntax.MaxLength = 1000;
            this.uiTextBox_QueryLibInfoSyntax.MinimumSize = new System.Drawing.Size(1, 16);
            this.uiTextBox_QueryLibInfoSyntax.Multiline = true;
            this.uiTextBox_QueryLibInfoSyntax.Name = "uiTextBox_QueryLibInfoSyntax";
            this.uiTextBox_QueryLibInfoSyntax.RadiusSides = Sunny.UI.UICornerRadiusSides.None;
            this.uiTextBox_QueryLibInfoSyntax.RectSides = System.Windows.Forms.ToolStripStatusLabelBorderSides.None;
            this.uiTextBox_QueryLibInfoSyntax.ShowScrollBar = true;
            this.uiTextBox_QueryLibInfoSyntax.ShowText = false;
            this.uiTextBox_QueryLibInfoSyntax.Size = new System.Drawing.Size(600, 124);
            this.uiTextBox_QueryLibInfoSyntax.TabIndex = 0;
            this.uiTextBox_QueryLibInfoSyntax.Text = resources.GetString("uiTextBox_QueryLibInfoSyntax.Text");
            this.uiTextBox_QueryLibInfoSyntax.TextAlignment = System.Drawing.ContentAlignment.MiddleLeft;
            this.uiTextBox_QueryLibInfoSyntax.Watermark = "";
            this.uiTextBox_QueryLibInfoSyntax.ZoomScaleRect = new System.Drawing.Rectangle(0, 0, 0, 0);
            // 
            // FDataSource
            // 
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            this.ClientSize = new System.Drawing.Size(657, 515);
            this.Controls.Add(this.uiTabControl_DataSource);
            this.MaximumSize = new System.Drawing.Size(657, 515);
            this.Name = "FDataSource";
            this.Symbol = 104;
            this.SymbolSize = 30;
            this.Text = "数据源";
            this.uiTabControl_DataSource.ResumeLayout(false);
            this.tabPage_DirectFilling.ResumeLayout(false);
            this.uiTitlePanel_Operation1.ResumeLayout(false);
            this.uiTitlePanel_LibID.ResumeLayout(false);
            this.uiTitlePanel_LibInfo.ResumeLayout(false);
            this.uiTitlePanel_Cookie.ResumeLayout(false);
            this.tabPage_AutoIdentify.ResumeLayout(false);
            this.uiTitlePanel_Operation2.ResumeLayout(false);
            this.uiTitlePanel_Session.ResumeLayout(false);
            this.tabPage_BuiltinQuerySyntax.ResumeLayout(false);
            this.uiTitlePanel_ReserveSeatSyntax.ResumeLayout(false);
            this.uiTitlePanel_QueryLibInfoSyntax.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private Sunny.UI.UITabControl uiTabControl_DataSource;
        private TabPage tabPage_DirectFilling;
        private TabPage tabPage_AutoIdentify;
        private Sunny.UI.UITitlePanel uiTitlePanel_Cookie;
        private TabPage tabPage_BuiltinQuerySyntax;
        private Sunny.UI.UITitlePanel uiTitlePanel_LibInfo;
        private Sunny.UI.UITitlePanel uiTitlePanel_LibID;
        private Sunny.UI.UIIntegerUpDown uiIntegerUpDown_LibID;
        private Sunny.UI.UITitlePanel uiTitlePanel_Operation1;
        private Sunny.UI.UISymbolButton uiSymbolButton_ReadDataSource;
        private Sunny.UI.UISymbolButton uiSymbolButton_Verify;
        private Sunny.UI.UISymbolButton uiSymbolButton_SaveDataSource;
        private Sunny.UI.UISymbolLabel uiSymbolLabel_LibStatus;
        private Sunny.UI.UISymbolLabel uiSymbolLabel_LibAvailableSeatsNum;
        private Sunny.UI.UISymbolLabel uiSymbolLabel_LibFloor;
        private Sunny.UI.UISymbolLabel uiSymbolLabel_LibName;
        public Sunny.UI.UITextBox uiTextBox_Cookies;
        private Sunny.UI.UITitlePanel uiTitlePanel_QueryLibInfoSyntax;
        public Sunny.UI.UITextBox uiTextBox_QueryLibInfoSyntax;
        private Sunny.UI.UITitlePanel uiTitlePanel_Session;
        private Sunny.UI.UITitlePanel uiTitlePanel_Operation2;
        private Sunny.UI.UISymbolButton uiSymbolButton_AutoIdentify;
        private Sunny.UI.UITextBox uiTextBox_Session;
        private Sunny.UI.UITitlePanel uiTitlePanel_ReserveSeatSyntax;
        private Sunny.UI.UITextBox uiTextBox_ReserveSeatSyntax;
    }
}