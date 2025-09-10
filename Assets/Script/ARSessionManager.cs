using UnityEngine;
using UnityEngine.XR.ARFoundation;

public class ARSessionManager : MonoBehaviour
{
    public static ARSessionManager Instance { get; private set; }
    private ARSession arSession;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject); // Ensure singleton
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        arSession = GetComponent<ARSession>();
        if (arSession == null)
        {
            arSession = gameObject.AddComponent<ARSession>();
        }
    }

    // Method to reset or reinitialize AR session if needed
    public void ResetARSession()
    {
        if (arSession != null)
        {
            arSession.Reset();
        }
    }
}