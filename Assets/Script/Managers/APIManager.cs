using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Text;

public class APIManager : MonoBehaviour
{
    public static APIManager Instance;
    public string baseUrl = "https://shiprabackend.triosoft.ai/api/admin_link/";

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public IEnumerator PostRequest(string endpoint, string jsonData, System.Action<string> callback)
    {
        string url = baseUrl + endpoint;
        Debug.Log("POST URL: " + url);

        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonData);

        using (UnityWebRequest www = new UnityWebRequest(url, "POST"))
        {
            www.uploadHandler = new UploadHandlerRaw(bodyRaw);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");

            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
            {
                Debug.Log("Response: " + www.downloadHandler.text);
                callback?.Invoke(www.downloadHandler.text);
            }
            else
            {
                Debug.LogError("Error: " + www.error);
            }
        }
    }

public IEnumerator PostFormRequest(string endpoint, WWWForm form, System.Action<string> callback)
{
    string url =   endpoint;
    Debug.Log("POST Form URL: " + url);

    using (UnityWebRequest www = UnityWebRequest.Post(url, form))
    {
        yield return www.SendWebRequest();

        if (www.result == UnityWebRequest.Result.Success)
        {
            Debug.Log("POST Response: " + www.downloadHandler.text);
            callback?.Invoke(www.downloadHandler.text);
        }
        else
        {
            Debug.LogError("POST Error: " + www.error + " | Response code: " + www.responseCode);
        }
    }
}


    public IEnumerator GetRequest(string endpoint, System.Action<string> callback)
    {
        string url = endpoint;
        Debug.Log("GET URL: " + url);

        using (UnityWebRequest www = UnityWebRequest.Get(url))
        {
            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
            {
                Debug.Log("Response: " + www.downloadHandler.text);
                callback?.Invoke(www.downloadHandler.text);
            }
            else
            {
                Debug.LogError("Error: " + www.error);
            }
        }
    }
}
