namespace IGoLibrary_Winform.Controller
{
    public interface ICancelReserveService
    {
        public bool CancelReserve(string Cookie, string QueryStatement, ref string RetMessage);
    }
}