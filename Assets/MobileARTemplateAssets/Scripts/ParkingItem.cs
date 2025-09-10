using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class ParkingItem : MonoBehaviour
{
    public TMP_Text heading;
    public TMP_Text content;
    public Button directionButton; // optional

    public void Set(string title, string address, string status = null, string lat = null, string lon = null)
    {
        if (heading != null) heading.text = title ?? "";
        if (content != null) content.text = address ?? "";

        if (directionButton != null)
        {
            directionButton.onClick.RemoveAllListeners();
            directionButton.onClick.AddListener(() =>
            {
                Debug.Log($"Direction clicked for {title} ({lat},{lon})");
                // Hook your navigation call here
            });
        }
    }
}
