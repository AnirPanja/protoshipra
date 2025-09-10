

using UnityEngine;
using UnityEngine.SceneManagement;

public class SplashController : MonoBehaviour
{
    void Start()
    {
        // Only load the main menu if the current scene is the splash screen
        if (SceneManager.GetActiveScene().name == "Splash")
        {
            Invoke("LoadMainMenu", 2f);
        }
    }

    void LoadMainMenu()
    {
        SceneManager.LoadScene("MainMenu");
    }
}