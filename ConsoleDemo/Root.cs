using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsoleDemo
{
    public class Lib_rt
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

    public class LibsItemEx
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
        public string is_open { get; set; }
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
        public Lib_rt lib_rt { get; set; }
    }

    public class LibGroupsItem
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

    public class ReserveEx
    {
        /// <summary>
        /// 
        /// </summary>
        public string isRecordUser { get; set; }
    }

    public class Reserve
    {
        /// <summary>
        /// 
        /// </summary>
        public List<LibsItemEx> libs { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public List<LibGroupsItem> libGroups { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public ReserveEx reserve { get; set; }
    }

    public class Record
    {
        /// <summary>
        /// 
        /// </summary>
        public List<string> libs { get; set; }
    }

    public class Rule
    {
        /// <summary>
        /// 
        /// </summary>
        public string signRule { get; set; }
    }

    public class UserAuth
    {
        /// <summary>
        /// 
        /// </summary>
        public Reserve reserve { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public Record record { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public Rule rule { get; set; }
    }

    public class Data
    {
        /// <summary>
        /// 
        /// </summary>
        public UserAuth userAuth { get; set; }
    }

    public class Root
    {
        /// <summary>
        /// 
        /// </summary>
        public Data data { get; set; }
    }
}
