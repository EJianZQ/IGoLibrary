using IGoLibrary.Core.Data;

namespace IGoLibrary.Core.Services
{
    /// <summary>
    /// 座位筛选服务，用于从图书馆数据中筛选出指定的座位
    /// </summary>
    public class SeatFilterService
    {
        /// <summary>
        /// 从图书馆数据中获取指定座位的最新状态
        /// </summary>
        public List<SeatKeyData> GetSelectedSeatsKeyData(Library library, List<SeatKeyData> selectedSeats)
        {
            if (library?.Seats == null || selectedSeats == null)
            {
                return new List<SeatKeyData>();
            }

            var result = new List<SeatKeyData>();

            foreach (var selectedSeat in selectedSeats)
            {
                // 在图书馆的座位列表中查找匹配的座位
                var matchedSeat = library.Seats.FirstOrDefault(s => s.key == selectedSeat.Key);

                if (matchedSeat != null)
                {
                    result.Add(new SeatKeyData
                    {
                        Name = matchedSeat.name,
                        Status = matchedSeat.status ? "有人" : "无人",
                        Key = matchedSeat.key
                    });
                }
            }

            return result;
        }
    }
}
