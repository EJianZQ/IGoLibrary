using IGoLibrary_Winform.Data;

namespace IGoLibrary_Winform.Controller
{
    public interface IGetAllLibsSummaryService
    {
        public AllLibsSummary GetAllLibsSummary(string Cookie, string QueryStatement);
    }
}