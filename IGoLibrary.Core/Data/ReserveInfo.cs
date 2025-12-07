using System;

namespace IGoLibrary.Core.Data
{
    public class ReserveInfo
    {
        public ReserveInfo() { }
        public string Token { get; set; }
        public string ExpiredTimeStamp { get; set; }
        public DateTime ExpiredTime
        {
            get
            {
                return ConvertToDateTime(Convert.ToInt64(ExpiredTimeStamp));
            }
        }
        public string LibName { get; set; }
        public SeatKeyData SeatKeyDta { get; set; }

        private static DateTime ConvertToDateTime(long timestamp)
        {
            long begtime = timestamp * 10000000;
            DateTime dt_1970 = new DateTime(1970, 1, 1, 8, 0, 0);
            long tricks_1970 = dt_1970.Ticks;
            long time_tricks = tricks_1970 + begtime;
            DateTime dt = new DateTime(time_tricks);
            return dt;
        }
    }
}
