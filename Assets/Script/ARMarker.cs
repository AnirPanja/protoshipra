using UnityEngine;
using UnityEngine.UI;
using TMPro;

[DisallowMultipleComponent]
public class ARMarker : MonoBehaviour
{
    [Header("UI refs (assign in prefab)")]
    public TMP_Text labelText;    // the label text in the prefab (optional)
    public Image iconImage;       // optional, can be null (the Figma image goes here)

    /// <summary>Populate marker visuals after instantiation.</summary>
    public void SetData(string label, Sprite icon = null)
    {
        SetLabel(label);
        SetIcon(icon);
    }

    /// <summary>Set/replace the label text</summary>
    public void SetLabel(string label)
    {
        if (labelText != null)
            labelText.text = label ?? "";
    }

    /// <summary>Set/replace the icon sprite. Passing null will hide the iconImage.</summary>
    public void SetIcon(Sprite icon)
    {
        if (iconImage == null) return;

        if (icon != null)
        {
            iconImage.sprite = icon;
            iconImage.enabled = true;
            // optionally reset native size if the image uses different aspect
            // iconImage.SetNativeSize();
        }
        else
        {
            iconImage.sprite = null;
            iconImage.enabled = false;
        }
    }

    /// <summary>Sets the short distance text (e.g. "120 m on left") into the labelText.</summary>
    public void SetDistanceText(string text)
    {
        if (labelText != null)
            labelText.text = text ?? "";
    }

    /// <summary>Toggle the whole prefab visible/invisible. Use this to hide the object if out of range.</summary>
    public void SetVisible(bool visible)
    {
        gameObject.SetActive(visible);
    }

    /// <summary>Hide or show the label inside the prefab without disabling the whole prefab.</summary>
    public void HideLabel(bool hide)
    {
        if (labelText != null)
            labelText.gameObject.SetActive(!hide);
    }
}
