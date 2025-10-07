using UnityEngine;
using System.Collections;
using System.Collections.Generic;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

public class AndroidSpeechRecognition : MonoBehaviour
{
    private AndroidJavaObject speechRecognizer;
    private AndroidJavaObject unityActivity;
    private AndroidJavaObject recognizerIntent;
    
    public delegate void OnSpeechResult(string text);
    public event OnSpeechResult onSpeechResult;
    
    private bool isListening = false;
    private string lastRecognizedText = "";
    private bool isInitialized = false;

    void Start()
    {
        StartCoroutine(InitializeWithDelay());
    }

    IEnumerator InitializeWithDelay()
    {
        yield return new WaitForSeconds(0.5f);
        RequestMicrophonePermission();
        yield return new WaitForSeconds(0.5f);
        InitializeSpeechRecognizer();
    }

    void RequestMicrophonePermission()
    {
#if UNITY_ANDROID
        if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            Debug.Log("Requesting microphone permission...");
            Permission.RequestUserPermission(Permission.Microphone);
        }
        else
        {
            Debug.Log("Microphone permission already granted");
        }
#endif
    }

    void InitializeSpeechRecognizer()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            Debug.Log("Starting Speech Recognizer initialization...");
            
            // Get Unity Activity
            AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            unityActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            
            if (unityActivity == null)
            {
                Debug.LogError("Failed to get Unity Activity!");
                return;
            }
            
            Debug.Log("Unity Activity obtained");

            // Check if speech recognition is available
            AndroidJavaClass speechRecognizerClass = new AndroidJavaClass("android.speech.SpeechRecognizer");
            bool isAvailable = speechRecognizerClass.CallStatic<bool>("isRecognitionAvailable", unityActivity);
            
            if (!isAvailable)
            {
                Debug.LogError("Speech Recognition is not available on this device!");
                return;
            }
            
            Debug.Log("Speech Recognition is available");

            // Create SpeechRecognizer on UI thread
            unityActivity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
            {
                try
                {
                    speechRecognizer = speechRecognizerClass.CallStatic<AndroidJavaObject>("createSpeechRecognizer", unityActivity);
                    
                    if (speechRecognizer != null)
                    {
                        // Set listener
                        speechRecognizer.Call("setRecognitionListener", new SpeechRecognitionListener(this));
                        isInitialized = true;
                        Debug.Log("Speech Recognizer initialized successfully!");
                    }
                    else
                    {
                        Debug.LogError("Failed to create SpeechRecognizer!");
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError("Error in UI thread initialization: " + e.Message);
                }
            }));
        }
        catch (System.Exception e)
        {
            Debug.LogError("Failed to initialize Speech Recognizer: " + e.Message);
            Debug.LogError("Stack trace: " + e.StackTrace);
        }
#else
        Debug.Log("Speech Recognition only works on Android device");
        isInitialized = true; // For testing
#endif
    }

    public void StartListening(string languageCode = "en-US")
    {
        if (!isInitialized)
        {
            Debug.LogWarning("Speech Recognizer not initialized yet! Trying to initialize...");
            InitializeSpeechRecognizer();
            return;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            Debug.LogWarning("Microphone permission not granted");
            RequestMicrophonePermission();
            return;
        }

        if (isListening)
        {
            Debug.Log("Already listening...");
            return;
        }

        if (speechRecognizer == null)
        {
            Debug.LogError("Speech recognizer is null! Reinitializing...");
            InitializeSpeechRecognizer();
            return;
        }

        try
        {
            Debug.Log("Creating recognition intent...");
            
            // Create Intent on UI thread
            unityActivity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
            {
                try
                {
                    AndroidJavaClass recognizerIntentClass = new AndroidJavaClass("android.speech.RecognizerIntent");
                    recognizerIntent = new AndroidJavaObject("android.content.Intent", 
                        recognizerIntentClass.GetStatic<string>("ACTION_RECOGNIZE_SPEECH"));

                    // Set parameters
                    recognizerIntent.Call<AndroidJavaObject>("putExtra", 
                        recognizerIntentClass.GetStatic<string>("EXTRA_LANGUAGE_MODEL"),
                        recognizerIntentClass.GetStatic<string>("LANGUAGE_MODEL_FREE_FORM"));
                    
                    recognizerIntent.Call<AndroidJavaObject>("putExtra", 
                        recognizerIntentClass.GetStatic<string>("EXTRA_LANGUAGE"), languageCode);
                    
                    recognizerIntent.Call<AndroidJavaObject>("putExtra", 
                        recognizerIntentClass.GetStatic<string>("EXTRA_MAX_RESULTS"), 5);
                    
                    recognizerIntent.Call<AndroidJavaObject>("putExtra",
                        recognizerIntentClass.GetStatic<string>("EXTRA_PARTIAL_RESULTS"), true);

                    // Start listening
                    if (speechRecognizer != null)
                    {
                        speechRecognizer.Call("startListening", recognizerIntent);
                        isListening = true;
                        Debug.Log("Started listening with language: " + languageCode);
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError("Error starting listener: " + e.Message);
                    isListening = false;
                }
            }));
        }
        catch (System.Exception e)
        {
            Debug.LogError("Error starting speech recognition: " + e.Message);
            Debug.LogError("Stack trace: " + e.StackTrace);
            isListening = false;
        }
#else
        Debug.Log("Testing mode - Speech Recognition only works on Android");
        // For testing in editor
        StartCoroutine(SimulateRecognition("Hello test"));
#endif
    }

    public void StopListening()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (speechRecognizer != null && isListening)
        {
            try
            {
                unityActivity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                {
                    speechRecognizer.Call("stopListening");
                    isListening = false;
                    Debug.Log("Stopped listening");
                }));
            }
            catch (System.Exception e)
            {
                Debug.LogError("Error stopping listener: " + e.Message);
            }
        }
#else
        isListening = false;
        Debug.Log("Stopped listening (test mode)");
#endif
    }

    public void CancelListening()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (speechRecognizer != null && isListening)
        {
            try
            {
                unityActivity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                {
                    speechRecognizer.Call("cancel");
                    isListening = false;
                    Debug.Log("Cancelled listening");
                }));
            }
            catch (System.Exception e)
            {
                Debug.LogError("Error cancelling listener: " + e.Message);
            }
        }
#endif
    }

    void OnSpeechRecognized(string text)
    {
        isListening = false;
        lastRecognizedText = text;
        
        Debug.Log("=== SPEECH RECOGNIZED ===");
        Debug.Log("Text: " + text);
        Debug.Log("========================");
        
        // Invoke on main thread
        UnityMainThreadDispatcher.Instance().Enqueue(() =>
        {
            onSpeechResult?.Invoke(text);
        });
    }

    void OnSpeechError(int errorCode)
    {
        isListening = false;
        string errorMessage = GetErrorMessage(errorCode);
        Debug.LogError("Speech Recognition Error: " + errorMessage + " (Code: " + errorCode + ")");
        
        // If error is not "No match" or "Speech timeout", try to reinitialize
        if (errorCode != 7 && errorCode != 6)
        {
            StartCoroutine(ReinitializeAfterError());
        }
    }

    IEnumerator ReinitializeAfterError()
    {
        yield return new WaitForSeconds(1f);
        Debug.Log("Attempting to reinitialize after error...");
        InitializeSpeechRecognizer();
    }

    string GetErrorMessage(int errorCode)
    {
        switch (errorCode)
        {
            case 1: return "Network timeout";
            case 2: return "Network error";
            case 3: return "Audio error";
            case 4: return "Server error";
            case 5: return "Client error";
            case 6: return "Speech timeout (no speech detected)";
            case 7: return "No match (couldn't understand)";
            case 8: return "Recognizer busy";
            case 9: return "Insufficient permissions";
            default: return "Unknown error";
        }
    }

    IEnumerator SimulateRecognition(string text)
    {
        yield return new WaitForSeconds(2f);
        OnSpeechRecognized(text);
    }

    public bool IsListening()
    {
        return isListening;
    }

    public string GetLastRecognizedText()
    {
        return lastRecognizedText;
    }

    public bool IsInitialized()
    {
        return isInitialized;
    }

    void OnDestroy()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (speechRecognizer != null)
        {
            try
            {
                speechRecognizer.Call("destroy");
                Debug.Log("Speech recognizer destroyed");
            }
            catch (System.Exception e)
            {
                Debug.LogError("Error destroying speech recognizer: " + e.Message);
            }
        }
#endif
    }

    void OnApplicationQuit()
    {
        OnDestroy();
    }

    // Inner class for handling callbacks
#if UNITY_ANDROID && !UNITY_EDITOR
    private class SpeechRecognitionListener : AndroidJavaProxy
    {
        private AndroidSpeechRecognition parent;

        public SpeechRecognitionListener(AndroidSpeechRecognition parent) 
            : base("android.speech.RecognitionListener")
        {
            this.parent = parent;
        }

        void onReadyForSpeech(AndroidJavaObject @params)
        {
            Debug.Log("Ready for speech - Start speaking now!");
        }

        void onBeginningOfSpeech()
        {
            Debug.Log("Speech detected - Listening...");
        }

        void onRmsChanged(float rmsdB)
        {
            // Audio level changed - can be used for visualization
        }

        void onBufferReceived(byte[] buffer)
        {
            // Audio buffer received
        }

        void onEndOfSpeech()
        {
            Debug.Log("End of speech detected - Processing...");
        }

        void onError(int error)
        {
            parent.OnSpeechError(error);
        }

        void onResults(AndroidJavaObject results)
        {
            try
            {
                AndroidJavaClass speechRecognizerClass = new AndroidJavaClass("android.speech.SpeechRecognizer");
                string key = speechRecognizerClass.GetStatic<string>("RESULTS_RECOGNITION");
                
                AndroidJavaObject resultsList = results.Call<AndroidJavaObject>("getStringArrayList", key);
                
                if (resultsList != null)
                {
                    int size = resultsList.Call<int>("size");
                    if (size > 0)
                    {
                        string text = resultsList.Call<string>("get", 0);
                        parent.OnSpeechRecognized(text);
                    }
                    else
                    {
                        Debug.LogWarning("No results in list");
                    }
                }
                else
                {
                    Debug.LogWarning("Results list is null");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError("Error processing results: " + e.Message);
            }
        }

        void onPartialResults(AndroidJavaObject partialResults)
        {
            try
            {
                AndroidJavaClass speechRecognizerClass = new AndroidJavaClass("android.speech.SpeechRecognizer");
                string key = speechRecognizerClass.GetStatic<string>("RESULTS_RECOGNITION");
                
                AndroidJavaObject resultsList = partialResults.Call<AndroidJavaObject>("getStringArrayList", key);
                
                if (resultsList != null)
                {
                    int size = resultsList.Call<int>("size");
                    if (size > 0)
                    {
                        string text = resultsList.Call<string>("get", 0);
                        Debug.Log("Partial result: " + text);
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError("Error processing partial results: " + e.Message);
            }
        }

        void onEvent(int eventType, AndroidJavaObject @params)
        {
            Debug.Log("Speech event: " + eventType);
        }
    }
#endif
}

// Helper class for running code on Unity main thread
public class UnityMainThreadDispatcher : MonoBehaviour
{
    private static UnityMainThreadDispatcher _instance = null;
    private readonly Queue<System.Action> _executionQueue = new Queue<System.Action>();

    public static UnityMainThreadDispatcher Instance()
    {
        if (_instance == null)
        {
            GameObject go = new GameObject("UnityMainThreadDispatcher");
            _instance = go.AddComponent<UnityMainThreadDispatcher>();
            DontDestroyOnLoad(go);
        }
        return _instance;
    }

    void Update()
    {
        lock (_executionQueue)
        {
            while (_executionQueue.Count > 0)
            {
                _executionQueue.Dequeue().Invoke();
            }
        }
    }

    public void Enqueue(System.Action action)
    {
        lock (_executionQueue)
        {
            _executionQueue.Enqueue(action);
        }
    }
}