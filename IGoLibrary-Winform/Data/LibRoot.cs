using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IGoLibrary_Winform.Data
{
    public class LibRoot
    {
        public LibRoot_Data data { get; set; }
        public List<LibRoot_ErrorsItem> errors { get; set; }
    }
    public class LibRoot_ErrorsItem
    {
        /// <summary>
        /// 如：场馆不存在
        /// </summary>
        public string msg { get; set; }
        /// <summary>
        /// 错误代码
        /// </summary>
        public int code { get; set; }
    }

    public class LibRoot_Data
    {
        public LibRoot_UserAuth userAuth { get; set; }
    }

    public class LibRoot_UserAuth
    {
        public LibRoot_Reserve reserve { get; set; }
    }

    public class LibRoot_Reserve
    {
        public List<LibRoot_LibsItem> libs { get; set; }
    }

    public class LibRoot_LibsItem
    {
        /// <summary>
        /// ID
        /// </summary>
        public int lib_id { get; set; }
        /// <summary>
        /// 当前是否开放
        /// </summary>
        public bool is_open { get; set; }
        /// <summary>
        /// 楼层，如：三楼
        /// </summary>
        public string lib_floor { get; set; }
        /// <summary>
        /// 场馆名称，如：过刊阅览室
        /// </summary>
        public string lib_name { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public int lib_type { get; set; }
        /// <summary>
        /// 场馆布局
        /// </summary>
        public LibRoot_Lib_layout lib_layout { get; set; }
    }

    public class LibRoot_Lib_layout
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
