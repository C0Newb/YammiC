using System.Text;

namespace CCU_Emulator.Protocol;

public enum PlaybackState : byte { Unknown = 0, Paused = 1, Playing = 2, Rewinding = 3, FastForwarding = 4, Unavailable = 0xFF }

/// <summary>Wire values as observed with YammiC (they differ from the ordering in docs/mvp-music-and-weather.md).</summary>
public enum WeatherSlot : byte
{
    Current = 0, After1Day = 1, After2Days = 2, After3Days = 3, After4Days = 4, After5Days = 5,
    After1Hour = 6, After2Hours = 7, After3Hours = 8, After6Days = 9,
}

public enum WeatherType : byte
{
    Sunny = 0, PartlyCloudyDay, CloudyDay, Rain, Snow, Wind, RainStormMix, RainSnowMix, Fog, Smog, Hail,
    ScatteredShowersDay, ScatteredStormDay, NoData, RainLight, RainHeavy, SnowLight, SnowHeavy,
    RainSnowMixLight, RainSnowMixHeavy, Cloudy, Dust, Tornado, Clear, PartlyCloudyNight, CloudyNight,
    ScatteredShowersNight, ScatteredStormNight,
}

public enum CellSignal : byte { NoConnection = 0, NoSignal = 1, Lv1 = 2, Lv2 = 3, Lv3 = 4, Lv4 = 5 }
public enum ThermalState : byte { Low = 0, Middle = 1, High = 2 }
public enum VolumeType : byte { Phone = 0, Media = 1 }

public abstract record PhoneMessage;

public sealed record IdentifyAppMessage(byte RequestId, int AppId) : PhoneMessage;
public sealed record AuthenticationMessage(string CcuId, string PassKey) : PhoneMessage;
public sealed record LanguageResponseMessage(byte RequestId, byte Language) : PhoneMessage;
public sealed record WeatherLocationMessage(byte RequestId, string Name) : PhoneMessage;

public sealed record WeatherInfoMessage(
    byte RequestId, WeatherSlot Slot, WeatherType Type,
    double Temperature, double MaxTemperature, double MinTemperature, int PrecipitationPercent) : PhoneMessage;

/// <summary>Phone's local wall-clock time (the wire value already includes the timezone offset).</summary>
public sealed record DeviceTimeMessage(byte RequestId, DateTime Time) : PhoneMessage;

public sealed record MusicMetaDataMessage(
    string Artist, string Album, string Song, int DurationSeconds, PlaybackState State, double Rate, int ElapsedSeconds) : PhoneMessage;

public sealed record NotificationMessage(
    int Id, int Category, DateTime? Timestamp, string Title, string SubTitle, string Detail) : PhoneMessage;

public sealed record NotificationLifecycleMessage(CommandCode Operation, byte RequestId, int Id) : PhoneMessage;
public sealed record NotificationCategoryMessage(bool[] Enabled) : PhoneMessage;
public sealed record IncomingCallInfoMessage(string Name, string Number, bool Missed) : PhoneMessage;
public sealed record CallChangeMessage(bool Started) : PhoneMessage;

public sealed record PhoneStateMessage(
    int Battery, bool Charging, int PhoneVolume, int MediaVolume,
    CellSignal Signal, ThermalState Thermal, bool HeadsetConnected, bool OnHook) : PhoneMessage;

public sealed record ThermalStateMessage(ThermalState State) : PhoneMessage;
public sealed record CellSignalResponseMessage(CellSignal Signal) : PhoneMessage;
public sealed record HeadsetStateResponseMessage(bool Connected) : PhoneMessage;
public sealed record VolumeControllableResponseMessage(bool CanControl) : PhoneMessage;
public sealed record VolumeLevelMessage(VolumeType Type, int Percent) : PhoneMessage;
public sealed record PongMessage(byte RequestId) : PhoneMessage;

public sealed record VpbRequestMessage : PhoneMessage;
public sealed record VpbClearMessage : PhoneMessage;
public sealed record VpbGraphicResponseNgMessage : PhoneMessage;

public sealed record VpbChunkMessage(int ContentId, byte Index, byte TotalIndex, int TotalSize, uint Crc, byte[] Data) : PhoneMessage
{
    public bool CrcOk => VpbCrc32.Compute(Data) == Crc;

    protected override bool PrintMembers(StringBuilder b)
    {
        b.Append($"ContentId = {ContentId}, Index = {Index}/{TotalIndex}, TotalSize = {TotalSize}, DataLength = {Data.Length}, CrcOk = {CrcOk}");
        return true;
    }
}

public sealed record UnknownMessage(int Command, byte[] Data) : PhoneMessage
{
    protected override bool PrintMembers(StringBuilder b)
    {
        b.Append($"Command = 0x{Command:X4}, Length = {Data.Length}");
        return true;
    }
}
