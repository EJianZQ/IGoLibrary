#!/bin/bash

# 快速测试PrereserveSeat API
# 座位：340号（Key: 18,12）
# 图书馆：430

cd /Users/apple/PycharmProjects/IGoLibrary

# 创建临时测试程序
cat > /tmp/test_api.cs << 'EOF'
using System;
using System.IO;
using IGoLibrary.Core.Services;
using IGoLibrary.Core.Utils;
using IGoLibrary.Core.Exceptions;

class Program
{
    static void Main()
    {
        Console.WriteLine("========================================");
        Console.WriteLine("🔍 测试PrereserveSeat API");
        Console.WriteLine("========================================");
        Console.WriteLine();

        // 1. 读取和解密Cookie
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var cookieFilePath = Path.Combine(appDataPath, "IGoLibrary", "SavedCookie");

        string encryptedCookie = File.ReadAllText(cookieFilePath);
        string cookie = Decrypt.DES(encryptedCookie, "ejianzqq");

        Console.WriteLine("✅ Cookie解密成功");
        Console.WriteLine();

        // 2. 测试参数
        string seatKey = "18,12";  // 座位340的Key
        int libId = 430;           // 图书馆ID

        Console.WriteLine($"📋 测试参数:");
        Console.WriteLine($"   座位：340号");
        Console.WriteLine($"   座位Key: {seatKey}");
        Console.WriteLine($"   图书馆LibID: {libId}");
        Console.WriteLine();

        // 3. 调用API
        var service = new PrereserveSeatServiceImpl();

        try
        {
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
            }
            else if (ex.Message.Contains("已经预约"))
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
            Console.WriteLine($"异常: {ex.Message}");
        }

        Console.WriteLine();
        Console.WriteLine("========================================");
        Console.WriteLine("测试完成");
        Console.WriteLine("========================================");
    }
}
EOF

# 编译并运行
echo "正在编译测试程序..."
/usr/local/share/dotnet/dotnet build IGoLibrary.Core/IGoLibrary.Core.csproj -o /tmp/test_output > /dev/null 2>&1

echo "正在运行测试..."
echo ""

cd /tmp
/usr/local/share/dotnet/dotnet exec /usr/local/share/dotnet/csc.dll \
    -r:/tmp/test_output/IGoLibrary.Core.dll \
    -r:/tmp/test_output/Newtonsoft.Json.dll \
    -r:/tmp/test_output/RestSharp.dll \
    test_api.cs -out:test_api.exe 2>&1 | grep -v "warning"

if [ -f test_api.exe ]; then
    /usr/local/share/dotnet/dotnet test_api.exe
else
    echo "编译失败，使用备用方法..."
fi
