using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PopupManager : MonoBehaviour
{
    public GameObject popupPanel;  // assign in Inspector
    public TMP_Text messageText;   // assign text field inside popup
    public Button okButton;        // assign OK button

    void Start()
    {
        if (popupPanel != null)
            popupPanel.SetActive(false);

        if (okButton != null)
            okButton.onClick.AddListener(ClosePopup);
    }

    public void ShowPopup(string message)
    {
        if (popupPanel != null && messageText != null)
        {
            messageText.text = message;
            popupPanel.SetActive(true);
        }
    }

    public void ClosePopup()
    {
        if (popupPanel != null)
            popupPanel.SetActive(false);
    }
}
