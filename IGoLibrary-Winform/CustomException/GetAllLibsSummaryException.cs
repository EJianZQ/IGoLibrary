using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IGoLibrary_Winform.CustomException
{
    public class GetAllLibsSummaryException : ApplicationException
    {
        public string error;
        private Exception innerException;
        public GetAllLibsSummaryException() { }
        public GetAllLibsSummaryException(string msg) : base(msg)
        {
            this.error = msg;
        }
        public GetAllLibsSummaryException(string msg, Exception innerException) : base(msg, innerException)
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
