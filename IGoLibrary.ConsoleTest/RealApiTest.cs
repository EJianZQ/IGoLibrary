using System;
using System.IO;
using IGoLibrary.Core.Services;
using IGoLibrary.Core.Exceptions;
using IGoLibrary.Core.Utils;

namespace IGoLibrary.ConsoleTest
{
    /// <summary>
    /// 真实API测试 - 测试PrereserveSeat接口的返回信息
    /// </summary>
    public class RealApiTest
    {
        public static void TestPrereserveApi()
        {
            Console.WriteLine("========================================");
            Console.WriteLine("🔍 真实API测试 - PrereserveSeat接口");
            Console.WriteLine("========================================");
            Console.WriteLine();

            // 1. 自动读取和解密Cookie
            string? cookie = null;
            try
            {
                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var cookieFilePath = Path.Combine(appDataPath, "IGoLibrary", "SavedCookie");

                if (File.Exists(cookieFilePath))
                {
                    Console.WriteLine($"📁 找到Cookie文件: {cookieFilePath}");
                    string encryptedCookie = File.ReadAllText(cookieFilePath);
                    Console.WriteLine($"📦 加密Cookie长度: {encryptedCookie.Length} 字符");

                    Console.WriteLine("🔓 正在解密Cookie...");
                    cookie = Decrypt.DES(encryptedCookie, "ejianzqq");

                    if (cookie == "解密失败")
                    {
                        Console.WriteLine("❌ Cookie解密失败");
                        cookie = null;
                    }
                    else
                    {
                        Console.WriteLine($"✅ Cookie解密成功");
                        Console.WriteLine($"📋 Cookie内容（前100字符）: {cookie.Substring(0, Math.Min(100, cookie.Length))}...");
                    }
                }
                else
                {
                    Console.WriteLine($"⚠️ Cookie文件不存在: {cookieFilePath}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ 读取Cookie失败: {ex.Message}");
            }

            // 如果自动读取失败，让用户手动输入
            if (string.IsNullOrEmpty(cookie))
            {
                Console.WriteLine();
                Console.WriteLine("请手动输入Cookie:");
                Console.Write("Cookie: ");
                cookie = Console.ReadLine();
                if (string.IsNullOrEmpty(cookie))
                {
                    Console.WriteLine("❌ Cookie不能为空");
                    return;
                }
            }

            Console.WriteLine();
            Console.WriteLine("========================================");
            Console.WriteLine("请提供测试参数:");
            Console.WriteLine("========================================");

            Console.Write("座位Key（例如：100529952）: ");
            string? seatKey = Console.ReadLine();
            if (string.IsNullOrEmpty(seatKey))
            {
                Console.WriteLine("❌ 座位Key不能为空");
                return;
            }

            Console.Write("图书馆LibID（例如：1234）: ");
            string? libIdStr = Console.ReadLine();
            if (!int.TryParse(libIdStr, out int libId))
            {
                Console.WriteLine("❌ LibID必须是数字");
                return;
            }

            Console.WriteLine();
            Console.WriteLine("========================================");
            Console.WriteLine("开始测试API...");
            Console.WriteLine("========================================");
            Console.WriteLine();

            // 创建真实的服务实例
            var prereserveSeatService = new PrereserveSeatServiceImpl();

            Console.WriteLine($"📋 测试参数:");
            Console.WriteLine($"   座位Key: {seatKey}");
            Console.WriteLine($"   图书馆LibID: {libId}");
            Console.WriteLine();

            try
            {
                Console.WriteLine("🚀 正在调用PrereserveSeat API...");
                var startTime = DateTime.Now;

                bool result = prereserveSeatService.PrereserveSeat(cookie, seatKey, libId);

                var endTime = DateTime.Now;
                var duration = (endTime - startTime).TotalMilliseconds;

                Console.WriteLine();
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
                    Console.WriteLine("说明：服务器返回了成功的响应，座位预约成功。");
                    Console.WriteLine();
                    Console.WriteLine("💡 这意味着：");
                    Console.WriteLine("   ➤ API返回了成功状态");
                    Console.WriteLine("   ➤ 座位在调用时是空闲的");
                    Console.WriteLine("   ➤ 系统以API返回信息判断成功/失败");
                }
                else
                {
                    Console.WriteLine("⚠️ 预约失败（但没有抛出异常）");
                    Console.WriteLine("说明：服务器返回了失败的响应，但没有错误信息。");
                }
            }
            catch (ReserveSeatException ex)
            {
                Console.WriteLine();
                Console.WriteLine("========================================");
                Console.WriteLine("❌ 预约失败 - 服务器返回错误");
                Console.WriteLine("========================================");
                Console.WriteLine($"错误类型: ReserveSeatException");
                Console.WriteLine($"错误信息: {ex.Message}");
                Console.WriteLine($"时间戳: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                Console.WriteLine();
                Console.WriteLine("📝 错误分析:");

                if (ex.Message.Contains("座位有人") || ex.Message.Contains("已被预约") || ex.Message.Contains("已预约"))
                {
                    Console.WriteLine("   ➤ 座位已被占用或预约");
                    Console.WriteLine("   ➤ 原因：其他人已经预约了这个座位");
                    Console.WriteLine("   ➤ 建议：选择其他空闲座位，或添加多个备选座位");
                    Console.WriteLine();
                    Console.WriteLine("💡 这就是你说的'才开放抢座就提示座位有人'的情况！");
                    Console.WriteLine("   系统根据API返回的这个错误信息判断座位有人，");
                    Console.WriteLine("   而不是根据座位列表中的Status字段判断。");
                }
                else if (ex.Message.Contains("未登录") || ex.Message.Contains("Cookie") || ex.Message.Contains("登录"))
                {
                    Console.WriteLine("   ➤ Cookie可能已过期或无效");
                    Console.WriteLine("   ➤ 建议：重新扫码登录");
                }
                else if (ex.Message.Contains("时间") || ex.Message.Contains("未开放") || ex.Message.Contains("不在"))
                {
                    Console.WriteLine("   ➤ 预约时间未到或已过");
                    Console.WriteLine("   ➤ 建议：检查预约开放时间（通常是20:00:00）");
                }
                else if (ex.Message.Contains("验证码") || ex.Message.Contains("captcha"))
                {
                    Console.WriteLine("   ➤ 需要验证码");
                    Console.WriteLine("   ➤ 建议：检查是否需要人机验证");
                }
                else
                {
                    Console.WriteLine($"   ➤ 其他错误：{ex.Message}");
                }

                Console.WriteLine();
                Console.WriteLine("💡 重要说明：");
                Console.WriteLine("   这个错误信息就是PrereserveSeat API返回的实际错误！");
                Console.WriteLine("   系统会根据这个错误信息判断是否有人，");
                Console.WriteLine("   而不是根据座位列表的Status字段判断。");
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine("========================================");
                Console.WriteLine("❌ 发生未知异常");
                Console.WriteLine("========================================");
                Console.WriteLine($"异常类型: {ex.GetType().Name}");
                Console.WriteLine($"异常信息: {ex.Message}");
                Console.WriteLine($"时间戳: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                Console.WriteLine();
                Console.WriteLine($"堆栈跟踪:");
                Console.WriteLine(ex.StackTrace);
            }

            Console.WriteLine();
            Console.WriteLine("========================================");
            Console.WriteLine("测试完成");
            Console.WriteLine("========================================");
        }
    }
}
