using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Networking;

#if UNITY_ANDROID
using UnityEngine.Android;
#endif

public class FeedbackVoiceBot : MonoBehaviour
{
    [Header("API Configuration")]
    public string feedbackApiUrl = "https://technotaskapi.triosoft.ai/api/binary/xr-vr-feedback/";

    [Header("Bot Metadata (defaults)")]
    public string bot_id = "1";
    public string bot_name = "Shipra";
    public string custom_flag = "feedback";
    public string front_website = "RideBuddi";
    public int    repeat = 0;

    [Header("Audio & Speech")]
    public AudioSource audioSource;
    private AndroidSpeechRecognition speechRecognition;

    [Header("Overlay UI")]
    public GameObject overlayPanel;          // simple panel
    public TextMeshProUGUI statusText;       // "Listening…" / "Speaking…"
    public TextMeshProUGUI transcriptText;   // show user/bot text

    [Header("Bottom Button")]
    public Button shareFeedbackButton;       // white bottom bar button
    public TextMeshProUGUI shareFeedbackLabel;
    public string shareFeedbackText = "Share Your Feedback";

    [Header("Flow Control")]
    public bool botSpeaksFirst = true;
    public float listenSeconds = 25f;

    // ===== Runtime =====
    private string uid_id;
    private const string EN = "en-US";       // English only
    private string flagForStart = "1";       // "1" continue, "3" end
    private string playMic = "0";
    private bool startServer = true;

    private bool isTimerRunning = false;
    private float timer = 0f;

    private double currentLat = 0, currentLon = 0;
    private string locationName = "";        // destination name passed in by ARNavigation

    void Awake()
    {
        if (shareFeedbackButton != null)
        {
            shareFeedbackButton.onClick.RemoveAllListeners();
            shareFeedbackButton.onClick.AddListener(StartFeedbackFlow_Button);
        }
        if (shareFeedbackLabel != null) shareFeedbackLabel.text = shareFeedbackText;
    }

    void Start()
    {
#if UNITY_ANDROID
        if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
            Permission.RequestUserPermission(Permission.Microphone);
#endif
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();

        speechRecognition = GetComponent<AndroidSpeechRecognition>();
        if (speechRecognition == null) speechRecognition = gameObject.AddComponent<AndroidSpeechRecognition>();
        speechRecognition.onSpeechResult += OnMicrophoneResult;

        HideOverlay();
        Input.location.Start();   // for lat/lon
    }

    void OnDestroy()
    {
        if (speechRecognition != null)
            speechRecognition.onSpeechResult -= OnMicrophoneResult;
    }

    void Update()
    {
        if (!isTimerRunning) return;
        timer -= Time.deltaTime;
        if (timer <= 0f)
        {
            isTimerRunning = false;
            StopListening();
            HideOverlay();
            flagForStart = "3";
        }
    }
public void SetLocationName(string name)
{
    locationName = string.IsNullOrEmpty(name) ? "" : name.Trim();
    Debug.Log($"[FeedbackVoiceBot] location_name set to '{locationName}'");
}
    // ========== Public entry points ==========
    // Called by the UI button directly
    // FeedbackVoiceBot.cs
public void StartFeedbackFlow_Button()
{
    // If locationName is still empty, try to pull it from ARNavigation
    if (string.IsNullOrEmpty(locationName))
    {
        var nav = FindObjectOfType<ARNavigation>();
        if (nav != null)
        {
            // you'll add GetDestinationName() in ARNavigation in step 2
            var n = nav.GetDestinationName();
            if (!string.IsNullOrEmpty(n)) SetLocationName(n);
        }
    }

    StartFeedbackFlow();
}


    // Called by ARNavigation when arriving
    public void StartFeedbackFlow_AutoArrival(string destName)
    {
        locationName = destName; // keep for API
        StartFeedbackFlow();
    }

    // ========== Core flow ==========
    private void StartFeedbackFlow()
    {
        HideShareFeedbackButton();
        uid_id = Guid.NewGuid().ToString("N").Substring(0, 20);
        flagForStart = botSpeaksFirst ? "1" : "1";

        RefreshLocation();

        if (botSpeaksFirst)
            StartCoroutine(BotFirstTurn());
        else
            StartListening();
    }

    private IEnumerator BotFirstTurn()
    {
        yield return null;
        flagForStart="1";
        ServerTurn("Hello, please share your feedback.", flagForStart);
    }

    private void OnMicrophoneResult(string recognizedText)
    {
        if (string.IsNullOrWhiteSpace(recognizedText)) return;
        ShowListening(recognizedText);
        RefreshLocation();
        ServerTurn(recognizedText, flagForStart);
    }

    private void StartListening()
    {
        if (speechRecognition == null || !speechRecognition.IsInitialized())
        {
            Debug.LogWarning("[FeedbackVoiceBot] SpeechRecognition not initialized");
            return;
        }
        ShowListening();
        speechRecognition.StartListening(EN);   // English only
        StartTimer(listenSeconds);
    }

    private void StopListening()
    {
        if (speechRecognition != null && speechRecognition.IsListening())
            speechRecognition.StopListening();
        isTimerRunning = false;
    }

    private void StartTimer(float sec)
    {
        timer = sec;
        isTimerRunning = true;
    }

    // ========== Overlay helpers ==========
    private void SetOverlay(bool show, string status = "", string transcript = "")
    {
        if (overlayPanel != null) overlayPanel.SetActive(show);
        if (statusText != null) statusText.text = status ?? "";
        if (transcriptText != null) transcriptText.text = transcript ?? "";
    }
    private void ShowListening(string t = "") => SetOverlay(true, "Listening…", t);
    private void ShowSpeaking(string t = "") => SetOverlay(true, "Speaking…", t);
    private void HideOverlay() => SetOverlay(false, "", "");
private void HideShareFeedbackButton()
{
    if (shareFeedbackButton != null)
        shareFeedbackButton.gameObject.SetActive(false);
}

private void ShowShareFeedbackButton()
{
    if (shareFeedbackButton != null)
        shareFeedbackButton.gameObject.SetActive(true);
}

    // ========== Server I/O ==========
    private void ServerTurn(string userText, string frontFlag)
    {
        if (!startServer) return;
        startServer = false;

        RefreshLocation();

        var body = new FeedbackRequest
        {
            content_txt      = userText,
            front_flag       = frontFlag,
            uid_id           = uid_id,
            custom_flag      = custom_flag,
            bot_id           = bot_id,
            bot_name         = bot_name,
            repeat           = repeat,
            front_website    = front_website,
            location_name    = locationName,
            content_language = EN,
            language_used    = EN,
            lat              = currentLat.ToString(),
            lng              = currentLon.ToString()
        };


        Debug.Log($"[XR-LOCATION]{frontFlag}");

        StartCoroutine(SendFeedbackRequest(body));
    }

    private IEnumerator SendFeedbackRequest(FeedbackRequest data)
    {
        string json = JsonUtility.ToJson(data);
        using (UnityWebRequest req = new UnityWebRequest(feedbackApiUrl, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                HandleFeedbackResponse(req.downloadHandler.text);
            }
            else
            {
                Debug.LogError($"[FeedbackAPI] {req.responseCode} - {req.error}\n{req.downloadHandler.text}");
                startServer = true;
                HideOverlay();
            }
            
        }
    }

    private void HandleFeedbackResponse(string json)
    {
        FeedbackResponse r = null;
        try { r = JsonUtility.FromJson<FeedbackResponse>(json); }
        catch (Exception e)
        {
            Debug.LogError("[FeedbackAPI] JSON parse error: " + e.Message);
            startServer = true; HideOverlay(); return;
        }
        if (r == null) { startServer = true; HideOverlay(); return; }

        flagForStart = string.IsNullOrEmpty(r.for_front_flag) ? "3" : r.for_front_flag;
        playMic      = string.IsNullOrEmpty(r.play_mic) ? "0" : r.play_mic;

        if (!string.IsNullOrEmpty(r.speak_txt)) ShowSpeaking(r.speak_txt);
        else ShowSpeaking();

        if (!string.IsNullOrEmpty(r.file_path))
        {
            StartCoroutine(PlayAudioFromURL(r.file_path, onDone: () =>
            {
                if (playMic == "1" && flagForStart != "3") StartListening();
                else HideOverlay();
                 if (flagForStart == "3") ShowShareFeedbackButton();
                startServer = true;
            }));
        }
        else
        {
            if (playMic == "1" && flagForStart != "3") StartListening();
            else HideOverlay();
            if (flagForStart == "3") ShowShareFeedbackButton(); 
            startServer = true;
        }
    }

    private IEnumerator PlayAudioFromURL(string url, Action onDone)
    {
        using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.MPEG))
        {
            yield return www.SendWebRequest();
            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("[FeedbackVoiceBot] Audio download error: " + www.error);
                onDone?.Invoke();
                yield break;
            }
            var clip = DownloadHandlerAudioClip.GetContent(www);
            audioSource.clip = clip;
            audioSource.Play();
            yield return new WaitForSeconds(clip.length);
            onDone?.Invoke();
        }
    }

    // ========== Helpers ==========
    private void RefreshLocation()
    {
        if (Input.location.status == LocationServiceStatus.Running)
        {
            currentLat = Input.location.lastData.latitude;
            currentLon = Input.location.lastData.longitude;
        }
    }

    // === DTOs expected by your backend ===
    [Serializable]
    private class FeedbackRequest
    {
        public string content_txt;
        public string front_flag;
        public string uid_id;
        public string custom_flag;
        public string bot_id;
        public string bot_name;
        public int    repeat;
        public string front_website;
        public string location_name;
        public string content_language;
        public string language_used;
        public string lat;
        public string lng;
    }

    [Serializable]
    private class FeedbackResponse
    {
        public string for_front_flag;
        public string play_mic;
        public string speak_txt;
        public string file_path;
    }
}
