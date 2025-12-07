using System;
using IGoLibrary.Core.Services;
using IGoLibrary.Core.Exceptions;

class QuickCheck
{
    static void Main(string[] args)
    {
        string cookie = "Authorization=eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiJ9.eyJ1c2VySWQiOjQ0NzQ3Njg0LCJzY2hJZCI6MjIsImV4cGlyZUF0IjoxNzY1MTE1NDM2LCJ0YWciOiJjb29raWUtNjZlMiJ9.GZY10rQfMyzLGdr2D5MT6YExqdHTb5DpKCLkD7BEmPS17NSWfA7NMSxyjJiDalXajW-4EM8cuMl2iHP8EE9BFo5E0xl7-hDKG-Y7bFwK7d3ggok_d2qFDk46tPOaTbGAVrO8fyTFQ_LUCgueDeI6J-tDTfljzCGOrO9619M1FdwOWMSnIMIQvfGI5ukWLuEH4p6ZjutVkQSdw_1OnKIrsj1Zv0VEafgYl8DcyyFC04mj4TaXZPOrEOBXo4dl0hYh6ksRVlIygyPKsTil-EmS45280AWqX7O-6pNT6leHR-0EUT1bY6u4vhNkxLu8sHFyKxwTfnJ7bLj66DR_LVgorQ; SERVERID=b9fc7bd86d2eed91b23d7347e0ee995e|1765108236|1765108236";

        Console.WriteLine("检查当前预约状态...");
        Console.WriteLine();

        try
        {
            var service = new GetReserveInfoServiceImpl();

            // 使用完整的 index 查询
            string fullQuery = "{\"operationName\":\"index\",\"query\":\"query index($pos: String!, $param: [hash]) {\\n userAuth {\\n oftenseat {\\n list {\\n id\\n info\\n lib_id\\n seat_key\\n status\\n }\\n }\\n message {\\n new(from: \\\"system\\\") {\\n has\\n from_user\\n title\\n num\\n }\\n indexMsg {\\n message_id\\n title\\n content\\n isread\\n isused\\n from_user\\n create_time\\n }\\n }\\n reserve {\\n reserve {\\n token\\n status\\n user_id\\n user_nick\\n sch_name\\n lib_id\\n lib_name\\n lib_floor\\n seat_key\\n seat_name\\n date\\n exp_date\\n exp_date_str\\n validate_date\\n hold_date\\n diff\\n diff_str\\n mark_source\\n isRecordUser\\n isChooseSeat\\n isRecord\\n mistakeNum\\n openTime\\n threshold\\n daynum\\n mistakeNum\\n closeTime\\n timerange\\n forbidQrValid\\n renewTimeNext\\n forbidRenewTime\\n forbidWechatCancle\\n }\\n getSToken\\n }\\n currentUser {\\n user_id\\n user_nick\\n user_mobile\\n user_sex\\n user_sch_id\\n user_sch\\n user_last_login\\n user_avatar(size: MIDDLE)\\n user_adate\\n user_student_no\\n user_student_name\\n area_name\\n user_deny {\\n deny_deadline\\n }\\n sch {\\n sch_id\\n sch_name\\n activityUrl\\n isShowCommon\\n isBusy\\n }\\n }\\n }\\n ad(pos: $pos, param: $param) {\\n name\\n pic\\n url\\n }\\n}\",\"variables\":{\"pos\":\"App-首页\"}}";

            var info = service.GetReserveInfo(cookie, fullQuery);

            Console.WriteLine("✅ 找到预约信息:");
            Console.WriteLine($"   图书馆: {info.LibName}");
            Console.WriteLine($"   座位: {info.SeatKeyDta.Name}");
            Console.WriteLine($"   座位Key: {info.SeatKeyDta.Key}");
            Console.WriteLine($"   Token: {info.Token}");
            Console.WriteLine($"   过期时间: {info.ExpiredTimeStamp}");
        }
        catch (GetReserveInfoException ex)
        {
            Console.WriteLine($"❌ 错误: {ex.Message}");
            Console.WriteLine();
            Console.WriteLine("可能的原因:");
            Console.WriteLine("1. Cookie 已过期");
            Console.WriteLine("2. 当前确实没有预约");
            Console.WriteLine("3. 预约已经过期");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ 异常: {ex.Message}");
        }
    }
}
