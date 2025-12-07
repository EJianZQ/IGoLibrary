using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

class TestCancelRebook
{
    static async Task Main(string[] args)
    {
        // Your cookie
        string cookie = "Authorization=eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiJ9.eyJ1c2VySWQiOjQ0NzQ3Njg0LCJzY2hJZCI6MjIsImV4cGlyZUF0IjoxNzY1MTE1NDM2LCJ0YWciOiJjb29raWUtNjZlMiJ9.GZY10rQfMyzLGdr2D5MT6YExqdHTb5DpKCLkD7BEmPS17NSWfA7NMSxyjJiDalXajW-4EM8cuMl2iHP8EE9BFo5E0xl7-hDKG-Y7bFwK7d3ggok_d2qFDk46tPOaTbGAVrO8fyTFQ_LUCgueDeI6J-tDTfljzCGOrO9619M1FdwOWMSnIMIQvfGI5ukWLuEH4p6ZjutVkQSdw_1OnKIrsj1Zv0VEafgYl8DcyyFC04mj4TaXZPOrEOBXo4dl0hYh6ksRVlIygyPKsTil-EmS45280AWqX7O-6pNT6leHR-0EUT1bY6u4vhNkxLu8sHFyKxwTfnJ7bLj66DR_LVgorQ; SERVERID=b9fc7bd86d2eed91b23d7347e0ee995e|1765108236|1765108236";

        string apiUrl = "https://wechat.v2.traceint.com/index.php/graphql/";

        Console.WriteLine("========================================");
        Console.WriteLine("测试取消并重新预约座位");
        Console.WriteLine("========================================");
        Console.WriteLine();

        try
        {
            // Step 1: Get current reservation info
            Console.WriteLine("步骤 1: 获取当前预约信息...");
            string queryReserveInfo = @"{""operationName"":""index"",""query"":""query index($pos: String!, $param: [hash]) {\n userAuth {\n reserve {\n reserve {\n token\n status\n lib_id\n lib_name\n seat_key\n seat_name\n }\n getSToken\n }\n }\n}"",""variables"":{""pos"":""App-首页""}}";

            var reserveInfoResponse = await SendGraphQLRequest(apiUrl, cookie, queryReserveInfo);
            Console.WriteLine($"预约信息响应: {reserveInfoResponse}");
            Console.WriteLine();

            var reserveInfoJson = JObject.Parse(reserveInfoResponse);

            // Check for errors
            if (reserveInfoJson["errors"] != null)
            {
                Console.WriteLine($"❌ 获取预约信息失败: {reserveInfoJson["errors"][0]["msg"]}");
                return;
            }

            var reserveData = reserveInfoJson["data"]["userAuth"]["reserve"]["reserve"];
            if (reserveData == null || reserveData.Type == JTokenType.Null)
            {
                Console.WriteLine("❌ 当前没有预约座位");
                return;
            }

            string token = reserveData["token"]?.ToString();
            string seatKey = reserveData["seat_key"]?.ToString();
            string seatName = reserveData["seat_name"]?.ToString();
            int libId = reserveData["lib_id"]?.ToObject<int>() ?? 0;
            string libName = reserveData["lib_name"]?.ToString();

            Console.WriteLine($"✅ 当前预约信息:");
            Console.WriteLine($"   图书馆: {libName} (ID: {libId})");
            Console.WriteLine($"   座位: {seatName} (Key: {seatKey})");
            Console.WriteLine($"   Token: {token}");
            Console.WriteLine();

            // Step 2: Cancel reservation
            Console.WriteLine("步骤 2: 取消当前预约...");
            string cancelQuery = $@"{{""operationName"":""reserveCancle"",""query"":""mutation reserveCancle($sToken: String!) {{\n userAuth {{\n reserve {{\n reserveCancle(sToken: $sToken) {{\n timerange\n img\n hours\n mins\n per\n }}\n }}\n }}\n}}"",""variables"":{{""sToken"":""{token}""}}}}";

            var cancelResponse = await SendGraphQLRequest(apiUrl, cookie, cancelQuery);
            Console.WriteLine($"取消预约响应: {cancelResponse}");
            Console.WriteLine();

            var cancelJson = JObject.Parse(cancelResponse);
            if (cancelJson["errors"] != null)
            {
                string errorMsg = cancelJson["errors"][0]["msg"]?.ToString();
                if (errorMsg?.Contains("成功") == true)
                {
                    Console.WriteLine($"✅ 取消预约成功: {errorMsg}");
                }
                else
                {
                    Console.WriteLine($"❌ 取消预约失败: {errorMsg}");
                    return;
                }
            }
            else
            {
                Console.WriteLine("⚠️ 取消预约响应格式异常");
            }
            Console.WriteLine();

            // Wait a bit before re-booking
            Console.WriteLine("等待 2 秒后重新预约...");
            await Task.Delay(2000);
            Console.WriteLine();

            // Step 3: Re-book the seat
            Console.WriteLine("步骤 3: 重新预约座位...");
            string reserveQuery = $@"{{""operationName"":""reserueSeat"",""query"":""mutation reserueSeat($libId: Int!, $seatKey: String!, $captchaCode: String, $captcha: String!) {{\n userAuth {{\n reserve {{\n reserueSeat(\n libId: $libId\n seatKey: $seatKey\n captchaCode: $captchaCode\n captcha: $captcha\n )\n }}\n }}\n}}"",""variables"":{{""seatKey"":""{seatKey}"",""libId"":{libId},""captchaCode"":"""",""captcha"":""""}}}}";

            var reserveResponse = await SendGraphQLRequest(apiUrl, cookie, reserveQuery);
            Console.WriteLine($"预约响应: {reserveResponse}");
            Console.WriteLine();

            var reserveJson = JObject.Parse(reserveResponse);
            if (reserveJson["errors"] != null)
            {
                Console.WriteLine($"❌ 预约失败: {reserveJson["errors"][0]["msg"]}");
                return;
            }

            var reserveResult = reserveJson["data"]?["userAuth"]?["reserve"]?["reserueSeat"]?.ToString();
            if (reserveResult == "true")
            {
                Console.WriteLine($"✅ 预约成功！");
                Console.WriteLine($"   已重新预约座位: {seatName}");
            }
            else
            {
                Console.WriteLine($"❌ 预约失败，返回值: {reserveResult}");
            }

            Console.WriteLine();
            Console.WriteLine("========================================");
            Console.WriteLine("测试完成");
            Console.WriteLine("========================================");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ 发生异常: {ex.Message}");
            Console.WriteLine($"堆栈跟踪: {ex.StackTrace}");
        }
    }

    static async Task<string> SendGraphQLRequest(string url, string cookie, string jsonBody)
    {
        using (var client = new HttpClient())
        {
            client.Timeout = TimeSpan.FromSeconds(10);

            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("Cookie", cookie);
            request.Headers.Add("Origin", "https://web.traceint.com");
            request.Headers.Add("Referer", "https://web.traceint.com/web/index.html");
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 6.1; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/81.0.4044.138 Safari/537.36 NetType/WIFI MicroMessenger/7.0.20.1781(0x6700143B) WindowsWechat(0x63070626)");
            request.Headers.Add("App-Version", "2.0.11");
            request.Headers.Add("Accept", "*/*");
            request.Headers.Add("Accept-Language", "zh-CN,zh;q=0.9,en-US;q=0.8,en;q=0.7");

            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            var response = await client.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            return content;
        }
    }
}
