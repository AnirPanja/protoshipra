using UnityEngine;
using TMPro;
using System.Collections;
using System.Collections.Generic;

public class LiveUpdateUIManager : MonoBehaviour
{
    [Header("UI References")]
    public Transform cardContainer;          // The parent container (ScrollView content)
    public GameObject cardPrefab;            // Prefab for each update card

    void Start()
    {
        // Example: Fetch data from backend
        StartCoroutine(APIManager.Instance.GetRequest("live-updates", OnLiveUpdatesReceived));
    }

    void OnLiveUpdatesReceived(string json)
    {
        // Convert JSON to model
        LiveUpdate[] updates = JsonHelper.FromJson<LiveUpdate>(json);

        // Clear old cards
        foreach (Transform child in cardContainer)
            Destroy(child.gameObject);

        // Spawn new cards
        foreach (var update in updates)
        {
            GameObject card = Instantiate(cardPrefab, cardContainer);
            TMP_Text[] texts = card.GetComponentsInChildren<TMP_Text>();

            foreach (var t in texts)
            {
                if (t.name == "AlertText") t.text = update.alert;
                else if (t.name == "TimeText") t.text = update.time;
                else if (t.name == "DescriptionText") t.text = update.description;
            }
        }
    }
}
