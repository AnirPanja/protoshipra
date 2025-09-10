using UnityEngine;
using UnityEngine.Android; // For permissions
using UnityEngine.SceneManagement;
public class LocationChecker : MonoBehaviour {
    public void CheckAndProceed(string sceneName) {
        if (!Input.location.isEnabledByUser) {
            // Request permission (Android/iOS system popup)
            Permission.RequestUserPermission(Permission.FineLocation);
            // Or show custom dialog, then return if denied
            return;
        }
        StartLocationService(() => SceneManager.LoadScene(sceneName));
    }
    void StartLocationService(System.Action onSuccess) {
        Input.location.Start();
        if (Input.location.status == LocationServiceStatus.Running) {
            // Fetch lat/long: Input.location.lastData.latitude/longitude
            onSuccess();
        }
    }
}