using CCU_Emulator.Protocol;
using CCU_Emulator.Transport;

namespace CCU_Emulator.Emulation;

public sealed partial class CcuSession
{
    private static readonly TimeSpan DeviceTimeRepeat = TimeSpan.FromSeconds(5);

    private TimeSpan _sinceDeviceTimeRequest;
    private TimeSpan _sincePing;
    private bool _deviceTimeAnswered;
    private byte _pingSeq;

    /// <summary>Sends <paramref name="packet"/> to the phone on the main RX characteristic.</summary>
    private void SendCcu(byte[] packet, string name) => Send(CcuChannel.MainRx, packet, name);

    /// <summary>What a real CCU sends right after a successful auth (order taken from a capture).</summary>
    private void StartBootstrap()
    {
        var b = Settings.Behavior;
        _deviceTimeAnswered = false;
        _sinceDeviceTimeRequest = TimeSpan.Zero;
        _sincePing = TimeSpan.Zero;
        _pingSeq = 0;

        if (b.SendLibVersion) SendCcu(CcuPackets.LibVersion(), "LibVersion");
        if (b.SendLanguageRequest) SendCcu(CcuPackets.LanguageRequest(), "LanguageRequest");
        if (b.SendDeviceTimeRequest) SendCcu(CcuPackets.DeviceTimeRequest(), "DeviceTimeRequest");
        if (b.SendVolumeControllableRequest) SendCcu(CcuPackets.VolumeControllableRequest(), "VolumeControllableRequest");
        if (b.SendWeatherRequest) SendCcu(CcuPackets.WeatherRequestCurrent(), "WeatherRequest");
        if (b.SendHeadsetStateRequest) SendCcu(CcuPackets.HeadsetStateRequest(), "HeadsetStateRequest");
        if (b.SendCellSignalRequest) SendCcu(CcuPackets.CellSignalRequest(), "CellSignalRequest");
    }

    /// <summary>Advances the session clock; the host calls this about once a second.</summary>
    public void Tick(TimeSpan elapsed)
    {
        if (!State.Authenticated) return;
        var b = Settings.Behavior;

        if (b.SendDeviceTimeRequest && !_deviceTimeAnswered)
        {
            _sinceDeviceTimeRequest += elapsed;
            if (_sinceDeviceTimeRequest >= DeviceTimeRepeat)
            {
                _sinceDeviceTimeRequest = TimeSpan.Zero;
                SendCcu(CcuPackets.DeviceTimeRequest(), "DeviceTimeRequest (repeat)");
            }
        }

        if (b.SendPing && b.PingIntervalSeconds > 0)
        {
            _sincePing += elapsed;
            if (_sincePing >= TimeSpan.FromSeconds(b.PingIntervalSeconds))
            {
                _sincePing = TimeSpan.Zero;
                SendCcu(CcuPackets.Ping(_pingSeq++), "PingRequest");
            }
        }
    }
}
