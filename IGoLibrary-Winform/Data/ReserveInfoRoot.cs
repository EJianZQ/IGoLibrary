namespace IGoLibrary_Winform.Data
{
    public class ReserveInfoRoot
    {
        /// <summary>
        /// 
        /// </summary>
        public ReserveInfoRoot_Data data { get; set; }
        public List<ReserveInfoRoot_ErrorsItem> errors { get; set; }
    }

    public class ReserveInfoRoot_ErrorsItem
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
    public class ReserveInfoRoot_Data
    {
        /// <summary>
        /// 
        /// </summary>
        public ReserveInfoRoot_UserAuth userAuth { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public List<ReserveInfoRoot_AdItem> ad { get; set; }
    }

    public class ReserveInfoRoot_AdItem
    {
        /// <summary>
        /// 统一头图
        /// </summary>
        public string name { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string pic { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string url { get; set; }
    }

    public class ReserveInfoRoot_UserAuth
    {
        /// <summary>
        /// 
        /// </summary>
        public ReserveInfoRoot_Oftenseat oftenseat { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public ReserveInfoRoot_Message message { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public ReserveInfoRoot_Reserve reserve { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public ReserveInfoRoot_CurrentUser currentUser { get; set; }
    }

    public class ReserveInfoRoot_CurrentUser
    {
        /// <summary>
        /// 
        /// </summary>
        public int user_id { get; set; }
        /// <summary>
        /// .NET天下第一
        /// </summary>
        public string user_nick { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string user_mobile { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public int user_sex { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public int user_sch_id { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string user_sch { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public int user_last_login { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string user_avatar { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public int user_adate { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string user_student_no { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string user_student_name { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string area_name { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public ReserveInfoRoot_User_deny user_deny { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public ReserveInfoRoot_Sch sch { get; set; }
    }

    public class ReserveInfoRoot_Sch
    {
        /// <summary>
        /// 
        /// </summary>
        public int sch_id { get; set; }
        /// <summary>
        /// 常州工学院
        /// </summary>
        public string sch_name { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string activityUrl { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string isShowCommon { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string isBusy { get; set; }
    }

    public class ReserveInfoRoot_User_deny
    {
        /// <summary>
        /// 
        /// </summary>
        public string deny_deadline { get; set; }
    }

    public class ReserveInfoRoot_Reserve
    {
        /// <summary>
        /// 
        /// </summary>
        public ReserveInfoRoot_ReserveEx reserve { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string getSToken { get; set; }
    }

    public class ReserveInfoRoot_ReserveEx
    {
        /// <summary>
        /// 
        /// </summary>
        public string token { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public int status { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public int user_id { get; set; }
        /// <summary>
        /// .NET天下第一
        /// </summary>
        public string user_nick { get; set; }
        /// <summary>
        /// 常州工学院
        /// </summary>
        public string sch_name { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public int lib_id { get; set; }
        /// <summary>
        /// 过刊阅览室
        /// </summary>
        public string lib_name { get; set; }
        /// <summary>
        /// 三楼
        /// </summary>
        public string lib_floor { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string seat_key { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string seat_name { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public int date { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public int exp_date { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string exp_date_str { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public int validate_date { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string hold_date { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string diff { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string diff_str { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string mark_source { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string isRecordUser { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string isChooseSeat { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string isRecord { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string mistakeNum { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string openTime { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string threshold { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string daynum { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string closeTime { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string timerange { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string forbidQrValid { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string renewTimeNext { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string forbidRenewTime { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string forbidWechatCancle { get; set; }
    }

    public class ReserveInfoRoot_Message
    {
        /// <summary>
        /// 
        /// </summary>
        public @ReserveInfoRoot_new @new { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string indexMsg { get; set; }
    }

    public class @ReserveInfoRoot_new
    {
        /// <summary>
        /// 
        /// </summary>
        public string has { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string from_user { get; set; }
        /// <summary>
        /// 通知
        /// </summary>
        public string title { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public int num { get; set; }
    }

    public class ReserveInfoRoot_Oftenseat
    {
        /// <summary>
        /// 
        /// </summary>
        public List<ReserveInfoRoot_ListItem> list { get; set; }
    }

    public class ReserveInfoRoot_ListItem
    {
        /// <summary>
        /// 
        /// </summary>
        public int id { get; set; }
        /// <summary>
        /// 艺术阅览室 58号
        /// </summary>
        public string info { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public int lib_id { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string seat_key { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public int status { get; set; }
    }
}