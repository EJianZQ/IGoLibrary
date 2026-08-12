using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Desktop.ViewModels;

namespace IGoLibrary.Ex.Tests;

public sealed class HomeDashboardViewModelTests
{
    [Fact]
    public void Constructor_UsesCurrentTimeForGreeting()
    {
        var localNow = CreateLocalTimestamp(23);
        var viewModel = CreateViewModel(new FakeTimeProvider(localNow.ToUniversalTime()));

        Assert.StartsWith("夜深了，", viewModel.HomeGreetingTitleText, StringComparison.Ordinal);
        Assert.Equal("也别忘了给自己留一点休息时间", viewModel.HomeGreetingMessageText);
        Assert.Equal("23:00:00", viewModel.HomeTimeText);
    }

    [Fact]
    public void UpdateHeroPresentation_CoversEveryHourWithMatchingGreetingAndMessage()
    {
        var viewModel = CreateViewModel(TimeProvider.System);
        var periods = new[]
        {
            new GreetingPeriod(0, 4, "夜深了，", "也别忘了给自己留一点休息时间"),
            new GreetingPeriod(5, 10, "早安，", "准备好开始今天的学习了吗？"),
            new GreetingPeriod(11, 13, "中午好，", "给今天的计划加把劲吧"),
            new GreetingPeriod(14, 17, "下午好，", "专注状态已经准备就绪"),
            new GreetingPeriod(18, 22, "晚上好，", "把今天最后一段时间好好度过吧"),
            new GreetingPeriod(23, 23, "夜深了，", "也别忘了给自己留一点休息时间")
        };

        for (var hour = 0; hour < 24; hour++)
        {
            viewModel.UpdateHeroPresentation(CreateLocalTimestamp(hour));
            var expected = Assert.Single(periods, period => hour >= period.StartHour && hour <= period.EndHour);

            Assert.StartsWith(
                expected.TitlePrefix,
                viewModel.HomeGreetingTitleText,
                StringComparison.Ordinal);
            Assert.Equal(expected.Message, viewModel.HomeGreetingMessageText);
            Assert.StartsWith(hour.ToString("D2"), viewModel.HomeTimeText, StringComparison.Ordinal);
        }
    }

    private static HomeDashboardViewModel CreateViewModel(TimeProvider timeProvider)
    {
        return new HomeDashboardViewModel(
            new ActivityLogService(),
            new FakeAppThemeService(),
            timeProvider);
    }

    private static DateTimeOffset CreateLocalTimestamp(int hour)
    {
        var localDateTime = new DateTime(2026, 8, 12, hour, 0, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(localDateTime, TimeZoneInfo.Local.GetUtcOffset(localDateTime));
    }

    private sealed record GreetingPeriod(
        int StartHour,
        int EndHour,
        string TitlePrefix,
        string Message);
}
