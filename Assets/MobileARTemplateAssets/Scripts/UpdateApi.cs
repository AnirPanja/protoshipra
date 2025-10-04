using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;

public class LiveUpdateFetcher : MonoBehaviour
{
    [Header("Assign in Inspector")]
    public GameObject cardPrefab;        // Card prefab with LiveUpdateCard component
    public Transform parentContainer;    // Content (ScrollView > Viewport > Content)

    private string apiUrl = "https://shiprabackend.triosoft.ai/api/admin_link/fetch_all_logs";

    void Start()
    {
        if (cardPrefab == null) Debug.LogError("[LiveUpdateFetcher] cardPrefab is not assigned.");
        if (parentContainer == null) Debug.LogError("[LiveUpdateFetcher] parentContainer is not assigned.");
        StartCoroutine(FetchLiveUpdates());
    }

    IEnumerator FetchLiveUpdates()
    {
        // Optional: send form data if API requires POST fields (uncomment/add fields)
        WWWForm form = new WWWForm();
        // form.AddField("type", "parking"); // example

        using (UnityWebRequest www = UnityWebRequest.Post(apiUrl, form))
        {
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("[LiveUpdateFetcher] Error fetching Live Updates: " + www.error);
                yield break;
            }

            string raw = www.downloadHandler.text;
            Debug.Log("[LiveUpdateFetcher] Response: " + raw);

            // Try to parse multiple possible response shapes
            LiveUpdateList parsed = TryParseResponse(raw);

            if (parsed == null || parsed.data == null || parsed.data.Length == 0)
            {
                Debug.LogWarning("[LiveUpdateFetcher] No items found in response.");
                yield break;
            }

            PopulateCards(parsed.data);
        }
    }

    void PopulateCards(LiveUpdateItem[] items)
    {
        if (parentContainer == null)
        {
            Debug.LogError("[LiveUpdateFetcher] parentContainer is null, cannot populate cards.");
            return;
        }

        // Clear existing children
        for (int i = parentContainer.childCount - 1; i >= 0; i--)
        {
            Destroy(parentContainer.GetChild(i).gameObject);
        }

        int created = 0;
        foreach (var item in items)
        {
            if (cardPrefab == null) continue;

            GameObject card = Instantiate(cardPrefab, parentContainer);
            var cardComp = card.GetComponent<LiveUpdateCard>();
            if (cardComp != null)
            {
                // Use the strongly-typed Setup to avoid transform.Find lookups
                cardComp.Setup(item.lost_or_found, item.description, item.entry_date);
            }
            else
            {
                // Fallback: try to find named TMP elements (less safe)
                var tag = card.transform.Find("tag");
                if (tag != null) TrySetTMPText(tag, item.lost_or_found);

                var content = card.transform.Find("Content");
                if (content != null) TrySetTMPText(content, item.description);

                var time = card.transform.Find("TimeText");
                if (time != null) TrySetTMPText(time, item.entry_date);
            }
            created++;
        }

        Debug.Log($"[LiveUpdateFetcher] Created {created} cards.");
    }

    void TrySetTMPText(Transform t, string value)
    {
        if (t == null) return;
        var tmp = t.GetComponent<TMPro.TextMeshProUGUI>();
        if (tmp != null) tmp.text = value ?? "";
    }

    LiveUpdateList TryParseResponse(string rawJson)
    {
        // Attempt #1: Response already in form { "data": [ ... ] }
        try
        {
            var a = JsonUtility.FromJson<LiveUpdateList>(rawJson);
            if (a != null && a.data != null && a.data.Length > 0) return a;
        }
        catch { /* ignore and try next */ }

        // Attempt #2: Response in shape { "success": true, "logs": [ ... ] }
        try
        {
            var wrapper = JsonUtility.FromJson<SuccessLogsWrapper>(rawJson);
            if (wrapper != null && wrapper.logs != null && wrapper.logs.Length > 0)
            {
                return new LiveUpdateList { data = wrapper.logs };
            }
        }
        catch { /* ignore and try next */ }

        // Attempt #3: Response is raw array [...] — wrap it into {"data": ...} and parse
        if (rawJson.TrimStart().StartsWith("["))
        {
            try
            {
                string wrapped = "{\"data\":" + rawJson + "}";
                var b = JsonUtility.FromJson<LiveUpdateList>(wrapped);
                if (b != null && b.data != null && b.data.Length > 0) return b;
            }
            catch { }
        }

        // Failed to parse
        Debug.LogError("[LiveUpdateFetcher] Failed to parse response JSON into known shapes.");
        return null;
    }

    // ----- JSON data containers -----
    [System.Serializable]
    public class LiveUpdateList
    {
        public LiveUpdateItem[] data;
    }

    [System.Serializable]
    public class SuccessLogsWrapper
    {
        public bool success;
        public LiveUpdateItem[] logs;
    }

    [System.Serializable]
    public class LiveUpdateItem
    {
        public string primary_id;
        public string entry_date;
        public string name;
        public string description;
        public string age;
        public string img1;
        public string lost_or_found;
        public string point_address;
        public string lat;
        public string @long; // 'long' is a keyword so using @long
        public string status;
        public string flag;
    }
}
