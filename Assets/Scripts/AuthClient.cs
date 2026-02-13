using UnityEngine;
using UnityEngine.Networking;
using System.Collections;

[System.Serializable]
public class LoginRequest
{
    public string email;
    public string password;
}

[System.Serializable]
public class LoginResponse
{
    public string accessToken;
}


public class AuthClient : MonoBehaviour
{

    void Start()
    {
        StartCoroutine(Login(
            token =>
            {
                AuthState.AccessToken = token;
                Debug.Log("JWT: " + token.Substring(0, 10) + "...");
            },
            error =>
            {
                Debug.LogError(error);
            }
        ));
    }

    public IEnumerator Login(System.Action<string> onSuccess, System.Action<string> onError)
    {
        var payload = new LoginRequest
        {
            email = AuthState.EMAIL,
            password = AuthState.PASSWORD
        };

        string json = JsonUtility.ToJson(payload);

        using (UnityWebRequest req = new UnityWebRequest($"{AuthState.httpUrl}/auth/login", "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Login failed: " + req.error);
                onError?.Invoke(req.error);
                yield break;
            }

            Debug.Log("Login success");

            var response = JsonUtility.FromJson<LoginResponse>(req.downloadHandler.text);

            if (string.IsNullOrEmpty(response.accessToken))
            {
                Debug.LogError("accessToken missing");
                onError?.Invoke("Token missing");
                yield break;
            }
            onSuccess?.Invoke(response.accessToken);
        }
    }
}
