using UnityEngine;
using UnityEngine.SceneManagement;

public class NavigateButton : MonoBehaviour
{
    [SerializeField]
    private string targetSceneName; // Set in Inspector (e.g., "SOSScene", "ARNavigation")

    public void OnNavigateButtonPressed()
    {
        // Check if targetSceneName is set
        if (string.IsNullOrEmpty(targetSceneName))
        {
            Debug.LogError($"NavigateButton on {gameObject.name}: Target scene name is not set!");
            return;
        }

        // Log the scene being attempted
        Debug.Log($"NavigateButton: Attempting to load scene '{targetSceneName}'");

        // Check if the scene exists in Build Settings
        bool sceneExists = false;
        for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
        {
            string scenePath = SceneUtility.GetScenePathByBuildIndex(i);
            string sceneName = System.IO.Path.GetFileNameWithoutExtension(scenePath);
            if (sceneName == targetSceneName)
            {
                sceneExists = true;
                break;
            }
        }

        if (sceneExists)
        {
            // If navigating to ARNavigation, reset AR session
            if (targetSceneName == "ARNavigation" && ARSessionManager.Instance != null)
            {
                Debug.Log("NavigateButton: Resetting AR session for ARNavigation");
                ARSessionManager.Instance.ResetARSession();
            }
            else if (targetSceneName == "ARNavigation" && ARSessionManager.Instance == null)
            {
                Debug.LogWarning("NavigateButton: ARSessionManager instance is null, proceeding without AR reset");
            }

            // Load the scene
            Debug.Log($"NavigateButton: Loading scene '{targetSceneName}'");
            SceneManager.LoadScene(targetSceneName);
        }
        else
        {
            Debug.LogError($"NavigateButton: Scene '{targetSceneName}' not found in Build Settings!");
        }
    }
}