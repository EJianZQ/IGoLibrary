using IGoLibrary_Winform.Data;

namespace IGoLibrary_Winform.Controller
{
    public interface IGetReserveInfoService
    {
        public ReserveInfo GetReserveInfo(string Cookie, string QueryStatement);
    }
}