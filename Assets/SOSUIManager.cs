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

    private float latitude;
    private float longitude;

    void Start()
    {
        if (submitButton != null)
            submitButton.onClick.AddListener(OnSubmitSOS);

        latitude = PlayerPrefs.GetFloat("latitude", 0f);
        longitude = PlayerPrefs.GetFloat("longitude", 0f);
    }

void OnSubmitSOS()
{
    // Use empty string if field is null
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
        StartCoroutine(APIManager.Instance.PostFormRequest("https://shiprabackend.triosoft.ai/api/admin_link/save_update_sos_call", form, OnResponse));
    }
    else
    {
        Debug.LogWarning("APIManager instance not found. SOS not sent.");
    }
}
    void OnResponse(string response)
    {
        Debug.Log("SOS Response: " + response);
        // TODO: Show success popup or confirmation to user
    }
}
