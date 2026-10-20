namespace CCU_Emulator.Protocol;

public enum MusicAction : byte { Next = 1, Previous = 2, Play = 3, Pause = 4 }
public enum VolumeAction : byte { PhoneUp = 1, PhoneDown = 2, MuteMicrophone = 3, MuteSpeaker = 4, MediaUp = 5, MediaDown = 6 }
public enum CallAction : byte { Reject = 0, Answer = 1, HangUp = 2 }
public enum ChunkResponse : byte { Ok = 1, Retry = 2, Abort = 3 }

/// <summary>Builders for packets the CCU sends to the phone (main RX notifications, or SPP payloads).</summary>
public static class CcuPackets
{
    private static byte[] Cmd(CommandCode code, params byte[] rest)
        => new PacketWriter().U16((ushort)code).Bytes(rest).ToArray();

    public static byte[] AuthenticationResponse(bool ok) => Cmd(CommandCode.AuthenticationResponse, (byte)(ok ? 1 : 0));

    /// <summary>Exactly what a real CCU sends right after auth (SDK version bytes).</summary>
    public static byte[] LibVersion() => Cmd(CommandCode.LibVersion, 0x03, 0x01, 0x01);

    public static byte[] LanguageRequest() => Cmd(CommandCode.LanguageRequest);
    public static byte[] DeviceTimeRequest() => Cmd(CommandCode.VehicleSettingRequest);
    public static byte[] HeadsetStateRequest() => Cmd(CommandCode.HeadsetStateRequest);
    public static byte[] VolumeControllableRequest() => Cmd(CommandCode.VolumeControllableRequest);
    public static byte[] CellSignalRequest() => Cmd(CommandCode.CellSignalRequest);

    public static byte[] WeatherRequestCurrent() => Cmd(CommandCode.WeatherRequest, 0x01);

    public static byte[] WeatherRequestPoint(int latitudeE6, int longitudeE6)
        => new PacketWriter().U16((ushort)CommandCode.WeatherRequest).U8(0x02).I32(latitudeE6).I32(longitudeE6).ToArray();

    /// <summary>Keepalive: <c>01 35 &lt;seq&gt;</c>, the counter starting at 0 (as observed).</summary>
    public static byte[] Ping(byte seq) => Cmd(CommandCode.PingRequest, seq);

    public static byte[] MusicControl(MusicAction a) => Cmd(CommandCode.MusicControl, (byte)a);
    public static byte[] VolumeControl(VolumeAction a) => Cmd(CommandCode.VolumeControl, (byte)a);
    public static byte[] CallControl(CallAction a) => Cmd(CommandCode.IncomingCallControl, (byte)a);

    public static byte[] NotificationRemoved(int id)
        => new PacketWriter().U16((ushort)CommandCode.NotificationRemoved).I32(id).ToArray();

    public static byte[] VpbGraphicRequest() => Cmd(CommandCode.VpbGraphicRequest);
    public static byte[] VpbResponseNg() => Cmd(CommandCode.VpbResponseNg);
    public static byte[] VpbClearOk() => Cmd(CommandCode.VpbClearOk);
    public static byte[] VpbClearNg() => Cmd(CommandCode.VpbClearNg);

    public static byte[] VpbChunkAck(int contentId, byte index, ChunkResponse response)
        => new PacketWriter().U16((ushort)CommandCode.VpbSendGraphicResponse).I32(contentId).U8(index).U8((byte)response).ToArray();

    /// <summary>First SPP frame payload: <c>[0x013A][ccuId 14B][passKey 6B]</c>.</summary>
    public static byte[] BleConnectInformation(string ccuId, string passKey)
        => new PacketWriter().U16((ushort)CommandCode.BleConnectInformation).Utf8(ccuId).Utf8(passKey).ToArray();
}
