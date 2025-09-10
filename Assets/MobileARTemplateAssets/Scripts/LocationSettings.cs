using UnityEngine;

public class LocationSettings : MonoBehaviour
{
#if UNITY_ANDROID && !UNITY_EDITOR
    private static AndroidJavaClass unityPlayer;
    private static AndroidJavaObject currentActivity;
    private static AndroidJavaClass locationPlugin;

    static LocationSettings()
    {
        unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
        currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
        locationPlugin = new AndroidJavaClass("com.triosoft.locationplugin.LocationPlugin");
    }

    public static void OpenLocationSettings()
    {
        locationPlugin.CallStatic("OpenLocationSettings", currentActivity);
    }
#else
    public static void OpenLocationSettings()
    {
        Debug.Log("Location settings only works on Android device.");
    }
#endif
}
