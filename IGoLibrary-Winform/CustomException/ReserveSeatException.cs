namespace IGoLibrary_Winform.CustomException
{
    [Serializable]
    public class ReserveSeatException: ApplicationException
    {
        public string error;
        private Exception innerException;
        public ReserveSeatException() { }
        public ReserveSeatException(string msg) : base(msg)
        {
            this.error = msg;
        }
        public ReserveSeatException(string msg, Exception innerException) : base(msg, innerException)
        {
            this.innerException = innerException;
            error = msg;
        }
        public string GetErrorInfo()
        {
            return error;
        }
    }
}
