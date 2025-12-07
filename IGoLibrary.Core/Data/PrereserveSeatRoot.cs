using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IGoLibrary.Core.Data
{
    // 预约抢座响应数据结构
    public class PrereserveSeatRoot
    {
        public PrereserveSeatData data { get; set; }
        public List<PrereserveSeatError> errors { get; set; }
    }

    public class PrereserveSeatData
    {
        public PrereserveSeatUserAuth userAuth { get; set; }
    }

    public class PrereserveSeatUserAuth
    {
        public PrereserveSeatPrereserve prereserve { get; set; }
    }

    public class PrereserveSeatPrereserve
    {
        public string save { get; set; }
    }

    public class PrereserveSeatError
    {
        public string msg { get; set; }
        public string debugMessage { get; set; }
    }
}
