using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using System.Collections.Generic;

[System.Serializable]
public class LocationPoint
{
    public string name;
    public float lat;
    public float lon;
}

public class NavigationButton : MonoBehaviour
{
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

    // Success - update the input field
    float lat = Input.location.lastData.latitude;
    float lon = Input.location.lastData.longitude;
    currentLocationInput.text = $"{lat},{lon}";
    
    Debug.Log($"Current location set: {lat}, {lon}");
}
   private void PopulateDropdown()
{
    destinationDropdown.ClearOptions();
    List<string> names = new List<string>();

    // Add a placeholder as the first item
    names.Add("-- Select destination --");

    foreach (var point in points)
    {
        names.Add(point.name);
    }

    destinationDropdown.AddOptions(names);

    // Ensure dropdown shows placeholder initially
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

    // Parse source from text input
    Vector2 sourceLatLon = ParseLatLon(currentLocationInput.text);
    if (sourceLatLon == Vector2.zero)
    {
        Debug.LogError("Invalid source coordinates!");
        return;
    }

    // Get destination from dropdown
    // --- Get destination from dropdown (placeholder at index 0) ---
int selectedIndex = destinationDropdown.value;

// If placeholder (index 0) is selected -> ask user to choose a real destination
if (selectedIndex == 0)
{
    Debug.LogError("Please select a destination from the dropdown!");
    return;
}

// Map dropdown index to points[] (we inserted one placeholder at index 0)
int pointIndex = selectedIndex - 1;

// Safety checks (helps avoid out-of-range when options/points get changed at runtime)
if (points == null || pointIndex < 0 || pointIndex >= points.Count)
{
    Debug.LogError($"Invalid destination selection! selectedIndex={selectedIndex} pointIndex={pointIndex} pointsCount={(points==null?0:points.Count)}");
    return;
}

LocationPoint selectedPoint = points[pointIndex];
Vector2 destinLatLon = new Vector2(selectedPoint.lat, selectedPoint.lon);


    // Save in static data class
    NavigationData.Source = sourceLatLon;
    NavigationData.Destination = destinLatLon;
    NavigationData.DestinationName = selectedPoint.name; 
    NavigationData.HasData = true;

    Debug.Log($"Navigation Data Set - Source: {NavigationData.Source}, Destination: {NavigationData.Destination}");

    // Load ARNavigation scene
    SceneManager.LoadScene(targetSceneName);
}

   private Vector2 ParseLatLon(string input)
{
    if (string.IsNullOrEmpty(input))
    {
        Debug.LogError("Empty coordinate input");
        return Vector2.zero;
    }

    // Expected: "23.305338,77.6783"
    string[] parts = input.Trim().Split(',');
    if (parts.Length == 2)
    {
        if (float.TryParse(parts[0].Trim(), out float lat) && 
            float.TryParse(parts[1].Trim(), out float lon))
        {
            // Basic validation for reasonable coordinates
            if (lat >= -90 && lat <= 90 && lon >= -180 && lon <= 180)
            {
                return new Vector2(lat, lon);
            }
            else
            {
                Debug.LogError($"Coordinates out of valid range: lat={lat}, lon={lon}");
            }
        }
    }

    Debug.LogError("Invalid coordinate format: " + input + " (Expected: lat,lon)");
    return Vector2.zero;
}
}
