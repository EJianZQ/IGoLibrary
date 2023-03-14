using IGoLibrary_Winform.Data;
using IGoLibrary_Winform.CustomException;
using Newtonsoft.Json;
using RestSharp;
using System.Text.RegularExpressions;

namespace IGoLibrary_Winform.Controller
{
    public class CancelReserveServiceImpl : ICancelReserveService
    {
        public bool CancelReserve(string Cookie, string QueryStatement, ref string RetMessage)
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
                request.AddParameter("application/json", QueryStatement, ParameterType.RequestBody);
                //必要的Header和Body都填写完毕，获取响应报文
                CancellationTokenSource cts = new CancellationTokenSource();
                cts.CancelAfter(5000);//设定超时时间为5000ms
                RestResponse response = client.Execute(request, cts.Token);
                var responseContent = response.Content;
                if (responseContent != null)
                {
                    var outputString = Regex.Unescape(responseContent); //Unicode字符转义
                    var cancelReserveRoot = JsonConvert.DeserializeObject<CancelReserveRoot>(outputString);
                    if (cancelReserveRoot == null)
                    {
                        throw new CancelReserveException("解析返回的Json数据失败，可能响应报文为空");
                    }
                    if(cancelReserveRoot.errors != null)
                    {
                        if (cancelReserveRoot.errors[0].msg.Contains("成功") == true)
                        {
                            return true;
                        }
                        else
                        {
                            RetMessage = cancelReserveRoot.errors[0].msg;
                            return false;
                        }
                    }
                    else
                    {
                        throw new CancelReserveException("响应报文中未包含取消预约成功与否的信息");
                    }
                }
                else
                {
                    throw new CancelReserveException("获取响应报文时为空");
                }
            }
        }
    }
}
