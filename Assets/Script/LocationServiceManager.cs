using UnityEngine;
using UnityEngine.Android;
using System.Collections;
using TMPro;

public class LocationServiceManager : MonoBehaviour
{
    [Header("UI References")]
    public TMP_InputField locationInputField; // Assign TMP InputField in Inspector
    public TMPro.TextMeshProUGUI debugText;   // Optional for debug logs

    private bool isChecking = false;

    void Start()
    {
        StartCoroutine(CheckLocationService());
    }

    // Detect when user comes back from Location Settings
    void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus && !isChecking)
        {
            // Debug.Log("App resumed, re-checking location...");
            StartCoroutine(CheckLocationService());
        }
    }

    public IEnumerator CheckLocationService()
    {
        isChecking = true;

#if UNITY_ANDROID
        if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
        {
            Permission.RequestUserPermission(Permission.FineLocation);
            yield return new WaitForSeconds(1f);
        }
#endif

        // Check if GPS is enabled
        if (!Input.location.isEnabledByUser)
        {
            // Debug.Log("GPS is OFF. Opening Location Settings...");
            OpenLocationSettings();
            isChecking = false;
            yield break;
        }

        // Start location service
        Input.location.Start();

        // Wait until service initializes
        int maxWait = 20;
        while (Input.location.status == LocationServiceStatus.Initializing && maxWait > 0)
        {
            yield return new WaitForSeconds(1);
            maxWait--;
        }

        if (maxWait < 1)
        {
            // Debug.Log("Location service timed out.");
            isChecking = false;
            yield break;
        }

        if (Input.location.status == LocationServiceStatus.Failed)
        {
            // Debug.Log("Unable to determine device location.");
            isChecking = false;
            yield break;
        }
        else
        {
            // Show the first fetched location immediately
            UpdateLocationField();

            // Start continuous updates
            StartCoroutine(UpdateLocationData());
        }

        isChecking = false;
    }

    private IEnumerator UpdateLocationData()
    {
        while (true)
        {
            UpdateLocationField();
            yield return new WaitForSeconds(2f); // update every 2 seconds
        }
    }

   private void UpdateLocationField()
{
    if (Input.location.status == LocationServiceStatus.Running)
    {
        double lat = Input.location.lastData.latitude;
        double lon = Input.location.lastData.longitude;

        string latStr = lat.ToString("F6");
        string lonStr = lon.ToString("F6");

        // Format as: 23.2599, 77.4126 (without labels)
        string locText = latStr + ", " + lonStr;

        if (debugText != null)
            debugText.text = "Lat: " + latStr + ", Lon: " + lonStr; // Keep labels for debug

        if (locationInputField != null && !locationInputField.isFocused) 
        {
            // Only overwrite if user is NOT typing
            locationInputField.text = locText; // Simple format for input field
        }

        // Debug.Log("Location updated: " + locText);
    }
}

    // Button action
    public void OnCheckLocationButton()
    {
        StartCoroutine(CheckLocationService());
    }

    private void OpenLocationSettings()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        {
            AndroidJavaObject currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            using (var settingsClass = new AndroidJavaClass("android.provider.Settings"))
            {
                string actionLocationSourceSettings = settingsClass.GetStatic<string>("ACTION_LOCATION_SOURCE_SETTINGS");
                AndroidJavaObject intent = new AndroidJavaObject("android.content.Intent", actionLocationSourceSettings);
                currentActivity.Call("startActivity", intent);
            }
        }
#else
        // Debug.Log("Opening location settings works only on Android device.");
#endif
    }
}
