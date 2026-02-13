using UnityEngine;
using NativeWebSocket;
using System.Collections.Generic;
using System.Threading.Tasks;

public class AIRealTime : MonoBehaviour
{
    WebSocket ws;

    async void Start()
    {
        for (int i = 0; i < 30 && string.IsNullOrWhiteSpace(AuthState.AccessToken); i++)
        {
            await Task.Delay(100);
        }

        string url = $"{AuthState.wsUrl}/api/realtime/session?lang=fr-FR";
        if (!string.IsNullOrWhiteSpace(AuthState.AccessToken))
        {
            var headers = new Dictionary<string, string>
            {
                { "Authorization", $"Bearer {AuthState.AccessToken}" }
            };
            ws = new WebSocket(url, headers);
        }
        else
        {
            ws = new WebSocket(url);
        }

        ws.OnOpen += () =>
        {
            Debug.Log("WS Connected");
        };

        ws.OnMessage += (bytes) =>
        {
            Debug.Log("WS Message: " + System.Text.Encoding.UTF8.GetString(bytes));
        };

        await ws.Connect();
    }

    void Update()
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        ws?.DispatchMessageQueue();
#endif
    }

    async void OnApplicationQuit()
    {
        await ws.Close();
    }
}
