using UnityEngine;
using System.Text;
using System.Net.Http;
using System.Threading.Tasks;

public static class AuthApi
{
    public static async Task<string?> Login()
    {
        HttpClient client = new HttpClient();
        string url = $"{AuthState.httpUrl}/auth/login";
        Debug.Log($"AuthApi: POST {url}");

        var body = new LoginRequest
        {
            email = AuthState.EMAIL,
            password = AuthState.PASSWORD
        };

        string json = JsonUtility.ToJson(body);

        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await client.PostAsync(url, content);

        if (!response.IsSuccessStatusCode)
        {
            Debug.LogError(
                $"AuthApi: login failed ({(int)response.StatusCode}) -> {await response.Content.ReadAsStringAsync()}"
            );
            return null;
        }

        string responseText = await response.Content.ReadAsStringAsync();
        Debug.Log($"AuthApi: login OK ({responseText.Length} chars)");

        LoginResponse res = JsonUtility.FromJson<LoginResponse>(responseText);

        return res.accessToken;
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
}
