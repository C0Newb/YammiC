using CNCO.YccuSdk.Logging;
using Xunit;

namespace CNCO.YccuSdk.Tests;

public class LogFileNameTests
{
    [Fact]
    public void UsesTheLocalDateAndTime_NotAnEpoch()
    {
        Assert.Equal("yammic-2026_10_20-17_52_42.log", LogFileNames.For(new DateTime(2026, 10, 20, 17, 52, 42)));
    }

    [Fact]
    public void PadsSingleDigits_SoNamesSortInTimeOrder()
    {
        var early = LogFileNames.For(new DateTime(2026, 1, 2, 3, 4, 5));
        var late = LogFileNames.For(new DateTime(2026, 11, 12, 13, 14, 15));
        Assert.Equal("yammic-2026_01_02-03_04_05.log", early);
        Assert.True(string.CompareOrdinal(early, late) < 0);
    }

    [Fact]
    public void IgnoresTheCulture()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("ar-SA");   // different calendar and digits
            Assert.Equal("yammic-2026_10_20-17_52_42.log", LogFileNames.For(new DateTime(2026, 10, 20, 17, 52, 42)));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }
}
