namespace IGoLibrary_Winform.CustomException
{
    [Serializable]
    public class GetLibInfoException : ApplicationException
    {
        public string error;
        private Exception innerException;
        public GetLibInfoException() { }
        public GetLibInfoException(string msg) : base(msg) 
        {
            this.error = msg;
        }
        public GetLibInfoException(string msg, Exception innerException) : base(msg, innerException)
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
