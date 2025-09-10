using UnityEngine;
using UnityEngine.UI;
using TMPro;

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

    void Start()
    {
        submitButton.onClick.AddListener(OnSubmit);
    }

    void OnSubmit()
{
    // Make sure lat and long exist; replace with your location logic
    string latStr = "0";  // Replace with actual latitude
    string longStr = "0"; // Replace with actual longitude

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
        Debug.LogWarning("APIManager instance not found. Lost & Found not sent.");
    }
}

    void OnResponse(string res)
    {
        Debug.Log("Lost & Found Submitted: " + res);
    }
}
