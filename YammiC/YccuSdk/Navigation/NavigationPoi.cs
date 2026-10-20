using CNCO.YccuSdk.Navigation.Protocol;

namespace CNCO.YccuSdk.Navigation;

/// <summary>Which of the CCU's point-of-interest screens a list belongs to.</summary>
public enum NavPoiList
{
    Favorites = NaviLiteContentType.FavoriteList,
    Stations = NaviLiteContentType.StationsList,
}

/// <summary>Where a POI lies relative to the vehicle (not compass).</summary>
public enum NavPoiDirection : byte
{
    Undefined = 0,
    Front = 1,
    FrontRight = 2,
    Right = 3,
    BackRight = 4,
    Back = 5,
    BackLeft = 6,
    Left = 7,
    FrontLeft = 8,
}

/// <summary>
/// One entry on the CCU's Favorites / gas-station screen. Names are sent as ASCII and clipped to 144 bytes;
/// <paramref name="Distance"/> is a single quantised byte (0-255, whole miles in the reference app).
/// </summary>
public record NavPoi(string Name, string Road, int Distance = 0, NavPoiDirection Direction = NavPoiDirection.Front);

/// <summary>The rider picked a POI on the CCU (START_ROUTE_REQUEST).</summary>
/// <param name="List">Which screen the entry was picked from.</param>
/// <param name="ItemIndex">Index into the list that was sent.</param>
/// <param name="RouteOption">The CCU's route option byte (new route / next stop / last stop in StreetCross).</param>
/// <param name="Poi">The entry that was picked, or null if the index didn't match anything we sent.</param>
/// <param name="RawPayload">The unparsed request, for diagnosing field order on hardware.</param>
public record NavPoiSelection(NavPoiList List, int ItemIndex, int RouteOption, NavPoi? Poi, byte[] RawPayload);
