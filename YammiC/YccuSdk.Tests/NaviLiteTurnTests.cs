using CNCO.YccuSdk.Navigation.Protocol;
using Xunit;

namespace CNCO.YccuSdk.Tests;

public class NaviLiteTurnTests
{
    private static readonly double Feet500 = 500 * 0.3048;

    [Fact]
    public void FiveHundredFeet_ReadsAsFiveHundredFeet()
    {
        var turn = new TurnGuidance(TurnIcon.Right, Feet500);
        Assert.Equal((500f, "ft"), turn.Display());
        Assert.Equal("Turn right in 500 ft", turn.Describe());
    }

    [Theory]
    [InlineData(1609.344 * 1.2, 1.2f, "mi")]
    [InlineData(40, 150f, "ft")]
    public void Imperial_ChoosesFeetOrMiles(double meters, float value, string unit) =>
        Assert.Equal((value, unit), new TurnGuidance(TurnIcon.Left, meters).Display());

    [Theory]
    [InlineData(80, 80f, "m")]
    [InlineData(2500, 2.5f, "km")]
    public void Metric_ChoosesMetresOrKilometres(double meters, float value, string unit) =>
        Assert.Equal((value, unit), new TurnGuidance(TurnIcon.Left, meters, Units: DistanceUnit.Metric).Display());

    [Fact]
    public void NextTurn_IsIconFloatLengthsThenText()
    {
        var payload = NaviLiteTurn.NextTurn(new TurnGuidance(TurnIcon.Right, Feet500, "Main"));
        var expected = new List<byte> { 35 };
        expected.AddRange(BitConverter.GetBytes(500f));
        expected.AddRange(new byte[] { 2, 4 });
        expected.AddRange("ft"u8.ToArray());
        expected.AddRange("Main"u8.ToArray());
        Assert.Equal(expected, payload);
    }

    [Fact]
    public void NavigationInfo_CarriesTheRemainingTripAndLanes()
    {
        var payload = NaviLiteTurn.NavigationInfo(new TurnGuidance(TurnIcon.Left, Feet500, "A"), 1609.344 * 3, 12, new byte[] { 1, 2, 3, 4, 5, 6, 7 });
        var expected = new List<byte> { 34 };
        expected.AddRange(BitConverter.GetBytes(500f));
        expected.AddRange(new byte[] { 2, 1 });
        expected.AddRange(BitConverter.GetBytes(3f));
        expected.Add(2);
        expected.AddRange(BitConverter.GetBytes(12u));
        expected.Add(5); // at most five lanes
        expected.AddRange("ft"u8.ToArray());
        expected.AddRange("A"u8.ToArray());
        expected.AddRange("mi"u8.ToArray());
        expected.AddRange(new byte[] { 1, 2, 3, 4, 5 });
        Assert.Equal(expected, payload);
    }

    [Fact]
    public void LongRoadNames_AreCutOnACharacterBoundary()
    {
        var payload = NaviLiteTurn.NextTurn(new TurnGuidance(TurnIcon.Left, 100, new string('é', 100)));
        var roadLength = payload[6];
        Assert.True(roadLength <= NaviLiteTurn.MaxTextBytes);
        Assert.Equal(0, roadLength % 2);
    }

    [Theory]
    [InlineData("006-B4160-03", false, true)]
    [InlineData("006-B4920-01", false, true)]
    [InlineData("006-B3952-02", true, false)]
    [InlineData(null, true, true)]
    [InlineData("something else", true, true)]
    public void TheDashboardModelDecidesWhichMessageIsSent(string? part, bool next, bool info) =>
        Assert.Equal((next, info), NaviLiteTurnModels.For(part));
}
