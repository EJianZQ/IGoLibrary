using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Media3D;

namespace IGoLibrary_Winform.Data
{
    public class AllLibsRoot
    {
        public AllLibsRoot_Data data { get; set; }
        public List<AllLibsRoot_ErrorsItem> errors { get; set; }
    }

    public class AllLibsRoot_ErrorsItem
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

    public class AllLibsRoot_Data
    {
        public AllLibsRoot_UserAuth userAuth { get; set; }
    }

    public class AllLibsRoot_UserAuth
    {
        /// <summary>
        /// 
        /// </summary>
        public AllLibsRoot_Reserve reserve { get; set; }
        /// <summary>
        /// 
        /// </summary>
        //public AllLibsRoot_Record record { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public AllLibsRoot_Rule rule { get; set; }
    }

    public class AllLibsRoot_Rule
    {
        /// <summary>
        /// 
        /// </summary>
        public string signRule { get; set; }
    }

    public class AllLibsRoot_Record
    {
        /// <summary>
        /// 
        /// </summary>
        public List<string> libs { get; set; }
    }

    public class AllLibsRoot_Reserve
    {
        /// <summary>
        /// 
        /// </summary>
        public List<AllLibsRoot_LibsItem> libs { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public List<AllLibsRoot_LibGroupsItem> libGroups { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public AllLibsRoot_ReserveEx reserve { get; set; }
    }

    public class AllLibsRoot_ReserveEx
    {
        /// <summary>
        /// 
        /// </summary>
        public string isRecordUser { get; set; }
    }

    public class AllLibsRoot_LibGroupsItem
    {
        /// <summary>
        /// 
        /// </summary>
        public int id { get; set; }
        /// <summary>
        /// 座位预约
        /// </summary>
        public string group_name { get; set; }
    }

    public class AllLibsRoot_LibsItem
    {
        /// <summary>
        /// 
        /// </summary>
        public int lib_id { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string lib_floor { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public bool is_open { get; set; }
        /// <summary>
        /// 200人入馆资格
        /// </summary>
        public string lib_name { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public int lib_type { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public int lib_group_id { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string lib_comment { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public AllLibsRoot_Lib_rt lib_rt { get; set; }
    }

    public class AllLibsRoot_Lib_rt
    {
        /// <summary>
        /// 
        /// </summary>
        public int seats_total { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public int seats_used { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public int seats_booking { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public int seats_has { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public int reserve_ttl { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public int open_time { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string open_time_str { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string close_time { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string close_time_str { get; set; }
        /// <summary>
        /// 1小时
        /// </summary>
        public string advance_booking { get; set; }
    }
}
