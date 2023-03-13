using IGoLibrary_Winform.Pages;

namespace IGoLibrary_Winform.Data
{
    public class ReserveInfo
    {
        public ReserveInfo() { }
        public string Token { get; set; }
        public string ExpiredTimeStamp { get; set; }
        public DateTime ExpiredTime { get {
                return FOccupySeat.ConvertToDateTime(Convert.ToInt64(ExpiredTimeStamp));
            } }
        public string LibName { get; set; }
        public @SeatKeyData SeatKeyDta { get; set; }
    }
}