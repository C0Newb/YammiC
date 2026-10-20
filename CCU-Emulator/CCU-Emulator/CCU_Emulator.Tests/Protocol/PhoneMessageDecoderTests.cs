using CCU_Emulator.Protocol;
using Xunit;
using static CCU_Emulator.Tests.Support.Bytes;

namespace CCU_Emulator.Tests.Protocol;

public class PhoneMessageDecoderTests
{
    private static T Decode<T>(byte[] data) where T : PhoneMessage => Assert.IsType<T>(PhoneMessageDecoder.Decode(data));

    [Fact]
    public void Auth()
    {
        var m = Decode<AuthenticationMessage>(Cat(H("0101"), Ascii("01234567890123"), Ascii("012345")));
        Assert.Equal("01234567890123", m.CcuId);
        Assert.Equal("012345", m.PassKey);
    }

    [Fact]
    public void Identify()
    {
        var m = Decode<IdentifyAppMessage>(H("0002 01 00001234"));
        Assert.Equal(1, m.RequestId);
        Assert.Equal(0x1234, m.AppId);
    }

    [Fact]
    public void Music_UsesSecondsAndLengthPrefixedStrings()
    {
        var m = Decode<MusicMetaDataMessage>(Cat(H("0116"), Str8("Test Artist"), Str8("Test Album"), Str8("Test Song"), H("0000D2 02 000A 00000F")));
        Assert.Equal("Test Artist", m.Artist);
        Assert.Equal("Test Album", m.Album);
        Assert.Equal("Test Song", m.Song);
        Assert.Equal(210, m.DurationSeconds);
        Assert.Equal(PlaybackState.Playing, m.State);
        Assert.Equal(1.0, m.Rate);
        Assert.Equal(15, m.ElapsedSeconds);
    }

    [Fact]
    public void Music_MultibyteAndEmptyStrings()
    {
        var m = Decode<MusicMetaDataMessage>(Cat(H("0116"), Str8("Björk – Jóga"), Str8(""), Str8("日本語"), H("000000 01 000A 000000")));
        Assert.Equal("Björk – Jóga", m.Artist);
        Assert.Equal("", m.Album);
        Assert.Equal("日本語", m.Song);
        Assert.Equal(PlaybackState.Paused, m.State);
    }

    [Fact]
    public void Music_StringLengthBeyondPacketIsMalformed()
        => Assert.Throws<MalformedPacketException>(() => PhoneMessageDecoder.Decode(Cat(H("0116 40"), Ascii("abc"))));

    [Fact]
    public void WeatherInfo()
    {
        var m = Decode<WeatherInfoMessage>(H("0005 07 00 00 00FA 0118 00C8 02"));
        Assert.Equal(WeatherSlot.Current, m.Slot);
        Assert.Equal(WeatherType.Sunny, m.Type);
        Assert.Equal(25.0, m.Temperature);
        Assert.Equal(28.0, m.MaxTemperature);
        Assert.Equal(20.0, m.MinTemperature);
        Assert.Equal(10, m.PrecipitationPercent); // wire carries percent / 5
    }

    [Fact]
    public void WeatherInfo_NegativeTemperature()
        => Assert.Equal(-5.5, Decode<WeatherInfoMessage>(H("0005 01 06 14 FFC9 0000 FF9C 00")).Temperature);

    [Fact]
    public void WeatherLocation()
        => Assert.Equal("Berlin", Decode<WeatherLocationMessage>(Cat(H("0004 01"), Ascii("Berlin"))).Name);

    [Fact]
    public void DeviceTime_SecondsSinceEpoch()
    {
        var secs = new DateTimeOffset(2026, 10, 10, 12, 34, 56, TimeSpan.Zero).ToUnixTimeSeconds();
        var m = Decode<DeviceTimeMessage>(new PacketWriter().U16(0x000A).U8(3).I32(0).I32((int)secs).ToArray());
        Assert.Equal(new DateTime(2026, 10, 10, 12, 34, 56), m.Time);
    }

    [Fact]
    public void DeviceTime_OutOfRangeIsMalformed()
        => Assert.Throws<MalformedPacketException>(() => PhoneMessageDecoder.Decode(H("000A 03 7FFFFFFFFFFFFFFF")));

    [Fact]
    public void NotificationV2()
    {
        var proto = Cat(H("18 01 20 00 28 E9 0F 30 06 38 1D 40 00 48 00 50 00 5A 04"), Ascii("Test"), H("6A 09"), Ascii("It works!"));
        var packet = Cat(new PacketWriter().U16(0x012F).U24(proto.Length).ToArray(), proto);
        var m = Decode<NotificationMessage>(packet);
        Assert.Equal(1, m.Id);
        Assert.Equal(0, m.Category);
        Assert.Equal(new DateTime(2025, 6, 29, 0, 0, 0), m.Timestamp);
        Assert.Equal("Test", m.Title);
        Assert.Equal("", m.SubTitle);
        Assert.Equal("It works!", m.Detail);
    }

    [Fact]
    public void NotificationV2_UnsupportedWireTypeIsMalformed()
        => Assert.Throws<MalformedPacketException>(() => PhoneMessageDecoder.Decode(H("012F 000002 1D 00")));

    [Theory]
    [InlineData("0010", CommandCode.AddNotification)]
    [InlineData("0011", CommandCode.UpdateNotification)]
    [InlineData("0012", CommandCode.RemoveNotification)]
    public void NotificationLifecycle(string cmd, CommandCode op)
    {
        var m = Decode<NotificationLifecycleMessage>(H(cmd + " 09 00000007"));
        Assert.Equal(op, m.Operation);
        Assert.Equal(7, m.Id);
    }

    [Fact]
    public void NotificationCategory()
    {
        var m = Decode<NotificationCategoryMessage>(H("0128 00 01 00 00 01 00 00 00 00 00 00 01"));
        Assert.Equal(12, m.Enabled.Length);
        Assert.True(m.Enabled[1]);
        Assert.True(m.Enabled[4]);
        Assert.True(m.Enabled[11]);
        Assert.False(m.Enabled[0]);
    }

    [Fact]
    public void IncomingCallInfo()
    {
        var m = Decode<IncomingCallInfoMessage>(Cat(H("0118"), Str8("Alice"), Str8("+15551234"), H("00")));
        Assert.Equal("Alice", m.Name);
        Assert.Equal("+15551234", m.Number);
        Assert.False(m.Missed);
        Assert.True(Decode<IncomingCallInfoMessage>(Cat(H("0118"), Str8(""), Str8(""), H("01"))).Missed);
    }

    [Theory]
    [InlineData("0134 01", true)]
    [InlineData("0134 02", false)]
    public void CallChange(string hex, bool started) => Assert.Equal(started, Decode<CallChangeMessage>(H(hex)).Started);

    [Fact]
    public void PhoneStateCombined()
    {
        var m = Decode<PhoneStateMessage>(H("013C E4 B2 C8 47"));
        Assert.Equal(100, m.Battery);
        Assert.True(m.Charging);
        Assert.Equal(50, m.PhoneVolume);
        Assert.Equal(72, m.MediaVolume);
        Assert.Equal(CellSignal.Lv3, m.Signal);
        Assert.Equal(ThermalState.Middle, m.Thermal);
        Assert.True(m.HeadsetConnected);
        Assert.True(m.OnHook);
    }

    [Fact]
    public void SmallStateMessages()
    {
        Assert.Equal(ThermalState.Middle, Decode<ThermalStateMessage>(H("011A 01")).State);
        Assert.Equal(ThermalState.High, Decode<ThermalStateMessage>(H("011A 09")).State);
        Assert.Equal(CellSignal.Lv2, Decode<CellSignalResponseMessage>(H("0133 03")).Signal);
        Assert.True(Decode<HeadsetStateResponseMessage>(H("011C 01")).Connected);
        Assert.True(Decode<VolumeControllableResponseMessage>(H("012A 01")).CanControl);
        var v = Decode<VolumeLevelMessage>(H("011E 32 01"));
        Assert.Equal(50, v.Percent);
        Assert.Equal(VolumeType.Media, v.Type);
        Assert.Equal(5, Decode<PongMessage>(H("0136 05")).RequestId);
    }

    [Fact]
    public void VpbMessages()
    {
        Assert.IsType<VpbRequestMessage>(PhoneMessageDecoder.Decode(H("0231")));
        Assert.IsType<VpbClearMessage>(PhoneMessageDecoder.Decode(H("0237")));
        Assert.IsType<VpbGraphicResponseNgMessage>(PhoneMessageDecoder.Decode(H("0234")));
    }

    [Fact]
    public void VpbChunk_ChecksCrc()
    {
        var data = H("FFD8FFE0");
        var good = ChunkPacket(data, VpbCrc32.Compute(data));
        var bad = ChunkPacket(data, 0x12345678);
        var m = Decode<VpbChunkMessage>(good);
        Assert.Equal(20101, m.ContentId);
        Assert.Equal(0, m.Index);
        Assert.Equal(2, m.TotalIndex);
        Assert.Equal(13000, m.TotalSize);
        Assert.True(m.CrcOk);
        Assert.False(Decode<VpbChunkMessage>(bad).CrcOk);
    }

    private static byte[] ChunkPacket(byte[] data, uint crc)
        => new PacketWriter().U16(0x0235).I32(20101).U8(0).I32(data.Length).U8(2).I32(13000).U32(crc).Bytes(data).ToArray();

    [Fact]
    public void UnknownCommandIsReportedNotThrown()
    {
        var m = Decode<UnknownMessage>(H("7777 AABB"));
        Assert.Equal(0x7777, m.Command);
    }

    [Fact]
    public void TooShortForACommandCodeIsMalformed()
    {
        Assert.Throws<MalformedPacketException>(() => PhoneMessageDecoder.Decode([]));
        Assert.Throws<MalformedPacketException>(() => PhoneMessageDecoder.Decode(H("01")));
    }

    // Review Focus 1: no truncation of any known command may throw anything but MalformedPacketException.
    [Fact]
    public void EveryTruncationOfEveryKnownPacketFailsCleanly()
    {
        var data = H("FFD8FFE0");
        var proto = Cat(H("18 01 20 00 5A 04"), Ascii("Test"));
        var samples = new[]
        {
            Cat(H("0101"), Ascii("01234567890123"), Ascii("012345")),
            H("0002 01 00001234"), H("000C 05 00"), Cat(H("0004 01"), Ascii("Berlin")),
            H("0005 07 00 00 00FA 0118 00C8 02"), H("000A 03 0000000069E8B5F0"),
            Cat(H("0116"), Str8("A"), Str8("B"), Str8("C"), H("0000D2 02 000A 00000F")),
            Cat(new PacketWriter().U16(0x012F).U24(proto.Length).ToArray(), proto),
            H("0010 09 00000007"), H("0128 00 01 00 00 01 00 00 00 00 00 00 01"),
            Cat(H("0118"), Str8("Alice"), Str8("555"), H("00")), H("0134 01"), H("013C E4 B2 C8 47"),
            H("011A 01"), H("0133 03"), H("011C 01"), H("012A 01"), H("011E 32 01"), H("0136 05"),
            ChunkPacket(data, VpbCrc32.Compute(data)),
        };
        foreach (var sample in samples)
        {
            for (var len = 0; len < sample.Length; len++)
            {
                var prefix = sample[..len];
                try { PhoneMessageDecoder.Decode(prefix); }
                catch (MalformedPacketException) { }
            }
            PhoneMessageDecoder.Decode(sample); // the full sample must decode
        }
    }
}
