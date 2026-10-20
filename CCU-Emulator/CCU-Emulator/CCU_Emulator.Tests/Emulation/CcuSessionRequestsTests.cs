using CCU_Emulator.Tests.Support;
using Xunit;
using static CCU_Emulator.Tests.Support.Bytes;

namespace CCU_Emulator.Tests.Emulation;

public class CcuSessionRequestsTests
{
    private readonly SessionFixture _f = new();

    [Fact]
    public void AfterAuthTheCcuSendsItsStartupRequestsInOrder()
    {
        _f.ConnectAndAuthenticate();
        Assert.Equal(
            [
                H("0102 01"),      // auth ok
                H("0001 030101"),  // LibVersion
                H("000B"),         // LanguageRequest
                H("0009"),         // DeviceTimeRequest
                H("0129"),         // VolumeControllableRequest
                H("0003 01"),      // WeatherRequest (current)
                H("011B"),         // HeadsetStateRequest
                H("0132"),         // CellSignalRequest
            ],
            _f.Transport.MainRx);
    }

    [Fact]
    public void TogglesSuppressIndividualRequests()
    {
        var b = _f.Settings.Behavior;
        b.SendLibVersion = false;
        b.SendWeatherRequest = false;
        b.SendHeadsetStateRequest = false;
        b.SendCellSignalRequest = false;
        _f.ConnectAndAuthenticate();
        Assert.Equal([H("0102 01"), H("000B"), H("0009"), H("0129")], _f.Transport.MainRx);
    }

    [Fact]
    public void NoRequestsAreSentWhenAuthFails()
    {
        _f.Settings.Behavior.ForceAuthFailure = true;
        _f.ConnectAndAuthenticate();
        Assert.Equal([H("0102 00")], _f.Transport.MainRx);
    }

    [Fact]
    public void DeviceTimeRequestRepeatsEveryFiveSecondsUntilAnswered()
    {
        _f.ConnectAndAuthenticate();
        var baseline = _f.Transport.MainRx.Count;

        _f.Session.Tick(TimeSpan.FromSeconds(4));
        Assert.Equal(baseline, _f.Transport.MainRx.Count);
        _f.Session.Tick(TimeSpan.FromSeconds(1));
        Assert.Equal(H("0009"), _f.Transport.MainRx[^1]);
        Assert.Equal(baseline + 1, _f.Transport.MainRx.Count);

        _f.Phone.WriteSimple(H("000A 03 0000000069E8B5F0"));
        _f.Session.Tick(TimeSpan.FromSeconds(10));
        Assert.DoesNotContain(_f.Transport.MainRx.Skip(baseline + 1), p => p.SequenceEqual(H("0009")));
    }

    [Fact]
    public void PingCarriesAnIncrementingCounterAtTheConfiguredInterval()
    {
        _f.Settings.Behavior.SendDeviceTimeRequest = false;
        _f.ConnectAndAuthenticate();
        var pings = () => _f.Transport.MainRx.Where(p => p.Length == 3 && p[0] == 0x01 && p[1] == 0x35).ToList();

        _f.Session.Tick(TimeSpan.FromSeconds(9));
        Assert.Empty(pings());
        _f.Session.Tick(TimeSpan.FromSeconds(1));
        Assert.Equal([H("0135 00")], pings());
        _f.Session.Tick(TimeSpan.FromSeconds(10));
        Assert.Equal([H("0135 00"), H("0135 01")], pings());

        _f.Settings.Behavior.PingIntervalSeconds = 3;
        _f.Session.Tick(TimeSpan.FromSeconds(3));
        Assert.Equal(H("0135 02"), pings()[^1]);
    }

    [Theory]
    [InlineData(false, 10)]
    [InlineData(true, 0)]
    public void PingDisabledByToggleOrZeroInterval(bool sendPing, int interval)
    {
        _f.Settings.Behavior.SendDeviceTimeRequest = false;
        _f.Settings.Behavior.SendPing = sendPing;
        _f.Settings.Behavior.PingIntervalSeconds = interval;
        _f.ConnectAndAuthenticate();
        _f.Session.Tick(TimeSpan.FromMinutes(5));
        Assert.DoesNotContain(_f.Transport.MainRx, p => p.Length == 3 && p[0] == 0x01 && p[1] == 0x35);
    }

    [Fact]
    public void TickDoesNothingBeforeAuth()
    {
        _f.Phone.Connect();
        _f.Session.Tick(TimeSpan.FromMinutes(5));
        Assert.Empty(_f.Transport.MainRx);
    }
}
