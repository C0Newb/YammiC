namespace CCU_Emulator.Protocol;

/// <summary>Decodes a complete phone→CCU message (<c>[cmd 2B][payload]</c>). Throws only <see cref="MalformedPacketException"/>.</summary>
public static class PhoneMessageDecoder
{
    // Seconds-since-epoch range DateTimeOffset accepts.
    private const long MinUnixSeconds = -62135596800;
    private const long MaxUnixSeconds = 253402300799;

    private static string Str8(PacketReader r) => r.Utf8(r.U8());

    public static PhoneMessage Decode(byte[] data)
    {
        var r = new PacketReader(data);
        var cmd = r.U16();
        switch ((CommandCode)cmd)
        {
            case CommandCode.IdentifyApp:
                return new IdentifyAppMessage(r.U8(), r.I32());

            case CommandCode.AuthenticationRequest:
                return new AuthenticationMessage(r.Utf8(14), r.Utf8(6));

            case CommandCode.LanguageResponse:
                return new LanguageResponseMessage(r.U8(), r.U8());

            case CommandCode.WeatherLocation:
            {
                var id = r.U8();
                return new WeatherLocationMessage(id, r.Utf8(r.Remaining).TrimEnd('\0'));
            }

            case CommandCode.WeatherInfo:
            {
                var id = r.U8();
                var slot = (WeatherSlot)r.U8();
                var type = (WeatherType)r.U8();
                var temp = r.I16() / 10.0;
                var max = r.I16() / 10.0;
                var min = r.I16() / 10.0;
                var precipitation = r.U8() * 5;
                return new WeatherInfoMessage(id, slot, type, temp, max, min, precipitation);
            }

            case CommandCode.DatetimeResponse:
            {
                var id = r.U8();
                var seconds = r.I64();
                if (seconds is < MinUnixSeconds or > MaxUnixSeconds)
                    throw new MalformedPacketException($"device time out of range: {seconds}");
                return new DeviceTimeMessage(id, DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime);
            }

            case CommandCode.MusicMetaData:
            {
                var artist = Str8(r);
                var album = Str8(r);
                var song = Str8(r);
                var duration = r.U24();
                var state = (PlaybackState)r.U8();
                var rate = r.U16() / 10.0;
                var elapsed = r.U24();
                return new MusicMetaDataMessage(artist, album, song, duration, state, rate, elapsed);
            }

            case CommandCode.NotificationDataV2:
                return NotificationProto.Decode(r.Bytes(r.U24()));

            case CommandCode.AddNotification:
            case CommandCode.UpdateNotification:
            case CommandCode.RemoveNotification:
                return new NotificationLifecycleMessage((CommandCode)cmd, r.U8(), r.I32());

            case CommandCode.NotificationCategory:
            {
                var flags = new bool[12];
                for (var i = 0; i < flags.Length && r.Remaining > 0; i++) flags[i] = r.U8() != 0;
                return new NotificationCategoryMessage(flags);
            }

            case CommandCode.IncomingCallInformation:
            {
                var name = Str8(r);
                var number = Str8(r);
                var missed = r.Remaining > 0 && r.U8() != 0;
                return new IncomingCallInfoMessage(name, number, missed);
            }

            case CommandCode.CallChange:
                return new CallChangeMessage(r.U8() == 1);

            case CommandCode.PhoneStateCombined:
            {
                var b0 = r.U8();
                var b1 = r.U8();
                var b2 = r.U8();
                var b3 = r.U8();
                return new PhoneStateMessage(
                    Battery: b0 & 0x7F, Charging: (b0 & 0x80) != 0,
                    PhoneVolume: b1 & 0x7F, MediaVolume: b2 & 0x7F,
                    Signal: (CellSignal)((b3 >> 4) & 0x0F), Thermal: (ThermalState)((b3 >> 2) & 0x03),
                    HeadsetConnected: (b3 & 0x02) != 0, OnHook: (b3 & 0x01) != 0);
            }

            case CommandCode.PhoneThermalState:
                return new ThermalStateMessage((ThermalState)Math.Min(r.U8(), (byte)2));

            case CommandCode.CellSignalResponse:
                return new CellSignalResponseMessage((CellSignal)r.U8());

            case CommandCode.HeadsetStateResponse:
                return new HeadsetStateResponseMessage(r.U8() != 0);

            case CommandCode.VolumeControllableResponse:
                return new VolumeControllableResponseMessage(r.U8() != 0);

            case CommandCode.VolumeLevel:
            {
                var percent = r.U8(); // YammiC order: [cmd][volume][type]
                var type = (VolumeType)r.U8();
                return new VolumeLevelMessage(type, percent);
            }

            case CommandCode.PongResponse:
                return new PongMessage(r.Remaining > 0 ? r.U8() : (byte)0);

            case CommandCode.VpbRequest:
                return new VpbRequestMessage();

            case CommandCode.VpbClear:
                return new VpbClearMessage();

            case CommandCode.VpbGraphicResponseNg:
                return new VpbGraphicResponseNgMessage();

            case CommandCode.VpbSendGraphic:
            {
                var contentId = r.I32();
                var index = r.U8();
                var chunkSize = r.I32();
                var totalIndex = r.U8();
                var totalSize = r.I32();
                var crc = r.U32();
                return new VpbChunkMessage(contentId, index, totalIndex, totalSize, crc, r.Bytes(chunkSize));
            }

            default:
                return new UnknownMessage(cmd, data);
        }
    }
}
