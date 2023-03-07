using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IGoLibrary_Winform.CustomException
{
    public class GetCookieException : ApplicationException
    {
        public string error;
        private Exception innerException;
        public GetCookieException() { }
        public GetCookieException(string msg) : base(msg)
        {
            this.error = msg;
        }
        public GetCookieException(string msg, Exception innerException) : base(msg, innerException)
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
