using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IGoLibrary_Winform.Controller
{
    public interface IReserveSeatService
    {
        public bool ReserveSeat(string Cookies, string QueryStatement);
    }
}
