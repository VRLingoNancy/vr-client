using UnityEngine;
using System.Text;
using System.Threading.Tasks;
using UnityEngine.Networking;

public static class AuthApi
{
    public static async Task<string?> Login()
    {
        string url = $"{AuthState.httpUrl}/auth/login";
        Debug.Log($"AuthApi: POST {url}");

        var body = new LoginRequest
        {
            email = AuthState.EMAIL,
            password = AuthState.PASSWORD
        };

        string json = JsonUtility.ToJson(body);
        using var req = new UnityWebRequest(url, "POST");
        byte[] payload = Encoding.UTF8.GetBytes(json);
        req.uploadHandler = new UploadHandlerRaw(payload);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");

        var operation = req.SendWebRequest();
        while (!operation.isDone)
        {
            await Task.Yield();
        }

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError(
                $"AuthApi: login failed ({req.responseCode}) -> {req.error}\n{req.downloadHandler.text}"
            );
            return null;
        }

        string responseText = req.downloadHandler.text;
        Debug.Log($"AuthApi: login OK ({responseText.Length} chars)");

        LoginResponse res = JsonUtility.FromJson<LoginResponse>(responseText);

        return res.accessToken;
    }

    public static async Task<string?> CreateRealtimeTicket(string accessToken)
    {
        string url = $"{AuthState.httpUrl}/api/realtime/ws-ticket";
        Debug.Log($"AuthApi: POST {url}");

        using var req = new UnityWebRequest(url, "POST");
        req.uploadHandler = new UploadHandlerRaw(System.Array.Empty<byte>());
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Authorization", $"Bearer {accessToken}");

        var operation = req.SendWebRequest();
        while (!operation.isDone)
        {
            await Task.Yield();
        }

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError(
                $"AuthApi: ws-ticket failed ({req.responseCode}) -> {req.error}\n{req.downloadHandler.text}"
            );
            return null;
        }

        string responseText = req.downloadHandler.text;
        Debug.Log($"AuthApi: ws-ticket OK ({responseText.Length} chars)");

        RealtimeTicketResponse res = JsonUtility.FromJson<RealtimeTicketResponse>(responseText);

        return res.ticket;
    }

    [System.Serializable]
    public class LoginRequest
    {
        public string email;
        public string password;
    }

    [System.Serializable]
    class LoginResponse
    {
        public string accessToken;
    }

    [System.Serializable]
    class RealtimeTicketResponse
    {
        public string ticket;
    }
}
