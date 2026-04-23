using TMPro;
using UnityEngine;

public class ApiSettingsUI : MonoBehaviour
{
    public TMP_InputField ipInput;

    private TouchScreenKeyboard keyboard;

    void Awake()
    {
        AuthState.ip = PlayerPrefs.GetString("ip", AuthState.ip);
        ipInput.text = AuthState.ip;

        ipInput.onEndEdit.AddListener(UpdateIp);

        // 👇 THIS is where you hook it
        ipInput.onSelect.AddListener(OpenKeyboard);
    }

    void OpenKeyboard(string _)
    {
        keyboard = TouchScreenKeyboard.Open(ipInput.text, TouchScreenKeyboardType.Default);
    }

    void Update()
    {
        // Sync keyboard input → TMP field
        if (keyboard != null)
        {
            ipInput.text = keyboard.text;

            if (keyboard.status == TouchScreenKeyboard.Status.Done)
            {
                UpdateIp(keyboard.text);
                keyboard = null;
            }
        }
    }

    void UpdateIp(string newValue)
    {
        AuthState.ip = newValue;
        PlayerPrefs.SetString("ip", newValue);
    }
}
