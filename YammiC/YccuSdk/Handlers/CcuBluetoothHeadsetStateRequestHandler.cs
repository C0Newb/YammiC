using CNCO.YccuSdk.Packets;
using CNCO.YccuSdk.Packets.App;
using CNCO.YccuSdk.Packets.Ccu;

namespace CNCO.YccuSdk.Handlers;

/// <summary>
/// Handles <see cref="CcuBluetoothHeadsetStateRequest"/> by reporting whether a Bluetooth headset is connected.
/// The CCU gates music metadata delivery behind receiving <see cref="Connected"/> = true.
///
/// Update <see cref="Connected"/> if headset state changes at runtime.
/// </summary>
public class CcuBluetoothHeadsetStateRequestHandler : CcuRequestHandler<CcuBluetoothHeadsetStateRequest>
{
    public CcuBluetoothHeadsetStateRequestHandler(bool connected = true)
    {
        Connected = connected;
    }

    public bool Connected { get; set; }

    public override Task InvokeAsync(
        CcuBluetoothHeadsetStateRequest packet,
        IYmcPacketHandler packetHandler,
        YmcConnectionContext connectionContext) =>
        new AppBluetoothHeadsetStateResponse(packetHandler, Connected).SendAsync(connectionContext);
}
