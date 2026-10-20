using CNCO.YccuSdk.Models;
using CNCO.YccuSdk.Packets.Ccu;

namespace CNCO.YccuSdk.Platform;

/// <summary>
/// Read-only view of the phone's current state, used to build the phone-state reports sent to the CCU.
/// </summary>
public interface IYmcPhoneStatusProvider
{
    /// <summary>Battery level 0-100 (100 when unknown).</summary>
    int BatteryPercent { get; }

    bool IsCharging { get; }

    float BatteryTemperatureCelsius { get; }

    YmcCellSignalLevel CellSignalLevel { get; }

    /// <summary>Current volume of the given stream as a 0-100 percentage.</summary>
    int GetVolumePercent(BikeVolumeType type);
}

/// <summary>Adjusts the phone's volume on behalf of the CCU.</summary>
public interface IYmcVolumeController
{
    int GetVolumePercent(BikeVolumeType type);

    void Adjust(BikeVolumeType type, bool raise);
}

/// <summary>Answers / ends calls on behalf of the CCU.</summary>
public interface IYmcCallController
{
    bool CanControlCalls { get; }

    void AcceptRingingCall();

    void EndCall();
}

/// <summary>Controls media playback on behalf of the CCU.</summary>
public interface IYmcMediaController
{
    /// <summary>Tries the active media session's transport controls. False if there is no active session.</summary>
    bool TryTransportControl(CcuMusicControlRequest.Action action);

    /// <summary>Fallback when there is no active session: dispatches a media key event.</summary>
    void DispatchMediaKey(CcuMusicControlRequest.Action action);
}

/// <summary>Small string key/value persistence (credentials, device name, ...).</summary>
public interface IYmcKeyValueStore
{
    string? GetString(string key);

    void SetString(string key, string value);
}
