using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;

[System.Serializable]
public class PointData
{
    public int primary_id;
    public string point_name;
    public string point_address;
    public string description;
    public string lat;
    public string @long;   // `long` is a keyword, so use @long
    public string type;
}

[System.Serializable]
public class PointDataWrapper
{
    public List<PointData> data;
}

public class NavigationUIManager : MonoBehaviour
{
    [Header("UI References")]
    public TMP_Dropdown destinationDropdown;
    public Button getDirectionButton;

    [Header("Navigation References")]
    public CanvasSwitcher canvasSwitcher;  // Assign this in Inspector
    public ARNavigation arNavigation;      // Assign this in Inspector

    private string apiUrl = "https://shiprabackend.triosoft.ai/api/admin_link/fetch_points";
    private List<PointData> cachedPoints = new List<PointData>();

    void Start()
    {
        StartCoroutine(FetchDestinations());
        getDirectionButton.onClick.AddListener(OnGetDirection);
    }

    IEnumerator FetchDestinations()
    {
        WWWForm form = new WWWForm();

        using (UnityWebRequest request = UnityWebRequest.Post(apiUrl, form))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.ConnectionError ||
                request.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError($"❌ API Error: {request.error} | Code: {request.responseCode}");
                yield break;
            }

            string jsonResponse = request.downloadHandler.text;
            Debug.Log("✅ API Response: " + jsonResponse);

            PointDataWrapper wrapper = JsonUtility.FromJson<PointDataWrapper>(jsonResponse);
            if (wrapper != null && wrapper.data != null)
            {
                cachedPoints = wrapper.data;

                List<string> options = new List<string>();
                foreach (var p in cachedPoints)
                {
                    options.Add(string.IsNullOrEmpty(p.point_name) ? "Unnamed" : p.point_name);
                }

                destinationDropdown.ClearOptions();
                destinationDropdown.AddOptions(options);
            }
            else
            {
                Debug.LogError("⚠️ Could not parse JSON into PointDataWrapper.");
            }
        }
    }

    void OnGetDirection()
    {
        string selected = destinationDropdown.options[destinationDropdown.value].text;
        Debug.Log("Selected Point: " + selected);

        // Switch canvases
        if (canvasSwitcher != null)
        {
            canvasSwitcher.ShowNavigationCanvas();
        }
        else
        {
            Debug.LogError("CanvasSwitcher is not assigned in NavigationUIManager!");
        }

        // Call ARNavigation
        if (arNavigation != null)
        {
            Vector2 source = new Vector2(0, 0);  // TODO: replace with current user location
            Vector2 destination = new Vector2(32.1548f, 82.6589f); // Example, replace with API data

            arNavigation.StartNavigation(source, destination);
        }
        else
        {
            Debug.LogError("ARNavigation is not assigned in NavigationUIManager!");
        }
    }
}
