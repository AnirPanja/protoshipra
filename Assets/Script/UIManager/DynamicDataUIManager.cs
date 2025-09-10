using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using TMPro;
using UnityEngine.UI;
using System;

[Serializable]
public class ParkingPoint
{
    public int primary_id;
    public string entry_date;
    public string image_url;
    public string image_url1;
    public string point_name;
    public string point_address;
    public string description;
    public string lat;
    public string @long; // "long" in JSON
    public string type;
    public string status;
    public string start_time;
    public string end_time;
    public string active_again_on;
    public string max_capacity;
    public string occupied;
    public string flag;
}

[Serializable]
public class ParkingResponse
{
    public ParkingPoint[] data;
    public bool error;
}

public class DynamicDataUIManager : MonoBehaviour
{
    [Header("UI References (assign in scene)")]
    public RectTransform contentParent;         // assign the Scene Content (Scroll View -> Viewport -> Content)
    public GameObject parkingItemPrefab;        // prefab asset

    [Header("API")]
    public string apiUrl = "https://shiprabackend.triosoft.ai/api/admin_link/fetch_points_type";

    private void Start()
    {
        // Try to auto-find content if not assigned
        if (contentParent == null)
        {
            contentParent = FindScrollContent();
            if (contentParent == null)
            {
                Debug.LogError("[DynamicDataUIManager] contentParent is NULL. Please assign Content RectTransform (Scroll View -> Viewport -> Content) in the Inspector. Falling back to Canvas.");
            }
            else
            {
                Debug.Log("[DynamicDataUIManager] Auto-found Content: " + contentParent.name);
            }
        }

        if (parkingItemPrefab == null)
        {
            Debug.LogError("[DynamicDataUIManager] parkingItemPrefab is NULL. Assign prefab asset in Inspector.");
            return;
        }

        StartCoroutine(FetchParkingPoints_FormPost());
    }

    RectTransform FindScrollContent()
    {
        // Attempt to find a GameObject named "Content" that is a RectTransform and child of a ScrollRect
        var contentGo = GameObject.Find("Content");
        if (contentGo != null)
        {
            var rt = contentGo.GetComponent<RectTransform>();
            if (rt != null) return rt;
        }

        // fallback: search for any ScrollRect then its Content
        var scrolls = GameObject.FindObjectsOfType<ScrollRect>();
        foreach (var s in scrolls)
        {
            if (s.content != null) return s.content;
        }

        // fallback: try to find a Canvas and create an empty parent underneath it
        var canvas = FindObjectOfType<Canvas>();
        if (canvas != null)
        {
            GameObject fallback = new GameObject("DynamicListFallbackParent", typeof(RectTransform));
            fallback.transform.SetParent(canvas.transform, false);
            var rtf = fallback.GetComponent<RectTransform>();
            rtf.anchorMin = new Vector2(0.5f, 0.5f);
            rtf.anchorMax = new Vector2(0.5f, 0.5f);
            rtf.sizeDelta = new Vector2(800, 600);
            return rtf;
        }

        return null;
    }

    IEnumerator FetchParkingPoints_FormPost()
    {
        WWWForm form = new WWWForm();
        form.AddField("type", "parking");

        using (UnityWebRequest www = UnityWebRequest.Post(apiUrl, form))
        {
            www.timeout = 15;
            yield return www.SendWebRequest();

#if UNITY_2020_1_OR_NEWER
            if (www.result != UnityWebRequest.Result.Success)
#else
            if (www.isNetworkError || www.isHttpError)
#endif
            {
                Debug.LogError($"[DynamicDataUIManager] API Error: {www.error}  URL:{apiUrl}");
                yield break;
            }

            string respText = www.downloadHandler.text;
            Debug.Log($"[DynamicDataUIManager] Response: {Truncate(respText, 1000)}");

            string json = respText.Trim();
            if (!json.StartsWith("{") && !json.StartsWith("["))
            {
                Debug.LogError("[DynamicDataUIManager] Response is not JSON. Inspect server response in Console.");
                yield break;
            }

            ParkingResponse resp = null;
            try
            {
                resp = JsonUtility.FromJson<ParkingResponse>(json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DynamicDataUIManager] JSON parse exception: {ex.Message}");
                yield break;
            }

            if (resp == null || resp.data == null)
            {
                Debug.LogWarning("[DynamicDataUIManager] No data array in response.");
                yield break;
            }

            PopulateList(resp.data);
        }
    }

    void PopulateList(ParkingPoint[] points)
    {
        if (contentParent == null)
        {
            Debug.LogWarning("[DynamicDataUIManager] contentParent still null — items will be parented to root Canvas.");
        }

        // Clear existing children under contentParent (if any)
        if (contentParent != null)
        {
            for (int i = contentParent.childCount - 1; i >= 0; i--)
            {
                Destroy(contentParent.GetChild(i).gameObject);
            }
        }

        Debug.Log($"[DynamicDataUIManager] Populating {points.Length} items...");

        int created = 0;
        foreach (var p in points)
        {
            if (p == null) continue;

            GameObject itemGO;

            // Instantiate and parent to contentParent if available (use SetParent to preserve UI transform)
            if (contentParent != null)
            {
                itemGO = Instantiate(parkingItemPrefab, contentParent, false);
            }
            else
            {
                // No content parent: instantiate at root
                itemGO = Instantiate(parkingItemPrefab);
            }

            if (itemGO == null) continue;

            // Try ParkingItem component if present
            var parkingComp = itemGO.GetComponent<ParkingItem>();
            if (parkingComp != null)
            {
                parkingComp.Set(p.point_name, p.point_address, p.status, p.lat, p.@long);
                created++;
                continue;
            }

            // Fallback: look for children named "Heading" and "Content"
            Transform headingTf = itemGO.transform.Find("Heading");
            Transform contentTf = itemGO.transform.Find("Content");

            if (headingTf != null)
            {
                var t = headingTf.GetComponent<TMP_Text>();
                if (t != null) t.text = p.point_name;
            }
            if (contentTf != null)
            {
                var t2 = contentTf.GetComponent<TMP_Text>();
                if (t2 != null) t2.text = p.point_address;
            }

            created++;
        }

        Debug.Log($"[DynamicDataUIManager] Created {created} UI items (check Content in Hierarchy).");
    }

    string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return s;
        if (s.Length <= max) return s;
        return s.Substring(0, max) + "...(truncated)";
    }
}
