using CNCO.YccuSdk.Models;

namespace CNCO.YccuSdk.Packets.App;

/// <summary>
/// Sends music metadata to the CCU via DDT session.
/// Wire: `[cmd 2B][artistLen 1B][artist][albumLen 1B][album][songLen 1B][song][duration 3B seconds][state 1B][rate 2B][elapsed 3B seconds]`.
/// Strings are length-prefixed, max 64 UTF-8 bytes each.
/// Duration and elapsed are 24-bit big-endian seconds (not milliseconds), confirmed against a real CCU capture.
/// </summary>
public class AppMusicMetaDataResponse : AppOutgoingResponse
{
    public AppMusicMetaDataResponse(IYmcPacketHandler packetHandler, YmcMediaMetaData metadata)
        : base(packetHandler, YmcCommandCode.BluetoothMusicMetaData)
    {
        Metadata = metadata;
    }

    public YmcMediaMetaData Metadata { get; }

    public override Transport PacketTransport => Transport.Ddt;

    public override byte[] Serialize(byte requestId)
    {
        var artist = StringBytes(Metadata.ArtistName, maxBytes: 64);
        var album = StringBytes(Metadata.AlbumName, maxBytes: 64);
        var song = StringBytes(Metadata.SongName, maxBytes: 64);
        var payload = Concat(
            new[] { (byte)artist.Length }, artist,
            new[] { (byte)album.Length }, album,
            new[] { (byte)song.Length }, song,
            Int24(Metadata.DurationSeconds),
            new[] { (byte)Metadata.PlaybackState },
            ShortBytes((int)(Metadata.PlaybackRate * 10.0f)),
            Int24(Metadata.ElapsedSeconds));
        return Concat(Preamble, payload);
    }
}
