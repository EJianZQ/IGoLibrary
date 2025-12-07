using IGoLibrary.Core.Data;
using IGoLibrary.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Threading;

namespace IGoLibrary.Core.Services
{
    /// <summary>
    /// 模拟获取图书馆信息服务 - 用于测试 UI 逻辑
    /// </summary>
    public class MockGetLibInfoService : IGetLibInfoService
    {
        private readonly Random _random = new Random();
        private int _callCount = 0;

        public Library GetLibInfo(string Cookie, string QueryStatement)
        {
            _callCount++;

            // 模拟网络延迟 100-300ms
            Thread.Sleep(_random.Next(100, 300));

            Console.WriteLine($"[模拟模式] 第{_callCount}次查询座位信息");

            // 创建模拟的图书馆数据
            var library = new Library
            {
                IsOpen = true,
                Name = "模拟图书馆",
                LibID = 123,
                Floor = "3F",
                SeatsInfo = new SeatsInfo
                {
                    TotalSeats = 100,
                    BookedSeats = _random.Next(20, 40),
                    UsedSeats = _random.Next(40, 60)
                },
                Seats = GenerateMockSeats()
            };

            return library;
        }

        public Library? GetLibInfo_Debug(string Cookies, string QueryStatement)
        {
            return GetLibInfo(Cookies, QueryStatement);
        }

        public List<SeatsItem> GetLibSeats(LibRoot root)
        {
            // 模拟模式下不需要从 LibRoot 解析，直接返回空列表
            // 因为我们在 GetLibInfo 中已经生成了模拟座位数据
            return new List<SeatsItem>();
        }

        /// <summary>
        /// 生成模拟的座位数据
        /// </summary>
        private List<SeatsItem> GenerateMockSeats()
        {
            var seats = new List<SeatsItem>();

            // 生成 50 个座位
            for (int i = 1; i <= 50; i++)
            {
                // 随机设置座位状态（30% 有人，70% 无人）
                bool isOccupied = _random.Next(100) < 30;

                seats.Add(new SeatsItem
                {
                    name = i.ToString("D3"), // 001, 002, 003...
                    status = isOccupied,
                    key = $"mock_seat_key_{i}",
                    type = 1,  // 1 表示座位类型
                    seat_status = isOccupied ? 1 : 0,
                    x = i % 10,
                    y = i / 10
                });
            }

            return seats;
        }
    }
}
