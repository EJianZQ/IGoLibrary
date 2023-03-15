using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Sunny.UI;
using IGoLibrary_Winform.Pages;
using IGoLibrary_Winform.Controller;
using IGoLibrary_Winform.Data;
using IGoLibrary_Winform.Notify;
using Notifications.Wpf;

namespace IGoLibrary_Winform
{
    public partial class MainForm : UIAsideMainFrame
    {
        public static ServiceCollection services;
        public static Authentication authentication;
        public MainForm()
        {
            InitializeComponent();
            authentication = new Authentication();
            services = new ServiceCollection();
            services.AddSingleton<IGetLibInfoService, GetLibInfoServiceImpl>();
            services.AddSingleton<IReserveSeatService, ReserveSeatServiceImpl>();
            services.AddSingleton<IGetCookieService, GetCookieServiceImpl>();
            services.AddSingleton<IGetAllLibsSummaryService, GetAllLibsSummaryImpl>();
            services.AddSingleton<IGetReserveInfoService, GetReserveInfoServiceImpl>();
            services.AddSingleton<ICancelReserveService, CancelReserveServiceImpl>();
            using (var serviceProvider = services.BuildServiceProvider())
            {
                int pageIndex = 1000;
                TreeNode parent = Aside.CreateNode(AddPage(new FIndex(), pageIndex));
                Aside.SelectPage(1000);
                parent = Aside.CreateNode(AddPage(new FDataSource(this), ++pageIndex));
                parent = Aside.CreateNode("座位", 61747, 30, ++pageIndex);
                Aside.CreateChildNode(parent, AddPage(new FGrabSeat(), ++pageIndex));
                Aside.CreateChildNode(parent, AddPage(new FOccupySeat(), ++pageIndex));
            }
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            var grabSeatPage = GetPage<FGrabSeat>();
            if (grabSeatPage != null)
            {
                grabSeatPage._grabSeatsSignal = false;
            }
            var occpuySeatPage = GetPage<FOccupySeat>();
            if (occpuySeatPage != null)
            {
                occpuySeatPage._occupySeatSignal = false;
            }
        }
    }
}
