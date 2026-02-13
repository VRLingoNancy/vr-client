using UnityEngine;
using NativeWebSocket;

public class AIRealTime : MonoBehaviour
{
    WebSocket ws;

    async void Start()
    {
        ws = new WebSocket($"{AuthState.wsUrl}/api/realtime/session?lang=fr-FR");

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
