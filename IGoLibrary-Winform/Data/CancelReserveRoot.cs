namespace IGoLibrary_Winform.Data
{
    public class CancelReserveRoot
    {
        /// <summary>
        /// 
        /// </summary>
        public List<CancelReserveRoot_ErrorsItem> errors { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public CancelReserveRoot_Data data { get; set; }
    }

    public class CancelReserveRoot_Data
    {
        /// <summary>
        /// 
        /// </summary>
        public CancelReserveRoot_UserAuth userAuth { get; set; }
    }

    public class CancelReserveRoot_UserAuth
    {
        /// <summary>
        /// 
        /// </summary>
        public CancelReserveRoot_Reserve reserve { get; set; }
    }

    public class CancelReserveRoot_Reserve
    {
        /// <summary>
        /// 
        /// </summary>
        public string reserveCancle { get; set; }
    }

    public class CancelReserveRoot_ErrorsItem
    {
        /// <summary>
        /// 您还没有预定座位
        /// </summary>
        public string msg { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public int code { get; set; }
    }
}