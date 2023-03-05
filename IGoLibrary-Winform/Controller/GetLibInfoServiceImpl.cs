using IGoLibrary_Winform.Data;
using IGoLibrary_Winform.CustomException;
using Newtonsoft.Json;
using RestSharp;
using System.Text.RegularExpressions;

namespace IGoLibrary_Winform.Controller
{
    public class GetLibInfoServiceImpl : IGetLibInfoService
    {
        /// <summary>
        /// 获取图书馆所有信息，成功且信息合法返回Library类型，否则抛出异常
        /// </summary>
        /// <param name="Cookies"></param>
        /// <param name="QueryStatement"></param>
        /// <returns></returns>
        /// <exception cref="GetLibInfoException"></exception>
        public Library? GetLibInfo(string Cookies, string QueryStatement)
        {
            using (var client = new RestClient("https://wechat.v2.traceint.com/index.php/graphql/"))
            {
                var request = new RestRequest();
                request.Method = Method.Post;
                request.RequestFormat = DataFormat.Json;
                request.AddHeader("Content-Type", @"application/json");
                request.AddHeader("Host", @"wechat.v2.traceint.com");
                request.AddHeader("Connection", @"keep-alive");
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
                //以上信息都是通用的
                request.AddHeader("Content-Length", QueryStatement.Length);
                request.AddHeader("Cookie", Cookies);
                request.AddParameter("application/json", QueryStatement, ParameterType.RequestBody);
                //必要的Header和Body都填写完毕，获取响应报文
                CancellationTokenSource cts = new CancellationTokenSource();
                cts.CancelAfter(5000);//设定超时时间为5000ms
                RestResponse response = client.Execute(request, cts.Token);
                var responseContent = response.Content;
                if (responseContent != null)
                {
                    var outputString = Regex.Unescape(responseContent); //Unicode字符转义
                    var libRoot = JsonConvert.DeserializeObject<LibRoot>(outputString);
                    if (libRoot.errors != null) //判断是否有错误信息，有就抛异常
                    {
                        switch (libRoot.errors[0].code)
                        {
                            case 1:
                                {
                                    throw new GetLibInfoException("LibID错误，不存在该图书馆(室)");
                                }
                            case 40001:
                                {
                                    throw new GetLibInfoException("Cookies已过期");
                                }
                            default:
                                {
                                    throw new GetLibInfoException(libRoot.errors[0].msg);
                                }
                        }
                    }
                    if (libRoot.data.userAuth.reserve != null) //如果reserve不为null则有正常数据，可返回Library
                    {
                        return new Library(libRoot);
                    }
                    else
                    {
                        throw new GetLibInfoException("响应报文中未包含图书馆信息");
                    }
                }
                else
                {
                    throw new GetLibInfoException("获取响应报文时为空");
                }
            }
        }

        public List<SeatsItem> GetLibSeats(LibRoot root)
        {
            if(root != null)
            {
                List<SeatsItem> waitingOrderedSeats = new List<SeatsItem>();
                foreach (var singleSeat in root.data.userAuth.reserve.libs[0].lib_layout.seats)
                {
                    if(singleSeat.name != null)
                    {
                        if (Regex.IsMatch(singleSeat.name, @"\d{1,3}"))
                        {
                            waitingOrderedSeats.Add(singleSeat);
                        }
                    }
                }
                return waitingOrderedSeats;
            }
            return null;
        }


        public Library? GetLibInfo_Debug(string Cookies, string QueryStatement)
        {
            return GetLibInfo(Cookies, QueryStatement);
        }
    }
}
