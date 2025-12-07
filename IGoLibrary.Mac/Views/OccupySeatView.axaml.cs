using Avalonia.Controls;
using IGoLibrary.Mac.ViewModels;

namespace IGoLibrary.Mac.Views
{
    public partial class OccupySeatView : UserControl
    {
        public OccupySeatView()
        {
            InitializeComponent();

            if (Design.IsDesignMode)
                return;

            // 从依赖注入容器获取ViewModel
            if (App.ServiceProvider != null)
            {
                DataContext = App.ServiceProvider.GetService(typeof(OccupySeatViewModel));
            }
        }
    }
}
