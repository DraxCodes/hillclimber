using ClimberWeb.Services;

namespace ClimberWeb.Tests;

/// <summary>How a rail row says when it was last used.</summary>
public class SessionFormatTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, "now")]
    [InlineData(20, "now")]
    [InlineData(44, "now")]
    [InlineData(60, "1m")]
    [InlineData(7 * 60, "7m")]
    public void Relative_ReadsInMinutesUnderAnHour(int secondsAgo, string expected)
    {
        Assert.Equal(expected, SessionFormat.Relative(Now.AddSeconds(-secondsAgo), Now));
    }

    [Theory]
    [InlineData(1, "1h")]
    [InlineData(3.5, "3h")]
    public void Relative_ReadsInHoursUnderADay(double hoursAgo, string expected)
    {
        Assert.Equal(expected, SessionFormat.Relative(Now.AddHours(-hoursAgo), Now));
    }

    [Fact]
    public void Relative_ReadsInDaysUnderAWeek()
    {
        Assert.Equal("2d", SessionFormat.Relative(Now.AddDays(-2), Now));
        Assert.Equal("6d", SessionFormat.Relative(Now.AddDays(-6), Now));
    }

    [Fact]
    public void Relative_FallsBackToADateOnceItIsOld()
    {
        var label = SessionFormat.Relative(Now.AddDays(-8), Now);

        Assert.Matches(@"^[A-Z][a-z]{2} \d{1,2}$", label);
    }

    [Fact]
    public void Relative_ShowsNowForAClockThatRanBackwards()
    {
        Assert.Equal("now", SessionFormat.Relative(Now.AddMinutes(5), Now));
    }
}