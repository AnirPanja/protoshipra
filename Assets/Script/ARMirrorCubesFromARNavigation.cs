using UnityEngine;

public class ARMirrorCubesFromARNavigation : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ARNavigation nav;       // Drag your ARNavigation component here
    [SerializeField] private GameObject cubePrefab;  // A small cube prefab (e.g., scale 0.1, 0.1, 0.1)

    [Header("Options")]
    [SerializeField] private float heightOffsetMeters = 0.0f; // extra Y if you want cube a bit above ground
    [SerializeField] private bool includeDestinationToo = false; // also spawn a cube at nav destination
    [SerializeField] private bool onlyForEnabledPoints = true;   // match banners' enabled flag

    /// <summary>
    /// Wire this to your UI Button (OnClick).
    /// Spawns one cube per AR spawn point configured in ARNavigation.
    /// </summary>
    public void SpawnCubesForAllARPoints()
    {
        if (nav == null || cubePrefab == null)
        {
            Debug.LogError("Assign ARNavigation and cubePrefab.");
            return;
        }

        int count = nav.GetARSpawnPointCount();
        if (count <= 0)
        {
            Debug.LogWarning("No AR spawn points configured in ARNavigation.");
        }

        int spawned = 0;

        for (int i = 0; i < count; i++)
        {
            if (!nav.TryGetARSpawnPointLatLon(i, out var latlon, out var enabled))
                continue;

            if (onlyForEnabledPoints && !enabled)
                continue;

            // Use ARNavigation's own placement/anchoring path
            var go = nav.SpawnARObjectAtLatLon(
                cubePrefab,
                latlon,
                $"Cube_{i}",
                heightOffsetMeters,
                null,     // icon not needed
                null      // material not needed
            );

            if (go != null) spawned++;
        }

        if (includeDestinationToo && nav.TryGetDestinationLatLon(out var dest))
        {
            nav.SpawnARObjectAtLatLon(cubePrefab, dest, "Cube_Destination", heightOffsetMeters, null, null);
            spawned++;
        }

        Debug.Log($"SpawnCubesForAllARPoints: spawned {spawned} cube(s).");
    }
}
