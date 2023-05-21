namespace IGoLibrary_Winform.Pages
{
    partial class FOccupySeat
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
            this.uiTitlePanel_RealTimeData = new Sunny.UI.UITitlePanel();
            this.uiTextBox_RealTimeData = new Sunny.UI.UITextBox();
            this.uiTitlePanel_ReserveInfo = new Sunny.UI.UITitlePanel();
            this.uiSymbolLabel_SeatKey = new Sunny.UI.UISymbolLabel();
            this.uiSymbolLabel_SeatName = new Sunny.UI.UISymbolLabel();
            this.uiSymbolLabel_LibName = new Sunny.UI.UISymbolLabel();
            this.uiTitlePane_Setting = new Sunny.UI.UITitlePanel();
            this.uiLabel1 = new Sunny.UI.UILabel();
            this.uiIntegerUpDown_ReReseveInterval = new Sunny.UI.UIIntegerUpDown();
            this.uiMarkLabel2 = new Sunny.UI.UIMarkLabel();
            this.uiSymbolButton_Help = new Sunny.UI.UISymbolButton();
            this.uiComboBox_ReserveInfoRefreshInterval = new Sunny.UI.UIComboBox();
            this.uiMarkLabel1 = new Sunny.UI.UIMarkLabel();
            this.uiSymbolButton_UpdateStatus = new Sunny.UI.UISymbolButton();
            this.uiTitlePanel_OccupySeatSwitch = new Sunny.UI.UITitlePanel();
            this.uiSwitch_OccupySeat = new Sunny.UI.UISwitch();
            this.uiTitlePanel_RealTimeData.SuspendLayout();
            this.uiTitlePanel_ReserveInfo.SuspendLayout();
            this.uiTitlePane_Setting.SuspendLayout();
            this.uiTitlePanel_OccupySeatSwitch.SuspendLayout();
            this.SuspendLayout();
            // 
            // uiTitlePanel_RealTimeData
            // 
            this.uiTitlePanel_RealTimeData.Controls.Add(this.uiTextBox_RealTimeData);
            this.uiTitlePanel_RealTimeData.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.uiTitlePanel_RealTimeData.Location = new System.Drawing.Point(13, 14);
            this.uiTitlePanel_RealTimeData.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.uiTitlePanel_RealTimeData.MinimumSize = new System.Drawing.Size(1, 1);
            this.uiTitlePanel_RealTimeData.Name = "uiTitlePanel_RealTimeData";
            this.uiTitlePanel_RealTimeData.Padding = new System.Windows.Forms.Padding(0, 35, 0, 0);
            this.uiTitlePanel_RealTimeData.ShowText = false;
            this.uiTitlePanel_RealTimeData.Size = new System.Drawing.Size(410, 487);
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
            this.uiTextBox_RealTimeData.Size = new System.Drawing.Size(402, 442);
            this.uiTextBox_RealTimeData.TabIndex = 1;
            this.uiTextBox_RealTimeData.TextAlignment = System.Drawing.ContentAlignment.MiddleLeft;
            this.uiTextBox_RealTimeData.Watermark = "";
            this.uiTextBox_RealTimeData.ZoomScaleRect = new System.Drawing.Rectangle(0, 0, 0, 0);
            this.uiTextBox_RealTimeData.TextChanged += new System.EventHandler(this.uiTextBox_RealTimeData_TextChanged);
            // 
            // uiTitlePanel_ReserveInfo
            // 
            this.uiTitlePanel_ReserveInfo.Controls.Add(this.uiSymbolLabel_SeatKey);
            this.uiTitlePanel_ReserveInfo.Controls.Add(this.uiSymbolLabel_SeatName);
            this.uiTitlePanel_ReserveInfo.Controls.Add(this.uiSymbolLabel_LibName);
            this.uiTitlePanel_ReserveInfo.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.uiTitlePanel_ReserveInfo.Location = new System.Drawing.Point(431, 14);
            this.uiTitlePanel_ReserveInfo.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.uiTitlePanel_ReserveInfo.MinimumSize = new System.Drawing.Size(1, 1);
            this.uiTitlePanel_ReserveInfo.Name = "uiTitlePanel_ReserveInfo";
            this.uiTitlePanel_ReserveInfo.Padding = new System.Windows.Forms.Padding(0, 35, 0, 0);
            this.uiTitlePanel_ReserveInfo.ShowText = false;
            this.uiTitlePanel_ReserveInfo.Size = new System.Drawing.Size(213, 164);
            this.uiTitlePanel_ReserveInfo.TabIndex = 1;
            this.uiTitlePanel_ReserveInfo.Text = "预约座位信息";
            this.uiTitlePanel_ReserveInfo.TextAlignment = System.Drawing.ContentAlignment.MiddleCenter;
            this.uiTitlePanel_ReserveInfo.ZoomScaleRect = new System.Drawing.Rectangle(0, 0, 0, 0);
            // 
            // uiSymbolLabel_SeatKey
            // 
            this.uiSymbolLabel_SeatKey.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.uiSymbolLabel_SeatKey.Location = new System.Drawing.Point(10, 116);
            this.uiSymbolLabel_SeatKey.MinimumSize = new System.Drawing.Size(1, 1);
            this.uiSymbolLabel_SeatKey.Name = "uiSymbolLabel_SeatKey";
            this.uiSymbolLabel_SeatKey.Padding = new System.Windows.Forms.Padding(28, 0, 0, 0);
            this.uiSymbolLabel_SeatKey.Size = new System.Drawing.Size(200, 35);
            this.uiSymbolLabel_SeatKey.Symbol = 361572;
            this.uiSymbolLabel_SeatKey.TabIndex = 2;
            this.uiSymbolLabel_SeatKey.Text = "暂未获取坐标";
            this.uiSymbolLabel_SeatKey.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.uiSymbolLabel_SeatKey.ZoomScaleRect = new System.Drawing.Rectangle(0, 0, 0, 0);
            // 
            // uiSymbolLabel_SeatName
            // 
            this.uiSymbolLabel_SeatName.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.uiSymbolLabel_SeatName.Location = new System.Drawing.Point(10, 81);
            this.uiSymbolLabel_SeatName.MinimumSize = new System.Drawing.Size(1, 1);
            this.uiSymbolLabel_SeatName.Name = "uiSymbolLabel_SeatName";
            this.uiSymbolLabel_SeatName.Padding = new System.Windows.Forms.Padding(28, 0, 0, 0);
            this.uiSymbolLabel_SeatName.Size = new System.Drawing.Size(200, 35);
            this.uiSymbolLabel_SeatName.Symbol = 363168;
            this.uiSymbolLabel_SeatName.TabIndex = 1;
            this.uiSymbolLabel_SeatName.Text = "暂未获取";
            this.uiSymbolLabel_SeatName.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.uiSymbolLabel_SeatName.ZoomScaleRect = new System.Drawing.Rectangle(0, 0, 0, 0);
            // 
            // uiSymbolLabel_LibName
            // 
            this.uiSymbolLabel_LibName.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.uiSymbolLabel_LibName.Location = new System.Drawing.Point(10, 46);
            this.uiSymbolLabel_LibName.MinimumSize = new System.Drawing.Size(1, 1);
            this.uiSymbolLabel_LibName.Name = "uiSymbolLabel_LibName";
            this.uiSymbolLabel_LibName.Padding = new System.Windows.Forms.Padding(28, 0, 0, 0);
            this.uiSymbolLabel_LibName.Size = new System.Drawing.Size(200, 35);
            this.uiSymbolLabel_LibName.Symbol = 61687;
            this.uiSymbolLabel_LibName.TabIndex = 0;
            this.uiSymbolLabel_LibName.Text = "暂未获取";
            this.uiSymbolLabel_LibName.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.uiSymbolLabel_LibName.ZoomScaleRect = new System.Drawing.Rectangle(0, 0, 0, 0);
            // 
            // uiTitlePane_Setting
            // 
            this.uiTitlePane_Setting.Controls.Add(this.uiLabel1);
            this.uiTitlePane_Setting.Controls.Add(this.uiIntegerUpDown_ReReseveInterval);
            this.uiTitlePane_Setting.Controls.Add(this.uiMarkLabel2);
            this.uiTitlePane_Setting.Controls.Add(this.uiSymbolButton_Help);
            this.uiTitlePane_Setting.Controls.Add(this.uiComboBox_ReserveInfoRefreshInterval);
            this.uiTitlePane_Setting.Controls.Add(this.uiMarkLabel1);
            this.uiTitlePane_Setting.Controls.Add(this.uiSymbolButton_UpdateStatus);
            this.uiTitlePane_Setting.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.uiTitlePane_Setting.Location = new System.Drawing.Point(431, 188);
            this.uiTitlePane_Setting.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.uiTitlePane_Setting.MinimumSize = new System.Drawing.Size(1, 1);
            this.uiTitlePane_Setting.Name = "uiTitlePane_Setting";
            this.uiTitlePane_Setting.Padding = new System.Windows.Forms.Padding(0, 35, 0, 0);
            this.uiTitlePane_Setting.ShowText = false;
            this.uiTitlePane_Setting.Size = new System.Drawing.Size(213, 225);
            this.uiTitlePane_Setting.TabIndex = 2;
            this.uiTitlePane_Setting.Text = "设置";
            this.uiTitlePane_Setting.TextAlignment = System.Drawing.ContentAlignment.MiddleCenter;
            this.uiTitlePane_Setting.ZoomScaleRect = new System.Drawing.Rectangle(0, 0, 0, 0);
            // 
            // uiLabel1
            // 
            this.uiLabel1.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.uiLabel1.Location = new System.Drawing.Point(156, 146);
            this.uiLabel1.Name = "uiLabel1";
            this.uiLabel1.Size = new System.Drawing.Size(27, 23);
            this.uiLabel1.TabIndex = 7;
            this.uiLabel1.Text = "秒";
            this.uiLabel1.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.uiLabel1.ZoomScaleRect = new System.Drawing.Rectangle(0, 0, 0, 0);
            // 
            // uiIntegerUpDown_ReReseveInterval
            // 
            this.uiIntegerUpDown_ReReseveInterval.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.uiIntegerUpDown_ReReseveInterval.Location = new System.Drawing.Point(19, 144);
            this.uiIntegerUpDown_ReReseveInterval.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.uiIntegerUpDown_ReReseveInterval.Maximum = 1800;
            this.uiIntegerUpDown_ReReseveInterval.Minimum = 1;
            this.uiIntegerUpDown_ReReseveInterval.MinimumSize = new System.Drawing.Size(100, 0);
            this.uiIntegerUpDown_ReReseveInterval.Name = "uiIntegerUpDown_ReReseveInterval";
            this.uiIntegerUpDown_ReReseveInterval.ShowText = false;
            this.uiIntegerUpDown_ReReseveInterval.Size = new System.Drawing.Size(126, 29);
            this.uiIntegerUpDown_ReReseveInterval.TabIndex = 6;
            this.uiIntegerUpDown_ReReseveInterval.Text = "uiIntegerUpDown1";
            this.uiIntegerUpDown_ReReseveInterval.TextAlignment = System.Drawing.ContentAlignment.MiddleCenter;
            this.uiIntegerUpDown_ReReseveInterval.ZoomScaleRect = new System.Drawing.Rectangle(0, 0, 0, 0);
            // 
            // uiMarkLabel2
            // 
            this.uiMarkLabel2.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.uiMarkLabel2.Location = new System.Drawing.Point(19, 109);
            this.uiMarkLabel2.MarkPos = Sunny.UI.UIMarkLabel.UIMarkPos.Bottom;
            this.uiMarkLabel2.Name = "uiMarkLabel2";
            this.uiMarkLabel2.Padding = new System.Windows.Forms.Padding(0, 0, 0, 5);
            this.uiMarkLabel2.Size = new System.Drawing.Size(178, 30);
            this.uiMarkLabel2.TabIndex = 5;
            this.uiMarkLabel2.Text = "重新预约间隔";
            this.uiMarkLabel2.TextAlign = System.Drawing.ContentAlignment.TopCenter;
            this.uiMarkLabel2.ZoomScaleRect = new System.Drawing.Rectangle(0, 0, 0, 0);
            // 
            // uiSymbolButton_Help
            // 
            this.uiSymbolButton_Help.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.uiSymbolButton_Help.Location = new System.Drawing.Point(19, 181);
            this.uiSymbolButton_Help.MinimumSize = new System.Drawing.Size(1, 1);
            this.uiSymbolButton_Help.Name = "uiSymbolButton_Help";
            this.uiSymbolButton_Help.Size = new System.Drawing.Size(86, 32);
            this.uiSymbolButton_Help.Symbol = 61529;
            this.uiSymbolButton_Help.TabIndex = 4;
            this.uiSymbolButton_Help.Text = "帮助";
            this.uiSymbolButton_Help.ZoomScaleRect = new System.Drawing.Rectangle(0, 0, 0, 0);
            this.uiSymbolButton_Help.Click += new System.EventHandler(this.uiSymbolButton_Help_Click);
            // 
            // uiComboBox_ReserveInfoRefreshInterval
            // 
            this.uiComboBox_ReserveInfoRefreshInterval.DataSource = null;
            this.uiComboBox_ReserveInfoRefreshInterval.DropDownStyle = Sunny.UI.UIDropDownStyle.DropDownList;
            this.uiComboBox_ReserveInfoRefreshInterval.FillColor = System.Drawing.Color.White;
            this.uiComboBox_ReserveInfoRefreshInterval.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.uiComboBox_ReserveInfoRefreshInterval.Items.AddRange(new object[] {
            "固定间隔10秒",
            "随机10~20秒"});
            this.uiComboBox_ReserveInfoRefreshInterval.Location = new System.Drawing.Point(19, 75);
            this.uiComboBox_ReserveInfoRefreshInterval.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.uiComboBox_ReserveInfoRefreshInterval.MaxDropDownItems = 3;
            this.uiComboBox_ReserveInfoRefreshInterval.MinimumSize = new System.Drawing.Size(63, 0);
            this.uiComboBox_ReserveInfoRefreshInterval.Name = "uiComboBox_ReserveInfoRefreshInterval";
            this.uiComboBox_ReserveInfoRefreshInterval.Padding = new System.Windows.Forms.Padding(0, 0, 30, 2);
            this.uiComboBox_ReserveInfoRefreshInterval.Size = new System.Drawing.Size(178, 29);
            this.uiComboBox_ReserveInfoRefreshInterval.TabIndex = 2;
            this.uiComboBox_ReserveInfoRefreshInterval.TextAlignment = System.Drawing.ContentAlignment.MiddleLeft;
            this.uiComboBox_ReserveInfoRefreshInterval.Watermark = "";
            this.uiComboBox_ReserveInfoRefreshInterval.ZoomScaleRect = new System.Drawing.Rectangle(0, 0, 0, 0);
            // 
            // uiMarkLabel1
            // 
            this.uiMarkLabel1.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.uiMarkLabel1.Location = new System.Drawing.Point(19, 40);
            this.uiMarkLabel1.MarkPos = Sunny.UI.UIMarkLabel.UIMarkPos.Bottom;
            this.uiMarkLabel1.Name = "uiMarkLabel1";
            this.uiMarkLabel1.Padding = new System.Windows.Forms.Padding(0, 0, 0, 5);
            this.uiMarkLabel1.Size = new System.Drawing.Size(178, 30);
            this.uiMarkLabel1.TabIndex = 1;
            this.uiMarkLabel1.Text = "信息刷新间隔";
            this.uiMarkLabel1.TextAlign = System.Drawing.ContentAlignment.TopCenter;
            this.uiMarkLabel1.ZoomScaleRect = new System.Drawing.Rectangle(0, 0, 0, 0);
            // 
            // uiSymbolButton_UpdateStatus
            // 
            this.uiSymbolButton_UpdateStatus.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.uiSymbolButton_UpdateStatus.Location = new System.Drawing.Point(111, 181);
            this.uiSymbolButton_UpdateStatus.MinimumSize = new System.Drawing.Size(1, 1);
            this.uiSymbolButton_UpdateStatus.Name = "uiSymbolButton_UpdateStatus";
            this.uiSymbolButton_UpdateStatus.Size = new System.Drawing.Size(86, 32);
            this.uiSymbolButton_UpdateStatus.Symbol = 61473;
            this.uiSymbolButton_UpdateStatus.TabIndex = 3;
            this.uiSymbolButton_UpdateStatus.Text = "刷新";
            this.uiSymbolButton_UpdateStatus.ZoomScaleRect = new System.Drawing.Rectangle(0, 0, 0, 0);
            this.uiSymbolButton_UpdateStatus.Click += new System.EventHandler(this.uiSymbolButton_UpdateStatus_Click);
            // 
            // uiTitlePanel_OccupySeatSwitch
            // 
            this.uiTitlePanel_OccupySeatSwitch.Controls.Add(this.uiSwitch_OccupySeat);
            this.uiTitlePanel_OccupySeatSwitch.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.uiTitlePanel_OccupySeatSwitch.Location = new System.Drawing.Point(431, 423);
            this.uiTitlePanel_OccupySeatSwitch.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.uiTitlePanel_OccupySeatSwitch.MinimumSize = new System.Drawing.Size(1, 1);
            this.uiTitlePanel_OccupySeatSwitch.Name = "uiTitlePanel_OccupySeatSwitch";
            this.uiTitlePanel_OccupySeatSwitch.Padding = new System.Windows.Forms.Padding(0, 35, 0, 0);
            this.uiTitlePanel_OccupySeatSwitch.ShowText = false;
            this.uiTitlePanel_OccupySeatSwitch.Size = new System.Drawing.Size(213, 78);
            this.uiTitlePanel_OccupySeatSwitch.TabIndex = 3;
            this.uiTitlePanel_OccupySeatSwitch.Text = "操作";
            this.uiTitlePanel_OccupySeatSwitch.TextAlignment = System.Drawing.ContentAlignment.MiddleCenter;
            this.uiTitlePanel_OccupySeatSwitch.ZoomScaleRect = new System.Drawing.Rectangle(0, 0, 0, 0);
            // 
            // uiSwitch_OccupySeat
            // 
            this.uiSwitch_OccupySeat.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.uiSwitch_OccupySeat.Location = new System.Drawing.Point(19, 42);
            this.uiSwitch_OccupySeat.MinimumSize = new System.Drawing.Size(1, 1);
            this.uiSwitch_OccupySeat.Name = "uiSwitch_OccupySeat";
            this.uiSwitch_OccupySeat.Size = new System.Drawing.Size(168, 29);
            this.uiSwitch_OccupySeat.TabIndex = 1;
            this.uiSwitch_OccupySeat.ZoomScaleRect = new System.Drawing.Rectangle(0, 0, 0, 0);
            this.uiSwitch_OccupySeat.ActiveChanged += new System.EventHandler(this.uiSwitch_OccupySeat_ActiveChanged);
            // 
            // FOccupySeat
            // 
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            this.ClientSize = new System.Drawing.Size(657, 515);
            this.Controls.Add(this.uiTitlePanel_OccupySeatSwitch);
            this.Controls.Add(this.uiTitlePane_Setting);
            this.Controls.Add(this.uiTitlePanel_ReserveInfo);
            this.Controls.Add(this.uiTitlePanel_RealTimeData);
            this.MaximumSize = new System.Drawing.Size(657, 515);
            this.Name = "FOccupySeat";
            this.Symbol = 61746;
            this.Text = "占座";
            this.uiTitlePanel_RealTimeData.ResumeLayout(false);
            this.uiTitlePanel_ReserveInfo.ResumeLayout(false);
            this.uiTitlePane_Setting.ResumeLayout(false);
            this.uiTitlePanel_OccupySeatSwitch.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private Sunny.UI.UITitlePanel uiTitlePanel_RealTimeData;
        private Sunny.UI.UITitlePanel uiTitlePanel_ReserveInfo;
        private Sunny.UI.UISymbolLabel uiSymbolLabel_LibName;
        private Sunny.UI.UISymbolLabel uiSymbolLabel_SeatKey;
        private Sunny.UI.UISymbolLabel uiSymbolLabel_SeatName;
        private Sunny.UI.UITitlePanel uiTitlePane_Setting;
        private Sunny.UI.UITitlePanel uiTitlePanel_OccupySeatSwitch;
        public Sunny.UI.UISwitch uiSwitch_OccupySeat;
        private Sunny.UI.UIMarkLabel uiMarkLabel1;
        private Sunny.UI.UIComboBox uiComboBox_ReserveInfoRefreshInterval;
        private Sunny.UI.UISymbolButton uiSymbolButton_Help;
        private Sunny.UI.UISymbolButton uiSymbolButton_UpdateStatus;
        private Sunny.UI.UITextBox uiTextBox_RealTimeData;
        private Sunny.UI.UILabel uiLabel1;
        private Sunny.UI.UIIntegerUpDown uiIntegerUpDown_ReReseveInterval;
        private Sunny.UI.UIMarkLabel uiMarkLabel2;
    }
}