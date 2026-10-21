using CNCO.YccuSdk.Models;
using CNCO.YccuSdk.Packets.Ccu;

namespace CNCO.YccuSdk.Providers;

/// <summary>
/// Fixed, deliberately silly forecast for exercising every weather icon and the encoder's clamping.
/// </summary>
public class DemoWeatherProvider : IYmcWeatherProvider
{
    public Task<YmcWeatherData> GetWeatherAsync(CcuWeatherRequest request)
    {
        static KeyValuePair<YmcWeatherTime, YmcWeatherInfo> Slot(
            YmcWeatherTime time, YmcWeatherType type, double temp, double max, double min, int precipitation) =>
            new(time, new YmcWeatherInfo(type, temp, max, min, precipitation));

        return Task.FromResult(new YmcWeatherData(
            "Demo Location",
            new[]
            {
                Slot(YmcWeatherTime.Current, YmcWeatherType.Tornado, 22.0, 60.0, -20.0, 125),
                Slot(YmcWeatherTime.After1Hour, YmcWeatherType.Smog, 55.5, 58.0, 50.0, 5),
                Slot(YmcWeatherTime.After2Hours, YmcWeatherType.Sunny, 42.0, 45.0, 37.0, 45),
                Slot(YmcWeatherTime.After3Hours, YmcWeatherType.SnowHeavy, -12.0, -10.0, -15.0, 100),
                Slot(YmcWeatherTime.After1Day, YmcWeatherType.CloudyDay, 16.0, 18.0, 11.0, 30),
                Slot(YmcWeatherTime.After2Days, YmcWeatherType.RainLight, 14.0, 16.0, 10.0, 70),
                Slot(YmcWeatherTime.After3Days, YmcWeatherType.Clear, 20.0, 23.0, 14.0, 0),
                Slot(YmcWeatherTime.After4Days, YmcWeatherType.PartlyCloudyDay, 17.0, 20.0, 12.0, 20),
                Slot(YmcWeatherTime.After5Days, YmcWeatherType.Hail, 22.0, 25.0, 15.0, 85),
                Slot(YmcWeatherTime.After6Days, YmcWeatherType.RainSnowMixHeavy, 0.0, 3276.0, -3276.0, 95),
            }));
    }
}

public class DemoMusicMetadataProvider : IYmcMusicMetadataProvider
{
    public YmcMediaMetaData GetMetadata() => new(
        ArtistName: "Demo Artist",
        AlbumName: "Demo Album",
        SongName: "Demo Song",
        DurationSeconds: 210,
        PlaybackState: YmcMediaPlaybackState.Playing,
        PlaybackRate: 1.0f,
        ElapsedSeconds: 0);
}
