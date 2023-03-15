using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IGoLibrary_Winform.Data
{
    public class ReserveSeatRoot
    {
        public ReserveSeatRoot_Data data { get; set; }
        public List<ReserveSeatRoot_ErrorsItem> errors { get; set; }
    }


    public class ReserveSeatRoot_Data
    {
        public ReserveSeatRoot_UserAuth userAuth { get; set; }
    }

    public class ReserveSeatRoot_UserAuth
    {
        public ReserveSeatRoot_Reserve reserve { get; set; }
    }

    public class ReserveSeatRoot_Reserve
    {
        public string reserueSeat { get; set; }
    }

    public class ReserveSeatRoot_ErrorsItem
    {
        public string msg { get; set; }
        public int code { get; set; }
    }
}
