using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Networking;
using TMPro;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

// ===================== JSON MODELS (match API fields) =====================
[Serializable]
public class ApiPointRecord
{
    public string point_name;
    public string lat;      // server gives these as strings
    public string @long;    // "long" in JSON
}

[Serializable]
public class ApiPointsResponse
{
    public ApiPointRecord[] data;
    public bool error;      // if present on API
}

// ===================== APP MODELS =====================
[System.Serializable]
public class LocationPoint
{
    public string name;
    public double lat;
    public double lon;
}

public class NavigationButton : MonoBehaviour
{
    [Header("API")]
    [SerializeField] private string apiUrl = "https://shiprabackend.triosoft.ai/api/admin_link/fetch_points";

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
        // 1) fetch points from API, then populate dropdown
        StartCoroutine(FetchPointsFromApi_ThenPopulate());

        // 2) optionally auto-fill current lat/lon
        if (autoFillCurrentLocation)
        {
            StartGPSLocation();
        }
    }

    // -------------------- API FETCH --------------------
    private IEnumerator FetchPointsFromApi_ThenPopulate()
    {
        // Build POST body (add fields only if your backend expects them)
        WWWForm form = new WWWForm();
        // Example if needed:
        // form.AddField("type", "destination");
        // form.AddField("status", "active");

        using (UnityWebRequest www = UnityWebRequest.Post(apiUrl, form))
        {
            www.timeout = 15;
            www.SetRequestHeader("Accept", "application/json");

            yield return www.SendWebRequest();

#if UNITY_2020_1_OR_NEWER
        if (www.result != UnityWebRequest.Result.Success)
#else
            if (www.isNetworkError || www.isHttpError)
#endif
            {
                Debug.LogError($"[NavigationButton] API Error: {www.responseCode} {www.error}  URL:{apiUrl}");
                PopulateDropdown(); // still show placeholder even if API fails
                yield break;
            }

            string respText = www.downloadHandler.text?.Trim();
            if (string.IsNullOrEmpty(respText) || (!respText.StartsWith("{") && !respText.StartsWith("[")))
            {
                Debug.LogError("[NavigationButton] Response is not JSON. Inspect server response in Console.\n" + respText);
                PopulateDropdown();
                yield break;
            }

            ApiPointsResponse resp = null;
            try
            {
                resp = JsonUtility.FromJson<ApiPointsResponse>(respText);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NavigationButton] JSON parse exception: {ex.Message}");
                PopulateDropdown();
                yield break;
            }

            if (resp == null || resp.data == null || resp.data.Length == 0)
            {
                Debug.LogWarning("[NavigationButton] No 'data' array in response or it's empty.");
                PopulateDropdown();
                yield break;
            }

            // Map API data to your LocationPoint list
            points.Clear();
            int added = 0;

            foreach (var rec in resp.data)
            {
                if (rec == null) continue;

                // Robust parse using InvariantCulture
                if (TryParseDoubleInvariant(rec.lat, out double dlat) &&
                    TryParseDoubleInvariant(rec.@long, out double dlon) &&
                    dlat >= -90 && dlat <= 90 && dlon >= -180 && dlon <= 180)
                {
                    points.Add(new LocationPoint
                    {
                        name = string.IsNullOrWhiteSpace(rec.point_name) ? $"Point {added + 1}" : rec.point_name,
                        lat = dlat,
                        lon = dlon
                    });
                    added++;
                }
                else
                {
                    Debug.LogWarning($"[NavigationButton] Skipped invalid lat/lon: lat='{rec.lat}' lon='{rec.@long}' name='{rec.point_name}'");
                }
            }

            Debug.Log($"[NavigationButton] Loaded {added} destination points from API.");
            PopulateDropdown();
        }
    }


    private static bool TryParseDoubleInvariant(string s, out double value)
    {
        return double.TryParse(
            s?.Trim(),
            NumberStyles.Float | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture,
            out value
        );
    }

    // -------------------- GPS --------------------
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

    private IEnumerator WaitForLocation()
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
        currentLocationInput.text = $"{lat:F8},{lon:F8}";

        Debug.Log($"Current location set with full precision: {lat:F8}, {lon:F8}");
    }

    // -------------------- UI --------------------
    private void PopulateDropdown()
    {
        if (destinationDropdown == null)
        {
            Debug.LogError("[NavigationButton] destinationDropdown is not assigned.");
            return;
        }

        destinationDropdown.ClearOptions();
        List<string> names = new List<string> { "-- Select destination --" };

        if (points != null && points.Count > 0)
        {
            foreach (var point in points)
                names.Add(point.name);
        }

        destinationDropdown.AddOptions(names);
        destinationDropdown.value = 0;
        destinationDropdown.RefreshShownValue();
    }

    // -------------------- NAVIGATE --------------------
    public void OnNavigateButtonPressed()
    {
        if (string.IsNullOrEmpty(currentLocationInput.text))
        {
            Debug.LogError("Current location not set!");
            return;
        }

        if (points == null || points.Count == 0)
        {
            Debug.LogError("No destination points available! (API may have failed or returned none)");
            return;
        }

        // Parse source from text input - FULL PRECISION
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
        if (pointIndex < 0 || pointIndex >= points.Count)
        {
            Debug.LogError($"Invalid destination selection! selectedIndex={selectedIndex} pointIndex={pointIndex} pointsCount={(points == null ? 0 : points.Count)}");
            return;
        }

        LocationPoint selectedPoint = points[pointIndex];
        GeoCoordinate destinCoord = new GeoCoordinate(selectedPoint.lat, selectedPoint.lon);

        // Save for AR scene
        NavigationData.Source = sourceCoord;
        NavigationData.Destination = destinCoord;
        NavigationData.DestinationName = selectedPoint.name;
        NavigationData.HasData = true;

        Debug.Log($"Navigation Data Set - Source: ({NavigationData.Source.latitude:F8},{NavigationData.Source.longitude:F8}), " +
                  $"Destination: ({NavigationData.Destination.latitude:F8},{NavigationData.Destination.longitude:F8})");

        SceneManager.LoadScene(targetSceneName);
    }

    private GeoCoordinate ParseLatLonDouble(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            Debug.LogError("Empty coordinate input");
            return GeoCoordinate.zero;
        }

        string[] parts = input.Trim().Split(',');
        if (parts.Length == 2)
        {
            if (double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double lat) &&
                double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double lon))
            {
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
}