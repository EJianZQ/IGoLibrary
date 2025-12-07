using System;
using System.IO;
using IGoLibrary.Core.Services;
using IGoLibrary.Core.Exceptions;

class TestCancelRebookSimple
{
    static void Main(string[] args)
    {
        Console.WriteLine("========================================");
        Console.WriteLine("测试取消并重新预约座位");
        Console.WriteLine("========================================");
        Console.WriteLine();

        try
        {
            // 1. 读取 cookie
            var cookieFilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "IGoLibrary",
                "cookie.txt"
            );

            if (!File.Exists(cookieFilePath))
            {
                Console.WriteLine($"❌ Cookie 文件不存在: {cookieFilePath}");
                return;
            }

            string cookie = File.ReadAllText(cookieFilePath);
            Console.WriteLine($"✅ 已读取 Cookie");
            Console.WriteLine();

            // 2. 获取当前预约信息
            Console.WriteLine("步骤 1: 获取当前预约信息...");
            var getReserveInfoService = new GetReserveInfoServiceImpl();
            string queryReserveInfo = "{\"operationName\":\"index\",\"query\":\"query index($pos: String!, $param: [hash]) {\\n userAuth {\\n oftenseat {\\n list {\\n id\\n info\\n lib_id\\n seat_key\\n status\\n }\\n }\\n message {\\n new(from: \\\"system\\\") {\\n has\\n from_user\\n title\\n num\\n }\\n indexMsg {\\n message_id\\n title\\n content\\n isread\\n isused\\n from_user\\n create_time\\n }\\n }\\n reserve {\\n reserve {\\n token\\n status\\n user_id\\n user_nick\\n sch_name\\n lib_id\\n lib_name\\n lib_floor\\n seat_key\\n seat_name\\n date\\n exp_date\\n exp_date_str\\n validate_date\\n hold_date\\n diff\\n diff_str\\n mark_source\\n isRecordUser\\n isChooseSeat\\n isRecord\\n mistakeNum\\n openTime\\n threshold\\n daynum\\n mistakeNum\\n closeTime\\n timerange\\n forbidQrValid\\n renewTimeNext\\n forbidRenewTime\\n forbidWechatCancle\\n }\\n getSToken\\n }\\n currentUser {\\n user_id\\n user_nick\\n user_mobile\\n user_sex\\n user_sch_id\\n user_sch\\n user_last_login\\n user_avatar(size: MIDDLE)\\n user_adate\\n user_student_no\\n user_student_name\\n area_name\\n user_deny {\\n deny_deadline\\n }\\n sch {\\n sch_id\\n sch_name\\n activityUrl\\n isShowCommon\\n isBusy\\n }\\n }\\n }\\n ad(pos: $pos, param: $param) {\\n name\\n pic\\n url\\n }\\n}\",\"variables\":{\"pos\":\"App-首页\"}}";

            var reserveInfo = getReserveInfoService.GetReserveInfo(cookie, queryReserveInfo);

            Console.WriteLine($"✅ 当前预约信息:");
            Console.WriteLine($"   图书馆: {reserveInfo.LibName}");
            Console.WriteLine($"   座位: {reserveInfo.SeatKeyDta.Name}");
            Console.WriteLine($"   座位Key: {reserveInfo.SeatKeyDta.Key}");
            Console.WriteLine($"   Token: {reserveInfo.Token}");
            Console.WriteLine();

            // 保存座位信息用于重新预约
            string seatKey = reserveInfo.SeatKeyDta.Key;
            string seatName = reserveInfo.SeatKeyDta.Name;

            // 3. 取消预约
            Console.WriteLine("步骤 2: 取消当前预约...");
            var cancelReserveService = new CancelReserveServiceImpl();
            string cancelSyntax = "{\"operationName\":\"reserveCancle\",\"query\":\"mutation reserveCancle($sToken: String!) {\\n userAuth {\\n reserve {\\n reserveCancle(sToken: $sToken) {\\n timerange\\n img\\n hours\\n mins\\n per\\n }\\n }\\n }\\n}\",\"variables\":{\"sToken\":\"ReplaceMe\"}}";
            cancelSyntax = cancelSyntax.Replace("ReplaceMe", reserveInfo.Token);

            string errorMessage = "";
            bool cancelResult = cancelReserveService.CancelReserve(cookie, cancelSyntax, ref errorMessage);

            if (cancelResult)
            {
                Console.WriteLine($"✅ 取消预约成功");
            }
            else
            {
                Console.WriteLine($"❌ 取消预约失败: {errorMessage}");
                return;
            }
            Console.WriteLine();

            // 4. 等待2秒
            Console.WriteLine("等待 2 秒后重新预约...");
            System.Threading.Thread.Sleep(2000);
            Console.WriteLine();

            // 5. 重新预约
            Console.WriteLine("步骤 3: 重新预约座位...");
            Console.Write("请输入图书馆 libId (例如: 100457296): ");
            string? libIdInput = Console.ReadLine();
            if (!int.TryParse(libIdInput, out int libId))
            {
                Console.WriteLine("❌ libId 格式错误");
                return;
            }

            var reserveSeatService = new ReserveSeatServiceImpl();
            string reserveSyntax = "{\"operationName\":\"reserueSeat\",\"query\":\"mutation reserueSeat($libId: Int!, $seatKey: String!, $captchaCode: String, $captcha: String!) {\\n userAuth {\\n reserve {\\n reserueSeat(\\n libId: $libId\\n seatKey: $seatKey\\n captchaCode: $captchaCode\\n captcha: $captcha\\n )\\n }\\n }\\n}\",\"variables\":{\"seatKey\":\"ReplaceMeBySeatKey\",\"libId\":ReplaceMeByLibID,\"captchaCode\":\"\",\"captcha\":\"\"}}";
            reserveSyntax = reserveSyntax.Replace("ReplaceMeBySeatKey", seatKey);
            reserveSyntax = reserveSyntax.Replace("ReplaceMeByLibID", libId.ToString());

            bool reserveResult = reserveSeatService.ReserveSeat(cookie, reserveSyntax);

            if (reserveResult)
            {
                Console.WriteLine($"✅ 预约成功！");
                Console.WriteLine($"   已重新预约座位: {seatName}");
            }
            else
            {
                Console.WriteLine($"❌ 预约失败");
            }

            Console.WriteLine();
            Console.WriteLine("========================================");
            Console.WriteLine("测试完成");
            Console.WriteLine("========================================");
        }
        catch (GetReserveInfoException ex)
        {
            Console.WriteLine($"❌ 获取预约信息失败: {ex.Message}");
        }
        catch (CancelReserveException ex)
        {
            Console.WriteLine($"❌ 取消预约失败: {ex.Message}");
        }
        catch (ReserveSeatException ex)
        {
            Console.WriteLine($"❌ 预约座位失败: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ 发生异常: {ex.Message}");
            Console.WriteLine($"堆栈跟踪: {ex.StackTrace}");
        }
    }
}
