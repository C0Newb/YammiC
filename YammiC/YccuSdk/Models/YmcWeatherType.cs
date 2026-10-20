namespace CNCO.YccuSdk.Models;

/// <summary>
/// Visual weather condition displayed on the CCU screen.
///
/// Pick the value that best matches your weather source's condition code. Where a condition
/// has both Day and Night variants (e.g. <see cref="PartlyCloudyDay"/> / <see cref="PartlyCloudyNight"/>), choose
/// based on local solar time so the CCU icon matches ambient light. Use <see cref="NoData"/> when no
/// condition is available rather than defaulting to <see cref="Sunny"/>.
///
/// <see cref="YmcWeatherInfo.PrecipitationPercent"/> is independent of this field -
/// a type like <see cref="PartlyCloudyDay"/> can still carry a non-zero precipitation probability.
/// </summary>
public enum YmcWeatherType : byte
{
    Sunny = 0x00,
    PartlyCloudyDay = 0x01,
    CloudyDay = 0x02,
    Rain = 0x03,
    Snow = 0x04,
    Wind = 0x05,
    RainStormMix = 0x06,
    RainSnowMix = 0x07,
    Fog = 0x08,
    Smog = 0x0B,
    Hail = 0x0C,
    ScatteredShowersDay = 0x0D,
    ScatteredStormDay = 0x0E,
    NoData = 0x0F,
    RainLight = 0x10,
    RainHeavy = 0x11,
    SnowLight = 0x12,
    SnowHeavy = 0x13,
    RainSnowMixLight = 0x14,
    RainSnowMixHeavy = 0x15,
    Cloudy = 0x16,
    Dust = 0x17,
    Tornado = 0x18,
    Clear = 0x80,
    PartlyCloudyNight = 0x81,
    CloudyNight = 0x82,
    ScatteredShowersNight = 0x8D,
    ScatteredStormNight = 0x8E,
}
