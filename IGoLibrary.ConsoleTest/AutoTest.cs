using IGoLibrary.Core.Services;
using IGoLibrary.Core.Exceptions;

namespace IGoLibrary.ConsoleTest
{
    public class AutoTest
    {
        public static async Task TestUrlWithCode(string url)
        {
            Console.WriteLine("=".PadRight(80, '='));
            Console.WriteLine("自动化测试：从URL获取Cookie");
            Console.WriteLine("=".PadRight(80, '='));
            Console.WriteLine();

            // 从URL中提取code
            var codeMatch = System.Text.RegularExpressions.Regex.Match(url, @"code=([^&]+)");
            if (!codeMatch.Success)
            {
                Console.WriteLine("✗ 错误：无法从URL中提取code参数");
                return;
            }

            string code = codeMatch.Groups[1].Value;
            Console.WriteLine($"✓ 从URL中提取到code: {code}");
            Console.WriteLine();

            // 测试1: 获取Cookie
            Console.WriteLine("【测试1：获取Cookie】");
            var getCookieService = new GetCookieServiceImpl();

            try
            {
                Console.WriteLine($"正在使用code获取Cookie...");
                var cookie = getCookieService.GetCookie(code);
                Console.WriteLine($"✓ Cookie获取成功!");
                Console.WriteLine($"Cookie内容: {cookie}");
                Console.WriteLine();

                // 测试2: 获取所有图书馆列表
                Console.WriteLine("【测试2：获取所有图书馆列表】");
                var getAllLibsService = new GetAllLibsSummaryImpl();
                var query = "{\"operationName\":\"list\",\"query\":\"query list {\\n userAuth {\\n reserve {\\n libs(libType: -1) {\\n lib_id\\n lib_floor\\n is_open\\n lib_name\\n lib_type\\n lib_group_id\\n lib_comment\\n lib_rt {\\n seats_total\\n seats_used\\n seats_booking\\n seats_has\\n reserve_ttl\\n open_time\\n open_time_str\\n close_time\\n close_time_str\\n advance_booking\\n }\\n }\\n libGroups {\\n id\\n group_name\\n }\\n reserve {\\n isRecordUser\\n }\\n }\\n record {\\n libs {\\n lib_id\\n lib_floor\\n is_open\\n lib_name\\n lib_type\\n lib_group_id\\n lib_comment\\n lib_color_name\\n lib_rt {\\n seats_total\\n seats_used\\n seats_booking\\n seats_has\\n reserve_ttl\\n open_time\\n open_time_str\\n close_time\\n close_time_str\\n advance_booking\\n }\\n }\\n }\\n rule {\\n signRule\\n }\\n }\\n}\"}";

                try
                {
                    Console.WriteLine("正在获取图书馆列表...");
                    var summary = getAllLibsService.GetAllLibsSummary(cookie, query);
                    Console.WriteLine($"✓ 获取成功! 共找到 {summary.libSummaries.Count} 个图书馆");

                    foreach (var lib in summary.libSummaries)
                    {
                        Console.WriteLine($"  - [{lib.LibID}] {lib.Name} - {lib.Floor} - {(lib.IsOpen ? "开放" : "关闭")}");
                    }
                    Console.WriteLine();

                    // 测试3: 获取第一个开放图书馆的详细信息
                    var openLib = summary.libSummaries.FirstOrDefault(l => l.IsOpen);
                    if (openLib != null)
                    {
                        Console.WriteLine($"【测试3：获取图书馆详细信息】");
                        Console.WriteLine($"选择图书馆: {openLib.Name} (ID: {openLib.LibID})");

                        var getLibInfoService = new GetLibInfoServiceImpl();
                        var libQuery = "{\"operationName\":\"libLayout\",\"query\":\"query libLayout($libId: Int, $libType: Int) {\\n userAuth {\\n reserve {\\n libs(libType: $libType, libId: $libId) {\\n lib_id\\n is_open\\n lib_floor\\n lib_name\\n lib_type\\n lib_layout {\\n seats_total\\n seats_booking\\n seats_used\\n max_x\\n max_y\\n seats {\\n x\\n y\\n key\\n type\\n name\\n seat_status\\n status\\n }\\n }\\n }\\n }\\n }\\n}\",\"variables\":{\"libId\":" + openLib.LibID + "}}";

                        try
                        {
                            Console.WriteLine("正在获取图书馆详细信息...");
                            var library = getLibInfoService.GetLibInfo(cookie, libQuery);

                            if (library != null)
                            {
                                Console.WriteLine($"✓ 获取成功!");
                                Console.WriteLine($"图书馆名称: {library.Name}");
                                Console.WriteLine($"图书馆ID: {library.LibID}");
                                Console.WriteLine($"楼层: {library.Floor}");
                                Console.WriteLine($"是否开放: {(library.IsOpen ? "是" : "否")}");
                                Console.WriteLine($"总座位数: {library.SeatsInfo.TotalSeats}");
                                Console.WriteLine($"已预约: {library.SeatsInfo.BookedSeats}");
                                Console.WriteLine($"使用中: {library.SeatsInfo.UsedSeats}");
                                Console.WriteLine($"可用座位: {library.SeatsInfo.AvailableSeats}");

                                if (library.Seats != null && library.Seats.Count > 0)
                                {
                                    Console.WriteLine($"\n前10个座位信息:");
                                    foreach (var seat in library.Seats.Take(10))
                                    {
                                        var statusText = seat.status ? "已占用" : "可用";
                                        Console.WriteLine($"  - 座位 {seat.name}: {statusText} (Key: {seat.key})");
                                    }
                                }
                                Console.WriteLine();

                                // 测试4: 获取预约信息
                                Console.WriteLine("【测试4：获取当前预约信息】");
                                var getReserveInfoService = new GetReserveInfoServiceImpl();
                                var reserveQuery = "query=query{reservations{id,lib{id,name,floor},seat{id,name,key},status,startTime,endTime,token}}";

                                try
                                {
                                    Console.WriteLine("正在获取预约信息...");
                                    var reserveInfo = getReserveInfoService.GetReserveInfo(cookie, reserveQuery);
                                    Console.WriteLine($"✓ 获取成功!");
                                    Console.WriteLine($"图书馆: {reserveInfo.LibName}");
                                    Console.WriteLine($"座位: {reserveInfo.SeatKeyDta.Name}");
                                    Console.WriteLine($"过期时间: {reserveInfo.ExpiredTime}");
                                    Console.WriteLine($"Token: {reserveInfo.Token}");
                                }
                                catch (GetReserveInfoException ex)
                                {
                                    Console.WriteLine($"ℹ 当前无预约信息: {ex.Message}");
                                }
                            }
                        }
                        catch (GetLibInfoException ex)
                        {
                            Console.WriteLine($"✗ 获取图书馆详细信息失败: {ex.Message}");
                        }
                    }
                    else
                    {
                        Console.WriteLine("ℹ 当前没有开放的图书馆");
                    }
                }
                catch (GetAllLibsSummaryException ex)
                {
                    Console.WriteLine($"✗ 获取图书馆列表失败: {ex.Message}");
                }
            }
            catch (GetCookieException ex)
            {
                Console.WriteLine($"✗ 获取Cookie失败: {ex.Message}");
                Console.WriteLine("可能的原因:");
                Console.WriteLine("  1. code已过期（微信授权code通常只能使用一次）");
                Console.WriteLine("  2. 网络连接问题");
                Console.WriteLine("  3. 服务器返回了错误");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ 发生未预期的错误: {ex.Message}");
                Console.WriteLine($"异常类型: {ex.GetType().Name}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"内部异常: {ex.InnerException.Message}");
                }
            }

            Console.WriteLine();
            Console.WriteLine("=".PadRight(80, '='));
            Console.WriteLine("测试完成");
            Console.WriteLine("=".PadRight(80, '='));
        }
    }
}
