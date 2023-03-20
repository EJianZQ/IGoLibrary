using Sunny.UI;
using System.Diagnostics;

namespace IGoLibrary_Winform.Pages
{
    public partial class FIndex : UIPage
    {
        public FIndex()
        {
            InitializeComponent();
        }

        private void uiSymbolButton_ProjectPage_Click(object sender, EventArgs e)
        {
            Process.Start(new ProcessStartInfo("https://xn--e-5g8az75bbi3a.com/%E9%A1%B9%E7%9B%AE%E5%8F%91%E5%B8%83/14.html") { UseShellExecute = true });
        }

        private void uiSymbolButton_Github_Click(object sender, EventArgs e)
        {
            Process.Start(new ProcessStartInfo("https://github.com/EJianZQ/IGoLibrary") { UseShellExecute = true });
        }

        private void uiSymbolButton_CheckUpdate_Click(object sender, EventArgs e)
        {
            Process.Start(new ProcessStartInfo("https://github.com/EJianZQ/IGoLibrary/releases") { UseShellExecute = true });
        }
    }
}
