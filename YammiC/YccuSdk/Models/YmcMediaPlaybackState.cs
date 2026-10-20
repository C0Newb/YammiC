namespace CNCO.YccuSdk.Models;

public enum YmcMediaPlaybackState : byte
{
    Paused = 0x01,
    Playing = 0x02,
    Rewinding = 0x03,
    FastForwarding = 0x04,
    MediaDeviceUnavailable = 0xFF,
}
