using CNCO.YccuSdk.Navigation;
using Xunit;

namespace CNCO.YccuSdk.Tests;

public class DayNightClockTests
{
    [Theory]
    [InlineData(0, 0, NavDayNightMode.Night)]
    [InlineData(5, 59, NavDayNightMode.Night)]
    [InlineData(6, 0, NavDayNightMode.Day)]
    [InlineData(12, 30, NavDayNightMode.Day)]
    [InlineData(18, 59, NavDayNightMode.Day)]
    [InlineData(19, 0, NavDayNightMode.Night)]
    [InlineData(23, 59, NavDayNightMode.Night)]
    public void LocalTime_PicksDayOrNight(int hour, int minute, NavDayNightMode expected)
    {
        Assert.Equal(expected, DayNightClock.For(new DateTime(2026, 10, 21, hour, minute, 0)));
    }

    [Fact]
    public void ItNeverAnswersAuto_BecauseThePicturesNeedARealColourScheme()
    {
        for (var hour = 0; hour < 24; hour++)
            Assert.NotEqual(NavDayNightMode.Auto, DayNightClock.For(new DateTime(2026, 1, 1, hour, 0, 0)));
    }
}
