using IGoLibrary.Core.Data;

namespace IGoLibrary.Core.Services
{
    public interface IGetAllLibsSummaryService
    {
        public AllLibsSummary GetAllLibsSummary(string Cookie, string QueryStatement);
    }
}
