namespace IGoLibrary_Winform.CustomException
{
    public class CancelReserveException : ApplicationException
    {
        public string error;
        private Exception innerException;
        public CancelReserveException() { }
        public CancelReserveException(string msg) : base(msg)
        {
            this.error = msg;
        }
        public CancelReserveException(string msg, Exception innerException) : base(msg, innerException)
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
