using UnityEngine;
using System.Net.Http;
using System.Threading.Tasks;

public static class ConversationScoreApi
{
    public static async Task<ScoreResponse> GetScore(string conversationId)
    {
        if (string.IsNullOrEmpty(conversationId)) return null;

        HttpClient client = new HttpClient();

        if (string.IsNullOrEmpty(AuthState.AccessToken))
            AuthState.AccessToken = await AuthApi.Login();

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer",
                AuthState.AccessToken
            );

        var response = await client.GetAsync(
            $"{AuthState.httpUrl}/api/conversations/{conversationId}/scoring"
        );

        if (!response.IsSuccessStatusCode)
        {
            Debug.LogError(await response.Content.ReadAsStringAsync());
            return null;
        }

        string responseText = await response.Content.ReadAsStringAsync();
        return JsonUtility.FromJson<ScoreResponse>(responseText);
    }
}
