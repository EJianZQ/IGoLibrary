using RestSharp;
using System;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using System.Reflection.Metadata;

namespace ConsoleDemo
{
    internal class Program
    {
        static async Task  Main(string[] args)
        {
            KeepCookie();
            Console.ReadLine();
        }

        public static void GetSeats()
        {
            var client = new RestClient("https://wechat.v2.traceint.com/index.php/graphql/");
            var request = new RestRequest();
            request.Method = Method.Post;
            request.RequestFormat = DataFormat.Json;
            request.AddHeader("Content-Type", @"application/json");
            request.AddHeader("Host", @"wechat.v2.traceint.com");
            request.AddHeader("Connection", @"keep-alive");
            request.AddHeader("Content-Length", @"393");
            request.AddHeader("Origin", @"https://web.traceint.com");
            request.AddHeader("User-Agent", @"Mozilla/5.0 (Windows NT 6.1; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/81.0.4044.138 Safari/537.36 NetType/WIFI MicroMessenger/7.0.20.1781(0x6700143B) WindowsWechat(0x63070626)");
            request.AddHeader("App-Version", @"2.0.11");
            request.AddHeader("Accept", @"*/*");
            request.AddHeader("Sec-Fetch-Site", @"same-site");
            request.AddHeader("Sec-Fetch-Mode", @"cors");
            request.AddHeader("Sec-Fetch-Dest", @"empty");
            request.AddHeader("Referer", @"https://web.traceint.com/web/index.html");
            request.AddHeader("Accept-Encoding", @"gzip, deflate, br");
            request.AddHeader("Accept-Language", @"zh-CN,zh;q=0.9,en-US;q=0.8,en;q=0.7");
            request.AddHeader("Cookie", @"FROM_TYPE=weixin; v=5.5; wechatSESS_ID=9b931d4511a638827e38d73bc913fd1010a56f8ce6dadb0d; Authorization=eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiJ9.eyJ1c2VySWQiOjM3NTgwNDM0LCJzY2hJZCI6MjAxNzUsImV4cGlyZUF0IjoxNjc3MTQ0Nzk3fQ.BBfrMYbK-vJeZwUVPVSZOXiMyXy33BD5iyAJRo-WK-0gSHe5NmLNTF2DDUfyR9RcFgTRmIYAJmav_yIowmLK_r9x3T3H9fQomIwbki9hcgW9tHXGvDZPTrMMNtVF1wNVpH-ovyFLxX45dwfcyRfrnNDQyzWU55zXl6iFDLWRJLWAsGnQqQgsTkwgaR39w80lY5coI4DeOLbzIHDBij3a5ZUWEbIJGTXZukooI4N8teIg4ccki69HmkXe1_Rjfg9sJz_SRtRm5Oi2JwjDNFShwjSFUieIZGnKPmtKrTH5mEVeBhe-OUp3nhctWBQaVRJWfMTXv3wOOF0QL4_UG55_BA; SERVERID=d3936289adfff6c3874a2579058ac651|1677137597|1677137595; Hm_lvt_7ecd21a13263a714793f376c18038a87=1677137598; Hm_lpvt_7ecd21a13263a714793f376c18038a87=1677137598");
            request.AddParameter("application/json", @"{""operationName"":""libLayout"",""query"":""query libLayout($libId: Int, $libType: Int) {\n userAuth {\n reserve {\n libs(libType: $libType, libId: $libId) {\n lib_id\n is_open\n lib_floor\n lib_name\n lib_type\n lib_layout {\n seats_total\n seats_booking\n seats_used\n max_x\n max_y\n seats {\n x\n y\n key\n type\n name\n seat_status\n status\n }\n }\n }\n }\n }\n}"",""variables"":{""libId"":117685}}", ParameterType.RequestBody);
            Console.WriteLine("准备就绪，按下开始");
            Console.ReadLine();
            RestResponse response = client.Execute(request);
            var responseContent = response.Content;
            var outputString = Regex.Unescape(responseContent);
            var lib = JsonConvert.DeserializeObject<LibDetailRoot>(outputString);
            Console.WriteLine("楼层：" + lib.data.userAuth.reserve.libs[0].lib_floor);
            Console.WriteLine("名称：" + lib.data.userAuth.reserve.libs[0].lib_name);
            Console.WriteLine("当前是否开放：" + lib.data.userAuth.reserve.libs[0].is_open);
            Console.WriteLine("剩余座位数量：" + (lib.data.userAuth.reserve.libs[0].lib_layout.seats_total - lib.data.userAuth.reserve.libs[0].lib_layout.seats_used));
            Console.WriteLine("已被预定座位：" + lib.data.userAuth.reserve.libs[0].lib_layout.seats_booking + "\n——————————————————————————————————");
            List<SeatsItem> waitingOrderedSeats = new List<SeatsItem>();
            foreach (var singleSeat in lib.data.userAuth.reserve.libs[0].lib_layout.seats)
            {
                if (Regex.IsMatch(singleSeat.name, @"\d{1,3}") && singleSeat.status == false)
                {
                    //Console.WriteLine("空着的座位号：" + singleSeat.name);
                    waitingOrderedSeats.Add(singleSeat);
                }
            }
            IEnumerable<SeatsItem> query = waitingOrderedSeats.OrderBy(x => Convert.ToInt32(x.name));
            foreach (var seatOut in query)
            {
                Console.WriteLine("空着的座位号：" + seatOut.name);
            }
            Console.WriteLine("\n\n输入需要预定的座位号：");
            //获取座位的业务逻辑完毕
            string targetSeatName = Console.ReadLine();
            bool ifFound = false;
            foreach (var seatBooking in query)
            {
                if(seatBooking.name == targetSeatName)
                {
                    ifFound = true;
                    BookSeat(seatBooking);
                }
            }
        }

        public static void BookSeat(SeatsItem seatsItem)
        {
            //判断提交的文本长度，根据座位号不同长度不同
            int OriginalLength = 352;
            if (seatsItem.x >= 10)
                OriginalLength++;
            if (seatsItem.y >= 10)
                OriginalLength++;
            var client = new RestClient("https://wechat.v2.traceint.com/index.php/graphql/");
            var request = new RestRequest();
            request.Method = Method.Post;
            request.RequestFormat = DataFormat.Json;
            request.AddHeader("Content-Type", @"application/json");
            request.AddHeader("Host", @"wechat.v2.traceint.com");
            request.AddHeader("Connection", @"keep-alive");
            request.AddHeader("Content-Length", OriginalLength);
            request.AddHeader("Origin", @"https://web.traceint.com");
            request.AddHeader("User-Agent", @"Mozilla/5.0 (Windows NT 6.1; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/81.0.4044.138 Safari/537.36 NetType/WIFI MicroMessenger/7.0.20.1781(0x6700143B) WindowsWechat(0x63070626)");
            request.AddHeader("App-Version", @"2.0.11");
            request.AddHeader("Accept", @"*/*");
            request.AddHeader("Sec-Fetch-Site", @"same-site");
            request.AddHeader("Sec-Fetch-Mode", @"cors");
            request.AddHeader("Sec-Fetch-Dest", @"empty");
            request.AddHeader("Referer", @"https://web.traceint.com/web/index.html");
            request.AddHeader("Accept-Encoding", @"gzip, deflate, br");
            request.AddHeader("Accept-Language", @"zh-CN,zh;q=0.9,en-US;q=0.8,en;q=0.7");
            request.AddHeader("Cookie", @"FROM_TYPE=weixin; FROM_CODE=WwsCBVAOAws%3D; v=5.5; wechatSESS_ID=009ff2cde8adf4b9e8cf625ff685e3d6b7e337583bc8ffd3; Authorization=eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiJ9.eyJ1c2VySWQiOjM3NTgwNDM0LCJzY2hJZCI6MjAxNzUsImV4cGlyZUF0IjoxNjc3MDY2OTU1fQ.lCvSClfcxwhrNbWzPejpjoOpci_2v1YbPb9GqrpLPKGbXw2vtz-n711ISkfRiovRXIUz7NdT0VyYVOXIsgIMXwqjPW_8xtqf3YdABds_EQpTznogc_g4M0Aj7-5pl4GNjrYfeMHa_O8BAy1NTBD7a-WlQXanFAbcM3GeIxTv5Kds7NzHz3b3sVUbJcLQvigZTY_tiRyvhvoz1znjOjnWLwcY1k5yG08SVkWVoxNSP9oBeJ5JWZRsGu08kfgCXMKliF42Q2AW6NYO_nP46bFwdSufvmhMHkSPPieEg-7btoO411BBMIlha5TPGAW6iliykPzSGNNH_kcoYy1GoiAa_w; Hm_lvt_7ecd21a13263a714793f376c18038a87=1677059755; Hm_lpvt_7ecd21a13263a714793f376c18038a87=1677059755; SERVERID=e3fa93b0fb9e2e6d4f53273540d4e924|1677059760|1677059753");
            request.AddParameter("application/json", @"{""operationName"":""reserueSeat"",""query"":""mutation reserueSeat($libId: Int!, $seatKey: String!, $captchaCode: String, $captcha: String!) {\n userAuth {\n reserve {\n reserueSeat(\n libId: $libId\n seatKey: $seatKey\n captchaCode: $captchaCode\n captcha: $captcha\n )\n }\n }\n}"",""variables"":{""seatKey"":""ReplaceMePlz"",""libId"":117685,""captchaCode"":"""",""captcha"":""""}}".Replace("ReplaceMePlz",seatsItem.key), ParameterType.RequestBody);
            RestResponse response = client.Execute(request);
            var responseContent = response.Content;
            var outputString = Regex.Unescape(responseContent);
            Console.WriteLine(outputString);
            if (outputString.Contains("true"))
            {
                Console.WriteLine($"预定{seatsItem.name}号座位成功");
            }
        } 

        public static void CancelSeat()
        {

        }

        public static void GetCookie()
        {
            var client = new RestClient("http://wechat.v2.traceint.com/index.php/urlNew/auth.html?r=https%3A%2F%2Fweb.traceint.com%2Fweb%2Findex.html&code=021jTrFa1uImTE0ZTTFa1Tg41k4jTrFP&state=1");
            var request = new RestRequest();
            request.Method = Method.Get;
            RestResponse response = client.Execute(request);
            var responseContent = response.Cookies;
            foreach(var temp in responseContent)
            {
                Console.WriteLine(temp.ToString());
            }
        }

        public static void KeepCookie()
        {
            var client = new RestClient("https://wechat.v2.traceint.com/index.php/graphql/");
            var request = new RestRequest();
            request.Method = Method.Post;
            request.RequestFormat = DataFormat.Json;
            request.AddHeader("Cookie", @"Authorization=eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiJ9.eyJ1c2VySWQiOjM3NTgwNDM0LCJzY2hJZCI6MjAxNzUsImV4cGlyZUF0IjoxNjgxNzIxMjc1fQ.GnXDQrxCPAE7hXbUCYWlxbuqb6rDQZUQXZ0HZzKGYhZOiTBaCsDvEyLDVcHdfDnfpwEXT7XMwW1QOZP05Ico0aWphPsf7F6KplIbbLAZ2ZLfDRufj1b29WAIpsdp_qGNl7h2An3ocXoc5yGd31rONIwEGcnv9b5xsvEV1rq5oMnLkKVebL9bL0lIAldv73cQsxUXYQ8SthwOojIcPr3wSfnQ5oH5RBPT62BuRKwu0iGBhU_UrLA_9BVkZbiyS4nGcRGjshKrwZQmqPJBKc3MYidsyM7OlYc1OJ6l_v9K6ax_vUxN5NwID-UEU7_1RNfuI_-JRk6wHtvGz7O2EmceqQ; SERVERID=e3fa93b0fb9e2e6d4f53273540d4e924|1681714075|1681714075");
            request.AddParameter("application/json", @"{
        ""query"": 'query getUserCancleConfig { userAuth { user { holdValidate: getSchConfig(fields: ""hold_validate"", extra: true) } } }',
        ""variables"": {},
        ""operationName"": ""getUserCancleConfig""
    }", ParameterType.RequestBody);
            RestResponse response = client.Execute(request);
            var responseContent = response.Content;
            var outputString = Regex.Unescape(responseContent);
            Console.WriteLine(outputString);

        }
    }
}
