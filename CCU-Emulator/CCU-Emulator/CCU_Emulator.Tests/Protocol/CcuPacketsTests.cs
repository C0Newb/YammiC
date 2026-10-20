using CCU_Emulator.Protocol;
using Xunit;
using static CCU_Emulator.Tests.Support.Bytes;

namespace CCU_Emulator.Tests.Protocol;

public class CcuPacketsTests
{
    [Fact]
    public void ObservedFromARealCcu()
    {
        // Bytes seen in .local/YammiC Device Logs (NOTIFY on the main RX characteristic).
        Assert.Equal(H("01 02 01"), CcuPackets.AuthenticationResponse(true));
        Assert.Equal(H("01 02 00"), CcuPackets.AuthenticationResponse(false));
        Assert.Equal(H("00 01 03 01 01"), CcuPackets.LibVersion());
        Assert.Equal(H("00 0B"), CcuPackets.LanguageRequest());
        Assert.Equal(H("00 09"), CcuPackets.DeviceTimeRequest());
        Assert.Equal(H("01 29"), CcuPackets.VolumeControllableRequest());
        Assert.Equal(H("01 35 00"), CcuPackets.Ping(0));
        Assert.Equal(H("01 35 04"), CcuPackets.Ping(4));
        Assert.Equal(H("02 32"), CcuPackets.VpbResponseNg());
    }

    [Fact]
    public void Requests()
    {
        Assert.Equal(H("00 03 01"), CcuPackets.WeatherRequestCurrent());
        Assert.Equal(H("00 03 02 0001E240 FFFE1DC0"), CcuPackets.WeatherRequestPoint(123456, -123456));
        Assert.Equal(H("01 1B"), CcuPackets.HeadsetStateRequest());
        Assert.Equal(H("01 32"), CcuPackets.CellSignalRequest());
    }

    [Fact]
    public void ButtonCommands()
    {
        Assert.Equal(H("01 17 01"), CcuPackets.MusicControl(MusicAction.Next));
        Assert.Equal(H("01 17 02"), CcuPackets.MusicControl(MusicAction.Previous));
        Assert.Equal(H("01 17 03"), CcuPackets.MusicControl(MusicAction.Play));
        Assert.Equal(H("01 17 04"), CcuPackets.MusicControl(MusicAction.Pause));
        Assert.Equal(H("01 1D 05"), CcuPackets.VolumeControl(VolumeAction.MediaUp));
        Assert.Equal(H("01 1D 02"), CcuPackets.VolumeControl(VolumeAction.PhoneDown));
        Assert.Equal(H("01 19 00"), CcuPackets.CallControl(CallAction.Reject));
        Assert.Equal(H("01 19 01"), CcuPackets.CallControl(CallAction.Answer));
        Assert.Equal(H("01 19 02"), CcuPackets.CallControl(CallAction.HangUp));
        Assert.Equal(H("00 13 00000007"), CcuPackets.NotificationRemoved(7));
    }

    [Fact]
    public void VpbPackets()
    {
        Assert.Equal(H("02 33"), CcuPackets.VpbGraphicRequest());
        Assert.Equal(H("02 36 00004E85 03 01"), CcuPackets.VpbChunkAck(20101, 3, ChunkResponse.Ok));
        Assert.Equal(H("02 36 00004E85 00 02"), CcuPackets.VpbChunkAck(20101, 0, ChunkResponse.Retry));
        Assert.Equal(H("02 38"), CcuPackets.VpbClearOk());
        Assert.Equal(H("02 39"), CcuPackets.VpbClearNg());
    }

    [Fact]
    public void BleConnectInformationCarriesCredentials()
        => Assert.Equal(Cat(H("01 3A"), Ascii("01234567890123"), Ascii("012345")),
            CcuPackets.BleConnectInformation("01234567890123", "012345"));
}
