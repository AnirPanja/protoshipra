using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

public class ARProximitySpawner : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ARNavigation nav;          // Assign your ARNavigation in Inspector
    [SerializeField] private GameObject cubePrefab;     // Assign your Cube prefab (any scale; 0.8 is fine)

    [Header("Proximity (meters)")]
    [Tooltip("Spawn when user is this close or closer.")]
    [SerializeField] private float spawnWithinMeters = 60f;
    [Tooltip("Despawn when user is this far or farther (use > spawnWithinMeters).")]
    [SerializeField] private float despawnBeyondMeters = 120f;

    [Header("Options")]
    [Tooltip("Add vertical offset (in meters) on top of ARNavigation's own placement.")]
    [SerializeField] private float heightOffsetMeters = 0f;
    [SerializeField] private bool includeDestinationToo = false;
    [SerializeField] private bool onlyForEnabledPoints = true;
    [SerializeField] private bool clearExistingBeforeSpawnAll = false;
    [SerializeField] private bool debugLogs = true;

    // track spawned cubes by spawn-point index; -1 reserved for destination
    private readonly Dictionary<int, GameObject> _spawned = new Dictionary<int, GameObject>();

    void Reset()
    {
        nav = FindObjectOfType<ARNavigation>();
    }

    void Update()
    {
        if (nav == null || cubePrefab == null) return;

        // Need GPS running to know how close we are
        if (Input.location.status != LocationServiceStatus.Running) return;

        // user location (double precision from Unity LocationService)
        double userLat = Input.location.lastData.latitude;
        double userLon = Input.location.lastData.longitude;

        // Guard: keep radii sane
        if (despawnBeyondMeters <= spawnWithinMeters) despawnBeyondMeters = spawnWithinMeters + 10f;

        int count = nav.GetARSpawnPointCount();

        for (int i = 0; i < count; i++)
        {
            // FIXED: Get individual out parameters
            if (!nav.TryGetARSpawnPointLatLon(i, out double lat, out double lon, out bool enabled))
                continue;

            if (onlyForEnabledPoints && !enabled)
            {
                DespawnIndexIfExists(i);
                continue;
            }

            // Haversine with double math
            float dMeters = HaversineMeters(userLat, userLon, lat, lon);

            // spawn when near
            if (dMeters <= spawnWithinMeters)
            {
                if (!_spawned.ContainsKey(i) || _spawned[i] == null)
                {
                    // SpawnARObjectAtLatLon expects double lat, double lon
                    var go = nav.SpawnARObjectAtLatLon(
                        cubePrefab,
                        lat,
                        lon,
                        $"Cube_{i}",
                        heightOffsetMeters,
                        null,
                        null
                    );
                    if (go != null)
                    {
                        _spawned[i] = go;
                        if (debugLogs) Debug.Log($"[Proximity] Spawned Cube_{i}  dist={dMeters:F1}m");
                    }
                }
            }
            // despawn when far
            else if (dMeters >= despawnBeyondMeters)
            {
                DespawnIndexIfExists(i);
            }
        }

        // Optional: handle destination too
        if (includeDestinationToo && nav.TryGetDestinationLatLon(out double destLat, out double destLon))
        {
            const int DEST_KEY = -1;

            float dMeters = HaversineMeters(userLat, userLon, destLat, destLon);

            if (dMeters <= spawnWithinMeters)
            {
                if (!_spawned.ContainsKey(DEST_KEY) || _spawned[DEST_KEY] == null)
                {
                    var go = nav.SpawnARObjectAtLatLon(
                        cubePrefab,
                        destLat,
                        destLon,
                        "Cube_Destination",
                        heightOffsetMeters,
                        null,
                        null
                    );
                    if (go != null)
                    {
                        _spawned[DEST_KEY] = go;
                        if (debugLogs) Debug.Log($"[Proximity] Spawned Destination cube  dist={dMeters:F1}m");
                    }
                }
            }
            else if (dMeters >= despawnBeyondMeters)
            {
                DespawnIndexIfExists(DEST_KEY);
            }
        }
    }

    private void DespawnIndexIfExists(int key)
    {
        if (_spawned.TryGetValue(key, out var go) && go != null)
        {
            Destroy(go);
            if (debugLogs) Debug.Log($"[Proximity] Despawn index {key}");
        }
        _spawned.Remove(key);
    }

    /// <summary>
    /// Optional: hook this to a UI button if you want to spawn ALL cubes at once.
    /// It uses ARNavigation placement, so you still get stable anchors/altitude logic.
    /// </summary>
    public void OnSpawnAllButton()
    {
        if (nav == null || cubePrefab == null)
        {
            Debug.LogError("[Proximity] Assign ARNavigation and cubePrefab first.");
            return;
        }

        if (clearExistingBeforeSpawnAll)
        {
            // Clear both ARNavigation-registered objects and our local map
            nav.ClearSpawnedARObjects();
            foreach (var kv in _spawned)
                if (kv.Value) Destroy(kv.Value);
            _spawned.Clear();
        }

        int count = nav.GetARSpawnPointCount();
        int spawned = 0;

        for (int i = 0; i < count; i++)
        {
            // FIXED: Get individual out parameters
            if (!nav.TryGetARSpawnPointLatLon(i, out double lat, out double lon, out bool enabled))
                continue;
            if (onlyForEnabledPoints && !enabled)
                continue;

            var go = nav.SpawnARObjectAtLatLon(
                cubePrefab,
                lat,
                lon,
                $"Cube_{i}",
                heightOffsetMeters,
                null,
                null
            );
            if (go != null)
            {
                _spawned[i] = go;
                spawned++;
            }
        }

        if (includeDestinationToo && nav.TryGetDestinationLatLon(out double destLat, out double destLon))
        {
            const int DEST_KEY = -1;
            var go = nav.SpawnARObjectAtLatLon(
                cubePrefab,
                destLat,
                destLon,
                "Cube_Destination",
                heightOffsetMeters,
                null,
                null
            );
            if (go != null)
            {
                _spawned[DEST_KEY] = go;
                spawned++;
            }
        }

        Debug.Log($"[Proximity] SpawnAllButton placed {spawned} object(s).");
    }

    // ---- Geo helpers ----
    private static float HaversineMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371000.0; // meters
        double dLat = Deg2Rad(lat2 - lat1);
        double dLon = Deg2Rad(lon2 - lon1);
        double a =
            System.Math.Sin(dLat / 2) * System.Math.Sin(dLat / 2) +
            System.Math.Cos(Deg2Rad(lat1)) * System.Math.Cos(Deg2Rad(lat2)) *
            System.Math.Sin(dLon / 2) * System.Math.Sin(dLon / 2);
        double c = 2 * System.Math.Atan2(System.Math.Sqrt(a), System.Math.Sqrt(1.0 - a));
        return (float)(R * c);
    }

    private static double Deg2Rad(double deg) => deg * System.Math.PI / 180.0;
}