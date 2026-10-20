using CCU_Emulator.Emulation;
using CCU_Emulator.Protocol;
using CCU_Emulator.Tests.Support;
using CCU_Emulator.Transport;
using Xunit;
using static CCU_Emulator.Tests.Support.Bytes;

namespace CCU_Emulator.Tests.Emulation;

public class CcuSessionCoreTests
{
    private readonly SessionFixture _f = new();
    private CcuState State => _f.Session.State;

    [Fact]
    public void AuthSucceedsWithTheConfiguredIdentity()
    {
        _f.Settings.Behavior.SendLibVersion = false;
        _f.Settings.Behavior.SendLanguageRequest = false;
        _f.Settings.Behavior.SendWeatherRequest = false;
        _f.Settings.Behavior.SendDeviceTimeRequest = false;
        _f.Settings.Behavior.SendHeadsetStateRequest = false;
        _f.Settings.Behavior.SendVolumeControllableRequest = false;
        _f.Settings.Behavior.SendCellSignalRequest = false;
        _f.ConnectAndAuthenticate();

        Assert.True(State.BleConnected);
        Assert.True(State.Authenticated);
        Assert.Equal([H("0102 01")], _f.Transport.MainRx);
        // Status: active(reqId 1) first, completion last, like the real CCU.
        Assert.Equal([H("0101"), H("0000")], _f.Transport.DdtStatus);
    }

    [Theory]
    [InlineData("01234567890123", "999999")]
    [InlineData("99999999999999", "012345")]
    public void AuthFailsWithWrongCredentials(string ccuId, string passKey)
    {
        _f.Phone.Connect();
        _f.Phone.Authenticate(ccuId, passKey);
        Assert.False(State.Authenticated);
        Assert.Equal([H("0102 00")], _f.Transport.MainRx);
    }

    [Fact]
    public void ForcedAuthFailureRejectsCorrectCredentials()
    {
        _f.Settings.Behavior.ForceAuthFailure = true;
        _f.ConnectAndAuthenticate();
        Assert.False(State.Authenticated);
        Assert.Equal(H("0102 00"), Assert.Single(_f.Transport.MainRx));
    }

    [Fact]
    public void IdentifyRecordsTheAppId()
    {
        _f.Phone.WriteSimple(H("0002 01 00001234"));
        Assert.Equal(0x1234, State.AppId);
    }

    [Fact]
    public void MusicOverDdtUpdatesState()
    {
        _f.Phone.SendDdt(Cat(H("0116"), Str8("Test Artist"), Str8("Test Album"), Str8("Test Song"), H("0000D2 02 000A 00000F")));
        Assert.Equal("Test Artist", State.MusicArtist);
        Assert.Equal("Test Album", State.MusicAlbum);
        Assert.Equal("Test Song", State.MusicSong);
        Assert.Equal(210, State.MusicDurationSeconds);
        Assert.Equal(15, State.MusicElapsedSeconds);
        Assert.Equal(PlaybackState.Playing, State.MusicPlayback);
        Assert.Contains(_f.Session.Log.Entries, e => e.Name == "MusicMetaData" && e.Channel == "DDT");
    }

    [Fact]
    public void WeatherSlotsAreUpsertedAndSortedBySlot()
    {
        _f.Phone.WriteSimple(Cat(H("0004 01"), Ascii("Berlin")));
        _f.Phone.WriteSimple(H("0005 01 06 03 0064 0078 0050 04"));   // After1Hour, Rain
        _f.Phone.WriteSimple(H("0005 02 00 00 00FA 0118 00C8 02"));   // Current, Sunny
        _f.Phone.WriteSimple(H("0005 03 00 14 0105 0118 00C8 00"));   // Current again, Cloudy 26.1

        Assert.Equal("Berlin", State.WeatherLocation);
        Assert.Equal([WeatherSlot.Current, WeatherSlot.After1Hour], State.Weather.Select(w => w.Slot).ToArray());
        Assert.Equal(WeatherType.Cloudy, State.Weather[0].Type);
        Assert.Equal(26.1, State.Weather[0].Temperature, 1);
    }

    [Fact]
    public void NotificationsAreAddedReplacedAndRemoved()
    {
        byte[] Notification(int id, string title)
        {
            var proto = Cat(new PacketWriter().U8(0x18).U8(id).ToArray(), H("5A"), Str8(title));
            return Cat(new PacketWriter().U16(0x012F).U24(proto.Length).ToArray(), proto);
        }

        _f.Phone.SendDdt(Notification(1, "First"));
        _f.Phone.SendDdt(Notification(2, "Second"));
        Assert.Equal(["Second", "First"], State.Notifications.Select(n => n.Title).ToArray());
        Assert.Equal("Second", State.LatestNotification!.Title);

        _f.Phone.SendDdt(Notification(1, "First (edited)"));
        Assert.Equal(["First (edited)", "Second"], State.Notifications.Select(n => n.Title).ToArray());

        _f.Phone.WriteSimple(H("0012 05 00000001"));   // RemoveNotification(1)
        Assert.Equal(["Second"], State.Notifications.Select(n => n.Title).ToArray());
    }

    [Fact]
    public void CallLifecycle()
    {
        _f.Phone.SendDdt(Cat(H("0118"), Str8("Alice"), Str8("+15551234"), H("00")));
        Assert.True(State.CallRinging);
        Assert.Equal("Alice", State.CallerName);
        Assert.Equal("+15551234", State.CallerNumber);

        _f.Phone.WriteSimple(H("0134 01"));
        Assert.True(State.CallActive);
        Assert.False(State.CallRinging);

        _f.Phone.WriteSimple(H("0134 02"));
        Assert.False(State.CallActive);

        _f.Phone.SendDdt(Cat(H("0118"), Str8("Bob"), Str8("555"), H("01")));
        Assert.Equal(1, State.MissedCalls);
        Assert.False(State.CallRinging);
    }

    [Fact]
    public void PhoneStateMessagesUpdateState()
    {
        _f.Phone.WriteSimple(H("013C E4 B2 C8 47"));
        Assert.Equal(100, State.Battery);
        Assert.True(State.Charging);
        Assert.Equal(CellSignal.Lv3, State.Signal);
        Assert.True(State.HeadsetConnected);

        _f.Phone.WriteSimple(H("011A 02"));
        Assert.Equal(ThermalState.High, State.Thermal);
        _f.Phone.WriteSimple(H("0133 05"));
        Assert.Equal(CellSignal.Lv4, State.Signal);
        _f.Phone.WriteSimple(H("011E 21 00"));
        Assert.Equal(33, State.PhoneVolume);
        _f.Phone.WriteSimple(H("011E 40 01"));
        Assert.Equal(64, State.MediaVolume);
    }

    // Review Focus 1: garbage never escapes the session
    [Theory]
    [InlineData("0116 40 61")]          // string length past the end
    [InlineData("0101 3031")]           // truncated auth
    [InlineData("0235 00000001")]       // truncated VPB chunk
    [InlineData("7777 AABB")]           // unknown command
    [InlineData("00")]                  // shorter than a command code
    public void GarbageIsLoggedAndIgnored(string hex)
    {
        _f.Phone.WriteSimple(H(hex));
        Assert.Contains(_f.Session.Log.Entries, e => e.Direction == Direction.Rx);
    }

    [Fact]
    public void DisconnectResetsConnectionStateAndDropsAPartialDdtSession()
    {
        _f.ConnectAndAuthenticate();
        Assert.True(State.Authenticated);

        // Start a DDT session and never finish it.
        _f.Transport.RaiseData(CcuChannel.DdtControl, H("09 000004"));
        _f.Transport.RaiseBle(false);
        Assert.False(State.BleConnected);
        Assert.False(State.Authenticated);

        // After reconnecting, a fresh session works normally.
        _f.Phone.Connect();
        _f.Phone.SendDdt(Cat(H("0116"), Str8("A"), Str8("B"), Str8("C"), H("000000 01 000A 000000")));
        Assert.Equal("C", State.MusicSong);
    }

    [Fact]
    public void TransportEventsAreMarshalledThroughThePostDelegate()
    {
        var queue = new Queue<Action>();
        var transport = new FakeTransport();
        var session = new CcuSession(transport, new CcuSettings(), queue.Enqueue);

        transport.RaiseBle(true);
        Assert.False(session.State.BleConnected);   // nothing happened yet
        while (queue.Count > 0) queue.Dequeue()();
        Assert.True(session.State.BleConnected);
    }
}
