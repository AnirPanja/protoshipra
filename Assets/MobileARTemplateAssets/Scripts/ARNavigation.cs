using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using TMPro;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// ARNavigation (GPS-course based; NO COMPASS) – polyline-aware navigation + spawn/anchor helpers
/// Place on a manager GameObject under your AR scene.
/// Key idea: Never rotate world to North. Use ENU from geo origin + GPS-derived course for UI.
/// </summary>
public class ARNavigation : MonoBehaviour
{
    [Header("Visibility radius")]
    [SerializeField] float showRadiusMeters = 120f;
    [SerializeField] float hideRadiusMeters = 140f; // must be > showRadius
    [Header("AR Managers")]
    [SerializeField] private ARRaycastManager raycastManager;   // assign in Inspector
    [SerializeField] private ARPlaneManager planeManager;       // optional
    [SerializeField] private bool lockToPlaneIfAvailable = true;
    // private Queue<float> alongWindow = new Queue<float>();
    // [SerializeField] private int alongWindowSize = 4; // tweak 3–6 if you like
    private float smoothedAlong = 0f;

    [SerializeField] private bool alignWorldToPathAtStart = true;
    private bool didAlignWorldToPath = false;

    private Dictionary<GameObject, bool> _vis = new();

    [Header("AR Prefabs")]
    [SerializeField] private GameObject pathArrowPrefab;
    public GameObject arBannerPrefab;    // optional
    public GameObject defaultPrefab;     // fallback

    [Header("AR Anchor & Hierarchy")]
    public Transform anchorRoot;         // optional parent
    public ARAnchorManager arAnchorManager;

    [Header("Spawn Settings")]
    public float spawnYOffset = 0.2f;

    [Header("UI")]
    [SerializeField] private Image uiArrow;
    [SerializeField] private TMP_Text stepsPreviewText;
    [SerializeField] private TMP_Text bigInstructionText;
    [SerializeField] public TMP_Text debugText;
    [SerializeField] private TMP_Text thresholdTurnText;
    [SerializeField] private TMP_Text arObjectsDistanceText;

    [Header("Tuning")]
    [SerializeField] private float resampleIntervalMeters = 5f;
    [SerializeField] private float lookaheadMeters = 12f;
    [SerializeField] private float arrowSmoothing = 4f;
    [SerializeField] private float stepAdvanceMeters = 8f;
    [SerializeField] private float arrivalDistanceMeters = 8f;
    [SerializeField] private float turnAnnouncementThreshold = 50f;
    [SerializeField] private float dedupeStepMeters = 12f;
    [SerializeField] private float minorTurnAngle = 30f;
    [SerializeField] private float majorTurnAngle = 60f;
    [SerializeField] private int previewSegmentLimit = 6;
    [SerializeField] private bool debugLogs = false;
    // fields (top of class)


    
    [Header("Google API (dev)")]
    [SerializeField] private string googleApiKey = "YOUR_API_KEY_HERE";

    [Header("AR Guidance Arrow (visual)")]
    [SerializeField] private GameObject guidanceArrowPrefab;
    [SerializeField] private float guidanceArrowDistance = 3.0f;
    [SerializeField] private float guidanceArrowHeightOffset = -0.25f;
    [SerializeField] private float guidanceArrowRotateSpeed = 8f;

    [Header("Force objects visible on camera (screen-projection)")]
    [Tooltip("If true, AR objects will be placed relative to the camera so they are always visible on-screen.")]
    [SerializeField] private bool forceScreenPlacement = false;
    [SerializeField] private float forcedDisplayDistance = 15f;
    [SerializeField] private float forcedHeightAboveCamera = 2f;

    [Header("Sequential spawn by distance")]
    [SerializeField] private bool sequentialSpawnByDistance = true;
    [SerializeField] private float spawnStaggerSeconds = 0.12f;

    [Header("Vertical tuning (GPS / demo)")]
    [SerializeField] private float arGlobalHeightOffset = 0f;
    [SerializeField] private float maxVerticalDeltaFromCamera = 20f;

    [Header("Guidance Arrow Stability")]
    [SerializeField] private bool guidanceParentToCamera = true;
    [SerializeField] private float guidanceSmoothingTime = 0.15f;


    [Header("AR Origin & GPS Settings")]
    // (no northAlignRoot / compass now)

    [Header("Debug UI (optional)")]
    [SerializeField] private TMP_Text alignmentStatusText;  // now reports GPS origin status
    [SerializeField] private TMP_Text unityCompassText;     // repurposed to show GPS course (not magnetometer)
[Header("Banner Billboard Defaults")]
[SerializeField] private bool bannerBillboardYawOnly = true;   // keep rotation only on Y
[SerializeField] private bool bannerFlipForward = false;       // set TRUE if your banner looks backward
[SerializeField] private float bannerExtraYawDegrees = 0f; 

    [Header("Scene Roots")]
[SerializeField] private Transform contentRoot; 

    [Header("Destination Marker")]
    [SerializeField] private float destinationShowMeters = 30f;
[SerializeField] private GameObject destinationPrefab;     // <-- set this in Inspector
[SerializeField] private float destinationHeightOffset = 0f;
[SerializeField] private Sprite destinationIcon;
[SerializeField] private Material destinationMaterial;
[SerializeField] private float destinationPreviewMeters = 40f;  // show preview when <= 40m
[SerializeField] private float destinationPreviewHideMeters = 55f; // hysteresis to hide if you walk away

[SerializeField] private GameObject destinationPreviewPrefab; // optional: lighter UI-only prefab
[SerializeField] private float destinationPreviewHeightOffset = 0.0f;

[Header("Feedback Bot Integration")]
[SerializeField] private FeedbackVoiceBot feedbackBot;   // drag your FeedbackVoiceBot here (optional; auto-find)
[SerializeField] private bool autoStartFeedbackOnArrival = true;
private bool feedbackStartedOnce = false;
private GameObject destinationPreviewGO = null;
private bool destinationWorldSpawned = false; // separate from preview

private bool destinationSpawned = false;

    // --- Runtime state ---
    private bool originSet = false;
    private double originLat = 0.0;
    private double originLon = 0.0;
    private float originAlt = 0f;

    private double currentLat;
    private double currentLon;
    private float currentAlt;

    // GPS-derived heading/course (degrees, 0..360; 0 = North)
    private float gpsCourseDeg = 0f;
    private bool hasCourse = false;

    // private float stableAlong = 0f;   // filtered along we trust
    // private float lastStableAlong = 0f;

    // smooth UI yaw
    private float uiCurrentZ = 0f;

    private float lastAlignedHeading = 0f;
    private float headingLerpSpeed = 2.5f;   // adjust: higher = faster alignment
    private float headingThreshold = 30f;    // degrees difference to trigger rotation
    private bool isAligning = false;

    // Managers/Transforms
    private ARAnchorManager anchorManager;
    private Transform originTransform = null;  // ARSessionOrigin / XROrigin if present
    private object xrOriginInstance = null;

    private bool hasAlignedOnce = false;

    private Coroutine alignRoutine = null;


    // path & routing
    [System.Serializable] public class Polyline { public string points; }
    [System.Serializable] public class Location { public float lat; public float lng; }
    [System.Serializable] public class RouteResponse { public Route[] routes; }
    [System.Serializable] public class Route { public Leg[] legs; }
    [System.Serializable] public class Leg { public Step[] steps; public Location start_location; }
    [System.Serializable] public class Step
    {
        public string maneuver;
        public Location end_location;
        public Location start_location;
        public string html_instructions;
        public Polyline polyline;
    }

    private List<Step> steps = new List<Step>();
    private List<Vector2> path = new List<Vector2>();
    private List<float> cumulative = new List<float>();
    private List<int> stepStartIndex = new List<int>();
    private List<int> stepEndIndex = new List<int>();
    private List<float> stepStartAlong = new List<float>();
    private List<float> stepEndAlong = new List<float>();
    private List<GameObject> pathArrows = new List<GameObject>();
    private bool directionsFetched = false;
    private Vector2 destination = Vector2.zero;
    private int currentStepIndex = 0;
private string destDisplayName = "Destination";
    private Queue<float> alongWindow = new Queue<float>();
    private int alongWindowSize = 4;
    // private float smoothedAlong = 0f;

    // guidance arrow
    private GameObject guidanceArrowInstance = null;
    private string guidanceLabel = "Go straight";
    private float guidanceCurrentYaw = 0f;
    private float guidanceYawVelocity = 0f;

    [System.Serializable]
    public class ARSpawnPoint
    {
        public string name;
        public double lat;
        public double lon;
        public GameObject prefab;
        public Sprite icon;
        public Material material;
        public float heightOffset = 0f;
        public bool enabled = true;
    }

    [Header("Multiple AR Spawn Points")]
    [SerializeField] private List<ARSpawnPoint> arSpawnPoints = new List<ARSpawnPoint>();
    [SerializeField] private bool spawnAllOnDirectionsReady = true;
    [SerializeField] private bool faceSpawnedObjectsEveryFrame = true;
    [SerializeField] private bool updateSpawnedPositionsWithGPS = true;



    // spawned storage
    private List<GameObject> spawnedARObjects = new List<GameObject>();
    private List<ARSpawnPoint> spawnedARData = new List<ARSpawnPoint>();

    // ====== GPS COURSE BUFFER (Maps-like heading) ======
    private struct GpsFix { public double lat, lon; public double t; }
    private readonly Queue<GpsFix> courseBuffer = new Queue<GpsFix>();
    [SerializeField] private float courseWindowSeconds = 5.0f;   // was 3.0
    [SerializeField] private float minSpeedForCourseMps = 0.5f;  // was 0.8

    [SerializeField] private bool enableWorldAutoAlign = false;

    [SerializeField] private bool smoothGpsUpdates = true;       // use hysteresis instead of per-frame snaps
    [SerializeField] private float reprojectIfMeters = 8f;       // only move if > 8 m off
    [SerializeField] private float followLerp = 2f;              // calm follow speed

    // ====== LIFECYCLE ======
    void Awake()
    {
        anchorManager = FindObjectOfType<ARAnchorManager>();
        if (raycastManager == null) raycastManager = FindObjectOfType<ARRaycastManager>();
        if (planeManager == null) planeManager = FindObjectOfType<ARPlaneManager>();

        // Prefer ARSessionOrigin / XROrigin as frame (no north rotation!)
        var arOrigin = FindObjectOfType<ARSessionOrigin>();
        if (arOrigin != null)
        {
            originTransform = arOrigin.transform;
            if (debugLogs) Debug.Log("ARNavigation: Using ARSessionOrigin as originTransform.");
        }
        else
        {
            Type xrOriginType = Type.GetType("Unity.XR.CoreUtils.XROrigin, Unity.XR.CoreUtils")
                                ?? Type.GetType("Unity.XR.ARFoundation.ARRig, Unity.XR.ARFoundation")
                                ?? Type.GetType("UnityEngine.XR.Interaction.Toolkit.XRRig, Unity.XR.Interaction.Toolkit");
            if (xrOriginType != null)
            {
                var found = FindObjectOfType(xrOriginType);
                if (found != null && found is Component comp)
                {
                    originTransform = comp.transform;
                    xrOriginInstance = found;
                    if (debugLogs) Debug.Log($"ARNavigation: Using {xrOriginType.Name} as originTransform.");
                }
            }
        }
        if (originTransform == null && debugLogs)
            Debug.LogWarning("ARNavigation: No ARSessionOrigin/XROrigin found; using world coordinates as-is.");
    }

    void Start()
    {
        // NO compass here
        Input.location.Start();

        if (uiArrow != null) uiArrow.gameObject.SetActive(true);
        if (stepsPreviewText != null) stepsPreviewText.gameObject.SetActive(false);
        if (bigInstructionText != null) bigInstructionText.text = "";

        if (guidanceArrowPrefab != null)
        {
            guidanceArrowInstance = Instantiate(guidanceArrowPrefab);
            guidanceArrowInstance.SetActive(false);
            if (guidanceParentToCamera && Camera.main != null)
            {
                guidanceArrowInstance.transform.SetParent(Camera.main.transform, false);
                guidanceArrowInstance.transform.localPosition = Vector3.forward * guidanceArrowDistance + Vector3.up * guidanceArrowHeightOffset;
                guidanceArrowInstance.transform.localRotation = Quaternion.identity;
            }
        }
if (feedbackBot == null) feedbackBot = FindObjectOfType<FeedbackVoiceBot>();

        SetUI(alignmentStatusText, "GPS Origin: pending…");
        SetUI(unityCompassText, "Course: (waiting for movement)");

        StartCoroutine(InitAndMaybeStartNav());
    }

    IEnumerator InitAndMaybeStartNav()
    {
        // Wait for GPS
        float waitTimeout = 10f;
        float t = 0f;
        while (Input.location.status != LocationServiceStatus.Running && t < waitTimeout)
        {
            t += Time.deltaTime;
            yield return null;
        }

        if (Input.location.status == LocationServiceStatus.Running)
        {
            currentLat = Input.location.lastData.latitude;
            currentLon = Input.location.lastData.longitude;
            originLat  = currentLat;
            originLon  = currentLon;
            originAlt  = Input.location.lastData.altitude;
            currentAlt = originAlt;
            originSet  = true;

            SetUI(alignmentStatusText, $"GPS Origin set ✓  ({originLat:F6}, {originLon:F6})");
            if (debugLogs) Debug.Log($"Location ready. origin=({originLat},{originLon}) alt={originAlt}");
        }
        else
        {
            Debug.LogWarning("Init: location service not running after timeout.");
        }

      if (NavigationData.HasData)
{
    destination = new Vector2((float)NavigationData.Destination.latitude, (float)NavigationData.Destination.longitude);
    Vector2 source = new Vector2((float)NavigationData.Source.latitude, (float)NavigationData.Source.longitude);

    // Capture the display name locally so CheckArrival() and the debug UI can always read it
    destDisplayName = string.IsNullOrEmpty(NavigationData.DestinationName) ? "Destination" : NavigationData.DestinationName;

    Debug.Log($"Received nav data: Source ({NavigationData.Source.latitude:F8},{NavigationData.Source.longitude:F8}), " +
              $"Dest ({NavigationData.Destination.latitude:F8},{NavigationData.Destination.longitude:F8}) Name:'{destDisplayName}'");

    OnStartButtonPressed(source, destination);
if (feedbackBot == null) feedbackBot = FindObjectOfType<FeedbackVoiceBot>();
if (feedbackBot != null)
{
    feedbackBot.SetLocationName(destDisplayName);
}
    // clear the static data flag (we keep the name in destDisplayName)
    NavigationData.HasData = false;
}
else
{
    if (debugText != null)
        debugText.text = "NO NAVIGATION DATA RECEIVED!\nPlease set Source/Destination.";
}

    }

    private void AlignWorldToPathNow()
{
    if (originTransform == null || !directionsFetched || path.Count < 2) return;

    // Use your current progressed-along distance to get the local path heading
    float along = Mathf.Max(0f, smoothedAlong);
    float pathHeading = GetPathHeadingAtAlong(along); // degrees, 0 = North

    // Rotate AR world so +Z (forward) looks along the path
    AlignWorldToGpsCourseOrCamera(pathHeading);

    didAlignWorldToPath = true;
    if (debugLogs) Debug.Log($"[Align] World aligned to path heading {pathHeading:0.0}°");
}

    // ====== PUBLIC NAV API ======
    public string GetDestinationName()
{
    return string.IsNullOrEmpty(destDisplayName) ? "" : destDisplayName;
}
    public void OnStartButtonPressed(Vector2 source, Vector2 dest)
    {
        destination = new Vector2(dest.x, dest.y);
        StartNavigation(source, dest);
        Debug.Log($"Navigation Started - Source: {source.x},{source.y} Destination: {dest.x},{dest.y}");
    }

    public void StartNavigation(Vector2 source, Vector2 dest)
    {
          destinationSpawned = false;
        Debug.Log($"Hiiiiiiiiii");
        StartCoroutine(FetchDirections(source, dest));
        Debug.Log($"Byeee");
    }

    IEnumerator FetchDirections(Vector2 source, Vector2 dest)
   
    {
         Debug.Log($"Helloooo");
        if (string.IsNullOrEmpty(googleApiKey) || googleApiKey.StartsWith("YOUR_"))
            Debug.LogWarning("Set googleApiKey in Inspector for real requests.");

        string url = $"https://maps.googleapis.com/maps/api/directions/json?origin={source.x},{source.y}&destination={dest.x},{dest.y}&mode=walking&key={googleApiKey}";

        using (UnityWebRequest www = UnityWebRequest.Get(url))
        {
            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
            {
                string jsonResponse = www.downloadHandler.text;
                if (debugLogs) Debug.Log($"Directions response: {jsonResponse}");

                steps = ParseJsonForSteps(jsonResponse, out Location legStart);

                if (steps.Count > 0 && (steps[0].start_location == null ||
                   (steps[0].start_location.lat == 0 && steps[0].start_location.lng == 0)))
                {
                    steps[0].start_location = legStart ?? steps[0].end_location;
                }

                BuildPathFromStepPolylinesAndResample();
                DeduplicateCloseSteps(dedupeStepMeters);

                directionsFetched = path.Count > 0;
                currentStepIndex = 0;
                alongWindow.Clear();
                smoothedAlong = 0f;

                if (directionsFetched)
                {
                    SpawnPathArrows();
                    UpdateStepsPreview_StepBased();   // was UpdateStepsPreview();
                    UpdateBigInstruction_StepBased(); // was UpdateBigInstruction();
                    if (guidanceArrowInstance != null) guidanceArrowInstance.SetActive(true);
                    if (debugLogs) Debug.Log($"Ready: path pts {path.Count}, steps {steps.Count}");

                    if (spawnAllOnDirectionsReady)
                        StartCoroutine(SpawnAllARObjects_WhenLocationReady());
                }
            }
            else
            {
                Debug.LogError($"API error: {www.error}");
            }
        }
    }

    // ====== UPDATE LOOP ======
 void Update()
{
    if (Input.location.status != LocationServiceStatus.Running) return;

    // --- GPS fix + course (keep your buffer/course logic) ---
    var li   = Input.location.lastData;
    double newLat = li.latitude;
    double newLon = li.longitude;
    float  newAlt = li.altitude;
    double newT   = li.timestamp;

    PushCourseSample(newLat, newLon, newT);
    ComputeGpsCourse();

    currentLat = newLat;
    currentLon = newLon;
    currentAlt = newAlt;

    // Heading source for UI: prefer GPS course; fallback to camera yaw
    float heading = hasCourse
        ? gpsCourseDeg
        : (Camera.main != null ? YawFromForward(Camera.main.transform.forward) : 0f);

    // --- Optional adaptive world alignment (unchanged) ---
    if (enableWorldAutoAlign)
    {
        float delta = Mathf.Abs(Mathf.DeltaAngle(lastAlignedHeading, heading));
        if (!isAligning && (Mathf.Approximately(lastAlignedHeading, 0f) || delta > headingThreshold))
            AlignWorldToGpsCourseOrCamera(heading);
    }

    // --- No path yet? Update course UI only ---
    if (!directionsFetched || path.Count < 2)
    {
        UpdateCourseUIOnly();
        return;
    }

    // --- Step-based progress (keep your stabilized along smoothing) ---
    float rawAlong = GetAlongDistanceOfClosestPoint();
    float dt       = Mathf.Max(Time.deltaTime, 0.0001f);
    SmoothAlong(rawAlong);  

    // Keep step index in sync with along (step-based)
    AlignCurrentStepToAlong();

    // --- One-time start alignment to path tangent + center ahead (unchanged) ---
    if (alignWorldToPathAtStart && !didAlignWorldToPath)
    {
        smoothedAlong = GetAlongDistanceOfClosestPoint();
        AlignWorldToPathNow();
        SnapPathToCameraLookahead(2f, -1f);
    }

    // --- Step-based UI & guidance (ported from your working version) ---
    UpdateUIArrow_StepBased(heading);
    CheckStepProgress_StepBased();
    CheckArrival();

    UpdateBigInstruction_StepBased();
    UpdateStepsPreview_StepBased();
    UpdateThresholdTurnUI_StepBased();

    guidanceLabel = ExtractGuidanceLabelFromText(bigInstructionText != null ? bigInstructionText.text : "");
    UpdateGuidanceArrow_StepBased();

    UpdateSpawnedLabels();
    UpdateCourseUIOnly();

    // --- Spawned AR objects follow-up (unchanged, minor cleanups) ---
    if (spawnedARObjects.Count > 0 && Camera.main != null)
    {
        for (int i = spawnedARObjects.Count - 1; i >= 0; i--)
        {
            var go = spawnedARObjects[i];
            if (go == null)
            {
                spawnedARObjects.RemoveAt(i);
                if (i < spawnedARData.Count) spawnedARData.RemoveAt(i);
                continue;
            }

            if (faceSpawnedObjectsEveryFrame && !forceScreenPlacement)
                FaceObjectToCamera(go, Camera.main);

            if (updateSpawnedPositionsWithGPS && i < spawnedARData.Count && !forceScreenPlacement)
            {
                var d = spawnedARData[i];

                // ENU meters from geo origin -> world
                Vector3 enu = LatLonToUnity_Precise(d.lat, d.lon);
                Vector3 newPos = (originTransform != null)
                    ? originTransform.TransformPoint(enu)
                    : enu;

                float baseAlt = originSet ? originAlt : currentAlt;
                float targetY = !float.IsNaN(baseAlt)
                    ? baseAlt + arGlobalHeightOffset + d.heightOffset
                    : (Camera.main ? Camera.main.transform.position.y : 0f) + forcedHeightAboveCamera + d.heightOffset;

                if (Camera.main != null && !float.IsNaN(baseAlt))
                {
                    float camY = Camera.main.transform.position.y;
                    if (Mathf.Abs(targetY - camY) > maxVerticalDeltaFromCamera)
                        targetY = camY + Mathf.Sign(targetY - camY) * maxVerticalDeltaFromCamera;
                }

                newPos.y = targetY;

                if (forceScreenPlacement && Camera.main != null)
                    newPos = ProjectToCameraFrustum(Camera.main, newPos, forcedDisplayDistance);

                if (smoothGpsUpdates)
                {
                    float err = Vector3.Distance(go.transform.position, newPos);
                    if (err > reprojectIfMeters)
                        go.transform.position = Vector3.Lerp(go.transform.position, newPos, Time.deltaTime * followLerp);
                }
                else
                {
                    go.transform.position = Vector3.Lerp(go.transform.position, newPos, Time.deltaTime * 6f);
                }
            }
        }
    }

    UpdateARObjectsVisibility();
}


    // ====== COURSE (Maps-like heading) ======
    void PushCourseSample(double lat, double lon, double t)
    {
        var fix = new GpsFix { lat = lat, lon = lon, t = t };
        courseBuffer.Enqueue(fix);
        // drop old
        while (courseBuffer.Count > 0 && (fix.t - courseBuffer.Peek().t) > courseWindowSeconds)
            courseBuffer.Dequeue();
    }

    void ComputeGpsCourse()
    {
        hasCourse = false;
        gpsCourseDeg = 0f;

        if (courseBuffer.Count < 2) return;

        // Use first and last in window
        GpsFix first = default, last = default;
        bool gotFirst = false;
        foreach (var s in courseBuffer)
        {
            if (!gotFirst) { first = s; gotFirst = true; }
            last = s;
        }

        float dist = HaversineDistance(first.lat, first.lon, last.lat, last.lon); // meters
        double dt = Math.Max(0.001, last.t - first.t);
        double speedMps = dist / dt;

        if (speedMps < minSpeedForCourseMps) return; // standing or too noisy

        gpsCourseDeg = CalculateBearing(first.lat, first.lon, last.lat, last.lon);
        hasCourse = true;
    }

    void UpdateCourseUIOnly()
    {
        if (unityCompassText == null) return;
        string s = hasCourse ? $"{gpsCourseDeg:0}°" : "—";
        float camYaw = (Camera.main != null) ? YawFromForward(Camera.main.transform.forward) : 0f;
        unityCompassText.text = $"Course(GPS): {s} | CamYaw:{camYaw:0}°";
    }

    static float YawFromForward(Vector3 fwd)
    {
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-6f) return 0f;
        return Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg; // 0°=+Z, 90°=+X
    }

    // ====== VISIBILITY ======
    void UpdateARObjectsVisibility()
    {
        if (spawnedARObjects == null) return;
        double userLat = currentLat, userLon = currentLon;

        for (int i = 0; i < spawnedARObjects.Count; i++)
        {
            var go = spawnedARObjects[i];
            if (!go || i >= spawnedARData.Count) continue;

            var data = spawnedARData[i];
            float dist = HaversineDistance(userLat, userLon, data.lat, data.lon);

            bool wasOn = _vis.TryGetValue(go, out var on) ? on : true;
            bool shouldShow = wasOn ? (dist < hideRadiusMeters) : (dist < showRadiusMeters);

            if (shouldShow != wasOn)
            {
                go.SetActive(shouldShow);
                _vis[go] = shouldShow;
            }
        }
    }
private GameObject SpawnDestinationPreviewInFrontOfCamera(
    GameObject prefab,
    string label
){
    if (prefab == null || Camera.main == null) return null;

    // Put it some meters in front of the camera, parent to camera so it sticks to view
    Vector3 worldGuess = Camera.main.transform.position +
                         Camera.main.transform.forward * Mathf.Max(8f, forcedDisplayDistance) +
                         Vector3.up * (forcedHeightAboveCamera + destinationPreviewHeightOffset);

    // reuse your low-level spawner but force parentToCamera = true
    return SpawnAnchoredOrCameraPrefab(
        prefab,
        worldGuess,
        label,
        destinationIcon,
        0f,
        destinationMaterial,
        parentToCamera: true
    );
}

    // ====== SPAWN (same as before, no compass) ======
    public IEnumerator SpawnAllARObjects_WhenLocationReady(float timeoutSeconds = 8f)
    {
        if (debugLogs) Debug.Log("[Spawn] Waiting for LocationService…");

        float t = 0f;
        while (Input.location.status != LocationServiceStatus.Running && t < timeoutSeconds)
        {
            t += Time.deltaTime;
            yield return null;
        }

        if (Input.location.status == LocationServiceStatus.Running && !originSet)
        {
            currentLat = Input.location.lastData.latitude;
            currentLon = Input.location.lastData.longitude;
            originLat = currentLat;
            originLon = currentLon;
            originAlt = Input.location.lastData.altitude;
            currentAlt = originAlt;
            originSet = true;

            SetUI(alignmentStatusText, $"GPS Origin set ✓  ({originLat:F6}, {originLon:F6})");
            if (debugLogs) Debug.Log($"[Spawn] Origin set at ({originLat:F8},{originLon:F8}) alt={originAlt:F1}");
        }

        if (sequentialSpawnByDistance)
            StartCoroutine(SpawnAllARObjects_SequentialByDistance());
        else
            SpawnAllARObjects();
    }

    // Convert Canvas to world, apply materials, etc. (unchanged helpers)
    void ConfigureWorldSpaceCanvas(GameObject spawnedObj, Camera worldCamera)
    {
        if (spawnedObj == null) return;
        if (worldCamera == null) worldCamera = Camera.main;
        var canvases = spawnedObj.GetComponentsInChildren<Canvas>(true);
        foreach (var c in canvases)
        {
            c.renderMode = RenderMode.WorldSpace;
            c.worldCamera = worldCamera;
            var rt = c.GetComponent<RectTransform>();
            if (rt != null && rt.localScale == Vector3.one) rt.localScale = Vector3.one * 0.01f;
            var scaler = c.GetComponent<UnityEngine.UI.CanvasScaler>();
            if (scaler != null)
            {
                scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ConstantPixelSize;
                scaler.scaleFactor = 1f;
            }
        }
    }

    void ApplySpawnMaterial(GameObject spawnedObj, Material mat)
    {
        if (mat == null || spawnedObj == null) return;
        var mr = spawnedObj.GetComponentInChildren<MeshRenderer>();
        if (mr != null) mr.material = new Material(mat);
    }

    void HideInWorldText(GameObject spawnedObj)
    {
        if (spawnedObj == null) return;
        var marker = spawnedObj.GetComponentInChildren<ARMarker>();
        if (marker != null) { marker.HideLabel(true); return; }
        var tmp = spawnedObj.GetComponentInChildren<TMP_Text>();
        if (tmp != null) tmp.gameObject.SetActive(false);
    }

    ARAnchor TryCreateAnchor(Pose pose)
    {
        if (anchorManager != null)
        {
            Type mgrType = anchorManager.GetType();
            try
            {
                MethodInfo addAnchorMethod = mgrType.GetMethod("AddAnchor", new Type[] { typeof(Pose) });
                if (addAnchorMethod != null)
                {
                    object result = addAnchorMethod.Invoke(anchorManager, new object[] { pose });
                    if (result is ARAnchor created && created != null) return created;
                }

                MethodInfo tryAddMethod = mgrType.GetMethod("TryAddAnchor", new Type[] { typeof(Pose), typeof(ARAnchor).MakeByRefType() });
                if (tryAddMethod != null)
                {
                    object[] args = new object[] { pose, null };
                    bool ok = (bool)tryAddMethod.Invoke(anchorManager, args);
                    if (ok && args[1] is ARAnchor created2 && created2 != null) return created2;
                }
            }
            catch { }
        }
        return null;
    }

    private class CameraParentedTag : MonoBehaviour { public bool isCameraParented = false; }

    private GameObject SpawnAnchoredOrCameraPrefab(
        GameObject prefab,
        Vector3 worldPosition,
        string label,
        Sprite icon,
        float heightOffset = 0f,
        Material mat = null,
        bool parentToCamera = false)
    {
        if (prefab == null) return null;
        Camera cam = Camera.main;

        // camera-parented
        if (parentToCamera && cam != null)
        {
            Vector3 local = cam.transform.InverseTransformPoint(worldPosition);
            if (local.z < forcedDisplayDistance) local.z = forcedDisplayDistance;
            local.y = forcedHeightAboveCamera + heightOffset;

            GameObject obj = Instantiate(prefab, cam.transform);
            obj.transform.localPosition = local;
          // Face the camera: 180° so the banner's front looks toward the camera
float baseYaw = 180f + bannerExtraYawDegrees + (bannerFlipForward ? 180f : 0f);
obj.transform.localRotation = Quaternion.Euler(0f, baseYaw, 0f);
            ConfigureWorldSpaceCanvas(obj, cam);
            ApplySpawnMaterial(obj, mat);
            ForceOpaqueMaterials(obj);
              FaceObjectToCamera(obj, cam, true); 

            var markerComp = obj.GetComponentInChildren<ARMarker>();
            if (markerComp != null) { markerComp.SetData(label, icon); markerComp.HideLabel(true); }

            var tag = obj.GetComponent<CameraParentedTag>() ?? obj.AddComponent<CameraParentedTag>();
            tag.isCameraParented = true;
            return obj;
        }

        // world/anchor
        Vector3 target = worldPosition + Vector3.up * heightOffset;
        ARAnchor finalAnchor = null;

        if (lockToPlaneIfAvailable && raycastManager != null)
        {
#if UNITY_2021_3_OR_NEWER
            List<ARRaycastHit> hits = new List<ARRaycastHit>(1);
            Vector3 rayOrigin = target + Vector3.up * 2.0f;
            Vector3 rayDir = Vector3.down;
            if (raycastManager.Raycast(new Ray(rayOrigin, rayDir), hits, TrackableType.Planes) && hits.Count > 0)
            {
                var hit = hits[0];
                Pose pose = hit.pose;
                if (anchorManager != null)
                {
                    var plane = planeManager != null ? planeManager.GetPlane(hit.trackableId) : null;
                    if (plane != null)
                    {
                        ARAnchor attached = null;
                        try { attached = anchorManager.AttachAnchor(plane, pose); }
                        catch
                        {
                            try
                            {
                                var m = typeof(ARAnchorManager).GetMethod("AttachAnchor",
                                    new System.Type[] { typeof(ARPlane), typeof(Pose) });
                                if (m != null) attached = m.Invoke(anchorManager, new object[] { plane, pose }) as ARAnchor;
                            } catch { }
                        }
                        if (attached != null) finalAnchor = attached;
                        else
                        {
                            var child = new GameObject("ARAnchor_AttachedToPlane");
                            child.transform.SetPositionAndRotation(pose.position, pose.rotation);
                            child.transform.SetParent(plane.transform, true);
                            finalAnchor = child.AddComponent<ARAnchor>();
                        }
                    }
                }
            }
#endif
        }

        if (finalAnchor == null)
        {
            Pose pose = new Pose(target, Quaternion.identity);
            finalAnchor = TryCreateAnchor(pose);
            if (finalAnchor == null)
            {
                GameObject anchorGO = new GameObject($"ARAnchor_{(string.IsNullOrEmpty(label) ? "obj" : label)}");
                anchorGO.transform.SetPositionAndRotation(pose.position, pose.rotation);
                if (anchorRoot != null) anchorGO.transform.SetParent(anchorRoot, true);
                try { finalAnchor = anchorGO.AddComponent<ARAnchor>(); } catch { finalAnchor = null; }

                if (finalAnchor == null)
                {
                    GameObject fallback = (anchorRoot != null)
                        ? Instantiate(prefab, pose.position, pose.rotation, anchorRoot)
                        : Instantiate(prefab, pose.position, pose.rotation);

                    ConfigureWorldSpaceCanvas(fallback, cam);
                    ApplySpawnMaterial(fallback, mat);
                    ForceOpaqueMaterials(fallback);

                    var mf = fallback.GetComponentInChildren<ARMarker>();
                    if (mf != null) { mf.SetData(label, icon); mf.HideLabel(true); }
                    return fallback;
                }
            }
        }

        GameObject objInst = Instantiate(prefab, finalAnchor.transform);
        objInst.transform.localPosition = Vector3.zero;
        objInst.transform.localRotation = Quaternion.identity;

        ConfigureWorldSpaceCanvas(objInst, cam);
        ApplySpawnMaterial(objInst, mat);
        ForceOpaqueMaterials(objInst);
FaceObjectToCamera(objInst, cam, true);  
        var marker = objInst.GetComponentInChildren<ARMarker>();
        if (marker != null) { marker.SetData(label, icon); marker.HideLabel(true); }

        return objInst;
    }

    // Public helper: spawn by precise lat/lon
    public GameObject SpawnARObjectAtLatLon(
        GameObject prefab,
        double lat,
        double lon,
        string label = null,
        float heightOffset = 0f,
        Sprite icon = null,
        Material mat = null)
    {
        if (prefab == null) { Debug.LogError("SpawnARObjectAtLatLon: prefab is null"); return null; }
        if (Input.location.status != LocationServiceStatus.Running)
        {
            Debug.LogWarning("SpawnARObjectAtLatLon: Location service not running");
            return null;
        }

        Vector3 localENU = LatLonToUnity_Precise(lat, lon);
        Vector3 worldPos = (originTransform != null) ? originTransform.TransformPoint(localENU) : localENU;

        float baseAlt = originSet ? originAlt : Input.location.lastData.altitude;
        float targetY = !float.IsNaN(baseAlt)
            ? baseAlt + arGlobalHeightOffset + heightOffset
            : (Camera.main ? Camera.main.transform.position.y : 0f) + forcedHeightAboveCamera + heightOffset;

        if (Camera.main != null && !float.IsNaN(baseAlt))
        {
            float camY = Camera.main.transform.position.y;
            if (Mathf.Abs(targetY - camY) > maxVerticalDeltaFromCamera)
                targetY = camY + Mathf.Sign(targetY - camY) * maxVerticalDeltaFromCamera;
        }

        worldPos.y = targetY;

        bool parentToCamera = forceScreenPlacement && Camera.main != null;
        if (!parentToCamera && forceScreenPlacement && Camera.main != null)
            worldPos = ProjectToCameraFrustum(Camera.main, worldPos, forcedDisplayDistance);

        GameObject obj = SpawnAnchoredOrCameraPrefab(prefab, worldPos, label, icon, heightOffset, mat, parentToCamera);
        if (obj == null) { Debug.LogError($"SpawnARObjectAtLatLon: failed to spawn '{label}'"); return null; }

        obj.name = $"AR_{label ?? $"{lat:F6}_{lon:F6}"}";

        spawnedARObjects.Add(obj);
        spawnedARData.Add(new ARSpawnPoint
        {
            name = label ?? "",
            lat = lat,
            lon = lon,
            prefab = prefab,
            material = mat,
            heightOffset = heightOffset,
            icon = icon
        });

        if (!(obj.GetComponent<CameraParentedTag>()?.isCameraParented ?? false) && Camera.main != null)
            FaceObjectToCamera(obj, Camera.main, true);

        float gpsDistance = HaversineDistance(currentLat, currentLon, lat, lon);
        Debug.Log($"[Spawn] Final position: {obj.transform.position}, GPS dist: {gpsDistance:F1}m");

        return obj;
    }

    public void SpawnAllARObjects()
{
    ClearSpawnedARObjects();

    if (arSpawnPoints == null || arSpawnPoints.Count == 0)
    {
        Debug.Log("SpawnAllARObjects: no AR spawn points configured.");
        return;
    }

    RefreshCurrentLocationFromService();
    Debug.Log($"SpawnAllARObjects: currentLat={currentLat:F8}, currentLon={currentLon:F8} originSet={originSet}");

    Camera cam = Camera.main;

    for (int i = 0; i < arSpawnPoints.Count; i++)
    {
        var entry = arSpawnPoints[i];
        if (entry == null || !entry.enabled) continue;

        // ONLY use the prefab referenced on the list item
        GameObject prefabToUse = entry.prefab;
        if (prefabToUse == null)
        {
            Debug.LogWarning($"AR spawn entry '{entry.name}' has no prefab. Skipping.");
            continue;
        }

        GameObject obj = SpawnARObjectAtLatLon(prefabToUse, entry.lat, entry.lon, entry.name, entry.heightOffset, entry.icon, entry.material);
        if (obj == null) { Debug.LogWarning($"SpawnAllARObjects: failed to spawn '{entry.name}'"); continue; }

        obj.name = $"AR_{(string.IsNullOrEmpty(entry.name) ? i.ToString() : entry.name)}";

        var markerComp = obj.GetComponentInChildren<ARMarker>();
        if (markerComp != null) markerComp.SetData(entry.name, entry.icon);

        if (cam != null && !forceScreenPlacement) FaceObjectToCamera(obj, cam);
    }

    Debug.Log($"SpawnAllARObjects: spawned {spawnedARObjects.Count} objects.");
}


  private IEnumerator SpawnAllARObjects_SequentialByDistance()
{
    ClearSpawnedARObjects();

    if (arSpawnPoints == null || arSpawnPoints.Count == 0)
    {
        Debug.Log("SpawnAllARObjects_SequentialByDistance: no AR spawn points configured.");
        yield break;
    }

    RefreshCurrentLocationFromService();
    Debug.Log($"Sequential spawn: user {currentLat:F8},{currentLon:F8}");

    List<ARSpawnPoint> list = new List<ARSpawnPoint>(arSpawnPoints);
    // Keep only entries that have a prefab
    list.RemoveAll(e => e == null || !e.enabled || e.prefab == null);

    // sort by distance
    list.Sort((a, b) =>
    {
        float da = HaversineDistance(currentLat, currentLon, a.lat, a.lon);
        float db = HaversineDistance(currentLat, currentLon, b.lat, b.lon);
        return da.CompareTo(db);
    });

    Camera cam = Camera.main;

    for (int i = 0; i < list.Count; i++)
    {
        var entry = list[i];
        if (entry == null) continue;

        // ONLY the entry's prefab
        GameObject obj = SpawnARObjectAtLatLon(entry.prefab, entry.lat, entry.lon, entry.name, entry.heightOffset, entry.icon, entry.material);

        if (obj != null)
        {
            obj.name = $"AR_{(string.IsNullOrEmpty(entry.name) ? i.ToString() : entry.name)}";

            var markerComp = obj.GetComponentInChildren<ARMarker>();
            if (markerComp != null) markerComp.SetData(entry.name, entry.icon);

            if (cam != null && !forceScreenPlacement) FaceObjectToCamera(obj, cam);
            if (debugLogs) Debug.Log($"Sequential spawn created '{entry.name}' at ({entry.lat:F8},{entry.lon:F8})");
        }

        if (spawnStaggerSeconds > 0f) yield return new WaitForSeconds(spawnStaggerSeconds);
        else yield return null;
    }

    Debug.Log($"SpawnAllARObjects_SequentialByDistance: spawned {spawnedARObjects.Count} objects.");
}


    public void ClearSpawnedARObjects()
    {
        for (int i = 0; i < spawnedARObjects.Count; i++)
            if (spawnedARObjects[i] != null) Destroy(spawnedARObjects[i]);

        spawnedARObjects.Clear();
        spawnedARData.Clear();
    }

    // ====== LABELS / HUD ======
    void UpdateSpawnedLabels()
    {
        if (spawnedARObjects == null || spawnedARObjects.Count == 0)
        {
            if (arObjectsDistanceText != null) arObjectsDistanceText.text = "";
            return;
        }

        RefreshCurrentLocationFromService();
        double userLat = currentLat;
        double userLon = currentLon;

        System.Text.StringBuilder sb = new System.Text.StringBuilder();

        Camera cam = Camera.main;
        int closestCameraIndex = -1;
        float bestCamDist = float.MaxValue;

        for (int i = 0; i < spawnedARObjects.Count; i++)
        {
            var go = spawnedARObjects[i];
            if (go == null) continue;
            var tag = go.GetComponent<CameraParentedTag>();
            if (tag != null && tag.isCameraParented && cam != null)
            {
                float d = (go.transform.parent == cam.transform) ? go.transform.localPosition.z
                                                                 : Vector3.Distance(cam.transform.position, go.transform.position);
                if (d < bestCamDist) { bestCamDist = d; closestCameraIndex = i; }
            }
        }

        for (int i = 0; i < spawnedARObjects.Count; i++)
        {
            var go = spawnedARObjects[i];
            if (go == null || i >= spawnedARData.Count) continue;

            var data = spawnedARData[i];
            float dist = HaversineDistance(userLat, userLon, data.lat, data.lon);
            int distMeters = Mathf.RoundToInt(dist);

            float bearing = CalculateBearing(userLat, userLon, data.lat, data.lon);

            // Use GPS course if available, otherwise camera yaw as a rough proxy
            float heading = hasCourse ? gpsCourseDeg :
                            (Camera.main != null ? YawFromForward(Camera.main.transform.forward) : 0f);

            float rel = NormalizeAngle(bearing - heading);
            string dir;
            float absRel = Mathf.Abs(rel);
            if (absRel <= 25f) dir = "ahead";
            else if (absRel <= 110f) dir = (rel > 0f) ? "on right" : "on left";
            else dir = "behind";

            var marker = go.GetComponentInChildren<ARMarker>();
            var tag = go.GetComponent<CameraParentedTag>();

            bool shouldShow;
            if (tag != null && tag.isCameraParented)
                shouldShow = (i == closestCameraIndex);
            else
                shouldShow = forceScreenPlacement ? true : (dist <= 100f && distMeters > 0);

            string labelText = $"{data.name} — {distMeters} m {dir}";
            if (marker != null)
            {
                marker.HideLabel(!shouldShow);
                if (shouldShow) { marker.SetDistanceText(labelText); sb.AppendLine(labelText); }
            }
            else
            {
                if (shouldShow) sb.AppendLine(labelText);
            }
        }

        if (arObjectsDistanceText != null)
            arObjectsDistanceText.text = sb.ToString();
    }

    // ====== PATH / UI ======
// void UpdateUIArrow()
// {
//     int segment = FindClosestSegmentIndexOnPath();
//     Vector2 lookLatLon = GetLookaheadPointMeters(segment, lookaheadMeters);
//     float bearingToLook = CalculateBearing(currentLat, currentLon, lookLatLon.x, lookLatLon.y);

//     float heading = hasCourse ? gpsCourseDeg :
//                     (Camera.main != null ? YawFromForward(Camera.main.transform.forward) : 0f);

//     float desiredZ = -NormalizeAngle(bearingToLook - heading);

//     // keep this override
//     if (goingWrongWay)
//     {
//         desiredZ = 180f;              // point back
//         guidanceLabel = "u-turn";     // helps 3D arrow too
//     }

//     uiCurrentZ = Mathf.LerpAngle(uiCurrentZ, desiredZ, Time.deltaTime * arrowSmoothing);
//     if (uiArrow != null) uiArrow.rectTransform.localEulerAngles = new Vector3(0, 0, uiCurrentZ);
// }


void UpdateUIArrow_StepBased(float headingDeg)
{
    int segment = FindClosestSegmentIndexOnPath();
    Vector2 lookLatLon = GetLookaheadPointMeters(segment, lookaheadMeters);
    float bearingToLook = CalculateBearing(currentLat, currentLon, lookLatLon.x, lookLatLon.y);

    // desiredZ so that arrow points up when aligned with bearingToLook
    float desiredZ = -NormalizeAngle(bearingToLook - headingDeg);

    uiCurrentZ = Mathf.LerpAngle(uiCurrentZ, desiredZ, Time.deltaTime * Mathf.Max(1f, arrowSmoothing));
    if (uiArrow != null) uiArrow.rectTransform.localEulerAngles = new Vector3(0, 0, uiCurrentZ);
}

void UpdateBigInstruction_StepBased()
{
    if (bigInstructionText == null) return;

    float currentAlong = smoothedAlong;
    float routeEnd = RouteEndAlong();
    float tol = 0.5f;

    int nextTurnStep = FindNextTurnAfterAlong_StepBased(currentAlong + tol, tol);

    if (nextTurnStep == -1)
    {
        float distToDest = Mathf.Max(0f, routeEnd - currentAlong);
        bigInstructionText.text = $"Go straight — {Mathf.RoundToInt(distToDest)} m";
        return;
    }

    float turnAlong = (nextTurnStep < stepStartAlong.Count) ? stepStartAlong[nextTurnStep] : routeEnd;
    float distToTurn = Mathf.Max(0f, turnAlong - currentAlong);
    string label = GetManeuverLabelForStep(nextTurnStep);

    if (distToTurn > turnAnnouncementThreshold)
        bigInstructionText.text = $"Go straight — {Mathf.RoundToInt(distToTurn)} m";
    else
        bigInstructionText.text = $"{label} in {Mathf.RoundToInt(distToTurn)} m";
}

void UpdateStepsPreview_StepBased()
{
    if (stepsPreviewText == null) return;

    float currentAlong = smoothedAlong;
    var segments = BuildRicherPreviewSegments_StepBased(currentAlong);
    var parts = new List<string>();

    foreach (var kv in segments)
    {
        int meters = Mathf.RoundToInt(kv.Value);
        parts.Add($"{kv.Key} ({meters}m)");
    }

    if (segments.Count > 1 && segments[1].Value > turnAnnouncementThreshold)
    {
        parts.Clear();
        parts.Add($"Go straight ({Mathf.RoundToInt(segments[0].Value)}m)");
    }

    if (parts.Count == 0)
    {
        stepsPreviewText.text = "";
        stepsPreviewText.gameObject.SetActive(false);
        return;
    }

    stepsPreviewText.text = string.Join(" → ", parts);
    stepsPreviewText.gameObject.SetActive(true);
}

void CheckStepProgress_StepBased()
{
    if (currentStepIndex >= steps.Count) return;

    float current = smoothedAlong;
    AlignCurrentStepToAlong();

    float stepEnd = (currentStepIndex < stepEndAlong.Count)
        ? stepEndAlong[Mathf.Clamp(currentStepIndex, 0, stepEndAlong.Count - 1)]
        : current;

    float distToStep = Mathf.Max(0f, stepEnd - current);

    if (current >= stepEnd - 1.0f || distToStep <= stepAdvanceMeters)
    {
        currentStepIndex++;
        if (currentStepIndex < steps.Count)
        {
            UpdateBigInstruction_StepBased();
            UpdateStepsPreview_StepBased();
        }
        else
        {
            if (bigInstructionText != null) bigInstructionText.text = "Proceed to destination";
            UpdateStepsPreview_StepBased();
        }
        return;
    }

    UpdateBigInstruction_StepBased();
}


int FindNextTurnAfterAlong_StepBased(float afterAlong, float tol = 0.5f)
{
    if (stepStartAlong == null || stepStartAlong.Count == 0) return -1;
    for (int s = 0; s < steps.Count; s++)
    {
        if (s >= stepStartAlong.Count) continue;
        float startA = stepStartAlong[s];
        if (startA <= afterAlong + tol) continue;
        string label = GetManeuverLabelForStep(s);
        if (IsTurnLabel(label)) return s;
    }
    return -1;
}

void UpdateThresholdTurnUI_StepBased()
{
    if (thresholdTurnText == null) return;

    float current = smoothedAlong;
    int nextTurn = FindNextTurnAfterAlong_StepBased(current + 0.5f, 0.5f);
    if (nextTurn == -1)
    {
        thresholdTurnText.text = "";
        return;
    }

    float routeEnd = RouteEndAlong();
    float turnAlong = (nextTurn < stepStartAlong.Count) ? stepStartAlong[nextTurn] : routeEnd;
    float distToTurn = Mathf.Max(0f, turnAlong - current);
    if (distToTurn > turnAnnouncementThreshold)
    {
        thresholdTurnText.text = "";
    }
    else
    {
        string label = GetManeuverLabelForStep(nextTurn);
        thresholdTurnText.text = $"{label} in {Mathf.RoundToInt(distToTurn)} m";
    }
}


private void UpdateGuidanceArrow_StepBased()
{
    if (guidanceArrowInstance == null || !guidanceArrowInstance.activeSelf) return;

    Camera cam = Camera.main;
    if (cam == null) return;

    // keep position stable if parented
    if (guidanceParentToCamera && guidanceArrowInstance.transform.parent == cam.transform)
    {
        guidanceArrowInstance.transform.localPosition =
            Vector3.forward * guidanceArrowDistance + Vector3.up * guidanceArrowHeightOffset;

        if (string.IsNullOrEmpty(guidanceLabel) && bigInstructionText != null)
            guidanceLabel = ExtractGuidanceLabelFromText(bigInstructionText.text);

        string lab = (guidanceLabel ?? "").ToLowerInvariant();
        float desiredYaw = 0f; // forward

        if (lab.Contains("left"))
            desiredYaw = lab.Contains("slight") ? -35f : -90f;
        else if (lab.Contains("right"))
            desiredYaw = lab.Contains("slight") ? 35f : 90f;
        else
            desiredYaw = 0f;   // straight (no U-turn mapping)

        guidanceCurrentYaw = Mathf.SmoothDampAngle(
            guidanceCurrentYaw,
            desiredYaw,
            ref guidanceYawVelocity,
            Mathf.Max(0.001f, guidanceSmoothingTime)
        );

        guidanceArrowInstance.transform.localEulerAngles = new Vector3(0f, guidanceCurrentYaw, 0f);
        return;
    }

    // world-space fallback (kept from your file)
    Vector3 targetPos = cam.transform.position + cam.transform.forward * guidanceArrowDistance;
    targetPos.y += guidanceArrowHeightOffset;
    guidanceArrowInstance.transform.position =
        Vector3.Lerp(guidanceArrowInstance.transform.position, targetPos, Time.deltaTime * 8f);

    Quaternion baseRot = Quaternion.LookRotation(cam.transform.forward, Vector3.up);
    if (string.IsNullOrEmpty(guidanceLabel) && bigInstructionText != null)
        guidanceLabel = ExtractGuidanceLabelFromText(bigInstructionText.text);

    string label = (guidanceLabel ?? "").ToLowerInvariant();
    Quaternion desiredRot = baseRot;

    if (label.Contains("left"))
        desiredRot = baseRot * Quaternion.Euler(0f, label.Contains("slight") ? -35f : -90f, 0f);
    else if (label.Contains("right"))
        desiredRot = baseRot * Quaternion.Euler(0f, label.Contains("slight") ? 35f : 90f, 0f);
    else
        desiredRot = baseRot;

    guidanceArrowInstance.transform.rotation =
        Quaternion.Slerp(guidanceArrowInstance.transform.rotation, desiredRot, Time.deltaTime * guidanceArrowRotateSpeed);
}

// private class TripletEvent { public int stepIndex; public string label; public float along; }

List<KeyValuePair<string, float>> BuildRicherPreviewSegments_StepBased(float currentAlong)
{
    var result = new List<KeyValuePair<string, float>>();
    float cursor = currentAlong;
    float routeEnd = RouteEndAlong();
    int limit = Mathf.Max(1, previewSegmentLimit);
    var events = new List<TripletEvent>();
    float eps = 0.01f;

    for (int s = 0; s < steps.Count; s++)
    {
        if (s >= stepStartAlong.Count) continue;
        float startA = stepStartAlong[s];
        if (startA < currentAlong + eps) continue;
        string label = GetManeuverLabelForStep(s);
        if (IsTurnLabel(label))
            events.Add(new TripletEvent { stepIndex = s, label = label, along = startA });
    }

    events.Sort((a, b) => a.along.CompareTo(b.along));

    int ei = 0;
    while (result.Count < limit)
    {
        bool hasEvent = ei < events.Count;
        float nextEventAlong = hasEvent ? events[ei].along : routeEnd;
        string nextEventLabel = hasEvent ? events[ei].label : null;

        float preTurnEnd = Mathf.Max(cursor, nextEventAlong - turnAnnouncementThreshold);
        float preLen = Mathf.Max(0f, preTurnEnd - cursor);
        if (preLen > 0f)
        {
            result.Add(new KeyValuePair<string, float>("Go straight", preLen));
            cursor += preLen;
            if (result.Count >= limit) break;
        }

        if (hasEvent && nextEventAlong - cursor <= turnAnnouncementThreshold)
        {
            float toEvent = Mathf.Max(0f, nextEventAlong - cursor);
            float turnLen = Mathf.Min(turnAnnouncementThreshold, toEvent);
            if (turnLen > 0f)
            {
                result.Add(new KeyValuePair<string, float>(nextEventLabel, turnLen));
                cursor += turnLen;
                if (result.Count >= limit) break;
            }

            if (Mathf.Abs(cursor - nextEventAlong) <= 0.01f || toEvent <= 0.01f)
            {
                cursor = nextEventAlong;
                ei++;
                continue;
            }
        }
        else
        {
            float tail = Mathf.Max(0f, routeEnd - cursor);
            if (tail > 0f)
            {
                result.Add(new KeyValuePair<string, float>("Go straight", tail));
                cursor += tail;
            }
            break;
        }
    }

    return result;
}






// ====== AR GUIDANCE ARROW (patched for wrong-way) ======
// private void UpdateGuidanceArrow()
// {
//     if (guidanceArrowInstance == null || !guidanceArrowInstance.activeSelf) return;

//     Camera cam = Camera.main;
//     if (cam == null) return;

//     // --- Desired yaw purely from guidanceLabel / wrong-way ---
//     float desiredYaw = 0f; // 0 = forward
//     string lab = (guidanceLabel ?? "").ToLowerInvariant();

//     if (goingWrongWay)
//     {
//         desiredYaw = 180f; // force U-turn cue
//     }
//     else
//     {
//         if (lab.Contains("u-turn") || lab.Contains("uturn") || lab.Contains("u turn"))
//             desiredYaw = 180f;
//         else if (lab.Contains("slight left"))
//             desiredYaw = -35f;
//         else if (lab.Contains("slight right"))
//             desiredYaw = 35f;
//         else if (lab.Contains("turn left"))
//             desiredYaw = -90f;
//         else if (lab.Contains("turn right"))
//             desiredYaw = 90f;
//         else
//             desiredYaw = 0f; // "go straight" or no label
//     }

//     // --- Anticipatory smoothing (approaching next mapped turn only) ---
//     float distToNextTurn = 999f;
//     if (stepStartAlong != null && stepStartAlong.Count > currentStepIndex)
//     {
//         float currentA = smoothedAlong;
//         float nextA = stepStartAlong[currentStepIndex];
//         distToNextTurn = Mathf.Max(0f, nextA - currentA);
//     }

//     if (!goingWrongWay && distToNextTurn < 50f)
//     {
//         // ease into the turn as you approach (0..1)
//         float anticipation = Mathf.InverseLerp(50f, 0f, distToNextTurn);
//         desiredYaw = Mathf.LerpAngle(0f, desiredYaw, anticipation);
//     }

//     // --- Smoothly apply ---
//     guidanceCurrentYaw = Mathf.SmoothDampAngle(
//         guidanceCurrentYaw,
//         desiredYaw,
//         ref guidanceYawVelocity,
//         Mathf.Max(0.05f, guidanceSmoothingTime)
//     );

//     if (guidanceParentToCamera && guidanceArrowInstance.transform.parent == cam.transform)
//     {
//         guidanceArrowInstance.transform.localPosition =
//             Vector3.forward * guidanceArrowDistance + Vector3.up * guidanceArrowHeightOffset;
//         guidanceArrowInstance.transform.localEulerAngles =
//             new Vector3(0f, guidanceCurrentYaw, 0f);
//     }
//     else
//     {
//         Vector3 targetPos = cam.transform.position + cam.transform.forward * guidanceArrowDistance;
//         targetPos.y += guidanceArrowHeightOffset;
//         guidanceArrowInstance.transform.position =
//             Vector3.Lerp(guidanceArrowInstance.transform.position, targetPos, Time.deltaTime * 8f);

//         Quaternion baseRot = Quaternion.LookRotation(cam.transform.forward, Vector3.up);
//         Quaternion desiredRot = baseRot * Quaternion.Euler(0f, guidanceCurrentYaw, 0f);
//         guidanceArrowInstance.transform.rotation =
//             Quaternion.Slerp(guidanceArrowInstance.transform.rotation, desiredRot, Time.deltaTime * guidanceArrowRotateSpeed);
//     }
// }




// ====== BIG INSTRUCTION (patched for wrong-way + geodesic fallback) ======
// void UpdateBigInstruction()
// {
//     if (bigInstructionText == null) return;

//     // Wrong-way override
//     if (goingWrongWay)
//     {
//         bigInstructionText.text = "Going the wrong way — Make a U-turn";
//         return;
//     }

//     float currentAlong = smoothedAlong;
//     float routeEnd = RouteEndAlong();
//     float tol = 0.5f;

//     int nextTurnStep = FindNextTurnAfterAlong(currentAlong + tol, tol);

//     if (nextTurnStep == -1)
//     {
//         // No more mapped turns → straight to destination
//         float distToDest = Mathf.Max(0f, routeEnd - currentAlong);
//         bigInstructionText.text = $"Go straight — {Mathf.RoundToInt(distToDest)} m";
//         return;
//     }

//     float turnAlong = (nextTurnStep < stepStartAlong.Count) ? stepStartAlong[nextTurnStep] : routeEnd;
//     float distToTurn = Mathf.Max(0f, turnAlong - currentAlong);
//     string turnLabel = GetManeuverLabelForStep(nextTurnStep); // uses Google maneuver/html

//     float nearNow = 12f;
//     float announce = Mathf.Min(50f, turnAnnouncementThreshold);

//     if (distToTurn > turnAnnouncementThreshold + 0.001f)
//     {
//         bigInstructionText.text = $"Go straight — {Mathf.RoundToInt(distToTurn)} m";
//         return;
//     }

//     if (distToTurn <= nearNow)
//         bigInstructionText.text = $"{turnLabel} now";
//     else if (distToTurn <= announce)
//         bigInstructionText.text = $"{turnLabel} in {Mathf.RoundToInt(distToTurn)} m";
//     else
//         bigInstructionText.text = $"{turnLabel} in {Mathf.RoundToInt(distToTurn)} m";
// }



// void UpdateStepsPreview()
// {
//     if (stepsPreviewText == null) return;

//     float currentAlong = smoothedAlong;
//     var segments = BuildRicherPreviewSegments(currentAlong);
//     if (segments == null || segments.Count == 0)
//     {
//         stepsPreviewText.text = "";
//         stepsPreviewText.gameObject.SetActive(false);
//         return;
//     }

//     // Keep it concise: Now … → Then …
//     var parts = new List<string>();
//     parts.Add($"{segments[0].Key} ({Mathf.RoundToInt(segments[0].Value)}m)");
//     if (segments.Count > 1)
//         parts.Add($"{segments[1].Key} ({Mathf.RoundToInt(segments[1].Value)}m)");

//     stepsPreviewText.text = string.Join(" → ", parts);
//     stepsPreviewText.gameObject.SetActive(true);
// }


    // void CheckStepProgress()
    // {
    //     if (currentStepIndex >= steps.Count) return;
    //     float current = smoothedAlong;
    //     AlignCurrentStepToAlong();

    //     float stepEnd = (currentStepIndex < stepEndAlong.Count) ? stepEndAlong[Mathf.Clamp(currentStepIndex, 0, stepEndAlong.Count - 1)] : current;
    //     float distToStep = Mathf.Max(0f, stepEnd - current);

    //     if (current >= stepEnd - 1.0f || distToStep <= stepAdvanceMeters)
    //     {
    //         currentStepIndex++;
    //         if (currentStepIndex < steps.Count)
    //         {
    //             UpdateBigInstruction();
    //             UpdateStepsPreview();
    //         }
    //         else
    //         {
    //             if (bigInstructionText != null) bigInstructionText.text = "Proceed to destination";
    //             UpdateStepsPreview();
    //         }
    //         return;
    //     }
    //     UpdateBigInstruction();
    // }

void CheckArrival()
{
    float dToDest = HaversineDistance(currentLat, currentLon, destination.x, destination.y);
    string destName = string.IsNullOrEmpty(destDisplayName) ? "Destination" : destDisplayName;

    string distStr = dToDest >= 1000f ? $"{(dToDest / 1000f):F1} km" : $"{Mathf.RoundToInt(dToDest)} m";
    if (debugText != null)
    {
        if (dToDest <= arrivalDistanceMeters) debugText.text = $" {destName} — Arrived!";
        else debugText.text = $" {destName} — {distStr} away";
    }

    // 1) Preview marker
    if (dToDest <= destinationPreviewMeters)
    {
        if (destinationPreviewGO == null)
        {
            var prefab = destinationPreviewPrefab != null ? destinationPreviewPrefab : destinationPrefab;
            destinationPreviewGO = SpawnDestinationPreviewInFrontOfCamera(prefab, destName);
            if (destinationPreviewGO != null)
                Debug.Log($"[DestinationPreview] Shown at ~{Mathf.RoundToInt(dToDest)} m.");
        }
    }
    else if (destinationPreviewGO != null && dToDest > destinationPreviewHideMeters)
    {
        Destroy(destinationPreviewGO);
        destinationPreviewGO = null;
        Debug.Log("[DestinationPreview] Hidden (out of range).");
    }

    // 2) World destination marker
    if (!destinationWorldSpawned && destinationPrefab != null && dToDest <= destinationPreviewMeters)
    {
        GameObject dst = SpawnARObjectAtLatLon(
            destinationPrefab, destination.x, destination.y,
            destName, destinationHeightOffset, destinationIcon, destinationMaterial
        );
        destinationWorldSpawned = true;
    }

    // 3) Final arrival UI + trigger feedback voice bot ONCE
    if (dToDest <= arrivalDistanceMeters)
    {
        if (bigInstructionText != null) bigInstructionText.text = "You have reached your destination 🎉";
        if (stepsPreviewText != null) stepsPreviewText.text = "";
        if (guidanceArrowInstance != null) guidanceArrowInstance.SetActive(false);

        if (autoStartFeedbackOnArrival && !feedbackStartedOnce)
    {
        feedbackStartedOnce = true;
        string friendly = string.IsNullOrEmpty(destDisplayName) ? "your destination" : destDisplayName;

        if (feedbackBot == null) feedbackBot = FindObjectOfType<FeedbackVoiceBot>();
        if (feedbackBot != null)
        {
            Debug.Log("[ARNavigation] Starting feedback voice bot (arrival).");
            feedbackBot.StartFeedbackFlow_AutoArrival(friendly);
        }
    }
    }
}





    // ====== PATH GEOMETRY ======
    void BuildPathFromStepPolylinesAndResample()
    {
        path.Clear();
        cumulative.Clear();
        stepStartIndex.Clear();
        stepEndIndex.Clear();
        stepStartAlong.Clear();
        stepEndAlong.Clear();

        float accum = 0f;
        Vector2 lastAdded = Vector2.zero;
        bool firstAdded = false;

        for (int s = 0; s < steps.Count; s++)
        {
            var st = steps[s];
            List<Vector2> stepPoints = null;

            if (st.polyline != null && !string.IsNullOrEmpty(st.polyline.points))
                stepPoints = DecodePolyline(st.polyline.points);

            if (stepPoints == null || stepPoints.Count < 2)
            {
                Vector2 start = st.start_location != null ? new Vector2(st.start_location.lat, st.start_location.lng) : Vector2.zero;
                Vector2 end = st.end_location != null ? new Vector2(st.end_location.lat, st.end_location.lng) : Vector2.zero;
                stepPoints = new List<Vector2>() { start, end };
            }

            int startIndexForStep = path.Count;

            for (int i = 0; i < stepPoints.Count - 1; i++)
            {
                Vector2 a = stepPoints[i];
                Vector2 b = stepPoints[i + 1];
                float segDist = HaversineDistance(a.x, a.y, b.x, b.y);
                int subdivisions = Mathf.Max(1, Mathf.CeilToInt(segDist / resampleIntervalMeters));

                for (int j = 0; j <= subdivisions; j++)
                {
                    float t = (float)j / (float)subdivisions;
                    float lat = Mathf.Lerp(a.x, b.x, t);
                    float lon = Mathf.Lerp(a.y, b.y, t);
                    Vector2 p = new Vector2(lat, lon);

                    if (firstAdded && Mathf.Approximately(p.x, lastAdded.x) && Mathf.Approximately(p.y, lastAdded.y)) continue;

                    if (!firstAdded)
                    {
                        path.Add(p);
                        cumulative.Add(0f);
                        lastAdded = p;
                        firstAdded = true;
                    }
                    else
                    {
                        float delta = HaversineDistance(lastAdded.x, lastAdded.y, p.x, p.y);
                        accum += delta;
                        path.Add(p);
                        cumulative.Add(accum);
                        lastAdded = p;
                    }
                }
            }

            int endIndexForStep = path.Count - 1;
            stepStartIndex.Add(startIndexForStep);
            stepEndIndex.Add(endIndexForStep);
            stepStartAlong.Add((startIndexForStep < cumulative.Count) ? cumulative[startIndexForStep] : 0f);
            stepEndAlong.Add((endIndexForStep < cumulative.Count) ? cumulative[endIndexForStep] : (stepStartAlong.Count > 0 ? stepStartAlong[stepStartAlong.Count - 1] : 0f));
        }

        if (cumulative.Count == 0 && path.Count == 1) cumulative.Add(0f);
    }

void SmoothAlong(float value)
{
    alongWindow.Enqueue(value);
    if (alongWindow.Count > alongWindowSize) alongWindow.Dequeue();
    float sum = 0f;
    foreach (var v in alongWindow) sum += v;
    smoothedAlong = sum / Mathf.Max(1, alongWindow.Count);
}


    void DeduplicateCloseSteps(float thresholdMeters)
    {
        if (steps.Count <= 1 || thresholdMeters <= 0f) return;
        List<Step> newSteps = new List<Step>();
        List<int> newStart = new List<int>();
        List<int> newEnd = new List<int>();
        List<float> newStartAlong = new List<float>();
        List<float> newEndAlong = new List<float>();

        newSteps.Add(steps[0]);
        newStart.Add(stepStartIndex[0]);
        newEnd.Add(stepEndIndex[0]);
        newStartAlong.Add(stepStartAlong[0]);
        newEndAlong.Add(stepEndAlong[0]);

        for (int i = 1; i < steps.Count; i++)
        {
            float distBetweenEnds = Mathf.Abs(stepEndAlong[i] - newEndAlong[newEndAlong.Count - 1]);

            if (distBetweenEnds <= thresholdMeters)
            {
                if (string.IsNullOrEmpty(newSteps[newSteps.Count - 1].maneuver) && !string.IsNullOrEmpty(steps[i].maneuver))
                    newSteps[newSteps.Count - 1] = steps[i];

                newEnd[newEnd.Count - 1] = stepEndIndex[i];
                newEndAlong[newEndAlong.Count - 1] = stepEndAlong[i];
            }
            else
            {
                newSteps.Add(steps[i]);
                newStart.Add(stepStartIndex[i]);
                newEnd.Add(stepEndIndex[i]);
                newStartAlong.Add(stepStartAlong[i]);
                newEndAlong.Add(stepEndAlong[i]);
            }
        }

        steps = newSteps;
        stepStartIndex = newStart;
        stepEndIndex = newEnd;
        stepStartAlong = newStartAlong;
        stepEndAlong = newEndAlong;
    }

    void AlignCurrentStepToAlong()
    {
        if (stepEndAlong == null || stepEndAlong.Count == 0) { currentStepIndex = 0; return; }
        float tol = 0.5f;
        for (int i = 0; i < stepEndAlong.Count; i++)
        {
            if (smoothedAlong < stepEndAlong[i] - tol)
            {
                currentStepIndex = i;
                return;
            }
        }
        currentStepIndex = stepEndAlong.Count;
    }

    int FindClosestSegmentIndexOnPath()
    {
        if (path.Count < 2) return 0;
        float latRef = (float)currentLat;
        float meanLatRad = latRef * Mathf.Deg2Rad;
        float metersPerDegLat = 110574f;
        float metersPerDegLonRef = 111320f * Mathf.Cos(meanLatRad);
        float best = float.MaxValue;
        int bestI = 0;

        for (int i = 0; i < path.Count - 1; i++)
        {
            Vector2 a = path[i];
            Vector2 b = path[i + 1];
            Vector2 aM = new Vector2((a.y - (float)currentLon) * metersPerDegLonRef,
                                     (a.x - (float)currentLat) * metersPerDegLat);
            Vector2 bM = new Vector2((b.y - (float)currentLon) * metersPerDegLonRef,
                                     (b.x - (float)currentLat) * metersPerDegLat);
            Vector2 ab = bM - aM;
            Vector2 ao = -aM;
            float ab2 = ab.sqrMagnitude;
            float t = ab2 == 0 ? 0f : Mathf.Clamp01(Vector2.Dot(ao, ab) / ab2);
            Vector2 proj = aM + t * ab;
            float dist = proj.magnitude;
            if (dist < best) { best = dist; bestI = i; }
        }
        return bestI;
    }

    Vector2 GetLookaheadPointMeters(int closestSegment, float lookMeters)
    {
        float currentAlong = GetAlongDistanceOfClosestPoint();
        float targetAlong = currentAlong + lookMeters;

        if (targetAlong <= 0f) return path[0];
        if (targetAlong >= cumulative[cumulative.Count - 1]) return path[path.Count - 1];

        int idx = cumulative.BinarySearch(targetAlong);
        if (idx < 0) idx = ~idx;
        idx = Mathf.Clamp(idx, 1, path.Count - 1);

        float aAlong = cumulative[idx - 1];
        float bAlong = cumulative[idx];
        float t = (bAlong - aAlong) == 0f ? 0f : (targetAlong - aAlong) / (bAlong - aAlong);
        Vector2 a = path[idx - 1], b = path[idx];
        float lat = Mathf.Lerp(a.x, b.x, t);
        float lon = Mathf.Lerp(a.y, b.y, t);
        return new Vector2(lat, lon);
    }

    float GetAlongDistanceOfClosestPoint()
    {
        if (path.Count < 2) return 0f;
        float latRef2 = (float)currentLat;
        float meanLatRad2 = latRef2 * Mathf.Deg2Rad;
        float metersPerDegLat = 110574f;
        float metersPerDegLonRef2 = 111320f * Mathf.Cos(meanLatRad2);
        float bestDist = float.MaxValue;
        float bestAlong = 0f;

        for (int i = 0; i < path.Count - 1; i++)
        {
            Vector2 a = path[i];
            Vector2 b = path[i + 1];
            Vector2 aM2 = new Vector2((a.y - (float)currentLon) * metersPerDegLonRef2,
                                      (a.x - (float)currentLat) * metersPerDegLat);
            Vector2 bM2 = new Vector2((b.y - (float)currentLon) * metersPerDegLonRef2,
                                      (b.x - (float)currentLat) * metersPerDegLat);
            Vector2 ab = bM2 - aM2;
            Vector2 ao = -aM2;
            float ab2 = ab.sqrMagnitude;
            float t = ab2 == 0 ? 0f : Mathf.Clamp01(Vector2.Dot(ao, ab) / ab2);
            Vector2 proj = aM2 + t * ab;
            float dist = proj.magnitude;
            if (dist < bestDist)
            {
                bestDist = dist;
                float alongAtA = (i < cumulative.Count) ? cumulative[i] : 0f;
                float segLen = ab.magnitude;
                float offset = segLen * t;
                bestAlong = alongAtA + offset;
            }
        }
        return bestAlong;
    }

    // ====== GEO / UTILS ======
    Vector3 LatLonToUnity(Vector2 latlon)
    {
        double latRef = originSet ? originLat : currentLat;
        double lonRef = originSet ? originLon : currentLon;
        double meanLatRad = latRef * Mathf.Deg2Rad;
        double metersPerDegLat = 110574.0;
        double metersPerDegLon = 111320.0 * Math.Cos(meanLatRad);

        double east = (latlon.y - lonRef) * metersPerDegLon;
        double north = (latlon.x - latRef) * metersPerDegLat;

        return new Vector3((float)east, 0f, (float)north);
    }

    private Vector3 LatLonToUnity_Precise(double lat, double lon)
    {
        double latRef = originSet ? originLat : currentLat;
        double lonRef = originSet ? originLon : currentLon;
        double meanLatRad = latRef * System.Math.PI / 180.0;
        double metersPerDegLat = 110574.0;
        double metersPerDegLon = 111320.0 * System.Math.Cos(meanLatRad);

        double east = (lon - lonRef) * metersPerDegLon;
        double north = (lat - latRef) * metersPerDegLat;

        return new Vector3((float)east, 0f, (float)north);
    }

    float HaversineDistance(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371000.0;
        double dLat = (lat2 - lat1) * Mathf.Deg2Rad;
        double dLon = (lon2 - lon1) * Mathf.Deg2Rad;
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                   Math.Cos(lat1 * Mathf.Deg2Rad) * Math.Cos(lat2 * Mathf.Deg2Rad) *
                   Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return (float)(R * c);
    }

    float CalculateBearing(double lat1, double lon1, double lat2, double lon2)
    {
        double dLon = (lon2 - lon1) * Mathf.Deg2Rad;
        double lat1Rad = lat1 * Mathf.Deg2Rad;
        double lat2Rad = lat2 * Mathf.Deg2Rad;
        double y = Math.Sin(dLon) * Math.Cos(lat2Rad);
        double x = Math.Cos(lat1Rad) * Math.Sin(lat2Rad) -
                   Math.Sin(lat1Rad) * Math.Cos(lat2Rad) * Math.Cos(dLon);
        return (float)((Math.Atan2(y, x) * Mathf.Rad2Deg + 360.0) % 360.0);
    }

    float NormalizeAngle(float angle)
    {
        angle %= 360f;
        if (angle > 180f) angle -= 360f;
        if (angle < -180f) angle += 360f;
        return angle;
    }

    void SetUI(TMP_Text t, string msg) { if (t != null) t.text = msg; }

    void RefreshCurrentLocationFromService()
    {
        if (Input.location.status == LocationServiceStatus.Running)
        {
            currentLat = Input.location.lastData.latitude;
            currentLon = Input.location.lastData.longitude;
            currentAlt = Input.location.lastData.altitude;
        }
        else if (debugLogs) Debug.LogWarning("Location service not running.");
    }

  void FaceObjectToCamera(GameObject obj, Camera cam, bool applyFlipIfConfigured = true)
{
    if (obj == null || cam == null) return;

    var tag = obj.GetComponent<CameraParentedTag>();
    bool isParented = (tag != null && tag.isCameraParented);

    if (isParented)
    {
        // Always face the camera in local space
        float y = 180f + (applyFlipIfConfigured && bannerFlipForward ? 180f : 0f) + bannerExtraYawDegrees;
        obj.transform.localRotation = Quaternion.Euler(0f, y, 0f);
        return;
    }

    // World-anchored objects
    Vector3 toCam = cam.transform.position - obj.transform.position;

    if (bannerBillboardYawOnly)
    {
        toCam.y = 0f;
        if (toCam.sqrMagnitude <= 1e-6f) return;

        Quaternion rot = Quaternion.LookRotation(toCam.normalized, Vector3.up);
        if (applyFlipIfConfigured && bannerFlipForward) rot *= Quaternion.Euler(0f, 180f, 0f);
        if (Mathf.Abs(bannerExtraYawDegrees) > 0.001f) rot *= Quaternion.Euler(0f, bannerExtraYawDegrees, 0f);

        obj.transform.rotation = rot;
    }
    else
    {
        if (toCam.sqrMagnitude <= 1e-6f) return;

        Quaternion rot = Quaternion.LookRotation(toCam.normalized, Vector3.up);
        if (applyFlipIfConfigured && bannerFlipForward) rot *= Quaternion.Euler(0f, 180f, 0f);
        if (Mathf.Abs(bannerExtraYawDegrees) > 0.001f) rot *= Quaternion.Euler(0f, bannerExtraYawDegrees, 0f);

        obj.transform.rotation = rot;
    }
}



    private void SpawnPathArrows()
    {
        ClearPathArrows();
        if (pathArrowPrefab == null) return;
        for (int i = 1; i < path.Count; i++)
        {
            Vector2 a = path[i - 1], b = path[i];
            Vector3 start = LatLonToUnity(a);
            Vector3 end = LatLonToUnity(b);
            if (originTransform != null)
            {
                start = originTransform.TransformPoint(start);
                end = originTransform.TransformPoint(end);
            }

            Vector3 dir = end - start;
            if (dir.magnitude < 0.05f) continue;
            var arrow = Instantiate(pathArrowPrefab, end, Quaternion.LookRotation(dir.normalized, Vector3.up));
            if (anchorRoot != null) arrow.transform.SetParent(anchorRoot, true);
            pathArrows.Add(arrow);
        }
    }

    private void ClearPathArrows()
    {
        foreach (var g in pathArrows) if (g != null) Destroy(g);
        pathArrows.Clear();
    }

private string ExtractGuidanceLabelFromText(string text)
{
    if (string.IsNullOrEmpty(text)) return "go straight";
    string t = text.ToLowerInvariant();

    // NO u-turn branch
    if (t.Contains("slight left"))  return "slight left";
    if (t.Contains("slight right")) return "slight right";
    if (t.Contains("turn left") || t.Contains(" left"))  return "turn left";
    if (t.Contains("turn right") || t.Contains(" right")) return "turn right";
    if (t.Contains("straight") || t.Contains("proceed") || t.Contains("head") || t.Contains("continue"))
        return "go straight";

    return "go straight";
}


    private Vector3 ProjectToCameraFrustum(Camera cam, Vector3 worldPos, float minDistance = 8f)
    {
        if (cam == null) return worldPos;
        Vector3 vp = cam.WorldToViewportPoint(worldPos);
        if (vp.z < 0f)
        {
            vp.z = Mathf.Max(minDistance, forcedDisplayDistance);
            vp.x = 0.5f;
            vp.y = 0.5f;
        }
        else
        {
            vp.x = Mathf.Clamp(vp.x, 0.05f, 0.95f);
            vp.y = Mathf.Clamp(vp.y, 0.05f, 0.95f);
            vp.z = Mathf.Max(vp.z, minDistance);
        }
        return cam.ViewportToWorldPoint(vp);
    }

    void ForceOpaqueMaterials(GameObject root)
    {
        if (root == null) return;
        Camera cam = Camera.main;

        var canvases = root.GetComponentsInChildren<Canvas>(true);
        foreach (var c in canvases)
        {
            c.renderMode = RenderMode.WorldSpace;
            if (cam != null) c.worldCamera = cam;
            var rt = c.GetComponent<RectTransform>();
            if (rt != null && rt.localScale == Vector3.one) rt.localScale = Vector3.one * 0.01f;
            var cg = c.GetComponent<CanvasGroup>();
            if (cg != null) { cg.alpha = 1f; cg.interactable = true; cg.blocksRaycasts = true; }

            var graphics = c.GetComponentsInChildren<UnityEngine.UI.Graphic>(true);
            foreach (var g in graphics)
            {
                try
                {
                    Color col = g.color; col.a = 1f; g.color = col;
                    if (g is UnityEngine.UI.Image img) { img.material = null; img.canvasRenderer.SetAlpha(1f); }
                } catch { }
            }

            var tmpInCanvas = c.GetComponentsInChildren<TMP_Text>(true);
            foreach (var t in tmpInCanvas)
            {
                try
                {
                    Color col = t.color; col.a = 1f; t.color = col;
                    var rend = t.GetComponent<Renderer>();
                    if (rend != null)
                    {
                        foreach (var mat in rend.materials) TryMakeMaterialOpaque(mat);
                    }
                    if (t.fontSharedMaterial != null) TryMakeMaterialOpaque(t.fontSharedMaterial);
                } catch { }
            }

            CanvasSorterSet(c, 1000);
        }

        var tmps = root.GetComponentsInChildren<TMP_Text>(true);
        foreach (var t in tmps)
        {
            try
            {
                Color col = t.color; col.a = 1f; t.color = col;
                var rend = t.GetComponent<Renderer>();
                if (rend != null)
                    foreach (var mat in rend.materials) TryMakeMaterialOpaque(mat);
                if (t.fontSharedMaterial != null) TryMakeMaterialOpaque(t.fontSharedMaterial);
            } catch { }
        }

        var meshRenderers = root.GetComponentsInChildren<MeshRenderer>(true);
        foreach (var mr in meshRenderers)
            foreach (var mat in mr.materials) TryMakeMaterialOpaque(mat);

        var spriteRends = root.GetComponentsInChildren<SpriteRenderer>(true);
        foreach (var sr in spriteRends)
        {
            try
            {
                Color c = sr.color; c.a = 1f; sr.color = c;
                if (sr.sharedMaterial != null) TryMakeMaterialOpaque(sr.sharedMaterial);
            } catch { }
        }
    }

    void TryMakeMaterialOpaque(Material mat)
    {
        if (mat == null) return;
        try
        {
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 0f);
            if (mat.HasProperty("_Mode")) mat.SetFloat("_Mode", 0f);
            if (mat.HasProperty("_Cull")) mat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Back);
            if (mat.HasProperty("_ZWrite")) mat.SetInt("_ZWrite", 1);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
            try { mat.DisableKeyword("_ALPHATEST_ON"); } catch { }
            try { mat.DisableKeyword("_ALPHABlend_ON"); } catch { }
            try { mat.DisableKeyword("_ALPHAPREMULTIPLY_ON"); } catch { }
        }
        catch { }
    }

    void CanvasSorterSet(Canvas c, int order)
    {
        if (c == null) return;
        try { c.overrideSorting = true; c.sortingOrder = order; } catch { }
    }

    // ====== POLYLINE / JSON ======
    List<Vector2> DecodePolyline(string encoded)
    {
        var poly = new List<Vector2>();
        if (string.IsNullOrEmpty(encoded)) return poly;
        int index = 0, len = encoded.Length;
        int lat = 0, lng = 0;
        while (index < len)
        {
            int b, shift = 0, result = 0;
            do { b = encoded[index++] - 63; result |= (b & 0x1f) << shift; shift += 5; }
            while (b >= 0x20);
            int dlat = ((result & 1) != 0) ? ~(result >> 1) : (result >> 1);
            lat += dlat;

            shift = 0; result = 0;
            do { b = encoded[index++] - 63; result |= (b & 0x1f) << shift; shift += 5; }
            while (b >= 0x20);
            int dlng = ((result & 1) != 0) ? ~(result >> 1) : (result >> 1);
            lng += dlng;

            float finalLat = lat * 1e-5f;
            float finalLng = lng * 1e-5f;
            poly.Add(new Vector2(finalLat, finalLng));
        }
        return poly;
    }

    List<Step> ParseJsonForSteps(string json, out Location legStart)
    {
        legStart = null;
        try
        {
            string clean = Regex.Replace(json, @"""geocoded_waypoints"":\s*\[.*?\],", "");
            var response = JsonUtility.FromJson<RouteResponse>(clean);
            if (response == null || response.routes == null || response.routes.Length == 0 ||
                response.routes[0].legs == null || response.routes[0].legs.Length == 0) return new List<Step>();

            var leg = response.routes[0].legs[0];
            legStart = leg.start_location;
            var rawSteps = new List<Step>(leg.steps);
            for (int i = 0; i < rawSteps.Count; i++)
            {
                if (rawSteps[i].start_location == null)
                {
                    if (i == 0) rawSteps[i].start_location = legStart ?? rawSteps[i].end_location;
                    else rawSteps[i].start_location = rawSteps[i - 1].end_location;
                }
            }
            return rawSteps;
        }
        catch (Exception e)
        {
            Debug.LogError($"JSON parse steps error: {e.Message}");
            return new List<Step>();
        }
    }

    public int GetARSpawnPointCount()
{
    return (arSpawnPoints != null) ? arSpawnPoints.Count : 0;
}

public bool TryGetARSpawnPointLatLon(int index, out double lat, out double lon, out bool enabled)
{
    lat = 0; lon = 0; enabled = false;
    if (arSpawnPoints == null || index < 0 || index >= arSpawnPoints.Count) return false;
    var e = arSpawnPoints[index];
    if (e == null) return false;
    lat = e.lat; lon = e.lon; enabled = e.enabled;
    return true;
}

public bool TryGetDestinationLatLon(out double lat, out double lon)
{
    lat = destination.x; lon = destination.y;
    return (destination != Vector2.zero);
}

private float RouteEndAlong()
{
    return (cumulative != null && cumulative.Count > 0) ? cumulative[cumulative.Count - 1] : 0f;
}

private bool IsTurnLabel(string label)
{
    if (string.IsNullOrEmpty(label)) return false;
    string l = label.ToLowerInvariant();
    return l.Contains("turn") || l.Contains("slight") || l.Contains("u-turn") || l.Contains("roundabout");
}

private int FindNextTurnAfterAlong(float afterAlong, float tol = 0.5f)
{
    if (stepStartAlong == null || stepStartAlong.Count == 0) return -1;
    for (int s = 0; s < steps.Count; s++)
    {
        if (s >= stepStartAlong.Count) continue;
        float startA = stepStartAlong[s];
        if (startA <= afterAlong + tol) continue;
        string label = GetManeuverLabelForStep(s);
        if (IsTurnLabel(label)) return s;
    }
    return -1;
}

private string GetManeuverLabelForStep(int stepIndex)
{
    if (stepIndex < 0 || stepIndex >= steps.Count) return "Go straight";
    var st = steps[stepIndex];

    if (!string.IsNullOrEmpty(st.maneuver))
        return ManeuverToText(st.maneuver);

    string plain = StripHtmlTags(st.html_instructions).ToLowerInvariant();
    if (!string.IsNullOrEmpty(plain))
    {
        // NO u-turn mapping
        if (plain.Contains("slight left"))  return "Slight left";
        if (plain.Contains("slight right")) return "Slight right";
        if (plain.Contains("turn left") || plain.Contains(" left"))   return "Turn left";
        if (plain.Contains("turn right") || plain.Contains(" right")) return "Turn right";
        if (plain.Contains("roundabout")) return "Roundabout";
        if (plain.StartsWith("head") || plain.StartsWith("continue") || plain.Contains("straight"))
            return "Go straight";
    }
    return "Go straight";
}


private string ManeuverToText(string maneuver)
{
    if (string.IsNullOrEmpty(maneuver)) return "Go straight";
    string m = maneuver.ToLowerInvariant();

    // NO u-turn mapping
    if (m.Contains("slight left"))  return "Slight left";
    if (m.Contains("slight right")) return "Slight right";
    if (m.Contains("left"))         return "Turn left";
    if (m.Contains("right"))        return "Turn right";
    if (m.Contains("roundabout"))   return "Roundabout";
    return "Go straight";
}


private string StripHtmlTags(string html)
{
    if (string.IsNullOrEmpty(html)) return "";
    return System.Text.RegularExpressions.Regex.Replace(html, "<.*?>", "").Trim();
}

private class TripletEvent { public int stepIndex; public string label; public float along; }

private List<KeyValuePair<string, float>> BuildRicherPreviewSegments(float currentAlong)
{
    var result = new List<KeyValuePair<string, float>>();
    float routeEnd = RouteEndAlong();
    int limit = Mathf.Max(1, previewSegmentLimit);
    var events = new List<TripletEvent>();
    float eps = 0.01f;

    for (int s = 0; s < steps.Count; s++)
    {
        if (s >= stepStartAlong.Count) continue;
        float startA = stepStartAlong[s];
        if (startA < currentAlong + eps) continue;
        string label = GetManeuverLabelForStep(s);
        if (IsTurnLabel(label)) events.Add(new TripletEvent { stepIndex = s, label = label, along = startA });
    }

    events.Sort((a, b) => a.along.CompareTo(b.along));

    float cursor = currentAlong;
    int ei = 0;
    while (result.Count < limit)
    {
        bool hasEvent = ei < events.Count;
        float nextEventAlong = hasEvent ? events[ei].along : routeEnd;
        string nextEventLabel = hasEvent ? events[ei].label : null;

        // pre-turn straight section (don’t spam too far ahead)
        float preTurnEnd = Mathf.Max(cursor, nextEventAlong - turnAnnouncementThreshold);
        float preLen = Mathf.Max(0f, preTurnEnd - cursor);
        if (preLen > 0f)
        {
            result.Add(new KeyValuePair<string, float>("Go straight", preLen));
            cursor += preLen;
            if (result.Count >= limit) break;
        }

        // turn window (when within threshold)
        if (hasEvent && nextEventAlong - cursor <= turnAnnouncementThreshold)
        {
            float toEvent = Mathf.Max(0f, nextEventAlong - cursor);
            float turnLen = Mathf.Min(turnAnnouncementThreshold, toEvent);
            if (turnLen > 0f)
            {
                result.Add(new KeyValuePair<string, float>(nextEventLabel, turnLen));
                cursor += turnLen;
                if (result.Count >= limit) break;
            }

            if (Mathf.Abs(cursor - nextEventAlong) <= 0.01f || toEvent <= 0.01f)
            {
                cursor = nextEventAlong;
                ei++;
                continue;
            }
        }
        else
        {
            // tail to destination
            float tail = Mathf.Max(0f, routeEnd - cursor);
            if (tail > 0f)
            {
                result.Add(new KeyValuePair<string, float>("Go straight", tail));
                cursor += tail;
            }
            break;
        }
    }

    return result;
}


private void AlignWorldToGpsCourseOrCamera(float targetHeading)
{
    if (originTransform == null) return;
    if (alignRoutine != null) { StopCoroutine(alignRoutine); alignRoutine = null; }
    alignRoutine = StartCoroutine(SmoothAlignRoutine(targetHeading));
}


private IEnumerator SmoothAlignRoutine(float targetHeading)
{
    isAligning = true;
    float startY = originTransform.eulerAngles.y;
    float elapsed = 0f;
    float duration = 1f / Mathf.Max(0.1f, headingLerpSpeed); // controls how long smoothing takes

    // target rotation so that Unity's +Z faces your heading
    float endY = -targetHeading;

    while (elapsed < duration)
    {
        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / duration);
        float currentY = Mathf.LerpAngle(startY, endY, t);
        originTransform.rotation = Quaternion.Euler(0f, currentY, 0f);
        yield return null;
    }

    originTransform.rotation = Quaternion.Euler(0f, endY, 0f);
    lastAlignedHeading = targetHeading;
    isAligning = false;
    Debug.Log($"[Align] World smoothed to {targetHeading:0.0}°");

}



private float GetPathHeadingAtAlong(float alongMeters)
{
    if (path.Count < 2 || cumulative.Count < 2) return 0f;

    alongMeters = Mathf.Clamp(alongMeters, 0f, cumulative[cumulative.Count - 1]);

    int idx = cumulative.BinarySearch(alongMeters);
    if (idx < 0) idx = ~idx;

    idx = Mathf.Clamp(idx, 1, path.Count - 1);

    var a = path[idx - 1];
    var b = path[idx];

    return CalculateBearing(a.x, a.y, b.x, b.y); // degrees
}

private bool TryGetClosestPathLatLon(out float lat, out float lon, out int segIndex, out float segT)
{
    lat = lon = 0f; segIndex = -1; segT = 0f;
    if (path.Count < 2) return false;

    float latRef = (float)currentLat;
    float meanLatRad = latRef * Mathf.Deg2Rad;
    float mPerDegLat = 110574f;
    float mPerDegLon = 111320f * Mathf.Cos(meanLatRad);

    float best = float.MaxValue;
    int bestI = 0; float bestT = 0f;

    for (int i = 0; i < path.Count - 1; i++)
    {
        Vector2 a = path[i];
        Vector2 b = path[i + 1];

        // to local meters around user
        Vector2 aM = new((a.y - (float)currentLon) * mPerDegLon,
                         (a.x - (float)currentLat) * mPerDegLat);
        Vector2 bM = new((b.y - (float)currentLon) * mPerDegLon,
                         (b.x - (float)currentLat) * mPerDegLat);

        Vector2 ab = bM - aM;
        float ab2 = ab.sqrMagnitude;
        float t = (ab2 <= 1e-6f) ? 0f : Mathf.Clamp01(Vector2.Dot(-aM, ab) / ab2);
        Vector2 proj = aM + t * ab;
        float d = proj.magnitude;
        if (d < best) { best = d; bestI = i; bestT = t; }
    }

    Vector2 A = path[bestI];
    Vector2 B = path[bestI + 1];
    lat = Mathf.Lerp(A.x, B.x, bestT);
    lon = Mathf.Lerp(A.y, B.y, bestT);
    segIndex = bestI; segT = bestT;
    return true;
}

public void SnapPathOriginToCamera(float forwardMeters = 2f)
{
    if (originTransform == null || Camera.main == null) return;
    var root = contentRoot != null ? contentRoot : anchorRoot;
    if (root == null) return;
    if (!directionsFetched || path.Count < 2) return;

    // find closest point on the path (geo)
    if (!TryGetClosestPathLatLon(out float lat, out float lon, out _, out _)) return;

    // convert that lat/lon to current world position
    Vector3 enu = LatLonToUnity_Precise(lat, lon);                 // ENU meters from geo origin
    Vector3 closestWorld = originTransform.TransformPoint(enu);    // world position

    // desired place: a bit in front of camera
    Camera cam = Camera.main;
    Vector3 targetWorld = cam.transform.position + cam.transform.forward * forwardMeters;

    // delta to move *content* (not the camera, not ARSessionOrigin)
    Vector3 delta = targetWorld - closestWorld;
    root.position += delta;
}

private bool TryGetWorldOnPathAtAlong(float along, out Vector3 world, out Vector3 tangent)
{
    world = Vector3.zero; tangent = Vector3.forward;
    if (!directionsFetched || path.Count < 2 || cumulative.Count < 2 || originTransform == null) return false;

    along = Mathf.Clamp(along, 0f, cumulative[cumulative.Count - 1]);

    int idx = cumulative.BinarySearch(along);
    if (idx < 0) idx = ~idx;
    idx = Mathf.Clamp(idx, 1, path.Count - 1);

    Vector2 A = path[idx - 1], B = path[idx];
    float aAlong = cumulative[idx - 1], bAlong = cumulative[idx];
    float t = (bAlong - aAlong) > 1e-4f ? (along - aAlong) / (bAlong - aAlong) : 0f;

    float lat = Mathf.Lerp(A.x, B.x, t);
    float lon = Mathf.Lerp(A.y, B.y, t);

    // ENU -> world
    Vector3 enu = LatLonToUnity_Precise(lat, lon);
    world = originTransform.TransformPoint(enu);

    // direction of the path segment (world-horizontal)
    Vector3 enuA = LatLonToUnity_Precise(A.x, A.y);
    Vector3 enuB = LatLonToUnity_Precise(B.x, B.y);
    Vector3 segWorld = originTransform.TransformPoint(enuB) - originTransform.TransformPoint(enuA);
    segWorld.y = 0f;
    if (segWorld.sqrMagnitude < 1e-6f) segWorld = Vector3.forward;
    tangent = segWorld.normalized;
    return true;
}

public void SnapPathToCameraLookahead(float forwardMeters = 2f, float lateralBiasMeters = 0f)
{
    if (Camera.main == null || originTransform == null || !directionsFetched) return;

    // start from the along you are closest to
    float along0 = GetAlongDistanceOfClosestPoint();

    // we want the point forwardMeters further along the path, so it sits in front of you
    float alongTarget = Mathf.Clamp(along0 + Mathf.Max(0.1f, forwardMeters), 0f, RouteEndAlong());

    if (!TryGetWorldOnPathAtAlong(alongTarget, out var pathWorld, out var pathTangent)) return;

    // camera forward on ground plane
    var cam = Camera.main;
    Vector3 camFwd = cam.transform.forward; camFwd.y = 0f;
    if (camFwd.sqrMagnitude < 1e-6f) camFwd = pathTangent; // fallback
    camFwd.Normalize();
    Vector3 camRight = Vector3.Cross(Vector3.up, camFwd);

    // desired world target exactly forwardMeters in front of camera (+ optional lateral tweak)
    Vector3 targetWorld = cam.transform.position + camFwd * forwardMeters + camRight * lateralBiasMeters;

    // translate content so the path point lands at target
    Transform root = (contentRoot != null) ? contentRoot : anchorRoot;
    if (root == null) return;

    Vector3 delta = targetWorld - pathWorld;
    root.position += delta;
}


}