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
/// ARNavigation (polyline-aware) - navigation + spawn/anchor helpers with Canvas/Panel support
/// Place this script on an appropriate manager GameObject in your AR scene.
/// </summary>
public class ARNavigation : MonoBehaviour
{
    [Header("AR Prefabs")]
    [SerializeField] private GameObject pathArrowPrefab;

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

    [Header("Google API (dev)")]
    [SerializeField] private string googleApiKey = "YOUR_API_KEY_HERE";

    [Header("AR Guidance Arrow (visual)")]
    [SerializeField] private GameObject guidanceArrowPrefab;
    [SerializeField] private float guidanceArrowDistance = 1.6f;
    [SerializeField] private float guidanceArrowHeightOffset = -0.25f;
    [SerializeField] private float guidanceArrowRotateSpeed = 8f;

    [Header("Force objects visible on camera (screen-projection)")]
    [Tooltip("If true, AR objects will be placed relative to the camera so they are always visible on-screen.")]
    [SerializeField] private bool forceScreenPlacement = false;

    [Tooltip("When forcing screen placement, how far in front of the camera to place the object (meters).")]
    [SerializeField] private float forcedDisplayDistance = 5f;

    [Tooltip("When forcing screen placement, additional height above the camera (meters).")]
    [SerializeField] private float forcedHeightAboveCamera = 2f;

    [Header("Sequential spawn by distance")]
    [Tooltip("If true, spawn AR entries ordered by distance to user (closest first).")]
    [SerializeField] private bool sequentialSpawnByDistance = true;

    [Tooltip("Delay between spawning sequential objects (seconds). 0 for immediate spawn all.")]
    [SerializeField] private float spawnStaggerSeconds = 0.12f;

    [Header("Vertical tuning (GPS / demo)")]
    [Tooltip("Global vertical offset (meters) to raise all spawned AR objects above origin altitude. Useful for mountain/demonstration scenes.")]
    [SerializeField] private float arGlobalHeightOffset = 0f;

    [Tooltip("Maximum vertical delta (meters) allowed between camera Y and computed GPS-based Y. If exceeded, GPS Y will be clamped to camera +/- this value.")]
    [SerializeField] private float maxVerticalDeltaFromCamera = 20f;

    [Header("Guidance Arrow Stability")]
    [Tooltip("If true, the guidance arrow will be parented to the camera so it stays stable on-screen.")]
    [SerializeField] private bool guidanceParentToCamera = true;

    [Tooltip("Seconds used by SmoothDampAngle to smooth yaw changes. Higher = smoother/slower.")]
    [SerializeField] private float guidanceSmoothingTime = 0.15f;

    // runtime smoothing state
    private float guidanceCurrentYaw = 0f;
    private float guidanceYawVelocity = 0f;

    [System.Serializable]
    public class ARSpawnPoint
    {
        public string name;
        public float lat;
        public float lon;
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

    // runtime storage
    private List<GameObject> spawnedARObjects = new List<GameObject>();
    private List<ARSpawnPoint> spawnedARData = new List<ARSpawnPoint>();
    [Header("Spawn orientation")]
    [Tooltip("If true, apply a 180° yaw correction when facing spawned prefabs to camera. Useful when model's front faces -Z.")]
    [SerializeField] private bool invertSpawnedPrefabFacing = true;
    // AR managers
    private ARAnchorManager anchorManager;

    // sensors / navigation state
    private float currentLat;
    private float currentLon;
    private float currentHeading;

    // altitude
    private float originAlt = 0f;
    private float currentAlt = 0f;

    // stable origin variables
    private bool originSet = false;
    private float originLat = 0f;
    private float originLon = 0f;

    // guidance arrow instance
    private GameObject guidanceArrowInstance = null;
    private string guidanceLabel = "Go straight";

    // path & routing structures
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

    // smoothing for along-distance
    private Queue<float> alongWindow = new Queue<float>();
    private int alongWindowSize = 4;
    private float smoothedAlong = 0f;

    private float uiCurrentZ = 0f;

    // origin transform (ARSessionOrigin or XROrigin) - if present use it when placing world objects
    private Transform originTransform = null;
    private object xrOriginInstance = null; // for reflection-based XROrigin detection

    // camera-parented bookkeeping
    private const float CAMERA_MIN_Z = 0.8f;
    private int cameraParentedVisibleIndex = -1;

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

    void Awake()
    {
        anchorManager = FindObjectOfType<ARAnchorManager>();
        if (anchorManager == null)
        {
            Debug.LogWarning("ARNavigation: No ARAnchorManager found. Anchors will still be created using AddComponent<ARAnchor>().");
        }

        // Choose origin transform: prefer ARSessionOrigin, otherwise try to find XROrigin via reflection
        var arOrigin = FindObjectOfType<ARSessionOrigin>();
        if (arOrigin != null)
        {
            originTransform = arOrigin.transform;
            if (debugLogs) Debug.Log("ARNavigation: Using ARSessionOrigin as originTransform.");
        }
        else
        {
            // Try to find common XROrigin types by name (works across XR packages)
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

        if (originTransform == null)
        {
            if (debugLogs) Debug.LogWarning("ARNavigation: No ARSessionOrigin/XROrigin found; world coordinates will be used as-is.");
        }
    }

    void Start()
    {
        Input.compass.enabled = true;
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

        StartCoroutine(InitAndMaybeStartNav());
    }

    IEnumerator InitAndMaybeStartNav()
    {
        yield return null;

        float waitTimeout = 8f; // give location more time
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
            originLat = currentLat;
            originLon = currentLon;
            originSet = true;

            // record altitude if available
            originAlt = Input.location.lastData.altitude;
            currentAlt = originAlt;

            if (debugLogs) Debug.Log($"Location ready. origin set to {originLat}, {originLon} alt={originAlt}");
        }
        else
        {
            Debug.LogWarning("InitAndMaybeStartNav: location service not running after timeout.");
        }

        if (NavigationData.HasData && !string.IsNullOrEmpty(NavigationData.DestinationName))
        {
            if (debugText != null)
                debugText.text += $" Destination: {NavigationData.DestinationName}";
        }

        if (NavigationData.HasData)
        {
            destination = new Vector2(NavigationData.Destination.x, NavigationData.Destination.y);
            OnStartButtonPressed(NavigationData.Source, NavigationData.Destination);
            NavigationData.HasData = false;
        }
        else
        {
            if (debugText != null)
                debugText.text = "NO NAVIGATION DATA RECEIVED!\nPlease go back to Navigation and set Place properly.";
        }
    }
public bool TryGetOrigin(out double lat, out double lon, out Transform originXform)
{
    lat = originLat;
    lon = originLon;
    originXform = originTransform;
    return originSet;
}

// Public: convert (lat,lon) to scene world position using ARNavigation's logic
// heightOffset is added on top of ARNavigation's own vertical placement rules.
public Vector3 LatLonToWorld(Vector2 latlon, float heightOffset = 0f)
{
    // Convert to local meters (east, 0, north) relative to geo origin
    Vector3 worldPosLocal = LatLonToUnity(latlon);

    // Transform to scene world coordinates if originTransform exists
    Vector3 worldPos = (originTransform != null) ? originTransform.TransformPoint(worldPosLocal) : worldPosLocal;

    // --- vertical placement (mirror ARNavigation's approach) ---
    float baseAlt = float.NaN;
    if (originSet) baseAlt = originAlt;
    else if (Input.location.status == LocationServiceStatus.Running) baseAlt = Input.location.lastData.altitude;

    float y;
    if (!float.IsNaN(baseAlt))
    {
        y = baseAlt + arGlobalHeightOffset + heightOffset;
        if (Camera.main != null)
        {
            float camY = Camera.main.transform.position.y;
            if (Mathf.Abs(y - camY) > maxVerticalDeltaFromCamera)
                y = camY + Mathf.Sign(y - camY) * maxVerticalDeltaFromCamera;
        }
    }
    else
    {
        y = (Camera.main != null ? Camera.main.transform.position.y : 0f) + forcedHeightAboveCamera + heightOffset;
    }

    worldPos.y = y;
    return worldPos;
}

// Public: retrieve a lat/lon from Destination or arSpawnPoints[index]
public bool TryGetLatLonFromNavigation(out Vector2 latlon, int spawnPointIndex = -1)
{
    // prefer explicit spawn point if valid
    if (spawnPointIndex >= 0 && arSpawnPoints != null && spawnPointIndex < arSpawnPoints.Count)
    {
        var e = arSpawnPoints[spawnPointIndex];
        latlon = new Vector2(e.lat, e.lon);
        return true;
    }

    // otherwise use destination if available (non-zero)
    if (destination != Vector2.zero)
    {
        latlon = destination;
        return true;
    }

    latlon = Vector2.zero;
    return false;
}
    #region Public API (Navigation)
    public void OnStartButtonPressed(Vector2 source, Vector2 dest)
    {
        destination = new Vector2(dest.x, dest.y);
        StartNavigation(source, dest);
        Debug.Log($"Navigation Started - Source: {source.x},{source.y} Destination: {dest.x},{dest.y}");
    }

    public void StartNavigation(Vector2 source, Vector2 dest)
    {
        StartCoroutine(FetchDirections(source, dest));
    }

    IEnumerator FetchDirections(Vector2 source, Vector2 dest)
    {
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
                    UpdateStepsPreview();
                    UpdateBigInstruction();
                    if (guidanceArrowInstance != null) guidanceArrowInstance.SetActive(true);
                    if (debugLogs) Debug.Log($"Ready: path pts {path.Count}, steps {steps.Count}");

                    if (spawnAllOnDirectionsReady)
                        
                       SpawnAllARObjects(); 
                }
            }
            else
            {
                Debug.LogError($"API error: {www.error}");
            }
        }
    }
    #endregion

    void Update()
    {
        if (Input.location.status != LocationServiceStatus.Running) return;

        currentLat = Input.location.lastData.latitude;
        currentLon = Input.location.lastData.longitude;
        currentHeading = Input.compass.enabled ? Input.compass.trueHeading : 0f;
        currentAlt = Input.location.lastData.altitude;

        if (!directionsFetched || path.Count < 2) return;

        float currentAlong = GetAlongDistanceOfClosestPoint();
        SmoothAlong(currentAlong);

        AlignCurrentStepToAlong();

        UpdateUIArrow();
        CheckStepProgress();
        CheckArrival();

        UpdateBigInstruction();
        UpdateStepsPreview();
        UpdateThresholdTurnUI();
        guidanceLabel = ExtractGuidanceLabelFromText(bigInstructionText.text);
        UpdateGuidanceArrow();
        UpdateSpawnedLabels();

        // Update spawned billboards
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

                if (faceSpawnedObjectsEveryFrame && !forceScreenPlacement) // when forced screen placement, prefab is child of camera and rotates with camera
                    FaceObjectToCamera(go, Camera.main);

                if (updateSpawnedPositionsWithGPS && i < spawnedARData.Count && !forceScreenPlacement)
                {
                    var d = spawnedARData[i];
                    Vector3 newPos = LatLonToUnity(new Vector2(d.lat, d.lon));

                    // compute altitude base
                    float baseAlt = float.NaN;
                    if (originSet) baseAlt = originAlt;
                    else if (Input.location.status == LocationServiceStatus.Running) baseAlt = Input.location.lastData.altitude;

                    float targetY;
                    if (!float.IsNaN(baseAlt))
                    {
                        targetY = baseAlt + arGlobalHeightOffset + d.heightOffset;
                        // clamp to camera if delta too large
                        if (Camera.main != null)
                        {
                            float camY = Camera.main.transform.position.y;
                            if (Mathf.Abs(targetY - camY) > maxVerticalDeltaFromCamera)
                            {
                                targetY = camY + Mathf.Sign(targetY - camY) * maxVerticalDeltaFromCamera;
                            }
                        }
                    }
                    else
                    {
                        // fallback to camera-based placement
                        targetY = Camera.main.transform.position.y + forcedHeightAboveCamera + d.heightOffset;
                    }

                    newPos.y = targetY;

                    // If forcing screen placement at runtime: project to camera frustum so object stays visible
                    if (forceScreenPlacement && Camera.main != null)
                    {
                        newPos = ProjectToCameraFrustum(Camera.main, (originTransform != null) ? originTransform.TransformPoint(newPos) : newPos, forcedDisplayDistance);
                        // if originTransform used earlier, and we fed worldPos already transformed, ensure staying consistent:
                        // our ProjectToCameraFrustum returns world coords, so we're safe to set directly.
                    }

                    // smooth vertical movement so objects move naturally as GPS updates
                    go.transform.position = Vector3.Lerp(go.transform.position, newPos, Time.deltaTime * 6f);
                }
            }
        }
        void UpdateARObjectsVisibility()
{
    if (spawnedARObjects == null || spawnedARObjects.Count == 0) return;

    float userLat = currentLat;
    float userLon = currentLon;

    for (int i = 0; i < spawnedARObjects.Count; i++)
    {
        if (spawnedARObjects[i] == null || i >= spawnedARData.Count) continue;

        var data = spawnedARData[i];
        float dist = HaversineDistance(userLat, userLon, data.lat, data.lon);

        bool shouldShow = dist < 30f; // <-- choose your visibility radius in meters
        spawnedARObjects[i].SetActive(shouldShow);
    }
}

    }
public int GetARSpawnPointCount()
{
    return (arSpawnPoints != null) ? arSpawnPoints.Count : 0;
}

public bool TryGetARSpawnPointLatLon(int index, out Vector2 latlon, out bool enabled)
{
    latlon = Vector2.zero;
    enabled = false;
    if (arSpawnPoints == null || index < 0 || index >= arSpawnPoints.Count) return false;
    var e = arSpawnPoints[index];
    if (e == null) return false;
    latlon = new Vector2(e.lat, e.lon);
    enabled = e.enabled;
    return true;
}

// Optional: expose destination if you want a cube there too
public bool TryGetDestinationLatLon(out Vector2 latlon)
{
    latlon = destination;
    return (destination != Vector2.zero);
}
    #region Spawn helpers (Canvas + Anchors)
    public IEnumerator SpawnAllARObjects_WhenLocationReady(float timeoutSeconds = 8f)
    {
        if (debugLogs) Debug.Log("SpawnAllARObjects_WhenLocationReady called, waiting for location...");
        float t = 0f;
        while (Input.location.status != LocationServiceStatus.Running && t < timeoutSeconds)
        {
            t += Time.deltaTime;
            yield return null;
        }
        if (Input.location.status == LocationServiceStatus.Running)
        {
            currentLat = Input.location.lastData.latitude;
            currentLon = Input.location.lastData.longitude;
            originLat = currentLat;
            originLon = currentLon;
            originSet = true;
            originAlt = Input.location.lastData.altitude;
            currentAlt = originAlt;
            if (debugLogs) Debug.Log($"Location ready. origin set to {originLat}, {originLon} alt={originAlt}");
        }
        else
        {
            if (debugLogs) Debug.LogWarning("SpawnAllARObjects_WhenLocationReady: location did not start within timeout.");
        }

        // spawn
        if (sequentialSpawnByDistance)
            StartCoroutine(SpawnAllARObjects_SequentialByDistance());
        else
            SpawnAllARObjects();
    }

    // Convert any Canvas inside the prefab to World Space and configure it
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
            if (rt != null)
            {
                // conservative default scale so Figma exports don't become huge in AR
                // only change scale if the prefab canvas is 1,1,1 to avoid double-scaling
                if (rt.localScale == Vector3.one)
                    rt.localScale = Vector3.one * 0.01f;
            }

            var scaler = c.GetComponent<UnityEngine.UI.CanvasScaler>();
            if (scaler != null)
            {
                scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ConstantPixelSize;
                scaler.scaleFactor = 1f;
            }
        }
    }

    // Try to apply a material to the first MeshRenderer found in the spawned hierarchy.
    void ApplySpawnMaterial(GameObject spawnedObj, Material mat)
    {
        if (mat == null || spawnedObj == null) return;
        var mr = spawnedObj.GetComponentInChildren<MeshRenderer>();
        if (mr != null)
        {
            mr.material = new Material(mat);
        }
    }

    // Hide only the label inside the prefab; DO NOT disable the whole prefab
    void HideInWorldText(GameObject spawnedObj)
    {
        if (spawnedObj == null) return;
        var marker = spawnedObj.GetComponentInChildren<ARMarker>();
        if (marker != null)
        {
            marker.HideLabel(true);
            return;
        }
        var tmp = spawnedObj.GetComponentInChildren<TMP_Text>();
        if (tmp != null) tmp.gameObject.SetActive(false);
    }

    /// <summary>
    /// Robust attempt to create an ARAnchor for a given Pose.
    /// This uses reflection to call ARAnchorManager.AddAnchor or TryAddAnchor if available,
    /// and falls back to AddComponent<ARAnchor>() or a plain parent GameObject if needed.
    /// Returns the ARAnchor instance (or null if none could be created).
    /// </summary>
    ARAnchor TryCreateAnchor(Pose pose)
    {
        if (anchorManager != null)
        {
            // Try AddAnchor(Pose) via reflection
            Type mgrType = anchorManager.GetType();
            try
            {
                // 1) Try AddAnchor(Pose) -> returns ARAnchor in newer ARFoundation
                MethodInfo addAnchorMethod = mgrType.GetMethod("AddAnchor", new Type[] { typeof(Pose) });
                if (addAnchorMethod != null)
                {
                    object result = addAnchorMethod.Invoke(anchorManager, new object[] { pose });
                    if (result is ARAnchor created && created != null)
                    {
                        if (debugLogs) Debug.Log("TryCreateAnchor: created anchor via AddAnchor(Pose).");
                        return created;
                    }
                }

                // 2) Try TryAddAnchor(Pose, out ARAnchor) signature
                MethodInfo tryAddMethod = mgrType.GetMethod("TryAddAnchor", new Type[] { typeof(Pose), typeof(ARAnchor).MakeByRefType() });
                if (tryAddMethod != null)
                {
                    // prepare args
                    object[] args = new object[] { pose, null };
                    bool ok = (bool)tryAddMethod.Invoke(anchorManager, args);
                    if (ok && args[1] is ARAnchor created2 && created2 != null)
                    {
                        if (debugLogs) Debug.Log("TryCreateAnchor: created anchor via TryAddAnchor(Pose, out ARAnchor).");
                        return created2;
                    }
                }
            }
            catch (Exception ex)
            {
                if (debugLogs) Debug.LogWarning($"TryCreateAnchor: reflection call failed: {ex.Message}");
            }
        }

        // fallback: we'll return null here so caller can AddComponent<ARAnchor>() on a GameObject parent
        return null;
    }

    // Spawn prefab under an anchor or as child of camera when parentToCamera true.
    private GameObject SpawnAnchoredOrCameraPrefab(GameObject prefab, Vector3 worldPosition, string label, Sprite icon, float heightOffset = 0f, Material mat = null, bool parentToCamera = false)
    {
        if (prefab == null) return null;

        Camera cam = Camera.main;

        // --- camera-parented placement (force-screen)
        if (parentToCamera && cam != null)
        {
            // Compute camera-local coordinates of desired world position
            Vector3 local = cam.transform.InverseTransformPoint(worldPosition);

            // ensure minimum forward distance to avoid being too close
            if (local.z < forcedDisplayDistance) local.z = forcedDisplayDistance;
            if (local.z < CAMERA_MIN_Z) local.z = CAMERA_MIN_Z;

            // set vertical offset relative to camera (override to forcedHeightAboveCamera + heightOffset)
            local.y = forcedHeightAboveCamera + heightOffset;

            // clamp lateral offset so object remains on-screen.
            float vFovRad = cam.fieldOfView * Mathf.Deg2Rad * 0.5f;
            float halfHorizontal = Mathf.Atan(Mathf.Tan(vFovRad) * cam.aspect);
            float maxX = Mathf.Tan(halfHorizontal) * local.z * 0.95f;
            local.x = Mathf.Clamp(local.x, -maxX, maxX);

            // instantiate as child of camera using local transform
            GameObject obj = Instantiate(prefab, cam.transform);
            obj.transform.localPosition = local;
            obj.transform.localRotation = Quaternion.identity;

            ConfigureWorldSpaceCanvas(obj, cam);
             ApplySpawnMaterial(obj, mat);
            ForceOpaqueMaterials(obj);
           

            var markerComp = obj.GetComponentInChildren<ARMarker>();
            if (markerComp != null)
            {
                markerComp.SetData(label, icon);
                // initially hide label; we will show only nearest in UpdateSpawnedLabels()
                markerComp.HideLabel(true);
            }

            // Tag camera-parented object
            var tag = obj.GetComponent<CameraParentedTag>();
            if (tag == null) tag = obj.AddComponent<CameraParentedTag>();
            tag.isCameraParented = true;

            if (debugLogs) Debug.Log($"SpawnAnchoredOrCameraPrefab: Spawned (camera-parented) {label} at local {obj.transform.localPosition}");
            return obj;
        }

        // --- preferred: try ARAnchorManager reflection-based creation
        Pose pose = new Pose(worldPosition, Quaternion.identity);
        ARAnchor createdAnchor = TryCreateAnchor(pose);
        if (createdAnchor != null)
        {
            // instantiate prefab as child of created anchor
            GameObject objInst = Instantiate(prefab, createdAnchor.transform);
            objInst.transform.localPosition = new Vector3(0f, heightOffset, 0f);
            objInst.transform.localRotation = Quaternion.identity;
            ConfigureWorldSpaceCanvas(objInst, Camera.main);
                    ApplySpawnMaterial(objInst, mat);
            ForceOpaqueMaterials(objInst);
    

            var marker = objInst.GetComponentInChildren<ARMarker>();
            if (marker != null)
            {
                marker.SetData(label, icon);
                marker.HideLabel(true);
            }

            if (debugLogs) Debug.Log($"SpawnAnchoredOrCameraPrefab: Spawned anchor via ARAnchorManager {label} at {worldPosition}");
            if (debugLogs) DrawDebugSphere(worldPosition, Color.green, 6f);
            return objInst;
        }

        // --- fallback: make an anchor GameObject and AddComponent<ARAnchor>()
        GameObject anchorGO = new GameObject($"ARAnchor_{(string.IsNullOrEmpty(label) ? "obj" : label)}");
        anchorGO.transform.position = worldPosition;
        anchorGO.transform.rotation = Quaternion.identity;

        // Try to parent anchor under ARSessionOrigin.trackablesParent if available so anchor uses AR coordinate frame
        var arOrigin = FindObjectOfType<ARSessionOrigin>();
        if (arOrigin != null)
        {
            Transform trackParent = null;
#pragma warning disable 618
            var tpProp = typeof(ARSessionOrigin).GetProperty("trackablesParent");
            if (tpProp != null)
            {
                trackParent = tpProp.GetValue(arOrigin) as Transform;
            }
#pragma warning restore 618
            if (trackParent == null) trackParent = arOrigin.transform;
            anchorGO.transform.SetParent(trackParent, true);
        }
        else if (originTransform != null)
        {
            // parent under detected origin (XROrigin) if ARSessionOrigin not found
            anchorGO.transform.SetParent(originTransform, true);
        }

        ARAnchor arAnchor = null;
        try
        {
            arAnchor = anchorGO.AddComponent<ARAnchor>();
        }
        catch (Exception e)
        {
            if (debugLogs) Debug.LogWarning($"SpawnAnchoredOrCameraPrefab: AddComponent<ARAnchor>() failed: {e.Message}");
        }

        if (arAnchor == null)
        {
            // fallback to plain instance (no anchor). Parent fallback under ARSessionOrigin if available.
            GameObject fallback = Instantiate(prefab, worldPosition + Vector3.up * heightOffset, Quaternion.identity);
            if (arOrigin != null) fallback.transform.SetParent(arOrigin.transform, true);
            else if (originTransform != null) fallback.transform.SetParent(originTransform, true);

            ConfigureWorldSpaceCanvas(fallback, Camera.main);
             ApplySpawnMaterial(fallback, mat);
            ForceOpaqueMaterials(fallback);
           
            var mf = fallback.GetComponentInChildren<ARMarker>();
            if (mf != null) mf.SetData(label, icon);
            HideInWorldText(fallback);
            if (debugLogs) Debug.Log($"SpawnAnchoredOrCameraPrefab: Spawned fallback (no anchor) {label} at {worldPosition}");
            if (debugLogs) DrawDebugSphere(worldPosition, Color.magenta, 6f);
            return fallback;
        }

        // instantiate prefab as child of anchorGO
        GameObject objInst2 = Instantiate(prefab, anchorGO.transform);
        objInst2.transform.localPosition = new Vector3(0f, heightOffset, 0f);
        objInst2.transform.localRotation = Quaternion.identity;
        ConfigureWorldSpaceCanvas(objInst2, Camera.main);
        ApplySpawnMaterial(objInst2, mat);
        ForceOpaqueMaterials(objInst2);
        
        var marker2 = objInst2.GetComponentInChildren<ARMarker>();
        if (marker2 != null)
        {
            marker2.SetData(label, icon);
            marker2.HideLabel(true);
        }

        if (debugLogs) Debug.Log($"SpawnAnchoredOrCameraPrefab: Spawned anchor {label} at {worldPosition}");
        if (debugLogs) DrawDebugSphere(worldPosition, Color.green, 6f);
        return objInst2;
    }

    // Small helper to draw a temporary debug sphere at a world position
    void DrawDebugSphere(Vector3 pos, Color c, float lifetime = 5f)
    {
        if (!debugLogs) return;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        GameObject dbg = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        dbg.transform.position = pos;
        dbg.transform.localScale = Vector3.one * 0.5f;
        var mr = dbg.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            var mat = new Material(Shader.Find("Standard"));
            mat.color = c;
            mr.material = mat;
        }
        Destroy(dbg, lifetime);
#endif
    }

    // Simple wrapper that spawns anchored prefab, stores metadata and returns the spawned GameObject
    public GameObject SpawnARObjectAtLatLon(GameObject prefab, Vector2 latlon, string label = null, float heightOffset = 0f, Sprite icon = null, Material mat = null)
    {
        if (prefab == null)
        {
            Debug.LogError("SpawnARObjectAtLatLon: prefab is null");
            return null;
        }

        // Convert lat/lon to world position (meters) relative to origin
        Vector3 worldPosLocal = LatLonToUnity(latlon);

        // If we have an originTransform, transform to that coordinate frame's world coordinates
        Vector3 worldPos;
        if (originTransform != null)
            worldPos = originTransform.TransformPoint(worldPosLocal);
        else
            worldPos = worldPosLocal;

        // Determine baseline altitude: prefer originAlt/currentAlt if available, else use camera Y (fallback)
        float baseAlt = float.NaN;
        if (originSet) baseAlt = originAlt;
        else if (Input.location.status == LocationServiceStatus.Running) baseAlt = Input.location.lastData.altitude;

        float candidateY;
        if (!float.IsNaN(baseAlt))
        {
            candidateY = baseAlt + arGlobalHeightOffset + heightOffset;

            // clamp to camera if delta too large
            if (Camera.main != null)
            {
                float camY = Camera.main.transform.position.y;
                if (Mathf.Abs(candidateY - camY) > maxVerticalDeltaFromCamera)
                    candidateY = camY + Mathf.Sign(candidateY - camY) * maxVerticalDeltaFromCamera;
            }
        }
        else if (Camera.main != null)
        {
            candidateY = Camera.main.transform.position.y + forcedHeightAboveCamera + heightOffset;
        }
        else
        {
            candidateY = arGlobalHeightOffset + heightOffset;
        }

        // set y on worldPos (world coords)
        worldPos.y = candidateY;

        // If forcing screen placement, compute camera-parented placement or projection
        bool parentToCamera = forceScreenPlacement && Camera.main != null;

        // If we are not parenting to camera but still want objects visible, project world pos into camera frustum
        if (!parentToCamera && forceScreenPlacement && Camera.main != null)
        {
            // Project world pos to camera frustum but keep as world coordinates
            worldPos = ProjectToCameraFrustum(Camera.main, worldPos, forcedDisplayDistance);
        }

        if (debugLogs) Debug.Log($"SpawnARObjectAtLatLon: target worldPos {worldPos} (latlon {latlon.x},{latlon.y}) parentToCamera={parentToCamera} baseAlt={(float.IsNaN(baseAlt) ? float.NaN : baseAlt)}");

        GameObject obj = SpawnAnchoredOrCameraPrefab(prefab, worldPos, label, icon, heightOffset, mat, parentToCamera);
        if (obj == null)
        {
            Debug.LogError("SpawnARObjectAtLatLon: failed to spawn prefab");
            return null;
        }

        obj.name = $"AR_{latlon.x:F6}_{latlon.y:F6}";
        spawnedARObjects.Add(obj);
        spawnedARData.Add(new ARSpawnPoint { name = label ?? "", lat = latlon.x, lon = latlon.y, prefab = prefab, material = mat, heightOffset = heightOffset, icon = icon });

        // orient billboard to camera for readability (if not camera-parented)
        if (!(obj.GetComponent<CameraParentedTag>() != null && obj.GetComponent<CameraParentedTag>().isCameraParented))
            FaceObjectToCamera(obj, Camera.main, true);

        if (debugLogs) Debug.Log($"Spawned anchored AR object '{label}' at world {obj.transform.position} (latlon {latlon.x},{latlon.y})");

        return obj;
    }

    /// <summary>
    /// Spawn all AR objects configured in arSpawnPoints (caller usually SpawnAllARObjects or after directions ready).
    /// Immediate (all at once) version.
    /// </summary>
    public void SpawnAllARObjects()
    {
        ClearSpawnedARObjects();

        if (arSpawnPoints == null || arSpawnPoints.Count == 0)
        {
            Debug.Log("SpawnAllARObjects: no AR spawn points configured.");
            return;
        }

        RefreshCurrentLocationFromService();
        Debug.Log($"SpawnAllARObjects: GPS status {Input.location.status} currentLat={currentLat:F6}, currentLon={currentLon:F6} originSet={originSet}");

        Camera cam = Camera.main;

        for (int i = 0; i < arSpawnPoints.Count; i++)
        {
            var entry = arSpawnPoints[i];
            if (!entry.enabled) continue;
            if (entry.prefab == null)
            {
                Debug.LogWarning($"AR spawn entry '{entry.name}' has no prefab assigned. Skipping.");
                continue;
            }

            Vector2 latlon = new Vector2(entry.lat, entry.lon);

            // Spawn first (this will compute and apply final Y inside SpawnARObjectAtLatLon)
            GameObject obj = SpawnARObjectAtLatLon(entry.prefab, latlon, entry.name, entry.heightOffset, entry.icon, entry.material);
            if (obj == null)
            {
                Debug.LogWarning($"SpawnAllARObjects: failed to spawn '{entry.name}'");
                continue;
            }

            // ensure a friendly name
            obj.name = $"AR_{(string.IsNullOrEmpty(entry.name) ? i.ToString() : entry.name)}";

            // Now log the *actual* world position where the object ended up
            Vector3 placedPos = obj.transform.position;
            Debug.Log($"Spawned '{entry.name}' at latlon ({latlon.x:F6},{latlon.y:F6}) -> final world {placedPos} (parent:{(obj.transform.parent? obj.transform.parent.name : "null")})");

            var markerComp = obj.GetComponentInChildren<ARMarker>();
            if (markerComp != null)
                markerComp.SetData(entry.name, entry.icon);

            if (cam != null && !forceScreenPlacement) FaceObjectToCamera(obj, cam);
        }

        Debug.Log($"SpawnAllARObjects: spawned {spawnedARObjects.Count} objects.");
    }

    // Sequential spawn implementation sorted by distance (closest first), uses stagger delay
    private IEnumerator SpawnAllARObjects_SequentialByDistance()
    {
        ClearSpawnedARObjects();

        if (arSpawnPoints == null || arSpawnPoints.Count == 0)
        {
            Debug.Log("SpawnAllARObjects_SequentialByDistance: no AR spawn points configured.");
            yield break;
        }

        RefreshCurrentLocationFromService();
        Debug.Log($"Sequential spawn by distance start: user {currentLat},{currentLon}");

        // make a local copy and sort by Haversine distance to user
        List<ARSpawnPoint> list = new List<ARSpawnPoint>(arSpawnPoints);
        list.RemoveAll(e => e == null || !e.enabled || e.prefab == null);

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
            Vector2 latlon = new Vector2(entry.lat, entry.lon);
            GameObject obj = SpawnARObjectAtLatLon(entry.prefab, latlon, entry.name, entry.heightOffset, entry.icon, entry.material);
            if (obj != null)
            {
                obj.name = $"AR_{(string.IsNullOrEmpty(entry.name) ? i.ToString() : entry.name)}";
                var markerComp = obj.GetComponentInChildren<ARMarker>();
                if (markerComp != null)
                    markerComp.SetData(entry.name, entry.icon);
                if (cam != null && !forceScreenPlacement) FaceObjectToCamera(obj, cam);
                if (debugLogs) Debug.Log($"Sequential spawn created '{entry.name}'");
            }

            if (spawnStaggerSeconds > 0f)
                yield return new WaitForSeconds(spawnStaggerSeconds);
            else
                yield return null;
        }

        Debug.Log($"SpawnAllARObjects_SequentialByDistance: spawned {spawnedARObjects.Count} objects.");
    }

    public void ClearSpawnedARObjects()
    {
        for (int i = 0; i < spawnedARObjects.Count; i++)
        {
            if (spawnedARObjects[i] != null)
                Destroy(spawnedARObjects[i]);
        }
        spawnedARObjects.Clear();
        spawnedARData.Clear();
    }
    #endregion

    #region AR label & HUD updates
    void UpdateSpawnedLabels()
    {
        if (spawnedARObjects == null || spawnedARObjects.Count == 0)
        {
            if (arObjectsDistanceText != null) arObjectsDistanceText.text = "";
            return;
        }

        RefreshCurrentLocationFromService();
        float userLat = currentLat;
        float userLon = currentLon;
        float userHeading = Input.compass.enabled ? Input.compass.trueHeading : 0f;

        System.Text.StringBuilder sb = new System.Text.StringBuilder();

        // --- camera-parented handling: find nearest camera-parented object (if any) and only show it
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
                float d;
                if (go.transform.parent == cam.transform) d = go.transform.localPosition.z;
                else d = Vector3.Distance(cam.transform.position, go.transform.position);

                if (d < bestCamDist)
                {
                    bestCamDist = d;
                    closestCameraIndex = i;
                }
            }
        }

        // show only the nearest camera-parented label, hide the rest
        for (int i = 0; i < spawnedARObjects.Count; i++)
        {
            var go = spawnedARObjects[i];
            if (go == null || i >= spawnedARData.Count) continue;

            var data = spawnedARData[i];
            float dist = HaversineDistance(userLat, userLon, data.lat, data.lon);
            int distMeters = Mathf.RoundToInt(dist);

            float bearing = CalculateBearing(userLat, userLon, data.lat, data.lon);
            float rel = NormalizeAngle(bearing - userHeading);
            string dir;
            float absRel = Mathf.Abs(rel);
            if (absRel <= 25f) dir = "ahead";
            else if (absRel <= 110f) dir = (rel > 0f) ? "on right" : "on left";
            else dir = "behind";

            string labelText = $"{data.name} — {distMeters} m {dir}";

            var marker = go.GetComponentInChildren<ARMarker>();
            var tag = go.GetComponent<CameraParentedTag>();

            // When forceScreenPlacement is enabled we want labels visible regardless of distance.
            // But only nearest camera-parented should be visible when parented to camera.
            bool shouldShow;
            if (tag != null && tag.isCameraParented)
            {
                shouldShow = (i == closestCameraIndex);
            }
            else
            {
                shouldShow = forceScreenPlacement ? true : (dist <= 100f && distMeters > 0);
            }

            if (marker != null)
            {
                marker.HideLabel(!shouldShow);
                if (shouldShow)
                {
                    marker.SetDistanceText($"{data.name} — {distMeters} m {dir}");
                    sb.AppendLine($"{data.name} — {distMeters} m {dir}");
                    if (debugLogs) Debug.Log($"[AR VISIBLE] {labelText}");
                }
            }
            else
            {
                if (shouldShow)
                {
                    sb.AppendLine(labelText);
                }
            }
        }

        if (arObjectsDistanceText != null)
            arObjectsDistanceText.text = sb.ToString();
    }
    #endregion

    // Call to change icon of a single spawned AR object by index (0-based)
    public bool UpdateSpawnedIconByIndex(int index, Sprite newIcon)
    {
        if (index < 0 || index >= spawnedARObjects.Count) return false;
        var go = spawnedARObjects[index];
        if (go == null) return false;
        var marker = go.GetComponentInChildren<ARMarker>();
        if (marker != null)
        {
            marker.SetIcon(newIcon);
            // also update stored data if you need to persist
            if (index < spawnedARData.Count) spawnedARData[index].icon = newIcon;
            return true;
        }
        return false;
    }

    // Call to change icon of a single spawned AR object by its configured name
    public bool UpdateSpawnedIconByName(string name, Sprite newIcon)
    {
        for (int i = 0; i < spawnedARObjects.Count; i++)
        {
            if (spawnedARData.Count > i && spawnedARData[i] != null && spawnedARData[i].name == name)
            {
                return UpdateSpawnedIconByIndex(i, newIcon);
            }
        }
        return false;
    }

    // Update icons for all spawned objects with a parallel list of Sprites.
    // The sprites list may be shorter; only update up to min length.
    public void UpdateAllSpawnedIcons(List<Sprite> sprites)
    {
        if (sprites == null) return;
        int limit = Mathf.Min(spawnedARObjects.Count, sprites.Count);
        for (int i = 0; i < limit; i++)
        {
            UpdateSpawnedIconByIndex(i, sprites[i]);
        }
    }

    // Convenience: update spawn entry's configured icon before spawning so next spawn uses it
    public void SetARSpawnPointIcon(int spawnIndex, Sprite newIcon)
    {
        if (arSpawnPoints == null) return;
        if (spawnIndex < 0 || spawnIndex >= arSpawnPoints.Count) return;
        arSpawnPoints[spawnIndex].icon = newIcon;
    }

    #region Utilities: geo, path building, instructions (kept from original)
    // -- Build path using step polylines (preferred) --
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

            if (debugLogs)
                Debug.Log($"Step {s} -> startIdx {startIndexForStep}, endIdx {endIndexForStep}, startAlong {stepStartAlong[s]:F1}, endAlong {stepEndAlong[s]:F1}");
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
                {
                    newSteps[newSteps.Count - 1] = steps[i];
                }
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

        if (debugLogs) Debug.Log($"After dedupe: steps {steps.Count}");
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

    void CheckStepProgress()
    {
        if (currentStepIndex >= steps.Count) return;
        float current = smoothedAlong;
        AlignCurrentStepToAlong();

        float stepEnd = (currentStepIndex < stepEndAlong.Count) ? stepEndAlong[Mathf.Clamp(currentStepIndex, 0, stepEndAlong.Count - 1)] : current;
        float distToStep = Mathf.Max(0f, stepEnd - current);

        if (current >= stepEnd - 1.0f || distToStep <= stepAdvanceMeters)
        {
            currentStepIndex++;
            if (currentStepIndex < steps.Count)
            {
                UpdateBigInstruction();
                UpdateStepsPreview();
            }
            else
            {
                if (bigInstructionText != null) bigInstructionText.text = "Proceed to destination";
                UpdateStepsPreview();
            }
            return;
        }
        UpdateBigInstruction();
    }

    void UpdateThresholdTurnUI()
    {
        if (thresholdTurnText == null) return;
        float currentAlong = smoothedAlong;
        float tol = 0.5f;
        int nextTurnStep = FindNextTurnAfterAlong(currentAlong + tol, tol);
        if (nextTurnStep == -1)
        {
            float distToDest = Mathf.Max(0f, RouteEndAlong() - currentAlong);
            thresholdTurnText.text = $"Go straight - {Mathf.RoundToInt(distToDest)} m";
            return;
        }

        float turnAlong = (nextTurnStep < stepStartAlong.Count) ? stepStartAlong[nextTurnStep] : RouteEndAlong();
        float distToTurn = Mathf.Max(0f, turnAlong - currentAlong);
        string label = GetManeuverLabelForStep(nextTurnStep);

        if (distToTurn <= turnAnnouncementThreshold + 0.001f)
            thresholdTurnText.text = $"{label} - {Mathf.RoundToInt(distToTurn)} m";
        else
            thresholdTurnText.text = $"Go straight - {Mathf.RoundToInt(distToTurn)} m";
    }

    void CheckArrival()
    {
        float dToDest = HaversineDistance(currentLat, currentLon, destination.x, destination.y);
        if (dToDest <= arrivalDistanceMeters)
        {
            if (bigInstructionText != null) bigInstructionText.text = "You have reached your destination 🎉";
            if (stepsPreviewText != null) stepsPreviewText.text = "";
            if (guidanceArrowInstance != null) guidanceArrowInstance.SetActive(false);
        }
    }

    void UpdateUIArrow()
    {
        int segment = FindClosestSegmentIndexOnPath();
        Vector2 lookLatLon = GetLookaheadPointMeters(segment, lookaheadMeters);
        float bearingToLook = CalculateBearing(currentLat, currentLon, lookLatLon.x, lookLatLon.y);
        float desiredZ = -NormalizeAngle(bearingToLook - currentHeading);
        uiCurrentZ = Mathf.LerpAngle(uiCurrentZ, desiredZ, Time.deltaTime * arrowSmoothing);
        if (uiArrow != null) uiArrow.rectTransform.localEulerAngles = new Vector3(0, 0, uiCurrentZ);
    }

    int FindClosestSegmentIndexOnPath()
    {
        if (path.Count < 2) return 0;
        float latRef = currentLat;
        float meanLatRad = latRef * Mathf.Deg2Rad;
        float metersPerDegLat = 110574f;
        float metersPerDegLonRef = 111320f * Mathf.Cos(meanLatRad);
        float best = float.MaxValue;
        int bestI = 0;

        for (int i = 0; i < path.Count - 1; i++)
        {
            Vector2 a = path[i];
            Vector2 b = path[i + 1];
            Vector2 aM = new Vector2((a.y - currentLon) * metersPerDegLonRef, (a.x - currentLat) * metersPerDegLat);
            Vector2 bM = new Vector2((b.y - currentLon) * metersPerDegLonRef, (b.x - currentLat) * metersPerDegLat);
            Vector2 ab = bM - aM;
            Vector2 ao = -aM;
            float ab2 = ab.sqrMagnitude;
            float t = ab2 == 0 ? 0f : Mathf.Clamp01(Vector2.Dot(ao, ab) / ab2);
            Vector2 proj = aM + t * ab;
            float dist = proj.magnitude;
            if (dist < best)
            {
                best = dist;
                bestI = i;
            }
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
        float latRef = currentLat;
        float meanLatRad = latRef * Mathf.Deg2Rad;
        float metersPerDegLat = 110574f;
        float metersPerDegLonRef = 111320f * Mathf.Cos(meanLatRad);
        float bestDist = float.MaxValue;
        float bestAlong = 0f;

        for (int i = 0; i < path.Count - 1; i++)
        {
            Vector2 a = path[i];
            Vector2 b = path[i + 1];
            Vector2 aM = new Vector2((a.y - currentLon) * metersPerDegLonRef, (a.x - currentLat) * metersPerDegLat);
            Vector2 bM = new Vector2((b.y - currentLon) * metersPerDegLonRef, (b.x - currentLat) * metersPerDegLat);
            Vector2 ab = bM - aM;
            Vector2 ao = -aM;
            float ab2 = ab.sqrMagnitude;
            float t = ab2 == 0 ? 0f : Mathf.Clamp01(Vector2.Dot(ao, ab) / ab2);
            Vector2 proj = aM + t * ab;
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

    private string ExtractGuidanceLabelFromText(string text)
    {
        if (string.IsNullOrEmpty(text)) return "Go straight";
        string t = text.ToLowerInvariant();
        if (t.Contains("u-turn") || t.Contains("u turn") || t.Contains("uturn")) return "u-turn";
        if (t.Contains("turn left")) return "turn left";
        if (t.Contains("turn right")) return "turn right";
        if (t.Contains("slight left")) return "slight left";
        if (t.Contains("slight right")) return "slight right";
        if (t.Contains("left")) return "turn left";
        if (t.Contains("right")) return "turn right";
        if (t.Contains("straight") || t.Contains("proceed") || t.Contains("head") || t.Contains("continue")) return "go straight";
        return "go straight";
    }

    // Project a world position into the camera frustum and clamp it inside viewport with a minimum distance.
    private Vector3 ProjectToCameraFrustum(Camera cam, Vector3 worldPos, float minDistance = 3f)
    {
        if (cam == null) return worldPos;

        // convert world pos to viewport space
        Vector3 vp = cam.WorldToViewportPoint(worldPos);

        // If the point is behind the camera (z < 0) push it to forward center
        if (vp.z < 0f)
        {
            vp.z = Mathf.Max(minDistance, forcedDisplayDistance);
            vp.x = 0.5f;
            vp.y = 0.5f;
        }
        else
        {
            // Clamp to a comfortable inset so it doesn't hug edges
            vp.x = Mathf.Clamp(vp.x, 0.05f, 0.95f);
            vp.y = Mathf.Clamp(vp.y, 0.05f, 0.95f);
            vp.z = Mathf.Max(vp.z, minDistance);
        }

        // Convert back to world coords
        return cam.ViewportToWorldPoint(vp);
    }

    // Put this inside your ARNavigation class (replace any older ForceOpaqueMaterials)
    void ForceOpaqueMaterials(GameObject root)
    {
        if (root == null) return;
        Camera cam = Camera.main;

        // --- CANVAS & UI GRAPHICS fix ---
        var canvases = root.GetComponentsInChildren<Canvas>(true);
        foreach (var c in canvases)
        {
            // Force world-space + camera
            c.renderMode = RenderMode.WorldSpace;
            if (cam != null) c.worldCamera = cam;

            // Conservative scale if prefab came from screen-space UI exports
            var rt = c.GetComponent<RectTransform>();
            if (rt != null && rt.localScale == Vector3.one)
                rt.localScale = Vector3.one * 0.01f;

            // CanvasGroup => ensure fully visible
            var cg = c.GetComponent<CanvasGroup>();
            if (cg != null)
            {
                cg.alpha = 1f;
                cg.interactable = true;
                cg.blocksRaycasts = true;
            }

            // Force UI graphics (Images/Text) to opaque + reset custom materials
            var graphics = c.GetComponentsInChildren<UnityEngine.UI.Graphic>(true);
            foreach (var g in graphics)
            {
                try
                {
                    Color col = g.color; col.a = 1f; g.color = col;

                    // For Images: clear custom material so Unity uses default opaque UI shader
                    if (g is UnityEngine.UI.Image img)
                    {
                        // remove any custom material that may be using transparency
                        img.material = null;
                        img.canvasRenderer.SetAlpha(1f);
                    }
                }
                catch { /* non-fatal */ }
            }

            // If Canvas contains TextMeshPro components, handle them too
            var tmpInCanvas = c.GetComponentsInChildren<TMP_Text>(true);
            foreach (var t in tmpInCanvas)
            {
                try
                {
                    Color col = t.color; col.a = 1f; t.color = col;
                    var rend = t.GetComponent<Renderer>();
                    if (rend != null)
                    {
                        foreach (var mat in rend.materials)
                        {
                            if (mat == null) continue;
                            TryMakeMaterialOpaque(mat);
                        }
                    }
                    // Also set the font shared material renderQueue if present
                    if (t.fontSharedMaterial != null) TryMakeMaterialOpaque(t.fontSharedMaterial);
                }
                catch { }
            }

            // Sorting order so world-space canvas isn't unexpectedly behind other transparent objects
            CanvasSorterSet(c, 1000);
        }

        // --- TextMeshPro outside canvas ---
        var tmps = root.GetComponentsInChildren<TMP_Text>(true);
        foreach (var t in tmps)
        {
            try
            {
                Color col = t.color; col.a = 1f; t.color = col;
                var rend = t.GetComponent<Renderer>();
                if (rend != null)
                {
                    foreach (var mat in rend.materials)
                    {
                        if (mat == null) continue;
                        TryMakeMaterialOpaque(mat);
                    }
                }
                if (t.fontSharedMaterial != null) TryMakeMaterialOpaque(t.fontSharedMaterial);
            }
            catch { }
        }

        // --- Mesh renderers (3D geometry that may use transparent shader) ---
        var meshRenderers = root.GetComponentsInChildren<MeshRenderer>(true);
        foreach (var mr in meshRenderers)
        {
            foreach (var mat in mr.materials)
            {
                if (mat == null) continue;
                TryMakeMaterialOpaque(mat);
            }
        }

        // --- SpriteRenderer fix ---
        var spriteRends = root.GetComponentsInChildren<SpriteRenderer>(true);
        foreach (var sr in spriteRends)
        {
            try
            {
                Color c = sr.color; c.a = 1f; sr.color = c;
                if (sr.sharedMaterial != null) TryMakeMaterialOpaque(sr.sharedMaterial);
            }
            catch { }
        }
    }

    // Make a best-effort to convert material to opaque geometry render path
    void TryMakeMaterialOpaque(Material mat)
    {
        if (mat == null) return;
        try
        {
            // URP/Standard style properties - try to set OPAQUE/GEOMETRY
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 0f);        // URP Lit: 0 = Opaque
            if (mat.HasProperty("_Mode")) mat.SetFloat("_Mode", 0f);            // Standard shader: 0 = Opaque
            if (mat.HasProperty("_Cull")) mat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Back);
            if (mat.HasProperty("_ZWrite")) mat.SetInt("_ZWrite", 1);

            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry; // 2000

            // If shader supports keywords for transparency, try to disable them
            try { mat.DisableKeyword("_ALPHATEST_ON"); } catch { }
            try { mat.DisableKeyword("_ALPHABLEND_ON"); } catch { }
            try { mat.DisableKeyword("_ALPHAPREMULTIPLY_ON"); } catch { }
        }
        catch { /* ignore failures for shaders that don't expose these props */ }
    }

    // Helper to set sorting order/override
    void CanvasSorterSet(Canvas c, int order)
    {
        if (c == null) return;
        try
        {
            c.overrideSorting = true;
            c.sortingOrder = order;
        }
        catch { }
    }

    // Diagnostic helper - prints material/shader info for an object tree
    void DumpMaterials(GameObject root, string tag = "")
    {
        if (root == null) { Debug.Log("[DumpMaterials] root null"); return; }
        var rends = root.GetComponentsInChildren<Renderer>(true);
        Debug.Log($"[DumpMaterials] {tag} - found {rends.Length} renderers under {root.name}");
        foreach (var r in rends)
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                string matName = m == null ? "NULL" : m.name;
                string shaderName = (m != null && m.shader != null) ? m.shader.name : "NULL";
                int rq = (m != null) ? m.renderQueue : -1;
                Debug.Log($"  Renderer: {r.gameObject.name} [{r.GetType().Name}] mat#{i}: {matName} shader: {shaderName} rq:{rq}");
            }
        }

        var imgs = root.GetComponentsInChildren<UnityEngine.UI.Image>(true);
        foreach (var img in imgs) Debug.Log($"  Image {img.gameObject.name} color={img.color} mat={(img.material!=null?img.material.name:"NULL")}");

        var tmps = root.GetComponentsInChildren<TMP_Text>(true);
        foreach (var t in tmps) Debug.Log($"  TMP {t.gameObject.name} color={t.color} fontMat={(t.fontSharedMaterial!=null?t.fontSharedMaterial.name:"NULL")} rq={(t.fontSharedMaterial!=null?t.fontSharedMaterial.renderQueue:-1)}");
    }

    private void UpdateGuidanceArrow()
    {
        if (guidanceArrowInstance == null) return;
        if (!guidanceArrowInstance.activeSelf) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        // If parented to camera we keep the arrow at a fixed local position and only adjust local yaw smoothly.
        if (guidanceParentToCamera && guidanceArrowInstance.transform.parent == cam.transform)
        {
            // ensure local position (in case some other code nudged it)
            guidanceArrowInstance.transform.localPosition = Vector3.forward * guidanceArrowDistance + Vector3.up * guidanceArrowHeightOffset;

            // Determine desired local yaw offset (degrees) based on guidanceLabel or bigInstructionText.
            if (string.IsNullOrEmpty(guidanceLabel) && bigInstructionText != null)
                guidanceLabel = ExtractGuidanceLabelFromText(bigInstructionText.text);

            string lab = (guidanceLabel ?? "").ToLowerInvariant();
            float desiredYaw = 0f; // 0 = forward on screen

            if (lab.Contains("left"))
            {
                desiredYaw = lab.Contains("slight") ? -35f : -90f;
            }
            else if (lab.Contains("right"))
            {
                desiredYaw = lab.Contains("slight") ? 35f : 90f;
            }
            else if (lab.Contains("u-turn") || lab.Contains("uturn") || lab.Contains("u turn"))
            {
                desiredYaw = 180f;
            }
            else
            {
                desiredYaw = 0f;
            }

            // Smooth the yaw to avoid quick jumps
            guidanceCurrentYaw = Mathf.SmoothDampAngle(guidanceCurrentYaw, desiredYaw, ref guidanceYawVelocity, Mathf.Max(0.001f, guidanceSmoothingTime));
            guidanceArrowInstance.transform.localEulerAngles = new Vector3(0f, guidanceCurrentYaw, 0f);

            return;
        }

        // --- FALLBACK world-space behavior (existing approach) ---
        Vector3 targetPos = cam.transform.position + cam.transform.forward * guidanceArrowDistance;
        targetPos.y += guidanceArrowHeightOffset;
        guidanceArrowInstance.transform.position = Vector3.Lerp(guidanceArrowInstance.transform.position, targetPos, Time.deltaTime * 8f);

        Quaternion baseRot = Quaternion.LookRotation(cam.transform.forward, Vector3.up);
        if (string.IsNullOrEmpty(guidanceLabel) && bigInstructionText != null)
            guidanceLabel = ExtractGuidanceLabelFromText(bigInstructionText.text);

        string label = (guidanceLabel ?? "").ToLowerInvariant();
        Quaternion desiredRot = baseRot;
        if (label.Contains("left"))
        {
            desiredRot = baseRot * Quaternion.Euler(0f, -90f, 0f);
            if (label.Contains("slight")) desiredRot = baseRot * Quaternion.Euler(0f, -35f, 0f);
        }
        else if (label.Contains("right"))
        {
            desiredRot = baseRot * Quaternion.Euler(0f, 90f, 0f);
            if (label.Contains("slight")) desiredRot = baseRot * Quaternion.Euler(0f, 35f, 0f);
        }
        else if (label.Contains("u-turn") || label.Contains("uturn") || label.Contains("u turn"))
        {
            desiredRot = baseRot * Quaternion.Euler(0f, 180f, 0f);
        }
        else
        {
            desiredRot = baseRot;
        }

        guidanceArrowInstance.transform.rotation = Quaternion.Slerp(guidanceArrowInstance.transform.rotation, desiredRot, Time.deltaTime * guidanceArrowRotateSpeed);
    }

    void UpdateBigInstruction()
    {
        if (bigInstructionText == null) return;
        float currentAlong = smoothedAlong;
        float routeEnd = RouteEndAlong();
        float tol = 0.5f;
        int nextTurnStep = FindNextTurnAfterAlong(currentAlong + tol, tol);
        if (nextTurnStep == -1)
        {
            float distToDest = Mathf.Max(0f, routeEnd - currentAlong);
            bigInstructionText.text = $"Go straight - {Mathf.RoundToInt(distToDest)} m";
            return;
        }
        float turnAlong = (nextTurnStep < stepStartAlong.Count) ? stepStartAlong[nextTurnStep] : routeEnd;
        float distToTurn = Mathf.Max(0f, turnAlong - currentAlong);
        string label = GetManeuverLabelForStep(nextTurnStep);
        if (distToTurn > turnAnnouncementThreshold)
            bigInstructionText.text = $"Go straight - {Mathf.RoundToInt(distToTurn)} m";
        else
            bigInstructionText.text = $"{label} in {Mathf.RoundToInt(distToTurn)} m";
    }

    void UpdateStepsPreview()
    {
        if (stepsPreviewText == null) return;
        float currentAlong = smoothedAlong;
        var segments = BuildRicherPreviewSegments(currentAlong);
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

    bool IsTurnLabel(string label)
    {
        if (string.IsNullOrEmpty(label)) return false;
        string l = label.ToLowerInvariant();
        return l.Contains("turn") || l.Contains("slight") || l.Contains("u-turn") || l.Contains("roundabout");
    }

    int FindNextTurnAfterAlong(float afterAlong, float tol = 0.5f)
    {
        if (stepStartAlong == null || stepStartAlong.Count == 0) return -1;
        for (int s = 0; s < steps.Count; s++)
        {
            if (s >= stepStartAlong.Count) continue;
            float startA = stepStartAlong[s];
            if (startA <= afterAlong) continue;
            string label = GetManeuverLabelForStep(s);
            if (IsTurnLabel(label)) return s;
        }
        return -1;
    }

    int FindNextTurnStepIndex(int fromStep)
    {
        for (int i = Mathf.Max(0, fromStep); i < steps.Count; i++)
        {
            if (IsTurnLabel(GetManeuverLabelForStep(i))) return i;
        }
        return -1;
    }

    float RouteEndAlong() => (cumulative != null && cumulative.Count > 0) ? cumulative[cumulative.Count - 1] : 0f;

    List<KeyValuePair<string, float>> BuildRicherPreviewSegments(float currentAlong)
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


    class TripletEvent { public int stepIndex; public string label; public float along; }

    string GetManeuverLabelForStep(int stepIndex)
    {
        if (stepIndex < 0 || stepIndex >= steps.Count) return "Go straight";
        var st = steps[stepIndex];
        if (!string.IsNullOrEmpty(st.maneuver)) return ManeuverToText(st.maneuver);
        string plain = StripHtmlTags(st.html_instructions).ToLowerInvariant();
        if (!string.IsNullOrEmpty(plain))
        {
            if (plain.StartsWith("head") || plain.StartsWith("continue") || plain.StartsWith("proceed") ||
                plain.StartsWith("keep") || plain.Contains("continue straight") || plain.Contains("go straight"))
                return "Go straight";
        }
        return ManeuverToTextWithAngleFallback(st, stepIndex);
    }

    string ManeuverToText(string maneuver)
    {
        if (string.IsNullOrEmpty(maneuver)) return "Go straight";
        string m = maneuver.ToLower();
        if (m.Contains("left")) return m.Contains("slight") ? "Slight left" : "Turn left";
        if (m.Contains("right")) return m.Contains("slight") ? "Slight right" : "Turn right";
        if (m.Contains("uturn")) return "Make a U-turn";
        if (m.Contains("roundabout")) return "Roundabout";
        return "Go straight";
    }

    string StripHtmlTags(string html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        return Regex.Replace(html, "<.*?>", "").Trim();
    }

// --- Paste these helpers & replacement function into your ARNavigation class ---

// Helper: get a (lat,lon) point along the route at the given 'along' meters (interpolated)
Vector2 SamplePointAtAlong(float targetAlong)
{
    if (path == null || path.Count == 0) return Vector2.zero;
    if (cumulative == null || cumulative.Count == 0) return path[0];

    // clamp
    if (targetAlong <= 0f) return path[0];
    if (targetAlong >= cumulative[cumulative.Count - 1]) return path[path.Count - 1];

    int idx = cumulative.BinarySearch(targetAlong);
    if (idx < 0) idx = ~idx;
    idx = Mathf.Clamp(idx, 1, path.Count - 1);

    float aAlong = cumulative[idx - 1];
    float bAlong = cumulative[idx];
    float t = (bAlong - aAlong) == 0f ? 0f : (targetAlong - aAlong) / (bAlong - aAlong);

    Vector2 a = path[idx - 1];
    Vector2 b = path[idx];
    float lat = Mathf.Lerp(a.x, b.x, t);
    float lon = Mathf.Lerp(a.y, b.y, t);
    return new Vector2(lat, lon);
}

// Replacement: more robust angle-based fallback for textual maneuver
string ManeuverToTextWithAngleFallback(Step step, int stepIndex)
{
    // prefer explicit maneuver if present
    if (!string.IsNullOrEmpty(step.maneuver)) return ManeuverToText(step.maneuver);

    // quick textual checks
    string plain = StripHtmlTags(step.html_instructions).ToLowerInvariant();
    if (!string.IsNullOrEmpty(plain))
    {
        if (plain.StartsWith("head") || plain.StartsWith("continue") || plain.StartsWith("proceed") ||
            plain.StartsWith("keep") || plain.Contains("continue straight") || plain.Contains("go straight"))
            return "Go straight";
    }

    // require path/cumulative to exist and have enough points
    if (path == null || path.Count < 3 || cumulative == null || cumulative.Count < 2 || stepStartAlong == null || stepEndAlong == null)
        return "Go straight";

    // clamp indices safely
    int sIdx = Mathf.Clamp(stepIndex, 0, Mathf.Max(0, stepStartAlong.Count - 1));
    float startAlong = (sIdx < stepStartAlong.Count) ? stepStartAlong[sIdx] : 0f;
    float endAlong = (sIdx < stepEndAlong.Count) ? stepEndAlong[sIdx] : startAlong;
    int nextIdx = (sIdx + 1 < stepEndAlong.Count) ? sIdx + 1 : -1;
    float nextEndAlong = (nextIdx >= 0 && nextIdx < stepEndAlong.Count) ? stepEndAlong[nextIdx] : -1f;

    // If we don't have a following step with a valid along distance, treat as straight
    if (nextEndAlong <= 0f || nextEndAlong <= endAlong + 0.01f)
        return "Go straight";

    // Choose meters offsets for sampling before/after the junction
    float sampleOffset = 6f; // meters before / after; tune as needed
    float minSegmentConsiderMeters = 3f; // ignore very tiny geometry

    // sample a point slightly BEFORE the step end
    float beforeAlong = Mathf.Max(startAlong, endAlong - sampleOffset);
    Vector2 beforePt = SamplePointAtAlong(beforeAlong);

    // sample a point slightly AFTER the step end (on the next step)
    float afterAlong = Mathf.Min(nextEndAlong, endAlong + sampleOffset);
    Vector2 afterPt = SamplePointAtAlong(afterAlong);

    // To compute bearings, we need a short baseline before and after.
    // We'll take small offsets to form two micro-segments for bearing.
    float baseline = 2.0f; // meters baseline used to compute each bearing

    // sample a point further back to form the "before" micro-segment
    float beforeBaselineAlong = Mathf.Max(startAlong, beforeAlong - baseline);
    Vector2 beforeBaselinePt = SamplePointAtAlong(beforeBaselineAlong);

    // sample a point further forward to form the "after" micro-segment
    float afterBaselineAlong = Mathf.Min(nextEndAlong, afterAlong + baseline);
    Vector2 afterBaselinePt = SamplePointAtAlong(afterBaselineAlong);

    // Compute distances to ensure geometry isn't degenerate
    float beforeSegDist = HaversineDistance(beforeBaselinePt.x, beforeBaselinePt.y, beforePt.x, beforePt.y);
    float afterSegDist = HaversineDistance(afterPt.x, afterPt.y, afterBaselinePt.x, afterBaselinePt.y);

    if (beforeSegDist < minSegmentConsiderMeters || afterSegDist < minSegmentConsiderMeters)
    {
        // segments too small to make a reliable angle decision
        return "Go straight";
    }

    // compute bearings (bearing uses lat, lon order as in your CalculateBearing)
    float bearingBefore = CalculateBearing(beforeBaselinePt.x, beforeBaselinePt.y, beforePt.x, beforePt.y);
    float bearingAfter = CalculateBearing(afterPt.x, afterPt.y, afterBaselinePt.x, afterBaselinePt.y);

    float angle = Mathf.DeltaAngle(bearingBefore, bearingAfter);
    float absAngle = Mathf.Abs(angle);

    // Debugging helpful log - enable debugLogs to print this
    if (debugLogs)
    {
        Debug.Log($"ManeuverFallback step {stepIndex}: bearingBefore={bearingBefore:F1} bearingAfter={bearingAfter:F1} angle={angle:F1} abs={absAngle:F1} startAlong={startAlong:F1} endAlong={endAlong:F1} nextEnd={nextEndAlong:F1}");
    }

    // Decide classification based on thresholds (minorTurnAngle, majorTurnAngle)
    if (absAngle >= majorTurnAngle)
        return angle > 0f ? "Turn right" : "Turn left";
    if (absAngle >= minorTurnAngle)
        return angle > 0f ? "Slight right" : "Slight left";

    // otherwise treat as straight
    return "Go straight";
}


    void SpawnPathArrows()
    {
        ClearPathArrows();
        if (pathArrowPrefab == null) return;
        for (int i = 1; i < path.Count; i++)
        {
            Vector2 a = path[i - 1], b = path[i];
            Vector3 start = LatLonToUnity(a);
            Vector3 end = LatLonToUnity(b);

            // transform to world coords if originTransform exists
            if (originTransform != null)
            {
                start = originTransform.TransformPoint(start);
                end = originTransform.TransformPoint(end);
            }

            Vector3 dir = end - start;
            if (dir.magnitude < 0.05f) continue;
            var arrow = Instantiate(pathArrowPrefab, end, Quaternion.LookRotation(dir.normalized, Vector3.up));
            pathArrows.Add(arrow);
        }
    }

    void ClearPathArrows()
    {
        foreach (var g in pathArrows) if (g != null) Destroy(g);
        pathArrows.Clear();
    }

    float NormalizeAngle(float angle)
    {
        angle %= 360f;
        if (angle > 180f) angle -= 360f;
        if (angle < -180f) angle += 360f;
        return angle;
    }

    float HaversineDistance(float lat1, float lon1, float lat2, float lon2)
    {
        const float R = 6371000f;
        float dLat = (lat2 - lat1) * Mathf.Deg2Rad;
        float dLon = (lon2 - lon1) * Mathf.Deg2Rad;
        float a = Mathf.Sin(dLat / 2) * Mathf.Sin(dLat / 2) +
                  Mathf.Cos(lat1 * Mathf.Deg2Rad) * Mathf.Cos(lat2 * Mathf.Deg2Rad) *
                  Mathf.Sin(dLon / 2) * Mathf.Sin(dLon / 2);
        float c = 2 * Mathf.Atan2(Mathf.Sqrt(a), Mathf.Sqrt(1 - a));
        return R * c;
    }

    float CalculateBearing(float lat1, float lon1, float lat2, float lon2)
    {
        float dLon = (lon2 - lon1) * Mathf.Deg2Rad;
        float lat1Rad = lat1 * Mathf.Deg2Rad;
        float lat2Rad = lat2 * Mathf.Deg2Rad;
        float y = Mathf.Sin(dLon) * Mathf.Cos(lat2Rad);
        float x = Mathf.Cos(lat1Rad) * Mathf.Sin(lat2Rad) -
                  Mathf.Sin(lat1Rad) * Mathf.Cos(lat2Rad) * Mathf.Cos(dLon);
        return (Mathf.Atan2(y, x) * Mathf.Rad2Deg + 360f) % 360f;
    }

    Vector3 LatLonToUnity(Vector2 latlon)
    {
        // Use origin if set, otherwise fall back to current GPS reading (less stable)
        float latRef = originSet ? originLat : currentLat;
        float lonRef = originSet ? originLon : currentLon;
        float meanLatRad = latRef * Mathf.Deg2Rad;
        float metersPerDegLat = 110574f;
        float metersPerDegLon = 111320f * Mathf.Cos(meanLatRad);
        float east = (latlon.y - lonRef) * metersPerDegLon;
        float north = (latlon.x - latRef) * metersPerDegLat;

        // Note: returns local meters vector (east, 0, north) relative to chosen origin.
        // If an originTransform exists, caller should call originTransform.TransformPoint on the result
        // to get scene-world coordinates that match the runtime origin.
        return new Vector3(east, 0f, north);
    }

    List<Vector2> DecodePolyline(string encoded)
    {
        var poly = new List<Vector2>();
        if (string.IsNullOrEmpty(encoded)) return poly;
        int index = 0, len = encoded.Length;
        int lat = 0, lng = 0;
        while (index < len)
        {
            int b, shift = 0, result = 0;
            do
            {
                b = encoded[index++] - 63;
                result |= (b & 0x1f) << shift;
                shift += 5;
            } while (b >= 0x20);
            int dlat = ((result & 1) != 0) ? ~(result >> 1) : (result >> 1);
            lat += dlat;

            shift = 0;
            result = 0;
            do
            {
                b = encoded[index++] - 63;
                result |= (b & 0x1f) << shift;
                shift += 5;
            } while (b >= 0x20);
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
            if (response == null || response.routes == null || response.routes.Length == 0 || response.routes[0].legs == null || response.routes[0].legs.Length == 0) return new List<Step>();
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

    #endregion

    #region Small utilities & debug
    void RefreshCurrentLocationFromService()
    {
        if (Input.location.status == LocationServiceStatus.Running)
        {
            currentLat = Input.location.lastData.latitude;
            currentLon = Input.location.lastData.longitude;
            currentAlt = Input.location.lastData.altitude;
        }
        else
        {
            if (debugLogs) Debug.LogWarning("RefreshCurrentLocationFromService: location service not running.");
        }
    }

      void FaceObjectToCamera(GameObject obj, Camera cam, bool applyFlipIfConfigured = true)
    {
        if (obj == null || cam == null) return;

        // If object is camera-parented, it's often already aligned to camera.
        var tag = obj.GetComponent<CameraParentedTag>();
        if (tag != null && tag.isCameraParented)
        {
            // Keep local rotation locked to identity so it faces camera naturally,
            // but still allow a flip correction if the model is backwards.
            if (applyFlipIfConfigured && invertSpawnedPrefabFacing)
            {
                // flip the local Y by 180 degrees so front faces camera
                obj.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            }
            else
            {
                obj.transform.localRotation = Quaternion.identity;
            }
            return;
        }

        // Non camera-parented: point the object's forward to the camera
        Vector3 dir = cam.transform.position - obj.transform.position; // vector from object -> camera
        // Optionally keep the billboard upright
        dir.y = 0f;
        if (dir.sqrMagnitude <= 0.0001f) return;
        Quaternion rot = Quaternion.LookRotation(dir, Vector3.up);

        // Apply rotation
        obj.transform.rotation = rot;

        // If model's front faces -Z (opposite), flip by 180 degrees around Y
        if (applyFlipIfConfigured && invertSpawnedPrefabFacing)
        {
            obj.transform.rotation = obj.transform.rotation * Quaternion.Euler(0f, 180f, 0f);
        }
    }

    #endregion

    // Small tag component used to mark camera-parented spawns
    private class CameraParentedTag : MonoBehaviour
    {
        public bool isCameraParented = false;
    }
}
