using UnityEngine;
using UnityEngine.UI;

public class CanvasSwitcher : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject mainCanvas;
    [SerializeField] private GameObject navigationCanvas;

    [Header("Script References")]
    [SerializeField] private ARNavigation arNavigation; // Assign in Inspector

    [Header("Buttons")]
    [SerializeField] private Button openNavigationButton;
    [SerializeField] private Button backButton;

    private void Start()
    {
        // Make sure we start with the main canvas visible
        ShowMainCanvas();

        if (openNavigationButton != null)
            openNavigationButton.onClick.AddListener(ShowNavigationCanvas);

        if (backButton != null)
            backButton.onClick.AddListener(ShowMainCanvas);
    }

    private void ShowMainCanvas()
    {
        if (mainCanvas != null) mainCanvas.SetActive(true);
        if (navigationCanvas != null) navigationCanvas.SetActive(false);
    }

    public void ShowNavigationCanvas()
    {
        if (mainCanvas != null) mainCanvas.SetActive(false);
        if (navigationCanvas != null) navigationCanvas.SetActive(true);

        if (arNavigation != null)
        {
            // Safe defaults (replace later with real source & target)
            Vector2 defaultSource = Vector2.zero;
            Vector2 defaultTarget = Vector2.zero;

            arNavigation.StartNavigation(defaultSource, defaultTarget);
        }
        else
        {
            Debug.LogWarning("ARNavigation is not assigned in CanvasSwitcher.");
        }
    }
}
