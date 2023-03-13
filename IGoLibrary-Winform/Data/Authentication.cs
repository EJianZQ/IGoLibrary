using IGoLibrary_Winform.Controller;

namespace IGoLibrary_Winform.Data
{
    public class Authentication
    {
        public Authentication() 
        { 
            this.Authenticator = new Authenticator();
        }
        /// <summary>
        /// Cookies是否已验证为未过期
        /// </summary>
        public bool? IsAuthenticated { get; set; }
        /// <summary>
        /// 上次验证Cookies的时间
        /// </summary>
        public DateTime LastAuthenticationTime { get; set; }
        /// <summary>
        /// 获取到的最新的图书馆具体数据
        /// </summary>
        public Library? LatestLibraryData { get; set; }
        /// <summary>
        /// 获取到的最新的图书馆座位数据
        /// </summary>
        public List<SeatsItem>? latestSeats { get {
                if (LatestLibraryData != null)
                {
                    return LatestLibraryData.Seats;
                }
                else
                    return null;
            } 
        }
        public Authenticator Authenticator { get; set; }

    }
    public class Authenticator
    {
        public Authenticator()
        {
            this.Syntax = new QuerySyntax();
        }
        public string? Cookies { get; set; }
        public int? LibID { get; set;}
        public QuerySyntax Syntax { get; set; }
    }

    public class QuerySyntax
    {
        public string? QueryLibInfo { get; set;}
        public string? ReserveSeat { get; set; }
        public string? QueryReserveInfo { get;set; }
    }
}