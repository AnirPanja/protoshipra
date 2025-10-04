using UnityEngine;
using UnityEngine.XR.ARFoundation;
using System;
using System.Reflection;

public class ARGeoCubeSpawner_FromARNavigation : MonoBehaviour
{
    [Header("Refs (assign in Inspector)")]
    [SerializeField] private ARNavigation nav;          // Drag your ARNavigation component here
    [SerializeField] private ARAnchorManager anchorMgr; // Drag from XR/AR Origin (has ARAnchorManager)
    [SerializeField] private ARSessionOrigin arOrigin;  // Drag your AR Session Origin (XR Origin (AR))
    [SerializeField] private GameObject cubePrefab;     // Small cube (e.g., 0.1m scale)

    [Header("Which lat/lon to use from ARNavigation")]
    [Tooltip("If >= 0, uses arSpawnPoints[index]; otherwise uses Destination.")]
    [SerializeField] private int spawnPointIndex = -1;

    [Header("Placement")]
    [SerializeField] private float heightOffsetMeters = 0.0f;   // extra height if you want the cube a bit higher
    [SerializeField] private bool allowMultipleSpawns = false;   // otherwise only first press works

    private bool _spawnedOnce;

    private void Reset()
    {
        arOrigin = FindObjectOfType<ARSessionOrigin>();
        anchorMgr = FindObjectOfType<ARAnchorManager>();
        nav = FindObjectOfType<ARNavigation>();
    }

    /// <summary>
    /// Wire this to your UI Button (OnClick) to spawn the cube.
    /// </summary>
    public void SpawnCubeFromNavigation()
    {
        if (_spawnedOnce && !allowMultipleSpawns) return;

        if (nav == null || anchorMgr == null || arOrigin == null || cubePrefab == null)
        {
            Debug.LogError("Assign nav, anchorMgr, arOrigin, and cubePrefab in the inspector.");
            return;
        }

        // 1) Get the lat/lon we want from ARNavigation
        if (!nav.TryGetLatLonFromNavigation(out var latlon, spawnPointIndex))
        {
            Debug.LogWarning("No valid lat/lon available from ARNavigation (Destination and arSpawnPoints empty?).");
            return;
        }

        // 2) Convert to world using ARNavigation's own helper (keeps math identical)
        Vector3 worldPos = nav.LatLonToWorld(latlon, heightOffsetMeters);
        Pose pose = new Pose(worldPos, Quaternion.identity);

        // 3) Create an anchor at that pose (ARF 5.x TryAddAnchor or fallback)
        ARAnchor anchor = TryCreateAnchor(anchorMgr, pose, arOrigin.transform);
        if (anchor == null)
        {
            Debug.LogWarning("Failed to create ARAnchor at target pose.");
            return;
        }

        // 4) Instantiate cube under the anchor so it stays put
        var cube = Instantiate(cubePrefab, anchor.transform);
        cube.transform.localPosition = Vector3.zero;
        cube.transform.localRotation = Quaternion.identity;

        var rb = cube.GetComponent<Rigidbody>();
        if (rb) rb.isKinematic = true;

        _spawnedOnce = true;
        Debug.Log($"Spawned cube at lat={latlon.x:F6}, lon={latlon.y:F6}, world={worldPos}");
    }

    // --- helpers ---

    private ARAnchor TryCreateAnchor(ARAnchorManager mgr, Pose pose, Transform fallbackParent)
    {
        if (mgr != null)
        {
            // Prefer ARFoundation 5.x: TryAddAnchor(Pose)
            try
            {
                MethodInfo tryAdd = typeof(ARAnchorManager).GetMethod("TryAddAnchor", new Type[] { typeof(Pose) });
                if (tryAdd != null)
                {
                    var result = tryAdd.Invoke(mgr, new object[] { pose });
                    if (result is ARAnchor a5 && a5 != null) return a5;
                }
            }
            catch { /* ignore */ }

            // Older ARF sometimes had AddAnchor(Pose)
            try
            {
                MethodInfo add = typeof(ARAnchorManager).GetMethod("AddAnchor", new Type[] { typeof(Pose) });
                if (add != null)
                {
                    var result = add.Invoke(mgr, new object[] { pose });
                    if (result is ARAnchor a4 && a4 != null) return a4;
                }
            }
            catch { /* ignore */ }
        }

        // Fallback: create a GO, parent under trackables/origin, then AddComponent<ARAnchor>()
        var anchorGO = new GameObject("GeoAnchor_Fallback");
        anchorGO.transform.position = pose.position;
        anchorGO.transform.rotation = pose.rotation;

        // Parent under trackables if available to keep coordinate frames consistent
        if (arOrigin != null)
        {
#if UNITY_2020_3_OR_NEWER
            var parent = arOrigin.trackablesParent != null ? arOrigin.trackablesParent : arOrigin.transform;
            anchorGO.transform.SetParent(parent, true);
#else
            anchorGO.transform.SetParent(arOrigin.transform, true);
#endif
        }
        else if (fallbackParent != null)
        {
            anchorGO.transform.SetParent(fallbackParent, true);
        }

        return anchorGO.AddComponent<ARAnchor>();
    }
}
