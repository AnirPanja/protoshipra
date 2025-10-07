using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using System.Collections.Generic;

// Use double for full GPS precision
[System.Serializable]
public class LocationPoint
{
    public string name;
    public double lat;  
    public double lon;  
}


public class NavigationButton : MonoBehaviour
{
    private string apiUrl = "https://shiprabackend.triosoft.ai/api/admin_link/fetch_points";
    [Header("UI References")]
    public TMP_InputField currentLocationInput;   // Auto-filled from GPS
    public TMP_Dropdown destinationDropdown;      // Dropdown with names

    [Header("Destination Points")]
    public List<LocationPoint> points = new List<LocationPoint>();

    [SerializeField] private string targetSceneName = "ARNavigation";

    [Header("GPS")]
    [SerializeField] private bool autoFillCurrentLocation = true;

    private void Start()
    {
        PopulateDropdown();
        if (autoFillCurrentLocation)
        {
            StartGPSLocation();
        }
    }

    private void StartGPSLocation()
    {
        if (!Input.location.isEnabledByUser)
        {
            Debug.LogError("Location services not enabled by user");
            return;
        }

        Input.location.Start(1f, 1f);
        StartCoroutine(WaitForLocation());
    }

  private System.Collections.IEnumerator WaitForLocation()
{
    int maxWait = 20;
    while (Input.location.status == LocationServiceStatus.Initializing && maxWait > 0)
    {
        yield return new WaitForSeconds(1);
        maxWait--;
    }

    if (maxWait <= 0)
    {
        Debug.LogError("GPS timeout");
        yield break;
    }

    if (Input.location.status == LocationServiceStatus.Failed)
    {
        Debug.LogError("Unable to determine device location");
        yield break;
    }

    // Success - update the input field with FULL PRECISION
    double lat = Input.location.lastData.latitude;
    double lon = Input.location.lastData.longitude;
    currentLocationInput.text = $"{lat:F8},{lon:F8}";  // Show 8 decimal places
    
    Debug.Log($"Current location set with full precision: {lat:F8}, {lon:F8}");
}


    private void PopulateDropdown()
    {
        destinationDropdown.ClearOptions();
        List<string> names = new List<string>();

        names.Add("-- Select destination --"); // placeholder
        foreach (var point in points)
        {
            names.Add(point.name);
        }

        destinationDropdown.AddOptions(names);
        destinationDropdown.value = 0;
        destinationDropdown.RefreshShownValue();
    }

   public void OnNavigateButtonPressed()
{
    if (string.IsNullOrEmpty(currentLocationInput.text))
    {
        Debug.LogError("Current location not set!");
        return;
    }

    if (points == null || points.Count == 0)
    {
        Debug.LogError("No destination points configured!");
        return;
    }

    // Parse source from text input - NOW RETURNS DOUBLE PRECISION
    GeoCoordinate sourceCoord = ParseLatLonDouble(currentLocationInput.text);
    if (sourceCoord.IsZero())
    {
        Debug.LogError("Invalid source coordinates!");
        return;
    }

    // Get destination from dropdown
    int selectedIndex = destinationDropdown.value;

    if (selectedIndex == 0)
    {
        Debug.LogError("Please select a destination from the dropdown!");
        return;
    }

    int pointIndex = selectedIndex - 1;

    if (points == null || pointIndex < 0 || pointIndex >= points.Count)
    {
        Debug.LogError($"Invalid destination selection! selectedIndex={selectedIndex} pointIndex={pointIndex} pointsCount={(points==null?0:points.Count)}");
        return;
    }

    LocationPoint selectedPoint = points[pointIndex];
    GeoCoordinate destinCoord = new GeoCoordinate(selectedPoint.lat, selectedPoint.lon);

    // Save in static data class - NOW WITH FULL PRECISION
    NavigationData.Source = sourceCoord;
    NavigationData.Destination = destinCoord;
    NavigationData.DestinationName = selectedPoint.name; 
    NavigationData.HasData = true;

    Debug.Log($"Navigation Data Set - Source: ({NavigationData.Source.latitude:F8},{NavigationData.Source.longitude:F8}), " +
              $"Destination: ({NavigationData.Destination.latitude:F8},{NavigationData.Destination.longitude:F8})");

    // Load ARNavigation scene
    SceneManager.LoadScene(targetSceneName);
}
private GeoCoordinate ParseLatLonDouble(string input)
{
    if (string.IsNullOrEmpty(input))
    {
        Debug.LogError("Empty coordinate input");
        return GeoCoordinate.zero;
    }

    // Expected: "23.305338,77.6783"
    string[] parts = input.Trim().Split(',');
    if (parts.Length == 2)
    {
        if (double.TryParse(parts[0].Trim(), out double lat) && 
            double.TryParse(parts[1].Trim(), out double lon))
        {
            // Basic validation for reasonable coordinates
            if (lat >= -90 && lat <= 90 && lon >= -180 && lon <= 180)
            {
                Debug.Log($"Parsed coordinates with full precision: {lat:F8}, {lon:F8}");
                return new GeoCoordinate(lat, lon);
            }
            else
            {
                Debug.LogError($"Coordinates out of valid range: lat={lat}, lon={lon}");
            }
        }
    }

    Debug.LogError("Invalid coordinate format: " + input + " (Expected: lat,lon)");
    return GeoCoordinate.zero;
}

    private bool TryParseLatLon(string input, out double lat, out double lon)
    {
        lat = 0; lon = 0;
        if (string.IsNullOrEmpty(input)) return false;

        string[] parts = input.Trim().Split(',');
        if (parts.Length == 2 &&
            double.TryParse(parts[0].Trim(), out lat) &&
            double.TryParse(parts[1].Trim(), out lon))
        {
            if (lat >= -90 && lat <= 90 && lon >= -180 && lon <= 180)
                return true;
        }

        return false;
    }
}

