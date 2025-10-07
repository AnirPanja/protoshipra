using UnityEngine;
using System.Collections;
using TMPro;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

public class CompassARDebugger : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TMP_Text debugText;

    [Header("Alignment (optional)")]
    [Tooltip("If true, apply an offset so Unity camera heading matches device compass.")]
    [SerializeField] private bool autoAlignOnReady = false;

    private bool _askedAndroidPerm = false;
    private float _unityToDeviceYawOffset = 0f; // applied to Unity heading readout only (non-invasive)

    IEnumerator Start()
    {
        if (!debugText) yield break;

        // 1) Request permission (Android)
#if UNITY_ANDROID
        if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
        {
            _askedAndroidPerm = true;
            Permission.RequestUserPermission(Permission.FineLocation);
        }
#endif

        // 2) Enable sensors
        Input.compass.enabled = true;
        Input.gyro.enabled = true; // improves heading stability on many devices
        Input.location.Start(5f, 1f); // desiredAccuracyInMeters, updateDistanceInMeters

        // 3) Wait for location service to initialize (up to 10s)
        float t = 0f;
        while (Input.location.status == LocationServiceStatus.Initializing && t < 10f)
        {
            debugText.text = "⏳ Initializing location service…";
            t += Time.unscaledDeltaTime;
            yield return null;
        }

        // 4) If still not running, show reason
        if (Input.location.status != LocationServiceStatus.Running)
        {
            string reason = "Unknown";
#if UNITY_ANDROID
            if (_askedAndroidPerm && !Permission.HasUserAuthorizedPermission(Permission.FineLocation))
                reason = "Android location permission denied";
            else
                reason = "Location service disabled or unavailable";
#elif UNITY_IOS
            // iOS prompts automatically when starting location;
            // if user denies, service stays Stopped/Failed.
            reason = "iOS location permission denied/disabled";
#endif
            debugText.text = $"❌ Location not running. Reason: {reason}\n" +
                             $"status={Input.location.status}\n" +
                             $"Enable location permission in OS settings and relaunch.";
            yield break;
        }

        // 5) Small warm-up so compass starts reporting heading
        float warm = 0f;
        while (Input.compass.headingAccuracy < 0 && warm < 3f)
        {
            debugText.text = "⏳ Waiting for compass data…";
            warm += Time.unscaledDeltaTime;
            yield return null;
        }

        if (autoAlignOnReady)
        {
            // Compute initial alignment offset once both are available
            var (deviceTrue, _) = GetDeviceHeadings();
            float unity = GetUnityHeading();
            if (deviceTrue >= 0f)
                _unityToDeviceYawOffset = DeltaAngle(unity, deviceTrue);
        }
    }

    void Update()
    {
        if (!debugText) return;

        // Device headings
        var (deviceTrue, deviceMag) = GetDeviceHeadings();
        float acc = Input.compass.headingAccuracy; // -1 means unknown/not ready

        // Unity camera heading (project to horizontal)
        float unityHeading = GetUnityHeading();

        // Apply optional alignment offset to *displayed* Unity heading (non-invasive)
        float unityAligned = Wrap360(unityHeading + _unityToDeviceYawOffset);

        // Cardinal labels
        string devTrueLabel = HeadingToCardinal(deviceTrue);
        string devMagLabel  = HeadingToCardinal(deviceMag);
        string unityLabel   = HeadingToCardinal(unityAligned);

        // Explain why device shows N/A
        string statusLine = "";
        if (!Input.compass.enabled) statusLine = "Compass disabled by OS/user";
        else if (Input.location.status != LocationServiceStatus.Running) statusLine = "Location not running";
        else if (deviceTrue < 0) statusLine = "Awaiting compass samples…";

        debugText.text =
            $"📱 Device True: {(deviceTrue >= 0 ? deviceTrue.ToString("F1") + "° " + devTrueLabel : "N/A")}\n" +
            $"🧲 Device Magnetic: {(deviceMag >= 0 ? deviceMag.ToString("F1") + "° " + devMagLabel : "N/A")}\n" +
            $"🎮 Unity Camera: {unityAligned:F1}° {unityLabel}\n" +
            $"🎯 Accuracy: {(acc >= 0 ? acc.ToString("F1") + "°" : "N/A")} | Gyro: {(SystemInfo.supportsGyroscope ? "Yes" : "No")}\n" +
            $"{(string.IsNullOrEmpty(statusLine) ? "" : "ℹ️ " + statusLine)}";
    }

    // Call this from a button to align Unity display to current device heading
    public void AlignNow()
    {
        var (deviceTrue, _) = GetDeviceHeadings();
        float unity = GetUnityHeading();
        if (deviceTrue >= 0)
            _unityToDeviceYawOffset = DeltaAngle(unity, deviceTrue);
    }

    // --- helpers ---
    (float deviceTrue, float deviceMag) GetDeviceHeadings()
    {
        // Returns -1 if data isn't ready
        float trueH = Input.compass.enabled ? Input.compass.trueHeading : -1f;
        float magH  = Input.compass.enabled ? Input.compass.magneticHeading : -1f;

#if UNITY_ANDROID
        // Many Android devices report trueHeading = magneticHeading when declination is unknown.
        // If trueHeading is 0 with poor accuracy, treat as not-ready.
        if (Input.compass.headingAccuracy < 0) trueH = -1f;
#endif
        return (SanitizeHeading(trueH), SanitizeHeading(magH));
    }

    float GetUnityHeading()
    {
        var cam = Camera.main;
        if (!cam) return 0f;
        Vector3 f = cam.transform.forward; f.y = 0f;
        if (f.sqrMagnitude < 1e-6f) return 0f;
        float deg = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg; // z-forward convention
        if (deg < 0) deg += 360f;
        return deg;
    }

    float SanitizeHeading(float h) => (h < 0 ? -1f : Wrap360(h));
    float Wrap360(float a) { a %= 360f; if (a < 0) a += 360f; return a; }
    float DeltaAngle(float from, float to) => Mathf.DeltaAngle(from, to);

    string HeadingToCardinal(float heading)
    {
        if (heading < 0) return "N/A";
        // 8-way
        string[] dirs = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
        int idx = Mathf.RoundToInt(heading / 45f) % 8;
        return dirs[idx];
    }
}
