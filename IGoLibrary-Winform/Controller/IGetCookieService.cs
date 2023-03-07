using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IGoLibrary_Winform.Controller
{
    public interface IGetCookieService
    {
        public string GetCookie(string code);
    }
}
