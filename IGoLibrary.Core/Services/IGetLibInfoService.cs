using IGoLibrary.Core.Data;

namespace IGoLibrary.Core.Services
{
    public interface IGetLibInfoService
    {
        public Library GetLibInfo(string Cookies,string QueryStatement);
        public List<SeatsItem> GetLibSeats(LibRoot root);
        public Library? GetLibInfo_Debug(string Cookies, string QueryStatement);
    }
}
