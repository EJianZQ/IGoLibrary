using IGoLibrary.Core.Services;
using IGoLibrary.Core.Exceptions;
using IGoLibrary.ConsoleTest;

namespace IGoLibrary.ConsoleTest
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // 检查是否有命令行参数（自动测试模式）
            if (args.Length > 0 && args[0] == "--auto-test")
            {
                string testUrl = "http://wechat.v2.traceint.com/index.php/graphql/?operationName=index&query=query%7BuserAuth%7BtongJi%7Brank%7D%7D%7D&code=031BRF0w3VOW763IOX1w3FDTyi3BRF0y&state=1";
                if (args.Length > 1)
                {
                    testUrl = args[1];
                }
                await AutoTest.TestUrlWithCode(testUrl);
                return;
            }

            // 检查是否运行明日预约测试
            if (args.Length > 0 && args[0] == "--prereserve-test")
            {
                await PrereserveTest.RunPrereserveTest();
                return;
            }

            // 检查是否运行取消并重新预约测试
            if (args.Length > 0 && args[0] == "--test-cancel-reserve")
            {
                string cookie = args.Length > 1 ? args[1] : "";

                if (string.IsNullOrEmpty(cookie))
                {
                    Console.WriteLine("❌ 请提供Cookie参数");
                    return;
                }

                Console.WriteLine("========================================");
                Console.WriteLine("🔍 测试取消预约→重新预约流程");
                Console.WriteLine("========================================");
                Console.WriteLine();

                try
                {
                    // 1. 获取当前预约信息
                    Console.WriteLine("【步骤1】获取当前预约信息...");
                    var reserveInfoService = new GetReserveInfoServiceImpl();

                    var query = @"{""operationName"":""reserveInfo"",""query"":""query reserveInfo {\n  userAuth {\n    reserve {\n      reserve {\n        lib_name\n        seat_name\n        seat_key\n        exp_date\n      }\n      getSToken\n    }\n  }\n}\n"",""variables"":{}}";

                    var reserveInfo = reserveInfoService.GetReserveInfo(cookie, query);

                    Console.WriteLine($"✅ 当前预约信息:");
                    Console.WriteLine($"   图书馆: {reserveInfo.LibName}");
                    Console.WriteLine($"   座位: {reserveInfo.SeatKeyDta.Name}号");
                    Console.WriteLine($"   座位Key: {reserveInfo.SeatKeyDta.Key}");
                    Console.WriteLine();

                    string seatKey = reserveInfo.SeatKeyDta.Key;
                    string seatName = reserveInfo.SeatKeyDta.Name;

                    // 2. 取消预约
                    Console.WriteLine("【步骤2】取消当前预约...");
                    var cancelService = new CancelReserveServiceImpl();

                    var cancelQuery = @"{""operationName"":""cancelReserve"",""query"":""mutation cancelReserve {\n  userAuth {\n    reserve {\n      cancelReserve\n    }\n  }\n}\n"",""variables"":{}}";

                    string retMessage = "";
                    bool cancelSuccess = cancelService.CancelReserve(cookie, cancelQuery, ref retMessage);

                    if (cancelSuccess)
                    {
                        Console.WriteLine($"✅ 取消预约成功");
                        Console.WriteLine($"   时间戳: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                        Console.WriteLine();
                    }
                    else
                    {
                        Console.WriteLine($"❌ 取消预约失败: {retMessage}");
                        return;
                    }

                    // 等待1秒
                    Console.WriteLine("⏳ 等待1秒后重新预约...");
                    System.Threading.Thread.Sleep(1000);
                    Console.WriteLine();

                    // 3. 重新预约
                    Console.WriteLine("【步骤3】重新预约座位...");
                    var prereserveSeatService = new PrereserveSeatServiceImpl();

                    // 需要获取LibID，从reserveInfo中无法直接获取，使用默认值430
                    int libId = 430; // 默认图书馆ID

                    Console.WriteLine($"🚀 正在调用PrereserveSeat API...");
                    Console.WriteLine($"   座位: {seatName}号");
                    Console.WriteLine($"   座位Key: {seatKey}");
                    Console.WriteLine($"   图书馆LibID: {libId}");
                    Console.WriteLine($"⏰ 时间戳: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                    Console.WriteLine();

                    var startTime = DateTime.Now;
                    bool result = prereserveSeatService.PrereserveSeat(cookie, seatKey, libId);
                    var duration = (DateTime.Now - startTime).TotalMilliseconds;

                    Console.WriteLine("========================================");
                    Console.WriteLine("✅ 重新预约成功");
                    Console.WriteLine("========================================");
                    Console.WriteLine($"返回结果: {result}");
                    Console.WriteLine($"耗时: {duration:F0}ms");
                    Console.WriteLine($"时间戳: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                    Console.WriteLine();

                    if (result)
                    {
                        Console.WriteLine("🎉 完整流程测试成功！");
                        Console.WriteLine();
                        Console.WriteLine("💡 测试结论：");
                        Console.WriteLine("   ➤ 取消预约功能正常");
                        Console.WriteLine("   ➤ 重新预约功能正常");
                        Console.WriteLine("   ➤ PrereserveSeat API工作正常");
                        Console.WriteLine("   ➤ 系统以API返回信息判断成功/失败");
                    }
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
                    Console.WriteLine("========================================");
                    Console.WriteLine("❌ 重新预约失败 - 服务器返回错误");
                    Console.WriteLine("========================================");
                    Console.WriteLine($"错误信息: {ex.Message}");
                    Console.WriteLine($"时间戳: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                    Console.WriteLine();

                    if (ex.Message.Contains("座位有人") || ex.Message.Contains("已被预约") || ex.Message.Contains("已预约"))
                    {
                        Console.WriteLine("💡 座位在取消后立即被其他人预约了！");
                        Console.WriteLine("   这说明竞争非常激烈，需要更快的速度。");
                    }
                    else
                    {
                        Console.WriteLine($"💡 错误原因: {ex.Message}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("========================================");
                    Console.WriteLine("❌ 发生异常");
                    Console.WriteLine("========================================");
                    Console.WriteLine($"异常类型: {ex.GetType().Name}");
                    Console.WriteLine($"异常信息: {ex.Message}");
                }

                Console.WriteLine();
                Console.WriteLine("========================================");
                Console.WriteLine("测试完成");
                Console.WriteLine("========================================");
                return;
            }

            // 检查是否查找座位信息
            if (args.Length > 0 && args[0] == "--find-seat")
            {
                string cookie = args.Length > 1 ? args[1] : "";
                string seatName = args.Length > 2 ? args[2] : "48";

                if (string.IsNullOrEmpty(cookie))
                {
                    Console.WriteLine("❌ 请提供Cookie参数");
                    return;
                }

                Console.WriteLine("========================================");
                Console.WriteLine($"🔍 查找座位: {seatName}号");
                Console.WriteLine("========================================");
                Console.WriteLine();

                try
                {
                    // 1. 获取所有图书馆列表
                    Console.WriteLine("【步骤1】获取所有图书馆列表...");
                    var libsSummaryService = new GetAllLibsSummaryImpl();

                    var libsQuery = @"{""operationName"":""libs"",""query"":""query libs {\n  userAuth {\n    reserve {\n      libs {\n        lib_id\n        lib_name\n        lib_floor\n        is_open\n      }\n    }\n  }\n}\n"",""variables"":{}}";

                    var summary = libsSummaryService.GetAllLibsSummary(cookie, libsQuery);

                    Console.WriteLine($"✅ 找到 {summary.libSummaries.Count} 个图书馆");
                    Console.WriteLine();

                    // 2. 查找包含"东"的图书馆
                    Console.WriteLine("【步骤2】查找包含'东'的图书馆...");
                    var targetLibs = summary.libSummaries.Where(lib =>
                        lib.Name.Contains("东") || lib.Floor.Contains("东")).ToList();

                    if (targetLibs.Count == 0)
                    {
                        Console.WriteLine("❌ 没有找到包含'东'的图书馆");
                        Console.WriteLine();
                        Console.WriteLine("所有图书馆列表：");
                        foreach (var lib in summary.libSummaries)
                        {
                            Console.WriteLine($"  - [{lib.LibID}] {lib.Name} - {lib.Floor}");
                        }
                        return;
                    }

                    Console.WriteLine($"✅ 找到 {targetLibs.Count} 个相关图书馆：");
                    foreach (var lib in targetLibs)
                    {
                        Console.WriteLine($"  - [{lib.LibID}] {lib.Name} - {lib.Floor}");
                    }
                    Console.WriteLine();

                    // 3. 遍历每个图书馆，查找座位
                    Console.WriteLine($"【步骤3】在这些图书馆中查找 {seatName} 号座位...");
                    bool found = false;

                    foreach (var lib in targetLibs)
                    {
                        try
                        {
                            Console.WriteLine($"正在查询 [{lib.LibID}] {lib.Name} - {lib.Floor}...");

                            var libInfoService = new GetLibInfoServiceImpl();
                            var libQuery = $@"{{""operationName"":""index"",""query"":""query index($libId: Int, $libType: Int) {{\n  userAuth {{\n    reserve {{\n      libs(libType: $libType, libId: $libId) {{\n        lib_id\n        lib_name\n        lib_floor\n        is_open\n        lib_layout {{\n          seats_total\n          seats_booking\n          seats_used\n          seats {{\n            key\n            name\n            status\n          }}\n        }}\n      }}\n    }}\n  }}\n}}\n"",""variables"":{{""libId"":{lib.LibID},""libType"":1}}}}";

                            var library = libInfoService.GetLibInfo(cookie, libQuery);

                            if (library != null && library.Seats != null)
                            {
                                // 查找座位
                                var seat = library.Seats.FirstOrDefault(s =>
                                    s.name == seatName ||
                                    s.name == $"D{seatName}" ||
                                    s.name == $"0{seatName}" ||
                                    s.name.EndsWith(seatName));

                                if (seat != null)
                                {
                                    found = true;
                                    Console.WriteLine();
                                    Console.WriteLine("========================================");
                                    Console.WriteLine("✅ 找到座位！");
                                    Console.WriteLine("========================================");
                                    Console.WriteLine($"图书馆: {library.Name}");
                                    Console.WriteLine($"楼层: {library.Floor}");
                                    Console.WriteLine($"LibID: {library.LibID}");
                                    Console.WriteLine($"座位名称: {seat.name}");
                                    Console.WriteLine($"座位Key: {seat.key}");
                                    Console.WriteLine($"座位状态: {(seat.status ? "有人" : "无人")}");
                                    Console.WriteLine();

                                    // 立即测试预约
                                    Console.WriteLine("【步骤4】测试预约此座位...");
                                    var prereserveSeatService = new PrereserveSeatServiceImpl();

                                    Console.WriteLine($"🚀 正在调用PrereserveSeat API...");
                                    Console.WriteLine($"⏰ 时间戳: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                                    Console.WriteLine();

                                    var startTime = DateTime.Now;
                                    bool result = prereserveSeatService.PrereserveSeat(cookie, seat.key, library.LibID);
                                    var duration = (DateTime.Now - startTime).TotalMilliseconds;

                                    Console.WriteLine("========================================");
                                    Console.WriteLine("✅ 预约测试完成");
                                    Console.WriteLine("========================================");
                                    Console.WriteLine($"返回结果: {result}");
                                    Console.WriteLine($"耗时: {duration:F0}ms");
                                    Console.WriteLine($"时间戳: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                                    Console.WriteLine();

                                    if (result)
                                    {
                                        Console.WriteLine("🎉 预约成功！");
                                        Console.WriteLine();
                                        Console.WriteLine("💡 测试结论：");
                                        Console.WriteLine("   ➤ PrereserveSeat API工作正常");
                                        Console.WriteLine("   ➤ 座位在测试时是空闲的");
                                        Console.WriteLine("   ➤ 系统以API返回信息判断成功/失败");
                                    }

                                    break;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"  查询失败: {ex.Message}");
                        }
                    }

                    if (!found)
                    {
                        Console.WriteLine();
                        Console.WriteLine($"❌ 在所有相关图书馆中都没有找到 {seatName} 号座位");
                    }
                }
                catch (ReserveSeatException ex)
                {
                    Console.WriteLine();
                    Console.WriteLine("========================================");
                    Console.WriteLine("❌ 预约失败 - 服务器返回错误");
                    Console.WriteLine("========================================");
                    Console.WriteLine($"错误信息: {ex.Message}");
                    Console.WriteLine($"时间戳: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                    Console.WriteLine();

                    if (ex.Message.Contains("座位有人") || ex.Message.Contains("已被预约") || ex.Message.Contains("已预约"))
                    {
                        Console.WriteLine("💡 这就是'座位有人'的错误！");
                        Console.WriteLine("   系统根据API返回的这个错误信息判断座位有人。");
                    }
                    else if (ex.Message.Contains("已经预约") || ex.Message.Contains("已有预约"))
                    {
                        Console.WriteLine("💡 你已经有一个预约了！");
                        Console.WriteLine("   一个用户同时只能有一个预约。");
                    }
                    else
                    {
                        Console.WriteLine($"💡 错误原因: {ex.Message}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine();
                    Console.WriteLine("========================================");
                    Console.WriteLine("❌ 发生异常");
                    Console.WriteLine("========================================");
                    Console.WriteLine($"异常类型: {ex.GetType().Name}");
                    Console.WriteLine($"异常信息: {ex.Message}");
                }

                Console.WriteLine();
                Console.WriteLine("========================================");
                Console.WriteLine("测试完成");
                Console.WriteLine("========================================");
                return;
            }

            // 检查是否运行真实API测试（带参数）
            if (args.Length > 0 && args[0] == "--test-api")
            {
                // 自动读取Cookie并测试
                string seatKey = args.Length > 1 ? args[1] : "18,12";
                int libId = args.Length > 2 ? int.Parse(args[2]) : 430;

                Console.WriteLine("========================================");
                Console.WriteLine("🔍 测试PrereserveSeat API");
                Console.WriteLine("========================================");
                Console.WriteLine();

                try
                {
                    // 读取和解密Cookie
                    var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    var cookieFilePath = System.IO.Path.Combine(appDataPath, "IGoLibrary", "SavedCookie");

                    if (!System.IO.File.Exists(cookieFilePath))
                    {
                        Console.WriteLine($"❌ Cookie文件不存在: {cookieFilePath}");
                        return;
                    }

                    string encryptedCookie = System.IO.File.ReadAllText(cookieFilePath);
                    string cookie = IGoLibrary.Core.Utils.Decrypt.DES(encryptedCookie, "ejianzqq");

                    if (cookie == "解密失败")
                    {
                        Console.WriteLine("❌ Cookie解密失败");
                        return;
                    }

                    Console.WriteLine("✅ Cookie解密成功");
                    Console.WriteLine();

                    // 测试参数
                    Console.WriteLine($"📋 测试参数:");
                    Console.WriteLine($"   座位Key: {seatKey}");
                    Console.WriteLine($"   图书馆LibID: {libId}");
                    Console.WriteLine();

                    // 调用API
                    var service = new PrereserveSeatServiceImpl();

                    Console.WriteLine("🚀 正在调用PrereserveSeat API...");
                    Console.WriteLine($"⏰ 时间戳: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                    Console.WriteLine();

                    var startTime = DateTime.Now;
                    bool result = service.PrereserveSeat(cookie, seatKey, libId);
                    var duration = (DateTime.Now - startTime).TotalMilliseconds;

                    Console.WriteLine("========================================");
                    Console.WriteLine("✅ API调用成功");
                    Console.WriteLine("========================================");
                    Console.WriteLine($"返回结果: {result}");
                    Console.WriteLine($"耗时: {duration:F0}ms");
                    Console.WriteLine($"时间戳: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                    Console.WriteLine();

                    if (result)
                    {
                        Console.WriteLine("🎉 预约成功！");
                        Console.WriteLine("说明：服务器返回成功，座位在调用时是空闲的。");
                        Console.WriteLine();
                        Console.WriteLine("💡 这意味着：");
                        Console.WriteLine("   ➤ API返回了成功状态");
                        Console.WriteLine("   ➤ 座位在调用时是空闲的");
                        Console.WriteLine("   ➤ 系统以API返回信息判断成功/失败");
                    }
                }
                catch (ReserveSeatException ex)
                {
                    Console.WriteLine("========================================");
                    Console.WriteLine("❌ 预约失败 - 服务器返回错误");
                    Console.WriteLine("========================================");
                    Console.WriteLine($"错误信息: {ex.Message}");
                    Console.WriteLine($"时间戳: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                    Console.WriteLine();

                    if (ex.Message.Contains("座位有人") || ex.Message.Contains("已被预约") || ex.Message.Contains("已预约"))
                    {
                        Console.WriteLine("💡 这就是'座位有人'的错误！");
                        Console.WriteLine("   系统根据API返回的这个错误信息判断座位有人，");
                        Console.WriteLine("   而不是根据座位列表的Status字段判断。");
                        Console.WriteLine();
                        Console.WriteLine("📝 可能的原因：");
                        Console.WriteLine("   1. 其他人已经预约了这个座位");
                        Console.WriteLine("   2. 你的请求比别人慢了一步");
                        Console.WriteLine("   3. 建议添加多个备选座位提高成功率");
                    }
                    else if (ex.Message.Contains("已经预约") || ex.Message.Contains("已有预约"))
                    {
                        Console.WriteLine("💡 你已经有一个预约了！");
                        Console.WriteLine("   一个用户同时只能有一个预约。");
                        Console.WriteLine("   如果要测试新的预约，需要先取消当前预约。");
                    }
                    else
                    {
                        Console.WriteLine($"💡 其他错误: {ex.Message}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("========================================");
                    Console.WriteLine("❌ 发生异常");
                    Console.WriteLine("========================================");
                    Console.WriteLine($"异常类型: {ex.GetType().Name}");
                    Console.WriteLine($"异常信息: {ex.Message}");
                }

                Console.WriteLine();
                Console.WriteLine("========================================");
                Console.WriteLine("测试完成");
                Console.WriteLine("========================================");
                return;
            }

            Console.WriteLine("=".PadRight(80, '='));
            Console.WriteLine("IGoLibrary.Core 控制台测试程序");
            Console.WriteLine("=".PadRight(80, '='));
            Console.WriteLine();

            // 初始化模拟服务
            var notificationService = new MockNotificationService();
            var storageService = new MockStorageService();
            var sessionService = new MockSessionService();

            // 初始化业务服务
            var getCookieService = new GetCookieServiceImpl();
            var getLibInfoService = new GetLibInfoServiceImpl();
            var getAllLibsService = new GetAllLibsSummaryImpl();
            var reserveSeatService = new ReserveSeatServiceImpl();
            var getReserveInfoService = new GetReserveInfoServiceImpl();
            var cancelReserveService = new CancelReserveServiceImpl();

            Console.WriteLine("✓ 所有服务已初始化");
            Console.WriteLine();

            // 主菜单循环
            while (true)
            {
                Console.WriteLine("-".PadRight(80, '-'));
                Console.WriteLine("请选择测试功能:");
                Console.WriteLine("1. 测试获取 Cookie (从 code)");
                Console.WriteLine("2. 测试获取图书馆信息");
                Console.WriteLine("3. 测试获取所有图书馆列表");
                Console.WriteLine("4. 测试获取预约信息");
                Console.WriteLine("5. 测试预约座位");
                Console.WriteLine("6. 测试取消预约");
                Console.WriteLine("7. 保存 Cookie 到内存");
                Console.WriteLine("8. 从内存加载 Cookie");
                Console.WriteLine("9. 手动设置 Cookie");
                Console.WriteLine("10. 运行明日预约功能测试（时间模拟为晚上8点）");
                Console.WriteLine("11. 测试真实PrereserveSeat API（查看返回信息）");
                Console.WriteLine("0. 退出");
                Console.WriteLine("-".PadRight(80, '-'));
                Console.Write("请输入选项: ");

                var choice = Console.ReadLine();
                Console.WriteLine();

                try
                {
                    switch (choice)
                    {
                        case "1":
                            await TestGetCookie(getCookieService, sessionService);
                            break;

                        case "2":
                            await TestGetLibInfo(getLibInfoService, sessionService, notificationService);
                            break;

                        case "3":
                            await TestGetAllLibs(getAllLibsService, sessionService, notificationService);
                            break;

                        case "4":
                            await TestGetReserveInfo(getReserveInfoService, sessionService, notificationService);
                            break;

                        case "5":
                            await TestReserveSeat(reserveSeatService, sessionService, notificationService);
                            break;

                        case "6":
                            await TestCancelReserve(cancelReserveService, sessionService, notificationService);
                            break;

                        case "7":
                            await TestSaveCookie(storageService, sessionService);
                            break;

                        case "8":
                            await TestLoadCookie(storageService, sessionService);
                            break;

                        case "9":
                            TestSetCookie(sessionService);
                            break;

                        case "10":
                            await PrereserveTest.RunPrereserveTest();
                            break;

                        case "11":
                            RealApiTest.TestPrereserveApi();
                            break;

                        case "0":
                            Console.WriteLine("感谢使用，再见！");
                            return;

                        default:
                            Console.WriteLine("无效选项，请重新选择");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    notificationService.ShowError("测试失败", ex.Message);
                    Console.WriteLine($"异常详情: {ex.GetType().Name}");
                    if (ex.InnerException != null)
                    {
                        Console.WriteLine($"内部异常: {ex.InnerException.Message}");
                    }
                }

                Console.WriteLine();
            }
        }

        static async Task TestGetCookie(GetCookieServiceImpl service, MockSessionService session)
        {
            Console.WriteLine("【测试获取 Cookie】");
            Console.Write("请输入微信授权 code (从跳转链接中获取): ");
            var code = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(code))
            {
                Console.WriteLine("Code 不能为空");
                return;
            }

            try
            {
                var cookie = service.GetCookie(code);
                session.Cookie = cookie;
                Console.WriteLine($"✓ Cookie 获取成功!");
                Console.WriteLine($"Cookie 内容: {cookie}");
            }
            catch (GetCookieException ex)
            {
                Console.WriteLine($"✗ 获取 Cookie 失败: {ex.Message}");
            }
        }

        static async Task TestGetLibInfo(GetLibInfoServiceImpl service, MockSessionService session, MockNotificationService notification)
        {
            Console.WriteLine("【测试获取图书馆信息】");

            if (string.IsNullOrWhiteSpace(session.Cookie))
            {
                notification.ShowError("测试失败", "请先设置 Cookie (选项 1 或 9)");
                return;
            }

            Console.Write("请输入 GraphQL 查询语句 (或按回车使用默认): ");
            var query = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(query))
            {
                // 默认查询语句示例
                query = @"{""operationName"":""index"",""query"":""query index($libId: Int, $libType: Int) {\n  userAuth {\n    reserve {\n      libs(libType: $libType, libId: $libId) {\n        lib_id\n        lib_name\n        lib_floor\n        is_open\n        lib_layout {\n          seats_total\n          seats_booking\n          seats_used\n          seats {\n            key\n            name\n            status\n          }\n        }\n      }\n    }\n  }\n}\n"",""variables"":{""libId"":1,""libType"":1}}";
                Console.WriteLine("使用默认查询语句");
            }

            try
            {
                var library = service.GetLibInfo(session.Cookie, query);

                if (library != null)
                {
                    notification.ShowSuccess("获取成功", $"图书馆: {library.Name}");
                    Console.WriteLine($"图书馆名称: {library.Name}");
                    Console.WriteLine($"图书馆 ID: {library.LibID}");
                    Console.WriteLine($"楼层: {library.Floor}");
                    Console.WriteLine($"是否开放: {(library.IsOpen ? "是" : "否")}");
                    Console.WriteLine($"总座位数: {library.SeatsInfo.TotalSeats}");
                    Console.WriteLine($"已预约: {library.SeatsInfo.BookedSeats}");
                    Console.WriteLine($"使用中: {library.SeatsInfo.UsedSeats}");
                    Console.WriteLine($"可用座位: {library.SeatsInfo.AvailableSeats}");
                    Console.WriteLine($"座位列表数量: {library.Seats?.Count ?? 0}");

                    if (library.Seats != null && library.Seats.Count > 0)
                    {
                        Console.WriteLine("\n前 10 个座位:");
                        foreach (var seat in library.Seats.Take(10))
                        {
                            Console.WriteLine($"  - {seat.name}号: {(seat.status ? "有人" : "无人")}");
                        }
                    }

                    session.CurrentLibrary = library;
                }
            }
            catch (GetLibInfoException ex)
            {
                notification.ShowError("获取失败", ex.Message);
            }
        }

        static async Task TestGetAllLibs(GetAllLibsSummaryImpl service, MockSessionService session, MockNotificationService notification)
        {
            Console.WriteLine("【测试获取所有图书馆列表】");

            if (string.IsNullOrWhiteSpace(session.Cookie))
            {
                notification.ShowError("测试失败", "请先设置 Cookie");
                return;
            }

            Console.Write("请输入 GraphQL 查询语句 (或按回车使用默认): ");
            var query = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(query))
            {
                query = @"{""operationName"":""libs"",""query"":""query libs {\n  userAuth {\n    reserve {\n      libs {\n        lib_id\n        lib_name\n        lib_floor\n        is_open\n      }\n    }\n  }\n}\n"",""variables"":{}}";
                Console.WriteLine("使用默认查询语句");
            }

            try
            {
                var summary = service.GetAllLibsSummary(session.Cookie, query);

                notification.ShowSuccess("获取成功", $"共 {summary.libSummaries.Count} 个图书馆");

                foreach (var lib in summary.libSummaries)
                {
                    Console.WriteLine($"  - [{lib.LibID}] {lib.Name} - {lib.Floor}楼 - {(lib.IsOpen ? "开放" : "关闭")}");
                }
            }
            catch (GetAllLibsSummaryException ex)
            {
                notification.ShowError("获取失败", ex.Message);
            }
        }

        static async Task TestGetReserveInfo(GetReserveInfoServiceImpl service, MockSessionService session, MockNotificationService notification)
        {
            Console.WriteLine("【测试获取预约信息】");

            if (string.IsNullOrWhiteSpace(session.Cookie))
            {
                notification.ShowError("测试失败", "请先设置 Cookie");
                return;
            }

            Console.Write("请输入 GraphQL 查询语句 (或按回车使用默认): ");
            var query = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(query))
            {
                query = @"{""operationName"":""reserveInfo"",""query"":""query reserveInfo {\n  userAuth {\n    reserve {\n      reserve {\n        lib_name\n        seat_name\n        seat_key\n        exp_date\n      }\n      getSToken\n    }\n  }\n}\n"",""variables"":{}}";
                Console.WriteLine("使用默认查询语句");
            }

            try
            {
                var reserveInfo = service.GetReserveInfo(session.Cookie, query);

                notification.ShowSuccess("获取成功", $"图书馆: {reserveInfo.LibName}");
                Console.WriteLine($"图书馆: {reserveInfo.LibName}");
                Console.WriteLine($"座位: {reserveInfo.SeatKeyDta.Name}号");
                Console.WriteLine($"过期时间戳: {reserveInfo.ExpiredTimeStamp}");
                Console.WriteLine($"Token: {reserveInfo.Token}");
            }
            catch (GetReserveInfoException ex)
            {
                notification.ShowError("获取失败", ex.Message);
            }
        }

        static async Task TestReserveSeat(ReserveSeatServiceImpl service, MockSessionService session, MockNotificationService notification)
        {
            Console.WriteLine("【测试预约座位】");

            if (string.IsNullOrWhiteSpace(session.Cookie))
            {
                notification.ShowError("测试失败", "请先设置 Cookie");
                return;
            }

            Console.Write("请输入座位 Key: ");
            var seatKey = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(seatKey))
            {
                Console.WriteLine("座位 Key 不能为空");
                return;
            }

            var query = @"{""operationName"":""reserve"",""query"":""mutation reserve($key: String!) {\n  userAuth {\n    reserve {\n      reserueSeat(key: $key)\n    }\n  }\n}\n"",""variables"":{""key"":""" + seatKey + @"""}}";

            try
            {
                var success = service.ReserveSeat(session.Cookie, query);

                if (success)
                {
                    notification.ShowSuccess("预约成功", "座位已预约");
                }
                else
                {
                    notification.ShowWarning("预约失败", "未知原因");
                }
            }
            catch (ReserveSeatException ex)
            {
                notification.ShowError("预约失败", ex.Message);
            }
        }

        static async Task TestCancelReserve(CancelReserveServiceImpl service, MockSessionService session, MockNotificationService notification)
        {
            Console.WriteLine("【测试取消预约】");

            if (string.IsNullOrWhiteSpace(session.Cookie))
            {
                notification.ShowError("测试失败", "请先设置 Cookie");
                return;
            }

            var query = @"{""operationName"":""cancelReserve"",""query"":""mutation cancelReserve {\n  userAuth {\n    reserve {\n      cancelReserve\n    }\n  }\n}\n"",""variables"":{}}";

            try
            {
                string retMessage = "";
                var success = service.CancelReserve(session.Cookie, query, ref retMessage);

                if (success)
                {
                    notification.ShowSuccess("取消成功", "预约已取消");
                }
                else
                {
                    notification.ShowWarning("取消失败", retMessage);
                }
            }
            catch (CancelReserveException ex)
            {
                notification.ShowError("取消失败", ex.Message);
            }
        }

        static async Task TestSaveCookie(MockStorageService storage, MockSessionService session)
        {
            Console.WriteLine("【测试保存 Cookie】");

            if (string.IsNullOrWhiteSpace(session.Cookie))
            {
                Console.WriteLine("当前会话中无 Cookie，无法保存");
                return;
            }

            await storage.SaveCookieAsync(session.Cookie);
            Console.WriteLine("✓ Cookie 已保存到内存");
        }

        static async Task TestLoadCookie(MockStorageService storage, MockSessionService session)
        {
            Console.WriteLine("【测试加载 Cookie】");

            var cookie = await storage.LoadCookieAsync();

            if (cookie != null)
            {
                session.Cookie = cookie;
                Console.WriteLine("✓ Cookie 已从内存加载到会话");
            }
            else
            {
                Console.WriteLine("内存中无 Cookie");
            }
        }

        static void TestSetCookie(MockSessionService session)
        {
            Console.WriteLine("【手动设置 Cookie】");
            Console.WriteLine("请输入完整的 Cookie 字符串:");
            Console.WriteLine("格式示例: wechat_login_token=xxx; wechat_login_user_id=yyy");
            Console.Write("Cookie: ");

            var cookie = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(cookie))
            {
                Console.WriteLine("Cookie 不能为空");
                return;
            }

            session.Cookie = cookie;
            Console.WriteLine("✓ Cookie 已设置到会话");
        }
    }
}
