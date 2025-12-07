using IGoLibrary.Core.Data;

namespace IGoLibrary.Core.Services
{
    public interface IGetReserveInfoService
    {
        public ReserveInfo GetReserveInfo(string Cookie, string QueryStatement);
    }
}
