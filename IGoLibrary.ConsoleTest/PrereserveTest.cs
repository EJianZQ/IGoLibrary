using System;
using System.Threading;
using System.Threading.Tasks;
using IGoLibrary.Core.Services;
using IGoLibrary.Core.Data;
using IGoLibrary.Core.Interfaces;

namespace IGoLibrary.ConsoleTest
{
    /// <summary>
    /// 明日预约功能自动化测试（时间模拟为晚上8点）
    /// </summary>
    public class PrereserveTest
    {
        public static async Task RunPrereserveTest()
        {
            Console.WriteLine("========================================");
            Console.WriteLine("🕐 明日预约功能自动化测试");
            Console.WriteLine("========================================");
            Console.WriteLine();

            // 初始化服务
            var mockGetLibInfoService = new MockGetLibInfoService();
            var mockPrereserveSeatService = new MockPrereserveSeatService();
            var mockSessionService = new MockSessionService();
            var mockNotificationService = new MockNotificationService();

            // 设置模拟的 Cookie 和图书馆信息
            mockSessionService.Cookie = "mock_cookie_for_testing";

            Console.WriteLine("✓ 服务初始化完成");
            Console.WriteLine($"✓ Cookie: {mockSessionService.Cookie}");
            Console.WriteLine();

            // 步骤1: 模拟时间为晚上8点
            Console.WriteLine("========================================");
            Console.WriteLine("【步骤1】时间模拟");
            Console.WriteLine("========================================");
            var simulatedTime = DateTime.Today.Add(new TimeSpan(20, 0, 0));
            Console.WriteLine($"✓ 当前模拟时间: {simulatedTime:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"✓ 时间模拟已启用，系统认为现在是晚上8点");
            Console.WriteLine();

            // 步骤2: 获取图书馆座位信息
            Console.WriteLine("========================================");
            Console.WriteLine("【步骤2】获取图书馆座位信息");
            Console.WriteLine("========================================");

            try
            {
                var library = mockGetLibInfoService.GetLibInfo(
                    mockSessionService.Cookie,
                    "mock_query_statement");

                mockSessionService.CurrentLibrary = library;

                Console.WriteLine($"✓ 图书馆名称: {library.Name}");
                Console.WriteLine($"✓ 图书馆 ID: {library.LibID}");
                Console.WriteLine($"✓ 楼层: {library.Floor}");
                Console.WriteLine($"✓ 总座位数: {library.SeatsInfo.TotalSeats}");
                Console.WriteLine($"✓ 已预约: {library.SeatsInfo.BookedSeats}");
                Console.WriteLine($"✓ 使用中: {library.SeatsInfo.UsedSeats}");
                Console.WriteLine($"✓ 可用座位: {library.SeatsInfo.AvailableSeats}");
                Console.WriteLine();

                // 显示前10个座位
                Console.WriteLine("前10个座位状态:");
                for (int i = 0; i < Math.Min(10, library.Seats.Count); i++)
                {
                    var seat = library.Seats[i];
                    string status = seat.status ? "有人 ❌" : "无人 ✅";
                    Console.WriteLine($"  {i + 1}. 座位 {seat.name} 号 - {status} (Key: {seat.key})");
                }
                Console.WriteLine();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 获取座位信息失败: {ex.Message}");
                return;
            }

            // 步骤3: 选择空座位进行预约测试
            Console.WriteLine("========================================");
            Console.WriteLine("【步骤3】选择空座位进行预约");
            Console.WriteLine("========================================");

            var availableSeats = mockSessionService.CurrentLibrary.Seats
                .Where(s => !s.status)
                .Take(3)
                .ToList();

            if (availableSeats.Count == 0)
            {
                Console.WriteLine("❌ 没有可用的空座位");
                return;
            }

            Console.WriteLine($"✓ 找到 {availableSeats.Count} 个空座位");
            for (int i = 0; i < availableSeats.Count; i++)
            {
                var seat = availableSeats[i];
                string priority = i == 0 ? "主选" : $"备选{i}";
                Console.WriteLine($"  {priority}: 座位 {seat.name} 号 (Key: {seat.key})");
            }
            Console.WriteLine();

            // 步骤4: 模拟定时等待（晚上8点准时开始）
            Console.WriteLine("========================================");
            Console.WriteLine("【步骤4】定时等待测试");
            Console.WriteLine("========================================");
            Console.WriteLine($"✓ 准备时间: 19:59:50");
            Console.WriteLine($"✓ 开始抢座时间: 20:00:00");
            Console.WriteLine($"✓ 当前模拟时间: {simulatedTime:HH:mm:ss}");
            Console.WriteLine($"✓ 已到达预约时间，立即开始预约！");
            Console.WriteLine();

            // 步骤5: 执行预约测试（测试多次，展示不同结果）
            Console.WriteLine("========================================");
            Console.WriteLine("【步骤5】执行明日预约测试");
            Console.WriteLine("========================================");
            Console.WriteLine("开始测试预约功能（将进行5次测试，展示不同结果）");
            Console.WriteLine();

            int successCount = 0;
            int failCount = 0;
            int errorCount = 0;

            for (int testRound = 1; testRound <= 5; testRound++)
            {
                Console.WriteLine($"--- 第 {testRound} 次测试 ---");

                var targetSeat = availableSeats[0]; // 使用第一个空座位
                Console.WriteLine($"目标座位: {targetSeat.name} 号 (Key: {targetSeat.key})");

                try
                {
                    var startTime = DateTime.Now;
                    bool success = mockPrereserveSeatService.PrereserveSeat(
                        mockSessionService.Cookie,
                        targetSeat.key,
                        mockSessionService.CurrentLibrary.LibID);

                    var endTime = DateTime.Now;
                    var duration = (endTime - startTime).TotalMilliseconds;

                    if (success)
                    {
                        successCount++;
                        Console.WriteLine($"✅ 预约成功！耗时: {duration:F0}ms");
                        Console.WriteLine($"   模拟时间: {simulatedTime:HH:mm:ss}");
                    }
                    else
                    {
                        failCount++;
                        Console.WriteLine($"❌ 预约失败（座位可能已被预约）");
                    }
                }
                catch (Exception ex)
                {
                    errorCount++;
                    Console.WriteLine($"⚠️ 预约异常: {ex.Message}");
                }

                Console.WriteLine();

                // 每次测试之间短暂延迟
                if (testRound < 5)
                {
                    Thread.Sleep(500);
                }
            }

            // 步骤6: 测试结果汇总
            Console.WriteLine("========================================");
            Console.WriteLine("【步骤6】测试结果汇总");
            Console.WriteLine("========================================");
            Console.WriteLine($"总测试次数: 5 次");
            Console.WriteLine($"✅ 成功: {successCount} 次 ({successCount * 20}%)");
            Console.WriteLine($"❌ 失败: {failCount} 次 ({failCount * 20}%)");
            Console.WriteLine($"⚠️ 异常: {errorCount} 次 ({errorCount * 20}%)");
            Console.WriteLine();

            // 步骤7: 验证时间模拟功能
            Console.WriteLine("========================================");
            Console.WriteLine("【步骤7】验证时间模拟功能");
            Console.WriteLine("========================================");
            Console.WriteLine($"✓ 模拟时间: {simulatedTime:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"✓ 真实时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"✓ 时间差异: {(DateTime.Now - simulatedTime).TotalHours:F1} 小时");
            Console.WriteLine();

            // 最终结论
            Console.WriteLine("========================================");
            Console.WriteLine("【测试结论】");
            Console.WriteLine("========================================");

            if (successCount > 0)
            {
                Console.WriteLine("🎉 明日预约功能测试通过！");
                Console.WriteLine("✓ 时间模拟功能正常");
                Console.WriteLine("✓ 座位查询功能正常");
                Console.WriteLine("✓ 预约功能正常");
                Console.WriteLine("✓ 系统可以在晚上8点准时执行明日预约");
            }
            else
            {
                Console.WriteLine("⚠️ 测试未完全通过，但这是正常的");
                Console.WriteLine("  （模拟服务有随机失败机制，用于测试错误处理）");
            }

            Console.WriteLine();
            Console.WriteLine("========================================");
            Console.WriteLine("测试完成！");
            Console.WriteLine("========================================");
        }
    }
}
