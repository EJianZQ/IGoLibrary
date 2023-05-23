namespace IGoLibrary_Winform.Pages
{
    partial class FGrabSeat
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
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle1 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle2 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle3 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle4 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle5 = new System.Windows.Forms.DataGridViewCellStyle();
            this.uiTabControl_GrabSeat = new Sunny.UI.UITabControl();
            this.tabPage_GrabController = new System.Windows.Forms.TabPage();
            this.uiTitlePanel_SelectedGrabSeats = new Sunny.UI.UITitlePanel();
            this.uiListBox_SelectedGrabSeats = new Sunny.UI.UIListBox();
            this.uiTitlePanel_Setting = new Sunny.UI.UITitlePanel();
            this.uiTimePicker_TimingTime = new Sunny.UI.UITimePicker();
            this.uiMarkLabel2 = new Sunny.UI.UIMarkLabel();
            this.uiComboBox_GrabSeatMode = new Sunny.UI.UIComboBox();
            this.uiMarkLabel1 = new Sunny.UI.UIMarkLabel();
            this.uiTitlePanel_GrabSeatSwitch = new Sunny.UI.UITitlePanel();
            this.uiSwitch_GrabSeatSwitch = new Sunny.UI.UISwitch();
            this.uiTitlePanel_RealTimeData = new Sunny.UI.UITitlePanel();
            this.uiTextBox_RealTimeData = new Sunny.UI.UITextBox();
            this.tabPage_SeatInfoList = new System.Windows.Forms.TabPage();
            this.uiSymbolButton_LoadFavorite = new Sunny.UI.UISymbolButton();
            this.uiSymbolButton_Favorite = new Sunny.UI.UISymbolButton();
            this.uiSymbolButton_AddGrabList = new Sunny.UI.UISymbolButton();
            this.uiDataGridView_SeatInfo = new Sunny.UI.UIDataGridView();
            this.uiTabControl_GrabSeat.SuspendLayout();
            this.tabPage_GrabController.SuspendLayout();
            this.uiTitlePanel_SelectedGrabSeats.SuspendLayout();
            this.uiTitlePanel_Setting.SuspendLayout();
            this.uiTitlePanel_GrabSeatSwitch.SuspendLayout();
            this.uiTitlePanel_RealTimeData.SuspendLayout();
            this.tabPage_SeatInfoList.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.uiDataGridView_SeatInfo)).BeginInit();
            this.SuspendLayout();
            // 
            // uiTabControl_GrabSeat
            // 
            this.uiTabControl_GrabSeat.Controls.Add(this.tabPage_GrabController);
            this.uiTabControl_GrabSeat.Controls.Add(this.tabPage_SeatInfoList);
            this.uiTabControl_GrabSeat.DrawMode = System.Windows.Forms.TabDrawMode.OwnerDrawFixed;
            this.uiTabControl_GrabSeat.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.uiTabControl_GrabSeat.Frame = null;
            this.uiTabControl_GrabSeat.ItemSize = new System.Drawing.Size(150, 40);
            this.uiTabControl_GrabSeat.Location = new System.Drawing.Point(12, 12);
            this.uiTabControl_GrabSeat.MainPage = "";
            this.uiTabControl_GrabSeat.MenuStyle = Sunny.UI.UIMenuStyle.Custom;
            this.uiTabControl_GrabSeat.Name = "uiTabControl_GrabSeat";
            this.uiTabControl_GrabSeat.SelectedIndex = 0;
            this.uiTabControl_GrabSeat.Size = new System.Drawing.Size(633, 491);
            this.uiTabControl_GrabSeat.SizeMode = System.Windows.Forms.TabSizeMode.Fixed;
            this.uiTabControl_GrabSeat.TabBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(243)))), ((int)(((byte)(249)))), ((int)(((byte)(255)))));
            this.uiTabControl_GrabSeat.TabIndex = 0;
            this.uiTabControl_GrabSeat.TabSelectedColor = System.Drawing.Color.FromArgb(((int)(((byte)(243)))), ((int)(((byte)(249)))), ((int)(((byte)(255)))));
            this.uiTabControl_GrabSeat.TabUnSelectedForeColor = System.Drawing.Color.Black;
            this.uiTabControl_GrabSeat.ZoomScaleRect = new System.Drawing.Rectangle(0, 0, 0, 0);
            // 
            // tabPage_GrabController
            // 
            this.tabPage_GrabController.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(243)))), ((int)(((byte)(249)))), ((int)(((byte)(255)))));
            this.tabPage_GrabController.Controls.Add(this.uiTitlePanel_SelectedGrabSeats);
            this.tabPage_GrabController.Controls.Add(this.uiTitlePanel_Setting);
            this.tabPage_GrabController.Controls.Add(this.uiTitlePanel_GrabSeatSwitch);
            this.tabPage_GrabController.Controls.Add(this.uiTitlePanel_RealTimeData);
            this.tabPage_GrabController.Location = new System.Drawing.Point(0, 40);
            this.tabPage_GrabController.Name = "tabPage_GrabController";
            this.tabPage_GrabController.Size = new System.Drawing.Size(633, 451);
            this.tabPage_GrabController.TabIndex = 0;
            this.tabPage_GrabController.Text = "抢座控制台";
            // 
            // uiTitlePanel_SelectedGrabSeats
            // 
            this.uiTitlePanel_SelectedGrabSeats.Controls.Add(this.uiListBox_SelectedGrabSeats);
            this.uiTitlePanel_SelectedGrabSeats.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.uiTitlePanel_SelectedGrabSeats.Location = new System.Drawing.Point(443, 12);
            this.uiTitlePanel_SelectedGrabSeats.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.uiTitlePanel_SelectedGrabSeats.MinimumSize = new System.Drawing.Size(1, 1);
            this.uiTitlePanel_SelectedGrabSeats.Name = "uiTitlePanel_SelectedGrabSeats";
            this.uiTitlePanel_SelectedGrabSeats.Padding = new System.Windows.Forms.Padding(0, 35, 0, 0);
            this.uiTitlePanel_SelectedGrabSeats.ShowText = false;
            this.uiTitlePanel_SelectedGrabSeats.Size = new System.Drawing.Size(186, 118);
            this.uiTitlePanel_SelectedGrabSeats.TabIndex = 3;
            this.uiTitlePanel_SelectedGrabSeats.Text = "待抢座位";
            this.uiTitlePanel_SelectedGrabSeats.TextAlignment = System.Drawing.ContentAlignment.MiddleCenter;
            this.uiTitlePanel_SelectedGrabSeats.ZoomScaleRect = new System.Drawing.Rectangle(0, 0, 0, 0);
            // 
            // uiListBox_SelectedGrabSeats
            // 
            this.uiListBox_SelectedGrabSeats.FillColor = System.Drawing.Color.White;
            this.uiListBox_SelectedGrabSeats.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.uiListBox_SelectedGrabSeats.Location = new System.Drawing.Point(4, 40);
            this.uiListBox_SelectedGrabSeats.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.uiListBox_SelectedGrabSeats.MinimumSize = new System.Drawing.Size(1, 1);
            this.uiListBox_SelectedGrabSeats.Name = "uiListBox_SelectedGrabSeats";
            this.uiListBox_SelectedGrabSeats.Padding = new System.Windows.Forms.Padding(2);
            this.uiListBox_SelectedGrabSeats.RectSides = System.Windows.Forms.ToolStripStatusLabelBorderSides.None;
            this.uiListBox_SelectedGrabSeats.ShowText = false;
            this.uiListBox_SelectedGrabSeats.Size = new System.Drawing.Size(178, 73);
            this.uiListBox_SelectedGrabSeats.TabIndex = 0;
            this.uiListBox_SelectedGrabSeats.Text = null;
            this.uiListBox_SelectedGrabSeats.ZoomScaleRect = new System.Drawing.Rectangle(0, 0, 0, 0);
            // 
            // uiTitlePanel_Setting
            // 
            this.uiTitlePanel_Setting.Controls.Add(this.uiTimePicker_TimingTime);
            this.uiTitlePanel_Setting.Controls.Add(this.uiMarkLabel2);
            this.uiTitlePanel_Setting.Controls.Add(this.uiComboBox_GrabSeatMode);
            this.uiTitlePanel_Setting.Controls.Add(this.uiMarkLabel1);
            this.uiTitlePanel_Setting.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.uiTitlePanel_Setting.Location = new System.Drawing.Point(443, 140);
            this.uiTitlePanel_Setting.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.uiTitlePanel_Setting.MinimumSize = new System.Drawing.Size(1, 1);
            this.uiTitlePanel_Setting.Name = "uiTitlePanel_Setting";
            this.uiTitlePanel_Setting.Padding = new System.Windows.Forms.Padding(0, 35, 0, 0);
            this.uiTitlePanel_Setting.ShowText = false;
            this.uiTitlePanel_Setting.Size = new System.Drawing.Size(186, 180);
            this.uiTitlePanel_Setting.TabIndex = 2;
            this.uiTitlePanel_Setting.Text = "设置";
            this.uiTitlePanel_Setting.TextAlignment = System.Drawing.ContentAlignment.MiddleCenter;
            this.uiTitlePanel_Setting.ZoomScaleRect = new System.Drawing.Rectangle(0, 0, 0, 0);
            // 
            // uiTimePicker_TimingTime
            // 
            this.uiTimePicker_TimingTime.FillColor = System.Drawing.Color.White;
            this.uiTimePicker_TimingTime.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.uiTimePicker_TimingTime.Location = new System.Drawing.Point(13, 144);
            this.uiTimePicker_TimingTime.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.uiTimePicker_TimingTime.MaxLength = 8;
            this.uiTimePicker_TimingTime.MinimumSize = new System.Drawing.Size(63, 0);
            this.uiTimePicker_TimingTime.Name = "uiTimePicker_TimingTime";
            this.uiTimePicker_TimingTime.Padding = new System.Windows.Forms.Padding(0, 0, 30, 2);
            this.uiTimePicker_TimingTime.Size = new System.Drawing.Size(158, 29);
            this.uiTimePicker_TimingTime.SymbolDropDown = 61555;
            this.uiTimePicker_TimingTime.SymbolNormal = 61555;
            this.uiTimePicker_TimingTime.TabIndex = 3;
            this.uiTimePicker_TimingTime.Text = "00:00:00";
            this.uiTimePicker_TimingTime.TextAlignment = System.Drawing.ContentAlignment.MiddleLeft;
            this.uiTimePicker_TimingTime.Value = new System.DateTime(2023, 3, 7, 0, 0, 0, 0);
            this.uiTimePicker_TimingTime.Watermark = "";
            this.uiTimePicker_TimingTime.ZoomScaleRect = new System.Drawing.Rectangle(0, 0, 0, 0);
            // 
            // uiMarkLabel2
            // 
            this.uiMarkLabel2.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.uiMarkLabel2.Location = new System.Drawing.Point(13, 109);
            this.uiMarkLabel2.MarkPos = Sunny.UI.UIMarkLabel.UIMarkPos.Bottom;
            this.uiMarkLabel2.Name = "uiMarkLabel2";
            this.uiMarkLabel2.Padding = new System.Windows.Forms.Padding(0, 0, 0, 5);
            this.uiMarkLabel2.Size = new System.Drawing.Size(158, 30);
            this.uiMarkLabel2.TabIndex = 2;
            this.uiMarkLabel2.Text = "定时抢座";
            this.uiMarkLabel2.TextAlign = System.Drawing.ContentAlignment.TopCenter;
            this.uiMarkLabel2.ZoomScaleRect = new System.Drawing.Rectangle(0, 0, 0, 0);
            // 
            // uiComboBox_GrabSeatMode
            // 
            this.uiComboBox_GrabSeatMode.DataSource = null;
            this.uiComboBox_GrabSeatMode.DropDownStyle = Sunny.UI.UIDropDownStyle.DropDownList;
            this.uiComboBox_GrabSeatMode.FillColor = System.Drawing.Color.White;
            this.uiComboBox_GrabSeatMode.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.uiComboBox_GrabSeatMode.Items.AddRange(new object[] {
            "极限速度",
            "随机延迟",
            "延迟5秒"});
            this.uiComboBox_GrabSeatMode.Location = new System.Drawing.Point(13, 75);
            this.uiComboBox_GrabSeatMode.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.uiComboBox_GrabSeatMode.MaxDropDownItems = 3;
            this.uiComboBox_GrabSeatMode.MinimumSize = new System.Drawing.Size(63, 0);
            this.uiComboBox_GrabSeatMode.Name = "uiComboBox_GrabSeatMode";
            this.uiComboBox_GrabSeatMode.Padding = new System.Windows.Forms.Padding(0, 0, 30, 2);
            this.uiComboBox_GrabSeatMode.Size = new System.Drawing.Size(158, 29);
            this.uiComboBox_GrabSeatMode.TabIndex = 1;
            this.uiComboBox_GrabSeatMode.TextAlignment = System.Drawing.ContentAlignment.MiddleLeft;
            this.uiComboBox_GrabSeatMode.Watermark = "";
            this.uiComboBox_GrabSeatMode.ZoomScaleRect = new System.Drawing.Rectangle(0, 0, 0, 0);
            // 
            // uiMarkLabel1
            // 
            this.uiMarkLabel1.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.uiMarkLabel1.Location = new System.Drawing.Point(13, 40);
            this.uiMarkLabel1.MarkPos = Sunny.UI.UIMarkLabel.UIMarkPos.Bottom;
            this.uiMarkLabel1.Name = "uiMarkLabel1";
            this.uiMarkLabel1.Padding = new System.Windows.Forms.Padding(0, 0, 0, 5);
            this.uiMarkLabel1.Size = new System.Drawing.Size(158, 30);
            this.uiMarkLabel1.TabIndex = 0;
            this.uiMarkLabel1.Text = "抢座模式";
            this.uiMarkLabel1.TextAlign = System.Drawing.ContentAlignment.TopCenter;
            this.uiMarkLabel1.ZoomScaleRect = new System.Drawing.Rectangle(0, 0, 0, 0);
            // 
            // uiTitlePanel_GrabSeatSwitch
            // 
            this.uiTitlePanel_GrabSeatSwitch.Controls.Add(this.uiSwitch_GrabSeatSwitch);
            this.uiTitlePanel_GrabSeatSwitch.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.uiTitlePanel_GrabSeatSwitch.Location = new System.Drawing.Point(443, 330);
            this.uiTitlePanel_GrabSeatSwitch.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.uiTitlePanel_GrabSeatSwitch.MinimumSize = new System.Drawing.Size(1, 1);
            this.uiTitlePanel_GrabSeatSwitch.Name = "uiTitlePanel_GrabSeatSwitch";
            this.uiTitlePanel_GrabSeatSwitch.Padding = new System.Windows.Forms.Padding(0, 35, 0, 0);
            this.uiTitlePanel_GrabSeatSwitch.ShowText = false;
            this.uiTitlePanel_GrabSeatSwitch.Size = new System.Drawing.Size(186, 107);
            this.uiTitlePanel_GrabSeatSwitch.TabIndex = 1;
            this.uiTitlePanel_GrabSeatSwitch.Text = "操作";
            this.uiTitlePanel_GrabSeatSwitch.TextAlignment = System.Drawing.ContentAlignment.MiddleCenter;
            this.uiTitlePanel_GrabSeatSwitch.ZoomScaleRect = new System.Drawing.Rectangle(0, 0, 0, 0);
            // 
            // uiSwitch_GrabSeatSwitch
            // 
            this.uiSwitch_GrabSeatSwitch.Enabled = false;
            this.uiSwitch_GrabSeatSwitch.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.uiSwitch_GrabSeatSwitch.Location = new System.Drawing.Point(30, 56);
            this.uiSwitch_GrabSeatSwitch.MinimumSize = new System.Drawing.Size(1, 1);
            this.uiSwitch_GrabSeatSwitch.Name = "uiSwitch_GrabSeatSwitch";
            this.uiSwitch_GrabSeatSwitch.Size = new System.Drawing.Size(121, 29);
            this.uiSwitch_GrabSeatSwitch.TabIndex = 0;
            this.uiSwitch_GrabSeatSwitch.ZoomScaleRect = new System.Drawing.Rectangle(0, 0, 0, 0);
            this.uiSwitch_GrabSeatSwitch.ActiveChanged += new System.EventHandler(this.uiSwitch_GrabSeatSwitch_ActiveChanged);
            // 
            // uiTitlePanel_RealTimeData
            // 
            this.uiTitlePanel_RealTimeData.Controls.Add(this.uiTextBox_RealTimeData);
            this.uiTitlePanel_RealTimeData.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.uiTitlePanel_RealTimeData.Location = new System.Drawing.Point(4, 12);
            this.uiTitlePanel_RealTimeData.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.uiTitlePanel_RealTimeData.MinimumSize = new System.Drawing.Size(1, 1);
            this.uiTitlePanel_RealTimeData.Name = "uiTitlePanel_RealTimeData";
            this.uiTitlePanel_RealTimeData.Padding = new System.Windows.Forms.Padding(0, 35, 0, 0);
            this.uiTitlePanel_RealTimeData.ShowText = false;
            this.uiTitlePanel_RealTimeData.Size = new System.Drawing.Size(431, 425);
            this.uiTitlePanel_RealTimeData.TabIndex = 0;
            this.uiTitlePanel_RealTimeData.Text = "实时数据";
            this.uiTitlePanel_RealTimeData.TextAlignment = System.Drawing.ContentAlignment.MiddleCenter;
            this.uiTitlePanel_RealTimeData.ZoomScaleRect = new System.Drawing.Rectangle(0, 0, 0, 0);
            // 
            // uiTextBox_RealTimeData
            // 
            this.uiTextBox_RealTimeData.Font = new System.Drawing.Font("微软雅黑", 10.5F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.uiTextBox_RealTimeData.Location = new System.Drawing.Point(4, 40);
            this.uiTextBox_RealTimeData.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.uiTextBox_RealTimeData.MinimumSize = new System.Drawing.Size(1, 16);
            this.uiTextBox_RealTimeData.Multiline = true;
            this.uiTextBox_RealTimeData.Name = "uiTextBox_RealTimeData";
            this.uiTextBox_RealTimeData.RectSides = System.Windows.Forms.ToolStripStatusLabelBorderSides.None;
            this.uiTextBox_RealTimeData.ShowScrollBar = true;
            this.uiTextBox_RealTimeData.ShowText = false;
            this.uiTextBox_RealTimeData.Size = new System.Drawing.Size(423, 380);
            this.uiTextBox_RealTimeData.TabIndex = 0;
            this.uiTextBox_RealTimeData.TextAlignment = System.Drawing.ContentAlignment.MiddleLeft;
            this.uiTextBox_RealTimeData.Watermark = "";
            this.uiTextBox_RealTimeData.ZoomScaleRect = new System.Drawing.Rectangle(0, 0, 0, 0);
            this.uiTextBox_RealTimeData.TextChanged += new System.EventHandler(this.uiTextBox_RealTimeData_TextChanged);
            // 
            // tabPage_SeatInfoList
            // 
            this.tabPage_SeatInfoList.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(243)))), ((int)(((byte)(249)))), ((int)(((byte)(255)))));
            this.tabPage_SeatInfoList.Controls.Add(this.uiSymbolButton_LoadFavorite);
            this.tabPage_SeatInfoList.Controls.Add(this.uiSymbolButton_Favorite);
            this.tabPage_SeatInfoList.Controls.Add(this.uiSymbolButton_AddGrabList);
            this.tabPage_SeatInfoList.Controls.Add(this.uiDataGridView_SeatInfo);
            this.tabPage_SeatInfoList.Location = new System.Drawing.Point(0, 40);
            this.tabPage_SeatInfoList.Name = "tabPage_SeatInfoList";
            this.tabPage_SeatInfoList.Size = new System.Drawing.Size(633, 451);
            this.tabPage_SeatInfoList.TabIndex = 1;
            this.tabPage_SeatInfoList.Text = "选择座位";
            // 
            // uiSymbolButton_LoadFavorite
            // 
            this.uiSymbolButton_LoadFavorite.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.uiSymbolButton_LoadFavorite.Location = new System.Drawing.Point(45, 395);
            this.uiSymbolButton_LoadFavorite.MinimumSize = new System.Drawing.Size(1, 1);
            this.uiSymbolButton_LoadFavorite.Name = "uiSymbolButton_LoadFavorite";
            this.uiSymbolButton_LoadFavorite.Size = new System.Drawing.Size(160, 40);
            this.uiSymbolButton_LoadFavorite.Symbol = 61470;
            this.uiSymbolButton_LoadFavorite.TabIndex = 4;
            this.uiSymbolButton_LoadFavorite.Text = "加载收藏";
            this.uiSymbolButton_LoadFavorite.ZoomScaleRect = new System.Drawing.Rectangle(0, 0, 0, 0);
            this.uiSymbolButton_LoadFavorite.Click += new System.EventHandler(this.uiSymbolButton_LoadFavorite_Click);
            // 
            // uiSymbolButton_Favorite
            // 
            this.uiSymbolButton_Favorite.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.uiSymbolButton_Favorite.Location = new System.Drawing.Point(211, 395);
            this.uiSymbolButton_Favorite.MinimumSize = new System.Drawing.Size(1, 1);
            this.uiSymbolButton_Favorite.Name = "uiSymbolButton_Favorite";
            this.uiSymbolButton_Favorite.Size = new System.Drawing.Size(160, 40);
            this.uiSymbolButton_Favorite.Symbol = 61444;
            this.uiSymbolButton_Favorite.TabIndex = 3;
            this.uiSymbolButton_Favorite.Text = "收藏选中座位";
            this.uiSymbolButton_Favorite.ZoomScaleRect = new System.Drawing.Rectangle(0, 0, 0, 0);
            this.uiSymbolButton_Favorite.Click += new System.EventHandler(this.uiSymbolButton_Favorite_Click);
            // 
            // uiSymbolButton_AddGrabList
            // 
            this.uiSymbolButton_AddGrabList.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.uiSymbolButton_AddGrabList.Location = new System.Drawing.Point(377, 395);
            this.uiSymbolButton_AddGrabList.MinimumSize = new System.Drawing.Size(1, 1);
            this.uiSymbolButton_AddGrabList.Name = "uiSymbolButton_AddGrabList";
            this.uiSymbolButton_AddGrabList.Size = new System.Drawing.Size(253, 40);
            this.uiSymbolButton_AddGrabList.Symbol = 61543;
            this.uiSymbolButton_AddGrabList.TabIndex = 2;
            this.uiSymbolButton_AddGrabList.Text = "添加选中座位至抢座列表";
            this.uiSymbolButton_AddGrabList.ZoomScaleRect = new System.Drawing.Rectangle(0, 0, 0, 0);
            this.uiSymbolButton_AddGrabList.Click += new System.EventHandler(this.uiSymbolButton_AddGrabList_Click);
            // 
            // uiDataGridView_SeatInfo
            // 
            this.uiDataGridView_SeatInfo.AllowUserToOrderColumns = true;
            dataGridViewCellStyle1.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(235)))), ((int)(((byte)(243)))), ((int)(((byte)(255)))));
            this.uiDataGridView_SeatInfo.AlternatingRowsDefaultCellStyle = dataGridViewCellStyle1;
            this.uiDataGridView_SeatInfo.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            this.uiDataGridView_SeatInfo.BackgroundColor = System.Drawing.Color.White;
            this.uiDataGridView_SeatInfo.ColumnHeadersBorderStyle = System.Windows.Forms.DataGridViewHeaderBorderStyle.Single;
            dataGridViewCellStyle2.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
            dataGridViewCellStyle2.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(80)))), ((int)(((byte)(160)))), ((int)(((byte)(255)))));
            dataGridViewCellStyle2.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            dataGridViewCellStyle2.ForeColor = System.Drawing.Color.White;
            dataGridViewCellStyle2.SelectionBackColor = System.Drawing.SystemColors.Highlight;
            dataGridViewCellStyle2.SelectionForeColor = System.Drawing.SystemColors.HighlightText;
            dataGridViewCellStyle2.WrapMode = System.Windows.Forms.DataGridViewTriState.True;
            this.uiDataGridView_SeatInfo.ColumnHeadersDefaultCellStyle = dataGridViewCellStyle2;
            this.uiDataGridView_SeatInfo.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            dataGridViewCellStyle3.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleLeft;
            dataGridViewCellStyle3.BackColor = System.Drawing.SystemColors.Window;
            dataGridViewCellStyle3.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            dataGridViewCellStyle3.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(48)))), ((int)(((byte)(48)))), ((int)(((byte)(48)))));
            dataGridViewCellStyle3.SelectionBackColor = System.Drawing.SystemColors.Highlight;
            dataGridViewCellStyle3.SelectionForeColor = System.Drawing.SystemColors.HighlightText;
            dataGridViewCellStyle3.WrapMode = System.Windows.Forms.DataGridViewTriState.False;
            this.uiDataGridView_SeatInfo.DefaultCellStyle = dataGridViewCellStyle3;
            this.uiDataGridView_SeatInfo.EnableHeadersVisualStyles = false;
            this.uiDataGridView_SeatInfo.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.uiDataGridView_SeatInfo.GridColor = System.Drawing.Color.FromArgb(((int)(((byte)(80)))), ((int)(((byte)(160)))), ((int)(((byte)(255)))));
            this.uiDataGridView_SeatInfo.Location = new System.Drawing.Point(3, 10);
            this.uiDataGridView_SeatInfo.Name = "uiDataGridView_SeatInfo";
            dataGridViewCellStyle4.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleLeft;
            dataGridViewCellStyle4.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(235)))), ((int)(((byte)(243)))), ((int)(((byte)(255)))));
            dataGridViewCellStyle4.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            dataGridViewCellStyle4.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(48)))), ((int)(((byte)(48)))), ((int)(((byte)(48)))));
            dataGridViewCellStyle4.SelectionBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(80)))), ((int)(((byte)(160)))), ((int)(((byte)(255)))));
            dataGridViewCellStyle4.SelectionForeColor = System.Drawing.Color.White;
            dataGridViewCellStyle4.WrapMode = System.Windows.Forms.DataGridViewTriState.True;
            this.uiDataGridView_SeatInfo.RowHeadersDefaultCellStyle = dataGridViewCellStyle4;
            this.uiDataGridView_SeatInfo.RowHeadersWidth = 62;
            dataGridViewCellStyle5.BackColor = System.Drawing.Color.White;
            dataGridViewCellStyle5.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.uiDataGridView_SeatInfo.RowsDefaultCellStyle = dataGridViewCellStyle5;
            this.uiDataGridView_SeatInfo.RowTemplate.Height = 25;
            this.uiDataGridView_SeatInfo.ScrollBarRectColor = System.Drawing.Color.FromArgb(((int)(((byte)(80)))), ((int)(((byte)(160)))), ((int)(((byte)(255)))));
            this.uiDataGridView_SeatInfo.SelectedIndex = -1;
            this.uiDataGridView_SeatInfo.Size = new System.Drawing.Size(627, 374);
            this.uiDataGridView_SeatInfo.StripeOddColor = System.Drawing.Color.FromArgb(((int)(((byte)(235)))), ((int)(((byte)(243)))), ((int)(((byte)(255)))));
            this.uiDataGridView_SeatInfo.Style = Sunny.UI.UIStyle.Custom;
            this.uiDataGridView_SeatInfo.TabIndex = 0;
            this.uiDataGridView_SeatInfo.ZoomScaleRect = new System.Drawing.Rectangle(0, 0, 0, 0);
            this.uiDataGridView_SeatInfo.SelectIndexChange += new Sunny.UI.UIDataGridView.OnSelectIndexChange(this.uiDataGridView_SeatInfo_SelectIndexChange);
            // 
            // FGrabSeat
            // 
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            this.ClientSize = new System.Drawing.Size(657, 515);
            this.Controls.Add(this.uiTabControl_GrabSeat);
            this.MaximumSize = new System.Drawing.Size(657, 515);
            this.Name = "FGrabSeat";
            this.Symbol = 61671;
            this.Text = "抢座";
            this.uiTabControl_GrabSeat.ResumeLayout(false);
            this.tabPage_GrabController.ResumeLayout(false);
            this.uiTitlePanel_SelectedGrabSeats.ResumeLayout(false);
            this.uiTitlePanel_Setting.ResumeLayout(false);
            this.uiTitlePanel_GrabSeatSwitch.ResumeLayout(false);
            this.uiTitlePanel_RealTimeData.ResumeLayout(false);
            this.tabPage_SeatInfoList.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.uiDataGridView_SeatInfo)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private Sunny.UI.UITabControl uiTabControl_GrabSeat;
        private TabPage tabPage_GrabController;
        private TabPage tabPage_SeatInfoList;
        private Sunny.UI.UIDataGridView uiDataGridView_SeatInfo;
        private Sunny.UI.UISymbolButton uiSymbolButton_AddGrabList;
        private Sunny.UI.UITitlePanel uiTitlePanel_RealTimeData;
        private Sunny.UI.UISymbolButton uiSymbolButton_Favorite;
        private Sunny.UI.UITextBox uiTextBox_RealTimeData;
        private Sunny.UI.UITitlePanel uiTitlePanel_GrabSeatSwitch;
        private Sunny.UI.UITitlePanel uiTitlePanel_Setting;
        private Sunny.UI.UIMarkLabel uiMarkLabel1;
        private Sunny.UI.UIComboBox uiComboBox_GrabSeatMode;
        private Sunny.UI.UITitlePanel uiTitlePanel_SelectedGrabSeats;
        private Sunny.UI.UIListBox uiListBox_SelectedGrabSeats;
        private Sunny.UI.UITimePicker uiTimePicker_TimingTime;
        private Sunny.UI.UIMarkLabel uiMarkLabel2;
        public Sunny.UI.UISwitch uiSwitch_GrabSeatSwitch;
        private Sunny.UI.UISymbolButton uiSymbolButton_LoadFavorite;
    }
}