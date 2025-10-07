using System;
using System.Collections;
using UnityEngine;
using TMPro;
using UnityEngine.InputSystem;

#if UNITY_ANDROID
using UnityEngine.Android;
#endif

public class CompassNewInputSystem : MonoBehaviour
{
    [Header("Optional UI")]
    [SerializeField] private TMP_Text debugText;

    [Header("Timings")]
    [SerializeField] private float firstReadingTimeoutSec = 10f;
    [SerializeField] private float periodicRecheckSec = 2f;

    [Header("North Alignment (optional)")]
    [SerializeField] private Transform northAlignRoot;
    [SerializeField] private bool autoAlignOnce = true;
    [SerializeField] private bool continuousTrim = false;
    [Range(0.01f, 1f)]
    [SerializeField] private float trimLerp = 0.08f;

    [Header("Stability")]
    [SerializeField] private float headingChangeThreshold = 5f; // degrees
    [SerializeField] private float stabilizationTime = 3f;      // seconds before allowing changes
    private float lastAppliedHeading = 0f;
    private float headingStableTimer = 0f;
    private bool isHeadingStable = false;

    [Header("True North (optional)")]
    [Tooltip("If enabled and LocationService is running, apply declination to magnetic heading.")]
    [SerializeField] private bool applyTrueNorth = false;

    // state
    private bool _didAutoAlign = false;
    private float _recheckTimer = 0f;
    private string _lastReason = "";
    private bool northAligned = false;

    // Cached sensors
    private MagneticFieldSensor _mag;
    private GravitySensor _grav;         // Preferred
    private Accelerometer _accel;        // Fallback

    private void SetText(string s) { if (debugText != null) debugText.text = s; }
public bool IsAligned => _didAutoAlign && northAligned;
    private void OnEnable()
    {
        _mag = MagneticFieldSensor.current;
        _grav = GravitySensor.current;
        _accel = Accelerometer.current;

        if (_mag != null) InputSystem.EnableDevice(_mag);
        if (_grav != null) InputSystem.EnableDevice(_grav);
        else if (_accel != null) InputSystem.EnableDevice(_accel);

        SetSampling(_mag, 30);
        SetSampling(_grav, 30);
        SetSampling(_accel, 30);

        if (applyTrueNorth && Input.location.status == LocationServiceStatus.Stopped)
            Input.location.Start(1f, 0.1f);

        StartCoroutine(WaitForFirstReading());
    }

    private void SetSampling(Sensor sensor, float hz)
    {
        if (sensor == null) return;
        try { sensor.samplingFrequency = hz; } catch { }
    }

    private IEnumerator WaitForFirstReading()
    {
        float t = 0f;
        while (t < firstReadingTimeoutSec && !HasMagAndGravity())
        {
            t += Time.deltaTime;
            SetText($"Waiting for sensors... ({t:F1}s)\n{BackendSummary()}");
            yield return null;
        }
        if (!HasMagAndGravity())
            Debug.LogWarning("[CompassNIS] Sensor data not ready yet; will keep polling.");
    }

    private void Update()
    {
        string reason = DiagnoseReason();
        if (reason != _lastReason)
        {
            Debug.Log($"[CompassNIS] {reason}");
            _lastReason = reason;
        }

        bool ok = TryGetHeading(out float magneticDeg, out float? trueDeg);

        float unityHeading = GetUnityCameraHeading();
        string headStr = ok
            ? (applyTrueNorth && trueDeg.HasValue
                ? $"{trueDeg.Value:F1}° (true) / {magneticDeg:F1}° (mag)"
                : $"{magneticDeg:F1}° (mag)")
            : "NA";

        string ui =
            $"Device Heading: {headStr}\n" +
            $"Unity Camera:   {(float.IsNaN(unityHeading) ? "NA" : $"{unityHeading:F1}°")}\n" +
            $"{BackendSummary()}\n" +
            $"Reason: {reason}";
        SetText(ui);

        // keep your original call site working (two-arg overload below)
        AlignNorthIfPossible(
            unityHeading,
            (applyTrueNorth && trueDeg.HasValue) ? trueDeg.Value : magneticDeg
        );

        _recheckTimer += Time.deltaTime;
        if (_recheckTimer >= periodicRecheckSec)
        {
            _recheckTimer = 0f;

#if UNITY_ANDROID
            if (applyTrueNorth && Input.location.status == LocationServiceStatus.Stopped && IsAndroidLocationEnabled())
                Input.location.Start(1f, 0.1f);
#endif
        }
    }

    private bool HasMagAndGravity() => _mag != null && (_grav != null || _accel != null);

    // ---------- Heading computation (tilt-compensated) ----------
    public bool TryGetHeading(out float magneticDeg, out float? trueDeg)
    {
        magneticDeg = float.NaN;
        trueDeg = null;

        if (_mag == null) return false;

        Vector3 m = _mag.magneticField.ReadValue(); // μT

        Vector3 g;
        if (_grav != null) g = _grav.gravity.ReadValue();
        else if (_accel != null) g = LowPassAccel(_accel.acceleration.ReadValue());
        else return false;

        if (g.sqrMagnitude < 1e-6f || m.sqrMagnitude < 1e-6f) return false;

        Vector3 gN = g.normalized;
        Vector3 mN = m.normalized;

        Vector3 east = Vector3.Cross(mN, gN).normalized;
        if (east.sqrMagnitude < 1e-6f) return false;

        Vector3 north = Vector3.Cross(gN, east).normalized;

        Vector2 n2 = new Vector2(north.x, north.y).normalized;
        if (n2.sqrMagnitude < 1e-6f) return false;

        float deg = Mathf.Atan2(n2.x, n2.y) * Mathf.Rad2Deg; // 0 at +Y, +CW
        if (deg < 0) deg += 360f;
        magneticDeg = deg;

#if UNITY_ANDROID
        if (applyTrueNorth && Input.location.status == LocationServiceStatus.Running)
        {
            if (TryGetDeclinationDegrees(out float decl))
            {
                float tdeg = magneticDeg + decl; // add East, subtract West
                tdeg %= 360f; if (tdeg < 0) tdeg += 360f;
                trueDeg = tdeg;
            }
        }
#endif
        return true;
    }

    private Vector3 _accelLP;
    private Vector3 LowPassAccel(Vector3 a)
    {
        float alpha = 0.1f;
        _accelLP = Vector3.Lerp(_accelLP, a, alpha);
        return _accelLP;
    }

    // === NORTH ALIGNMENT ===
  private void AlignNorthIfPossible(float unityHeading, float deviceHeadingDeg)
{
    if (northAlignRoot == null) return;
    if (float.IsNaN(unityHeading) || float.IsNaN(deviceHeadingDeg)) return;

    float delta = Mathf.DeltaAngle(unityHeading, deviceHeadingDeg);
    float headingChange = Mathf.Abs(Mathf.DeltaAngle(lastAppliedHeading, deviceHeadingDeg));

    if (headingChange < headingChangeThreshold)
    {
        headingStableTimer += Time.deltaTime;
        if (headingStableTimer >= stabilizationTime)
            isHeadingStable = true;
    }
    else
    {
        headingStableTimer = 0f;
        isHeadingStable = false;
        lastAppliedHeading = deviceHeadingDeg;
    }

    // one-time align
    if (autoAlignOnce && !_didAutoAlign && isHeadingStable)
    {
        northAlignRoot.Rotate(0f, delta, 0f, Space.World);
        _didAutoAlign = true;
        northAligned = true;  // ADD THIS LINE if it's missing
        lastAppliedHeading = deviceHeadingDeg;
        Debug.Log($"[Compass] Aligned north (Δ={delta:F1}°)");
    }

        // gentle trim
        else if (continuousTrim && isHeadingStable && Mathf.Abs(delta) > headingChangeThreshold)
        {
            float step = delta * Mathf.Clamp01(trimLerp);
            northAlignRoot.Rotate(0f, step, 0f, Space.World);
            lastAppliedHeading = deviceHeadingDeg;
        }
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

    private string DiagnoseReason()
    {
        if (_mag == null)
            return "No MagneticFieldSensor (device lacks magnetometer or backend disabled).";
        if (_grav == null && _accel == null)
            return "No Gravity/Accelerometer sensor present.";
        if (!HasMagAndGravity())
            return "Sensors not ready yet.";
#if UNITY_ANDROID
        if (!IsAndroidLocationEnabled() && applyTrueNorth)
            return "Android Location OFF (true north correction unavailable).";
#endif
        return "OK";
    }

    private string BackendSummary()
    {
        bool hasMag = MagneticFieldSensor.current != null;
        bool hasGrav = GravitySensor.current != null;
        bool hasAcc = Accelerometer.current != null;

#if ENABLE_INPUT_SYSTEM
        string mode = "New Input System";
#else
        string mode = "Unknown";
#endif
        return $"Backend: {mode}, Mag:{hasMag}, Grav:{hasGrav}, Accel:{hasAcc}, Loc:{Input.location.status}";
    }

#if UNITY_ANDROID
    private bool TryGetDeclinationDegrees(out float declinationDeg)
    {
        declinationDeg = 0f;
        try
        {
            if (Input.location.status != LocationServiceStatus.Running) return false;
            var last = Input.location.lastData;

            using (var geo = new AndroidJavaObject(
                "android.hardware.GeomagneticField",
                (float)last.latitude,
                (float)last.longitude,
                (float)last.altitude,
                JavaTimeNowMillis()))
            {
                declinationDeg = geo.Call<float>("getDeclination");
                return true;
            }
        }
        catch { return false; }
    }

    private long JavaTimeNowMillis()
    {
        var dt = DateTimeOffset.UtcNow;
        return dt.ToUnixTimeMilliseconds();
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
                return mode != 0;
            }
        }
        catch { return true; }
    }
#endif
}
