namespace CNCO.YccuSdk.Models;

public class YmcLatLong
{
    public YmcLatLong(int latitude, int longitude)
    {
        Latitude = latitude;
        Longitude = longitude;
    }

    public int Latitude { get; set; }
    public int Longitude { get; set; }
}
