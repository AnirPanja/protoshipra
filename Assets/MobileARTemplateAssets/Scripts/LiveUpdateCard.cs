using UnityEngine;
using TMPro;

/// <summary>
/// Small helper to populate a card prefab. Attach to card prefab and wire the TMP fields in Inspector.
/// </summary>
public class LiveUpdateCard : MonoBehaviour
{
    public TextMeshProUGUI entryDateText;
    // public TextMeshProUGUI lostOrFoundText;
    public TextMeshProUGUI descriptionText;

    public void Setup(string entryDate, string lostOrFound, string description)
    {
        if (entryDateText != null) entryDateText.text = entryDate ?? "";
        // if (lostOrFoundText != null) lostOrFoundText.text = lostOrFound ?? "";
        if (descriptionText != null) descriptionText.text = description ?? "";
    }
}
