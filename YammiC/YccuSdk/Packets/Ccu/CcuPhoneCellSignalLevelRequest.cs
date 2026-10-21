namespace CNCO.YccuSdk.Packets.Ccu;

/// <summary>
/// Bike requesting the phone's current cell signal level.
/// Respond with an <see cref="App.AppPhoneCellSignalLevelResponse"/> packet.
/// </summary>
/// <inheritdoc cref="CcuIncomingRequest"/>
public class CcuPhoneCellSignalLevelRequest(IYmcPacketHandler packetHandler, byte[] data)
    : CcuIncomingRequest(YmcCommandCode.PhoneCellSignalLevelRequest, packetHandler, data)
{
    protected override void Deserialize(byte[] data) { }
}
