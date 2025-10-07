using System.Collections;
using UnityEngine;
using TMPro;

#if UNITY_ANDROID
using UnityEngine.Android;
#endif

/// <summary>
/// Robust Android compass + permissions + OS-settings helper.
/// - Requests Fine+Coarse at runtime
/// - Verifies OS Location Services ON, can open Settings
/// - Detects magnetometer presence
/// - Enables compass and waits for first readings
/// - Optionally aligns a parent transform's north to device north
/// Attach this to any GameObject in your first AR scene.
/// </summary>
public class CompassPermissionDiag : MonoBehaviour
{
    [Header("Optional UI")]
    [SerializeField] private TMP_Text debugText;

    [Header("Timings")]
    [Tooltip("Max seconds to wait for Unity LocationService to start (for trueHeading).")]
    [SerializeField] private float locationStartTimeoutSec = 12f;
    [Tooltip("Max seconds to wait for a first non-NaN heading after enabling compass.")]
    [SerializeField] private float firstHeadingTimeoutSec = 10f;
    [Tooltip("If we need to re-check things periodically (OEM throttling).")]
    [SerializeField] private float periodicRecheckSec = 2.0f;

    [Header("North Alignment (optional)")]
    [Tooltip("Rotate this Transform around Y so Unity world-north matches device north.")]
    [SerializeField] private Transform northAlignRoot;
    [Tooltip("Auto-rotate once when first valid heading arrives.")]
    [SerializeField] private bool autoAlignOnce = true;
    [Tooltip("Gently trim orientation each frame to reduce drift.")]
    [SerializeField] private bool continuousTrim = false;
    [Range(0.01f, 1f)]
    [SerializeField] private float trimLerp = 0.08f;

    // state
    private bool didAutoAlign = false;
    private string lastReason = "";

    // UI helper
    private void SetText(string s)
    {
        if (debugText != null) debugText.text = s;
    }

    private void Start()
    {
        StartCoroutine(BootstrapAndroid());
    }

    private IEnumerator BootstrapAndroid()
    {
        SetText("Starting compass bootstrap...");

#if UNITY_ANDROID
        // --- 1) Request runtime permissions (Fine + Coarse) ---
        if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
            Permission.RequestUserPermission(Permission.FineLocation);
        yield return null; // let the dialog show

        // Some OEMs behave better if Coarse is explicitly requested as well
        if (!Permission.HasUserAuthorizedPermission(Permission.CoarseLocation))
            Permission.RequestUserPermission(Permission.CoarseLocation);
        yield return null;

        // Re-check after dialogs
        bool fine = Permission.HasUserAuthorizedPermission(Permission.FineLocation);
        bool coarse = Permission.HasUserAuthorizedPermission(Permission.CoarseLocation);

        // Note: we do NOT need Background location for compass in-foreground; keep it out unless required.

        // --- 2) Verify device actually has a magnetometer ---
        if (!HasMagnetometer_Android())
        {
            Report("No magnetometer sensor on this device. Compass is unavailable.");
            yield break;
        }

        // --- 3) Verify OS Location Services are ON (trueHeading needs it; many OEMs also gate magnetic) ---
        if (!IsAndroidLocationEnabled())
        {
            Report("Location Services are OFF. Please enable them in Settings > Location.");
            // Offer to open settings
            OpenAndroidLocationSettings();
            // give user time to toggle, then we’ll re-check
            yield return new WaitForSeconds(2f);
        }

        // --- 4) Start LocationService (for trueHeading/declination & for some OEM gating) ---
        if (Input.location.status == LocationServiceStatus.Stopped)
        {
            Input.location.Start(1f, 0.1f); // high accuracy, small updates
        }

        // Wait for Running or time out (still OK to proceed; we’ll fall back to magneticHeading)
        float t = 0f;
        while (t < locationStartTimeoutSec &&
               (Input.location.status == LocationServiceStatus.Initializing ||
                Input.location.status == LocationServiceStatus.Stopped))
        {
            t += Time.deltaTime;
            SetText($"Waiting for LocationService... ({t:F1}s) status={Input.location.status}");
            yield return null;
        }

        // --- 5) Enable compass and wait for first reading ---
        Input.compass.enabled = true;

        float hWait = 0f;
        while (hWait < firstHeadingTimeoutSec && !HasHeadingData())
        {
            hWait += Time.deltaTime;
            SetText($"Waiting for compass data... ({hWait:F1}s)");
            yield return null;
        }

        if (!HasHeadingData())
        {
            // Some OEMs throttle sensors if device is stationary or screen is off.
            Report("No heading yet. Move/rotate the device slightly; some devices throttle sensors.");
            // continue anyway; Update() will keep checking periodically.
        }

#else
        // Non-Android: just enable and go.
        Input.location.Start(1f, 0.1f);
        Input.compass.enabled = true;
#endif

        SetText("Compass bootstrap complete.");
    }

    private void Update()
    {
        // Real-time reason string for on-screen debug
        string reason = DiagnoseReasonInline();
        if (reason != lastReason)
        {
            Debug.Log($"[CompassDiag] {reason}");
            lastReason = reason;
        }

        float deviceTrue = (Input.compass.enabled && Input.location.status == LocationServiceStatus.Running)
            ? Normalize0to360(Input.compass.trueHeading)
            : float.NaN;
        float deviceMag = Input.compass.enabled ? Normalize0to360(Input.compass.magneticHeading) : float.NaN;

        float unityHeading = GetUnityCameraHeading();

        string deviceHeadingStr = !float.IsNaN(deviceTrue)
            ? $"{deviceTrue:F1}° (true)"
            : (!float.IsNaN(deviceMag) ? $"{deviceMag:F1}° (mag)" : "NA");

        string ui =
            $"Device Compass: {deviceHeadingStr}\n" +
            $"Unity Camera:   {(float.IsNaN(unityHeading) ? "NA" : $"{unityHeading:F1}°")}\n" +
            $"Reason: {reason}";
        SetText(ui);

        // Optional north alignment to anchor AR world to real north
        AlignNorthIfPossible(unityHeading);

        // Periodic re-check (OEM throttling / user toggled settings)
        _recheckTimer += Time.deltaTime;
        if (_recheckTimer >= periodicRecheckSec)
        {
            _recheckTimer = 0f;
            PeriodicKeeps();
        }
    }

    // --- alignment ---
    private void AlignNorthIfPossible(float unityHeading)
    {
        if (northAlignRoot == null) return;
        if (!Input.compass.enabled) return;

        float deviceHeadingDeg;
        if (!TryGetBestDeviceHeading(out deviceHeadingDeg)) return;

        float delta = Mathf.DeltaAngle(unityHeading, deviceHeadingDeg);

        if (autoAlignOnce && !didAutoAlign)
        {
            northAlignRoot.Rotate(0f, delta, 0f, Space.World);
            didAutoAlign = true;
        }
        else if (continuousTrim)
        {
            float step = delta * Mathf.Clamp01(trimLerp);
            northAlignRoot.Rotate(0f, step, 0f, Space.World);
        }
    }

    private bool TryGetBestDeviceHeading(out float headingDeg)
    {
        headingDeg = float.NaN;
        if (!Input.compass.enabled) return false;

        bool locRunning = Input.location.status == LocationServiceStatus.Running;
        bool trueValid = locRunning && Input.compass.headingAccuracy >= 0f &&
                         (Input.compass.trueHeading > 0f || Input.compass.trueHeading < 0f);

        if (trueValid)
        {
            headingDeg = Normalize0to360(Input.compass.trueHeading);
            return true;
        }

        bool magValid = (Input.compass.magneticHeading > 0f || Input.compass.magneticHeading < 0f);
        if (magValid)
        {
            headingDeg = Normalize0to360(Input.compass.magneticHeading);
            return true;
        }

        return false;
    }

    private float Normalize0to360(float deg)
    {
        if (float.IsNaN(deg)) return deg;
        deg %= 360f;
        if (deg < 0f) deg += 360f;
        return deg;
    }

    private float GetUnityCameraHeading()
    {
        var cam = Camera.main;
        if (cam == null) return float.NaN;
        Vector3 f = cam.transform.forward;
        f.y = 0f;
        if (f.sqrMagnitude < 1e-6f) return float.NaN;
        float hdg = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg; // z+ = North (0°)
        if (hdg < 0) hdg += 360f;
        return hdg;
    }

    private bool HasHeadingData()
    {
        if (!Input.compass.enabled) return false;
        bool magOk = Input.compass.magneticHeading > 0f || Input.compass.magneticHeading < 0f;
        bool trueOk = Input.compass.trueHeading > 0f || Input.compass.trueHeading < 0f;
        return magOk || trueOk;
    }

    private string DiagnoseReasonInline()
    {
        if (!Input.compass.enabled)
            return "Compass disabled by OS/User (or not enabled yet).";

#if UNITY_ANDROID
        if (!HasMagnetometer_Android())
            return "No magnetometer sensor present.";
#endif

        switch (Input.location.status)
        {
            case LocationServiceStatus.Stopped:
                return "Location service stopped (OS setting off).";
            case LocationServiceStatus.Failed:
                return "Location failed (permission denied/provider unavailable).";
            case LocationServiceStatus.Initializing:
                return "Location initializing...";
            case LocationServiceStatus.Running:
                break;
        }

        if (!HasHeadingData())
            return "No heading yet (OEM throttling—move device).";

        return "OK";
    }

    private void Report(string msg)
    {
        Debug.LogWarning("[CompassDiag] " + msg);
        SetText(msg);
        lastReason = msg;
    }

    // --- periodic keep-alives / retries (OEM quirks) ---
    private float _recheckTimer = 0f;
    private void PeriodicKeeps()
    {
        // If user toggled Location ON after we started, try again
        if (Input.location.status == LocationServiceStatus.Stopped && IsAndroidLocationEnabled())
        {
            Input.location.Start(1f, 0.1f);
        }
        // If compass somehow turned off (some OEMs do after resume), turn it back on
        if (!Input.compass.enabled)
        {
            Input.compass.enabled = true;
        }
    }

    // ---------- ANDROID interop ----------
#if UNITY_ANDROID
    private bool HasMagnetometer_Android()
    {
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var sensorService = activity.Call<AndroidJavaObject>("getSystemService", "sensor"))
            using (var sensorClass = new AndroidJavaClass("android.hardware.Sensor"))
            {
                int TYPE_MAGNETIC_FIELD = sensorClass.GetStatic<int>("TYPE_MAGNETIC_FIELD");
                var sensor = sensorService.Call<AndroidJavaObject>("getDefaultSensor", TYPE_MAGNETIC_FIELD);
                return sensor != null;
            }
        }
        catch
        {
            // fall back heuristic
            return Input.compass.rawVector != Vector3.zero;
        }
    }

    private bool IsAndroidLocationEnabled()
    {
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var cr = activity.Call<AndroidJavaObject>("getContentResolver"))
            using (var secure = new AndroidJavaClass("android.provider.Settings$Secure"))
            {
                int mode = secure.CallStatic<int>("getInt", cr, "location_mode", 0);
                // 0 = OFF, 1 = SENSORS_ONLY, 2 = BATTERY_SAVING, 3 = HIGH_ACCURACY
                return mode != 0;
            }
        }
        catch { return true; }
    }

    private void OpenAndroidLocationSettings()
    {
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var intent = new AndroidJavaObject("android.content.Intent",
                       "android.provider.Settings$ACTION_LOCATION_SOURCE_SETTINGS"))
            {
                activity.Call("startActivity", intent);
            }
        }
        catch
        {
            // no-op
        }
    }
#endif
}