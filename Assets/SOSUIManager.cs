using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections;

public class SOSUIManager : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TMP_InputField nameInput;
    [SerializeField] private TMP_InputField contactInput;
    [SerializeField] private TMP_Dropdown emergencyTypeDropdown;
    [SerializeField] private TMP_InputField addressInput;
    [SerializeField] private TMP_InputField latInput;
    [SerializeField] private TMP_InputField longInput;
    [SerializeField] private TMP_InputField descriptionInput;
    [SerializeField] private Button submitButton;

    [Header("Alert UI")]
    [SerializeField] private TMP_Text alertText;  // optional: will be auto-created if null
    [SerializeField] private float alertDuration = 3f;
    [SerializeField] private Color successColor = Color.green;
    [SerializeField] private Color errorColor = Color.red;
    [SerializeField] private Color infoColor = Color.yellow;

    private float latitude;
    private float longitude;

    void Start()
    {
        // ensure submit listener
        if (submitButton != null)
        {
            submitButton.onClick.RemoveListener(OnSubmitSOS);
            submitButton.onClick.AddListener(OnSubmitSOS);
        }
        else
        {
            Debug.LogWarning("[SOSUIManager] submitButton NOT assigned in Inspector.");
        }

        latitude = PlayerPrefs.GetFloat("latitude", 0f);
        longitude = PlayerPrefs.GetFloat("longitude", 0f);

        // Try to ensure alertText exists
        if (alertText == null)
        {
            TryFindOrCreateAlertText();
        }
        else
        {
            // hide by default
            alertText.gameObject.SetActive(false);
        }

        Debug.Log("[SOSUIManager] Start complete. alertText assigned: " + (alertText != null));
    }

    void OnSubmitSOS()
    {
        // show submitting message
        ShowAlert("Submitting SOS...", infoColor, -1f); // -1 = show until response

        string name = nameInput != null ? nameInput.text : "";
        string contact = contactInput != null ? contactInput.text : "";
        string emergencyType = (emergencyTypeDropdown != null && emergencyTypeDropdown.options.Count > 0)
                               ? emergencyTypeDropdown.options[emergencyTypeDropdown.value].text
                               : "";
        string address = addressInput != null ? addressInput.text : "";
        string desc = descriptionInput != null ? descriptionInput.text : "";
        string latStr = latitude.ToString();
        string longStr = longitude.ToString();

        WWWForm form = new WWWForm();
        form.AddField("name", name);
        form.AddField("contact", contact);
        form.AddField("type_of_emergency", emergencyType);
        form.AddField("point_address", address);
        form.AddField("lat", latStr);
        form.AddField("long", longStr);
        form.AddField("description", desc);

        if (APIManager.Instance != null)
        {
            StartCoroutine(APIManager.Instance.PostFormRequest(
                "https://shiprabackend.triosoft.ai/api/admin_link/save_update_sos_call", 
                form, 
                OnResponse));
        }
        else
        {
            Debug.LogWarning("[SOSUIManager] APIManager instance not found. SOS not sent.");
            ShowAlert("Failed: API Manager not found.", errorColor, alertDuration);
        }
    }

    void OnResponse(string response)
    {
        Debug.Log("[SOSUIManager] SOS Response: " + response);

        // naive success check (adjust as per your API)
        bool isSuccess = !string.IsNullOrEmpty(response) && !response.ToLower().Contains("error");

        if (isSuccess)
        {
            ShowAlert("SOS Sent Successfully!", successColor, alertDuration);
        }
        else
        {
            ShowAlert("Failed to send SOS. Try again.", errorColor, alertDuration);
        }
    }

    // Public helper to show alerts. duration <=0 means persistent (until explicitly hidden)
    private Coroutine alertCoroutine;
    private void ShowAlert(string message, Color color, float duration)
    {
        if (alertText == null)
        {
            Debug.LogWarning("[SOSUIManager] ShowAlert called but alertText is null. Attempting to create one.");
            TryFindOrCreateAlertText();
            if (alertText == null)
            {
                Debug.LogError("[SOSUIManager] Could not create alertText. Aborting ShowAlert.");
                return;
            }
        }

        alertText.text = message;
        alertText.color = color;
        alertText.gameObject.SetActive(true);

        // optionally bring to front
        alertText.transform.SetAsLastSibling();

        // stop previous coroutine
        if (alertCoroutine != null) StopCoroutine(alertCoroutine);
        if (duration > 0f)
        {
            alertCoroutine = StartCoroutine(HideAlertAfterSeconds(duration));
        }
    }

    private IEnumerator HideAlertAfterSeconds(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        if (alertText != null)
            alertText.gameObject.SetActive(false);
        alertCoroutine = null;
    }

    // Creates a simple TMP alert text if none found. Tries to find "AlertText" by name first.
    private void TryFindOrCreateAlertText()
    {
        // try find by name
        GameObject byName = GameObject.Find("AlertText");
        if (byName != null)
        {
            alertText = byName.GetComponent<TMP_Text>();
            if (alertText != null) { alertText.gameObject.SetActive(false); return; }
        }

        // try find any TMP_Text in canvas with tag or first canvas found
        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("[SOSUIManager] No Canvas found in scene. Please add a Canvas to show UI elements.");
            return;
        }

        // create a new GameObject with TMP_Text as child of canvas
        GameObject go = new GameObject("AlertText", typeof(RectTransform));
        go.transform.SetParent(canvas.transform, false);

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.95f);
        rt.anchorMax = new Vector2(0.5f, 0.95f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(600, 60);

        // Add TMP_Text
        TMP_Text tmp = go.AddComponent<TMP_Text>();
        tmp.fontSize = 24;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = true;
        tmp.text = "";
#if UNITY_EDITOR
        // If running inside Editor, ensure there's a default TMP font assigned to avoid null warnings
        if (tmp.font == null)
        {
            var settings = UnityEditor.AssetDatabase.FindAssets("TMPro Font");
            // not mandatory; ignore if none found
        }
#endif
        alertText = tmp;
        alertText.gameObject.SetActive(false);

        Debug.Log("[SOSUIManager] Created AlertText at runtime under Canvas.");
    }
}
