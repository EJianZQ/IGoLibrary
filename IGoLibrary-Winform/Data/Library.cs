using IGoLibrary_Winform.Controller;
using IGoLibrary_Winform.Data;

namespace IGoLibrary_Winform.Data
{
    public class Library
    {
        public Library(LibRoot root) 
        {
            this.IsOpen = root.data.userAuth.reserve.libs[0].is_open;
            this.Name = root.data.userAuth.reserve.libs[0].lib_name;
            this.LibID = root.data.userAuth.reserve.libs[0].lib_id;
            this.Floor = root.data.userAuth.reserve.libs[0].lib_floor;
            this.SeatsInfo = new SeatsInfo() { TotalSeats = root.data.userAuth.reserve.libs[0].lib_layout.seats_total, 
                BookedSeats = root.data.userAuth.reserve.libs[0].lib_layout.seats_booking,
                UsedSeats = root.data.userAuth.reserve.libs[0].lib_layout.seats_used
            };
            var service = new GetLibInfoServiceImpl();
            this.Seats = service.GetLibSeats(root);
        }
        public bool IsOpen { get; set; }
        public string Name { get; set; }
        public int LibID { get; set; }
        public string Floor { get; set; }
        public SeatsInfo SeatsInfo { get; set; }
        public List<SeatsItem> Seats { get; set; }
        public List<SeatKeyData> GetSelectedSeatsKeyData(List<SeatKeyData> waitingGrabSeats)
        {
            List<SeatKeyData> temp = new List<SeatKeyData>();
            foreach(var seatSingle in Seats)
            {
                for(int i = 0;i < waitingGrabSeats.Count; i++)
                {
                    if(seatSingle.name == waitingGrabSeats[i].Name)
                    {
                        temp.Add(new SeatKeyData() { Name= seatSingle.name ,Status = seatSingle.status ? "有人":"无人",Key = seatSingle.key});
                    }
                }
            }
            return temp;
        }
    }

    public class SeatsInfo
    {
        public SeatsInfo() { }
        public int TotalSeats { get; set; }
        public int BookedSeats { get; set; }
        public int UsedSeats { get; set; }
        public int AvailableSeats { get {
                return TotalSeats - BookedSeats - UsedSeats;
            }}

    }
}
