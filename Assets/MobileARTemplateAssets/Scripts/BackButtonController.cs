using UnityEngine;
using UnityEngine.SceneManagement;

public class BackButtonController : MonoBehaviour
{
    private const string MAIN_MENU_SCENE = "MainMenu";

    private float lastBackPressTime = 0f;
    private const float DOUBLE_TAP_TIME = 1.5f;

    private void Update()
    {
#if UNITY_ANDROID
        // Android hardware back button (Escape key in new Input System)
        if (UnityEngine.InputSystem.Keyboard.current != null &&
            UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            HandleBackPress();
        }
#endif
    }

    private void HandleBackPress()
    {
        string currentScene = SceneManager.GetActiveScene().name;

        if (currentScene == MAIN_MENU_SCENE)
        {
            // ✅ Only MainMenu requires double-tap to exit
            if (Time.time - lastBackPressTime < DOUBLE_TAP_TIME)
            {
                Application.Quit();
            }
            else
            {
                Debug.Log("Press back again to exit");
                lastBackPressTime = Time.time;
            }
        }
        else
        {
            // ✅ Any other scene: single press → back to MainMenu
            lastBackPressTime = 0f; // reset so MainMenu logic is clean
            GoToMainMenu();
        }
    }

    public void OnBackButtonPressed() // For Unity UI button
    {
        HandleBackPress();
    }

    private void GoToMainMenu()
    {
        // ✅ Clean up DontDestroyOnLoad objects (prevents freezing)
        var allRoots = SceneManager.GetActiveScene().GetRootGameObjects();
        foreach (var root in allRoots)
        {
            if (root != null)
                Destroy(root);
        }

        // ✅ If you are using ARFoundation, reset session before unloading
        var arSession = FindObjectOfType<UnityEngine.XR.ARFoundation.ARSession>();
        if (arSession != null)
        {
            arSession.Reset();
        }

        // ✅ Async load prevents UI freeze
        SceneManager.LoadSceneAsync(MAIN_MENU_SCENE, LoadSceneMode.Single);
    }
}
