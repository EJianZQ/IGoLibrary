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
            this.uiSymbolButton1 = new Sunny.UI.UISymbolButton();
            this.SuspendLayout();
            // 
            // uiSymbolButton1
            // 
            this.uiSymbolButton1.Font = new System.Drawing.Font("微软雅黑", 22F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.uiSymbolButton1.Location = new System.Drawing.Point(64, 69);
            this.uiSymbolButton1.MinimumSize = new System.Drawing.Size(1, 1);
            this.uiSymbolButton1.Name = "uiSymbolButton1";
            this.uiSymbolButton1.Size = new System.Drawing.Size(333, 301);
            this.uiSymbolButton1.TabIndex = 0;
            this.uiSymbolButton1.Text = "首页";
            this.uiSymbolButton1.ZoomScaleRect = new System.Drawing.Rectangle(0, 0, 0, 0);
            this.uiSymbolButton1.Click += new System.EventHandler(this.uiSymbolButton1_Click);
            // 
            // FIndex
            // 
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            this.ClientSize = new System.Drawing.Size(657, 515);
            this.Controls.Add(this.uiSymbolButton1);
            this.MaximumSize = new System.Drawing.Size(657, 515);
            this.Name = "FIndex";
            this.Padding = new System.Windows.Forms.Padding(0, 35, 0, 0);
            this.Symbol = 57353;
            this.SymbolSize = 30;
            this.Text = "首页";
            this.ZoomScaleRect = new System.Drawing.Rectangle(22, 22, 100, 100);
            this.ResumeLayout(false);

        }

        #endregion

        public Sunny.UI.UISymbolButton uiSymbolButton1;
    }
}