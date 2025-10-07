using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections;
using UnityEngine.SceneManagement;

#if UNITY_ANDROID
using UnityEngine.Android;
#endif

public class Voice_boat : MonoBehaviour
{
    [Header("Navigation")]
    public string targetSceneName = "ARNavigation";

    [Header("API")]
    [Tooltip("POST endpoint that returns TTS url + optional lat/lng/place")]
    public string apiUrl = "https://technotaskapi.triosoft.ai/api/binary/xr-vr-conversation/";

    [Header("Audio")]
    public AudioSource audioSource;

    [Header("Language Popup UI")]
    public GameObject languagePopup;         // Panel (inactive by default)
    public TMP_Dropdown languageDropdown;    // Options order must match map (see MapDropdownSelectionToLangCode)

    [Header("Overlay UI (no video, text only)")]
    public GameObject overlayPanel;          // Panel (inactive by default)
    public TextMeshProUGUI statusText;       // "Listening…" / "Speaking…"
    public TextMeshProUGUI transcriptText;   // AI or user transcript

    // ====== Conversation & Identity ======
    private string uidForPerson = "";
    private string companyNameCache = "Sameer";
    private string companyNameId = "";
    private string botId = "";
    private string botName = "";
    private string chatboatId = "";
    private string frontWebsiteWord = "";

    // ====== Language ======
    private string languageFor  = "bho-IN";  // default; replaced by dropdown
    private string targetLanguage = "bho-IN";

    // ====== Flow flags ======
    // "1" = first turn (bot speaks first), "3" = end
    private string flagForStart = "1";
    private string playMic = "0";
    private bool startServer = true;

    // ====== Q/A rolling history (2-turn memory) ======
    private string questionFirst = "";
    private string questionSecond = "";
    private string answerFirst = "";
    private string answerSecond = "";

    // Mic / Speech
    private AndroidSpeechRecognition speechRecognition;
    private bool isTimerRunning = false;
    private float timer = 30f;

    void Start()
    {
        RequestMicrophonePermission();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();

        // Android SR component
        speechRecognition = GetComponent<AndroidSpeechRecognition>();
        if (speechRecognition == null) speechRecognition = gameObject.AddComponent<AndroidSpeechRecognition>();
        speechRecognition.onSpeechResult += OnMicrophoneResult;

        // UI initial state
        HideOverlay();
        if (languagePopup != null) languagePopup.SetActive(false);
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
            flagForStart = "3";
            StopListening();
            HideOverlay();
        }
    }

    // ====== Permissions ======
    void RequestMicrophonePermission()
    {
#if UNITY_ANDROID
        if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
            Permission.RequestUserPermission(Permission.Microphone);
#endif
    }

    // ====== Popup hooks ======
    public void OnMicButtonClick()
    {
        if (languagePopup == null) { Debug.LogWarning("LanguagePopup not linked."); return; }
        languagePopup.SetActive(true);
    }

    public void OnLanguageConfirm()
    {
        if (languageDropdown != null)
        {
            languageFor = MapDropdownSelectionToLangCode(languageDropdown.value);
            targetLanguage = languageFor;
        }
        if (languagePopup != null) languagePopup.SetActive(false);

        // Start session: bot speaks first
        uidForPerson = GenerateUID();
        flagForStart = "1";
        StartCoroutine(BotFirstTurn());
    }

public void OnLanguageCancel()
{
    if (languagePopup != null)
        languagePopup.SetActive(false); // just close popup
}
    // ====== Overlay helpers ======
    private void SetOverlay(bool show, string status = "", string transcript = "")
    {
        if (overlayPanel != null) overlayPanel.SetActive(show);
        if (statusText != null) statusText.text = status ?? "";
        if (transcriptText != null) transcriptText.text = transcript ?? "";
    }
    private void ShowListening(string transcript = "")
    {
        SetOverlay(true, "Listening…", transcript);
    }
    private void ShowSpeaking(string transcript = "")
    {
        SetOverlay(true, "Speaking…", transcript);
    }
    private void HideOverlay()
    {
        SetOverlay(false, "", "");
    }

    // ====== First bot message ======
    private IEnumerator BotFirstTurn()
    {
        yield return null; // debounce
        StartServerTurn("Hello", "1");
    }

    // ====== SR result → next server turn ======
    private void OnMicrophoneResult(string recognizedText)
    {
        if (string.IsNullOrEmpty(recognizedText)) return;

        // Show user's transcript on overlay
        ShowListening(recognizedText);

        // Send to server as user's turn
        StartServerTurn(recognizedText, flagForStart);
    }

    // ====== Start / Stop listening ======
    private void StartListening()
    {
        if (speechRecognition == null || !speechRecognition.IsInitialized())
        {
            Debug.LogWarning("SpeechRecognition not ready yet.");
            return;
        }
        ShowListening();
        var sttLang = GetSTTLang(languageFor);
Debug.Log("[Voice_boat] STT language: " + sttLang + " (desired: " + languageFor + ")");
speechRecognition.StartListening(sttLang);
        StartTimer(30f);
    }

    private void StopListening()
    {
        if (speechRecognition != null && speechRecognition.IsListening())
            speechRecognition.StopListening();
        isTimerRunning = false;
    }

    private void StartTimer(float seconds)
    {
        timer = seconds;
        isTimerRunning = true;
    }

    // ====== Server roundtrip ======
    private void StartServerTurn(string content, string frontFlag)
    {
        if (!startServer) return;
        startServer = false;

        // keep 2-turn rolling Q memory
        PushQuestion(content);

        var req = new ConversationRequest
        {
            content_txt     = content,
            language_used    = NormalizeLang(languageFor),
            content_language = NormalizeLang(targetLanguage),
            uid_id          = uidForPerson,
            front_website   = frontWebsiteWord,
            question_1      = questionFirst,
            question_2      = string.IsNullOrEmpty(questionSecond) ? "0" : questionSecond,
            answer_1        = string.IsNullOrEmpty(answerFirst) ? "0" : answerFirst,
            answer_2        = string.IsNullOrEmpty(answerSecond) ? "0" : answerSecond,
            front_flag      = frontFlag,
            custom_flag     = "0",
            company_name    = companyNameCache,
            company_id      = companyNameId,
            bot_id          = botId,
            bot_name        = botName,
            chatboat_id     = chatboatId
        };

        StartCoroutine(SendServerRequest(req));
    }

    private IEnumerator SendServerRequest(ConversationRequest data)
    {
        string json = JsonUtility.ToJson(data);
        using (var req = new UnityWebRequest(apiUrl, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                HandleServerResponse(req.downloadHandler.text);
            }
            else
            {
                Debug.LogError($"[Server] {req.responseCode} - {req.error}\n{req.downloadHandler.text}");
                startServer = true;
                // nothing else; overlay state will be controlled by next actions
            }
        }
    }

    private void HandleServerResponse(string json)
    {
        ServerResponse r = null;
        try { r = JsonUtility.FromJson<ServerResponse>(json); }
        catch (Exception e)
        {
            Debug.LogError("JSON parse error: " + e.Message);
            startServer = true;
            HideOverlay();
            return;
        }

        if (r == null)
        {
            Debug.LogError("ServerResponse is null!");
            startServer = true;
            HideOverlay();
            return;
        }

        // Update flow flags
        flagForStart = string.IsNullOrEmpty(r.for_front_flag) ? "3" : r.for_front_flag;
        playMic      = string.IsNullOrEmpty(r.play_mic) ? "0" : r.play_mic;

        // Answer history
        PushAnswer(r.speak_txt ?? "");

        // ---- Show AI transcript while speaking ----
        if (!string.IsNullOrEmpty(r.speak_txt)) ShowSpeaking(r.speak_txt);
        else ShowSpeaking();

        // Did we get destination?
        bool gotDestination = Math.Abs(r.lat) > 1e-9 && Math.Abs(r.lng) > 1e-9;

        // If destination present, set it now (we will navigate after audio)
        if (gotDestination)
        {
            NavigationData.Destination = new GeoCoordinate(r.lat, r.lng);
            NavigationData.DestinationName = string.IsNullOrEmpty(r.place_name) ? "" : r.place_name;
        }

        // Play TTS if provided; after speaking decide next
        if (!string.IsNullOrEmpty(r.file_path))
        {
            StartCoroutine(PlayAudioFromURL(r.file_path, onDone: () =>
            {
                // If we had destination -> navigate
                if (gotDestination)
                {
                    if (NavigationData.Source.IsZero())
                        StartCoroutine(FillSourceFromGPSAndNavigate());
                    else
                    {
                        NavigationData.HasData = true;
                        SceneManager.LoadScene(targetSceneName);
                    }
                }
                else
{
    // If AI didn't give lat/lng: listen again only if server allows
    if (playMic == "1" && flagForStart != "3")
    {
        ShowListening();
        StartListening();
    }
    else
    {
        HideOverlay();
        StopListening();
    }
}

                startServer = true;
            }));
        }
        else
        {
            // No audio returned: just enforce same rule
            if (gotDestination)
            {
                if (NavigationData.Source.IsZero())
                    StartCoroutine(FillSourceFromGPSAndNavigate());
                else
                {
                    NavigationData.HasData = true;
                    SceneManager.LoadScene(targetSceneName);
                }
            }
            else
{
    if (playMic == "1" && flagForStart != "3")
    {
        ShowListening();
        StartListening();
    }
    else
    {
        HideOverlay();
        StopListening();
    }
}
            startServer = true;
        }
    }
private string GetSTTLang(string desired)
{
    switch (desired)
    {
        case "bho-IN": return "hi-IN";   // fallback: Bhojpuri → Hindi
        // add more fallbacks if needed
        default: return desired;
    }
}
    private IEnumerator PlayAudioFromURL(string url, Action onDone)
    {
        using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.MPEG))
        {
            yield return www.SendWebRequest();
            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Audio download error: " + www.error);
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

    private IEnumerator FillSourceFromGPSAndNavigate()
    {
        if (!Input.location.isEnabledByUser)
        {
            Debug.LogWarning("Location services not enabled by user. Source will remain (0,0).");
            NavigationData.HasData = true;
            SceneManager.LoadScene(targetSceneName);
            yield break;
        }

        Input.location.Start(1f, 1f);
        int maxWait = 20;
        while (Input.location.status == LocationServiceStatus.Initializing && maxWait > 0)
        {
            yield return new WaitForSeconds(1f);
            maxWait--;
        }

        if (Input.location.status != LocationServiceStatus.Running)
        {
            Debug.LogWarning("Unable to fetch GPS in time; continuing without precise source.");
            NavigationData.HasData = true;
            SceneManager.LoadScene(targetSceneName);
            yield break;
        }

        var last = Input.location.lastData;
        NavigationData.Source = new GeoCoordinate(last.latitude, last.longitude);
        NavigationData.HasData = true;

        SceneManager.LoadScene(targetSceneName);
    }

    // ====== History roll (2) ======
    private string PushQuestion(string msg)
    {
        if (string.IsNullOrEmpty(questionFirst)) questionFirst = msg;
        else if (string.IsNullOrEmpty(questionSecond)) questionSecond = msg;
        else { questionFirst = questionSecond; questionSecond = msg; }
        return questionFirst + "~@~" + questionSecond;
    }
    private void PushAnswer(string ans)
    {
        if (string.IsNullOrEmpty(answerFirst)) answerFirst = ans;
        else if (string.IsNullOrEmpty(answerSecond)) answerSecond = ans;
        else { answerFirst = answerSecond; answerSecond = ans; }
    }

    // ====== Helpers ======
    private string GenerateUID() => Guid.NewGuid().ToString("N").Substring(0, 25);

    private string NormalizeLang(string lang)
    {
        switch (lang)
        {
            case "en-XA":
            case "en-US": return "en-US";
            case "hi-XA":
            case "hi-IN": return "hi-IN";
            case "bho-IN": return "bho-IN";
            case "bn-IN":  return "bn-IN";
            case "gu-IN":  return "gu-IN";
            default:       return "en-US";
        }
    }

    private string MapDropdownSelectionToLangCode(int index)
    {
        // 0: English (US) | 1: Hindi (IN) | 2: Bhojpuri (IN) | 3: Bengali (IN) | 4: Gujarati (IN)
        switch (index)
        {
            case 0: return "en-US";
            case 1: return "hi-IN";
            case 2: return "bho-IN";
            case 3: return "bn-IN";
            case 4: return "gu-IN";
            default: return "en-US";
        }
    }

    // ====== Data contracts ======
    [Serializable]
    public class ConversationRequest
    {
        public string content_txt;
        public string language_used;
        public string content_language;
        public string uid_id;
        public string front_website;
        public string question_1;
        public string question_2;
        public string answer_1;
        public string answer_2;
        public string front_flag;
        public string custom_flag;
        public string company_name;
        public string company_id;
        public string bot_id;
        public string bot_name;
        public string chatboat_id;
    }

    [Serializable]
    public class ServerResponse
    {
        // audio / flow
        public string for_front_flag; // "1" continues, "3" end
        public string play_mic;       // "1" listen next
        public string speak_txt;      // <-- AI transcript to show while speaking
        public string file_path;      // TTS mp3 url

        // optional nav payload from the AI (double)
        public double lat;            // e.g. 26.9124
        public double lng;            // e.g. 75.7873
        public string place_name;     // e.g. "Hawa Mahal"

        // others (kept for compatibility)
        public string front_website;
        public float  pop_up_timing;
        public string back_flag;
    }
}
