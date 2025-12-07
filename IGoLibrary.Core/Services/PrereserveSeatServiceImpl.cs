using IGoLibrary.Core.Data;
using IGoLibrary.Core.Exceptions;
using IGoLibrary.Core.Interfaces;
using Newtonsoft.Json;
using RestSharp;
using System.Text.RegularExpressions;

namespace IGoLibrary.Core.Services
{
    public class PrereserveSeatServiceImpl : IPrereserveSeatService
    {
        public bool PrereserveSeat(string cookie, string seatKey, int libId)
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
                request.AddHeader("Cookie", cookie);

                // 构造预约抢座的 GraphQL mutation
                // 注意：座位 key 需要末尾加点号 "."
                var queryObject = new
                {
                    operationName = "save",
                    query = "mutation save($key: String!, $libid: Int!, $captchaCode: String, $captcha: String) {\n userAuth {\n prereserve {\n save(key: $key, libId: $libid, captcha: $captcha, captchaCode: $captchaCode)\n }\n }\n}",
                    variables = new
                    {
                        key = $"{seatKey}.",  // 座位key末尾加点号
                        libid = libId,
                        captchaCode = "",
                        captcha = ""
                    }
                };

                var queryJson = JsonConvert.SerializeObject(queryObject);
                request.AddParameter("application/json", queryJson, ParameterType.RequestBody);

                // 设置超时时间为5秒
                CancellationTokenSource cts = new CancellationTokenSource();
                cts.CancelAfter(5000);

                RestResponse response = client.Execute(request, cts.Token);
                var responseContent = response.Content;

                if (responseContent != null)
                {
                    var outputString = Regex.Unescape(responseContent); // Unicode字符转义
                    var prereserveSeatRoot = JsonConvert.DeserializeObject<PrereserveSeatRoot>(outputString);

                    if (prereserveSeatRoot.errors != null && prereserveSeatRoot.errors.Count > 0)
                    {
                        // 有错误信息，抛出异常
                        throw new ReserveSeatException(prereserveSeatRoot.errors[0].msg);
                    }

                    if (prereserveSeatRoot.data?.userAuth?.prereserve != null)
                    {
                        // 检查预约结果
                        var saveResult = prereserveSeatRoot.data.userAuth.prereserve.save;

                        // 如果返回的是 "ok" 或者非空字符串，表示预约成功
                        if (!string.IsNullOrEmpty(saveResult))
                        {
                            return true;
                        }
                        else
                        {
                            throw new ReserveSeatException("预约失败：返回结果为空");
                        }
                    }
                    else
                    {
                        throw new ReserveSeatException("响应报文中未包含预约座位信息");
                    }
                }
                else
                {
                    throw new ReserveSeatException("获取响应报文时为空");
                }
            }
        }
    }
}
