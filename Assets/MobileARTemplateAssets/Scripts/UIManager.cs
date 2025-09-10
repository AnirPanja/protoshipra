using UnityEngine;
using UnityEngine.UI;
using TMPro; // For TextMeshPro input fields
using System.Collections;

public class UIManager : MonoBehaviour
{
    [SerializeField] private TMP_InputField sourceInput; // Source lat/long input
    [SerializeField] private TMP_InputField destInput;   // Destination lat/long input
    [SerializeField] private Button startButton;        // Start navigation button
    [SerializeField] private ARNavigation arNavigation; // Reference to ARNavigation script

    void Start()
    {
        // Validate references
        if (startButton == null) Debug.LogError("Start Button not assigned in UIManager.");
        if (sourceInput == null) Debug.LogError("Source Input not assigned in UIManager.");
        if (destInput == null) Debug.LogError("Destination Input not assigned in UIManager.");
        if (arNavigation == null) Debug.LogError("ARNavigation script not assigned in UIManager.");

        // Set up button click listener if button is assigned
        if (startButton != null)
        {
            startButton.onClick.AddListener(OnStartButtonClicked);
        }

        // Pre-fill source with current location
        StartCoroutine(GetCurrentLocation());
    }

    void OnStartButtonClicked()
    {
        Vector2 source = ParseLatLong(sourceInput.text);
        Vector2 dest = ParseLatLong(destInput.text);

        if (source != Vector2.zero && dest != Vector2.zero)
        {
            if (arNavigation != null)
            {
                arNavigation.OnStartButtonPressed(source, dest); // Use OnStartButtonPressed
                Debug.Log($"Navigation Started - Source: {source.x}, {source.y}, Destination: {dest.x}, {dest.y}");
            }
            else
            {
                Debug.LogError("ARNavigation script not assigned in UIManager.");
            }
        }
        else
        {
            Debug.LogError("Invalid lat/long input. Use format: lat,long (e.g., 23.3016188,77.3645689)");
        }
    }

    Vector2 ParseLatLong(string input)
    {
        if (string.IsNullOrEmpty(input)) return Vector2.zero;
        string[] parts = input.Split(',');
        if (parts.Length == 2 && float.TryParse(parts[0].Trim(), out float lat) && float.TryParse(parts[1].Trim(), out float lng))
        {
            return new Vector2(lat, lng);
        }
        return Vector2.zero;
    }

    IEnumerator GetCurrentLocation()
    {
        if (!Input.location.isEnabledByUser)
        {
            Debug.LogWarning("Location services not enabled. Please enable it in device settings.");
            yield break;
        }

        Input.location.Start();
        int maxWait = 20;
        while (Input.location.status == LocationServiceStatus.Initializing && maxWait > 0)
        {
            yield return new WaitForSeconds(1);
            maxWait--;
        }

        if (Input.location.status == LocationServiceStatus.Running && sourceInput != null)
        {
            float lat = Input.location.lastData.latitude;
            float lon = Input.location.lastData.longitude;
            sourceInput.text = $"{lat:F6}, {lon:F6}"; // Use 6 decimal places for precision
            Debug.Log($"Current location set: {lat}, {lon}");
        }
        else
        {
            Debug.LogError("Failed to get location or timed out.");
        }

        // Stop location service to save battery
        Input.location.Stop();
    }
}