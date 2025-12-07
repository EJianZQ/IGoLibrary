namespace IGoLibrary.Core.Services
{
    public interface ICancelReserveService
    {
        public bool CancelReserve(string Cookie, string QueryStatement, ref string RetMessage);
    }
}
