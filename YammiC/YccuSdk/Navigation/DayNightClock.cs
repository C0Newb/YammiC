namespace CNCO.YccuSdk.Navigation;

/// <summary>
/// Day or night by the phone's clock. The bike's own light sensor isn't visible to the phone, so this is a fixed window:
/// dark from 19:00 to 06:00.
/// </summary>
public static class DayNightClock
{
    public static NavDayNightMode For(DateTime localTime) =>
        localTime.Hour >= 19 || localTime.Hour < 6 ? NavDayNightMode.Night : NavDayNightMode.Day;
}
