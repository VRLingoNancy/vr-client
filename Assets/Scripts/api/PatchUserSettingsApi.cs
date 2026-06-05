using UnityEngine;
using UnityEngine.UI;
using System.Text;
using System.Net.Http;
using System.Threading.Tasks;
using System.Security.Cryptography;
using TMPro;
using System.Collections.Generic;

public class PatchUserSettingsApi: MonoBehaviour
{
    public TMP_Dropdown PreferredLangageDropdown;
    public TMP_Dropdown AiContextDropdown;
    public Button sceneTestButton;

    private List<string> languageKeys = new List<string>
    {
        "fr",
        "en",
        "it",
        "de"
    };

    private List<string> contexts = new List<string>
    {
        "medieval",
        "classic"
    };

    async void Start()
    {
        PreferredLangageDropdown.onValueChanged.AddListener(UpdatePrefeeredLangage);
        AiContextDropdown.onValueChanged.AddListener(UpdateAiContext);

        // Remplit le dropdown des thèmes depuis la liste (label == valeur) et
        // sélectionne le thème courant, pour que l'UI corresponde toujours aux
        // contextes acceptés par le backend.
        AiContextDropdown.ClearOptions();
        AiContextDropdown.AddOptions(contexts);
        int ctxIndex = contexts.IndexOf(AuthState.aiContext);
        if (ctxIndex >= 0)
            AiContextDropdown.SetValueWithoutNotify(ctxIndex);

        if (sceneTestButton != null)
            sceneTestButton.interactable = false;

        try
        {
            var settings = await getSettings();
            if (settings == null || string.IsNullOrEmpty(settings.preferredStudyLanguage))
                return;

            int index = languageKeys.IndexOf(settings.preferredStudyLanguage);
            if (index < 0) return;

            AuthState.learningLanguage = settings.preferredStudyLanguage;
            PreferredLangageDropdown.SetValueWithoutNotify(index);
        }
        finally
        {
            if (sceneTestButton != null)
                sceneTestButton.interactable = true;
        }
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
        AuthState.learningLanguage = value;
        var body = new SettingsPatchRequest
        {
            preferredStudyLanguage = value
        };

        await TweakSetting(body);
    }

    public async void UpdateAiContext(int index)
    {
        string value = contexts[index];
        AuthState.aiContext = value;
    }
}
