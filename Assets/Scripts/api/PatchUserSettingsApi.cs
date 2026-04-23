using UnityEngine;
using System.Text;
using System.Net.Http;
using System.Threading.Tasks;
using System.Security.Cryptography;
using TMPro;
using System.Collections.Generic;

public class PatchUserSettingsApi: MonoBehaviour
{
    public TMP_Dropdown PreferredLangageDropdown;

    private List<string> languageKeys = new List<string>
    {
        "FR_fr",
        "EN_en"
    };

    void Start()
    {
        PreferredLangageDropdown.onValueChanged.AddListener(UpdatePrefeeredLangage);
    }

    public async Task<SettingsPatchResponse?> getSettings()
    {
        HttpClient client = new HttpClient();

        if (string.IsNullOrEmpty(AuthState.AccessToken))
        {
            AuthState.AccessToken = await AuthApi.Login();
        }

        client.DefaultRequestHeaders.Authorization = 
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer",
                AuthState.AccessToken
            );

        var response = await client.GetAsync($"{AuthState.httpUrl}/api/users/me/settings");

        if (!response.IsSuccessStatusCode)
        {
            Debug.LogError(await response.Content.ReadAsStringAsync());
            return null;
        }

        string responseText = await response.Content.ReadAsStringAsync();
        return JsonUtility.FromJson<SettingsPatchResponse>(responseText);
    }

    public async Task<SettingsPatchResponse?> TweakSetting(SettingsPatchRequest body)
    {
        HttpClient client = new HttpClient();

        if (string.IsNullOrEmpty(AuthState.AccessToken))
        {
            AuthState.AccessToken = await AuthApi.Login();
        }

        client.DefaultRequestHeaders.Authorization = 
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer",
                AuthState.AccessToken
            );

        string json = JsonUtility.ToJson(body);

        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await client.PatchAsync($"{AuthState.httpUrl}/api/users/me/settings", content);

        if (!response.IsSuccessStatusCode)
        {
            Debug.LogError(await response.Content.ReadAsStringAsync());
            return null;
        }

        string responseText = await response.Content.ReadAsStringAsync();

        return JsonUtility.FromJson<SettingsPatchResponse>(responseText);
    }

    public async void UpdatePrefeeredLangage(int index)
    {
        string value = languageKeys[index];
        var body = new SettingsPatchRequest
        {
            preferredStudyLanguage = value
        };

        await TweakSetting(body);
    }
}
