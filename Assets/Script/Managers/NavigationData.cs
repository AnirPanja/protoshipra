using UnityEngine;

public static class NavigationData
{
    // Changed from Vector2 (float) to custom struct with double precision
    public static GeoCoordinate Source;
    public static GeoCoordinate Destination;
    public static string DestinationName = "";
    public static bool HasData = false;
}

// New struct to hold double-precision coordinates
[System.Serializable]
public struct GeoCoordinate
{
    public double latitude;
    public double longitude;

    public GeoCoordinate(double lat, double lon)
    {
        latitude = lat;
        longitude = lon;
    }

    // Convenience conversion to Vector2 when needed (with precision loss warning)
    public Vector2 ToVector2()
    {
        return new Vector2((float)latitude, (float)longitude);
    }

    public static GeoCoordinate zero => new GeoCoordinate(0, 0);
    
    public bool IsZero()
    {
        return System.Math.Abs(latitude) < 0.000001 && System.Math.Abs(longitude) < 0.000001;
    }
}
