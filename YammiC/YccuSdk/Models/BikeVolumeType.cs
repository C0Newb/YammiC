namespace CNCO.YccuSdk.Models;

/// <summary>
/// Audio channel volume to control (telophony or media).
/// </summary>
public enum BikeVolumeType : byte
{
    /// <summary>
    /// Telophony volume
    /// </summary>
    Phone = 0x00,

    /// <summary>
    /// Audio/Media volume
    /// </summary>
    Media = 0x01,
}
