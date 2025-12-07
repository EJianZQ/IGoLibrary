using IGoLibrary.Core.Exceptions;
using IGoLibrary.Core.Interfaces;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace IGoLibrary.Core.Services
{
    /// <summary>
    /// 模拟行为枚举
    /// </summary>
    public enum MockBehavior
    {
        /// <summary>
        /// 全部失败 - 所有座位都预约失败
        /// </summary>
        AllFail,

        /// <summary>
        /// 第二个座位成功 - 主选失败，备选1成功
        /// </summary>
        SuccessAtSecond,

        /// <summary>
        /// 第三个座位成功 - 主选和备选1失败，备选2成功
        /// </summary>
        SuccessAtThird,

        /// <summary>
        /// 随机结果 - 60%成功，30%失败，10%异常
        /// </summary>
        Random,

        /// <summary>
        /// 全部成功 - 所有座位都预约成功（用于快速测试）
        /// </summary>
        AllSuccess
    }

    /// <summary>
    /// 模拟预约座位服务 - 用于测试 UI 逻辑和验证核心流程
    /// </summary>
    public class MockPrereserveSeatService : IPrereserveSeatService
    {
        private readonly Random _random = new Random();
        private int _callCount = 0;

        /// <summary>
        /// 模拟行为配置（静态属性，可全局设置）
        /// </summary>
        public static MockBehavior MockBehaviorMode { get; set; } = MockBehavior.Random;

        /// <summary>
        /// 模拟网络延迟时间（毫秒）
        /// </summary>
        public static int MockDelayMs { get; set; } = 200;

        /// <summary>
        /// 是否启用详细日志
        /// </summary>
        public static bool EnableVerboseLogging { get; set; } = true;

        public bool PrereserveSeat(string cookie, string seatKey, int libId)
        {
            _callCount++;

            // 模拟网络延迟（使用异步等待）
            Task.Delay(MockDelayMs).Wait();

            if (EnableVerboseLogging)
            {
                Console.WriteLine($"[模拟抢座] 第{_callCount}次调用 - 座位: {seatKey}, LibID: {libId}, 模式: {MockBehaviorMode}");
            }

            // 根据配置的模拟行为返回结果
            switch (MockBehaviorMode)
            {
                case MockBehavior.AllFail:
                    return HandleAllFail();

                case MockBehavior.SuccessAtSecond:
                    return HandleSuccessAtSecond();

                case MockBehavior.SuccessAtThird:
                    return HandleSuccessAtThird();

                case MockBehavior.AllSuccess:
                    return HandleAllSuccess();

                case MockBehavior.Random:
                default:
                    return HandleRandom();
            }
        }

        /// <summary>
        /// 处理全部失败模式
        /// </summary>
        private bool HandleAllFail()
        {
            Console.WriteLine($"[模拟抢座] 第{_callCount}次调用 - ❌ 预约失败（AllFail 模式）");
            return false;
        }

        /// <summary>
        /// 处理第二个座位成功模式
        /// </summary>
        private bool HandleSuccessAtSecond()
        {
            if (_callCount == 1)
            {
                Console.WriteLine($"[模拟抢座] 第{_callCount}次调用 - ❌ 主选座位失败");
                return false;
            }
            else if (_callCount == 2)
            {
                Console.WriteLine($"[模拟抢座] 第{_callCount}次调用 - ✅ 备选1座位成功！");
                return true;
            }
            else
            {
                Console.WriteLine($"[模拟抢座] 第{_callCount}次调用 - ❌ 预约失败");
                return false;
            }
        }

        /// <summary>
        /// 处理第三个座位成功模式
        /// </summary>
        private bool HandleSuccessAtThird()
        {
            if (_callCount <= 2)
            {
                Console.WriteLine($"[模拟抢座] 第{_callCount}次调用 - ❌ 座位{_callCount}失败");
                return false;
            }
            else if (_callCount == 3)
            {
                Console.WriteLine($"[模拟抢座] 第{_callCount}次调用 - ✅ 备选2座位成功！");
                return true;
            }
            else
            {
                Console.WriteLine($"[模拟抢座] 第{_callCount}次调用 - ❌ 预约失败");
                return false;
            }
        }

        /// <summary>
        /// 处理全部成功模式
        /// </summary>
        private bool HandleAllSuccess()
        {
            Console.WriteLine($"[模拟抢座] 第{_callCount}次调用 - ✅ 预约成功（AllSuccess 模式）");
            return true;
        }

        /// <summary>
        /// 处理随机模式
        /// </summary>
        private bool HandleRandom()
        {
            int randomValue = _random.Next(100);

            // 10% 概率抛出异常（模拟服务器错误）
            if (randomValue < 10)
            {
                string[] errorMessages = new[]
                {
                    "服务器繁忙，请稍后重试",
                    "网络连接超时",
                    "座位已被预约",
                    "预约时间未到",
                    "Cookie 已过期"
                };
                string errorMsg = errorMessages[_random.Next(errorMessages.Length)];
                Console.WriteLine($"[模拟抢座] 第{_callCount}次调用 - ⚠️ 抛出异常: {errorMsg}");
                throw new ReserveSeatException($"[模拟模式] {errorMsg}");
            }

            // 30% 概率返回 false（预约失败）
            if (randomValue < 40)
            {
                Console.WriteLine($"[模拟抢座] 第{_callCount}次调用 - ❌ 预约失败（随机模式）");
                return false;
            }

            // 60% 概率返回 true（预约成功）
            Console.WriteLine($"[模拟抢座] 第{_callCount}次调用 - ✅ 预约成功（随机模式）");
            return true;
        }

        /// <summary>
        /// 重置调用计数（用于测试）
        /// </summary>
        public void ResetCallCount()
        {
            _callCount = 0;
            Console.WriteLine($"[模拟抢座] 调用计数已重置");
        }
    }
}
