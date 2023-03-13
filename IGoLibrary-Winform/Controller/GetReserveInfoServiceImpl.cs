using IGoLibrary_Winform.Data;
using IGoLibrary_Winform.CustomException;
using Newtonsoft.Json;
using RestSharp;
using System.Text.RegularExpressions;

namespace IGoLibrary_Winform.Controller
{
    public class GetReserveInfoServiceImpl : IGetReserveInfoService
    {
        public ReserveInfo GetReserveInfo(string Cookie, string QueryStatement)
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
                request.AddHeader("Cookie", Cookie);
                request.AddParameter("application/json", QueryStatement, ParameterType.RequestBody,false);
                MessageBox.Show(request.Parameters.GetParameters(ParameterType.RequestBody).ToString());
                //必要的Header和Body都填写完毕，获取响应报文
                CancellationTokenSource cts = new CancellationTokenSource();
                cts.CancelAfter(5000);//设定超时时间为5000ms
                RestResponse response = client.Execute(request, cts.Token);
                var responseContent = response.Content;
                if (responseContent != null)
                {
                    var outputString = Regex.Unescape(responseContent); //Unicode字符转义
                    var reserveInfoRoot = JsonConvert.DeserializeObject<ReserveInfoRoot>(outputString);
                    if (reserveInfoRoot == null)
                    {
                        throw new GetReserveInfoException("解析返回的Json数据失败，可能响应报文为空");
                    }
                    if (reserveInfoRoot.errors != null) //判断是否有错误信息，有就抛异常
                    {
                        switch (reserveInfoRoot.errors[0].code)
                        {
                            case 1:
                                {
                                    throw new GetReserveInfoException("LibID错误，不存在该图书馆(室)");
                                }
                            case 40001:
                                {
                                    throw new GetReserveInfoException("Cookies已过期");
                                }
                            default:
                                {
                                    throw new GetReserveInfoException(reserveInfoRoot.errors[0].msg);
                                }
                        }
                    }
                    if (reserveInfoRoot.data.userAuth.reserve.reserve != null)
                    {
                        return new ReserveInfo() { Token = reserveInfoRoot.data.userAuth.reserve.getSToken,
                            ExpiredTimeStamp = reserveInfoRoot.data.userAuth.reserve.reserve.exp_date.ToString(),
                            LibName = reserveInfoRoot.data.userAuth.reserve.reserve.lib_name,
                            SeatKeyDta = new SeatKeyData() { Name = reserveInfoRoot.data.userAuth.reserve.reserve.seat_name, 
                                Status = null,
                                Key = reserveInfoRoot.data.userAuth.reserve.reserve.seat_key
                            }
                        };
                    }
                    else
                    {
                        throw new GetReserveInfoException("当前没有预定座位");
                    }
                }
                else
                { 
                    throw new GetReserveInfoException("获取响应报文时为空");
                }
            }
        }
    }
}