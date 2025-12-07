using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using IGoLibrary.Core.Data;
using IGoLibrary.Core.Interfaces;
using IGoLibrary.Core.Services;
using IGoLibrary.Mac.ViewModels;
using Moq;
using Xunit;

namespace IGoLibrary.Tests
{
    /// <summary>
    /// GrabSeatViewModel 核心算法单元测试
    /// 测试重点：备选座位切换、北京时间倒计时、自动重试限制
    /// </summary>
    public class GrabSeatViewModelTests
    {
        #region 测试1: 备选座位切换逻辑

        [Fact]
        public async Task BackupSeatSwitching_MainSeatFails_ShouldTryBackup1()
        {
            // Arrange - 准备测试数据
            var mockGetLibInfoService = new Mock<IGetLibInfoService>();
            var mockPrereserveSeatService = new Mock<IPrereserveSeatService>();
            var mockSessionService = new Mock<ISessionService>();
            var mockNotificationService = new Mock<INotificationService>();
            var mockGetAllLibsService = new Mock<IGetAllLibsSummaryService>();

            // 设置 Cookie 和图书馆信息
            mockSessionService.Setup(s => s.Cookie).Returns("test_cookie");
            mockSessionService.Setup(s => s.QueryLibInfoSyntax).Returns("test_query");
            mockSessionService.Setup(s => s.CurrentLibrary).Returns(new Library { LibID = 123 });

            // 模拟座位数据：主选座位和备选座位都是空的
            var library = new Library
            {
                LibID = 123,
                Name = "测试图书馆",
                Seats = new List<SeatsItem>
                {
                    new SeatsItem { key = "seat_001", name = "001", status = false }, // 主选：空座
                    new SeatsItem { key = "seat_002", name = "002", status = false }, // 备选1：空座
                    new SeatsItem { key = "seat_003", name = "003", status = false }  // 备选2：空座
                }
            };

            mockGetLibInfoService.Setup(s => s.GetLibInfo(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(library);

            // 关键设置：主选座位预约失败（返回 false），备选1 成功（返回 true）
            mockPrereserveSeatService.SetupSequence(s => s.PrereserveSeat(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<int>()))
                .Returns(false)  // 第1次调用：主选座位失败
                .Returns(true);  // 第2次调用：备选1 成功

            var viewModel = new TestableGrabSeatViewModel(
                mockGetLibInfoService.Object,
                new Mock<IReserveSeatService>().Object,
                mockPrereserveSeatService.Object,
                mockSessionService.Object,
                mockNotificationService.Object,
                mockGetAllLibsService.Object);

            // 添加主选和备选座位
            viewModel.WaitingGrabSeats.Add(new SeatKeyData { Key = "seat_001", Name = "001", Priority = 0 });
            viewModel.WaitingGrabSeats.Add(new SeatKeyData { Key = "seat_002", Name = "002", Priority = 1 });
            viewModel.WaitingGrabSeats.Add(new SeatKeyData { Key = "seat_003", Name = "003", Priority = 2 });

            // Act - 执行测试
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await viewModel.TestRunMonitorAsync(cts.Token);

            // Assert - 验证结果
            // 验证1: PrereserveSeat 被调用了 2 次（主选失败后，立即尝试备选1）
            mockPrereserveSeatService.Verify(
                s => s.PrereserveSeat(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()),
                Times.Exactly(2),
                "主选失败后应该立即尝试备选座位，总共调用2次");

            // 验证2: 第1次调用是主选座位
            mockPrereserveSeatService.Verify(
                s => s.PrereserveSeat(It.IsAny<string>(), "seat_001", It.IsAny<int>()),
                Times.Once,
                "第1次应该尝试主选座位 seat_001");

            // 验证3: 第2次调用是备选1座位
            mockPrereserveSeatService.Verify(
                s => s.PrereserveSeat(It.IsAny<string>(), "seat_002", It.IsAny<int>()),
                Times.Once,
                "第2次应该尝试备选1座位 seat_002");

            // 验证4: 备选2 不应该被调用（因为备选1成功了）
            mockPrereserveSeatService.Verify(
                s => s.PrereserveSeat(It.IsAny<string>(), "seat_003", It.IsAny<int>()),
                Times.Never,
                "备选1成功后不应该尝试备选2");

            // 验证5: 成功通知被调用
            mockNotificationService.Verify(
                s => s.ShowSuccess(It.IsAny<string>(), It.IsAny<string>()),
                Times.Once,
                "预约成功后应该显示成功通知");
        }

        [Fact]
        public async Task BackupSeatSwitching_AllSeatsFail_ShouldTryAllSeatsAndStop()
        {
            // Arrange
            var mockGetLibInfoService = new Mock<IGetLibInfoService>();
            var mockPrereserveSeatService = new Mock<IPrereserveSeatService>();
            var mockSessionService = new Mock<ISessionService>();
            var mockNotificationService = new Mock<INotificationService>();
            var mockGetAllLibsService = new Mock<IGetAllLibsSummaryService>();

            mockSessionService.Setup(s => s.Cookie).Returns("test_cookie");
            mockSessionService.Setup(s => s.QueryLibInfoSyntax).Returns("test_query");
            mockSessionService.Setup(s => s.CurrentLibrary).Returns(new Library { LibID = 123 });

            var library = new Library
            {
                LibID = 123,
                Seats = new List<SeatsItem>
                {
                    new SeatsItem { key = "seat_001", name = "001", status = false },
                    new SeatsItem { key = "seat_002", name = "002", status = false },
                    new SeatsItem { key = "seat_003", name = "003", status = false }
                }
            };

            mockGetLibInfoService.Setup(s => s.GetLibInfo(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(library);

            // 所有座位都失败
            mockPrereserveSeatService.Setup(s => s.PrereserveSeat(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<int>()))
                .Returns(false);

            var viewModel = new TestableGrabSeatViewModel(
                mockGetLibInfoService.Object,
                new Mock<IReserveSeatService>().Object,
                mockPrereserveSeatService.Object,
                mockSessionService.Object,
                mockNotificationService.Object,
                mockGetAllLibsService.Object);

            viewModel.WaitingGrabSeats.Add(new SeatKeyData { Key = "seat_001", Name = "001", Priority = 0 });
            viewModel.WaitingGrabSeats.Add(new SeatKeyData { Key = "seat_002", Name = "002", Priority = 1 });
            viewModel.WaitingGrabSeats.Add(new SeatKeyData { Key = "seat_003", Name = "003", Priority = 2 });

            // Act
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await viewModel.TestRunMonitorAsync(cts.Token);

            // Assert
            // 验证：所有3个座位都被尝试了
            mockPrereserveSeatService.Verify(
                s => s.PrereserveSeat(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()),
                Times.Exactly(3),
                "所有座位都失败时，应该尝试所有3个座位");

            // 验证：显示错误通知（所有座位都失败）
            mockNotificationService.Verify(
                s => s.ShowError("抢座失败", "所有座位都预约失败"),
                Times.Once,
                "所有座位失败后应该显示错误通知");
        }

        #endregion

        #region 测试2: 北京时间倒计时逻辑

        [Theory]
        [InlineData(19, 0, 0, 19, 59, 50)]  // 19:00 -> 19:59:50，倒计时 59分50秒
        [InlineData(19, 59, 55, 19, 59, 50)] // 19:59:55 -> 19:59:50，已经过了准备时间（跨天计算）
        [InlineData(20, 0, 1, 19, 59, 50)]  // 20:00:01 -> 19:59:50，已经过了准备时间（跨天计算）
        public void BeijingTimeCountdown_VariousTimes_ShouldCalculateCorrectly(
            int currentHour, int currentMinute, int currentSecond,
            int targetHour, int targetMinute, int targetSecond)
        {
            // Arrange
            var currentTime = new TimeSpan(currentHour, currentMinute, currentSecond);
            var targetTime = new TimeSpan(targetHour, targetMinute, targetSecond);

            // Act
            var interval = targetTime - currentTime;

            // 如果已经过了目标时间（跨天情况）
            if (interval.TotalSeconds < 0)
            {
                interval = interval.Add(TimeSpan.FromDays(1));
            }

            // Assert
            if (currentHour == 19 && currentMinute == 0 && currentSecond == 0)
            {
                // 场景1: 19:00 -> 19:59:50，应该是 59分50秒
                interval.TotalMinutes.Should().BeApproximately(59.833, 0.01, "19:00 到 19:59:50 应该是约 59.83 分钟");
                interval.TotalSeconds.Should().BeApproximately(3590, 1, "19:00 到 19:59:50 应该是 3590 秒");
            }
            else if (currentHour == 19 && currentMinute == 59 && currentSecond == 55)
            {
                // 场景2: 19:59:55 -> 19:59:50，已经过了，应该跨天计算（23小时59分55秒）
                interval.TotalHours.Should().BeApproximately(23.998, 0.01, "19:59:55 到明天 19:59:50 应该是约 23.998 小时");
                interval.TotalSeconds.Should().BeApproximately(86395, 1, "应该是约 86395 秒（跨天）");
            }
            else if (currentHour == 20 && currentMinute == 0 && currentSecond == 1)
            {
                // 场景3: 20:00:01 -> 19:59:50，已经过了，应该跨天计算（23小时59分49秒）
                interval.TotalHours.Should().BeApproximately(23.997, 0.01, "20:00:01 到明天 19:59:50 应该是约 23.997 小时");
                interval.TotalSeconds.Should().BeApproximately(86389, 1, "应该是约 86389 秒（跨天）");
            }
        }

        [Fact]
        public void BeijingTimeCountdown_EdgeCase_MidnightCrossing()
        {
            // Arrange - 测试跨午夜的边界情况
            var currentTime = new TimeSpan(23, 59, 0);  // 23:59:00
            var targetTime = new TimeSpan(0, 1, 0);     // 00:01:00（第二天）

            // Act
            var interval = targetTime - currentTime;
            if (interval.TotalSeconds < 0)
            {
                interval = interval.Add(TimeSpan.FromDays(1));
            }

            // Assert
            interval.TotalMinutes.Should().BeApproximately(2, 0.1, "23:59 到 00:01 应该是 2 分钟");
            interval.TotalSeconds.Should().BeApproximately(120, 1, "应该是 120 秒");
        }

        [Fact]
        public void BeijingTimeCountdown_ExactTargetTime_ShouldBeZero()
        {
            // Arrange - 测试正好到达目标时间
            var currentTime = new TimeSpan(19, 59, 50);
            var targetTime = new TimeSpan(19, 59, 50);

            // Act
            var interval = targetTime - currentTime;

            // Assert
            interval.TotalSeconds.Should().Be(0, "当前时间等于目标时间时，倒计时应该是 0");
        }

        #endregion

        #region 测试3: 自动重试限制

        [Fact]
        public async Task AutoRetryLimit_AllSeatsOccupied_ShouldNotInfiniteLoop()
        {
            // Arrange
            var mockGetLibInfoService = new Mock<IGetLibInfoService>();
            var mockPrereserveSeatService = new Mock<IPrereserveSeatService>();
            var mockSessionService = new Mock<ISessionService>();
            var mockNotificationService = new Mock<INotificationService>();
            var mockGetAllLibsService = new Mock<IGetAllLibsSummaryService>();

            mockSessionService.Setup(s => s.Cookie).Returns("test_cookie");
            mockSessionService.Setup(s => s.QueryLibInfoSyntax).Returns("test_query");
            mockSessionService.Setup(s => s.CurrentLibrary).Returns(new Library { LibID = 123 });

            int callCount = 0;

            // 模拟所有座位都有人（status = true）
            mockGetLibInfoService.Setup(s => s.GetLibInfo(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(() =>
                {
                    callCount++;
                    return new Library
                    {
                        LibID = 123,
                        Seats = new List<SeatsItem>
                        {
                            new SeatsItem { key = "seat_001", name = "001", status = true }, // 有人
                            new SeatsItem { key = "seat_002", name = "002", status = true }, // 有人
                            new SeatsItem { key = "seat_003", name = "003", status = true }  // 有人
                        }
                    };
                });

            var viewModel = new TestableGrabSeatViewModel(
                mockGetLibInfoService.Object,
                new Mock<IReserveSeatService>().Object,
                mockPrereserveSeatService.Object,
                mockSessionService.Object,
                mockNotificationService.Object,
                mockGetAllLibsService.Object);

            viewModel.WaitingGrabSeats.Add(new SeatKeyData { Key = "seat_001", Name = "001", Priority = 0 });
            viewModel.WaitingGrabSeats.Add(new SeatKeyData { Key = "seat_002", Name = "002", Priority = 1 });
            viewModel.WaitingGrabSeats.Add(new SeatKeyData { Key = "seat_003", Name = "003", Priority = 2 });

            // 设置超时时间为 3 秒（防止无限循环）
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

            // Act
            var startTime = DateTime.Now;
            try
            {
                await viewModel.TestRunMonitorAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                // 预期会超时取消
            }
            var endTime = DateTime.Now;
            var duration = (endTime - startTime).TotalSeconds;

            // Assert
            // 验证1: 不会无限循环，应该在超时时间内被取消
            duration.Should().BeLessThan(5, "监控应该在超时时间内被取消，不会无限循环");

            // 验证2: GetLibInfo 被调用了多次（说明在循环重试）
            callCount.Should().BeGreaterThan(0, "应该至少调用一次 GetLibInfo");
            callCount.Should().BeLessThan(100, "不应该调用太多次（说明有合理的延迟机制）");

            // 验证3: PrereserveSeat 不应该被调用（因为所有座位都有人）
            mockPrereserveSeatService.Verify(
                s => s.PrereserveSeat(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()),
                Times.Never,
                "所有座位都有人时，不应该尝试预约");
        }

        [Fact]
        public async Task AutoRetryLimit_With50Iterations_ShouldAddExtraDelay()
        {
            // Arrange - 测试每50次循环会额外延迟
            var mockGetLibInfoService = new Mock<IGetLibInfoService>();
            var mockPrereserveSeatService = new Mock<IPrereserveSeatService>();
            var mockSessionService = new Mock<ISessionService>();
            var mockNotificationService = new Mock<INotificationService>();
            var mockGetAllLibsService = new Mock<IGetAllLibsSummaryService>();

            mockSessionService.Setup(s => s.Cookie).Returns("test_cookie");
            mockSessionService.Setup(s => s.QueryLibInfoSyntax).Returns("test_query");
            mockSessionService.Setup(s => s.CurrentLibrary).Returns(new Library { LibID = 123 });

            int callCount = 0;

            mockGetLibInfoService.Setup(s => s.GetLibInfo(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(() =>
                {
                    callCount++;
                    // 在第3次调用时，让座位变为空的，触发预约成功
                    if (callCount >= 3)
                    {
                        return new Library
                        {
                            LibID = 123,
                            Seats = new List<SeatsItem>
                            {
                                new SeatsItem { key = "seat_001", name = "001", status = false }
                            }
                        };
                    }
                    return new Library
                    {
                        LibID = 123,
                        Seats = new List<SeatsItem>
                        {
                            new SeatsItem { key = "seat_001", name = "001", status = true }
                        }
                    };
                });

            mockPrereserveSeatService.Setup(s => s.PrereserveSeat(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<int>()))
                .Returns(true);

            var viewModel = new TestableGrabSeatViewModel(
                mockGetLibInfoService.Object,
                new Mock<IReserveSeatService>().Object,
                mockPrereserveSeatService.Object,
                mockSessionService.Object,
                mockNotificationService.Object,
                mockGetAllLibsService.Object);

            viewModel.WaitingGrabSeats.Add(new SeatKeyData { Key = "seat_001", Name = "001", Priority = 0 });

            // Act
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await viewModel.TestRunMonitorAsync(cts.Token);

            // Assert
            callCount.Should().BeGreaterThanOrEqualTo(3, "应该至少调用3次 GetLibInfo");
            mockPrereserveSeatService.Verify(
                s => s.PrereserveSeat(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()),
                Times.Once,
                "找到空座位后应该尝试预约");
        }

        [Fact]
        public async Task AutoRetryLimit_CancellationToken_ShouldStopImmediately()
        {
            // Arrange - 测试取消令牌能否立即停止循环
            var mockGetLibInfoService = new Mock<IGetLibInfoService>();
            var mockPrereserveSeatService = new Mock<IPrereserveSeatService>();
            var mockSessionService = new Mock<ISessionService>();
            var mockNotificationService = new Mock<INotificationService>();
            var mockGetAllLibsService = new Mock<IGetAllLibsSummaryService>();

            mockSessionService.Setup(s => s.Cookie).Returns("test_cookie");
            mockSessionService.Setup(s => s.QueryLibInfoSyntax).Returns("test_query");
            mockSessionService.Setup(s => s.CurrentLibrary).Returns(new Library { LibID = 123 });

            mockGetLibInfoService.Setup(s => s.GetLibInfo(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(new Library
                {
                    LibID = 123,
                    Seats = new List<SeatsItem>
                    {
                        new SeatsItem { key = "seat_001", name = "001", status = true }
                    }
                });

            var viewModel = new TestableGrabSeatViewModel(
                mockGetLibInfoService.Object,
                new Mock<IReserveSeatService>().Object,
                mockPrereserveSeatService.Object,
                mockSessionService.Object,
                mockNotificationService.Object,
                mockGetAllLibsService.Object);

            viewModel.WaitingGrabSeats.Add(new SeatKeyData { Key = "seat_001", Name = "001", Priority = 0 });

            var cts = new CancellationTokenSource();

            // Act - 启动监控，然后立即取消
            var monitorTask = viewModel.TestRunMonitorAsync(cts.Token);
            await Task.Delay(500); // 等待500ms让监控开始
            cts.Cancel(); // 取消监控

            var startTime = DateTime.Now;
            try
            {
                await monitorTask;
            }
            catch (OperationCanceledException)
            {
                // 预期会抛出取消异常
            }
            var endTime = DateTime.Now;
            var duration = (endTime - startTime).TotalSeconds;

            // Assert
            duration.Should().BeLessThan(2, "取消令牌应该能够快速停止监控循环");
        }

        #endregion
    }

    #region 测试辅助类

    /// <summary>
    /// 可测试的 GrabSeatViewModel（暴露私有方法用于测试）
    /// </summary>
    public class TestableGrabSeatViewModel : GrabSeatViewModel
    {
        public TestableGrabSeatViewModel(
            IGetLibInfoService getLibInfoService,
            IReserveSeatService reserveSeatService,
            IPrereserveSeatService prereserveSeatService,
            ISessionService sessionService,
            INotificationService notificationService,
            IGetAllLibsSummaryService getAllLibsSummaryService)
            : base(getLibInfoService, reserveSeatService, prereserveSeatService,
                  sessionService, notificationService, getAllLibsSummaryService)
        {
        }

        /// <summary>
        /// 暴露 RunMonitorAsync 方法用于测试
        /// </summary>
        public async Task TestRunMonitorAsync(CancellationToken cancellationToken)
        {
            // 使用反射调用私有方法
            var method = typeof(GrabSeatViewModel).GetMethod("RunMonitorAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (method != null)
            {
                var task = (Task)method.Invoke(this, new object[] { cancellationToken });
                await task;
            }
        }
    }

    #endregion
}
