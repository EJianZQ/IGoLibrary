using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IGoLibrary.Core.Interfaces
{
    /// <summary>
    /// 预约抢座服务接口（用于提前预约第二天的座位）
    /// </summary>
    public interface IPrereserveSeatService
    {
        /// <summary>
        /// 预约座位（用于晚上8点抢座）
        /// </summary>
        /// <param name="cookie">用户Cookie</param>
        /// <param name="seatKey">座位Key（需要末尾加点号）</param>
        /// <param name="libId">图书馆ID</param>
        /// <returns>是否预约成功</returns>
        bool PrereserveSeat(string cookie, string seatKey, int libId);
    }
}
