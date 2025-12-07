using System;
using System.IO;
using IGoLibrary.Core.Services;
using IGoLibrary.Core.Utils;
using IGoLibrary.Core.Exceptions;

class TestPrereserveApi
{
    static void Main(string[] args)
    {
        Console.WriteLine("========================================");
        Console.WriteLine("🔍 测试PrereserveSeat API返回信息");
        Console.WriteLine("========================================");
        Console.WriteLine();

        // 1. 读取加密的Cookie
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var cookieFilePath = Path.Combine(appDataPath, "IGoLibrary", "SavedCookie");

        if (!File.Exists(cookieFilePath))
        {
            Console.WriteLine($"❌ Cookie文件不存在: {cookieFilePath}");
            return;
        }

        Console.WriteLine($"📁 Cookie文件路径: {cookieFilePath}");

        string encryptedCookie = File.ReadAllText(cookieFilePath);
        Console.WriteLine($"📦 加密Cookie长度: {encryptedCookie.Length} 字符");
        Console.WriteLine();

        // 2. 解密Cookie
        Console.WriteLine("🔓 正在解密Cookie...");
        string cookie = Decrypt.DES(encryptedCookie, "ejianzqq");

        if (cookie == "解密失败")
        {
            Console.WriteLine("❌ Cookie解密失败");
            return;
        }

        Console.WriteLine($"✅ Cookie解密成功");
        Console.WriteLine($"📋 Cookie内容（前100字符）: {cookie.Substring(0, Math.Min(100, cookie.Length))}...");
        Console.WriteLine();

        // 3. 获取测试参数
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

        // 4. 调用PrereserveSeat API
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
            Console.WriteLine("💡 这个错误信息就是PrereserveSeat API返回的实际错误！");
            Console.WriteLine("   系统会根据这个错误信息判断是否有人，而不是根据座位列表的状态。");
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
