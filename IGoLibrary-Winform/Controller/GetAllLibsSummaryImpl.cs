using IGoLibrary_Winform.CustomException;
using IGoLibrary_Winform.Data;
using Newtonsoft.Json;
using RestSharp;
using Sunny.UI.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace IGoLibrary_Winform.Controller
{
    internal class GetAllLibsSummaryImpl : IGetAllLibsSummaryService
    {
        /// <summary>
        /// 根据Cookie获取所有可用的图书馆的概述，成功且信息合法返回AllLibsSummary类型，否则抛出异常
        /// </summary>
        /// <param name="Cookie"></param>
        /// <param name="QueryStatement"></param>
        /// <returns></returns>
        public AllLibsSummary GetAllLibsSummary(string Cookie, string QueryStatement)
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
                if(responseContent != null)
                {
                    var outputString = Regex.Unescape(responseContent); //Unicode字符转义
                    var allLibsRoot = JsonConvert.DeserializeObject<AllLibsRoot>(outputString);
                    if(allLibsRoot.data.userAuth.reserve.libs != null)
                    {
                        AllLibsSummary tempSummary = new AllLibsSummary();
                        for(int i = 0 ; i < allLibsRoot.data.userAuth.reserve.libs.Count; i++)
                        {
                            if (allLibsRoot.data.userAuth.reserve.libs[i].lib_floor != "0")
                            {
                                tempSummary.libSummaries.Add(new LibSummary() { LibID = allLibsRoot.data.userAuth.reserve.libs[i].lib_id,
                                    Floor = allLibsRoot.data.userAuth.reserve.libs[i].lib_floor,
                                    Name = allLibsRoot.data.userAuth.reserve.libs[i].lib_name,
                                    IsOpen = allLibsRoot.data.userAuth.reserve.libs[i].is_open
                                });
                            }
                        }
                        if(tempSummary.libSummaries.Count > 0)
                            return tempSummary;
                        else
                            throw new GetAllLibsSummaryException("未获取到任何一个图书馆信息");
                    }
                    else
                    {
                        throw new GetAllLibsSummaryException("响应报文中未包含图书馆信息");
                    }
                }
                else
                {
                    throw new GetAllLibsSummaryException("获取响应报文时为空");
                }
            }
        }
    }
}
