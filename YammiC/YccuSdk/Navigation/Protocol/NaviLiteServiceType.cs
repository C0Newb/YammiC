using System.Text;

namespace CNCO.YccuSdk.Navigation.Protocol;

internal static class NaviLiteConstants
{
    /// <summary>
    /// NaviLite frameType for messages the phone sends to the bike.
    /// </summary>
    public const int FrameTypePhone = 6;

    /// <summary>
    /// NaviLite payloadDataType values.
    /// </summary>
    public const int PdtValue = 0;
    public const int PdtPointer = 1;
}

/// <summary>
/// NaviLite service-type identifiers used by the handshake and image channel.
/// </summary>
internal static class NaviLiteServiceType
{
    // handshake
    public const int EsnUpdate = 66;
    public const int EsnAck = 81;
    public const int AuthRequest = 33;
    public const int SecData = 83;
    public const int SecDataAck = 84;
    // image channel
    public const int Image = 0;
    public const int ImageAck = 80;
    // post-auth setup burst
    public const int NavStatus = 2;
    public const int DayNight = 31;
    public const int Home = 10;
    public const int Office = 11;
    public const int Gps = 13;
    public const int AppSetting = 12;
    public const int Zoom = 14;
    public const int Road = 3;
    public const int SpeedLimit = 17;
    // dash -> phone: fired when the rider taps "Go home" / "Go to work" on the CCU
    public const int ZoomInRequest = 51;
    public const int ZoomOutRequest = 52;
    public const int GoHomeRequest = 53;
    public const int GoOfficeRequest = 54;
    // dash -> phone: fired when the rider opens/leaves a content screen (e.g. Favorites,
    // Nearby gas stations) - payload is [contentType][0x00], contentType 3=favorites, 4=gas.
    public const int StartRouteRequest = 48;
    public const int StartContentUpdateRequest = 55;
    public const int StopContentUpdateRequest = 56;
    // phone -> dash: list-size replies to START_CONTENT_UPDATE_REQUEST. Payload confirmed from
    // StreetCross's native GetFavoritePoiUpdateMessage/GetGasPoiUpdateMessage
    // (libnaviliteprotocol.so): [itemCount][0x00].
    public const int FavPoiListUpdate = 7;
    public const int GasPoiListUpdate = 8;
    // phone -> dash: one per list item, sent after FAV_POI_LIST_UPDATE/GAS_POI_LIST_UPDATE.
    // Payload confirmed from StreetCross's native GetFavoritePoiDataUpdateMessage/
    // GetGasPoiDataUpdateMessage (libnaviliteprotocol.so) - see NaviLitePoiEntry.
    public const int FavPoiListDataUpdate = 98;
    public const int GasPoiListDataUpdate = 99;
}

/// <summary>
/// APP_START_CONTENT_UPDATE_REQUEST/APP_STOP_CONTENT_UPDATE_REQUEST contentType values.
/// </summary>
internal static class NaviLiteContentType
{
    public const int NaviImage = 1;
    public const int FavoriteList = 3;
    public const int StationsList = 4;
}

/// <summary>
/// FAV/GAS_POI_LIST_DATA_UPDATE's direction byte - relative to the vehicle, not compass.
/// </summary>
internal static class NaviLitePoiDirectionType
{
    public const byte Undefined = 0;
    public const byte Front = 1;
    public const byte FrontRight = 2;
    public const byte Right = 3;
    public const byte BackRight = 4;
    public const byte Back = 5;
    public const byte BackLeft = 6;
    public const byte Left = 7;
    public const byte FrontLeft = 8;
}

/// <summary>
/// One FAV_POI_LIST_DATA_UPDATE/GAS_POI_LIST_DATA_UPDATE entry.
///
/// Wire format reverse-engineered from StreetCross's native
/// GetFavoritePoiDataUpdateMessage(short, short, signed char, float, string, string) /
/// GetGasPoiDataUpdateMessage(...) in libnaviliteprotocol.so - every field (including the
/// `float` distance) collapses to a single byte in the actual packer, so distance is quantized,
/// not a full IEEE-754 float:
///
/// `[index][unknown][direction][len(name)][len(road)][distance][road bytes][name bytes]`
///
/// The two length-prefix bytes are written in (name, road) order, but the string bytes
/// themselves are appended in (road, name) order - a genuine quirk of the native packer, not a
/// transcription error; confirmed by reading the disassembly's push order directly.
/// </summary>
internal record NaviLitePoiEntry(int Index, byte Direction, int Distance, string Name, string Road)
{
    public byte[] ToPayload()
    {
        var nameBytes = Encoding.ASCII.GetBytes(Name);
        var roadBytes = Encoding.ASCII.GetBytes(Road);
        var payload = new byte[6 + roadBytes.Length + nameBytes.Length];
        payload[0] = (byte)Index;
        payload[1] = 0;
        payload[2] = Direction;
        payload[3] = (byte)Math.Clamp(nameBytes.Length, 0, 144);
        payload[4] = (byte)Math.Clamp(roadBytes.Length, 0, 144);
        payload[5] = (byte)Math.Clamp(Distance, 0, 255);
        roadBytes.CopyTo(payload, 6);
        nameBytes.CopyTo(payload, 6 + roadBytes.Length);
        return payload;
    }
}
