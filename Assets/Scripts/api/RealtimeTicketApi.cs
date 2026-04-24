using UnityEngine;
using System.Net.Http;
using System.Threading.Tasks;

public static class RealtimeTicketApi
{
    [System.Serializable]
    class TicketResponse
    {
        public string ticket;
        public int expiresInSec;
    }

    public static async Task<string> Fetch()
    {
        HttpClient client = new HttpClient();

        if (string.IsNullOrEmpty(AuthState.AccessToken))
            AuthState.AccessToken = await AuthApi.Login();

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer",
                AuthState.AccessToken
            );

        var response = await client.PostAsync(
            $"{AuthState.httpUrl}/api/realtime/ws-ticket",
            null
        );

        if (!response.IsSuccessStatusCode)
        {
            Debug.LogError("ws-ticket failed: " + await response.Content.ReadAsStringAsync());
            return null;
        }

        string responseText = await response.Content.ReadAsStringAsync();
        var parsed = JsonUtility.FromJson<TicketResponse>(responseText);
        return parsed?.ticket;
    }
}
