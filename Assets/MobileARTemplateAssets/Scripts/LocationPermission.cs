using UnityEngine;
using UnityEngine.Android;

public class LocationPermission : MonoBehaviour
{
    void Start()
    {
#if UNITY_ANDROID
        // Ask for location permission if not granted
        if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
        {
            Permission.RequestUserPermission(Permission.FineLocation);
        }
#endif
    }
}
