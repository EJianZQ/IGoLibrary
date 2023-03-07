using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using IGoLibrary_Winform.Data;

namespace IGoLibrary_Winform.Controller
{
    public interface IGetLibInfoService
    {
        public Library GetLibInfo(string Cookies,string QueryStatement);
        public List<SeatsItem> GetLibSeats(LibRoot root);
        public Library? GetLibInfo_Debug(string Cookies, string QueryStatement);

        //public void DITest();
    }
}
