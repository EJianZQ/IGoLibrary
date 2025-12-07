namespace IGoLibrary.Core.Data
{
    public class Library
    {
        public Library() { }

        public bool IsOpen { get; set; }
        public string Name { get; set; }
        public int LibID { get; set; }
        public string Floor { get; set; }
        public SeatsInfo SeatsInfo { get; set; }
        public List<SeatsItem> Seats { get; set; }
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
