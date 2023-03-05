using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsoleDemo
{
    public class LibDetailRoot
    {
        /// <summary>
        /// 
        /// </summary>
        public LibRootData data { get; set; }
    }

    public class LibRootData
    {
        /// <summary>
        /// 
        /// </summary>
        public LibRootUserAuth userAuth { get; set; }
    }

    public class LibRootUserAuth
    {
        /// <summary>
        /// 
        /// </summary>
        public LibRootReserve reserve { get; set; }
    }

    public class LibRootReserve
    {
        /// <summary>
        /// 
        /// </summary>
        public List<LibsItem> libs { get; set; }
    }

    public class LibsItem
    {
        /// <summary>
        /// 
        /// </summary>
        public int lib_id { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string is_open { get; set; }
        /// <summary>
        /// 三楼
        /// </summary>
        public string lib_floor { get; set; }
        /// <summary>
        /// 过刊阅览室
        /// </summary>
        public string lib_name { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public int lib_type { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public Lib_layout lib_layout { get; set; }
    }

    public class Lib_layout
    {
        /// <summary>
        /// 
        /// </summary>
        public int seats_total { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public int seats_booking { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public int seats_used { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public int max_x { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public int max_y { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public List<SeatsItem> seats { get; set; }
    }

    public class SeatsItem
    {
        /// <summary>
        /// 
        /// </summary>
        public int x { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public int y { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string key { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public int type { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string name { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public int seat_status { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public bool status { get; set; }
    }
}
