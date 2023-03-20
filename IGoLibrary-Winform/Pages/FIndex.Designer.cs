namespace IGoLibrary_Winform.Pages
{
    partial class FIndex
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
            this.pictureBox_Main = new System.Windows.Forms.PictureBox();
            this.uiLabel_SoftwareName = new Sunny.UI.UILabel();
            this.uiLabel1 = new Sunny.UI.UILabel();
            this.uiSymbolButton_ProjectPage = new Sunny.UI.UISymbolButton();
            this.uiSymbolButton_Github = new Sunny.UI.UISymbolButton();
            this.uiSymbolButton_CheckUpdate = new Sunny.UI.UISymbolButton();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox_Main)).BeginInit();
            this.SuspendLayout();
            // 
            // pictureBox_Main
            // 
            this.pictureBox_Main.Image = global::IGoLibrary_Winform.Properties.Resources.Library;
            this.pictureBox_Main.Location = new System.Drawing.Point(59, 120);
            this.pictureBox_Main.Name = "pictureBox_Main";
            this.pictureBox_Main.Size = new System.Drawing.Size(143, 123);
            this.pictureBox_Main.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.pictureBox_Main.TabIndex = 0;
            this.pictureBox_Main.TabStop = false;
            // 
            // uiLabel_SoftwareName
            // 
            this.uiLabel_SoftwareName.Font = new System.Drawing.Font("微软雅黑", 26.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.uiLabel_SoftwareName.Location = new System.Drawing.Point(205, 130);
            this.uiLabel_SoftwareName.Name = "uiLabel_SoftwareName";
            this.uiLabel_SoftwareName.Size = new System.Drawing.Size(422, 58);
            this.uiLabel_SoftwareName.TabIndex = 1;
            this.uiLabel_SoftwareName.Text = "我去图书馆小助手";
            this.uiLabel_SoftwareName.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.uiLabel_SoftwareName.ZoomScaleRect = new System.Drawing.Rectangle(0, 0, 0, 0);
            // 
            // uiLabel1
            // 
            this.uiLabel1.Font = new System.Drawing.Font("微软雅黑", 18F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.uiLabel1.Location = new System.Drawing.Point(205, 183);
            this.uiLabel1.Name = "uiLabel1";
            this.uiLabel1.Size = new System.Drawing.Size(446, 46);
            this.uiLabel1.TabIndex = 2;
            this.uiLabel1.Text = "IGoLibrary Helper";
            this.uiLabel1.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.uiLabel1.ZoomScaleRect = new System.Drawing.Rectangle(0, 0, 0, 0);
            // 
            // uiSymbolButton_ProjectPage
            // 
            this.uiSymbolButton_ProjectPage.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.uiSymbolButton_ProjectPage.Location = new System.Drawing.Point(59, 260);
            this.uiSymbolButton_ProjectPage.MinimumSize = new System.Drawing.Size(1, 1);
            this.uiSymbolButton_ProjectPage.Name = "uiSymbolButton_ProjectPage";
            this.uiSymbolButton_ProjectPage.Size = new System.Drawing.Size(171, 43);
            this.uiSymbolButton_ProjectPage.Symbol = 62056;
            this.uiSymbolButton_ProjectPage.SymbolSize = 28;
            this.uiSymbolButton_ProjectPage.TabIndex = 3;
            this.uiSymbolButton_ProjectPage.Text = "项目页面";
            this.uiSymbolButton_ProjectPage.ZoomScaleRect = new System.Drawing.Rectangle(0, 0, 0, 0);
            this.uiSymbolButton_ProjectPage.Click += new System.EventHandler(this.uiSymbolButton_ProjectPage_Click);
            // 
            // uiSymbolButton_Github
            // 
            this.uiSymbolButton_Github.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.uiSymbolButton_Github.Location = new System.Drawing.Point(236, 260);
            this.uiSymbolButton_Github.MinimumSize = new System.Drawing.Size(1, 1);
            this.uiSymbolButton_Github.Name = "uiSymbolButton_Github";
            this.uiSymbolButton_Github.Size = new System.Drawing.Size(171, 43);
            this.uiSymbolButton_Github.Symbol = 61595;
            this.uiSymbolButton_Github.SymbolSize = 28;
            this.uiSymbolButton_Github.TabIndex = 4;
            this.uiSymbolButton_Github.Text = "开源地址";
            this.uiSymbolButton_Github.ZoomScaleRect = new System.Drawing.Rectangle(0, 0, 0, 0);
            this.uiSymbolButton_Github.Click += new System.EventHandler(this.uiSymbolButton_Github_Click);
            // 
            // uiSymbolButton_CheckUpdate
            // 
            this.uiSymbolButton_CheckUpdate.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.uiSymbolButton_CheckUpdate.Location = new System.Drawing.Point(413, 260);
            this.uiSymbolButton_CheckUpdate.MinimumSize = new System.Drawing.Size(1, 1);
            this.uiSymbolButton_CheckUpdate.Name = "uiSymbolButton_CheckUpdate";
            this.uiSymbolButton_CheckUpdate.Size = new System.Drawing.Size(171, 43);
            this.uiSymbolButton_CheckUpdate.Symbol = 61454;
            this.uiSymbolButton_CheckUpdate.SymbolSize = 28;
            this.uiSymbolButton_CheckUpdate.TabIndex = 5;
            this.uiSymbolButton_CheckUpdate.Text = "检查更新";
            this.uiSymbolButton_CheckUpdate.ZoomScaleRect = new System.Drawing.Rectangle(0, 0, 0, 0);
            this.uiSymbolButton_CheckUpdate.Click += new System.EventHandler(this.uiSymbolButton_CheckUpdate_Click);
            // 
            // FIndex
            // 
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            this.ClientSize = new System.Drawing.Size(657, 515);
            this.Controls.Add(this.uiSymbolButton_CheckUpdate);
            this.Controls.Add(this.uiSymbolButton_Github);
            this.Controls.Add(this.uiSymbolButton_ProjectPage);
            this.Controls.Add(this.uiLabel1);
            this.Controls.Add(this.uiLabel_SoftwareName);
            this.Controls.Add(this.pictureBox_Main);
            this.MaximumSize = new System.Drawing.Size(657, 515);
            this.Name = "FIndex";
            this.Padding = new System.Windows.Forms.Padding(0, 35, 0, 0);
            this.Symbol = 57353;
            this.SymbolSize = 30;
            this.Text = "首页";
            this.ZoomScaleRect = new System.Drawing.Rectangle(22, 22, 100, 100);
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox_Main)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private PictureBox pictureBox_Main;
        private Sunny.UI.UILabel uiLabel_SoftwareName;
        private Sunny.UI.UILabel uiLabel1;
        private Sunny.UI.UISymbolButton uiSymbolButton_ProjectPage;
        private Sunny.UI.UISymbolButton uiSymbolButton_Github;
        private Sunny.UI.UISymbolButton uiSymbolButton_CheckUpdate;
    }
}