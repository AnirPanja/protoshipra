using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class LostFoundUIManager : MonoBehaviour
{
    [Header("UI References")]
    public TMP_Dropdown reportTypeDropdown;
    public TMP_InputField descriptionField;
    public TMP_InputField nameField;
    public TMP_InputField nameuserField;
    public TMP_InputField mobileField;
    public TMP_InputField age;
    public TMP_InputField locationField;
    public Button submitButton;

    [Header("Alert UI (optional)")]
    public TMP_Text alertText; // assign in Inspector or let script create one
    public float alertDuration = 3f;
    public Color successColor = Color.green;
    public Color errorColor = Color.red;
    public Color infoColor = Color.yellow;

    private Coroutine alertCoroutine;

    void Start()
    {
        if (submitButton != null)
        {
            submitButton.onClick.RemoveListener(OnSubmit);
            submitButton.onClick.AddListener(OnSubmit);
        }
        else
        {
            Debug.LogWarning("[LostFoundUIManager] submitButton not assigned in Inspector.");
        }

        if (alertText == null)
            TryFindOrCreateAlertText();
        else
            alertText.gameObject.SetActive(false);

        Debug.Log("[LostFoundUIManager] Start complete. alertText assigned: " + (alertText != null));
    }

    void OnSubmit()
    {
        // Show submitting toast (persistent until response)
        ShowAlert("Submitting Lost & Found...", infoColor, -1f);

        // TODO: replace with actual latitude/longitude logic if available
        string latStr = "0";
        string longStr = "0";

        WWWForm form = new WWWForm();
        form.AddField("name", nameField != null ? nameField.text : "");
        form.AddField("description", descriptionField != null ? descriptionField.text : "");
        form.AddField("age", age != null ? age.text : "");
        form.AddField("lost_or_found", reportTypeDropdown != null && reportTypeDropdown.options.Count > 0
                                   ? reportTypeDropdown.options[reportTypeDropdown.value].text
                                   : "");
        form.AddField("point_address", locationField != null ? locationField.text : "");
        form.AddField("lat", latStr);
        form.AddField("long", longStr);
        form.AddField("Contact_person_name", nameuserField != null ? nameuserField.text : "");
        form.AddField("Contact_person_number", mobileField != null ? mobileField.text : "");

        if (APIManager.Instance != null)
        {
            StartCoroutine(APIManager.Instance.PostFormRequest(
                "https://shiprabackend.triosoft.ai/api/admin_link/save_update_lost_found",
                form,
                OnResponse
            ));
        }
        else
        {
            Debug.LogWarning("[LostFoundUIManager] APIManager instance not found. Lost & Found not sent.");
            ShowAlert("Failed: API Manager not found.", errorColor, alertDuration);
        }
    }

    void OnResponse(string res)
    {
        Debug.Log("[LostFoundUIManager] Lost & Found Submitted: " + res);

        // Basic success detection - change logic to suit your API's actual response format
        bool isSuccess = !string.IsNullOrEmpty(res) && !res.ToLower().Contains("error");

        if (isSuccess)
        {
            ShowAlert("Submitted successfully!", successColor, alertDuration);
        }
        else
        {
            ShowAlert("Submission failed. Try again.", errorColor, alertDuration);
        }
    }

    // Show alert; duration <= 0 => persistent until HideAlert called or replaced
    private void ShowAlert(string message, Color color, float duration)
    {
        if (alertText == null)
        {
            Debug.LogWarning("[LostFoundUIManager] ShowAlert: alertText is null. Attempting to create one.");
            TryFindOrCreateAlertText();
            if (alertText == null)
            {
                Debug.LogError("[LostFoundUIManager] Could not create alertText. Aborting ShowAlert.");
                return;
            }
        }

        alertText.text = message;
        alertText.color = color;
        alertText.gameObject.SetActive(true);
        alertText.transform.SetAsLastSibling();

        if (alertCoroutine != null)
            StopCoroutine(alertCoroutine);

        if (duration > 0f)
            alertCoroutine = StartCoroutine(HideAlertAfterSeconds(duration));
        else
            alertCoroutine = null; // persistent until next ShowAlert or HideAlert
    }

    private IEnumerator HideAlertAfterSeconds(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        if (alertText != null)
            alertText.gameObject.SetActive(false);
        alertCoroutine = null;
    }

    // Attempt to find an existing TMP_Text named "AlertText" or create one under first Canvas
    private void TryFindOrCreateAlertText()
    {
        // try find by name first
        GameObject byName = GameObject.Find("AlertText");
        if (byName != null)
        {
            TMP_Text tmp = byName.GetComponent<TMP_Text>();
            if (tmp != null)
            {
                alertText = tmp;
                alertText.gameObject.SetActive(false);
                Debug.Log("[LostFoundUIManager] Found AlertText by name in scene.");
                return;
            }
        }

        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("[LostFoundUIManager] No Canvas found in scene. Please add a Canvas to show UI elements.");
            return;
        }

        GameObject go = new GameObject("AlertText", typeof(RectTransform));
        go.transform.SetParent(canvas.transform, false);

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.95f);
        rt.anchorMax = new Vector2(0.5f, 0.95f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(600, 60);

        TMP_Text tmpText = go.AddComponent<TMP_Text>();
        tmpText.fontSize = 24;
        tmpText.alignment = TextAlignmentOptions.Center;
        tmpText.enableWordWrapping = true;
        tmpText.text = "";

#if UNITY_EDITOR
        // avoid editor errors if font missing; editor-only attempt to assign default font (optional)
        if (tmpText.font == null)
        {
            // editor-only lookup omitted for brevity - assign in inspector if needed
        }
#endif

        alertText = tmpText;
        alertText.gameObject.SetActive(false);
        Debug.Log("[LostFoundUIManager] Created AlertText at runtime under Canvas.");
    }
}
