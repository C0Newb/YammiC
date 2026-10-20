namespace CNCO.YccuSdk.Models;

/// <summary>
/// Time slot for a weather forecast entry sent to the CCU.
///
/// The CCU expects slots in ascending order: <see cref="Current"/> first, hourly slots next, then daily
/// slots. Not all slots need to be provided - send only the ones your weather source supports.
/// </summary>
public enum YmcWeatherTime : byte
{
    Current = 0x00,
    After1Day = 0x01,
    After2Days = 0x02,
    After3Days = 0x03,
    After4Days = 0x04,
    After5Days = 0x05,
    After1Hour = 0x06,
    After2Hours = 0x07,
    After3Hours = 0x08,
    After6Days = 0x09,
}
